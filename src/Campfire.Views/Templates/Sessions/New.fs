// Port of rust/crates/views/templates/sessions/new.html (reference/app/views/sessions/new.html.erb)
/// `sessions/new.html.erb`: sign in.
module Campfire.Views.Templates.Sessions.New

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Layouts
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// One row of the form: the field's translation button and its label with the field and its icon.
let private row (ctx: ViewContext) (key: string) (field: XmlNode) (icon: string) : XmlNode =
    Elem.div
        [ Attr.class' "flex align-center gap" ]
        [ Translations.translationButton ctx key
          Elem.label
              [ Attr.class' "flex align-center gap input input--actor txt-large" ]
              [ field; Html.sp; Assets.imageSized ctx icon 24 [ hidden; Attr.class' "colorize--black" ] ] ]

/// The page: `@page_title`, `content_for :head` (`turbo_page_requires_reload`) and the form. `emailAddress` is
/// `params[:email_address]`; `helpContact` is `User.administrator.first`.
let rendered (ctx: ViewContext) (emailAddress: string option) (helpContact: HelpContact option) : ViewPage =
    let panel =
        if ctx.FlashAlert.IsSome then
            Attr.class' "panel shake"
        else
            Attr.class' "panel"
    { Regions =
        { Regions.content
            [ Elem.section
                  [ Attr.class' "txt-align-center" ]
                  ([ Elem.div
                         [ panel ]
                         [ Markup.Users.accountLogoTag ctx (Some "center margin-block-end txt-xx-large")
                           Forms.formWith
                               (ctx.Url(Routes.session ()))
                               "post"
                               [ Attr.class' "flex flex-column gap" ]
                               [ Elem.fieldset
                                     [ Attr.class' "flex flex-column gap center-block upad" ]
                                     [ Elem.legend
                                           [ Attr.class' "txt-large txt-align-center" ]
                                           [ Elem.strong [] [ Html.text ctx.Account.Name ] ]
                                       row
                                           ctx
                                           "email_address"
                                           (Forms.emailField
                                               ""
                                               "email_address"
                                               emailAddress
                                               [ Attr.create "required" "required"
                                                 Attr.class' "input"
                                                 Attr.create "autofocus" "autofocus"
                                                 Attr.create "autocomplete" "username"
                                                 Attr.create "placeholder" "Enter your email address" ])
                                           "email.svg"
                                       row
                                           ctx
                                           "password"
                                           (Forms.passwordField
                                               ""
                                               "password"
                                               [ Attr.create "required" "required"
                                                 Attr.class' "input"
                                                 Attr.create "autocomplete" "current-password"
                                                 Attr.create "placeholder" "Enter your password"
                                                 Attr.create "maxlength" "72"
                                                 Attr.create "size" "72" ])
                                           "password.svg"
                                       Elem.button
                                           [ Attr.create "name" "log_in"
                                             Attr.create "type" "submit"
                                             Attr.class' "btn btn--reversed center txt-large" ]
                                           [ Assets.image ctx "arrow-right.svg" [ hidden ]
                                             Html.sp
                                             Application.screenReader "Go" ] ] ] ] ]
                   @ Campfire.Views.Templates.Accounts._HelpContact.render ctx helpContact) ] with
            Title = Some "Sign in"
            Head = [ Elem.meta [ Attr.create "name" "turbo-visit-control"; Attr.create "content" "reload" ] ] }
      Fragments = [] }
