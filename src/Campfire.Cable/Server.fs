// Phase 3 (plans/design-cable.md section 3): `ActionCable::Server::Base` on the WebSocket middleware: the channel registry,
// broadcasting, the heartbeat, remote disconnects and restarts, and the origin check.
namespace Campfire.Cable

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Microsoft.Extensions.Logging.Abstractions

/// One per app (a DI singleton), with the connection authenticator (`connect`), its re-check (C10), what names a connection for
/// remote disconnects (`identified_by`) and the channel classes. Commands run in scopes of `scopes` (C12); a stopping host
/// restarts every connection (C11).
[<Sealed>]
type CableServer<'U>
    (
        options: IOptions<CableOptions>,
        channels: ChannelRegistry<'U>,
        authenticate: HttpContext -> Task<'U option>,
        recheck: HttpContext -> IServiceProvider -> Task<bool>,
        identify: 'U -> string,
        scopes: IServiceScopeFactory,
        time: TimeProvider,
        lifetime: IHostApplicationLifetime,
        logger: ILogger
    ) as this =
    let options = options.Value
    let core = ServerCore<'U>(options, channels, authenticate, recheck, identify, scopes, logger)
    do
        core.Broadcaster <- (this :> ICableBroadcaster)
        // A stopping host closes its WebSockets (`server_restart`, so clients reconnect) instead of waiting out the shutdown timeout.
        lifetime.ApplicationStopping.Register(fun () -> this.Restart()) |> ignore

    member _.Options: CableOptions = options
    member _.Time: TimeProvider = time
    member _.Logger: ILogger = logger

    member internal _.Core: ServerCore<'U> = core

    /// Runs between a connection's registration and its re-check (test hook for A3).
    member internal _.AfterRegistered
        with get () = core.AfterRegistered
        and set value = core.AfterRegistered <- value

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

    /// `ActionCable.server.restart`: closes every connection with `server_restart`. A connection accepted after this
    /// (between `ApplicationStopping` and Kestrel's stop) is sent it right after its `welcome`.
    member _.Restart() : unit =
        core.BeginStopping()
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
      /// C10's second look; the default runs `Authenticate` again.
      Recheck: (HttpContext -> IServiceProvider -> Task<bool>) option
      Lifetime: IHostApplicationLifetime
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

    let withRecheck (recheck: HttpContext -> IServiceProvider -> Task<bool>) (builder: ServerBuilder<'U>) : ServerBuilder<'U> =
        { builder with Recheck = Some recheck }

    let withLifetime (lifetime: IHostApplicationLifetime) (builder: ServerBuilder<'U>) : ServerBuilder<'U> = { builder with Lifetime = lifetime }

    /// The provider commands take their scopes from (`Subscription.Services` is a scope of it).
    let withServices (services: IServiceProvider) (builder: ServerBuilder<'U>) : ServerBuilder<'U> = { builder with Services = services }

    let withLogger (logger: ILogger) (builder: ServerBuilder<'U>) : ServerBuilder<'U> = { builder with Logger = logger }

    /// The clock the heartbeat's `ping` frames and timer use.
    let withTime (time: TimeProvider) (builder: ServerBuilder<'U>) : ServerBuilder<'U> = { builder with Time = time }

    let build (builder: ServerBuilder<'U>) : CableServer<'U> =
        let channels = ChannelRegistry.create [ for KeyValue(name, factory) in builder.Channels -> name, factory ]
        let recheck =
            match builder.Recheck with
            | Some recheck -> recheck
            | None ->
                fun ctx _ ->
                    task {
                        let! user = builder.Authenticate ctx
                        return user.IsSome
                    }
        CableServer<'U>(
            Microsoft.Extensions.Options.Options.Create builder.Options,
            channels,
            builder.Authenticate,
            recheck,
            builder.Identify,
            builder.Services.GetRequiredService<IServiceScopeFactory>(),
            builder.Time,
            builder.Lifetime,
            builder.Logger
        )

/// A lifetime that never stops: a server built outside a host (tests, the bench) is stopped by `Restart`.
type private NeverStops() =
    interface IHostApplicationLifetime with
        member _.ApplicationStarted = CancellationToken.None
        member _.ApplicationStopping = CancellationToken.None
        member _.ApplicationStopped = CancellationToken.None
        member _.StopApplication() = ()

module Server =
    let private emptyServices: IServiceProvider = ServiceCollection().BuildServiceProvider()

    /// `ApplicationCable::Connection#connect` resolves the upgrade request to the connection's identity, or
    /// None for `reject_unauthorized_connection`. `identify` is `identified_by`: the connection
    /// identifier used for remote disconnects (for `identified_by :current_user`, the user's GID param,
    /// `Naming.gidParam`).
    let builder (options: CableOptions) (authenticate: HttpContext -> Task<'U option>) (identify: 'U -> string) : ServerBuilder<'U> =
        { Options = options
          Authenticate = authenticate
          Recheck = None
          Lifetime = NeverStops()
          Identify = identify
          Channels = Map.empty
          Services = emptyServices
          Time = TimeProvider.System
          Logger = NullLogger.Instance }
