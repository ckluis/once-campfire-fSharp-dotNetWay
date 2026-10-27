// Port of rust/crates/views/templates/users/sidebars/show.html (reference/app/views/users/sidebars/show.html.erb)
/// `users/sidebars/show.html.erb`: the sidebar, in its own Turbo Frame (the layout is the page's `content`).
module Campfire.Views.Templates.Users.Sidebars.Show

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup
open Campfire.Views.Templates.Users.Sidebars.Rooms
open Campfire.Views.Users

/// Items the ERB separates with whitespace, which renders between inline-level siblings.
let private spaced (nodes: XmlNode list) : XmlNode list =
    nodes |> List.mapi (fun index node -> if index = 0 then [ node ] else [ Html.sp; node ]) |> List.concat

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

let private content (ctx: ViewContext) (sidebar: SidebarShow) : XmlNode =
    Markup.Users.sidebarFrameWith
        None
        [ Turbo.streamFrom sidebar.RoomsStream
          Html.sp
          Turbo.streamFrom sidebar.UserRoomsStream
          Elem.div
              [ Attr.class' "sidebar__container overflow-y overflow-hide-scrollbar"
                Attr.create "data-controller" "badge-dot"
                Attr.create "data-badge-dot-unread-class" "unread"
                Attr.create
                    "data-action"
                    "rooms-list:unread@window->badge-dot#update rooms-list:read@window->badge-dot#update turbo:submit-start->turbo-frame#unpermanize" ]
              [ Elem.create
                    "turbo-frame"
                    [ Attr.id "direct_rooms_control"; Attr.create "target" "_top" ]
                    [ Elem.div
                          [ Attr.class' "directs gap overflow-x overflow-hide-scrollbar" ]
                          [ Links.linkTo
                                (Routes.newRoomsDirect ())
                                [ Attr.class' "direct direct__new"; Attr.create "data-turbo-frame" "_self" ]
                                [ Elem.span
                                      [ Attr.class' "avatar avatar--icon" ]
                                      [ Assets.imageSized ctx "messages-add.svg" 20 [ hidden; Attr.class' "colorize--black" ] ]
                                  Html.sp
                                  Elem.span
                                      [ Attr.class' "direct__author flex max-width min-width border-radius pad-inline-half" ]
                                      [ Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "New" ]
                                        Html.sp
                                        Elem.span [ Attr.class' "txt-small overflow-clip" ] [ Text.raw "Ping" ] ] ]
                            Elem.div
                                [ Attr.id "direct_rooms"
                                  Attr.create "contents" ""
                                  Attr.create "data-controller" "sorted-list"
                                  Attr.create "data-action" "rooms-list:unread@window->sorted-list#updateItem" ]
                                (spaced [ for membership in sidebar.DirectMemberships -> Html.fragment membership.Html ])
                            Elem.div
                                [ Attr.create "contents" "" ]
                                [ for user in sidebar.DirectPlaceholderUsers -> _DirectPlaceholder.render ctx user ] ] ]
                Elem.div
                    [ Attr.class' "rooms position-relative flex flex-column gap" ]
                    [ Elem.div
                          [ Attr.id "shared_rooms"; Attr.create "contents" ""; Attr.create "data-controller" "sorted-list" ]
                          (spaced [ for room in sidebar.OtherMemberships -> _Shared.render room ])
                      if sidebar.CanCreateRooms then
                          Links.linkTo
                              (Routes.newRoomsOpen ())
                              [ Attr.class' "rooms__new-btn btn room align-center gap txt-reversed"
                                Attr.create "aria-label" "New Chat Room" ]
                              [ Assets.imageSized
                                    ctx
                                    "add.svg"
                                    20
                                    [ hidden; Attr.create "style" "view-transition-name: new-room" ] ] ]
                Elem.button
                    [ Attr.class' "btn sidebar__toggle"; Attr.create "data-action" "toggle-class#toggle" ]
                    [ Assets.imageSized ctx "menu.svg" 20 [ hidden ]
                      Html.sp
                      Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Open menu" ] ] ]
          Elem.div
              [ Attr.class' "flex align-end sidebar__tools gap justify-end" ]
              [ Links.linkTo
                    (Routes.userProfile ())
                    [ Attr.class' "btn avatar flex-item-no-shrink sidebar__tool" ]
                    [ Assets.imageSized
                          ctx
                          sidebar.CurrentUser.AvatarPath
                          48
                          [ hidden; Html.attr "style" ("view-transition-name: avatar-" + string sidebar.CurrentUser.Id) ]
                      Html.sp
                      Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "My Settings" ] ]
                Html.sp
                Links.linkTo
                    (Routes.editAccount ())
                    [ Attr.class' "btn align-center gap txt-reversed sidebar__tool" ]
                    [ Assets.imageSized ctx "settings.svg" 20 [ hidden; Attr.create "style" "view-transition-name: account-settings" ]
                      Html.sp
                      Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Account Settings" ] ] ] ]

/// The page: the sidebar frame as the layout's content, with the direct rooms' fragments it places.
let rendered (ctx: ViewContext) (sidebar: SidebarShow) : ViewPage =
    { Regions = Regions.content [ content ctx sidebar ]
      Fragments = [ for membership in sidebar.DirectMemberships -> membership.Html ] }
