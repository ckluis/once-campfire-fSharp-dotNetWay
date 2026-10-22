// plans/design-richtext.md (Phase 5, unit 5.3): the `ActionText` entry points on AngleSharp plain text and mentions (5.3), the editor value (5.4).
// The presentation arrives in 5.5.
namespace Campfire.RichText.Next

open Campfire.RichText
open Campfire.RichText.Next
open Campfire.RichText.RubyExt
open Campfire.RailsCompat
open Campfire.Ruby

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

    /// The value Lexxy's editor receives when editing a message: `RichTextHelper#editable_body`, then
    /// Lexxy's `render_custom_attachments_in`. `None` when the body is blank (no `value` attribute).
    let editableValue (body: string) (ctx: RenderContext) : Result<string option, RenderError> =
        result {
            // editable_body: every attachment rebuilt from its attachable, on the stored markup as is
            let! root = parseErr (Html5.parseBody (RubyString.strip body))
            for node in Content.attachmentNodes root do
                let! attachment = Attachables.attachmentFrom (Html5.attrsOf node) ctx
                match attachment.Attachable with
                // A mention of a deleted user, say: nothing to edit, so it leaves the editor (Rails raises).
                | Attachable.Missing _ ->
                    match node.Parent with
                    | null -> ()
                    | parent -> parent.RemoveChild node |> ignore
                | attachable ->
                    let! contentType = Attachables.attachableContentType attachable
                    let! content = Content.renderAttachmentHtml attachment ctx
                    node.SetAttribute("content-type", contentType)
                    node.SetAttribute("content", content)
            let editable = Html5.innerHtml root
            if isBlank editable then
                return None
            else
                // Lexxy: attachments without a url get their rendered partial as a JSON string
                let! root = parseErr (Html5.parseBody (RubyString.strip editable))
                for node in Content.attachmentNodes root do
                    let noUrl =
                        match Html5.attr node "url" with
                        | ValueNone -> true
                        | ValueSome url -> isBlank url
                    if noUrl then
                        let! attachment = Attachables.attachmentFrom (Html5.attrsOf node) ctx
                        let! content = Content.renderAttachmentHtml attachment ctx
                        node.SetAttribute("content", Json.encode (Value.String content))
                return Some(Html5.innerHtml root)
        }
