// Port of rust/crates/views/templates/users/profiles/_transfer.html (reference/app/views/users/profiles/_transfer.html.erb)
/// `users/profiles/_transfer.html.erb`: the link that signs a user in on another device.
module Campfire.Views.Templates.Users.Profiles._Transfer

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

let private icon (ctx: ViewContext) (source: string) (size: int) (classes: string) : XmlNode =
    Assets.imageSized ctx source size [ hidden; Html.attr "class" classes ]

/// The label of an action button comes first, then its icon.
let private labelled (ctx: ViewContext) (label: string) (source: string) (classes: string) : XmlNode list =
    [ Application.screenReader label; Html.sp; icon ctx source 20 classes ]

let render (ctx: ViewContext) (user: UserSummary) (transferId: string) : XmlNode =
    let url = ctx.Url(Routes.sessionTransfer transferId)
    Elem.fieldset
        []
        [ Elem.legend
              [ Attr.class' "gap" ]
              [ icon ctx "laptop.svg" 36 "colorize--black"
                Html.sp
                icon ctx "transfer.svg" 36 "colorize--black"
                Html.sp
                icon ctx "mobile-phone.svg" 36 "colorize--black" ]
          Elem.div
              [ Attr.class' "flex flex-column gap" ]
              [ if not (ctx.IsCurrentUser user.Id) then
                    Elem.div
                        [ Attr.class' "flex align-center gap justify-center" ]
                        [ Assets.imageSized
                              ctx
                              "crown.svg"
                              16
                              [ hidden; Attr.class' "flex-item-no-shrink colorize--black" ]
                          Html.sp
                          Elem.label
                              [ Attr.create "for" "session_transfer_url" ]
                              [ Text.raw "Share to get them back into their account" ] ]
                else
                    Elem.label
                        [ Attr.create "for" "session_transfer_url"; Attr.class' "for-screen-reader" ]
                        [ Text.raw "Use this link to login automatically on another device" ]
                    Html.sp
                Elem.input
                    [ Attr.create "type" "text"
                      Attr.class' "input"
                      Html.attr "value" url
                      Attr.create "id" "session_transfer_url"
                      Attr.readonly ]
                Elem.div
                    [ Attr.class' "flex align-center center gap" ]
                    [ Application.linkToZoomQrCode url (labelled ctx "Show auto-login QR code" "qr-code.svg" "colorize--black")
                      Html.sp
                      Application.buttonToCopyToClipboard
                          url
                          (labelled ctx "Copy auto-login link" "copy-paste.svg" "flex-item-no-shrink colorize--black")
                      Html.sp
                      Application.webShareSessionButton
                          url
                          "Your sign-in link"
                          "This is your own private sign-in URL, DO NOT SHARE IT. Use it to sign-in on another device or if you get locked out."
                          (labelled ctx "Share auto-login link" "share.svg" "flex-item-no-shrink colorize--black") ] ] ]
