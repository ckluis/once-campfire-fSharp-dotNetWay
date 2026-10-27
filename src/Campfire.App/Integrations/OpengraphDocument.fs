// Port of rust/crates/campfire/src/integrations/opengraph/document.rs
//
// `Opengraph::Document` (reference/app/models/opengraph/document.rb). The `meta` reader is AngleSharp (plans/design-jobs.md J5), not
// libxml2's HTML parser: the two differ on malformed pages (README, class B), and two caps stop a hostile page.
namespace Campfire.App.Integrations

open System
open System.Collections.Generic
open System.Text
open System.Text.Unicode
open AngleSharp
open AngleSharp.Dom
open AngleSharp.Html.Parser

type MetaElement =
    { Attributes: (string * string) list }

    member this.Attr(name: string) : string option =
        this.Attributes |> List.tryFind (fun (n, _) -> n = name) |> Option.map snd

    member this.HasAttr(name: string) : bool = this.Attributes |> List.exists (fun (n, _) -> n = name)

module OpengraphDocument =
    /// `Opengraph::Metadata::ATTRIBUTES`, in the order `Hash#slice` returns them.
    let Attributes: string list = [ "title"; "url"; "image"; "description" ]

    /// libxml2 reading a UTF-8 buffer: valid sequences decode, any other byte is taken as Latin-1.
    let decode (bytes: byte[]) : string =
        if Utf8.IsValid bytes then
            Encoding.UTF8.GetString bytes
        else
            let out = StringBuilder(bytes.Length)
            let mutable i = 0
            while i < bytes.Length do
                let mutable rune = Unchecked.defaultof<Rune>
                let mutable consumed = 0
                match Rune.DecodeFromUtf8(ReadOnlySpan(bytes, i, bytes.Length - i), &rune, &consumed) with
                | Buffers.OperationStatus.Done ->
                    out.Append(rune.ToString()) |> ignore
                    i <- i + consumed
                | _ ->
                    out.Append(char bytes[i]) |> ignore
                    i <- i + 1
            out.ToString()

    /// `String#blank?`: empty or only (Unicode) whitespace. (`Char.IsWhiteSpace` also counts U+001C to U+001F,
    /// which `char::is_whitespace` and `[[:space:]]` don't.)
    let isBlank (s: string) : bool =
        let whiteSpace (c: char) =
            (c >= '\t' && c <= '\r')
            || c = ' '
            || c = '\u0085'
            || c = ' '
            || c = ' '
            || (c >= ' ' && c <= ' ')
            || c = ' '
            || c = ' '
            || c = ' '
            || c = ' '
            || c = '　'
        s |> Seq.forall whiteSpace

    let private lowerAscii (c: char) : char = if c >= 'A' && c <= 'Z' then char (int c + 32) else c

    /// `eq_ignore_ascii_case`: only A-Z fold (`OrdinalIgnoreCase` also folds a few non-ASCII letters).
    let private equalsIgnoreAscii (a: string) (b: string) : bool =
        a.Length = b.Length && Seq.forall2 (fun x y -> lowerAscii x = lowerAscii y) a b

    /// libxml2's default maximum depth, where its HTML parser halts.
    [<Literal>]
    let MaxDepth = 256

    /// Elements a page may create; ours (libxml2 has no such cap).
    [<Literal>]
    let MaxElements = 100000

    let private voidElements =
        HashSet<string>([ "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input"; "link"; "meta"; "param"; "source"; "track"; "wbr" ], StringComparer.Ordinal)

    /// Raised from the parser's callback to stop it; the tree built so far is kept.
    exception private Stopped

    /// An element's depth, counted only up to `MaxDepth + 1` (the open ancestors, itself included).
    let private overDepth (element: IElement) : bool =
        let mutable depth = 0
        let mutable node: INode | null = element
        while depth <= MaxDepth && not (isNull node) do
            match node with
            | :? IElement as current ->
                depth <- depth + 1
                node <- current.Parent
            | _ -> node <- null
        depth > MaxDepth

    let private metasOf (document: IDocument) : MetaElement list =
        [ for meta in document.QuerySelectorAll "meta" ->
              { Attributes = [ for attribute in meta.Attributes -> attribute.Name, attribute.Value ] } ]

    /// What reading a page found: its metas, whether a cap stopped the parse, and how many elements had been created.
    type Reading =
        { Metas: MetaElement list
          Stopped: bool
          Elements: int }

    /// The `<meta>` elements of the document, in document order. A page nested deeper than `MaxDepth`, or with more than
    /// `MaxElements` elements, stops the parse; the metas created before the stop are kept (libxml2's `xmlHaltParser` keeps
    /// the tree so far), so a page's head still unfurls.
    let read (html: string) : Reading =
        let document: (IDocument | null) ref = ref null
        let previous: (IElement | null) ref = ref null
        let elements = ref 0
        let checkPrevious () =
            match previous.Value with
            | null -> ()
            | element ->
                previous.Value <- null
                // A void element is never open.
                if not (voidElements.Contains element.LocalName) && overDepth element then raise Stopped
        let onCreated (element: IElement) (_: Text.TextPosition) =
            if isNull document.Value then document.Value <- element.Owner
            checkPrevious ()
            if elements.Value >= MaxElements then raise Stopped
            elements.Value <- elements.Value + 1
            previous.Value <- element
        let mutable options = HtmlParserOptions(IsScripting = false)
        options.OnCreated <- Action<IElement, Text.TextPosition>(onCreated)
        let parser = HtmlParser(options, BrowsingContext.New(Configuration.Default))
        let kept (stopped: bool) =
            let metas = match document.Value with null -> [] | document -> metasOf document
            { Metas = metas; Stopped = stopped; Elements = elements.Value }
        try
            let parsed = parser.ParseDocument html
            checkPrevious ()
            { Metas = metasOf parsed; Stopped = false; Elements = elements.Value }
        with
        | Stopped -> kept true
        // AngleSharp 1.8.4 has a NullReferenceException in its select mode (see Html5): what was built so far counts.
        | _ -> kept false

    let metaElements (html: string) : MetaElement list = (read html).Metas

    /// `content[/charset\s*=\s*([\w-]+)/i, 1]`
    let private charsetIn (content: string) : string option =
        let isSpace (c: char) = c = ' ' || c = '\t' || c = '\n' || c = '\011' || c = '\012' || c = '\r'
        let isWord (c: char) = Char.IsAsciiLetterOrDigit c || c = '_' || c = '-'
        let mutable result: string option = None
        let mutable at = 0
        while result.IsNone && at < content.Length do
            if not (at + 7 <= content.Length && equalsIgnoreAscii (content.Substring(at, 7)) "charset") then
                at <- at + 1
            else
                let mutable i = at + 7
                while i < content.Length && isSpace content[i] do
                    i <- i + 1
                if i < content.Length && content[i] = '=' then
                    i <- i + 1
                    while i < content.Length && isSpace content[i] do
                        i <- i + 1
                    let start = i
                    while i < content.Length && isWord content[i] do
                        i <- i + 1
                    if i > start then result <- Some(content.Substring(start, i - start))
                if result.IsNone then at <- at + 1
        result

    /// `Nokogiri::HTML4::Document#meta_encoding`: the first `meta[@charset]`, else the charset in the first
    /// `http-equiv="Content-Type"` meta with a `content`.
    let metaEncoding (metas: MetaElement list) : string option =
        match metas |> List.tryFind (fun m -> m.HasAttr "charset") with
        | Some meta -> meta.Attr "charset"
        | None ->
            metas
            |> List.tryFind (fun m ->
                m.HasAttr "content"
                && (match m.Attr "http-equiv" with
                    | Some v -> equalsIgnoreAscii v "content-type"
                    | None -> false))
            |> Option.bind (fun meta -> meta.Attr "content" |> Option.bind charsetIn)

    /// `//*/meta[starts-with(@property, "og:") or starts-with(@name, "og:")]`
    let private isOpengraphTag (meta: MetaElement) : bool =
        let startsWithOg (value: string option) =
            match value with
            | Some value -> value.StartsWith("og:", StringComparison.Ordinal)
            | None -> false
        startsWithOg (meta.Attr "property") || startsWithOg (meta.Attr "name")

    /// `opengraph_attributes`: from each `meta` whose `property` or `name` starts with "og:", the key is that
    /// attribute (`property` when present) with every "og:" removed, and the value its non-blank `content`. Later
    /// tags win. Without a meta charset, non-ASCII characters are dropped
    /// (`content.encode("UTF-8", "binary", invalid: :replace, undef: :replace, replace: "")`).
    let opengraphAttributes (body: byte[] option) : (string * string) list =
        let html = decode (defaultArg body [||])
        let metas = metaElements html
        let metaEncoding = metaEncoding metas

        // Only the `ATTRIBUTES` keys are sliced out, so only they are kept.
        let found = Collections.Generic.Dictionary<string, string>()
        for meta in metas |> List.filter isOpengraphTag do
            let key = if meta.HasAttr "property" then "property" else "name"
            let name = (defaultArg (meta.Attr key) "").Replace("og:", "")
            if List.contains name Attributes then
                match meta.Attr "content" |> Option.filter (fun c -> not (isBlank c)) with
                | Some content ->
                    let content = if metaEncoding.IsSome then content else String(content.ToCharArray() |> Array.filter (fun c -> c < '\u0080'))
                    found[name] <- content
                | None -> ()
        [ for key in Attributes do
              match found.TryGetValue key with
              | true, value -> key, value
              | _ -> () ]
