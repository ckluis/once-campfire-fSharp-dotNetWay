// plans/design-richtext.md (Phase 5, unit 5.3): the `ActionText` entry points on AngleSharp that need only loading: plain text and
// mentions. The presentation, the editor value and the rest arrive in 5.4 and 5.5.
namespace Campfire.RichText.Next

open Campfire.RichText
open Campfire.RichText.Next

module ActionText =
    /// `message.body.to_plain_text` (Action Text's `RichText#to_plain_text`), which feeds `Message#plain_text_body`: the search
    /// index, push notification bodies, the webhook's plain body, emoji detection and `/play` commands.
    let toPlainText (body: string) (ctx: RenderContext) : Result<string, RenderError> =
        result {
            let! content = Content.load body ctx
            return! Content.toPlainText content ctx
        }

    /// `Message::Mentionee#mentioned_users`: users attached with a verified SGID, once each.
    let mentionedUsers (body: string) (ctx: RenderContext) : Result<MentionUser list, RenderError> =
        Content.load body ctx
        |> Result.map (fun content ->
            let users = ResizeArray<MentionUser>()
            for node in Content.attachmentNodes content.Root do
                match Attachables.actionTextAttachableFrom (Html5.attrsOf node) ctx with
                | Attachable.User user when not (users.Exists(fun u -> u.Id = user.Id)) -> users.Add user
                | _ -> ()
            List.ofSeq users)
