// Port of rust/crates/views/templates/rooms/involvements/_bell.html (reference/app/views/rooms/involvements/_bell.html.erb)
/// `rooms/involvements/_bell.html.erb`: the notifications bell of a room's nav, and the dialog for blocked notifications.
module Campfire.Views.Templates.Rooms.Involvements._Bell

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let private icon (ctx: ViewContext) (name: string) (size: int) (options: XmlAttribute list) : XmlNode =
    Assets.imageSized ctx name size (Attr.create "aria-hidden" "true" :: options)

let render (ctx: ViewContext) (room: RoomView) : XmlNode =
    Elem.span
        []
        [ Elem.span
              [ Attr.class' "button_to_change_notifying"
                Attr.create "data-controller" "notifications"
                Html.attr "data-notifications-subscriptions-url-value" (Routes.userPushSubscriptions ())
                Attr.create "data-notifications-attention-class" "btn--pulsing" ]
              [ Turbo.frameTag
                    (roomDomId room.Kind room.Id "involvement")
                    None
                    None
                    [ Attr.create "data-controller" "turbo-frame"
                      Attr.create "data-action" "notifications:ready@window->turbo-frame#load"
                      Html.attr "data-turbo-frame-url-param" (Routes.roomInvolvement room.Id) ]
                    [ Elem.button
                          [ Attr.class' "btn"
                            Attr.create "data-action" "click->notifications#attemptToSubscribe"
                            Attr.create "data-notifications-target" "bell" ]
                          [ icon ctx "notification-bell-loading.svg" 20 []
                            Html.sp
                            icon ctx "notification-bell-alert.svg" 20 [ Attr.create "hidden" "hidden" ]
                            Html.sp
                            Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text ("Notification settings for this " + room.Noun) ] ] ]
                Elem.create
                    "dialog"
                    [ Attr.create "data-notifications-target" "notAllowedNotice"
                      Attr.class' "dialog pad center center-block border-radius border shadow"
                      Attr.create "style" "--inline-space: var(--block-space)" ]
                    [ Elem.div
                          [ Attr.class' "flex flex-column txt-align-center" ]
                          [ Elem.span
                                [ Attr.class' "btn btn--faux center txt-x-large" ]
                                [ icon ctx "notification-bell-alert.svg" 48 []
                                  Html.sp
                                  Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Notifications alert" ] ]
                            Elem.section
                                []
                                [ Elem.h1 [ Attr.class' "txt-large margin-none" ] [ Text.raw "Notifications aren’t allowed" ]
                                  Elem.div
                                      [ Attr.class' "txt-align-start margin-block-start" ]
                                      [ Legacy.node (fun w -> Templates.Pwa._BrowserSettings.render w ctx)
                                        Legacy.node (fun w -> Templates.Pwa._SystemSettings.render w ctx)
                                        Legacy.node (fun w -> Templates.Pwa._InstallInstructions.render w ctx) ] ]
                            Elem.form
                                [ Attr.create "method" "dialog"; Attr.class' "flex align-center gap center" ]
                                [ Elem.button
                                      [ Attr.class' "btn dialog__close"; Attr.create "autofocus" "true" ]
                                      [ Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Close" ]
                                        Html.sp
                                        icon ctx "remove.svg" 20 [] ] ] ] ] ] ]
