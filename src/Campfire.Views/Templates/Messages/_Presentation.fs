// Port of rust/crates/views/templates/messages/_presentation.html (reference/app/views/messages/_presentation.html.erb)
/// `messages/_presentation.html.erb`, which `MessagesController#update` also broadcasts.
module Campfire.Views.Templates.Messages._Presentation

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let render (ctx: ViewContext) (message: MessageView) : XmlNode =
    Elem.div
        [ Html.attr "id" ("presentation_message_" + message.ClientMessageId)
          Attr.create "dir" "auto"
          Attr.create "data-reply-target" "body"
          Attr.create "data-messages-target" "body" ]
        [ Markup.Messages.presentation ctx message ]
