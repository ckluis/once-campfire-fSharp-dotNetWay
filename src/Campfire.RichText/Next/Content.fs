// plans/design-richtext.md (Phase 5, unit 5.3): `ActionText::Content` on AngleSharp, the first half: the attachment nodes, Trix
// conversion, galleries, `load`, `wrap`, `toHtml` and `toPlainText`. The second half (rendering) arrives in 5.4.
//
// As in ../Content.fs, each Ruby step that serializes a node and parses the markup back (`Fragment#replace` with a string,
// `inner_html=`) does the same here, in the same parse context, because those round trips are where the output takes its shape.
namespace Campfire.RichText.Next

open System
open AngleSharp.Dom
open Campfire.RailsCompat
open Campfire.Ruby
open Campfire.RichText
open Campfire.RichText.Next
open Campfire.RichText.RubyExt

/// A fragment of rich text: the container element a body was parsed into (R5).
type Content = { Root: IElement }

module Content =
    [<Literal>]
    let AttachmentTag = "action-text-attachment"

    let private asElement (node: INode) : IElement = node :?> IElement

    let attachmentNodes (root: INode) : IElement list =
        Html5.descendants root |> List.filter (fun n -> Html5.isNamed n AttachmentTag) |> List.map asElement

    /// `ActionText::TrixAttachment::ATTRIBUTES`, in order
    let private trixAttributes =
        [ "sgid", "sgid"
          "contentType", "content-type"
          "url", "url"
          "href", "href"
          "filename", "filename"
          "filesize", "filesize"
          "width", "width"
          "height", "height"
          "previewable", "previewable"
          "content", "content"
          "caption", "caption"
          "presentation", "presentation" ]

    /// The attributes `data-trix-attachment` and `data-trix-attributes` carry, merged in that order,
    /// keyed by their Trix names.
    let private trixAttributesOf (node: INode) : Result<ResizeArray<string * Value>, RenderError> =
        let attributes = ResizeArray<string * Value>()
        let mutable failed = ValueNone
        for name in [ "data-trix-attachment"; "data-trix-attributes" ] do
            if failed.IsNone then
                let parsed =
                    match Html5.attr node name with
                    // Unparseable JSON is logged and treated as no attributes
                    | ValueSome json -> defaultArg (jsonParse json) Value.Null
                    | ValueNone -> Value.Null
                match parsed with
                | Value.Null
                | Value.Bool false -> ()
                | Value.Object map ->
                    for (key, value) in map do
                        match trixAttributes |> List.tryFind (fun (trix, _) -> trix = key) with
                        | Some(trix, _) ->
                            match attributes.FindIndex(fun (k, _) -> k = trix) with
                            | -1 -> attributes.Add((trix, value))
                            | i -> attributes[i] <- (trix, value)
                        | None -> ()
                | _ -> failed <- ValueSome(Raised "NoMethodError: merge")
        match failed with
        | ValueSome e -> Error e
        | ValueNone -> Ok attributes

    /// `fragment_by_converting_trix_attachments`: `figure[data-trix-attachment]` (or any element
    /// carrying the attribute) becomes an `<action-text-attachment>`, or disappears if it has none of
    /// the attachment attributes.
    let private convertTrixAttachments (root: INode) (ctx: RenderContext) : Result<unit, RenderError> =
        let nodes = Html5.descendants root |> List.filter (fun n -> (Html5.attr n "data-trix-attachment").IsSome)
        result {
            for node in nodes do
                let! attributes = trixAttributesOf node
                let elementAttrs = ResizeArray<string * string>()
                for name in SafeListData.attachmentAttributes do
                    let trixName = trixAttributes |> List.tryFind (fun (_, dashed) -> dashed = name) |> Option.map fst
                    match attributes |> Seq.tryFind (fun (k, _) -> Some k = trixName) with
                    | Some(_, value) -> elementAttrs.Add((name, jsonValueToS value))
                    | None -> ()
                let! replacement =
                    if elementAttrs.Count = 0 then
                        Ok ""
                    else
                        let element = Html5.createElement node AttachmentTag (List.ofSeq elementAttrs)
                        // Attachment.from_node resolves the attachable, which may raise
                        Attachables.attachmentFrom (Html5.attrsOf element) ctx
                        |> Result.map (fun _ -> Html5.toHtml element)
                do! parseErr (Html5.replaceWithHtml node replacement)
        }

    let private isGalleryAttachment (node: INode) : bool =
        Html5.isNamed node AttachmentTag && Html5.attr node "presentation" = ValueSome "gallery"

    /// `AttachmentGallery.find_attachment_gallery_nodes`: `div:has(A + A)` for gallery attachments A,
    /// whose children are all gallery attachments or newline/space text.
    let attachmentGalleryNodes (root: INode) : IElement list =
        Html5.descendants root
        |> List.filter (fun d -> Html5.isNamed d "div")
        |> List.filter (fun div ->
            Html5.descendants div
            |> List.exists (fun n ->
                isGalleryAttachment n
                && (match n with
                    | :? IElement as element ->
                        (match element.PreviousElementSibling with
                         | null -> false
                         | previous -> isGalleryAttachment previous)
                    | _ -> false)))
        |> List.filter (fun div ->
            div.ChildNodes
            |> Seq.forall (fun child ->
                match child with
                | :? IText as text -> text.Data |> Seq.forall (fun c -> c = '\n' || c = ' ')
                | _ -> isGalleryAttachment child))
        |> List.map asElement

    /// Loads (and canonicalizes) `html` as a new fragment: the container element.
    let loadRoot (html: string) (ctx: RenderContext) : Result<IElement, RenderError> =
        result {
            let! root = parseErr (Html5.parseBody (RubyString.strip html))
            do! convertTrixAttachments root ctx
            for node in attachmentNodes root do
                do! parseErr (Html5.setInnerHtml node "")
            for gallery in attachmentGalleryNodes root do
                let html = "<div>" + Html5.innerHtml gallery + "</div>"
                do! parseErr (Html5.replaceWithHtml gallery html)
            return root
        }

    /// `ActionText::Content.new(html)`, which is also how a stored body loads: canonicalized.
    let load (html: string) (ctx: RenderContext) : Result<Content, RenderError> =
        loadRoot html ctx |> Result.map (fun root -> { Root = root })

    /// `ActionText::Content.new(html, canonicalize: false)`, i.e. `Fragment.from_html`.
    let wrap (html: string) : Result<Content, RenderError> =
        parseErr (Html5.parseBody (RubyString.strip html)) |> Result.map (fun root -> { Root = root })

    let toHtml (content: Content) : string = Html5.innerHtml content.Root

    /// `render_attachments`' first step: an attachment's `content` attribute is sanitized with Action
    /// Text's allowlist, and dropped if that leaves nothing.
    let private sanitizeContentAttribute (node: IElement) : Result<unit, RenderError> =
        match Html5.attr node "content" with
        | ValueSome content ->
            node.RemoveAttribute "content" |> ignore
            Sanitizer.sanitize content SafeList.actionText
            |> parseErr
            |> Result.map (fun sanitized -> if not (isBlank sanitized) then node.SetAttribute("content", sanitized))
        | ValueNone -> Ok()

    /// `ActionText::Content#to_plain_text`
    let toPlainText (content: Content) (ctx: RenderContext) : Result<string, RenderError> =
        let root = Html5.clone content.Root
        result {
            for node in attachmentNodes root do
                do! sanitizeContentAttribute node
                let! attachment = Attachables.attachmentFrom (Html5.attrsOf node) ctx
                match Attachables.attachmentPlainText attachment with
                | PlainTextRepresentation.Markup text -> do! parseErr (Html5.replaceWithHtml node text)
                | PlainTextRepresentation.Fragment fragmentHtml ->
                    let! fragment = loadRoot fragmentHtml ctx
                    match node.Parent with
                    | null -> ()
                    | parent ->
                        for child in Seq.toArray fragment.ChildNodes do
                            parent.InsertBefore(child, node) |> ignore
                        parent.RemoveChild node |> ignore
            return PlainText.nodeToPlainText root
        }
