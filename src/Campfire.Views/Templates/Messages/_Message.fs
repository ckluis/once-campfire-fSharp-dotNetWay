// Port of rust/crates/views/templates/messages/_message.html (reference/app/views/messages/_message.html.erb)
/// `messages/_message.html.erb`, with `MessagesHelper#message_tag`: one message, or `_unrenderable` in its place when its
/// content raised. Its body is `cache [ message, "presentation-v3" ]` (`MessagesCached.fetch`).
module Campfire.Views.Templates.Messages._Message

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let private avatar (user: UserView) : XmlNode =
    Elem.figure
        [ Attr.class' "avatar message__avatar" ]
        [ Links.linkTo
              user.Path
              [ Html.attr "title" user.Title; Attr.class' "btn avatar"; Attr.create "data-turbo-frame" "_top" ]
              [ Elem.img
                    [ Attr.create "aria-hidden" "true"
                      Html.attr "src" user.AvatarUrl
                      Attr.create "width" "48"
                      Attr.create "height" "48" ] ] ]

/// The heading's three inline pieces, each separated by the space the ERB's line breaks leave (V4).
let private heading (message: MessageView) : XmlNode =
    Elem.h3
        [ Attr.class' "message__heading" ]
        [ Elem.span
              [ Attr.class' "message__author"; Html.attr "title" message.Creator.Title ]
              [ Elem.strong [ Attr.create "data-reply-target" "author" ] [ Html.text message.Creator.Name ] ]
          Html.sp
          Elem.a
              [ Attr.create "target" "_top"; Attr.class' "message__permalink"; Html.attr "href" message.AtPath ]
              [ Elem.time
                    [ Attr.class' "message__timestamp"
                      Html.attr "datetime" message.CreatedAtIso
                      Attr.create "data-local-time-target" "time" ]
                    [] ]
          Html.sp
          Elem.span
              [ Attr.class' "message__room" ]
              [ // The ERB's line break before the link renders after the `::before` arrow of a search result (the loose DOM drops it).
                Html.sp
                Elem.a
                    [ Attr.create "target" "_top"; Attr.create "data-reply-target" "link"; Html.attr "href" message.AtPath ]
                    [ Html.text message.RoomName ] ] ]

/// `boosts` are the message's boosts' fragments, fetched up front (`BoostsCached.boosts`).
let render (ctx: ViewContext) (message: MessageView) (boosts: CachedFragment[]) : XmlNode =
    if message.IsUnrenderable then
        _Unrenderable.render
    else
        Elem.div
            [ Html.attr "id" ("message_" + message.ClientMessageId)
              (if message.AllEmoji then Attr.class' "message message--emoji" else Attr.class' "message")
              Attr.create "data-controller" "reply"
              Html.attrInt "data-user-id" message.Creator.Id
              Html.attrInt "data-message-id" message.Id
              Html.attrInt "data-message-timestamp" message.CreatedAtEpoch
              Html.attrInt "data-message-updated-at" message.UpdatedAtEpoch
              Html.attrInt "data-sort-value" message.CreatedAtEpoch
              Attr.create "data-messages-target" "message"
              Attr.create "data-search-results-target" "message"
              Attr.create "data-refresh-room-target" "message"
              Attr.create "data-reply-composer-outlet" "#composer" ]
            [ Elem.h2
                  [ Attr.class' "message__day-separator" ]
                  [ Elem.time [ Html.attr "datetime" message.CreatedAtIso; Attr.create "data-local-time-target" "date" ] [] ]
              avatar message.Creator
              Elem.create
                  "turbo-frame"
                  [ Html.attr "id" ("edit_message_" + message.ClientMessageId) ]
                  [ Elem.div
                        [ Attr.class' "message__body" ]
                        [ Elem.div
                              [ Attr.class' "message__body-content" ]
                              [ Elem.div
                                    [ Attr.class' "message__meta" ]
                                    [ heading message; _Actions.render ctx message ]
                                _Presentation.render ctx message
                                Boosts._Boosts.render ctx message boosts ] ] ] ]
