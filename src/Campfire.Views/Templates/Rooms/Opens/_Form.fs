// Port of rust/crates/views/templates/rooms/opens/_form.html (reference/app/views/rooms/opens/_form.html.erb)
/// `rooms/opens/_form.html.erb`: the access list of an open room, in the shared room form.
module Campfire.Views.Templates.Rooms.Opens._Form

open Falco.Markup
open Campfire.Views
open Campfire.Views.Templates.Rooms
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let private everyone (ctx: ViewContext) (canAdminister: bool) (typeChangePath: string) : XmlNode =
    Elem.li
        [ Attr.class' "flex align-center gap margin-none" ]
        [ Elem.figure
              [ Attr.class' "avatar flex-item-no-shrink"; Attr.create "style" "--avatar-border-radius: 0; --avatar-size: 4ch;" ]
              [ Elem.img
                    [ Attr.create "aria-hidden" "true"
                      Attr.class' "colorize--black"
                      Attr.create "style" "background-color: transparent"
                      Html.attr "src" (ctx.Asset "everyone.svg") ]
                Html.sp
                Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Everyone" ] ]
          Elem.div
              [ Attr.class' "min-width" ]
              [ Elem.div [ Attr.class' "overflow-ellipsis fill-shade" ] [ Elem.strong [] [ Text.raw "Everyone" ] ] ]
          Elem.hr [ Attr.class' "separator"; Attr.create "aria-hidden" "true" ]
          if canAdminister then
              Elem.a
                  [ Attr.class' "btn--faux flex-inline"
                    Attr.create "tabindex" "-1"
                    Attr.create "data-turbo-action" "replace"
                    Html.attr "href" typeChangePath ]
                  [ Elem.label
                        [ Attr.create "for" "room_type"; Attr.class' "switch" ]
                        [ Elem.input
                              [ Attr.create "type" "checkbox"
                                Attr.create "id" "room_type"
                                Attr.class' "switch__input"
                                Attr.create "checked" "checked" ]
                          Html.sp
                          Elem.span [ Attr.class' "switch__btn round" ] []
                          Html.sp
                          Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Give only some access to this room" ] ] ] ]

let render (ctx: ViewContext) (form: OpenFormView) (typeChangePath: string) : XmlNode =
    Layouts._Form.render
        ctx
        form.Room
        form.CanAdminister
        Open
        [ Markup.Users.userFilterMenu
              [ everyone ctx form.CanAdminister typeChangePath
                Elem.hr [ Attr.class' "separator full-width"; Attr.create "style" "--border-style: solid" ]
                if form.Users.Length > 20 then
                    Markup.Users.userFilterSearch
                Elem.div
                    [ Attr.create "data-filter-target" "list"; Attr.create "contents" "" ]
                    [ for user in form.Users -> _User.render ctx user form.CanAdminister ] ] ]
