// Port of rust/crates/views/templates/accounts/_invite.html (reference/app/views/accounts/_invite.html.erb)
/// `accounts/_invite.html.erb`: the join link with its QR code, copy and share buttons, on the
/// account page and the original room's invitation.
module Campfire.Views.Templates.Accounts._Invite

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Markup

let private icon (ctx: ViewContext) (source: string) : XmlNode =
    Assets.imageSized ctx source 20 [ Attr.create "aria-hidden" "true"; Attr.class' "colorize--black" ]

/// A button's screen-reader label, then its icon.
let private labelled (ctx: ViewContext) (label: string) (source: string) : XmlNode list =
    [ Application.screenReader label; Html.sp; icon ctx source ]

let render (ctx: ViewContext) (joinCode: string) : XmlNode =
    let url = ctx.Url(Routes.join joinCode)
    Elem.div
        [ Attr.class' "flex flex-column align-center gap" ]
        [ Elem.label
              [ Attr.class' "flex flex-column gap full-width"; Attr.create "style" "--row-gap: 0.5em" ]
              [ Elem.strong [ Attr.id "invite_label"; Attr.class' "invite-label" ] [ Text.raw "Share to invite more people" ]
                Html.sp
                Elem.span
                    [ Attr.class' "flex align-center gap input input--actor fill-white" ]
                    [ Assets.imageSized ctx "person-add.svg" 20 [ Attr.create "aria-hidden" "true"; Attr.class' "colorize--black" ]
                      Html.sp
                      Elem.input
                          [ Attr.create "type" "text"
                            Attr.class' "input"
                            Attr.id "invite_url"
                            Html.attr "value" url
                            Attr.create "aria-labelledby" "invite_label"
                            Attr.readonly ] ] ]
          Elem.div
              [ Attr.class' "flex align-center gap" ]
              [ Application.linkToZoomQrCode url (labelled ctx "Show join link QR code" "qr-code.svg")
                Html.sp
                Application.buttonToCopyToClipboard url (labelled ctx "Copy join link" "copy-paste.svg")
                Html.sp
                Application.webShareSessionButton
                    url
                    "Link to join Campfire"
                    "Hit this link to join me in Campfire and start chatting."
                    (labelled ctx "Share join link" "share.svg")
                if ctx.CanAdminister then
                    Forms.buttonTo
                        (Routes.accountJoinCode ())
                        "post"
                        [ Attr.class' "btn btn--regenerate" ]
                        [ icon ctx "refresh.svg"; Html.sp; Application.screenReader "Regenerate join link" ] ] ]
