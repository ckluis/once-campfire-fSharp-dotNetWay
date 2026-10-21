// Port of rust/crates/views/templates/users/profiles/show.html (reference/app/views/users/profiles/show.html.erb)
/// `users/profiles/show.html.erb`: the signed-in user's own profile.
module Campfire.Views.Templates.Users.Profiles.Show

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup
open Campfire.Views.Users

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// `profile_form_with(@user, **params)`: a patch to the profile, handled by the `form` controller.
let private profileForm (options: XmlAttribute list) (content: XmlNode list) : XmlNode =
    Forms.formWith (Routes.userProfile ()) "patch" (options @ [ Attr.create "data-controller" "form" ]) content

/// `content_for :nav`.
let private nav (ctx: ViewContext) : XmlNode list =
    [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBack ctx ]
      Elem.div
          [ Attr.class' "flex-item-justify-end" ]
          [ Forms.formWith
                (Routes.session ())
                "delete"
                [ Attr.create "data-controller" "sessions" ]
                [ Elem.input
                      [ Attr.create "type" "hidden"
                        Attr.create "name" "push_subscription_endpoint"
                        Attr.create "id" "push_subscription_endpoint"
                        Attr.create "data-sessions-target" "pushSubscriptionEndpoint" ]
                  Elem.button
                      [ Attr.class' "btn"; Attr.create "data-action" "sessions#logout:prevent" ]
                      [ Assets.image ctx "logout.svg" [ hidden ]; Html.sp; Application.screenReader "Log out" ] ] ] ]

/// The avatar's file field, which submits the form on change.
let private avatarFile: XmlNode =
    Forms.fileField
        "user"
        "avatar"
        (Some "file")
        [ Attr.class' "input"
          Attr.create "accept" "image/*"
          Attr.create "data-upload-preview-target" "input"
          Attr.create "data-action" "upload-preview#previewImage change->form#submit" ]

let private multipart: XmlAttribute = Attr.create "enctype" "multipart/form-data"

/// A labelled field with an icon, as the profile form lays them out.
let private field (ctx: ViewContext) (key: string) (rowClass: string) (labelClass: string) (input: XmlNode) (icon: string) : XmlNode =
    Elem.div
        [ Html.attr "class" rowClass ]
        [ Translations.translationButton ctx key
          Elem.label
              [ Html.attr "class" labelClass ]
              [ input
                Html.sp
                Assets.imageSized ctx icon 24 [ hidden; Attr.class' "colorize--black" ] ] ]

/// The page itself.
let private content (ctx: ViewContext) (profile: ProfileShow) : XmlNode =
    let user = profile.User
    Elem.section
        [ Attr.class' "panel flex flex-column gap"; Html.attr "style" ("view-transition-name: avatar-" + string user.Id) ]
        [ Legacy.node (fun w -> Campfire.Views.Templates.Pwa._InstallInstructions.render w ctx)
          Elem.div
              [ Attr.class' "align-center center avatar__form gap"; Attr.create "data-controller" "upload-preview" ]
              [ profileForm
                    [ Attr.class' "txt-medium"; multipart ]
                    [ Elem.label
                          [ Attr.class' "btn input--file" ]
                          [ Assets.imageSized ctx "camera.svg" 20 [ hidden ]
                            Html.sp
                            avatarFile
                            Html.sp
                            Application.screenReader "Upload avatar" ] ]
                profileForm
                    [ multipart ]
                    [ Elem.label
                          [ Attr.class' "btn avatar input--file txt-xx-large" ]
                          [ Assets.imageSized
                                ctx
                                user.AvatarPath
                                300
                                [ hidden; Attr.create "data-upload-preview-target" "image" ]
                            Html.sp
                            avatarFile
                            Html.sp
                            Application.screenReader "Avatar" ] ]
                if profile.AvatarAttached then
                    Forms.buttonTo
                        (Routes.userAvatar user.Id)
                        "delete"
                        [ Attr.class' "btn btn--negative txt-small avatar__delete-btn" ]
                        [ Assets.imageSized ctx "minus.svg" 20 [ hidden ]
                          Html.sp
                          Application.screenReader "Delete avatar" ] ]
          profileForm
              []
              [ Elem.div
                    [ Attr.class' "flex flex-column gap" ]
                    [ field
                          ctx
                          "user_name"
                          "flex align-center gap"
                          "flex align-center gap flex-item-grow input input--actor"
                          (Forms.textField
                              "user"
                              "name"
                              (Some user.Name)
                              [ Attr.class' "input txt-large "
                                Attr.create "autocomplete" "name"
                                Attr.create "placeholder" "Enter your name"
                                Attr.create "autofocus" "autofocus"
                                Attr.create "required" "required"
                                Attr.create "data-1p-ignore" "true" ])
                          "person.svg"
                      field
                          ctx
                          "email_address"
                          "flex align-center gap"
                          "flex align-center gap flex-item-grow input input--actor"
                          (Forms.emailField
                              "user"
                              "email_address"
                              None
                              [ Attr.class' "input txt-large"
                                match user.EmailAddress with
                                | Some email -> Html.attr "value" email
                                | None -> ()
                                Attr.create "autocomplete" "username"
                                Attr.create "placeholder" "Enter your email address" ])
                          "email.svg"
                      field
                          ctx
                          "update_password"
                          "flex align-center gap"
                          "flex align-center gap flex-item-grow input input--actor"
                          (Forms.passwordField
                              "user"
                              "password"
                              [ Attr.class' "input txt-large"
                                Attr.create "autocomplete" "new-password"
                                Attr.create "placeholder" "Change password"
                                Attr.create "maxlength" "72"
                                Attr.create "size" "72" ])
                          "password.svg"
                      field
                          ctx
                          "bio"
                          "flex align-start gap"
                          "flex align--center gap flex-item--grow input input--actor"
                          (Forms.textArea
                              "user"
                              "bio"
                              user.Bio
                              [ Attr.class' "input txt-large"
                                Attr.create "placeholder" "A few words about yourself…"
                                Attr.create "maxlength" "200"
                                Attr.create "rows" "3" ])
                          "bio.svg"
                      Markup.Users.profileFormSubmitButton ctx ] ]
          Elem.div
              [ Attr.class' "margin-block pad-inline pad-block fill-shade border-radius" ]
              [ Elem.create
                    "menu"
                    [ Attr.class' "flex flex-column gap margin-none pad" ]
                    [ for membership in profile.SharedMemberships do
                          _Membership.render ctx membership
                      if not profile.DirectMemberships.IsEmpty && not profile.SharedMemberships.IsEmpty then
                          Elem.hr [ Attr.class' "separator full-width"; Attr.create "style" "--border-style: solid" ]
                      for membership in profile.DirectMemberships do
                          _Membership.render ctx membership ] ]
          _Transfer.render ctx user profile.TransferId ]

/// The page: `@page_title`, the nav and the content.
let rendered (ctx: ViewContext) (profile: ProfileShow) : ViewPage =
    { Regions =
        { Regions.content [ content ctx profile ] with
            Title = Some profile.User.Name
            Nav = nav ctx }
      Fragments = [] }
