// Port of rust/crates/views/templates/messages/boosts/_boost.html (reference/app/views/messages/boosts/_boost.html.erb)
/// `messages/boosts/_boost.html.erb`, whose body is `cache boost` (`BoostsCached.boost`); the boosts controller
/// also broadcasts it on its own. Two roots: the boost and the label screen readers read for it.
module Campfire.Views.Templates.Messages.Boosts._Boost

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let render (ctx: ViewContext) (boost: BoostView) : XmlNode list =
    [ Elem.div
          [ Html.attr "id" boost.DomId
            Attr.class' "boost boost-item flex-inline postion--relative max-width align-center fill-white gap"
            Attr.create "data-controller" "boost-delete"
            Attr.create "data-boost-delete-perform-class" "boost--deleting"
            Attr.create "data-boost-delete-reveal-class" "expanded"
            Html.attrInt "data-boost-delete-booster-id-value" boost.Booster.Id ]
          [ Elem.figure
                [ Attr.class' "avatar boost__avatar flex-item-no-shrink" ]
                [ Elem.a
                      [ Html.attr "title" boost.Booster.Title
                        Attr.class' "btn avatar"
                        Attr.create "data-turbo-frame" "_top"
                        Html.attr "href" boost.Booster.Path ]
                      [ Elem.img
                            [ Html.attr "aria-label" (boost.Booster.Name + " boosted " + boost.Content)
                              Html.attr "src" boost.Booster.AvatarUrl
                              Attr.create "width" "48"
                              Attr.create "height" "48" ] ] ]
            Elem.span
                [ Attr.create "role" "button"
                  (if boost.AllEmoji then Attr.class' "txt-small txt-medium" else Attr.class' "txt-small")
                  Attr.create "data-action" "click->boost-delete#reveal keydown.enter->boost-delete#reveal:prevent"
                  Attr.create "data-boost-delete-target" "content" ]
                [ Html.text boost.Content ]
            Forms.buttonTo
                boost.Path
                "delete"
                [ Attr.create "data-action" "boost-delete#perform"
                  Attr.create "data-boost-delete-target" "button"
                  Attr.class' "btn btn--negative flex-item-justify-end boost__delete" ]
                [ Assets.imageSized ctx "minus.svg" 20 [ Attr.create "aria-hidden" "true" ]
                  Html.sp
                  Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Delete this boost" ] ] ]
      Elem.span
          [ Attr.id "delete_boost_accessible_label"; Attr.class' "for-screen-reader" ]
          [ Text.raw "Press enter to delete this boost" ] ]
