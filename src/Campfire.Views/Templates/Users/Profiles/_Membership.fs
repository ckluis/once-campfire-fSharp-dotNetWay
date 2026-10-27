// Port of rust/crates/views/templates/users/profiles/_membership.html (reference/app/views/users/profiles/_membership.html.erb)
/// `users/profiles/_membership.html.erb`: a room on the profile, with the button that changes its notifications.
module Campfire.Views.Templates.Users.Profiles._Membership

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (ctx: ViewContext) (membership: ProfileMembership) : XmlNode =
    Elem.li
        [ Attr.class' "flex align-center gap margin-none min-width membership-item" ]
        [ Links.linkTo
              (Routes.room membership.RoomId)
              [ Attr.class' "overflow-ellipsis fill-shade txt-primary txt-undecorated" ]
              [ Elem.strong [] [ Html.text membership.RoomDisplayName ] ]
          Elem.hr [ Attr.class' "separator"; Attr.create "aria-hidden" "true" ]
          Elem.span
              [ Attr.class' "txt-small" ]
              [ Turbo.frameTag
                    (Campfire.Views.Helpers.Turbo.domId membership.RoomParamKey membership.RoomId (Some "involvement"))
                    None
                    None
                    []
                    [ Markup.Rooms.buttonToChangeInvolvement ctx membership.InvolvementRoom membership.Involvement ] ] ]
