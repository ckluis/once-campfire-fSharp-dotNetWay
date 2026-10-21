// `ApplicationHelper` and `RoomsHelper`'s navigation links as Falco.Markup functions (plans/design-views.md V13).
namespace Campfire.Views.Markup

open System.Text
open Falco.Markup
open Campfire.RailsCompat
open Campfire.Routes
open Campfire.Views

module Application =
    /// `page_title_tag`: `@page_title || "Campfire"`.
    let pageTitleTag (pageTitle: string option) : XmlNode = Elem.title [] [ Html.text (defaultArg pageTitle "Campfire") ]

    /// `current_user_meta_tags`.
    let currentUserMetaTags (ctx: ViewContext) : XmlNode list =
        match ctx.CurrentUser with
        | Some user ->
            [ Elem.meta [ Attr.create "name" "current-user-id"; Html.attrInt "content" user.Id ]
              Elem.meta [ Attr.create "name" "current-user-name"; Html.attr "content" user.Name ] ]
        | None -> []

    /// `script_aware_action_cable_meta_tag`.
    let scriptAwareActionCableMetaTag (ctx: ViewContext) : XmlNode =
        Elem.meta [ Attr.create "name" "action-cable-url"; Html.attr "content" ctx.CableUrl ]

    /// `custom_styles_tag`: the account's CSS, written as it is typed (`html_safe` in Rails), inside its `style` element.
    let customStylesTag (ctx: ViewContext) : XmlNode list =
        match ctx.CustomStyles with
        | Some styles ->
            [ Elem.style [ Attr.create "data-turbo-track" "reload" ] [ Html.trusted (Html.Trusted.ofAccountStyles styles) ] ]
        | None -> []

    /// `body_classes`: `[ @body_class, admin_body_class, account_logo_body_class ].compact.join(" ")`.
    let bodyClasses (ctx: ViewContext) (bodyClass: string option) : string =
        let admin = if ctx.CanAdminister then Some "admin" else None
        let logo = if ctx.Account.HasLogo then Some "account-has-logo" else None
        System.String.Join(" ", [ bodyClass; admin; logo ] |> List.choose id)

    /// `tag.span(text, class: "for-screen-reader")`.
    let screenReader (text: string) : XmlNode = Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text text ]

    /// `link_back_to(destination)`: an arrow and "Go Back", with no whitespace between.
    let linkBackTo (ctx: ViewContext) (destination: string) : XmlNode =
        Links.linkTo
            destination
            [ Attr.class' "btn" ]
            [ Assets.imageSized ctx "arrow-left.svg" 20 [ Attr.create "aria-hidden" "true" ]; screenReader "Go Back" ]

    /// `link_back_to_last_room_visited`.
    let linkBackToLastRoomVisited (ctx: ViewContext) : XmlNode =
        match ctx.LastRoomVisitedId with
        | Some roomId -> linkBackTo ctx (Routes.room roomId)
        | None -> linkBackTo ctx (Routes.root ())

    /// `version_badge`.
    let versionBadge (ctx: ViewContext) : XmlNode = Elem.span [ Attr.class' "version-badge" ] [ Html.text ctx.AppVersion ]

    /// `link_back`: to the referrer, unless it is missing or the current page.
    let linkBack (ctx: ViewContext) : XmlNode =
        match ctx.Referrer with
        | Some referrer when referrer <> ctx.RequestUrl -> linkBackTo ctx referrer
        | _ -> linkBackTo ctx (Routes.root ())

    /// `button_to_copy_to_clipboard(url) { content }`.
    let buttonToCopyToClipboard (url: string) (content: XmlNode list) : XmlNode =
        Elem.button
            [ Attr.class' "btn"
              Attr.create "data-controller" "copy-to-clipboard"
              Attr.create "data-action" "copy-to-clipboard#copy"
              Attr.create "data-copy-to-clipboard-success-class" "btn--success"
              Html.attr "data-copy-to-clipboard-content-value" url ]
            content

    /// `link_to_zoom_qr_code(url) { content }`: the QR code route takes the URL, `Base64.urlsafe_encode64`d.
    let linkToZoomQrCode (url: string) (content: XmlNode list) : XmlNode =
        let path = Routes.qrCode (RailsEncoding.urlsafeEncodePadded (Encoding.UTF8.GetBytes url))
        Links.linkTo
            path
            [ Attr.class' "btn"
              Attr.create "data-lightbox-target" "image"
              Attr.create "data-action" "lightbox#open"
              Html.attr "data-lightbox-url-value" path ]
            content

    /// `web_share_session_button(url, title, text) { content }`.
    let webShareSessionButton (url: string) (title: string) (text: string) (content: XmlNode list) : XmlNode =
        Elem.button
            [ Attr.class' "btn"
              Attr.create "hidden" "hidden"
              Attr.create "data-controller" "web-share"
              Attr.create "data-action" "web-share#share"
              Html.attr "data-web-share-url-value" url
              Html.attr "data-web-share-text-value" text
              Html.attr "data-web-share-title-value" title ]
            content
