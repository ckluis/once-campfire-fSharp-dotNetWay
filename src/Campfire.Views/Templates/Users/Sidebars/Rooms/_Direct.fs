// Port of rust/crates/views/templates/users/sidebars/rooms/_direct.html (reference/app/views/users/sidebars/rooms/_direct.html.erb)
/// `users/sidebars/rooms/_direct.html.erb` on its own (broadcast when a direct room appears). The reference wraps it in
/// `cache membership do`; the cached rendering is `UsersCached.fetch`.
module Campfire.Views.Templates.Users.Sidebars.Rooms._Direct

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (ctx: ViewContext) (membership: SidebarDirect) : XmlNode =
    let many = membership.Members.Length > 1
    let avatars =
        if many then
            [ Elem.div
                  [ Attr.class' "avatar__group" ]
                  [ for index, member' in List.indexed (List.truncate 4 membership.Members) do
                        if index > 0 then Html.sp
                        Elem.span
                            [ Attr.class' "avatar" ]
                            [ Assets.imageSized ctx member'.AvatarPath 20 [ Attr.create "aria-hidden" "true" ] ] ] ]
        else
            [ Elem.span
                  [ Attr.class' "avatar" ]
                  [ Assets.imageSized ctx (List.head membership.Members).AvatarPath 48 [ Attr.create "aria-hidden" "true" ] ]
              Html.sp ]
    let label = if many then membership.MemberInitials else (List.head membership.Members).FirstName
    Markup.Rooms.linkToRoom
        membership.RoomId
        [ Html.attr "class" membership.ClassNames
          Html.attr "id" (Campfire.Views.Helpers.Turbo.domId "rooms_direct" membership.RoomId (Some "list")) ]
        [ "data-sorted-list-number", membership.UpdatedAtEpoch ]
        []
        (avatars
         @ [ Elem.span
                 [ Attr.class' "direct__author flex align-center gap max-width min-width border-radius txt-small" ]
                 [ Elem.span
                       [ Attr.class' "txt-nowrap overflow-ellipsis" ]
                       [ Markup.Application.screenReader "Ping with"; Html.sp; Html.text label ] ] ])
