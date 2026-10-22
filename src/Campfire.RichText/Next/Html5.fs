// plans/design-richtext.md R2, R3 and R5 (Phase 5, unit 5.1): rich text's parser, limits, serializer and DOM helpers on AngleSharp.
//
// One HtmlParser over one shared BrowsingContext. A body (Nokogiri's `Document#fragment`) is parsed as a document after "<body>"
// and the body element is the container, used in place; markup built by the pipeline for an element (`inner_html=`, `replace`)
// is parsed in that element's context. Gumbo's two limits and three caps of ours are enforced while parsing, through the
// parser's callbacks; serialization is Nokogiri's HTML5 rules.
namespace Campfire.RichText.Next

open System
open System.Collections.Generic
open System.Text
open AngleSharp
open AngleSharp.Dom
open AngleSharp.Html.Dom
open AngleSharp.Html.Parser
open AngleSharp.Html.Parser.Tokens.Struct
open Campfire.RichText

/// Raised by a parser callback to stop the parse; `Html5` turns it into the `ParseError`.
exception internal LimitExceeded of ParseError

/// What one parse has counted. The parser is shared by every thread, so the callbacks reach this through a thread-static, set up by
/// the parse that runs on that thread (a parse never nests another).
[<Sealed; AllowNullLiteral>]
type internal ParseState() =
    [<ThreadStatic; DefaultValue>]
    static val mutable private instance: ParseState

    static member Current: ParseState =
        match ParseState.instance with
        | null ->
            let created = ParseState()
            ParseState.instance <- created
            created
        | existing -> existing

    /// Elements above the container in a node's ancestors (html and body for a document parse, the context's copy for a fragment).
    member val Baseline = 0 with get, set
    member val Elements = 0 with get, set
    member val Tokens = 0 with get, set
    member val RootAttributes = 0 with get, set
    /// The tag (by its position) whose distinct attribute names are being collected, from its 400th attribute on.
    member val AttributePosition = -1 with get, set
    member val AttributeNames: HashSet<string> | null = null with get, set
    /// The element created last: its depth is checked at the next token (it is in the tree by then), and at the end.
    member val Previous: IElement | null = null with get, set
    member val PreviousChecked = true with get, set
    member val SawTemplate = false with get, set
    /// The depth of each template whose content has been seen to hold an element, to count what is inside the content fragment.
    member val TemplateDepths: Dictionary<INode, int> | null = null with get, set

    member this.Reset(baseline: int, elements: int, tokens: int) =
        this.Baseline <- baseline
        this.Elements <- elements
        this.Tokens <- tokens
        this.RootAttributes <- 0
        this.AttributePosition <- -1
        this.AttributeNames <- null
        this.Previous <- null
        this.PreviousChecked <- true
        this.SawTemplate <- false
        this.TemplateDepths <- null

module Html5 =
    /// Gumbo's defaults (`Nokogiri::Gumbo::DEFAULT_MAX_TREE_DEPTH`, `DEFAULT_MAX_ATTRIBUTES`).
    [<Literal>]
    let MaxTreeDepth = 400

    [<Literal>]
    let MaxAttributes = 400

    /// Ours (R3): AngleSharp's tree builder is quadratic on three hostile shapes that these bound.
    [<Literal>]
    let MaxElements = 10_000

    [<Literal>]
    let MaxTokens = 40_000

    [<Literal>]
    let MaxRootAttributes = 400

    let private htmlNs = "http://www.w3.org/1999/xhtml"

    let private isVoid (local: string) =
        match local with
        | "area" | "base" | "basefont" | "bgsound" | "br" | "col" | "embed" | "frame" | "hr" | "img" | "input" | "keygen" | "link"
        | "meta" | "param" | "source" | "track" | "wbr" -> true
        | _ -> false

    let private isRawText (local: string) =
        match local with
        | "style" | "script" | "xmp" | "iframe" | "noembed" | "noframes" | "plaintext" | "noscript" -> true
        | _ -> false

    /// The attribute's name as Nokogiri reports and serializes it ("xlink:href", "xml:lang", "xmlns:xlink", "href"). AngleSharp's
    /// `Name` leaves an XLink attribute's prefix off, so the HTML serialization algorithm's rule by namespace applies.
    let qualifiedName (attribute: IAttr) : string =
        match attribute.NamespaceUri with
        | "http://www.w3.org/1999/xlink" -> "xlink:" + attribute.LocalName
        | "http://www.w3.org/XML/1998/namespace" -> "xml:" + attribute.LocalName
        | "http://www.w3.org/2000/xmlns/" -> if attribute.LocalName = "xmlns" then "xmlns" else "xmlns:" + attribute.LocalName
        | _ ->
            match attribute.Prefix with
            | null
            | "" -> attribute.LocalName
            | prefix -> prefix + ":" + attribute.LocalName

    let private isHtml (e: IElement) = String.Equals(e.NamespaceUri, htmlNs, StringComparison.Ordinal)

    // --- Limits ---------------------------------------------------------------------------------

    /// The element's depth as Gumbo's stack of open elements would hold it: its ancestors up to the container, itself included.
    /// Inside a template's content (a fragment of its own) the template's depth carries on.
    let private depthOf (st: ParseState) (element: IElement) : int =
        let mutable count = 0
        let mutable node: INode | null = element
        let mutable carried = -1
        while not (isNull node) do
            match node with
            | :? IElement as current ->
                count <- count + 1
                node <- current.Parent
            | :? IDocumentFragment as fragment ->
                match st.TemplateDepths with
                | null -> ()
                | depths ->
                    match depths.TryGetValue fragment with
                    | true, depth -> carried <- depth
                    | _ -> ()
                node <- null
            | _ -> node <- null
        if carried >= 0 then count + carried else count - st.Baseline

    let private checkPrevious (st: ParseState) =
        match st.Previous with
        | null -> ()
        | _ when st.PreviousChecked -> ()
        | previous ->
            st.PreviousChecked <- true
            // A void element is never on Gumbo's stack
            if not (isHtml previous && isVoid previous.LocalName) then
                let depth = depthOf st previous
                if depth > MaxTreeDepth then raise (LimitExceeded TreeDepthExceeded)
                match previous with
                | :? IHtmlTemplateElement as template ->
                    let depths =
                        match st.TemplateDepths with
                        | null ->
                            let created = Dictionary<INode, int>(HashIdentity.Reference)
                            st.TemplateDepths <- created
                            created
                        | existing -> existing
                    depths[template.Content] <- depth
                | _ -> ()

    let private onCreated (element: IElement) (_: Text.TextPosition) =
        let st = ParseState.Current
        checkPrevious st
        st.Elements <- st.Elements + 1
        if st.Elements > MaxElements then raise (LimitExceeded TooManyElements)
        if not st.SawTemplate && element :? IHtmlTemplateElement then st.SawTemplate <- true
        st.Previous <- element
        st.PreviousChecked <- false

    let private onToken (token: Tokens.HtmlToken) (_: Text.TextRange) =
        let st = ParseState.Current
        if token.Type <> HtmlTokenType.EndOfFile then
            st.Tokens <- st.Tokens + 1
            if st.Tokens > MaxTokens then raise (LimitExceeded TooManyTokens)
        // The element the last token created is in the tree by now. Checked here, not at the next element, so that a body that is
        // too deep and then one 16 MB comment is refused before the comment is read.
        checkPrevious st
        match token.Type with
        | HtmlTokenType.StartTag ->
            match token.Name with
            | "html"
            | "body" ->
                st.RootAttributes <- st.RootAttributes + token.AsTag().Attributes.Count
                if st.RootAttributes > MaxRootAttributes then raise (LimitExceeded TooManyRootAttributes)
            | _ -> ()
        | _ -> ()

    /// Called for every attribute name the tokenizer finishes, a duplicate and an end tag's attribute included. Gumbo refuses when a
    /// tag that already holds 400 attributes meets another name, whether it would keep it or drop it as a duplicate; 1,000 equal
    /// ones are one attribute and fine. AngleSharp's token keeps the duplicates until the tag is done, so below 400 names there is
    /// nothing to count, and from 400 on the distinct ones are (once per tag, then by one set).
    let private shouldEmit (token: byref<StructHtmlToken>) (name: ReadOnlyMemory<char>) : bool =
        let count = token.Attributes.Count
        if count >= MaxAttributes then
            let st = ParseState.Current
            let position = token.Position.Position
            let names =
                match st.AttributeNames with
                | null -> null
                | existing -> if st.AttributePosition = position then existing else null
            let names =
                match names with
                | null ->
                    let created = HashSet<string>(StringComparer.Ordinal)
                    for i in 0 .. count - 1 do
                        created.Add(token.Attributes[i].Name.ToString()) |> ignore
                    st.AttributePosition <- position
                    st.AttributeNames <- created
                    created
                | existing -> existing
            if names.Count >= MaxAttributes then raise (LimitExceeded TooManyAttributes)
            names.Add(name.ToString()) |> ignore
        true

    // --- The parser -------------------------------------------------------------------------------

    let private parser =
        let mutable options = HtmlParserOptions(IsScripting = false)
        options.OnCreated <- Action<IElement, Text.TextPosition>(onCreated)
        options.OnToken <- Action<Tokens.HtmlToken, Text.TextRange>(onToken)
        options.ShouldEmitAttribute <- ShouldEmitAttribute(fun token name -> shouldEmit &token name)
        HtmlParser(options, BrowsingContext.New(Configuration.Default))

    /// What every document parse starts with: the doctype keeps the parse out of quirks mode (where a <table> would not close a
    /// <p>, as Gumbo's and html5ever's fragment parses, in no-quirks mode, do), and "<body>" puts the content in the body.
    [<Literal>]
    let private DocumentPrefix = "<!DOCTYPE html><body>"

    // Elements a document parse creates before the body's contents (html, head, body), and the ancestors a top-level node has.
    [<Literal>]
    let private DocumentBaseline = 2

    [<Literal>]
    let private DocumentElements = 3

    /// Gumbo drops a byte order mark only at the start of the input.
    let private withoutBom (html: string) : string =
        if html.Length > 0 && html[0] = '\uFEFF' then html.Substring 1 else html

    /// A template's content, which AngleSharp keeps in a fragment of its own, becomes the template's children (Gumbo, libxml2).
    let rec private flattenTemplates (node: INode) =
        match node with
        | :? IHtmlTemplateElement as template ->
            let content = template.Content
            let mutable child = content.FirstChild
            while not (isNull child) do
                let current = nonNull child
                child <- current.NextSibling
                template.AppendChild current |> ignore
        | _ -> ()
        let mutable child = node.FirstChild
        while not (isNull child) do
            let current = nonNull child
            flattenTemplates current
            child <- current.NextSibling

    /// The body of a document parsed after "<body>", with the comments the document algorithm puts outside it brought back in: one
    /// after `</body>` goes to the `html` element, one after `</html>` to the document, where the fragment algorithm (and Gumbo)
    /// leaves them in the body. They arrive after the body's other children, which is not their place when content follows them.
    let private bodyOf (document: IHtmlDocument) : IElement =
        match document.Body with
        | null -> failwith "a document parsed after <body> has no body"
        | body ->
            let mutable sibling = body.NextSibling
            while not (isNull sibling) do
                let current = nonNull sibling
                sibling <- current.NextSibling
                if current :? IComment then body.AppendChild current |> ignore
            let mutable sibling = document.DocumentElement.NextSibling
            while not (isNull sibling) do
                let current = nonNull sibling
                sibling <- current.NextSibling
                if current :? IComment then body.AppendChild current |> ignore
            body :> IElement

    /// Runs `parse` with fresh counters; the limits' exception, whatever the parser did with it, becomes the `ParseError`.
    let private guarded (baseline: int) (elements: int) (tokens: int) (parse: unit -> 'a) : Result<'a, ParseError> =
        let st = ParseState.Current
        st.Reset(baseline, elements, tokens)
        try
            try
                let result = parse ()
                checkPrevious st
                Ok result
            with LimitExceeded error ->
                Error error
        finally
            st.Reset(0, 0, 0)

    let private flattened (container: INode) =
        if ParseState.Current.SawTemplate then flattenTemplates container

    /// Nokogiri's `Document#fragment(html)`: a document parse after "<body>"; the body element is the container (R2).
    let parseBody (html: string) : Result<IElement, ParseError> =
        let text = withoutBom html
        let parsed =
            guarded DocumentBaseline (-DocumentElements) -2 (fun () ->
                let body = bodyOf (parser.ParseDocument(DocumentPrefix + text))
                // The fragment algorithm ignores a <frameset>, where a document makes the body a frameset. Parsed again behind a
                // letter (which closes the window for a frameset, as the fragment case does), its first character then taken
                // off; linear, where ParseFragment moves its nodes one at a time.
                if String.Equals(body.LocalName, "frameset", StringComparison.Ordinal) then None
                else
                    flattened body
                    Some body)
        match parsed with
        | Error error -> Error error
        | Ok(Some body) -> Ok body
        | Ok None ->
            guarded DocumentBaseline (-DocumentElements) -2 (fun () ->
                let body = bodyOf (parser.ParseDocument(DocumentPrefix + "x" + text))
                match body.FirstChild with
                | :? IText as first when first.Data.Length > 0 && first.Data[0] = 'x' ->
                    if first.Data.Length = 1 then body.RemoveChild first |> ignore else first.Data <- first.Data.Substring 1
                | _ -> ()
                flattened body
                body)

    /// An element in the SVG or MathML namespace where HTML start tags still break out (not an HTML integration point, not a MathML
    /// text integration point).
    let private isForeignContext (e: IElement) : bool =
        not (isHtml e)
        && (match e.NamespaceUri, e.LocalName with
            | "http://www.w3.org/2000/svg", ("foreignObject" | "desc" | "title") -> false
            | "http://www.w3.org/1998/Math/MathML", ("mi" | "mo" | "mn" | "ms" | "mtext") -> false
            | "http://www.w3.org/1998/Math/MathML", "annotation-xml" ->
                (match e.GetAttribute "encoding" with
                 | null -> true
                 | encoding ->
                     not (String.Equals(encoding, "text/html", StringComparison.OrdinalIgnoreCase)
                          || String.Equals(encoding, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)))
            | _ -> true)

    /// Nokogiri's `Node#fragment`: `html` parsed in `context`'s context; the nodes, detached, owned by a document of the parse.
    let parseIn (context: IElement) (html: string) : Result<INode[], ParseError> =
        let text = withoutBom html
        // AngleSharp's fragment algorithm never lets HTML break out of a foreign context (the pre-2017 rule), where Gumbo, as
        // browsers do, pops to the HTML root. A context that is foreign and not an integration point is parsed as a `div` instead:
        // equal for the markup the pipeline builds (spans, images, divs: breakout tags), recorded for the rest (unit 5.4).
        let context = if isForeignContext context then (nonNull context.Owner).CreateElement "div" else context
        guarded 1 0 0 (fun () ->
            let nodes = parser.ParseFragment(text, context)
            let result = Array.init nodes.Length (fun i -> nodes[i])
            // From the back: removing the first of many is a copy of the rest
            for i in result.Length - 1 .. -1 .. 0 do
                match result[i].Parent with
                | null -> ()
                | parent -> parent.RemoveChild result[i] |> ignore
            if ParseState.Current.SawTemplate then
                for node in result do
                    flattenTemplates node
            result)

    // --- Serialization ----------------------------------------------------------------------------

    let private escapeText (value: string) (out: StringBuilder) =
        for c in value do
            match c with
            | '&' -> out.Append "&amp;" |> ignore
            | ' ' -> out.Append "&nbsp;" |> ignore
            | '<' -> out.Append "&lt;" |> ignore
            | '>' -> out.Append "&gt;" |> ignore
            | c -> out.Append c |> ignore

    let private escapeAttribute (value: string) (out: StringBuilder) =
        for c in value do
            match c with
            | '&' -> out.Append "&amp;" |> ignore
            | ' ' -> out.Append "&nbsp;" |> ignore
            | '"' -> out.Append "&quot;" |> ignore
            | c -> out.Append c |> ignore

    let rec private write (node: INode) (out: StringBuilder) =
        match node with
        | :? IElement as element ->
            let name = element.LocalName
            out.Append('<').Append(name) |> ignore
            for attribute in element.Attributes do
                out.Append(' ').Append(qualifiedName attribute).Append("=\"") |> ignore
                escapeAttribute attribute.Value out
                out.Append '"' |> ignore
            out.Append '>' |> ignore
            let html = isHtml element
            if not (html && isVoid name) then
                writeChildren element out
                out.Append("</").Append(name).Append('>') |> ignore
        | :? IText as text ->
            match text.ParentElement with
            | null -> escapeText text.Data out
            | parent ->
                if isHtml parent && isRawText parent.LocalName then out.Append text.Data |> ignore
                else escapeText text.Data out
        | :? IComment as comment -> out.Append("<!--").Append(comment.Data).Append("-->") |> ignore
        | :? IDocumentType as doctype -> out.Append("<!DOCTYPE ").Append(doctype.Name).Append('>') |> ignore
        | _ -> writeChildren node out

    and private writeChildren (node: INode) (out: StringBuilder) =
        let mutable child = node.FirstChild
        while not (isNull child) do
            let current = nonNull child
            write current out
            child <- current.NextSibling

    /// Nokogiri's serialization (`html_standard_serialize`) of the node: the node itself, a document's or fragment's children.
    let toHtml (node: INode) : string =
        let out = StringBuilder()
        write node out
        out.ToString()

    /// Of the node's children: a container's markup.
    let innerHtml (node: INode) : string =
        let out = StringBuilder()
        writeChildren node out
        out.ToString()

    // --- The DOM helpers ------------------------------------------------------------------------

    /// All descendants in document order, `root` excluded.
    let descendants (root: INode) : INode list =
        let out = ResizeArray<INode>()
        let stack = Stack<INode>()
        let mutable child = root.LastChild
        while not (isNull child) do
            let current = nonNull child
            stack.Push current
            child <- current.PreviousSibling
        while stack.Count > 0 do
            let node = stack.Pop()
            out.Add node
            let mutable child = node.LastChild
            while not (isNull child) do
                let current = nonNull child
                stack.Push current
                child <- current.PreviousSibling
        List.ofSeq out

    /// The attribute named `name` as Nokogiri names it ("xlink:href", "href").
    let attr (node: INode) (name: string) : string voption =
        match node with
        | :? IElement as element ->
            let mutable result = ValueNone
            let attributes = element.Attributes
            let mutable i = 0
            while result.IsNone && i < attributes.Length do
                let a = nonNull attributes[i]
                if String.Equals(qualifiedName a, name, StringComparison.Ordinal) then result <- ValueSome a.Value
                i <- i + 1
            result
        | _ -> ValueNone

    /// Whether the node is an element with this local name (in any namespace).
    let isNamed (node: INode) (localName: string) : bool =
        match node with
        | :? IElement as element -> String.Equals(element.LocalName, localName, StringComparison.Ordinal)
        | _ -> false

    /// Deep, in the same document.
    let clone (root: IElement) : IElement = root.Clone true :?> IElement

    let private clear (element: INode) =
        let mutable last = element.LastChild
        while not (isNull last) do
            element.RemoveChild(nonNull last) |> ignore
            last <- element.LastChild

    /// Nokogiri's `node.inner_html = html`, parsed in the element's context.
    let setInnerHtml (element: IElement) (html: string) : Result<unit, ParseError> =
        match parseIn element html with
        | Error error -> Error error
        | Ok nodes ->
            clear element
            for node in nodes do
                element.AppendChild node |> ignore
            Ok()

    /// Nokogiri's `node.replace(html)`: parsed in the context of the node's parent.
    let replaceWithHtml (node: INode) (html: string) : Result<unit, ParseError> =
        match node.Parent with
        | :? IElement as parent ->
            match parseIn parent html with
            | Error error -> Error error
            | Ok replacements ->
                for replacement in replacements do
                    parent.InsertBefore(replacement, node) |> ignore
                parent.RemoveChild node |> ignore
                Ok()
        | _ -> Ok()

    /// The node's attributes by name, as the resolution rules in `Attachables` read them (R13).
    let attrsOf (node: INode) : Attrs = fun name -> attr node name

    /// A detached element of `owner`'s document with these attributes, in order.
    let createElement (owner: INode) (local: string) (attributes: (string * string) list) : IElement =
        let document = match owner with :? IDocument as d -> d | _ -> nonNull owner.Owner
        let element = document.CreateElement local
        for (name, value) in attributes do
            element.SetAttribute(name, value)
        element

    /// libxml2's `xmlNodeGetContent`: a text node's data, an element's text descendants joined (comments left out).
    let textContent (node: INode) : string =
        match node with
        | :? IText as text -> text.Data
        | :? IComment as comment -> comment.Data
        | _ ->
            let out = StringBuilder()
            for d in descendants node do
                match d with
                | :? IText as text -> out.Append text.Data |> ignore
                | _ -> ()
            out.ToString()
