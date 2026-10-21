// Phase 3 (plans/design-cable.md section 3): `ActionCable::Server::Base` on the WebSocket middleware: the channel registry,
// broadcasting, the heartbeat, remote disconnects and restarts, and the origin check.
namespace Campfire.Cable

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions

/// One per app, with the connection authenticator (`connect`), what names a connection for remote disconnects
/// (`identified_by`) and the channel classes.
[<Sealed>]
type CableServer<'U>
    internal
    (
        options: CableOptions,
        channels: ChannelRegistry<'U>,
        authenticate: HttpContext -> Task<'U option>,
        identify: 'U -> string,
        services: IServiceProvider,
        time: TimeProvider,
        logger: ILogger
    ) =
    let core = ServerCore<'U>(options, channels, authenticate, identify, services, logger)

    member _.Options: CableOptions = options
    member _.Time: TimeProvider = time
    member _.Logger: ILogger = logger

    member internal _.Core: ServerCore<'U> = core

    /// `ActionCable.server.broadcast(broadcasting, message)`; the payload is already encoded.
    member _.Broadcast(broadcasting: string, payload: Payload) : int = core.Broadcast(broadcasting, payload)

    /// `SomeChannel.broadcast_to(broadcastables, message)`.
    member this.BroadcastTo(className: string, broadcastables: string list, payload: Payload) : int =
        core.Broadcast(Naming.broadcastingFor className broadcastables, payload)

    /// `ActionCable.server.remote_connections.where(current_user: user).disconnect(reconnect:)` (C11): every connection
    /// under this identifier, on any socket, is sent `{"type":"disconnect","reason":"remote","reconnect":...}` and closed.
    member _.Disconnect(connectionIdentifier: string, reconnect: bool) : int =
        let connections = core.Connections.ByIdentifier connectionIdentifier
        for connection in connections do
            connection.Close(Some Remote, reconnect)
        connections.Length

    /// `ActionCable.server.restart`: closes every connection with `server_restart`.
    member _.Restart() : unit =
        for connection in core.Connections.All do
            connection.Close(Some ServerRestart, true)

    /// One ping per beat, built once from the app's clock and shared by every open connection (C8).
    member _.Beat() : unit =
        let frame = Frames.ping (time.GetUtcNow().ToUnixTimeSeconds())
        for connection in core.Connections.All do
            connection.TryWrite frame |> ignore

    member _.ConnectionCount: int = core.Connections.Count

    /// Broadcastings that currently have subscribers.
    member _.StreamCount: int = core.Streams.Count

    /// `ActionCable::Server::Base#allow_request_origin?` (C5): the origin is the request's scheme and host as the pipeline
    /// left them (after the forwarded headers and `assume_ssl`).
    member internal _.AllowRequestOrigin(ctx: HttpContext) : bool =
        if options.DisableRequestForgeryProtection then
            true
        else
            let origin = ctx.Request.Headers.Origin.ToString()
            if origin <> "" && (origin = ctx.Request.Scheme + "://" + ctx.Request.Host.Value || Array.contains origin options.AllowedRequestOrigins) then
                true
            else
                logger.LogError("Request origin not allowed {Origin}", origin)
                false

    member internal _.Authenticate(ctx: HttpContext) : Task<'U option> = authenticate ctx

    interface ICableBroadcaster with
        member _.Broadcast(broadcasting, payload) = core.Broadcast(broadcasting, payload)

type ServerBuilder<'U> =
    { Options: CableOptions
      Authenticate: HttpContext -> Task<'U option>
      Identify: 'U -> string
      Channels: Map<string, unit -> Channel<'U>>
      Services: IServiceProvider
      Time: TimeProvider
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

    /// The clock the heartbeat's `ping` frames and timer use.
    let withTime (time: TimeProvider) (builder: ServerBuilder<'U>) : ServerBuilder<'U> = { builder with Time = time }

    let build (builder: ServerBuilder<'U>) : CableServer<'U> =
        let channels = ChannelRegistry.create [ for KeyValue(name, factory) in builder.Channels -> name, factory ]
        let server = CableServer<'U>(builder.Options, channels, builder.Authenticate, builder.Identify, builder.Services, builder.Time, builder.Logger)
        server.Core.Broadcaster <- server
        server

module Server =
    let private emptyServices: IServiceProvider = ServiceCollection().BuildServiceProvider()

    /// `ApplicationCable::Connection#connect` resolves the upgrade request to the connection's identity, or
    /// None for `reject_unauthorized_connection`. `identify` is `identified_by`: the connection
    /// identifier used for remote disconnects (for `identified_by :current_user`, the user's GID param,
    /// `Naming.gidParam`).
    let builder (options: CableOptions) (authenticate: HttpContext -> Task<'U option>) (identify: 'U -> string) : ServerBuilder<'U> =
        { Options = options
          Authenticate = authenticate
          Identify = identify
          Channels = Map.empty
          Services = emptyServices
          Time = TimeProvider.System
          Logger = NullLogger.Instance }
