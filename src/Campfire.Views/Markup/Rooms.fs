// `RoomsHelper` and `Rooms::InvolvementsHelper` as Falco.Markup functions (plans/design-views.md V13). The writer halves that the
// sidebar and the profile's memberships still use are `Helpers/RoomsHelper.fs` (deleted with the writer in 4.11).
namespace Campfire.Views.Markup

open Falco.Markup
open Campfire.Routes
open Campfire.Views

module RoomsHelper = Campfire.Views.Helpers.RoomsHelper
module TurboIds = Campfire.Views.Helpers.Turbo
module UrlHelper = Campfire.Views.Helpers.Url
module ApplicationHelper = Campfire.Views.Helpers.Application

module Rooms =
    /// `button_to_delete_room(room)`: a trash icon and the room's name, with no whitespace between.
    let buttonToDeleteRoom (ctx: ViewContext) (roomId: int64) (displayName: string) : XmlNode =
        Forms.buttonTo
            (ctx.Url(Routes.room roomId))
            "delete"
            [ Attr.class' "btn btn--negative max-width"
              Html.attr "aria-label" ("Delete " + displayName)
              Attr.create "data-turbo-confirm" "Are you sure you want to delete this room and all messages in it? This can’t be undone." ]
            [ Assets.imageSized ctx "trash.svg" 20 [ Attr.create "aria-hidden" "true" ]
              Elem.span [ Attr.class' "overflow-ellipsis" ] [ Html.text displayName ] ]

    /// `button_to_change_involvement(room, involvement)`.
    let buttonToChangeInvolvement (ctx: ViewContext) (room: RoomsHelper.InvolvementRoom) (involvement: string) : XmlNode =
        let labelId = TurboIds.domId room.ParamKey room.Id (Some "involvement_label")
        let url =
            UrlHelper.withQuery (Routes.roomInvolvement room.Id) [ "involvement", UrlHelper.One(RoomsHelper.nextInvolvement room.Direct involvement) ]
        Forms.buttonTo
            url
            "put"
            [ Html.attr "class" ("btn " + involvement)
              Attr.create "role" "checkbox"
              Attr.create "aria-checked" "true"
              Html.attr "aria-labelledby" labelId
              Attr.create "tabindex" "0" ]
            [ Assets.imageSized ctx ($"notification-bell-{involvement}.svg") 20 [ Attr.create "aria-hidden" "true" ]
              Elem.span
                  [ Attr.class' "for-screen-reader"; Html.attr "id" labelId ]
                  [ Html.text (RoomsHelper.humanizeInvolvement involvement) ] ]

    /// A user's row in the open and closed rooms' access lists (`rooms/opens/_user`, `rooms/closeds/_user`): their avatar and name,
    /// then `tail` after the separator.
    let userRow (user: Campfire.Views.Messages.UserView) (tail: XmlNode list) : XmlNode =
        let children =
            [ Elem.figure
                  [ Attr.class' "avatar flex-item-no-shrink"; Attr.create "style" "--avatar-size: 4ch;" ]
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
              Elem.div
                  [ Attr.class' "min-width" ]
                  [ Elem.div [ Attr.class' "overflow-ellipsis fill-shade" ] [ Elem.strong [] [ Html.text user.Name ] ] ]
              Elem.hr [ Attr.class' "separator"; Attr.create "aria-hidden" "true" ] ]
        Elem.li
            [ Attr.class' "flex align-center gap margin-none"
              Html.attr "data-value" (ApplicationHelper.toLowercase user.Name) ]
            (children @ tail)
