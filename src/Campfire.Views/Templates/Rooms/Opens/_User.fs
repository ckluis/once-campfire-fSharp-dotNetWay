// Port of rust/crates/views/templates/rooms/opens/_user.html (reference/app/views/rooms/opens/_user.html.erb)
/// `rooms/opens/_user.html.erb`: a user's row in an open room's form.
module Campfire.Views.Templates.Rooms.Opens._User

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let render (ctx: ViewContext) (user: UserView) (canAdminister: bool) : XmlNode =
    Markup.Rooms.userRow
        user
        [ if canAdminister then
              Elem.img
                  [ Attr.class' "colorize--black flex-item-no-shrink"
                    Attr.create "aria-hidden" "true"
                    Html.attr "src" (ctx.Asset "check.svg")
                    Attr.create "width" "20"
                    Attr.create "height" "20" ] ]
