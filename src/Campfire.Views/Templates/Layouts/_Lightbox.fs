// reference/app/views/layouts/_lightbox.html.erb on Falco.Markup (plans/design-views.md unit 4.10)
module Campfire.Views.Templates.Layouts._Lightbox

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

let private screenReader (text: string) : XmlNode = Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text text ]

/// The dialog `lightbox#open` shows; its images are the icons of three buttons.
let render (ctx: ViewContext) : XmlNode =
    let icon (source: string) : XmlNode = Assets.image ctx source [ Attr.create "aria-hidden" "true" ]
    Elem.dialog
        [ Attr.class' "lightbox"
          Attr.create "aria-label" "Image Viewer (Press escape to close)"
          Attr.create "data-lightbox-target" "dialog"
          Attr.create "data-action" "close->lightbox#reset" ]
        [ Elem.img [ Attr.create "src" ""; Attr.class' "lightbox__image"; Attr.create "data-lightbox-target" "zoomedImage" ]
          // The ERB's line breaks stay as spaces: on a page that leaves a form open (`Html.unclosed`) the parser drops this
          // form, and its button sits beside the image and the links.
          Html.sp
          Elem.form
              [ Attr.create "method" "dialog"; Attr.class' "lightbox__btn" ]
              [ Elem.button [ Attr.class' "btn" ] [ icon "remove.svg"; Html.sp; screenReader "Close image viewer" ] ]
          Html.sp
          Elem.a
              [ Attr.create "href" ""
                Attr.class' "lightbox__btn--download btn hide-in-ios-pwa"
                Attr.create "data-lightbox-target" "download" ]
              [ icon "download.svg"; Html.sp; screenReader "Download file" ]
          Html.sp
          Elem.button
              [ Attr.class' "lightbox__btn--share btn"
                Attr.create "data-controller" "web-share"
                Attr.create "data-action" "web-share#share"
                Attr.create "data-web-share-files-value" ""
                Attr.create "data-lightbox-target" "share" ]
              [ icon "share.svg"; Html.sp; screenReader "Share file" ] ]
