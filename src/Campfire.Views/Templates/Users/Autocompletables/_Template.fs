// Port of rust/crates/views/templates/users/autocompletables/_template.html (reference/app/views/users/autocompletables/_template.html.erb)
/// `users/autocompletables/_template.html.erb`: the client-side template for a selected mention.
module Campfire.Views.Templates.Users.Autocompletables._Template

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

let render (ctx: ViewContext) : XmlNode =
    Elem.template
        [ Attr.id "autocompletable-user" ]
        [ Elem.div
              [ Attr.class' "autocomplete__pill max-width"; Attr.create "data-value" ""; Attr.create "tabindex" "0" ]
              [ Elem.img [ Attr.class' "avatar flex-item-no-shrink"; Attr.create "data-content" "avatar"; Attr.create "src" "" ]
                Html.sp
                Elem.span
                    [ Attr.class' "autocomplete-field__selected-value-text overflow-ellipsis flex-item-grow"
                      Attr.create "data-content" "label" ]
                    []
                Html.sp
                Elem.button
                    [ Attr.create "type" "button"
                      Attr.create "data-action" "autocomplete#remove:prevent"
                      Attr.create "data-value" ""
                      Attr.create "tabindex" "-1"
                      Attr.class' "btn btn--plain txt-small translucent flex-item-no-shrink" ]
                    [ Assets.image ctx "remove-circle.svg" [ Attr.create "aria-hidden" "true"; Attr.class' "colorize--black" ]
                      Html.sp
                      Elem.span
                          [ Attr.class' "for-screen-reader" ]
                          [ Text.raw "Remove "; Elem.span [ Attr.create "data-content" "screenReaderLabel" ] [] ] ] ] ]
