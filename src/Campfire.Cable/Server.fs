// Port of rust/crates/cable/src/server.rs
//
// `ActionCable::Server::Base`: configuration, the channel registry, broadcasting, the heartbeat,
// remote disconnects. The `/cable` endpoint is `Endpoint.call`, which needs the connection (the Rust
// `Server::call` sits in the same file as both).
namespace Campfire.Cable

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Campfire.RailsCompat
open Campfire.Cable.Socket

/// One connection's standing with the server-wide timers: what it has been told of the heartbeat
/// and of restarts, and how to wake it.
[<Sealed>]
type internal Beacon(wake: Wake, beat: int64, restarts: int64) =
    member _.Wake = wake
    member val BeatSeen = beat with get, set
    member val RestartSeen = restarts with get, set

module internal ServerNames =
    /// `ActionCable::Connection::InternalChannel#internal_channel`.
    let internalChannel (connectionIdentifier: string) : string = "action_cable/" + connectionIdentifier

    let unixNow () : int64 = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

/// `ActionCable::Server::Base`. One per app, with the connection authenticator (`connect`), what
/// names a connection for remote disconnects (`identified_by`) and the channel classes.
[<Sealed>]
type Server<'U>
    internal
    (
        config: Config,
        authenticate: ConnectRequest -> Task<'U option>,
        identify: 'U -> string,
        channels: ChannelRegistry<'U>,
        services: IServiceProvider,
        logger: ILogger
    ) =
    let hub = Hub(config.StreamCapacity)
    let stop = new CancellationTokenSource()
    let beacons = List<Beacon>()
    let mutable heartbeatStarted = 0
    let mutable heartbeat = Frame.OfString(Protocol.ping (ServerNames.unixNow ()))
    let mutable beat = 0L
    let mutable restarts = 0L

    /// The server-wide heartbeat timer's loop, so every connection pings in step.
    let heartbeatLoop () : Task =
        task {
            use timer = new PeriodicTimer(TimeSpan.FromSeconds(float Protocol.BeatInterval))
            try
                let mutable running = true
                while running do
                    let! ticked = timer.WaitForNextTickAsync stop.Token
                    if not ticked then
                        running <- false
                    else
                        // One frame per beat, shared by every connection.
                        Volatile.Write(&heartbeat, Frame.OfString(Protocol.ping (ServerNames.unixNow ())))
                        Interlocked.Increment(&beat) |> ignore
                        lock beacons (fun () ->
                            for beacon in beacons do
                                beacon.Wake.Set())
            with :? OperationCanceledException ->
                ()
        }

    member _.Config: Config = config
    member _.Hub: Hub = hub
    member _.Logger: ILogger = logger

    /// The root provider channels resolve services from (`Subscription.Services`) until 3.5 scopes each command.
    member _.Services: IServiceProvider = services

    member internal _.Authenticate(request: ConnectRequest) : Task<'U option> = authenticate request
    member internal _.Identify(user: 'U) : string = identify user

    /// The channel a client's `channel` names, with its class name (see `ChannelRegistry.resolve`).
    member internal _.Channel(requested: string) : struct (string * (unit -> Channel<'U>)) voption = ChannelRegistry.resolve channels requested

    /// `ActionCable.server.broadcast(broadcasting, message)`; the payload is already encoded.
    member this.Broadcast(broadcasting: string, payload: Payload) : int =
        if logger.IsEnabled LogLevel.Debug then logger.LogDebug("[ActionCable] Broadcasting {Broadcasting}", broadcasting)
        hub.Broadcast(broadcasting, ReadOnlySpan(Payload.toBytes payload))

    /// `SomeChannel.broadcast_to(broadcastables, message)`.
    member this.BroadcastTo(className: string, broadcastables: string list, payload: Payload) : int =
        this.Broadcast(Naming.broadcastingFor className broadcastables, payload)

    /// `ActionCable.server.remote_connections.where(current_user: user).disconnect(reconnect:)`:
    /// every connection with this identifier, on any socket, is sent
    /// `{"type":"disconnect","reason":"remote","reconnect":...}` and closed.
    member this.Disconnect(connectionIdentifier: string, reconnect: bool) : int =
        hub.Broadcast(
            ServerNames.internalChannel connectionIdentifier,
            "{\"type\":\"disconnect\",\"reconnect\":" + (if reconnect then "true" else "false") + "}"
        )

    /// Broadcastings that currently have subscribers (including connections' internal channels).
    member _.StreamCount: int = hub.StreamCount

    /// `ActionCable.server.restart`: closes every connection with `server_restart`.
    member _.Restart() : unit =
        Interlocked.Increment(&restarts) |> ignore
        lock beacons (fun () ->
            for beacon in beacons do
                beacon.Wake.Set())

    /// Starts the heartbeat timer on the first request, like Rails' `setup_heartbeat_timer`.
    member this.StartHeartbeat() : unit =
        if Interlocked.CompareExchange(&heartbeatStarted, 1, 0) = 0 then
            Volatile.Write(&heartbeat, Frame.OfString(Protocol.ping (ServerNames.unixNow ())))
            Task.Run(fun () -> heartbeatLoop ()) |> ignore

    member internal _.HeartbeatFrame: Frame = Volatile.Read(&heartbeat)
    member internal _.Beat: int64 = Volatile.Read(&beat)
    member internal _.Restarts: int64 = Volatile.Read(&restarts)

    member internal this.Register(wake: Wake) : Beacon =
        let beacon = Beacon(wake, this.Beat, this.Restarts)
        lock beacons (fun () -> beacons.Add beacon)
        beacon

    member internal _.Unregister(beacon: Beacon) : unit = lock beacons (fun () -> beacons.Remove beacon |> ignore)

    /// `ActionCable::Server::Base#allow_request_origin?`
    member internal _.AllowRequestOrigin(headers: IHeaderDictionary) : bool =
        if config.DisableRequestForgeryProtection then
            true
        else
            let first (name: string) : string | null =
                let values = headers[name]
                if values.Count = 0 then null else values[0]
            let origin = first "Origin"
            let host =
                match first "Host" with
                | null -> ""
                | host -> host
            let proto = if config.AssumeSsl || Server<'U>.SslRequest headers then "https" else "http"
            let sameOrigin =
                match origin with
                | null -> false
                | origin -> origin = proto + "://" + host
            if config.AllowSameOriginAsHost && sameOrigin then
                true
            else
                match origin with
                | origin when not (isNull origin) && List.contains origin config.AllowedRequestOrigins -> true
                | _ ->
                    logger.LogError("Request origin not allowed {Origin}", origin)
                    false

    /// `Rack::Request#ssl?` for a request that didn't arrive over TLS itself.
    static member private SslRequest(headers: IHeaderDictionary) : bool =
        let first (name: string) : string | null =
            let values = headers[name]
            if values.Count = 0 then null
            else
                match values[0] with
                | null -> null
                | value -> value.Split(',').[0].Trim().ToLowerInvariant()
        let is (name: string) (expected: string) = String.Equals(first name, expected, StringComparison.Ordinal)
        is "X-Forwarded-Ssl" "on" || is "X-Forwarded-Scheme" "https" || is "X-Forwarded-Proto" "https"

    interface ICableBroadcaster with
        member this.Broadcast(broadcasting, payload) = this.Broadcast(broadcasting, payload)

    interface IDisposable with
        member _.Dispose() = stop.Cancel()

type ServerBuilder<'U> =
    { Config: Config
      Authenticate: ConnectRequest -> Task<'U option>
      Identify: 'U -> string
      Channels: Map<string, unit -> Channel<'U>>
      Services: IServiceProvider
      Logger: ILogger }

module ServerBuilder =
    /// Registers a channel under its Ruby class name (`"RoomChannel"`, `"Turbo::StreamsChannel"`),
    /// which is what clients put in the identifier's `channel`. The factory makes the channel for
    /// each subscription.
    let channel (className: string) (factory: unit -> Channel<'U>) (builder: ServerBuilder<'U>) : ServerBuilder<'U> =
        { builder with Channels = Map.add className factory builder.Channels }

    /// The provider `Subscription.Services` hands channels (the server's root provider).
    let withServices (services: IServiceProvider) (builder: ServerBuilder<'U>) : ServerBuilder<'U> = { builder with Services = services }

    let withLogger (logger: ILogger) (builder: ServerBuilder<'U>) : ServerBuilder<'U> = { builder with Logger = logger }

    let build (builder: ServerBuilder<'U>) : Server<'U> =
        let channels = ChannelRegistry.create [ for KeyValue(name, factory) in builder.Channels -> name, factory ]
        new Server<'U>(builder.Config, builder.Authenticate, builder.Identify, channels, builder.Services, builder.Logger)

module Server =
    let private emptyServices: IServiceProvider = ServiceCollection().BuildServiceProvider()

    /// `ApplicationCable::Connection#connect` resolves the request to the connection's identity, or
    /// None for `reject_unauthorized_connection`. `identify` is `identified_by`: the connection
    /// identifier used for remote disconnects (for `identified_by :current_user`, the user's GID param,
    /// `Naming.gidParam`).
    let builder (config: Config) (authenticate: ConnectRequest -> Task<'U option>) (identify: 'U -> string) : ServerBuilder<'U> =
        { Config = config
          Authenticate = authenticate
          Identify = identify
          Channels = Map.empty
          Services = emptyServices
          Logger = NullLogger.Instance }

    /// `ActionCable::Connection::InternalChannel#internal_channel`.
    let internalChannel (connectionIdentifier: string) : string = ServerNames.internalChannel connectionIdentifier
