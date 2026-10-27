// Port of rust/crates/views/templates/users/push_subscriptions/_push_subscription.html (reference/app/views/users/push_subscriptions/_push_subscription.html.erb)
/// `users/push_subscriptions/_push_subscription.html.erb`: a device's push subscription.
module Campfire.Views.Templates.Users.PushSubscriptions._PushSubscription

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

let render (ctx: ViewContext) (pushSubscription: PushSubscription) : XmlNode =
    Elem.li
        [ Attr.class' "flex flex-column margin-none membership-item" ]
        [ Elem.span
              [ Attr.class' "overflow-ellipsis txt-primary txt-undecorated" ]
              [ Elem.strong
                    []
                    [ Html.text pushSubscription.Browser
                      Text.raw " "
                      Html.text pushSubscription.Version
                      Text.raw " on "
                      Html.text pushSubscription.Platform ]
                Elem.br [] ]
          Html.sp
          Elem.span
              [ Attr.class' "flex align-start gap txt-small" ]
              [ Elem.span [] [ Html.text pushSubscription.Endpoint ]
                Html.sp
                Elem.span
                    [ Attr.class' "flex align-center gap" ]
                    [ Forms.buttonTo
                          (Routes.userPushSubscriptionTestNotifications pushSubscription.Id)
                          "post"
                          [ Attr.class' "btn btn--reversed" ]
                          [ Assets.imageSized ctx "notification-bell-everything.svg" 20 [ hidden ]
                            Html.sp
                            Application.screenReader "Send test notification" ]
                      Forms.buttonTo
                          (Routes.userPushSubscription pushSubscription.Id)
                          "delete"
                          [ Attr.class' "btn btn--negative" ]
                          [ Assets.imageSized ctx "minus.svg" 20 [ hidden ]
                            Html.sp
                            Application.screenReader "Delete subscription" ] ] ] ]
