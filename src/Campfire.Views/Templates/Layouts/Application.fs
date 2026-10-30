// reference/app/views/layouts/application.html.erb on Falco.Markup (plans/design-views.md V8, unit 4.10)
/// The application layout. A page gives it what the ERB sets as `@page_title` and `@body_class` and the regions it fills
/// (`content_for`'s `head`, `nav`, `footer` and `sidebar`, and the page itself as `content`), each a list of nodes.
module Campfire.Views.Templates.Layouts.Application

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

/// The head's own tags, before the page's `head` region. The stylesheet and import map tags are one trusted string each.
let private headTags (ctx: ViewContext) (pageTitle: string option) : XmlNode list =
    [ Application.pageTitleTag pageTitle
      Elem.meta
          [ Attr.create "name" "viewport"
            Attr.create "content" "width=device-width, initial-scale=1, user-scalable=no, interactive-widget=resizes-content" ]
      Elem.meta [ Attr.create "name" "view-transition"; Attr.create "content" "same-origin" ]
      Elem.meta [ Attr.create "name" "color-scheme"; Attr.create "content" "light dark" ]
      Elem.meta
          [ Attr.create "name" "theme-color"
            Attr.create "content" "#ffffff"
            Attr.create "media" "(prefers-color-scheme: light)" ]
      Elem.meta
          [ Attr.create "name" "theme-color"
            Attr.create "content" "#000000"
            Attr.create "media" "(prefers-color-scheme: dark)" ]
      Elem.meta [ Attr.create "name" "apple-mobile-web-app-capable"; Attr.create "content" "yes" ] ]
    @ Application.currentUserMetaTags ctx
    @ [ Application.scriptAwareActionCableMetaTag ctx
        Elem.meta (
            Attr.create "name" "vapid-public-key"
            :: (match ctx.VapidPublicKey with
                | Some key -> [ Html.attr "content" key ]
                | None -> [])
        )
        Elem.meta [ Attr.create "name" "turbo-prefetch"; Attr.create "content" "true" ]
        Elem.link [ Attr.create "rel" "manifest"; Attr.create "href" "/webmanifest.json" ]
        Elem.link [ Attr.create "rel" "icon"; Html.attr "href" ctx.Account.LogoUrl; Attr.create "type" "image/png" ]
        Elem.link [ Attr.create "rel" "apple-touch-icon"; Html.attr "href" ctx.Account.LogoUrl ]
        Html.trusted ctx.StylesheetTags ]
    @ Application.customStylesTag ctx
    @ [ Html.trusted ctx.ImportmapTags ]

/// A region as the ERB writes it: on a line of its own, so a space touches each edge (V4: it shows when the region starts or
/// ends with text or an inline element).
let private region (nodes: XmlNode list) : XmlNode list = (Html.sp :: nodes) @ [ Html.sp ]

/// The flash: `notice`, or `alert` when there is no notice (`flash[:notice] || flash[:alert]`), as a toast and a live region.
let private flash (ctx: ViewContext) : XmlNode list =
    let notice =
        match ctx.FlashNotice with
        | Some notice -> Some notice
        | None -> ctx.FlashAlert
    match notice with
    | Some notice ->
        let alert = ctx.FlashAlert.IsSome
        [ Elem.div
              [ Attr.class' "flash"
                Attr.create "data-controller" "element-removal"
                Attr.create "data-action" "animationend->element-removal#remove" ]
              [ Elem.div
                    [ Attr.class' "flash__inner shadow"
                      (if alert then Attr.create "style" "--flash-background: var(--color-negative)" else Attr.create "style" "") ]
                    [ Assets.imageSized
                          ctx
                          (if alert then "alert.svg" else "check.svg")
                          24
                          [ Attr.create "aria-hidden" "true"; Attr.class' "colorize--white" ] ]
                Elem.span
                    [ Attr.class' "for-screen-reader"; Attr.create "role" "alert"; Attr.create "aria-atomic" "true" ]
                    [ Html.text notice ] ] ]
    | None -> []

let render
    (ctx: ViewContext)
    (pageTitle: string option)
    (bodyClass: string option)
    (head: XmlNode list)
    (nav: XmlNode list)
    (content: XmlNode list)
    (footer: XmlNode list)
    (sidebar: XmlNode list)
    : XmlNode =
    Elem.html
        []
        [ Elem.head [] (headTags ctx pageTitle @ head @ [ Html.sp ])
          Elem.body
              [ Html.attr "class" (Application.bodyClasses ctx bodyClass); Attr.create "data-controller" "local-time lightbox" ]
              ([ Elem.a [ Attr.create "href" "#main-content"; Attr.class' "skip-navigation btn" ] [ Text.raw "Skip to main content" ]
                 Elem.nav [ Attr.create "id" "nav" ] (region nav) ]
               @ flash ctx
               @ [ Elem.main [ Attr.create "id" "main-content" ] (region content @ [ Elem.footer [ Attr.create "id" "footer" ] (region footer) ])
                   Elem.aside
                       [ Attr.create "id" "sidebar"
                         Attr.create "data-controller" "toggle-class"
                         Attr.create "data-toggle-class-toggle-class" "open" ]
                       (region sidebar)
                   _Lightbox.render ctx
                   Elem.a
                       [ Attr.create "href" "https://once.com"
                         Attr.create "id" "app-logo"
                         Attr.create "target" "_blank"
                         Attr.create "aria-label" "Once software from 37signals home page" ]
                       [ Assets.image
                             ctx
                             "campfire-icon.png"
                             [ Attr.create "alt" "Campfire logo"; Attr.create "width" "256"; Attr.create "height" "216" ] ] ]) ]
