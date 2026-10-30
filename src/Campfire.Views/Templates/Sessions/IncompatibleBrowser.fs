// Port of rust/crates/views/templates/sessions/incompatible_browser.html (reference/app/views/sessions/incompatible_browser.html.erb)
/// `sessions/incompatible_browser.html.erb`, rendered by `AllowBrowser` for old browsers.
module Campfire.Views.Templates.Sessions.IncompatibleBrowser

open Falco.Markup
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup

/// The page: `@page_title` and the list of supported browsers.
let rendered (ctx: ViewContext) : ViewPage =
    { Regions =
        { Regions.content
            [ Elem.div
                  [ Attr.class' "panel center" ]
                  [ Elem.header
                        []
                        [ Elem.h1
                              [ Attr.class' "txt-x-large txt-tight-lines txt-align-center margin-none-block-start margin-block-end" ]
                              [ Text.raw "Upgrade to a supported web browser" ]
                          Elem.div
                              [ Attr.class' "flex align-start gap" ]
                              [ Translations.translationButton ctx "incompatible_browser_messsage"
                                Elem.p
                                    [ Attr.class' "margin-none-block-start" ]
                                    [ Text.raw
                                          "Campfire requires a modern web browser. Please use one of the browsers listed below and make sure auto-updates are enabled." ] ] ]
                    Elem.div
                        [ Attr.class' "browser-list flex align-center flex-wrap gap justify-center margin-block" ]
                        [ for (browser, version) in Campfire.Views.Sessions.allowBrowserVersions ->
                              Elem.div
                                  [ Attr.class' "browser flex flex-column" ]
                                  [ Assets.image ctx ("browsers/" + browser + ".svg") [ Attr.create "aria-hidden" "true"; Attr.class' "center" ]
                                    Elem.div
                                        [ Attr.class' "flex flex-column align-center margin-block-start-half" ]
                                        [ Elem.strong [] [ Html.text (Campfire.Views.Helpers.Application.capitalize browser) ]
                                          Html.sp
                                          Elem.span [] [ Text.raw " "; Html.text version; Text.raw "+" ] ] ] ] ] ] with
            Title = Some(if ctx.Platform.AppleMessages then "Campfire" else "Unsupported browser") }
      Fragments = [] }
