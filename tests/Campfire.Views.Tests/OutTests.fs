// Tests of the rendering core (Core/Out.fs): there is no Rust counterpart, askama's writer being a
// `fmt::Write` on a `String`. These pin what the templates rely on: static text is copied as bytes,
// values are ERB-escaped, safe text isn't escaped twice, and a page records its big fragments.
module Campfire.Views.Tests.OutTests

open System
open System.Text
open Xunit
open Campfire.Views

let private lit = Utf8.lit "<p class=\"é\">"

[<Fact>]
let ``static text is copied as its utf-8 bytes`` () =
    let rendered = Render.plain (fun w -> w.Lit lit)
    Assert.Equal<byte[]>(Encoding.UTF8.GetBytes "<p class=\"é\">", rendered)

[<Fact>]
let ``text is erb escaped and raw text is not`` () =
    Assert.Equal("&lt;b&gt; &amp; &quot;q&quot; &#39;s&#39; é 😀", Render.text (fun w -> w.Text "<b> & \"q\" 's' é 😀"))
    Assert.Equal("<b> & \"q\" é 😀", Render.text (fun w -> w.Raw "<b> & \"q\" é 😀"))
    Assert.Equal("", Render.text (fun w -> w.Text(null: string | null)))

[<Fact>]
let ``safe html is written as it is, never escaped a second time`` () =
    let safe = SafeBytes.Text "a < b"
    Assert.Equal("a &lt; b", safe.ToString())
    Assert.Equal("[a &lt; b]", Render.text (fun w ->
        w.Byte(byte '[')
        w.Raw safe
        w.Byte(byte ']')))
    Assert.Equal("&amp;lt;", Render.text (fun w -> w.Text "&lt;"))

[<Fact>]
let ``integers are written in decimal`` () =
    Assert.Equal("0 -7 9223372036854775807 -9223372036854775808", Render.text (fun w ->
        w.Int 0L
        w.Byte(byte ' ')
        w.Int -7L
        w.Byte(byte ' ')
        w.Int Int64.MaxValue
        w.Byte(byte ' ')
        w.Int Int64.MinValue))

[<Fact>]
let ``a writer grows past its first buffer and keeps what it holds`` () =
    let big = String('é', 20000)
    let text = Render.text (fun w ->
        w.Raw "start"
        w.Text big
        w.Raw(String('x', 100000))
        w.Raw "end")
    Assert.Equal("start" + big + String('x', 100000) + "end", text)

[<Fact>]
let ``a writer comes back from the pool empty`` () =
    for _ in 1..3 do
        let out = Out.Rent 0
        Assert.Equal(0, out.Length)
        out.Raw "dirty"
        Out.Return out
    // A render nested in another takes a writer of its own.
    let nested =
        Render.text (fun w ->
            w.Raw "outer["
            w.Raw(Render.text (fun inner -> inner.Raw "inner"))
            w.Raw "]")
    Assert.Equal("outer[inner]", nested)

[<Fact>]
let ``a writer is returned when the template throws`` () =
    Assert.Throws<InvalidOperationException>(fun () -> Render.plain (fun w -> w.Raw "x"; raise (InvalidOperationException())) |> ignore) |> ignore
    Assert.Equal("fine", Render.text (fun w -> w.Raw "fine"))

[<Fact>]
let ``fragments of a kilobyte or more are recorded and shorter ones are copied`` () =
    let big = CachedFragment(String('b', 1024))
    let small = CachedFragment(String('s', 1023))
    let page =
        Render.page 0 (fun w ->
            w.Raw "<ul>"
            w.Fragment big
            w.Raw "|"
            w.Fragment small
            w.Raw "</ul>")
    Assert.Equal<string[]>([| "<ul>"; "|" + String('s', 1023) + "</ul>" |], page.Parts |> Array.map Encoding.UTF8.GetString)
    Assert.Equal(1, page.Fragments.Length)
    Assert.Same(big, page.Fragments[0])
    Assert.Equal("<ul>" + String('b', 1024) + "|" + String('s', 1023) + "</ul>", page.ToString())
    Assert.Equal(page.Length, page.ToArray().Length)

[<Fact>]
let ``outside a page nothing is recorded`` () =
    let big = CachedFragment(String('b', 5000))
    Assert.Equal(String('b', 5000), Render.text (fun w -> w.Fragment big))
    Assert.Equal(String('b', 5000), (Render.page 0 (fun w -> w.Fragment big)).ToString())
    Assert.Empty (Render.page 0 (fun w -> w.Raw "no fragments")).Fragments

[<Fact>]
let ``a page written into another keeps its fragments`` () =
    let big = CachedFragment(String('b', 2000))
    let inner = Render.page 0 (fun w -> w.Raw "<a>"; w.Fragment big; w.Raw "</a>")
    let outer = Render.page 0 (fun w -> w.Raw "<frame>"; w.Page inner; w.Raw "</frame>")
    Assert.Equal("<frame><a>" + String('b', 2000) + "</a></frame>", outer.ToString())
    Assert.Equal(1, outer.Fragments.Length)
    Assert.Same(big, outer.Fragments[0])
    Assert.Equal("<frame><a>", Encoding.UTF8.GetString outer.Parts[0])

[<Fact>]
let ``escaping a lone surrogate writes the replacement character`` () =
    // .NET strings can hold what Rust's can't; the writer must not throw on it.
    Assert.Equal("a�b", Render.text (fun w -> w.Text "a\uD800b"))

[<Fact>]
let ``a page renders into a pooled buffer and allocates little more than itself`` () =
    // The layout page of the reference's facts: ~21 KB of HTML written by ~60 template and helper calls.
    let name = "welcome"
    let ctx = Facts.context name Facts.Request.none
    let render () =
        Render.page 0 (fun w ->
            Layouts.Application.render
                w
                ctx
                { Layouts.Application.create (Render.html (fun w -> w.Raw "<div id=\"message-area\" class=\"message-area\"></div>")) with
                    PageTitle = Some "No rooms yet"
                    BodyClass = Some "sidebar" })
    let length = (render ()).Length
    for _ in 1..200 do
        render () |> ignore
    let before = GC.GetAllocatedBytesForCurrentThread()
    let rounds = 500
    for _ in 1..rounds do
        render () |> ignore
    let perRender = (GC.GetAllocatedBytesForCurrentThread() - before) / int64 rounds
    printfn "welcome page: %d bytes rendered, %d bytes allocated per render" length perRender
    // The exact-size copy of the page is the one big allocation; the rest is the helpers' attribute
    // lists and a few small strings.
    Assert.True(perRender < int64 length + 12000L, $"{perRender} bytes allocated for a {length} byte page")

// sized.rs (still the writer's; `RenderSize` goes with it in unit 4.10)

[<Fact>]
let ``RenderSize reserves the last length with headroom up to a cap`` () =
    let size = RenderSize()
    let page (length: int) (w: Out) = w.Raw($"<p>{String('x', length)}</p>")
    size.Render(page 8000) |> ignore
    Assert.Equal(8007 + 8007 / 8, size.Capacity 0)
    // A shorter page sizes the next render: one long page doesn't stay reserved.
    size.Render(page 7000) |> ignore
    Assert.Equal(7007 + 7007 / 8, size.Capacity 0)
    size.Render(page (2 * RenderSize.Max)) |> ignore
    Assert.Equal(RenderSize.Max, size.Capacity 0)
    // The size hint is a floor.
    Assert.Equal(5000, RenderSize().Capacity 5000)

// layouts.rs: `frame`

[<Fact>]
let ``the turbo frame layout around a page keeps the pages fragments`` () =
    let items = [ CachedFragment(String('a', 1500)); CachedFragment "<li>small</li>"; CachedFragment(String('b', 1500)) ]
    let list (w: Out) =
        w.Raw "<ul>"
        for item in items do
            w.Raw "\n  "
            w.Fragment item
        w.Raw "\n</ul>"
    let head (w: Out) = w.Raw "<title>t</title>"
    let framed = Layouts.frame (RenderSize()) head list
    let plain = Render.text (fun w -> Templates.Layouts.TurboRails.Frame.render w head list)
    Assert.Equal(plain, framed.ToString())
    Assert.Equal(2, framed.Fragments.Length)
    Assert.StartsWith("<html>\n  <head>\n    <title>t</title>\n  </head>\n  <body>\n    <ul>", framed.ToString())
