// Port of rust/crates/views/templates/messages/edit.html (reference/app/views/messages/edit.html.erb)
/// `messages/edit.html.erb`: the edit frame, with the rich text editor (or just delete, for an attachment).
module Campfire.Views.Templates.Messages.Edit

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let private screenReader (text: string) : XmlNode = Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text text ]

/// The delete button: it submits the delete form at the end of the frame.
let private deleteButton (ctx: ViewContext) (message: MessageView) (options: XmlAttribute list) : XmlNode =
    Forms.buttonTag
        (options
         @ [ Html.attr "form" ("delete_form_message_" + message.ClientMessageId)
             Attr.create "data-turbo-confirm" "Are you sure you want to delete this message?" ])
        [ Assets.image ctx "trash.svg" [ Attr.create "aria-hidden" "true" ]
          Html.sp
          screenReader "Delete message" ]

let private editButtons = "message__edit-btns flex align-center justify-space-between gap full-width pad-block-start-half"

let private editor (ctx: ViewContext) (edit: EditView) : XmlNode =
    let message = edit.Message
    Elem.div
        [ Attr.class' "composer--edit composer--rich-text" ]
        [ Forms.formWith
              message.Path
              "patch"
              [ Html.attr "id" ("form_message_" + message.ClientMessageId)
                Attr.create "data-controller" "form"
                Attr.create
                    "data-action"
                    "lexxy:file-accept->form#preventAttachment keydown.esc->form#cancel keydown.ctrl+enter->form#submit:prevent keydown.meta+enter->form#submit:prevent" ]
              [ Elem.div
                    [ Attr.class' "full-width input input--actor min-width fill-white" ]
                    [ Elem.create
                          "lexxy-editor"
                          [ Attr.create "rows" "1"
                            Attr.class' "input lexxy-content"
                            Attr.create "aria-multiline" "true"
                            Attr.create "aria-label" "Edit message"
                            Attr.create "autofocus" "autofocus"
                            Attr.create
                                "permitted-attachment-types"
                                "application/vnd.campfire.mention application/vnd.actiontext.opengraph-embed"
                            Attr.create
                                "data-action"
                                "lexxy:change->typing-notifications#start keydown->composer#submitByKeyboard:capture"
                            Html.attr "data-direct-upload-url" (ctx.Url "/rails/active_storage/direct_uploads")
                            Html.attr "data-blob-url-template" (ctx.Url "/rails/active_storage/blobs/redirect/:signed_id/:filename")
                            Attr.create "id" "message_body"
                            Html.attr "input" ("message_body_trix_input_message_" + message.ClientMessageId)
                            Attr.create "name" "message[body]"
                            Html.attr "value" edit.EditableBodyHtml ]
                          [ Elem.create
                                "lexxy-prompt"
                                [ Attr.create "trigger" "@"
                                  Attr.create "name" "mention"
                                  Html.attr "src" (mentionPromptSrc message.RoomId)
                                  Attr.create "remote-filtering" "true"
                                  Attr.create "empty-results" "No matches" ]
                                [] ] ]
                Elem.a
                    [ Attr.create "data-form-target" "cancel"; Attr.create "hidden" "hidden"; Html.attr "href" message.Path ]
                    [ Text.raw "Close editor and discard changes" ]
                Elem.div
                    [ Html.attr "class" editButtons ]
                    [ Forms.buttonTag
                          [ Attr.class' "btn btn--reversed" ]
                          [ Assets.image ctx "check.svg" [ Attr.create "aria-hidden" "true" ]
                            Html.sp
                            screenReader "Save changes" ]
                      Html.sp
                      deleteButton ctx message [ Attr.class' "btn btn--negative" ] ] ] ]

let render (ctx: ViewContext) (edit: EditView) : XmlNode =
    let message = edit.Message
    Elem.create
        "turbo-frame"
        [ Html.attr "id" ("edit_message_" + message.ClientMessageId) ]
        [ Elem.div
              [ Attr.class' "message__body position-relative"; Attr.create "data-controller" "scroll-into-view" ]
              [ Elem.div
                    [ Attr.class' "message__body-content message__body-content--editing gap" ]
                    [ match message.Attachment with
                      | Some attachment ->
                          Markup.Messages.attachmentPresentation ctx attachment
                          Elem.div
                              [ Html.attr "class" editButtons ]
                              [ deleteButton ctx message [ Attr.class' "btn btn--negative center margin-block-end" ] ]
                      | None -> editor ctx edit ]
                Elem.div
                    [ Attr.class' "message__actions flex flex-wrap" ]
                    [ Links.linkTo
                          message.Path
                          [ Attr.class' "message__action-btn message__edit-close-btn txt-small btn btn--borderless" ]
                          [ Assets.image ctx "remove.svg" [ Attr.class' "colorize--black"; Attr.create "aria-hidden" "true" ]
                            Html.sp
                            screenReader "Close editor and discard changes" ] ]
                Forms.formWith
                    message.Path
                    "delete"
                    [ Html.attr "id" ("delete_form_message_" + message.ClientMessageId)
                      Html.attr "data-turbo-frame" ("edit_message_" + message.ClientMessageId) ]
                    [] ] ]
