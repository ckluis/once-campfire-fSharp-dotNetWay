// Port of rust/crates/views/templates/users/new.html (reference/app/views/users/new.html.erb)
/// `users/new.html.erb` (the join page).
module Campfire.Views.Templates.Users.New

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Layouts
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// An icon beside a field: `size: 24`, black.
let private fieldIcon (ctx: ViewContext) (source: string) : XmlNode =
    Assets.imageSized ctx source 24 [ hidden; Attr.class' "colorize--black" ]

/// One row of the nametag: the field's translation button and its label.
let private row (ctx: ViewContext) (key: string) (field: XmlNode) (icon: string) : XmlNode =
    Elem.div
        [ Attr.class' "flex align-center gap" ]
        [ Translations.translationButton ctx key
          Elem.label
              [ Attr.class' "flex align-center gap flex-item-grow txt-large input input--actor" ]
              [ field; Html.sp; fieldIcon ctx icon ] ]

/// `content_for :nav`.
let private nav (ctx: ViewContext) : XmlNode list =
    [ Elem.div
          [ Attr.class' "flex-item-justify-end" ]
          [ Links.linkTo
                (Routes.newSession ())
                [ Attr.class' "btn flex-item-justify-end" ]
                [ Assets.image ctx "login-keys.svg" [ hidden ]; Html.sp; Application.screenReader "Sign in" ] ] ]

/// The page itself.
let private content (ctx: ViewContext) (joinCode: string) (helpContact: HelpContact option) : XmlNode list =
    [ Forms.formWith
          (Routes.join joinCode)
          "post"
          [ Attr.class' "center"; Attr.create "enctype" "multipart/form-data" ]
          [ Elem.section
                [ Attr.class' "nametag u-relative" ]
                [ Elem.div
                      [ Attr.class' "flex justify-center align-center pad-block" ]
                      [ Assets.image ctx "lanyard.svg" [ Attr.class' "nametag__lanyard"; hidden ] ]
                  Elem.div
                      [ Attr.class' "nametag__inner flex flex-column gap" ]
                      [ Elem.fieldset
                            [ Attr.class' "flex flex-column center-block" ]
                            [ Elem.legend
                                  [ Attr.class' "txt-align-center flex gap" ]
                                  [ Markup.Users.accountLogoTag ctx None
                                    Elem.strong [ Attr.class' "txt-large" ] [ Html.text ctx.Account.Name ] ]
                              Elem.label
                                  [ Attr.class' "align-center center avatar__form gap"; Attr.create "data-controller" "upload-preview" ]
                                  [ Elem.div
                                        [ Attr.class' "btn input--file" ]
                                        [ Assets.image ctx "camera.svg" [ hidden ]
                                          Html.sp
                                          Forms.fileField
                                              "user"
                                              "avatar"
                                              None
                                              [ Attr.class' "input"
                                                Attr.create "accept" "image/*"
                                                Attr.create "data-upload-preview-target" "input"
                                                Attr.create "data-action" "upload-preview#previewImage" ]
                                          Html.sp
                                          Application.screenReader "Upload avatar" ]
                                    Elem.div
                                        [ Attr.class' "btn avatar input--file txt-xx-large" ]
                                        [ Assets.image
                                              ctx
                                              "default-avatar.svg"
                                              [ hidden; Attr.create "data-upload-preview-target" "image" ]
                                          Html.sp
                                          Application.screenReader "Avatar" ] ] ]
                        row
                            ctx
                            "user_name"
                            (Forms.textField
                                "user"
                                "name"
                                None
                                [ Attr.class' "input"
                                  Attr.create "autocomplete" "name"
                                  Attr.create "placeholder" "Name"
                                  Attr.create "autofocus" "autofocus"
                                  Attr.create "required" "required"
                                  Attr.create "data-1p-ignore" "true" ])
                            "person.svg"
                        row
                            ctx
                            "email_address"
                            (Forms.emailField
                                "user"
                                "email_address"
                                None
                                [ Attr.class' "input"
                                  Attr.create "autocomplete" "username"
                                  Attr.create "placeholder" "Email address"
                                  Attr.create "required" "required" ])
                            "email.svg"
                        row
                            ctx
                            "password"
                            (Forms.passwordField
                                "user"
                                "password"
                                [ Attr.class' "input"
                                  Attr.create "autocomplete" "new-password"
                                  Attr.create "placeholder" "Password"
                                  Attr.create "required" "required"
                                  Attr.create "maxlength" "72"
                                  Attr.create "size" "72" ])
                            "password.svg"
                        Forms.buttonTag
                            [ Attr.class' "btn btn--reversed center txt-large" ]
                            [ Assets.image ctx "check.svg" [ hidden ]; Html.sp; Application.screenReader "Save" ] ] ] ]
      Legacy.node (fun w -> Campfire.Views.Templates.Accounts._HelpContact.render w ctx helpContact) ]

/// The page: `@page_title`, `@body_class`, the nav and the form.
let rendered (ctx: ViewContext) (joinCode: string) (helpContact: HelpContact option) : ViewPage =
    { Regions =
        { Regions.content (content ctx joinCode helpContact) with
            Title = Some "Sign up"
            BodyClass = Some "signup"
            Nav = nav ctx }
      Fragments = [] }
