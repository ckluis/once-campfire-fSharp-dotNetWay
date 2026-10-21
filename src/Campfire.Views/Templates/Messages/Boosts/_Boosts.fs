// Port of rust/crates/views/templates/messages/boosts/_boosts.html (reference/app/views/messages/boosts/_boosts.html.erb)
/// `messages/boosts/_boosts.html.erb`: a message's boosts and the "Add a boost" link.
module Campfire.Views.Templates.Messages.Boosts._Boosts

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

/// `boosts` are the message's boosts' fragments, fetched up front (`BoostsCached.boosts`), in order.
let render (ctx: ViewContext) (message: MessageView) (boosts: CachedFragment[]) : XmlNode =
    Elem.create
        "turbo-frame"
        [ Html.attr "id" ("boosting_message_" + message.ClientMessageId) ]
        [ Elem.div
              [ Attr.class' "boosts flex flex-wrap align-center gap full-width"
                Attr.create "style" "--column-gap: 0.4ch; --row-gap: 0"
                Attr.create "data-controller" "turbo-streaming"
                Attr.create "data-action" "turbo:submit-start->turbo-streaming#unsubscribe" ]
              [ Elem.div
                    [ Attr.class' "flex-inline flex-wrap gap"
                      Html.attr "id" ("boosts_message_" + message.ClientMessageId)
                      Attr.create "data-turbo-streaming-target" "container" ]
                    [ for boost in boosts -> Html.fragment boost ]
                Elem.create
                    "turbo-frame"
                    [ Html.attr "id" ("new_boost_message_" + message.ClientMessageId) ]
                    [ Elem.div
                          [ Attr.class' "flex-inline message__boost-inline"; Attr.create "data-controller" "soft-keyboard" ]
                          [ Links.linkTo
                                message.NewBoostPath
                                [ Attr.class' "boost__action txt-small btn"; Attr.create "action" "soft-keyboard#open" ]
                                [ Assets.imageSized ctx "boost.svg" 20 [ Attr.create "aria-hidden" "true" ]
                                  Html.sp
                                  Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Add a boost" ] ] ] ] ] ]
