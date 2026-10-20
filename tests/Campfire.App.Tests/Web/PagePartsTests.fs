// Ports of the #[cfg(test)] module in rust/crates/kit/src/deflater/splice.rs, less its gzip pieces (unit 2.8), moved with the code to the app in unit 2.13, with the
// `Generations` test
module Campfire.App.Tests.Web.PagePartsTests

open System
open System.Buffers
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open Xunit
open Campfire.App.Web

let private utf8 (s: string) = Encoding.UTF8.GetBytes s
let private hex (bytes: byte[]) = Convert.ToHexStringLower bytes

let private message (n: int) : Fragment =
    let body = String.replicate (40 + n % 7) "<button>Boost</button> hello there "
    Fragment("<div id=\"message_" + string n + "\" class=\"message\">" + body + "</div>\n")

let private same (a: obj | null) (b: obj | null) = Object.ReferenceEquals(a, b)

/// A page as a template records it (its text, and where each fragment went), and the body it stands for.
type private Page =
    { Plain: string
      Text: string
      Fragments: (int * Fragment) list }

    static member Empty = { Plain = ""; Text = ""; Fragments = [] }

    member this.AddText(text: string) = { this with Plain = this.Plain + text; Text = this.Text + text }

    member this.AddFragment(fragment: Fragment) =
        { this with
            Plain = this.Plain + Encoding.UTF8.GetString fragment.Bytes
            Fragments = this.Fragments @ [ this.Text.Length, fragment ] }

    member this.Parts() : PageParts =
        let fragments = this.Fragments |> List.map (fun (offset, f) -> struct (offset, f)) |> ResizeArray
        match PageParts.Splice(ReadOnlyMemory<byte>(utf8 this.Text), fragments) with
        | Ok parts -> parts
        | Error _ -> failwith "a fragment big enough for a part"

    member this.Etag() : string = this.Parts().Etag()

let private page (head: string) (messages: Fragment list) (tail: string) : Page =
    (messages |> List.fold (fun (page: Page) message -> (page.AddText "  ").AddFragment message) (Page.Empty.AddText head)).AddText tail

/// What `WritePlain` writes.
let private plain (parts: PageParts) : byte[] =
    let writer = ArrayBufferWriter<byte>()
    parts.WritePlain writer
    writer.WrittenSpan.ToArray()

[<Fact>]
let ``a fragment reports what it keeps alive for the fragment cache's budget`` () =
    let messages = [ for n in 0..9 -> message n ]
    let fresh = messages[5]
    Assert.Equal(0, fresh.HeldBytes)
    (page "<html><head>layout</head><body>" messages "</body></html>").Etag() |> ignore
    // Its SHA-256, not its own bytes.
    Assert.Equal(32 + 64, fresh.HeldBytes)
    Assert.True(fresh.HeldBytes < fresh.Length)

[<Fact>]
let ``the plain body is the page`` () =
    let messages = [ for n in 900..909 -> message n ]
    let small = Fragment "<i>small</i>"
    let page =
        (page "<p>" messages[0..4] "").AddFragment(small).AddText(String.replicate (SpliceLimits.MaxGlue + 1) "x")
        |> fun p -> p.AddFragment(messages[5]).AddFragment(messages[6]).AddText "</p>"
    let parts = page.Parts()
    Assert.Equal(page.Plain.Length, parts.BodyLength)
    Assert.Equal<byte[]>(utf8 page.Plain, plain parts)

/// The ETags these pages got when they were split by searching the body for its fragments, before the
/// fragments' offsets were recorded. (Rust's test also pins the SHA-256 of the gzip members flate2
/// made; .NET's deflate makes other bytes that decode to the same body, so those are checked by
/// decoding and by being the same each time.)
[<Fact>]
let ``the same etags as when split by searching`` () =
    let messages = [ for n in 0..29 -> message n ]
    let whole = page "<html><head>layout</head><body>" messages "</body></html>"
    Assert.Equal("79e220089f196fbabb4a04d3a545749e", whole.Etag())
    Assert.Equal<byte[]>(utf8 whole.Plain, plain (whole.Parts()))

    let small = Fragment "<i>small</i>"
    let mixed =
        Page.Empty
            .AddText("<p>")
            .AddFragment(messages[0])
            .AddText(String.replicate 300 "x")
            .AddFragment(messages[1])
            .AddText("  ")
            .AddFragment(small)
            .AddText("  ")
            .AddFragment(messages[2])
            .AddText("</p>")
    Assert.Equal("ca77a29a2de60cdd36f203de61f1f9cb", mixed.Etag())
    Assert.Equal<byte[]>(utf8 mixed.Plain, plain (mixed.Parts()))

[<Fact>]
let ``a changed layout or neighbour decodes correctly`` () =
    let messages = [ for n in 100..109 -> message n ]
    (page "<p>" messages "</p>").Etag() |> ignore
    for page in [ page "<p>changed" messages "</p>"; page "<p>" messages "</p>changed" ] do
        Assert.Equal<byte[]>(utf8 page.Plain, plain (page.Parts()))
    // Drop one message: the one after it now follows a different predecessor.
    let fewer = messages |> List.removeAt 4
    let page' = page "<p>" fewer "</p>"
    Assert.Equal<byte[]>(utf8 page'.Plain, plain (page'.Parts()))
    // Different glue between the same fragments.
    let glued = messages |> List.fold (fun (p: Page) m -> (p.AddText "\n    ").AddFragment m) (Page.Empty.AddText "<p>")
    Assert.Equal<byte[]>(utf8 glued.Plain, plain (glued.Parts()))

[<Fact>]
let ``small repeated and far apart fragments`` () =
    let messages = [ for n in 200..205 -> message n ]
    let small = Fragment "<i>small</i>"
    let withSmall = (page "<p>" messages[0..0] "  ").AddFragment small
    let withSmall = messages[1..2] |> List.fold (fun (p: Page) m -> (p.AddText "  ").AddFragment m) withSmall |> fun p -> p.AddText "</p>"
    Assert.Equal<byte[]>(utf8 withSmall.Plain, plain (withSmall.Parts()))
    // A small fragment is text to the parts.
    let asText = page "<p>" messages[0..0] ("  " + Encoding.UTF8.GetString small.Bytes)
    let asText = messages[1..2] |> List.fold (fun (p: Page) m -> (p.AddText "  ").AddFragment m) asText |> fun p -> p.AddText "</p>"
    Assert.Equal(withSmall.Etag(), asText.Etag())
    let onlySmall = Page.Empty.AddText("<p>").AddFragment(small).AddText "</p>"
    match PageParts.Splice(ReadOnlyMemory<byte>(utf8 onlySmall.Text), ResizeArray(onlySmall.Fragments |> List.map (fun (o, f) -> struct (o, f)))) with
    | Ok _ -> failwith "no part"
    | Error whole -> Assert.Equal<byte[]>(utf8 onlySmall.Plain, whole.ToArray())

    let a, b = message 300, message 301
    let page' =
        Page.Empty
            .AddFragment(a)
            .AddFragment(b)
            .AddFragment(a)
            .AddText(String.replicate (SpliceLimits.MaxGlue + 1) "x")
            .AddFragment(b)
            .AddFragment(a)
    for _ in 0..1 do
        Assert.Equal<byte[]>(utf8 page'.Plain, plain (page'.Parts()))

[<System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)>]
let private pageOfAFragmentThatGoesAway () : WeakReference<Fragment> =
    let fragment = message 970
    (page "<p>" [ fragment; message 971 ] "</p>").Etag() |> ignore
    WeakReference<Fragment>(fragment)

[<Fact>]
let ``what a fragment remembers goes with it`` () =
    let weak = pageOfAFragmentThatGoesAway ()
    for _ in 0..3 do
        GC.Collect()
        GC.WaitForPendingFinalizers()
    let mutable fragment: Fragment = null
    Assert.False(weak.TryGetTarget(&fragment), "nothing keeps a fragment's digest alive past it")

[<Fact>]
let ``etags follow the content`` () =
    let messages = [ for n in 400..409 -> message n ]
    let body = page "<p>" messages "</p>"
    Assert.Equal(body.Etag(), (page "<p>" messages "</p>").Etag())
    Assert.Equal(32, body.Etag().Length)
    Assert.NotEqual<string>(body.Etag(), (page "<p>" messages "</p>!").Etag())
    Assert.NotEqual<string>(body.Etag(), (page "<q>" messages "</p>").Etag())
    let glued = messages |> List.fold (fun (p: Page) m -> (p.AddText " ").AddFragment m) (Page.Empty.AddText "<p>") |> fun p -> p.AddText "</p>"
    Assert.NotEqual<string>(body.Etag(), glued.Etag())

[<Fact>]
let ``a large one-off text is served but not remembered`` () =
    let size = SpliceLimits.MaxStoredText + 1
    let text = Array.zeroCreate<byte> size
    let mutable state = 0x9e3779b97f4a7c15UL
    for i in 0 .. size - 1 do
        state <- state * 6364136223846793005UL + 1442695040888963407UL
        text[i] <- byte (state >>> 56)
    let fragment = message 990
    let parts =
        match PageParts.Splice(ReadOnlyMemory<byte> text, ResizeArray [ struct (text.Length, fragment) ]) with
        | Ok parts -> parts
        | Error _ -> failwith "a part"
    Assert.Equal<byte[]>(Array.append text fragment.Bytes, plain parts)
    let remembered = lock SpliceCaches.textShasGate (fun () -> SpliceCaches.textShas.Get({ Memory = ReadOnlyMemory<byte> text }, ignore))
    Assert.True(remembered.IsNone, "hashed, not remembered")

[<Fact>]
let ``a text part is its texts sha256 the first time and after`` () =
    let text = Array.concat (List.replicate 50 (utf8 "<html><head>layout</head><body>"))
    for text in [ text[0..9]; text[5..]; text ] do
        let expected = SHA256.HashData text
        for _ in 0..1 do
            let sha = SpliceCaches.textSha (ReadOnlyMemory<byte> text)
            Assert.Equal<byte[]>(
                expected,
                Array.concat [ BitConverter.GetBytes sha.A; BitConverter.GetBytes sha.B; BitConverter.GetBytes sha.C; BitConverter.GetBytes sha.D ]
            )

/// Hashes every key alike, as texts whose fast hashes collide.
[<Sealed>]
type private Colliding() =
    interface IEqualityComparer<TextKey> with
        member _.Equals(a, b) = a.Memory.Span.SequenceEqual b.Memory.Span
        member _.GetHashCode _ = 0

[<Fact>]
let ``a text is known by its bytes not its fast hash`` () =
    // A small budget, so texts also age out and come back through the old generation.
    let shas =
        Generations<TextKey, TextSha>(
            8 * 1024,
            (fun key _ -> key.Memory.Length + CacheCosts.EntryOverhead),
            Colliding(),
            (fun _ stored -> stored.Owned)
        )
    let gate = obj ()
    // The same length, all in one bucket.
    let texts = [ for n in 0..29 -> utf8 (String.replicate 40 $"<h1>Room {n:D2}</h1>") ]
    for _ in 0..2 do
        for text in texts do
            let sha = SpliceCaches.textShaIn gate shas (ReadOnlyMemory<byte> text)
            let hash = SHA256.HashData text
            Assert.Equal(BitConverter.ToUInt64(hash, 0), sha.A)
            let found = lock gate (fun () -> shas.Get({ Memory = ReadOnlyMemory<byte> text }, (fun stored -> stored.Sha)))
            Assert.Equal(ValueSome sha, found) // remembered as its own

[<Fact>]
let ``texts that hash alike are told apart`` () =
    // The fast hash covers every byte, whatever the length: texts of every length around the lane and
    // word steps, differing in their last byte, hash differently (a collision here would be bad luck,
    // not a bug, but none of these should collide).
    let hashes = System.Collections.Generic.HashSet<int>()
    for length in [ 0; 1; 7; 8; 9; 31; 32; 33; 40; 100; 1000; 40000 ] do
        let text = Array.init length (fun i -> byte (i * 7))
        Assert.True(hashes.Add(TextKey.Hash(ReadOnlySpan<byte> text)))
        if length > 0 then
            let changed = Array.copy text
            changed[length - 1] <- changed[length - 1] + 1uy
            Assert.NotEqual(TextKey.Hash(ReadOnlySpan<byte> text), TextKey.Hash(ReadOnlySpan<byte> changed))

[<Fact>]
let ``texts that share a recent slot each get their own sha256`` () =
    // The same length and the same first and last bytes, so the same slot: only a compare of the whole
    // text tells them apart, as it must (a stored piece is found by the SHA-256).
    let make (middle: byte) = Array.concat [ "AAAAAAAA"B; Array.create 100 middle; "BBBBBBBB"B ]
    let one, two = make 1uy, make 2uy
    let sha (bytes: byte[]) =
        let hash = SHA256.HashData bytes
        BitConverter.ToUInt64(hash, 0)
    for _ in 1..4 do
        Assert.Equal(sha one, (SpliceCaches.textSha (ReadOnlyMemory<byte> one)).A)
        Assert.Equal(sha two, (SpliceCaches.textSha (ReadOnlyMemory<byte> two)).A)

[<Fact>]
let ``generations stay within their budget and keep what is read`` () =
    let budget = 64 * 1024
    let size = 1024
    let map = Generations<byte, int>(budget, (fun _ size -> size))
    for n in 0..255 do
        map.Insert(byte n, size)
        Assert.True((map.Get(0uy, ignore)).IsSome, "what's read stays")
        let held = map.Count * size
        // Each generation may overshoot half the budget by the entry that filled it.
        Assert.True(held <= budget + 2 * size, $"{held} bytes held")
    Assert.True((map.Get(255uy, ignore)).IsSome, "the latest is kept")
    Assert.True((map.Get(1uy, ignore)).IsNone, "what isn't read ages out")

    let map = Generations<byte, int>(budget, (fun _ size -> size))
    map.Insert(1uy, size)
    map.Insert(1uy, size)
    Assert.Equal(size, map.YoungCost)
