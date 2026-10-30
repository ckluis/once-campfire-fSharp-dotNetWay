// Port of rust/crates/views/templates/accounts/bots/edit.html (reference/app/views/accounts/bots/edit.html.erb)
/// `accounts/bots/edit.html.erb`: a bot's form, with the buttons that delete it and change its key.
module Campfire.Views.Templates.Accounts.Bots.Edit

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Layouts
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// The page: `@page_title`, the nav, the form and the buttons that delete the bot and change its key.
let rendered (ctx: ViewContext) (botId: int64) (bot: BotForm) : ViewPage =
    { Regions =
        { Regions.content
            [ Elem.section
                  [ Attr.class' "panel"; Html.attr "style" ("view-transition-name: chat-bot-" + string botId) ]
                  [ Forms.formWith
                        (Routes.accountBot botId)
                        "patch"
                        [ Attr.class' "flex flex-column gap"; Attr.create "enctype" "multipart/form-data" ]
                        (_Form.render ctx "user" bot)
                    Elem.hr [ Attr.class' "separator full-width margin-block-double" ]
                    Elem.div
                        [ Attr.class' "flex align-center gap justify-space-between" ]
                        [ Forms.buttonTo
                              (Routes.accountBot botId)
                              "delete"
                              [ Attr.class' "btn txt--small btn--negative"
                                Attr.create "aria-label" "Delete this chat bot"
                                Attr.create
                                    "data-turbo-confirm"
                                    "Are you sure you want to permanently remove this bot from the account? This can’t be undone." ]
                              [ Assets.imageSized ctx "trash.svg" 20 [ hidden ]; Html.sp; Assets.imageSized ctx "bot.svg" 20 [ hidden ] ]
                          Forms.buttonTo
                              (Routes.accountBotKey botId)
                              "put"
                              [ Attr.class' "btn full-width txt--small btn--negative"
                                Attr.create "aria-label" "Generate a new key"
                                Attr.create
                                    "data-turbo-confirm"
                                    "Are you sure you want to change the bot key? All usage of this bot must be updated." ]
                              [ Assets.imageSized ctx "refresh.svg" 20 [ hidden ]; Html.sp; Assets.imageSized ctx "key.svg" 20 [ hidden ] ] ] ] ] with
            Title = Some "Edit bot"
            Nav = [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBackTo ctx (Routes.accountBots ()) ] ] }
      Fragments = [] }
