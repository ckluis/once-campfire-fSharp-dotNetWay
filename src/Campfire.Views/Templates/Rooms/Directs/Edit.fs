// Port of rust/crates/views/templates/rooms/directs/edit.html (reference/app/views/rooms/directs/edit.html.erb)
/// `rooms/directs/edit.html.erb`: a Ping's members, and the button that deletes it.
module Campfire.Views.Templates.Rooms.Directs.Edit

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Rooms

let private member' (user: Campfire.Views.Messages.UserView) : XmlNode =
    Elem.div
        [ Attr.class' "member flex flex-column gap fill-shade pad border-radius" ]
        [ Elem.figure
              [ Attr.class' "avatar center"; Attr.create "style" "--avatar-border-radius: 10ch; --avatar-size: 10ch;" ]
              [ Elem.a
                    [ Html.attr "title" user.Title
                      Attr.class' "btn avatar"
                      Attr.create "data-turbo-frame" "_top"
                      Html.attr "href" user.Path ]
                    [ Elem.img
                          [ Attr.create "aria-hidden" "true"
                            Attr.create "loading" "lazy"
                            Html.attr "src" user.AvatarUrl
                            Attr.create "width" "48"
                            Attr.create "height" "48" ] ] ]
          Elem.strong [] [ Html.text user.Name ] ]

/// The page: `@page_title`, the way back, and the members with the delete button.
let rendered (ctx: ViewContext) (edit: DirectEditView) : Campfire.Views.Layouts.ViewPage =
    let content =
        Elem.div
            [ Attr.class' "panel txt-align-center" ]
            [ Elem.section [ Attr.class' "directs--edit margin-block-end" ] [ for user in edit.Users -> member' user ]
              Forms.buttonTo
                  (ctx.Url(Routes.roomsDirect edit.RoomId))
                  "delete"
                  [ Attr.class' "btn btn--negative center"
                    Attr.create "aria-label" "Delete Ping"
                    Attr.create "data-turbo-confirm" "Are you sure you want to delete this ping and all messages in it? This can’t be undone." ]
                  [ Elem.img [ Attr.create "aria-hidden" "true"; Html.attr "src" (ctx.Asset "trash.svg") ]
                    Html.sp
                    Text.raw "Ping" ] ]
    { Regions =
        { Campfire.Views.Layouts.Regions.content [ content ] with
            Title = Some("Edit settings for " + edit.DisplayName)
            Nav = [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBackToLastRoomVisited ctx ] ] }
      Fragments = [] }
