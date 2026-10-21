// Port of rust/crates/views/templates/rooms/show/_nav.html (reference/app/views/rooms/show/_nav.html.erb)
/// `rooms/show/_nav.html.erb` (`content_for :nav`): the room's name, its settings link and the notifications bell.
module Campfire.Views.Templates.Rooms.Show._Nav

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Templates.Rooms
open Campfire.Views.Rooms

let render (ctx: ViewContext) (room: RoomView) : XmlNode list =
    [ if ctx.Account.HasLogo then
          Markup.Users.accountLogoTag ctx None
      Elem.span
          [ Attr.class' "btn btn--reversed btn--faux room--current" ]
          [ Elem.h1
                [ Attr.class' "room__contents txt-medium overflow-ellipsis" ]
                [ if room.IsDirect then
                      Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Ping with" ]
                      Html.sp
                  Html.text room.DisplayName ] ]
      Html.sp
      Links.linkTo
          room.EditPath
          [ Attr.class' "btn"
            Html.attr "style" ("view-transition-name: edit-room-" + string room.Id)
            Html.attrInt "data-room-id" room.Id ]
          [ Assets.imageSized ctx "menu-dots-horizontal.svg" 20 [ Attr.create "aria-hidden" "true" ]
            Html.sp
            Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text ("Settings for this " + room.Noun) ] ]
      Html.sp
      Involvements._Bell.render ctx room ]
