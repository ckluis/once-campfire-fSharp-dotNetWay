// Port of rust/crates/views/templates/rooms/layouts/_form.html (reference/app/views/rooms/layouts/_form.html.erb)
/// `rooms/layouts/_form.html.erb`, used as a block wrapped around the open and closed room forms' fields (`content`).
module Campfire.Views.Templates.Rooms.Layouts._Form

open Falco.Markup
open Campfire.Views
open Campfire.Views.Helpers
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let render (ctx: ViewContext) (room: FormRoom) (canAdminister: bool) (kind: RoomKind) (content: XmlNode list) : XmlNode =
    Forms.formWith
        (room.Action kind)
        (if room.Id.IsSome then "patch" else "post")
        []
        [ Elem.div
              [ Attr.class' "flex align-center gap" ]
              [ if canAdminister then
                    Translations.translationButton ctx "room_name"
                    Elem.label
                        [ Attr.class' "flex-item-grow txt-large" ]
                        [ Elem.input
                              [ Attr.create "name" "room[name]"
                                Attr.create "id" "room_name"
                                Attr.class' "input full-width"
                                Attr.create "required" "required"
                                Attr.create "autofocus" "autofocus"
                                Attr.create "placeholder" "Name the room"
                                Attr.create "data-turbo-permanent" "true"
                                Attr.create "data-action" "keydown.enter->form#submit:prevent"
                                Attr.create "type" "text"
                                match room.Name with
                                | Some name -> Html.attr "value" name
                                | None -> () ]
                          Html.sp
                          Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Name this room" ] ]
                else
                    Elem.h1 [ Attr.class' "flex-item-grow txt-x-large" ] [ Html.text room.DisplayName ] ]
          Elem.hr [ Attr.class' "margin-block borderless" ]
          Elem.section [ Attr.class' "room-access margin-block pad-inline fill-shade border-radius" ] content
          if canAdminister then
              Forms.buttonTag
                  [ Attr.class' "btn btn--reversed txt-large center" ]
                  [ Assets.imageSized ctx "check.svg" 20 [ Attr.create "aria-hidden" "true" ]
                    Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Save" ] ] ]
