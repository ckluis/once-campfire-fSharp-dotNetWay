// `ApplicationHelper` and `RoomsHelper`'s navigation links as Falco.Markup functions (plans/design-views.md V13).
namespace Campfire.Views.Markup

open System.Text
open Falco.Markup
open Campfire.RailsCompat
open Campfire.Routes
open Campfire.Views

module Application =
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
