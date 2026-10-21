// Port of rust/crates/views/templates/messages/destroy.turbo_stream.html (reference/app/views/messages/destroy.turbo_stream.erb)
/// `messages/destroy.turbo_stream.erb`, also what `Message#broadcast_remove` sends.
module Campfire.Views.Templates.Messages.DestroyTurboStream

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let render (message: MessageView) : XmlNode list =
    [ Elem.create "turbo-stream" [ Attr.create "action" "remove"; Html.attr "target" (message.DomId "") ] [] ]

/// The response.
let output (message: MessageView) : HtmlOutput = HtmlOutput.ofNodes (render message) []
