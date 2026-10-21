// Port of rust/crates/cable/src/channel.rs
//
// `ActionCable::Channel::Base`: one instance per subscription, driven by the connection.
namespace Campfire.Cable

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.Text.Json
open System.Threading.Tasks

/// What a channel (and a Turbo helper) broadcasts through: `ActionCable.server.broadcast(broadcasting, message)`.
/// Returns how many subscribers it reached.
type ICableBroadcaster =
    abstract Broadcast: broadcasting: string * payload: Payload -> int

/// One stream change a callback asked for, applied by the connection once the callback's own frames are queued (C13).
type internal StreamOp =
    | Start of broadcasting: string
    | Stop of broadcasting: string

/// The per-subscription state a channel works with: params, the connection's identity, streams,
/// rejection and transmissions.
[<Sealed>]
type Subscription<'U>
    internal
    (
        broadcaster: ICableBroadcaster,
        services: IServiceProvider,
        className: string,
        identifier: string,
        identifierJson: byte[],
        parameters: JsonElement,
        currentUser: 'U
    ) =
    let streams = List<string>()
    /// Stream changes made by the last callback; the connection applies them after the callback's transmissions
    /// and the confirmation are queued, so a broadcast on a new stream never precedes the confirmation.
    let ops = List<StreamOp>()
    let transmissions = List<ReadOnlyMemory<byte>>()
    let mutable rejected = false
    let mutable unsubscribed = false

    /// The raw identifier string the client subscribed with.
    member _.Identifier: string = identifier

    /// The decoded identifier object, including `channel`.
    member _.Params: JsonElement = parameters

    member _.Param(key: string) : JsonElement voption =
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

    /// Recorded here and registered after the confirmation (C13). A stream named twice is delivered twice, as in Rails.
    member _.StreamFrom(broadcasting: string) : unit =
        if not unsubscribed then
            streams.Add broadcasting
            ops.Add(Start broadcasting)

    member this.StreamFor(broadcastables: string list) : unit = this.StreamFrom(this.BroadcastingFor broadcastables)

    member _.StopStreamFrom(broadcasting: string) : unit =
        while streams.Remove broadcasting do
            ops.Add(Stop broadcasting)

    member _.StopAllStreams() : unit =
        for broadcasting in streams do
            ops.Add(Stop broadcasting)
        streams.Clear()

    /// `stream_or_reject_for`.
    member this.StreamOrRejectFor(broadcastables: string list option) : unit =
        match broadcastables with
        | Some broadcastables -> this.StreamFor broadcastables
        | None -> this.Reject()

    member _.Streams: string list = List.ofSeq streams

    member _.Reject() : unit = rejected <- true

    /// `subscription_rejected?`
    member _.Rejected: bool = rejected

    /// Sends `{"identifier":...,"message":...}` to this subscriber only (not in the design's API; the Cable test channels use it).
    member _.Transmit(payload: Payload) : unit = transmissions.Add(Frames.message identifierJson payload)

    member internal _.Unsubscribed
        with get () = unsubscribed
        and set value = unsubscribed <- value

    member internal _.ClassName: string = className
    member internal _.IdentifierJson: byte[] = identifierJson

    /// Moves the frames the last callback transmitted onto `target`.
    member internal _.DrainTransmissions(target: List<ReadOnlyMemory<byte>>) : unit =
        target.AddRange transmissions
        transmissions.Clear()

    /// Moves the stream changes the last callback made onto `target`.
    member internal _.DrainOps(target: List<StreamOp>) : unit =
        target.AddRange ops
        ops.Clear()

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
