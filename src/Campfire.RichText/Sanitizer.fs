// plans/design-richtext.md R4 (Phase 5, unit 5.2): Loofah's `PermitScrubber` as a walker over AngleSharp nodes.
//
// Children first, then the node (`traverse_conditionally_bottom_up`). An element outside the tag list is unwrapped (its children
// move before it), a comment is removed, a foreign (SVG, MathML) element goes with its contents. An allowed element's attributes
// are visited in order: one outside the list, or a URI attribute failing `allowed_uri?`, is removed, a blank `src` too; after each
// kept attribute `force_correct_attribute_escaping!` runs over all the element's URI attributes, so a later URL is checked in its
// re-escaped form. Lexxy's `style` rule comes last. Every string rule is `Loofah`'s.
namespace Campfire.RichText

open System
open AngleSharp.Dom

module Sanitizer =
    let private htmlNs = "http://www.w3.org/1999/xhtml"

    let private isBlank (value: string) = value |> Seq.forall Loofah.isWhitespace

    let private remove (element: IElement) (attribute: IAttr) =
        element.RemoveAttribute(attribute.NamespaceUri, attribute.LocalName) |> ignore

    /// `PermitScrubber#scrub_attributes`, then Lexxy's `style` rule.
    let private scrubAttributes (element: IElement) (list: SafeList) =
        let isA = String.Equals(element.LocalName, "a", StringComparison.Ordinal)
        let attributes = Array.init element.Attributes.Length (fun i -> nonNull element.Attributes[i])
        let removed = Collections.Generic.HashSet<IAttr>(HashIdentity.Reference)
        for attribute in attributes do
            let name = Html5.qualifiedName attribute
            let value = attribute.Value
            let scrubbed = not (list.AllowsAttribute name) || (Loofah.attrValIsUri name && not (Loofah.allowedUri value))
            if scrubbed || (name = "src" && isBlank value) then
                remove element attribute
                removed.Add attribute |> ignore
            if not scrubbed then
                for other in attributes do
                    if not (removed.Contains other) && Loofah.escapesAttribute (Html5.qualifiedName other) isA && Loofah.needsEscaping other.Value then
                        other.Value <- Loofah.escapeAttributeValue other.Value
        match Html5.attr element "style" with
        | ValueNone -> ()
        | ValueSome style ->
            match Loofah.scrubStyle style with
            | None ->
                for attribute in attributes do
                    if not (removed.Contains attribute) && Html5.qualifiedName attribute = "style" then remove element attribute
            | Some scrubbed ->
                if scrubbed <> style then
                    for attribute in attributes do
                        if not (removed.Contains attribute) && Html5.qualifiedName attribute = "style" then attribute.Value <- scrubbed

    /// `Rails::HTML::PermitScrubber#scrub`
    let private scrubNode (node: INode) (list: SafeList) =
        match node with
        | :? IText -> ()
        | _ ->
            match node with
            | :? IElement as element when list.AllowsTag element.LocalName -> scrubAttributes element list
            | _ ->
                let foreign =
                    match node with
                    | :? IElement as element -> not (String.Equals(element.NamespaceUri, htmlNs, StringComparison.Ordinal))
                    | _ -> false
                match node.Parent with
                | null -> ()
                | parent ->
                    if not foreign then Html5.insertAllBefore parent (Html5.detachChildren node) node
                    parent.RemoveChild node |> ignore

    /// `Loofah::Scrubber#traverse_conditionally_bottom_up`: children (as they were before any of them was scrubbed) first, then the node.
    let rec private scrubBottomUp (node: INode) (list: SafeList) =
        for child in Html5.childrenOf node do
            scrubBottomUp child list
        scrubNode node list

    /// Scrubs the children of `root` in place (R4).
    let scrub (root: INode) (list: SafeList) : unit =
        for child in Html5.childrenOf root do
            scrubBottomUp child list

    /// `SafeListSanitizer#sanitize(html, tags:, attributes:)`: parse as a body, scrub, serialize the container's markup.
    let sanitize (html: string) (list: SafeList) : Result<string, ParseError> =
        if html.Length = 0 then
            Ok ""
        else
            match Html5.parseBody html with
            | Error error -> Error error
            | Ok body ->
                scrub body list
                Ok(Html5.innerHtml body)
