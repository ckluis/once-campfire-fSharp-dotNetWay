// Port of rust/crates/cable/src/turbo.rs
//
// turbo-rails: `Turbo::StreamsChannel`, the `<turbo-stream>` tags its broadcasts carry
// (`Turbo::Streams::ActionHelper#turbo_stream_action_tag`) and the `broadcast_*_to` helpers
// (`Turbo::Streams::Broadcasts`).
module Campfire.Cable.Turbo

open System
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Campfire.RailsCompat
open Campfire.Ruby

[<Literal>]
let StreamsChannelName = "Turbo::StreamsChannel"

/// `Turbo::StreamsChannel`, optionally with a guard prepended the way Campfire prepends
/// `RoomStreamsAreAuthorized` (reference/app/channels/concerns/room_streams_are_authorized.rb).
type StreamsChannel =
    { Verifier: string -> string option
      Guard: (string -> bool) option }

/// `params[:signed_stream_name]` verified with `verifier`. A missing or `null` name is simply
/// unverified; any other non-string makes `MessageVerifier#verified` raise.
let verifiedStreamNameFromParams (parameters: JsonElement) (verifier: string -> string option) : string option =
    if parameters.ValueKind <> JsonValueKind.Object then
        None
    else
        match parameters.TryGetProperty "signed_stream_name" with
        | false, _ -> None
        | true, value ->
            match value.ValueKind with
            | JsonValueKind.Null -> None
            | JsonValueKind.String -> verifier (value.GetString() |> nonNull)
            | _ -> failwith $"undefined method 'valid_encoding?' for {value.GetRawText()}"

module StreamsChannel =
    let withVerifier (verifier: string -> string option) : StreamsChannel = { Verifier = verifier; Guard = None }

    /// Verifies signed stream names with `Turbo.signed_stream_verifier`.
    let create (secrets: Secrets) : StreamsChannel =
        withVerifier (fun signed -> Turbo.verifiedStreamName secrets signed)

    /// Rejects any subscription whose verified stream name `guarded` returns true for, before the
    /// stock behavior runs. The guard also sees names that failed verification, as `""` (Ruby's
    /// `nil.to_s`).
    let guardedBy (guarded: string -> bool) (channel: StreamsChannel) : StreamsChannel = { channel with Guard = Some guarded }

    /// `verified_stream_name_from_params`, for channels (like `RoomMessagesChannel`) that take
    /// signed stream names too. Raises where Ruby does.
    let verifiedStreamNameFromParams (channel: StreamsChannel) (parameters: JsonElement) : string option =
        verifiedStreamNameFromParams parameters channel.Verifier

    /// The channel class a subscription gets.
    let channel<'U> (streams: StreamsChannel) : Channel<'U> =
        { Channel.empty with
            Subscribed =
                fun sub ->
                    let streamName = verifiedStreamNameFromParams streams sub.Params
                    let guarded =
                        match streams.Guard with
                        | Some guard -> guard (defaultArg streamName "")
                        | None -> false
                    if guarded then
                        sub.Reject()
                    else
                        match streamName with
                        | Some streamName -> sub.StreamFrom streamName
                        | None -> sub.Reject()
                    Task.CompletedTask }

/// Turbo Stream actions.
type Action =
    | Append
    | Prepend
    | Replace
    | Update
    | Remove
    | Before
    | After
    | Refresh

module Action =
    let asString (action: Action) : string =
        match action with
        | Append -> "append"
        | Prepend -> "prepend"
        | Replace -> "replace"
        | Update -> "update"
        | Remove -> "remove"
        | Before -> "before"
        | After -> "after"
        | Refresh -> "refresh"

/// Where the action applies. Records are passed as their `dom_id` (`dom_id(@room, :list)`), and
/// `targets` as a CSS selector (records become `#<dom_id>`).
type Target =
    | NoTarget
    | Target of string
    | Targets of string

let private htmlSpecials = System.Buffers.SearchValues.Create "&<>\"'"

/// An attribute value as `ERB::Util.unwrapped_html_escape` writes it (the escaping is `Campfire.Ruby`'s;
/// most values have nothing to escape and are used as they are).
let private escapedAttribute (value: string) : string =
    if value.AsSpan().IndexOfAny htmlSpecials < 0 then value else Erb.htmlEscape value

let private pushAttribute (tag: StringBuilder) (name: string) (value: string) : unit =
    tag.Append(' ').Append(name).Append("=\"").Append(escapedAttribute value).Append('"') |> ignore

/// `turbo_stream_action_tag(action, target:, targets:, template:, **attributes)`.
///
/// Extra attributes come first, then `action`, then `target`/`targets`, exactly as the tag helper's
/// hash is built. Attribute names are used as given (Rails only dasherizes the tag name, so
/// `maintain_scroll: true` renders `maintain_scroll="true"`), and a `None` value omits the
/// attribute. `template` is already-safe HTML; `remove` and `refresh` never carry one.
let actionTag (action: Action) (target: Target) (template: string option) (attributes: (string * string option) list) : string =
    let tag = StringBuilder()
    tag.Append "<turbo-stream" |> ignore
    for (name, value) in attributes do
        match value with
        | Some value -> pushAttribute tag name value
        | None -> ()
    pushAttribute tag "action" (Action.asString action)
    match target with
    | Target target -> pushAttribute tag "target" target
    | Targets targets -> pushAttribute tag "targets" targets
    | NoTarget -> ()
    tag.Append '>' |> ignore
    match action with
    | Remove
    | Refresh -> ()
    | _ -> tag.Append("<template>").Append(defaultArg template "").Append("</template>") |> ignore
    tag.Append("</turbo-stream>").ToString()

/// `turbo_stream_refresh_tag(request_id:)`.
let refreshTag (requestId: string option) : string =
    actionTag Refresh NoTarget None [ "request-id", requestId |> Option.filter (fun id -> id <> "") ]

// `Turbo::StreamsChannel.broadcast_*_to`. Streamables are the stream name parts (GID params and
// symbols); blank ones are dropped and nothing is sent if none remain, as in `broadcast_stream_to`.

/// The stream name of `streamables` that aren't blank, if any are left.
let private streamName (streamables: string list) : string option =
    match streamables |> List.filter (fun s -> s.Trim() <> "") with
    | [] -> None
    | remaining -> Some(Naming.streamNameFrom remaining)

let broadcastStreamTo (server: ICableBroadcaster) (streamables: string list) (content: string) : int =
    match streamName streamables with
    | Some name -> server.Broadcast(name, Payload.ofString content)
    | None -> 0

/// The tag as `actionTag` writes it, sent as a JSON string.
let broadcastActionTo
    (server: ICableBroadcaster)
    (streamables: string list)
    (action: Action)
    (target: Target)
    (template: string option)
    (attributes: (string * string option) list)
    : int =
    match streamName streamables with
    | None -> 0
    | Some name -> server.Broadcast(name, Payload.ofString (actionTag action target template attributes))

let broadcastAppendTo (server: ICableBroadcaster) (streamables: string list) (target: string) (html: string) : int =
    broadcastActionTo server streamables Append (Target target) (Some html) []

let broadcastPrependTo (server: ICableBroadcaster) (streamables: string list) (target: string) (html: string) : int =
    broadcastActionTo server streamables Prepend (Target target) (Some html) []

let broadcastReplaceTo (server: ICableBroadcaster) (streamables: string list) (target: string) (html: string) : int =
    broadcastActionTo server streamables Replace (Target target) (Some html) []

let broadcastUpdateTo (server: ICableBroadcaster) (streamables: string list) (target: string) (html: string) : int =
    broadcastActionTo server streamables Update (Target target) (Some html) []

let broadcastRemoveTo (server: ICableBroadcaster) (streamables: string list) (target: string) : int =
    broadcastActionTo server streamables Remove (Target target) None []

let broadcastRefreshTo (server: ICableBroadcaster) (streamables: string list) (requestId: string option) : int =
    broadcastActionTo server streamables Refresh NoTarget None [ "request-id", requestId |> Option.filter (fun id -> id <> "") ]
