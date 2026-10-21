// Port of rust/crates/views/templates/rooms/show.html (reference/app/views/rooms/show.html.erb)
/// `rooms/show.html.erb`, with `MessagesHelper#message_area_tag` and `#messages_tag`: the room page. A page module returns its
/// regions (`Layouts.Regions`) and the fragments it places; the layout is the writer's until unit 4.10 (`Legacy.inLayout`).
///
/// The module is `ShowPage`, not `Show`: `rooms/show/` holds the partials (`Templates/Rooms/Show/_Nav.fs`...), and a
/// module and a namespace of one name can't both be in an assembly.
module Campfire.Views.Templates.Rooms.ShowPage

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

/// `content_for :head`.
let private head (show: ShowView) : XmlNode list =
    [ Elem.meta [ Attr.create "name" "turbo-cache-control"; Attr.create "content" "no-preview" ]
      Elem.meta [ Attr.create "name" "current-room-id"; Html.attrInt "content" show.Room.Id ] ]

/// The page itself: the message area around the room's messages.
let private content (ctx: ViewContext) (show: ShowView) : XmlNode list =
    [ Elem.div
          [ Attr.id "message-area"
            Attr.class' "message-area"
            Attr.create "contents" "true"
            Attr.create "data-controller" "messages presence drop-target"
            Attr.create
                "data-action"
                "turbo:before-stream-render@document->messages#beforeStreamRender keydown.up@document->messages#editMyLastMessage dragenter->drop-target#dragenter dragover->drop-target#dragover drop->drop-target#drop visibilitychange@document->presence#visibilityChanged"
            Attr.create "data-messages-first-of-day-class" "message--first-of-day"
            Attr.create "data-messages-formatted-class" "message--formatted"
            Attr.create "data-messages-me-class" "message--me"
            Attr.create "data-messages-mentioned-class" "message--mentioned"
            Attr.create "data-messages-threaded-class" "message--threaded"
            Html.attr "data-messages-page-url-value" (ctx.Url(Routes.roomMessages show.Room.Id)) ]
          [ Campfire.Views.Templates.Messages._Template.render ctx show.User
            Elem.div
                [ Html.attr "id" (roomDomId show.Room.Kind show.Room.Id "messages")
                  Attr.class' "messages"
                  Attr.create "data-controller" "maintain-scroll refresh-room"
                  Attr.create
                      "data-action"
                      "turbo:before-stream-render@document->maintain-scroll#beforeStreamRender visibilitychange@document->refresh-room#visibilityChanged online@window->refresh-room#online"
                  Attr.create "data-messages-target" "messages"
                  Html.attrInt "data-refresh-room-loaded-at-value" (MessagesSupport.epochMs show.UpdatedAt)
                  Html.attr "data-refresh-room-url-value" (ctx.Url(Routes.roomRefresh show.Room.Id)) ]
                [ if show.Invitation then
                      Show._Invitation.render ctx show.JoinCode
                  for message in show.Messages do
                      Html.fragment message.Html ]
            Elem.create
                "turbo-cable-stream-source"
                [ Attr.create "channel" "RoomMessagesChannel"; Html.attr "signed-stream-name" show.MessagesStreamName ]
                []
            Html.sp
            Elem.button
                [ Attr.class' "message-area__return-to-latest btn"
                  Attr.create "data-action" "messages#returnToLatest"
                  Attr.create "data-messages-target" "latest"
                  Attr.create "hidden" "hidden" ]
                [ Assets.imageSized ctx "arrow-down.svg" 20 [ Attr.create "aria-hidden" "true" ]
                  Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Jump to newest message" ] ] ] ]

/// The page's regions: `@page_title`, `@body_class` and the `content_for` blocks.
let regions (ctx: ViewContext) (show: ShowView) : Regions =
    { Title = Some show.Room.DisplayName
      BodyClass = Some "sidebar"
      Head = head show
      Nav = Show._Nav.render ctx show.Room
      Content = content ctx show
      Footer = [ Show._Composer.render ctx show.Room ]
      Sidebar = [ Markup.Users.sidebarFrame (Some(Routes.userSidebar ())) ] }

/// The page, with the messages' fragments it places.
let rendered (ctx: ViewContext) (show: ShowView) : ViewPage =
    { Regions = regions ctx show
      Fragments = [ for message in show.Messages -> message.Html ] }
