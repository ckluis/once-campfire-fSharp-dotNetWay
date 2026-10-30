// Port of rust/crates/views/templates/rooms/closeds/_form.html (reference/app/views/rooms/closeds/_form.html.erb)
/// `rooms/closeds/_form.html.erb`: the access list of a closed room, in the shared room form.
module Campfire.Views.Templates.Rooms.Closeds._Form

open Falco.Markup
open Campfire.Views
open Campfire.Views.Templates.Rooms
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let private everyone (ctx: ViewContext) (typeChangePath: string) : XmlNode list =
    [ Elem.li
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
            Elem.a
                [ Attr.class' "btn--faux flex-inline"
                  Attr.create "tabindex" "-1"
                  Attr.create "data-turbo-action" "replace"
                  Html.attr "href" typeChangePath ]
                [ Elem.label
                      [ Attr.create "for" "room_type"; Attr.class' "switch" ]
                      [ Elem.input [ Attr.create "type" "checkbox"; Attr.create "id" "room_type"; Attr.class' "switch__input" ]
                        Html.sp
                        Elem.span [ Attr.class' "switch__btn round" ] []
                        Html.sp
                        Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Give everyone access to this room" ] ] ] ]
      Elem.hr [ Attr.class' "separator full-width"; Attr.create "style" "--border-style: solid" ] ]

let render (ctx: ViewContext) (form: ClosedFormView) (typeChangePath: string) : XmlNode =
    let access = if form.CanAdminister then everyone ctx typeChangePath else []
    let search =
        if form.SelectedUsers.Length + form.UnselectedUsers.Length > 20 then
            [ Markup.Users.userFilterSearch ]
        else
            []
    let list =
        Elem.div
            [ Attr.create "data-filter-target" "list"; Attr.create "contents" "" ]
            [ for user in form.SelectedUsers do
                  _User.render ctx user form true
              if not form.SelectedUsers.IsEmpty && not form.UnselectedUsers.IsEmpty then
                  Elem.hr [ Attr.class' "separator full-width"; Attr.create "style" "--border-style: solid" ]
              for user in form.UnselectedUsers do
                  _User.render ctx user form false ]
    Layouts._Form.render ctx form.Room form.CanAdminister Closed [ Markup.Users.userFilterMenu (access @ search @ [ list ]) ]
