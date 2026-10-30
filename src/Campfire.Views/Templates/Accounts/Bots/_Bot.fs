// Port of rust/crates/views/templates/accounts/bots/_bot.html (reference/app/views/accounts/bots/_bot.html.erb)
/// `accounts/bots/_bot.html.erb`: a bot, with the curl commands that post to each of its rooms.
module Campfire.Views.Templates.Accounts.Bots._Bot

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Helpers
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// One curl command: its icon, a read-only field holding it and the button that copies it.
let private commandRow (ctx: ViewContext) (iconSource: string) (line: string) (ariaLabel: string) (copyLabel: string) : XmlNode =
    Elem.div
        [ Attr.class' "flex align-center gap" ]
        [ Assets.imageSized ctx iconSource 24 [ hidden; Attr.class' "colorize--black" ]
          Elem.div
              [ Attr.class' "flex-item-grow" ]
              [ Elem.input
                    [ Attr.create "type" "text"
                      Attr.class' "input full-width fill-white"
                      Html.attr "value" line
                      Html.attr "aria-label" ariaLabel
                      Attr.readonly ] ]
          Elem.div
              [ Attr.class' "txt-small" ]
              [ Application.buttonToCopyToClipboard
                    line
                    [ Assets.imageSized ctx "copy-paste.svg" 20 [ hidden ]; Html.sp; Application.screenReader copyLabel ] ] ]

let render (ctx: ViewContext) (bot: Bot) : XmlNode =
    Elem.li
        [ Attr.class' "flex flex-column gap flush fill-shade border-radius pad-block pad-inline-double" ]
        [ Elem.div
              [ Attr.class' "flex align-center gap" ]
              [ Elem.figure
                    [ Attr.class' "avatar flex-item--no-shrink"; Attr.create "style" "--avatar-size: 2.65em;" ]
                    [ Markup.Users.avatarTag ctx bot.User.Avatar [ Attr.create "loading" "lazy" ] ]
                Elem.div
                    [ Attr.class' "min-width" ]
                    [ Elem.div [ Attr.class' "overflow-ellipsis txt-large" ] [ Elem.strong [] [ Html.text bot.User.Name ] ] ]
                Links.linkTo
                    (Routes.editAccountBot bot.User.Id)
                    [ Attr.class' "btn flex-item-justify-end"
                      Html.attr "style" ("view-transition-name: chat-bot-" + string bot.User.Id) ]
                    [ Assets.imageSized ctx "pencil.svg" 20 [ hidden ]
                      Html.sp
                      Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text ("Edit " + bot.User.Name) ] ] ]
          for room in bot.Rooms do
              let url = ctx.Url(Routes.roomBotMessages room.Id bot.BotKey)
              Elem.create
                  "fieldset"
                  [ Attr.class' "gap max-width pad border border-radius" ]
                  [ Elem.legend
                        [ Attr.class' "min-width txt-align-start pad-inline" ]
                        [ Elem.strong [ Attr.class' "overflow-ellipsis" ] [ Html.text room.Name ] ]
                    commandRow
                        ctx
                        "messages-outlined.svg"
                        (UsersHelper.curlTextLine url)
                        "curl command for posting messages"
                        "Copy message command"
                    commandRow
                        ctx
                        "attachment.svg"
                        (UsersHelper.curlUploadLine url)
                        "curl command for posting attachments"
                        "Copy attachment command" ] ]
