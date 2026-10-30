// Port of rust/crates/views/templates/accounts/bots/_form.html (reference/app/views/accounts/bots/_form.html.erb)
/// `accounts/bots/_form.html.erb`: the fields of the new and edit bot forms, inside the page's `form_with` block (model `user`).
module Campfire.Views.Templates.Accounts.Bots._Form

open Falco.Markup
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// A field with its translation button and an icon after it.
let private row (ctx: ViewContext) (key: string) (field: XmlNode) (icon: string) : XmlNode =
    Elem.div
        [ Attr.class' "flex align-center gap" ]
        [ Translations.translationButton ctx key
          Elem.label
              [ Attr.class' "flex align-center gap flex-item-grow txt-large input input--actor" ]
              [ field; Html.sp; Assets.imageSized ctx icon 24 [ hidden; Attr.class' "colorize--black" ] ] ]

/// `objectName` is the form's `model:` (the pages' is "user"; empty for a model-less `form_with`).
let render (ctx: ViewContext) (objectName: string) (bot: BotForm) : XmlNode list =
    [ Elem.h1 [ Attr.class' "for-screen-reader" ] [ Text.raw "Chat Bot Setup" ]
      Elem.label
          [ Attr.class' "align-center center avatar__form gap"; Attr.create "data-controller" "upload-preview" ]
          [ Elem.div
                [ Attr.class' "btn input--file" ]
                [ Assets.imageSized ctx "camera.svg" 20 [ hidden ]
                  Html.sp
                  Forms.fileField
                      objectName
                      "avatar"
                      None
                      [ Attr.class' "input"
                        Attr.create "accept" "image/*"
                        Attr.create "data-upload-preview-target" "input"
                        Attr.create "data-action" "upload-preview#previewImage" ]
                  Html.sp
                  Application.screenReader "Upload bot avatar" ]
            Elem.div
                [ Attr.class' "avatar input--file txt-xx-large"; Attr.create "style" "--avatar-size: var(--btn-size);" ]
                [ Assets.imageSized
                      ctx
                      (defaultArg bot.AvatarAttachmentUrl "default-bot-avatar.svg")
                      48
                      [ Attr.create "alt" "Bot avatar"; Attr.create "data-upload-preview-target" "image" ] ] ]
      row
          ctx
          "bot_name"
          (Forms.textField
              objectName
              "name"
              bot.Name
              [ Attr.class' "input"
                Attr.create "autocomplete" "name"
                Attr.create "placeholder" "Name the bot"
                Attr.create "autofocus" "autofocus"
                Attr.create "required" "required"
                Attr.create "data-1p-ignore" "true" ])
          "bot.svg"
      row
          ctx
          "webhook_url"
          (Forms.urlField objectName "webhook_url" bot.WebhookUrl [ Attr.class' "input"; Attr.create "placeholder" "Webhook URL" ])
          "web.svg"
      Markup.Users.profileFormSubmitButton ctx ]
