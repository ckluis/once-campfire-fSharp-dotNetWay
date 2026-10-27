// Port of rust/crates/views/templates/accounts/bots/new.html (reference/app/views/accounts/bots/new.html.erb)
/// `accounts/bots/new.html.erb`.
module Campfire.Views.Templates.Accounts.Bots.New

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Layouts
open Campfire.Views.Markup

/// The page: `@page_title`, the nav and the bot form.
let rendered (ctx: ViewContext) (bot: BotForm) : ViewPage =
    { Regions =
        { Regions.content
            [ Elem.section
                  [ Attr.class' "panel" ]
                  [ Forms.formWith
                        (Routes.accountBots ())
                        "post"
                        [ Attr.class' "flex flex-column gap"; Attr.create "enctype" "multipart/form-data" ]
                        (_Form.render ctx "user" bot) ] ] with
            Title = Some "New chat bot"
            Nav = [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBackTo ctx (Routes.accountBots ()) ] ] }
      Fragments = [] }
