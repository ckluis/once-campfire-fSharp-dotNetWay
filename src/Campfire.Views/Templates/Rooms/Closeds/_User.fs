// Port of rust/crates/views/templates/rooms/closeds/_user.html (reference/app/views/rooms/closeds/_user.html.erb)
/// `rooms/closeds/_user.html.erb`: a user's row in a closed room's form, `selected` when they have access.
module Campfire.Views.Templates.Rooms.Closeds._User

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let render (ctx: ViewContext) (user: UserView) (form: ClosedFormView) (selected: bool) : XmlNode =
    Markup.Rooms.userRow
        user
        [ if form.CanAdminister then
              if user.Id = form.CurrentUserId && form.Room.Id.IsNone then
                  Elem.input
                      [ Attr.create "type" "hidden"; Attr.create "name" "user_ids[]"; Html.attrInt "value" user.Id ]
                  Elem.img
                      [ Attr.class' "colorize--black flex-item-no-shrink"
                        Attr.create "aria-hidden" "true"
                        Html.attr "src" (ctx.Asset "check.svg")
                        Attr.create "width" "20"
                        Attr.create "height" "20" ]
              else
                  Elem.label
                      [ Attr.class' "switch flex-item-no-shrink" ]
                      [ Elem.input
                            [ Attr.create "type" "checkbox"
                              Attr.create "name" "user_ids[]"
                              Html.attrInt "value" user.Id
                              Attr.class' "switch__input"
                              if selected then
                                  Attr.create "checked" "checked" ]
                        Html.sp
                        Elem.span [ Attr.class' "switch__btn round" ] []
                        Html.sp
                        Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text ("Give " + user.Name + " access to this room") ] ] ]
