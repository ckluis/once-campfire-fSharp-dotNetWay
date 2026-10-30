// Port of rust/crates/views/templates/rooms/layouts/_edit.html (reference/app/views/rooms/layouts/_edit.html.erb)
/// `rooms/layouts/_edit.html.erb`: the page layout for editing a room; a page fills `roomForm`. The title is the page's
/// `@page_title`; `canAdminister`, `roomId` and `displayName` are what the open and closed forms share.
module Campfire.Views.Templates.Rooms.Layouts._Edit

open Falco.Markup
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup

let regions
    (ctx: ViewContext)
    (pageTitle: string option)
    (canAdminister: bool)
    (roomId: int64)
    (displayName: string)
    (roomForm: XmlNode)
    : Regions =
    { Regions.content
          [ Elem.section
                [ Attr.class' "panel txt-align-center"
                  Html.attr "style" ("view-transition-name: edit-room-" + string roomId) ]
                [ roomForm ]
            if canAdminister then
                Elem.section [ Attr.class' "panel txt-align-center" ] [ Markup.Rooms.buttonToDeleteRoom ctx roomId displayName ] ] with
        Title = pageTitle
        Nav = [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Markup.Application.linkBackToLastRoomVisited ctx ] ] }
