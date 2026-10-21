// Port of rust/crates/views/templates/messages/create.turbo_stream.html (reference/app/views/messages/create.turbo_stream.erb)
/// `messages/create.turbo_stream.erb`: appends the new message to its room's list.
module Campfire.Views.Templates.Messages.CreateTurboStream

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

let render (message: MessageItem) (roomKind: RoomKind) : XmlNode list =
    [ Elem.create
          "turbo-stream"
          [ Attr.create "action" "append"; Html.attr "target" (roomDomId roomKind message.RoomId "messages") ]
          [ Elem.template [] [ Html.fragment message.Html ] ] ]

/// The response.
let output (message: MessageItem) (roomKind: RoomKind) : HtmlOutput =
    HtmlOutput.ofNodes (render message roomKind) [ message.Html ]
