// Port of rust/crates/views/templates/messages/_actions.html (reference/app/views/messages/_actions.html.erb)
/// `messages/_actions.html.erb`: the options menu of a message (boosts, reply or download/share, copy link, edit).
module Campfire.Views.Templates.Messages._Actions

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let private icon (ctx: ViewContext) (name: string) : XmlNode =
    Assets.imageSized ctx name 20 [ Attr.class' "colorize--black"; Attr.create "aria-hidden" "true" ]

let private screenReader (text: string) : XmlNode = Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text text ]

/// One of `EmojiHelper::REACTIONS`: a form that posts the boost.
let private quickBoost (message: MessageView) (character: string, title: string) : XmlNode =
    Forms.formWith
        message.BoostsPath
        "post"
        [ Html.attr "data-turbo-frame" ("boosting_message_" + message.ClientMessageId); Attr.create "data-action" "popup#close" ]
        [ Forms.hiddenFieldTag "boost[content]" character
          Forms.buttonTag
              [ Html.attr "title" title; Attr.class' "btn message__action-btn"; Html.attr "data-emoji" character ]
              [ Elem.figure [ Attr.class' "margin-none boost-character" ] [ Html.text character ]
                Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text title ] ] ]

let private newBoost (ctx: ViewContext) (message: MessageView) : XmlNode =
    Links.linkTo
        message.NewBoostPath
        [ Attr.class' "btn message__action-btn message__boost-btn"
          Html.attr "data-turbo-frame" ("new_boost_message_" + message.ClientMessageId)
          Attr.create "data-action" "soft-keyboard#open popup#close" ]
        [ icon ctx "boost.svg"; Html.sp; screenReader "New boost" ]

/// The buttons of the grid: they are inline-block, so each space between two of them renders (V4).
let private grid (ctx: ViewContext) (message: MessageView) : XmlNode list =
    [ match message.Attachment with
      | Some attachment ->
          Links.linkTo
              attachment.DownloadPath
              [ Attr.class' "btn message__action-btn center full-width hide-in-ios-pwa"
                Attr.create "title" "Download"
                Attr.create "aria-label" "Download" ]
              [ icon ctx "download.svg" ]
          Html.sp
          Elem.button
              [ Attr.class' "btn message__action-btn center full-width"
                Attr.create "data-controller" "web-share"
                Attr.create "data-action" "web-share#share"
                Html.attr "data-web-share-files-value" attachment.BlobPath
                Html.attr "data-web-share-title-value" attachment.Filename
                Attr.create "title" "Share"
                Attr.create "aria-label" "Share" ]
              [ icon ctx "share.svg" ]
      | None ->
          Elem.button
              [ Attr.class' "btn message__action-btn center full-width"
                Attr.create "data-action" "reply#reply"
                Attr.create "title" "Reply"
                Attr.create "aria-label" "Reply" ]
              [ icon ctx "reply.svg" ]
      Html.sp
      Elem.button
          [ Attr.class' "btn message__action-btn center full-width"
            Attr.create "title" "Copy link"
            Attr.create "aria-label" "Copy link"
            Attr.create "data-controller" "copy-to-clipboard"
            Attr.create "data-action" "copy-to-clipboard#copy"
            Attr.create "data-copy-to-clipboard-success-class" "btn--success"
            Html.attr "data-copy-to-clipboard-url-value" message.AtPath ]
          [ icon ctx "link.svg" ]
      Html.sp
      Links.linkTo
          message.EditPath
          [ Attr.class' "btn message__action-btn center full-width message__edit-btn"
            Html.attr "data-turbo-frame" ("edit_message_" + message.ClientMessageId)
            Attr.create "title" "Edit"
            Attr.create "aria-label" "Edit" ]
          [ icon ctx "pencil.svg" ] ]

let render (ctx: ViewContext) (message: MessageView) : XmlNode =
    Elem.div
        [ Attr.class' "message__actions"; Attr.create "data-controller" "soft-keyboard" ]
        [ Elem.details
              [ Attr.class' "position-relative"
                Attr.create "data-controller" "popup"
                Attr.create "data-action" "keydown.esc->popup#close toggle->popup#toggle click@document->popup#closeOnClickOutside"
                Attr.create "data-popup-orientation-top-class" "popup-orientation-top" ]
              [ Elem.summary
                    [ Attr.class' "btn message__action-btn message__options-btn" ]
                    [ Assets.imageSized
                          ctx
                          "menu-dots-horizontal.svg"
                          20
                          [ Attr.class' "colorize--black"; Attr.create "aria-hidden" "true" ]
                      Html.sp
                      screenReader "Message options" ]
                Elem.div
                    [ Attr.class' "message__actions-menu border shadow"; Attr.create "data-popup-target" "menu" ]
                    [ Elem.div
                          [ Attr.class' "quick-boosts" ]
                          [ for reaction in reactions do
                                quickBoost message reaction
                            newBoost ctx message ]
                      Elem.div
                          [ Attr.class' "flex flex-wrap border-top margin-block-start-half pad-block-start-half message__actions-grid" ]
                          (grid ctx message) ] ] ]
