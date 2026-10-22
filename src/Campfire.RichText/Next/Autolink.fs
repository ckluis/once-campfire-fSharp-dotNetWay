// plans/design-richtext.md R6 (Phase 5, unit 5.5): rails_autolink on AngleSharp nodes.
//
// rails_autolink scans the serialized HTML with regular expressions, and a match can never cross a tag, so the same scan runs on each
// text node's form as Nokogiri would serialize it (`&amp;`, `&lt;`, `&gt;`, `&nbsp;`); the matchers are the old module's. The
// pieces come back decoded and become text nodes and `<a target="_blank">` elements built with the DOM: no string is spliced into
// markup, so the attribute-value breakout rails_autolink has cannot happen, and there is no tag index (a text node with an `a`
// ancestor is skipped, which is what `auto_linked?` computed from the string). Linking adds no parse.
//
// The old module sanitized each link text and href with the default list, a parse each. For the escaped form of text that is the
// identity unless the parser would change a character (a NUL, a lone surrogate), so only a piece holding one takes that path.
namespace Campfire.RichText.Next

open System
open System.Text
open AngleSharp.Dom
open Campfire.Ruby
open Campfire.RichText
open Campfire.RichText.RubyExt
open Campfire.RichText.Unicode

/// A text node cut into what stays text and what becomes a link, decoded.
type Segment =
    | Plain of string
    | Linked of href: string * text: string

module Autolink =
    /// The inverse of the text escaping plus `Erb.htmlEscape`'s two: every `&` of the escaped form starts one of these references.
    let private decode (escaped: string) : string =
        if escaped.IndexOf '&' < 0 then
            escaped
        else
            let out = StringBuilder(escaped.Length)
            let mutable i = 0
            let startsAt (reference: string) = String.CompareOrdinal(escaped, i, reference, 0, reference.Length) = 0
            while i < escaped.Length do
                if escaped[i] <> '&' then
                    out.Append escaped[i] |> ignore
                    i <- i + 1
                elif startsAt "&amp;" then
                    out.Append '&' |> ignore
                    i <- i + 5
                elif startsAt "&lt;" then
                    out.Append '<' |> ignore
                    i <- i + 4
                elif startsAt "&gt;" then
                    out.Append '>' |> ignore
                    i <- i + 4
                elif startsAt "&nbsp;" then
                    out.Append ' ' |> ignore
                    i <- i + 6
                elif startsAt "&quot;" then
                    out.Append '"' |> ignore
                    i <- i + 6
                elif startsAt "&#39;" then
                    out.Append '\'' |> ignore
                    i <- i + 5
                else
                    out.Append '&' |> ignore
                    i <- i + 1
            out.ToString()

    /// Whether parsing the piece as a body and serializing it could change it.
    let private mayChange (piece: string) : bool = piece |> Seq.exists (fun c -> c = '\000' || Char.IsSurrogate c)

    /// `sanitize(piece)` of a piece of escaped text: itself, unless the parser would change a character of it.
    let private sanitized (piece: string) : Result<string, ParseError> =
        if mayChange piece then Sanitizer.sanitize piece SafeList.defaults else Ok piece

    let private brackets = [| '['; ']'; '('; ')'; '{'; '}' |]

    /// `auto_link_urls`: the escaped text as pieces; `Error` only when a piece took the parsing path and a limit refused it.
    let private linkUrls (text: string) (pieces: ResizeArray<Choice<string, struct (string * string)>>) : Result<unit, ParseError> =
        let acc = StringBuilder()
        let mutable last = 0
        let mutable error = ValueNone
        let mutable i = 0
        while error.IsNone && i < text.Length do
            match Campfire.RichText.Autolink.urlMatchAt text i with
            | ValueNone -> i <- i + 1
            | ValueSome m ->
                acc.Append(text, last, m.Start - last) |> ignore
                last <- m.End
                i <- m.End
                // Strip trailing punctuation, keeping a closing bracket its URL opened
                let counts = Array.zeroCreate<int> 6
                for k in m.Start .. m.End - 1 do
                    let b = Array.IndexOf(brackets, text[k])
                    if b >= 0 then counts[b] <- counts[b] + 1
                let mutable hEnd = m.End
                let mutable stripping = true
                while stripping do
                    if hEnd <= m.Start then
                        stripping <- false
                    else
                        let paired = hEnd - 2 >= m.Start && Char.IsLowSurrogate text[hEnd - 1] && Char.IsHighSurrogate text[hEnd - 2]
                        let width = if paired then 2 else 1
                        let cp = if paired then Char.ConvertToUtf32(text[hEnd - 2], text[hEnd - 1]) else int text[hEnd - 1]
                        if Chars.isWordCodePoint cp || cp = int '/' || cp = int '-' || cp = int '=' || cp = int ';' then
                            stripping <- false
                        else
                            hEnd <- hEnd - width
                            if width = 1 then
                                let c = text[hEnd]
                                let b = Array.IndexOf(brackets, c)
                                if b >= 0 then counts[b] <- counts[b] - 1
                                match Campfire.RichText.Autolink.openingBracket c with
                                | ValueSome opening when counts[Array.IndexOf(brackets, opening)] > counts[Array.IndexOf(brackets, c)] ->
                                    hEnd <- hEnd + 1
                                    stripping <- false
                                | _ -> ()
                // The punctuation stripped from the end, then (as rails_autolink does, in this order) the `>` that ended the URL
                let punctuationStart = hEnd
                let mutable trailingGt = false
                if hEnd - 4 >= m.Start && String.CompareOrdinal(text, hEnd - 4, "&gt;", 0, 4) = 0 then
                    hEnd <- hEnd - 4
                    trailingGt <- true
                let linkText = text.Substring(m.Start, hEnd - m.Start)
                let href = if m.HasScheme then linkText else "http://" + linkText
                match sanitized linkText, sanitized href with
                | Ok linkText, Ok href ->
                    if acc.Length > 0 then
                        pieces.Add(Choice1Of2(acc.ToString()))
                        acc.Clear() |> ignore
                    pieces.Add(Choice2Of2(struct (href, linkText)))
                    // What was stripped is text again (SafeBuffer#+ escapes it), then the `>` that ended the URL
                    acc.Append(Erb.htmlEscape (text.Substring(punctuationStart, m.End - punctuationStart))) |> ignore
                    if trailingGt then acc.Append "&gt;" |> ignore
                | Error e, _
                | _, Error e -> error <- ValueSome e
        match error with
        | ValueSome e -> Error e
        | ValueNone ->
            acc.Append(text, last, text.Length - last) |> ignore
            if acc.Length > 0 then pieces.Add(Choice1Of2(acc.ToString()))
            Ok()

    /// `auto_link_email_addresses` over one stretch of escaped text outside the links just made.
    let private linkEmails (text: string) (out: ResizeArray<Segment>) : Result<unit, ParseError> =
        let mutable copied = 0
        let mutable position = 0
        let mutable error = ValueNone
        while error.IsNone && position < text.Length do
            let precededByLocalChar = position > 0 && Campfire.RichText.Autolink.isEmailLocalChar text[position - 1]
            let found = if precededByLocalChar then ValueNone else Campfire.RichText.Autolink.emailMatchEnd text position
            match found with
            | ValueNone -> position <- position + (if Char.IsHighSurrogate text[position] && position + 1 < text.Length then 2 else 1)
            | ValueSome finish ->
                let start = position
                match sanitized (text.Substring(start, finish - start)) with
                | Error e -> error <- ValueSome e
                | Ok email ->
                    if start > copied then out.Add(Plain(decode (text.Substring(copied, start - copied))))
                    // An address holds no `&` (a reference ends in `;`), so the escaped form is the text
                    let href = "mailto:" + (Campfire.Ruby.Uri.urlEncode email).Replace("%40", "@")
                    out.Add(Linked(href, email))
                    copied <- finish
                    position <- Math.Max(finish, position + 1)
        match error with
        | ValueSome e -> Error e
        | ValueNone ->
            if copied < text.Length then out.Add(Plain(decode (text.Substring copied)))
            Ok()

    /// rails_autolink over one text node's escaped form: text and links, decoded.
    let linkText (escaped: string) : Result<Segment list, ParseError> =
        let pieces = ResizeArray<Choice<string, struct (string * string)>>()
        match linkUrls escaped pieces with
        | Error e -> Error e
        | Ok() ->
            let out = ResizeArray<Segment>()
            let mutable error = ValueNone
            for piece in pieces do
                if error.IsNone then
                    match piece with
                    | Choice2Of2(struct (href, text)) -> out.Add(Linked(decode href, decode text))
                    | Choice1Of2 text ->
                        match linkEmails text out with
                        | Error e -> error <- ValueSome e
                        | Ok() -> ()
            match error with
            | ValueSome e -> Error e
            | ValueNone -> Ok(List.ofSeq out)

    /// A text node worth scanning: a URL needs `//` or `www.`, an address `@`.
    let private mayHaveLinks (data: string) : bool =
        data.IndexOf '@' >= 0 || data.IndexOf("//", StringComparison.Ordinal) >= 0 || data.IndexOf("w.", StringComparison.OrdinalIgnoreCase) >= 0

    /// The nodes that replace the text child of `parent`, or none when nothing in it links.
    let private replacementsFor (parent: IElement) (data: string) : Result<INode[] voption, ParseError> =
        if not (mayHaveLinks data) then
            Ok ValueNone
        else
            let escaped =
                let out = StringBuilder(data.Length + 16)
                Html5.escapeText data out
                out.ToString()
            match linkText escaped with
            | Error e -> Error e
            | Ok [ Plain _ ]
            | Ok [] -> Ok ValueNone
            | Ok segments ->
                let owner = nonNull parent.Owner
                let nodes =
                    segments
                    |> List.map (fun segment ->
                        match segment with
                        | Plain s -> owner.CreateTextNode s :> INode
                        | Linked(href, text) ->
                            let a = Html5.createElement owner "a" [ "target", "_blank"; "href", href ]
                            a.AppendChild(owner.CreateTextNode text) |> ignore
                            a :> INode)
                    |> Array.ofList
                Ok(ValueSome nodes)

    /// Links every text node below `container` with no `a` ancestor. An element's children are replaced all at once, so the work
    /// is linear in the node's width (AngleSharp finds a child by scanning its parent's list).
    let rec private linkIn (container: IElement) : Result<unit, ParseError> =
        let children = Html5.childrenOf container
        let rebuilt = ResizeArray<INode>(children.Length)
        let mutable changed = false
        let mutable error = ValueNone
        let mutable index = 0
        while error.IsNone && index < children.Length do
            let child = children[index]
            match child with
            | :? IText ->
                // Adjacent text nodes are one text in the serialized form rails_autolink scans (the sanitizer leaves them side by
                // side when it unwraps what was between them), so a URL runs across them
                let mutable stop = index + 1
                while stop < children.Length && (children[stop] :? IText) do
                    stop <- stop + 1
                let data =
                    if stop = index + 1 then (child :?> IText).Data
                    else String.Join("", seq { for k in index .. stop - 1 -> (children[k] :?> IText).Data })
                match replacementsFor container data with
                | Error e -> error <- ValueSome e
                | Ok(ValueSome nodes) ->
                    changed <- true
                    rebuilt.AddRange nodes
                | Ok ValueNone ->
                    for k in index .. stop - 1 do
                        rebuilt.Add children[k]
                index <- stop
            | :? IElement as element ->
                rebuilt.Add child
                if not (Html5.isNamed child "a") then
                    match linkIn element with
                    | Error e -> error <- ValueSome e
                    | Ok() -> ()
                index <- index + 1
            | _ ->
                rebuilt.Add child
                index <- index + 1
        match error with
        | ValueSome e -> Error e
        | ValueNone ->
            if changed then
                Html5.detachChildren container |> ignore
                for node in rebuilt do
                    container.AppendChild node |> ignore
            Ok()

    /// `auto_link(html, html: { target: "_blank" }, sanitize_options: { tags:, attributes: })`: the body is sanitized (the one parse),
    /// then every text node outside an `a` is linked in place and the container's markup written.
    let autoLink (html: string) (list: SafeList) : Result<string, ParseError> =
        if isBlank html then
            Ok ""
        else
            match Html5.parseBody html with
            | Error e -> Error e
            | Ok body ->
                Sanitizer.scrub body list
                match linkIn body with
                | Error e -> Error e
                | Ok() -> Ok(Html5.innerHtml body)
