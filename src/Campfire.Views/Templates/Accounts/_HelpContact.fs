// Port of rust/crates/views/templates/accounts/_help_contact.html (reference/app/views/accounts/_help_contact.html.erb)
/// `accounts/_help_contact.html.erb`: how to reach the person to ask for help, on the sign-in and
/// sign-up pages. Nothing when there is nobody to ask.
module Campfire.Views.Templates.Accounts._HelpContact

open Falco.Markup
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Markup

let render (ctx: ViewContext) (helpContact: HelpContact option) : XmlNode list =
    match helpContact with
    | Some owner ->
        [ Elem.div
              [ Attr.class' "txt-align-center margin-block-double full-width" ]
              [ Links.linkTo
                    ("mailto:\"" + owner.Name + "\" <" + owner.EmailAddress + ">")
                    [ Attr.class' "btn center"; Html.attr "title" ("Email " + owner.Name) ]
                    [ Assets.image ctx "lifebuoy.svg" [ Attr.create "aria-hidden" "true" ]
                      Html.sp
                      Elem.span [] [ Html.text owner.EmailAddress ] ]
                Elem.div
                    [ Attr.class' "txt-align-center center margin-block txt-subtle" ]
                    [ Text.raw "Campfire&trade; version "; Application.versionBadge ctx ] ] ]
    | None -> []
