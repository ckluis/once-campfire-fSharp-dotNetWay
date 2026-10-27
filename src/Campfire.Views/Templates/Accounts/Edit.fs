// Port of rust/crates/views/templates/accounts/edit.html (reference/app/views/accounts/edit.html.erb)
/// `accounts/edit.html.erb`: the account settings page.
module Campfire.Views.Templates.Accounts.Edit

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Layouts
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// `content_for :nav`.
let private nav (ctx: ViewContext) : XmlNode list =
    [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBackToLastRoomVisited ctx ]
      if ctx.CanAdminister then
          Elem.div
              [ Attr.class' "flex align-center gap flex-item-justify-end" ]
              [ Links.linkTo
                    (Routes.accountBots ())
                    [ Attr.class' "btn"; Attr.create "style" "view-transition-name: chat-bots" ]
                    [ Assets.imageSized ctx "bot.svg" 20 [ hidden ]; Html.sp; Application.screenReader "Set up chat bots" ]
                Html.sp
                Links.linkTo
                    (Routes.editAccountCustomStyles ())
                    [ Attr.class' "btn"; Attr.create "style" "view-transition-name: custom-styles" ]
                    [ Assets.imageSized ctx "art.svg" 20 [ hidden ]; Html.sp; Application.screenReader "Custom styles" ] ] ]

/// The upload field of the logo forms: previews the image, submits on change.
let private logoField: XmlNode =
    Forms.fileField
        "account"
        "logo"
        None
        [ Attr.class' "input"
          Attr.create "accept" "image/*"
          Attr.create "data-action" "upload-preview#previewImage change->form#submit" ]

/// The forms an administrator sees: the logo (twice, the button and the preview), the name and the room creation switch.
let private settings (ctx: ViewContext) (edit: EditView) : XmlNode list =
    [ Elem.div
          [ Attr.class' "align-center center avatar__form gap"; Attr.create "data-controller" "upload-preview" ]
          [ Forms.formWith
                edit.AccountAction
                "patch"
                [ Attr.class' "txt--medium"; Attr.create "data-controller" "form"; Attr.create "enctype" "multipart/form-data" ]
                [ Elem.label
                      [ Attr.class' "btn input--file" ]
                      [ Assets.imageSized ctx "camera.svg" 20 [ hidden ]
                        Html.sp
                        logoField
                        Html.sp
                        Application.screenReader "Upload logo" ] ]
            Forms.formWith
                edit.AccountAction
                "patch"
                [ Attr.create "data-controller" "form"; Attr.create "enctype" "multipart/form-data" ]
                [ Elem.label
                      [ Attr.class' "btn avatar input--file account-logo txt-xx-large" ]
                      [ Assets.imageSized
                            ctx
                            ctx.Account.LogoUrl
                            48
                            [ Attr.create "role" "presentation"; Attr.create "data-upload-preview-target" "image" ]
                        Html.sp
                        logoField
                        Html.sp
                        Application.screenReader "Upload logo" ] ]
            if ctx.Account.HasLogo then
                Forms.buttonTo
                    ctx.Account.LogoUrl
                    "delete"
                    [ Attr.class' "btn btn--negative txt-small avatar__delete-btn" ]
                    [ Assets.imageSized ctx "minus.svg" 20 [ hidden ]; Html.sp; Application.screenReader "Delete logo" ] ]
      Forms.formWith
          edit.AccountAction
          "patch"
          [ Attr.create "data-controller" "form"; Attr.class' "flex flex-column gap" ]
          [ Elem.div
                [ Attr.class' "flex align-center gap" ]
                [ Translations.translationButton ctx "account_name"
                  Elem.label
                      [ Attr.class' "flex align-center gap flex-item-grow" ]
                      [ Forms.textField
                            "account"
                            "name"
                            (Some ctx.Account.Name)
                            [ Attr.class' "input txt-large"
                              Attr.create "autocomplete" "off"
                              Attr.create "placeholder" "Name this account"
                              Attr.create "autofocus" "autofocus"
                              Attr.create "data-action" "keydown.enter->form#submit" ] ]
                  Html.sp
                  Forms.buttonTag
                      [ Attr.class' "btn btn--reversed center" ]
                      [ Assets.imageSized ctx "check.svg" 20 [ hidden ]; Html.sp; Application.screenReader "Save changes" ] ] ]
      Elem.div
          [ Attr.class' "margin-block-start pad-block pad-inline-double fill-shade border-radius" ]
          [ Forms.formWith
                edit.AccountAction
                "put"
                [ Attr.create "data-controller" "form"; Attr.class' "flex align-center gap center" ]
                [ Elem.div
                      [ Attr.class' "flex-item-grow flex align-center gap txt-align-start" ]
                      [ Assets.imageSized ctx "crown.svg" 18 [ Attr.class' "colorize--black"; hidden ]
                        Text.raw " Must be admin to create new rooms" ]
                  Forms.hiddenField
                      "account[settings]"
                      "restrict_room_creation_to_administrators"
                      (Some(if edit.RestrictRoomCreationToAdministrators then "false" else "true"))
                      []
                  Elem.label
                      [ Attr.class' "switch" ]
                      [ Elem.input
                            [ Attr.create "type" "checkbox"
                              Attr.class' "switch__input"
                              if edit.RestrictRoomCreationToAdministrators then
                                  Attr.checked'
                              Attr.create "data-action" "change->form#submit" ]
                        Html.sp
                        Elem.span [ Attr.class' "switch__btn round" ] []
                        Html.sp
                        Application.screenReader "Must be admin to create new rooms" ] ] ] ]

/// The page itself.
let private content (ctx: ViewContext) (edit: EditView) : XmlNode list =
    [ Elem.section
          [ Attr.class' "panel txt-align-center flex flex-column gap"
            Attr.create "style" "view-transition-name: account-settings" ]
          ((if ctx.CanAdminister then
                settings ctx edit
            else
                [ Markup.Users.accountLogoTag ctx (Some "txt-xx-large center")
                  Elem.h1 [ Attr.class' "flex-item-grow txt-x-large" ] [ Html.text ctx.Account.Name ] ])
           @ [ Elem.div
                [ Attr.class' "margin-block pad-inline pad-block-start fill-shade border-radius" ]
                [ _Invite.render ctx edit.JoinCode
                  Elem.hr [ Attr.class' "margin-block separator full-width"; Attr.create "style" "--border-style: solid" ]
                  Elem.create
                      "menu"
                      [ Attr.class' "flex flex-column gap margin-none pad" ]
                      [ Turbo.frameTag
                            "account_users"
                            None
                            None
                            []
                            [ for user in edit.Administrators do
                                  Campfire.Views.Templates.Accounts.Users._User.render ctx user
                              if not edit.Administrators.IsEmpty && not edit.Members.IsEmpty then
                                  Elem.hr [ Attr.class' "separator full-width"; Attr.create "style" "--border-style: solid" ]
                              for user in edit.Members do
                                  Campfire.Views.Templates.Accounts.Users._User.render ctx user
                              match edit.NextPage with
                              | Some page -> Campfire.Views.Templates.Accounts.Users._NextPageContainer.render page
                              | None -> () ] ] ] ]) ]

/// `content_for :footer`.
let private footer (ctx: ViewContext) : XmlNode list =
    [ Elem.div
          [ Attr.class' "txt-align-center center margin-block-double txt-subtle" ]
          [ Text.raw "Campfire&trade; version "; Application.versionBadge ctx ] ]

/// The page: `@page_title`, the nav, the settings and the footer.
let rendered (ctx: ViewContext) (edit: EditView) : ViewPage =
    { Regions =
        { Regions.content (content ctx edit) with
            Title = Some "Account settings"
            Nav = nav ctx
            Footer = footer ctx }
      Fragments = [] }
