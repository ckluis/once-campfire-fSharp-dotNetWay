// Port of rust/crates/views/templates/rooms/layouts/_new.html (reference/app/views/rooms/layouts/_new.html.erb)
/// `rooms/layouts/_new.html.erb`: the page layout for new rooms; a page fills `roomForm`.
module Campfire.Views.Templates.Rooms.Layouts._New

open Falco.Markup
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup

let regions (ctx: ViewContext) (pageTitle: string option) (roomForm: XmlNode) : Regions =
    { Regions.content
          [ Elem.section
                [ Attr.class' "panel txt-align-center"; Attr.create "style" "view-transition-name: new-room" ]
                [ roomForm ] ] with
        Title = pageTitle
        Nav = [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Markup.Application.linkBackToLastRoomVisited ctx ] ] }
