// Port of rust/crates/cable/src/channel.rs
//
// `ActionCable::Channel::Base`: one instance per subscription, driven by the connection.
namespace Campfire.Cable

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Campfire.Cable.Socket

/// What a channel (and a Turbo helper) broadcasts through: `ActionCable.server.broadcast(broadcasting, message)`.
/// Returns how many subscribers it reached.
type ICableBroadcaster =
    abstract Broadcast: broadcasting: string * payload: Payload -> int

/// The per-subscription state a channel works with: params, the connection's identity, streams,
/// rejection and transmissions.
[<Sealed>]
type Subscription<'U>
    internal
    (
        broadcaster: ICableBroadcaster,
        hub: Hub,
        services: IServiceProvider,
        className: string,
        identifier: string,
        encodedIdentifier: string,
        currentUser: 'U,
        wake: Wake
    ) =
    let streams = List<Subscriber>()
    /// Streams started by the last callback, for the connection to start reading once that
    /// callback's own frames (transmissions, the confirmation) are queued ahead of them.
    let started = List<Subscriber>()
    let transmissions = List<string>()
    let mutable parameters = Unchecked.defaultof<JsonElement>
    let mutable parsed = false
    let mutable rejected = false
    let mutable unsubscribed = false

    /// The raw identifier string the client subscribed with.
    member _.Identifier: string = identifier

    /// The decoded identifier object, including `channel`. Parsed when asked for (subscriptions live as
    /// long as their sockets, and are many) and detached from the parser's buffer.
    member _.Params: JsonElement =
        if not parsed then
            parameters <-
                try
                    use document = JsonDocument.Parse identifier
                    document.RootElement.Clone()
                with :? JsonException ->
                    use document = JsonDocument.Parse "{}"
                    document.RootElement.Clone()
            parsed <- true
        parameters

    member this.Param(key: string) : JsonElement voption =
        let parameters = this.Params
        if parameters.ValueKind <> JsonValueKind.Object then
            ValueNone
        else
            match parameters.TryGetProperty key with
            | true, value -> ValueSome value
            | _ -> ValueNone

    /// `identified_by :current_user`.
    member _.CurrentUser: 'U = currentUser

    /// The services a callback may resolve from: the server's root provider until 3.5 gives each command a scope.
    member _.Services: IServiceProvider = services

    member _.Broadcaster: ICableBroadcaster = broadcaster

    member _.ChannelName() : string = Naming.channelName className

    /// `broadcasting_for` for this channel's class.
    member _.BroadcastingFor(broadcastables: string list) : string = Naming.broadcastingFor className broadcastables

    /// `broadcast_to` for this channel's class.
    member this.BroadcastTo(broadcastables: string list, payload: Payload) : unit =
        broadcaster.Broadcast(this.BroadcastingFor broadcastables, payload) |> ignore

    member _.StreamFrom(broadcasting: string) : unit =
        if not unsubscribed then
            // The hub wraps each broadcast for this identifier once, for every subscriber sharing it.
            let subscriber = hub.Subscribe(broadcasting, encodedIdentifier, wake)
            started.Add subscriber
            streams.Add subscriber

    member this.StreamFor(broadcastables: string list) : unit = this.StreamFrom(this.BroadcastingFor broadcastables)

    member _.StopStreamFrom(broadcasting: string) : unit =
        let mutable i = 0
        while i < streams.Count do
            if streams[i].Broadcasting = broadcasting then
                (streams[i] :> IDisposable).Dispose()
                streams.RemoveAt i
            else
                i <- i + 1

    member _.StopAllStreams() : unit =
        for stream in streams do
            (stream :> IDisposable).Dispose()
        streams.Clear()
        started.Clear()

    /// `stream_or_reject_for`.
    member this.StreamOrRejectFor(broadcastables: string list option) : unit =
        match broadcastables with
        | Some broadcastables -> this.StreamFor broadcastables
        | None -> this.Reject()

    member _.Streams: string list = [ for stream in streams -> stream.Broadcasting ]

    member _.Reject() : unit = rejected <- true

    /// `subscription_rejected?`
    member _.Rejected: bool = rejected

    /// Sends `{"identifier":...,"message":...}` to this subscriber only (not in the design's API; the old transport's tests use it).
    member _.Transmit(payload: Payload) : unit =
        transmissions.Add(Protocol.message encodedIdentifier (Encoding.UTF8.GetString(Payload.toBytes payload)))

    member internal _.Unsubscribed
        with get () = unsubscribed
        and set value = unsubscribed <- value

    member internal _.ClassName: string = className

    /// Moves the frames the last callback transmitted onto `target`.
    member internal _.DrainTransmissions(target: List<Frame>) : unit =
        for text in transmissions do
            target.Add(Frame.OfString text)
        transmissions.Clear()

    /// Moves the streams the last callback started onto `target`.
    member internal _.DrainStarted(target: List<Subscriber>) : unit =
        target.AddRange started
        started.Clear()

    interface IDisposable with
        member this.Dispose() = this.StopAllStreams()

/// A channel class. The server builds a fresh instance per subscription from the factory registered
/// under the Ruby class name (see `ServerBuilder.channel`); what state it keeps is in the closures.
///
/// Rails' `on_subscribe`/`on_unsubscribe` callbacks run after `subscribed`/`unsubscribed`, so put
/// them at the end of these functions (guarding on `Subscription.Rejected` where the Ruby does
/// `unless: :subscription_rejected?`).
///
/// An exception that escapes a callback is Rails' rescued error: logged, nothing sent (a subscription whose
/// `subscribed` raised is neither confirmed nor rejected, and stays registered).
type Channel<'U> =
    { Subscribed: Subscription<'U> -> Task
      /// Also runs when a subscription is rejected, as in Rails (`reject_subscription` removes the
      /// subscription, which calls `unsubscribe_from_channel`).
      Unsubscribed: Subscription<'U> -> Task
      /// Dispatches a `perform` from the client. Return false when `action` isn't one of the
      /// channel's public methods, which Rails logs as "Unable to process". `action` is
      /// `data["action"]`, or `"receive"` when that's blank. The data element is only good during the call.
      Perform: string -> JsonElement -> Subscription<'U> -> Task<bool> }

module Channel =
    let private notPerformed: Task<bool> = Task.FromResult false

    /// A channel with no callbacks: `ApplicationCable::Channel` itself and `HeartbeatChannel`
    /// subscribe, confirm and do nothing else. Build others with `{ Channel.empty with ... }`.
    let empty<'U> : Channel<'U> =
        { Subscribed = fun _ -> Task.CompletedTask
          Unsubscribed = fun _ -> Task.CompletedTask
          Perform = fun _ _ _ -> notPerformed }

/// Ruby class name -> channel factory.
type ChannelRegistry<'U> = FrozenDictionary<string, unit -> Channel<'U>>

module ChannelRegistry =
    let create (channels: (string * (unit -> Channel<'U>)) list) : ChannelRegistry<'U> =
        channels |> dict |> FrozenDictionary.ToFrozenDictionary

    /// The channel a client's `channel` names, with its class name: `safe_constantize` resolves
    /// "::RoomChannel" too, but the class (and so every broadcasting it names) is "RoomChannel".
    let resolve (registry: ChannelRegistry<'U>) (requested: string) : struct (string * (unit -> Channel<'U>)) voption =
        let name = if requested.StartsWith("::", StringComparison.Ordinal) then requested.Substring 2 else requested
        match registry.TryGetValue name with
        | true, factory -> ValueSome(struct (name, factory))
        | _ -> ValueNone
