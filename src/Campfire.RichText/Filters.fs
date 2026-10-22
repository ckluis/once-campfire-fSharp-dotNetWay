// plans/design-richtext.md (Phase 5, unit 5.4): `ContentFilters::TextMessagePresentationFilters` on AngleSharp: RemoveSoloUnfurledLinkText,
// SanitizeTags, SanitizeAttributes, applied in that order (port of rust/crates/richtext/src/filters.rs).
namespace Campfire.RichText

open System
open System.Collections.Generic
open Campfire.Ruby
open AngleSharp.Dom
open Campfire.RichText.RubyExt

module Filters =
    let private twitterDomains = [ "x.com"; "twitter.com" ]

    /// The scheme, authority, path and fragment of a tweet's URL with `x.com` read as `twitter.com`, so that the two spellings of one
    /// tweet compare equal; any other string comes back as it is. (The query is dropped: the result is only compared with another.)
    let normalizeTweetUrl (url: string option) : Result<string option, RenderError> =
        match url with
        | None -> Ok None
        | Some url ->
            let isTwitterUrl = not (isBlank url) && twitterDomains |> List.exists (fun d -> (RubyString.strip url).Contains(d, StringComparison.Ordinal))
            if not isTwitterUrl then
                Ok(Some url)
            else
                match Attachables.tryAbsoluteUri url with
                | ValueNone -> Ok(Some url)
                | ValueSome parsed ->
                    let host = if parsed.IdnHost = "x.com" then "twitter.com" else parsed.IdnHost
                    let port = if parsed.IsDefaultPort then "" else $":{parsed.Port}"
                    let userInfo = if parsed.UserInfo = "" then "" else parsed.UserInfo + "@"
                    let fragment = parsed.Fragment
                    Ok(Some $"{parsed.Scheme}://{userInfo}{host}{port}{parsed.AbsolutePath}{fragment}")

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
            let! left = normalizeTweetUrl soloUnfurledUrl
            let! right = normalizeTweetUrl (Some plainText)
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
