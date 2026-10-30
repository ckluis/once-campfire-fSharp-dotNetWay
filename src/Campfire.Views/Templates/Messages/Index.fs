// Port of rust/crates/views/templates/messages/index.html (reference/app/views/messages/index.html.erb)
/// `messages/index.html.erb`: the page of messages the client fetches while scrolling (no layout). The messages' fragments are the
/// fragments of the page (`HtmlOutput.ofNodes (render messages) (fragments messages)`).
module Campfire.Views.Templates.Messages.Index

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let render (messages: MessageItem list) : XmlNode list = [ for message in messages -> Html.fragment message.Html ]

/// The page.
let output (messages: MessageItem list) : HtmlOutput =
    HtmlOutput.ofNodes (render messages) [ for message in messages -> message.Html ]
