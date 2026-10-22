// Port of rust/crates/richtext/src/sanitizer.rs
//
// `Rails::HTML5::SafeListSanitizer` with a `Rails::HTML::PermitScrubber`, over Loofah's HTML5
// scrubbing helpers (rails-html-sanitizer 1.7.1, loofah 2.25.2).
//
// Every sanitization layer in the pipeline is this one scrubber with a different pair of tag and
// attribute allowlists, so it is written once here rather than expressed through a sanitizer
// library, whose URL, comment, foreign-content and attribute-escaping rules differ from Loofah's.
namespace Campfire.RichText

open System
open Campfire.RichText.Html5ever

module Sanitizer =
    open Loofah

    /// `ContentFilters::SanitizeTags::ALLOWED_TAGS`
    let sanitizeTagsAllowedTags = SafeListData.sanitizeTagsAllowedTags

    /// `ActionText::Attachment::ATTRIBUTES`
    let attachmentAttributes = SafeListData.attachmentAttributes

    /// `Loofah::HTML5::Scrub.force_correct_attribute_escaping!` (libxml2 builds only, which CRuby is):
    /// spaces and double quotes in `href`, `action`, `src` and an `a`'s `name` become `%20` and `%22`.
    /// The value is written back through `Nokogiri::XML::Attr#value=`, where libxml2 drops the C0
    /// controls XML 1.0 doesn't allow.
    let private forceCorrectAttributeEscaping (attrs: ResizeArray<Attr>) (isA: bool) =
        for attr in attrs do
            if escapesAttribute attr.QualifiedName isA && needsEscaping attr.Value then attr.Value <- escapeAttributeValue attr.Value

    let private scrubStyle (dom: Dom) (node: NodeId) =
        match dom.Attr(node, "style") with
        | ValueNone -> ()
        | ValueSome style ->
            match Loofah.scrubStyle style with
            | None -> dom.RemoveAttr(node, "style") |> ignore
            | Some scrubbed -> if scrubbed <> style then dom.SetAttr(node, "style", scrubbed)

    /// `PermitScrubber#scrub_attributes` with an attribute allowlist. Attributes are visited in order,
    /// and each allowed one re-escapes every URL attribute on the node as it goes, so a later URL is
    /// checked in its re-escaped form (" javascript:" has become "%20javascript:" and passes).
    let private scrubAttributes (dom: Dom) (node: NodeId) (list: SafeList) =
        match dom.Element node with
        | ValueNone -> ()
        | ValueSome element ->
            let isA = element.Name.Local = "a"
            let attrs = element.Attrs
            let mutable i = 0
            while i < attrs.Count do
                let attr = attrs[i]
                let name = attr.QualifiedName
                let scrubbed = not (list.AllowsAttribute name) || (attrValIsUri name && not (allowedUri attr.Value))
                let blankSrc = name = "src" && attr.Value |> Seq.forall isWhitespace
                if scrubbed then
                    attrs.RemoveAt i
                else
                    // A blank src goes, but the escaping still runs for the attributes after it
                    if blankSrc then attrs.RemoveAt i else i <- i + 1
                    forceCorrectAttributeEscaping attrs isA
            scrubStyle dom node

    /// `Rails::HTML::PermitScrubber#scrub`
    let private scrub (dom: Dom) (node: NodeId) (list: SafeList) =
        if not (dom.IsText node) then
            let keep =
                match dom.LocalName node with
                | ValueSome name -> list.AllowsTag name
                | ValueNone -> false
            if not keep then
                // Unwrap HTML elements (and comments, which have no children); drop foreign (SVG,
                // MathML) elements together with their contents, since they carry a namespace.
                let foreign = dom.IsElement node && not (dom.IsHtmlElement node)
                if not foreign then
                    for child in Seq.toArray (dom.Children node) do
                        dom.InsertBefore(node, child)
                dom.Detach node
            else
                scrubAttributes dom node list

    /// `Loofah::Scrubber#traverse_conditionally_bottom_up`: children (as they were before any of them
    /// was scrubbed) first, then the node itself.
    let rec private scrubBottomUp (dom: Dom) (node: NodeId) (list: SafeList) =
        for child in Seq.toArray (dom.Children node) do
            scrubBottomUp dom child list
        scrub dom node list

    let private scrubbed (html: string) (list: SafeList) : Result<struct (Dom * NodeId), ParseError> =
        let dom = Dom()
        match dom.ParseFragment html with
        | Error e -> Error e
        | Ok fragment ->
            for child in Seq.toArray (dom.Children fragment) do
                scrubBottomUp dom child list
            Ok(struct (dom, fragment))

    /// `SafeListSanitizer#sanitize(html, tags:, attributes:)`.
    let sanitize (html: string) (list: SafeList) : Result<string, ParseError> =
        if html.Length = 0 then
            Ok ""
        else
            scrubbed html list |> Result.map (fun (struct (dom, fragment)) -> dom.ToHtml fragment)

    /// `sanitize`, serialized with `<` and `>` escaped in attribute values too, so that the result can
    /// be scanned with regular expressions (auto_link) without mistaking an attribute for text.
    /// Rails serializes them raw; the two are the same DOM.
    let sanitizeWithEscapedAttributeBrackets (html: string) (list: SafeList) : Result<string, ParseError> =
        if html.Length = 0 then
            Ok ""
        else
            scrubbed html list
            |> Result.map (fun (struct (dom, fragment)) -> dom.ToHtmlWithEscapedAttributeBrackets fragment)

    /// `Loofah::HTML5::Scrub.allowed_uri?`
    let allowedUri (uri: string) : bool = Loofah.allowedUri uri

    /// `CGI.unescapeHTML`
    let cgiUnescapeHtml (s: string) : string = Loofah.cgiUnescapeHtml s
