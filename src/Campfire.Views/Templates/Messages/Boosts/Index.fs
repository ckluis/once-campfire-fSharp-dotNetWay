// Port of rust/crates/views/templates/messages/boosts/index.html (reference/app/views/messages/boosts/index.html.erb)
/// `messages/boosts/index.html.erb`.
module Campfire.Views.Templates.Messages.Boosts.Index

open Falco.Markup
open Campfire.Views
open Campfire.Views.Messages

let render (ctx: ViewContext) (message: MessageView) (boosts: CachedFragment[]) : XmlNode = _Boosts.render ctx message boosts

/// The page.
let output (ctx: ViewContext) (message: MessageView) (boosts: CachedFragment[]) : HtmlOutput =
    HtmlOutput.ofNode (render ctx message boosts) boosts
