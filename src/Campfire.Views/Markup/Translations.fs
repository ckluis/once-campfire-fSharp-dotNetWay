// `TranslationsHelper` as Falco.Markup functions (plans/design-views.md V13): the language popups beside form fields.
namespace Campfire.Views.Markup

open Falco.Markup
open Campfire.Views

module Translations =
    /// `translations_for(key)`.
    let translationsFor (key: string) : XmlNode =
        let entries =
            match Campfire.Views.Helpers.TranslationsTable.translations |> Array.tryFind (fun (name, _) -> name = key) with
            | Some(_, entries) -> entries
            | None -> failwith $"unknown translation key {key}"
        Elem.dl
            [ Attr.class' "language-list" ]
            [ for (language, translation) in entries do
                  Elem.dt [] [ Html.text language ]
                  Elem.dd [ Attr.class' "margin-none" ] [ Html.text translation ] ]

    /// `translation_button(key)`.
    let translationButton (ctx: ViewContext) (key: string) : XmlNode =
        Elem.details
            [ Attr.class' "position-relative"
              Attr.create "data-controller" "popup"
              Attr.create "data-action" "keydown.esc->popup#close toggle->popup#toggle click@document->popup#closeOnClickOutside"
              Attr.create "data-popup-orientation-top-class" "popup-orientation-top" ]
            [ Elem.summary
                  [ Attr.class' "btn"; Attr.create "tabindex" "-1" ]
                  [ Assets.imageSized ctx "globe.svg" 20 [ Attr.create "aria-hidden" "true"; Attr.class' "color-icon" ]
                    Application.screenReader "Translate" ]
              Elem.div
                  [ Attr.class' "language-list-menu shadow"; Attr.create "data-popup-target" "menu" ]
                  [ translationsFor key ] ]
