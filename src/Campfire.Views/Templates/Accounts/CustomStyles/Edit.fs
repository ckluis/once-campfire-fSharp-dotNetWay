// Port of rust/crates/views/templates/accounts/custom_styles/edit.html (reference/app/views/accounts/custom_styles/edit.html.erb)
/// `accounts/custom_styles/edit.html.erb`: the account's custom CSS. The textarea is `Forms.textArea`: Rails writes a newline
/// right after the opening tag, so a value that starts with a newline keeps it (V4, T1).
module Campfire.Views.Templates.Accounts.CustomStyles.Edit

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup

/// The page: `@page_title`, the nav and the form.
let rendered (ctx: ViewContext) (customStyles: string option) : ViewPage =
    { Regions =
        { Regions.content
            [ Elem.section
                  [ Attr.class' "panel panel--wide txt-align-center flex flex-column position-relative"
                    Attr.create "style" "view-transition-name: custom-styles" ]
                  [ Forms.formWith
                        (ctx.Url(Routes.accountCustomStyles ()))
                        "patch"
                        [ Attr.class' "flex flex-column gap"
                          Attr.create "data-controller" "form"
                          Attr.create "data-action" "keydown.ctrl+enter->form#submit keydown.meta+enter->form#submit" ]
                        [ Elem.div [ Attr.class' "panel__button" ] [ Translations.translationButton ctx "custom_styles" ]
                          Elem.div
                              [ Attr.class' "pad-inline-double margin-inline" ]
                              [ Elem.h1 [ Attr.class' "margin-none" ] [ Text.raw "Custom CSS" ]
                                Elem.p
                                    [ Attr.class' "flex flex-wrap align-center justify-center gap margin-none-block-start"
                                      Attr.create "style" "--column-gap: 0.5ch; --row-gap: 0" ]
                                    [ Elem.span [] [ Text.raw "Add custom CSS styles." ]
                                      Html.sp
                                      Assets.imageSized
                                          ctx
                                          "alert.svg"
                                          16
                                          [ Attr.class' "flex-inline colorize--black"; Attr.create "aria-hidden" "true" ]
                                      Html.sp
                                      Elem.span [] [ Text.raw "Use Caution: you could break things." ] ] ]
                          Elem.label
                              [ Attr.class' "flex align-start gap flex-item-grow" ]
                              [ Forms.textArea
                                    "account"
                                    "custom_styles"
                                    customStyles
                                    [ Attr.class' "input input--code txt--small"
                                      Attr.create "placeholder" "Add CSS styles…"
                                      Attr.create "autocomplete" "off"
                                      Attr.create "spellcheck" "false"
                                      Attr.create "autocorrect" "off"
                                      Attr.create "autocapitalize" "off"
                                      Attr.create "rows" "16" ] ]
                          Html.sp
                          Forms.buttonTag
                              [ Attr.class' "btn btn--reversed center txt-large" ]
                              [ Assets.imageSized ctx "check.svg" 20 [ Attr.create "aria-hidden" "true" ]
                                Html.sp
                                Application.screenReader "Save changes" ] ] ] ] with
            Title = Some "Custom styles"
            Nav = [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBackTo ctx (Routes.editAccount ()) ] ] }
      Fragments = [] }
