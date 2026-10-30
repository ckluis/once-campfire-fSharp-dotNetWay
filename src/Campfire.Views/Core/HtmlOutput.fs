// plans/design-views.md V6: a page with cached fragments is written in parts, never joined into one string.
namespace Campfire.Views

open System
open System.Buffers
open System.Collections.Generic
open System.Security.Cryptography
open System.Text
open Falco.Markup

/// A rendered page or partial: UTF-8 text parts with a cached fragment between each two, in document order
/// (`Parts.Length = Fragments.Length + 1`). The response writer copies part, fragment, part into the body, so a room page's
/// 400 KB of messages are never copied into one buffer, nor into one string.
[<Sealed; AllowNullLiteral>]
type HtmlOutput(parts: byte[][], fragments: CachedFragment[]) =
    do
        if parts.Length <> fragments.Length + 1 then
            invalidArg "parts" "a page in parts has one more text part than fragments"

    [<VolatileField>]
    let mutable digest: byte[] | null = null

    member _.Parts: byte[][] = parts
    member _.Fragments: CachedFragment[] = fragments

    /// The text without its fragments, in bytes.
    member _.TextLength: int =
        let mutable length = 0
        for part in parts do
            length <- length + part.Length
        length

    /// The page's length in bytes: its text and its fragments.
    member this.Length: int =
        let mutable length = this.TextLength
        for fragment in fragments do
            length <- length + fragment.Length
        length

    /// SHA-256 over the text parts (each with its length) and the fragments' own digests, in order: what identifies the page for
    /// its kept compressed form (V7). Hashing 16 KB of text instead of the 400 KB page.
    member _.Digest() : byte[] =
        match digest with
        | null ->
            use hash = IncrementalHash.CreateHash HashAlgorithmName.SHA256
            let length = Array.zeroCreate<byte> 8
            for i in 0 .. parts.Length - 1 do
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(Span<byte> length, int64 parts[i].Length)
                hash.AppendData(ReadOnlySpan<byte> length)
                hash.AppendData parts[i]
                if i < fragments.Length then hash.AppendData fragments[i].Digest
            let computed = hash.GetHashAndReset()
            digest <- computed
            computed
        | computed -> computed

    /// The body, part after part.
    member _.WriteTo(writer: IBufferWriter<byte>) : unit =
        for i in 0 .. parts.Length - 1 do
            writer.Write(ReadOnlySpan<byte> parts[i])
            if i < fragments.Length then writer.Write(ReadOnlySpan<byte> fragments[i].Bytes)

    /// The whole page in one array (a fragment made of a page; tests).
    member this.ToArray() : byte[] =
        let all = GC.AllocateUninitializedArray<byte> this.Length
        let mutable at = 0
        for i in 0 .. parts.Length - 1 do
            Buffer.BlockCopy(parts[i], 0, all, at, parts[i].Length)
            at <- at + parts[i].Length
            if i < fragments.Length then
                Buffer.BlockCopy(fragments[i].Bytes, 0, all, at, fragments[i].Length)
                at <- at + fragments[i].Length
        all

    /// The page as text (Cable payloads, V9).
    override this.ToString() = Encoding.UTF8.GetString(this.ToArray())

module HtmlOutput =
    [<Literal>]
    let private Marker = ''

    let private utf8 (chars: ReadOnlySpan<char>) : byte[] =
        let bytes = Array.zeroCreate<byte> (Encoding.UTF8.GetByteCount chars)
        Encoding.UTF8.GetBytes(chars, Span<byte> bytes) |> ignore
        bytes

    /// A body with no fragment.
    let text (html: string) : HtmlOutput = HtmlOutput([| Encoding.UTF8.GetBytes html |], Array.empty)

    /// A body of bytes already rendered.
    let ofBytes (bytes: byte[]) : HtmlOutput = HtmlOutput([| bytes |], Array.empty)

    /// Splits Falco's string at the slots `Html.fragment` wrote and finds each fragment among `lookup`. A U+E000 that does not
    /// follow a `<` and a number and `>` is text. Raises if a slot names a fragment that was not passed or is placed twice.
    let internal split (rendered: string) (lookup: Dictionary<int64, CachedFragment>) (placed: HashSet<int64>) : HtmlOutput =
        let parts = List<byte[]>()
        let found = List<CachedFragment>()
        let mutable start = 0
        let mutable at = rendered.IndexOf Marker
        while at >= 0 do
            let mutable slotEnd = -1
            let mutable id = 0L
            if at > 0 && rendered[at - 1] = '<' then
                let mutable j = at + 1
                while j < rendered.Length && rendered[j] >= '0' && rendered[j] <= '9' do
                    j <- j + 1
                if j > at + 1 && j < rendered.Length && rendered[j] = '>' && Int64.TryParse(rendered.AsSpan(at + 1, j - at - 1), &id) then
                    slotEnd <- j
            if slotEnd >= 0 then
                match lookup.TryGetValue id with
                | true, fragment ->
                    if not (placed.Add id) then invalidOp $"fragment {id} is placed twice"
                    parts.Add(utf8 (rendered.AsSpan(start, at - 1 - start)))
                    found.Add fragment
                    start <- slotEnd + 1
                | _ -> invalidOp $"fragment {id} is placed but was not passed"
                at <- rendered.IndexOf(Marker, slotEnd + 1)
            else
                at <- rendered.IndexOf(Marker, at + 1)
        parts.Add(utf8 (rendered.AsSpan start))
        HtmlOutput(parts.ToArray(), found.ToArray())

    let internal lookupOf (fragments: CachedFragment seq) : Dictionary<int64, CachedFragment> =
        let lookup = Dictionary<int64, CachedFragment>()
        for fragment in fragments do
            lookup[fragment.Id] <- fragment
        lookup

    /// Raises unless every fragment passed was placed.
    let internal checkAllPlaced (lookup: Dictionary<int64, CachedFragment>) (placed: HashSet<int64>) : unit =
        if placed.Count <> lookup.Count then
            invalidOp $"{lookup.Count - placed.Count} of {lookup.Count} fragments were passed but never placed"

    let private render (renderString: unit -> string) (fragments: CachedFragment seq) : HtmlOutput =
        let lookup = lookupOf fragments
        let placed = HashSet<int64>()
        let output = split (renderString ()) lookup placed
        checkAllPlaced lookup placed
        output

    /// `renderNode`: one root, no doctype. Raises if a slot's fragment was not passed, or a passed one was placed twice or never.
    let ofNode (node: XmlNode) (fragments: CachedFragment seq) : HtmlOutput = render (fun () -> renderNode node) fragments

    /// `renderHtml`: a document, with its `<!DOCTYPE html>`.
    let ofDocument (node: XmlNode) (fragments: CachedFragment seq) : HtmlOutput = render (fun () -> renderHtml node) fragments

    /// Several roots (turbo streams, multi-root partials).
    let ofNodes (nodes: XmlNode list) (fragments: CachedFragment seq) : HtmlOutput =
        render (fun () -> String.Concat(nodes |> List.map renderNode)) fragments

    /// A rendered partial as a cached fragment: its text and any fragment nested in it (a message's boosts), flattened into one
    /// array.
    let toFragment (output: HtmlOutput) : CachedFragment = CachedFragment(output.ToArray())
