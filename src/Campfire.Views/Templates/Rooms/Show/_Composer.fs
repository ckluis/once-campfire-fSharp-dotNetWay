// Port of rust/crates/views/templates/rooms/show/_composer.html (reference/app/views/rooms/show/_composer.html.erb)
/// `rooms/show/_composer.html.erb` (`content_for :footer`), with `RoomsHelper#composer_form_tag`.
module Campfire.Views.Templates.Rooms.Show._Composer

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Rooms

let private screenReader (text: string) : XmlNode = Application.screenReader text

let render (ctx: ViewContext) (room: RoomView) : XmlNode =
    let icon (name: string) (size: string) (options: XmlAttribute list) : XmlNode =
        Elem.img (
            options
            @ [ Html.attr "src" (ctx.Asset name); Html.attr "width" size; Html.attr "height" size ]
        )
    Elem.div
        [ Attr.class' "composer flex align-end gap position-relative"
          Attr.create "data-controller" "typing-notifications"
          Attr.create "data-typing-notifications-active-class" "typing-indicator--active" ]
        [ Links.linkTo
              (Routes.searches ())
              [ Attr.class' "btn flex-item-no-shrink margin-block-end composer__context-btn"
                Attr.create "style" "view-transition-name: input-switcher" ]
              [ icon "search.svg" "20" [ Attr.create "aria-hidden" "true" ]; Html.sp; screenReader "Search" ]
          Html.sp
          Turbo.frameTag
              "composer-frame"
              None
              None
              []
              [ Forms.formWith
                    (Routes.roomMessages room.Id)
                    "post"
                    [ Attr.id "composer"
                      Attr.class' "margin-block flex-item-grow contain"
                      Attr.create "data-controller" "composer drop-target"
                      Attr.create
                          "data-action"
                          "dragenter->drop-target#dragenter dragover->drop-target#dragover drop->drop-target#drop drop-target:drop@window->composer#dropFiles lexxy:file-accept->composer#preventAttachment refresh-room:online@window->composer#online typing-notifications#stop paste->composer#pasteFiles turbo:submit-end->composer#submitEnd refresh-room:offline@window->composer#offline"
                      Attr.create "data-composer-messages-outlet" "#message-area"
                      Attr.create "data-composer-toolbar-class" "composer--rich-text"
                      Html.attrInt "data-composer-room-id-value" room.Id ]
                    [ Elem.fieldset
                          [ Attr.create "data-composer-target" "fields"; Attr.create "contents" "" ]
                          [ Elem.div
                                [ Attr.class' "flex flex-column" ]
                                [ Elem.div
                                      [ Attr.class' "composer__filelist flex flex--align-center gap flex-wrap"
                                        Attr.create "data-composer-target" "fileList" ]
                                      []
                                  Elem.div
                                      [ Attr.class' "flex composer__input input input--actor fill-white min-width"
                                        Attr.create "style" "--input-border-radius: 1.3rem" ]
                                      [ Elem.div
                                            [ Attr.class' "flex align-end gap full-width" ]
                                            [ icon
                                                  "messages-outlined.svg"
                                                  "22"
                                                  [ Attr.create "aria-hidden" "true"
                                                    Attr.class' "composer__input-hint colorize--black"
                                                    Attr.create "style" "view-transition-name: input-btn;" ]
                                              Elem.div
                                                  [ Attr.class' "flex flex-column flex-item-grow min-width gap" ]
                                                  [ Elem.create
                                                        "lexxy-editor"
                                                        [ Attr.create "rows" "1"
                                                          Attr.class' "input lexxy-content"
                                                          Attr.create "style" "order: -1"
                                                          Attr.create "aria-multiline" "true"
                                                          Attr.create "aria-label" "Write a message"
                                                          Attr.create
                                                              "permitted-attachment-types"
                                                              "application/vnd.campfire.mention application/vnd.actiontext.opengraph-embed"
                                                          Attr.create "data-controller" "unfurl"
                                                          Attr.create
                                                              "data-action"
                                                              "lexxy:change->typing-notifications#start keydown->composer#submitByKeyboard:capture lexxy:change->composer#saveDraft lexxy:insert-link->unfurl#unfurl"
                                                          Attr.create "data-composer-target" "text"
                                                          Html.attr "data-direct-upload-url" (ctx.Url "/rails/active_storage/direct_uploads")
                                                          Html.attr
                                                              "data-blob-url-template"
                                                              (ctx.Url "/rails/active_storage/blobs/redirect/:signed_id/:filename")
                                                          Attr.create "id" "message_body"
                                                          Attr.create "input" "message_body_trix_input_message"
                                                          Attr.create "name" "message[body]" ]
                                                        [ // The editor's script puts its toolbar before this space, so it renders between the toolbar and the prompt.
                                                          Html.sp
                                                          Elem.create
                                                              "lexxy-prompt"
                                                              [ Attr.create "trigger" "@"
                                                                Attr.create "name" "mention"
                                                                Html.attr "src" (mentionPromptSrc room.Id)
                                                                Attr.create "remote-filtering" "true"
                                                                Attr.create "empty-results" "No matches" ]
                                                              [] ] ]
                                              Elem.label
                                                  [ Attr.class' "btn btn--borderless txt-small flex-item-no-shrink composer__attachment-btn input--file" ]
                                                  [ icon "attachment.svg" "22" [ Attr.class' "colorize--black"; Attr.create "aria-hidden" "true" ]
                                                    Html.sp
                                                    Elem.input
                                                        [ Attr.create "type" "file"
                                                          Attr.create "data-action" "composer#filePicked"
                                                          Attr.create "multiple" "" ]
                                                    Html.sp
                                                    screenReader "Attach a file" ]
                                              Html.sp
                                              Elem.button
                                                  [ Attr.class' "btn btn--borderless txt-small flex-item-no-shrink composer__rich-text-btn"
                                                    Attr.create "type" "button"
                                                    Attr.create "data-action" "composer#toggleToolbar" ]
                                                  [ icon "text-options.svg" "20" [ Attr.class' "colorize--black"; Attr.create "aria-hidden" "true" ]
                                                    Html.sp
                                                    screenReader "Rich text" ]
                                              Html.sp
                                              Elem.button
                                                  [ Attr.create "name" "send"
                                                    Attr.create "type" "submit"
                                                    Attr.create "data-action" "composer#submit"
                                                    Attr.class' "btn btn--reversed flex-item-no-shrink txt-small" ]
                                                  [ icon "arrow-up.svg" "20" [ Attr.create "aria-hidden" "true" ]
                                                    Html.sp
                                                    screenReader "Send Message" ] ] ] ] ]
                      Elem.div
                          [ Attr.class' "typing-indicator gap txt-small align-center flex-inline"
                            Attr.create "data-typing-notifications-target" "indicator" ]
                          [ Elem.div
                                [ Attr.class' "typing-indicator__author spinner"
                                  Attr.create "data-typing-notifications-target" "author" ]
                                [] ]
                      Elem.input
                          [ Attr.create "data-composer-target" "clientid"
                            Attr.create "type" "hidden"
                            Attr.create "name" "message[client_message_id]"
                            Attr.create "id" "message_client_message_id" ] ] ] ]
