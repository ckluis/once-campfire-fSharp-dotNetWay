// Port of rust/crates/views/templates/messages/show.html (reference/app/views/messages/show.html.erb)
/// `messages/show.html.erb`: the message partial (no layout).
module Campfire.Views.Templates.Messages.Show

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

/// `message` is the fetched `messages/_message` fragment, placed in the page.
let render (message: CachedFragment) : XmlNode = Html.fragment message

/// The page.
let output (message: CachedFragment) : HtmlOutput = HtmlOutput.ofNode (render message) [ message ]
