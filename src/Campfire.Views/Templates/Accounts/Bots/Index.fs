// Port of rust/crates/views/templates/accounts/bots/index.html (reference/app/views/accounts/bots/index.html.erb)
/// `accounts/bots/index.html.erb`: the account's bots.
module Campfire.Views.Templates.Accounts.Bots.Index

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Layouts
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// The page: `@page_title`, the nav, the explanation with its add button and the bots.
let rendered (ctx: ViewContext) (bots: Bot list) : ViewPage =
    { Regions =
        { Regions.content
            [ Elem.section
                  [ Attr.class' "panel panel--wide txt-align-center flex flex-column position-relative"
                    Attr.create "style" "view-transition-name: chat-bots" ]
                  [ Elem.div
                        [ Attr.class' "flex align-center gap" ]
                        [ Elem.div [ Attr.class' "panel__button" ] [ Translations.translationButton ctx "chat_bots" ]
                          Elem.div
                              [ Attr.class' "pad-inline-double center" ]
                              [ Elem.h1 [ Attr.class' "margin-none" ] [ Text.raw "Chat bots" ]
                                Elem.p
                                    [ Attr.class' "margin-none-block-start" ]
                                    [ Text.raw "With Chat bots, other sites and services can post updates directly to Campfire." ]
                                Links.linkTo
                                    (Routes.newAccountBot ())
                                    [ Attr.class' "btn btn--reversed txt-large"; Attr.create "aria-label" "Add a chat bot" ]
                                    [ Assets.imageSized ctx "bot.svg" 20 [ hidden ]
                                      Html.sp
                                      Assets.imageSized ctx "add.svg" 20 [ hidden ] ] ] ]
                    Elem.div
                        [ Attr.class' "pad-inline pad-block-start " ]
                        [ Elem.create "menu" [ Attr.class' "flex flex-column gap margin-none pad" ] [ for bot in bots -> _Bot.render ctx bot ] ] ] ] with
            Title = Some "Chat bots"
            Nav = [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBackTo ctx (Routes.editAccount ()) ] ] }
      Fragments = [] }
