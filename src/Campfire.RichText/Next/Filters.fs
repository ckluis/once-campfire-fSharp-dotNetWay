// plans/design-richtext.md (Phase 5, unit 5.4): `ContentFilters::TextMessagePresentationFilters` on AngleSharp, the same three filters
// in the same order as ../Filters.fs: RemoveSoloUnfurledLinkText, SanitizeTags, SanitizeAttributes.
namespace Campfire.RichText.Next

open System
open System.Collections.Generic
open AngleSharp.Dom
open Campfire.RichText
open Campfire.RichText.Next
open Campfire.RichText.RubyExt

module Filters =
    let private detach (node: INode) =
        match node.Parent with
        | null -> ()
        | parent -> parent.RemoveChild node |> ignore

    /// A message that is nothing but a link to what it unfurls shows just the unfurl.
    let removeSoloUnfurledLinkText (content: Content) (ctx: RenderContext) : Result<Content, RenderError> =
        let root = content.Root
        let unfurledLinks =
            Html5.descendants root
            |> List.filter (fun n -> Html5.isNamed n Content.AttachmentTag && Html5.attr n "content-type" = ValueSome Attachables.OpengraphEmbedContentType)
        result {
            let! soloUnfurledUrl =
                if unfurledLinks.Length = 1 then
                    Attachables.opengraphEmbedFrom (Html5.attrsOf unfurledLinks[0]) ctx
                    |> Result.map (Option.bind (fun embed -> embed.Href))
                else
                    Ok None
            let! plainText = Content.toPlainText content ctx
            let! left = Campfire.RichText.Filters.normalizeTweetUrl soloUnfurledUrl
            let! right = Campfire.RichText.Filters.normalizeTweetUrl (Some plainText)
            if left <> right then
                return content
            else
                let divs = Html5.descendants root |> List.filter (fun n -> Html5.isNamed n "div")
                if not divs.IsEmpty then
                    // A Trix body: every div gets the unfurl as its only content
                    let unfurl = Html5.toHtml unfurledLinks[0]
                    for div in divs do
                        do! parseErr (Html5.setInnerHtml (div :?> IElement) unfurl)
                else
                    for p in Html5.descendants root |> List.filter (fun n -> Html5.isNamed n "p") do
                        let hasAttachment = Html5.descendants p |> List.exists (fun n -> Html5.isNamed n Content.AttachmentTag)
                        if not hasAttachment then detach p
                return content
        }

    let private sanitizeTagsAllowed = HashSet<string>(SafeListData.sanitizeTagsAllowedTags, StringComparer.Ordinal)

    /// Removes every element outside the allowlist, together with its contents.
    let sanitizeTags (content: Content) : Content =
        let disallowed =
            Html5.descendants content.Root
            |> List.filter (fun n ->
                match n with
                | :? IElement as e -> not (sanitizeTagsAllowed.Contains e.LocalName)
                | _ -> false)
        for node in disallowed do
            detach node
        content

    /// Scrubs attributes with Rails' safe-list sanitizer over SanitizeTags' own tags.
    let sanitizeAttributes (content: Content) : Result<Content, RenderError> =
        result {
            let! html = Sanitizer.sanitize (Content.toHtml content) SafeList.contentFilter |> parseErr
            return! Content.wrap html
        }

    let apply (content: Content) (ctx: RenderContext) : Result<Content, RenderError> =
        result {
            let! content = removeSoloUnfurledLinkText content ctx
            let content = sanitizeTags content
            return! sanitizeAttributes content
        }
