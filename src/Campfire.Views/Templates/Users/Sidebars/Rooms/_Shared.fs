// Port of rust/crates/views/templates/users/sidebars/rooms/_shared.html (reference/app/views/users/sidebars/rooms/_shared.html.erb)
/// `users/sidebars/rooms/_shared.html.erb` on its own (broadcast, and rendered by the rooms controllers).
module Campfire.Views.Templates.Users.Sidebars.Rooms._Shared

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (room: SidebarRoom) : XmlNode =
    Markup.Rooms.linkToRoom
        room.Id
        [ Html.attr "id" (Campfire.Views.Helpers.Turbo.domId room.ParamKey room.Id (Some "list")) ]
        [ Html.attr "data-sorted-list-name" room.Name ]
        [ Attr.create "style" "--column-gap: 0.5em"; Html.attr "class" room.ClassNames ]
        [ Elem.span [ Attr.class' "overflow-ellipsis" ] [ Html.text room.Name ] ]
