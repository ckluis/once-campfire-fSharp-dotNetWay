// Port of rust/crates/views/templates/users/push_subscriptions/index.html (reference/app/views/users/push_subscriptions/index.html.erb)
/// `users/push_subscriptions/index.html.erb`: the signed-in user's push subscriptions.
module Campfire.Views.Templates.Users.PushSubscriptions.Index

open Falco.Markup
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup
open Campfire.Views.Users

/// The page: `@page_title`, the nav and the list.
let rendered (ctx: ViewContext) (pushSubscriptions: PushSubscription list) : ViewPage =
    { Regions =
        { Regions.content
            [ Elem.section
                  [ Attr.class' "panel panel--wide flex flex-column gap" ]
                  [ Elem.h1 [ Attr.class' "txt-align-center txt-large margin-none" ] [ Text.raw "Push Notification Subscriptions" ]
                    Elem.div
                        [ Attr.class' "pad-inline fill-shade border-radius"; Attr.create "id" "push_subscriptions" ]
                        [ Elem.create
                              "menu"
                              [ Attr.class' "pad flex flex-column gap" ]
                              [ for pushSubscription in pushSubscriptions -> _PushSubscription.render ctx pushSubscription ] ] ] ] with
            Title = Some "Push notification subscriptions"
            Nav =
                [ Elem.div
                      [ Attr.class' "flex-item-justify-start" ]
                      [ Application.linkBackToLastRoomVisited ctx ] ] }
      Fragments = [] }
