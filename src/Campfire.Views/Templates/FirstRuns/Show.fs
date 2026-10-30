// Port of rust/crates/views/templates/first_runs/show.html (reference/app/views/first_runs/show.html.erb)
/// `first_runs/show.html.erb`: account setup, shown until the first user exists.
module Campfire.Views.Templates.FirstRuns.Show

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

let private title = "Set up Campfire"

/// One row of the nametag: the field's translation button and its label with the field and its icon.
let private row (ctx: ViewContext) (key: string) (field: XmlNode) (icon: string) : XmlNode =
    Elem.div
        [ Attr.class' "flex align-center gap" ]
        [ Translations.translationButton ctx key
          Elem.label
              [ Attr.class' "flex align-center gap flex-item-grow txt-large input input--actor" ]
              [ field; Html.sp; Assets.imageSized ctx icon 24 [ hidden; Attr.class' "colorize--black" ] ] ]

/// The page: `@page_title`, `@body_class` and the nametag form.
let rendered (ctx: ViewContext) : ViewPage =
    { Regions =
        { Regions.content
            [ Forms.formWith
                  (Routes.firstRun ())
                  "post"
                  [ Attr.class' "center max-width"; Attr.create "enctype" "multipart/form-data" ]
                  [ Elem.section
                        [ Attr.class' "nametag u-relative" ]
                        [ Elem.div
                              [ Attr.class' "flex justify-center align-center pad-block" ]
                              [ Assets.image ctx "lanyard.svg" [ Attr.class' "nametag__lanyard"; hidden ] ]
                          Elem.div
                              [ Attr.class' "nametag__inner flex flex-column gap" ]
                              [ Elem.fieldset
                                    [ Attr.class' "flex flex-column center-block" ]
                                    [ Elem.legend [ Attr.class' "txt-large txt-align-center" ] [ Elem.strong [] [ Html.text title ] ]
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
                                                  Application.screenReader "Add your avatar" ]
                                            Elem.div
                                                [ Attr.class' "btn avatar input--file txt-xx-large" ]
                                                [ Assets.image
                                                      ctx
                                                      "default-avatar.svg"
                                                      [ hidden
                                                        Attr.create "data-upload-preview-target" "image"
                                                        Attr.create "alt" "Add your avatar" ]
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
                                    [ Assets.image ctx "arrow-right.svg" [ hidden ]
                                      Html.sp
                                      Application.screenReader "Save" ] ] ] ] ] with
            Title = Some title
            BodyClass = Some "signup" }
      Fragments = [] }
