// Port of rust/crates/views/templates/rooms/refreshes/show.turbo_stream.html (reference/app/views/rooms/refreshes/show.turbo_stream.erb)
/// `rooms/refreshes/show.turbo_stream.erb`: the append (when there are new messages), then one replace for each updated message.
/// An empty refresh is an empty body (Erubi wrote a newline). Streams are custom elements, so the ERB's newline between two of
/// them renders as a space (V4).
module Campfire.Views.Templates.Rooms.Refreshes.ShowTurboStream

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let render (refresh: RefreshView) : XmlNode list =
    let append =
        if refresh.NewMessages.IsEmpty then
            []
        else
            [ Turbo.stream
                  "append"
                  (roomDomId refresh.RoomKind refresh.RoomId "messages")
                  [ for message in refresh.NewMessages -> Html.fragment message.Html ] ]
    let replaces =
        [ for message in refresh.UpdatedMessages ->
              Turbo.stream "replace" ("message_" + message.ClientMessageId) [ Html.fragment message.Html ] ]
    (append @ replaces) |> List.mapi (fun i stream -> if i = 0 then [ stream ] else [ Html.sp; stream ]) |> List.concat

/// The response.
let output (refresh: RefreshView) : HtmlOutput =
    HtmlOutput.ofNodes
        (render refresh)
        [ for message in refresh.NewMessages -> message.Html
          for message in refresh.UpdatedMessages -> message.Html ]
