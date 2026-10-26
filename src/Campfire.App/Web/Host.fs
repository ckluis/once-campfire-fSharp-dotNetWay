// plans/design-front.md F1-F3, F11-F13 (unit 7.4a): the app's own host. Thruster's job is Kestrel's options and listeners plus the
// middleware of `Web.Services.pipeline`; this builds the `WebApplication` that serves them. Plain HTTP only: the TLS listener, F6's
// branch on the HTTP port and the ACME service arrive in 7.4b, and an install with `TLS_DOMAIN` set stays on the old front until then.
namespace Campfire.App.Web

open System
open System.Net
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

module Host =
    /// How long in-flight requests get after SIGTERM/SIGINT (F13): one budget for the generic host's whole stop.
    let ShutdownGrace = TimeSpan.FromSeconds 10.0

    /// hyper's default `max_buf_size` (8192 + 4096 * 100): the most bytes of request line and headers the
    /// Rust port reads before it answers 431.
    let private maxRequestHead = 417_792

    /// hyper's `MAX_URI_LEN` (414 past it), plus room for the method and ` HTTP/1.1`, since Kestrel limits
    /// the whole request line where hyper limits the target alone.
    let private maxRequestLine = 65_534 + "OPTIONS ".Length + " HTTP/1.1".Length

    /// Settings Campfire's Kestrel needs, whatever else the host configures: no `Server` header, the app's own body limits
    /// (413) instead of Kestrel's 30 MB, header values written as UTF-8 the way Puma wrote them, and head limits as large as
    /// Puma's and hyper's (Kestrel's 32 KB of headers and 8 KB request line would lock out a browser with many cookies on a
    /// shared domain; `MaxRequestBufferSize` stays at its 1 MB, above both).
    let configureKestrel (options: KestrelServerOptions) : unit =
        options.AddServerHeader <- false
        options.Limits.MaxRequestBodySize <- Nullable()
        options.Limits.MaxRequestHeadersTotalSize <- maxRequestHead
        options.Limits.MaxRequestLineSize <- maxRequestLine
        options.ResponseHeaderEncodingSelector <- (fun _ -> System.Text.Encoding.UTF8)

    /// The listeners (F2) and the timeouts (F12): `HTTP_PORT` on every address, HTTP/1.1; `TARGET_PORT` on `TARGET_BIND`
    /// (skipped with a warning when it is the HTTP port); `HTTP_IDLE_TIMEOUT` is `KeepAliveTimeout` and `HTTP_READ_TIMEOUT` is
    /// `RequestHeadersTimeout`. Bodies fall under Kestrel's default minimum data rates.
    let listen (settings: FrontSettings) (logger: ILogger) (options: KestrelServerOptions) : unit =
        options.Limits.KeepAliveTimeout <- settings.IdleTimeout
        options.Limits.RequestHeadersTimeout <- settings.ReadTimeout
        if settings.TargetPort = settings.HttpPort then
            logger.LogWarning("TARGET_PORT is the front server's port; not listening on it separately port={Port}", settings.TargetPort)
        else
            options.Listen(settings.TargetBind, settings.TargetPort, fun l -> l.Protocols <- HttpProtocols.Http1)
        options.ListenAnyIP(settings.HttpPort, fun l -> l.Protocols <- HttpProtocols.Http1)

    /// The host's arguments: configuration is not watched. `appsettings.json` is read with `reloadOnChange` by default, which
    /// starts a FileSystemWatcher on the whole content root (recursive): every write SQLite makes under `storage/` then wakes the
    /// "File Watch" thread, which lstats the path (about 5% of a post's CPU). Nothing here reloads configuration.
    let hostArgs: string[] = [| "--hostBuilder:reloadConfigOnChange=false" |]

    /// F11: with `FORWARD_HEADERS` off a client's `X-Forwarded-*` is never honoured (an empty list of known proxies would trust every
    /// peer, so the middleware is turned off instead); with it on, Rails' `TRUSTED_PROXIES` (`Edge.configureForwardedHeaders`).
    let configureForwarding (settings: FrontSettings) (options: ForwardedHeadersOptions) : unit =
        if not settings.ForwardHeaders then options.ForwardedHeaders <- ForwardedHeaders.None

    /// The host (F1): the app's services (`configureServices`), Kestrel's listeners and limits, ConsoleLifetime's shutdown over
    /// `ShutdownGrace`, and the app's pipeline (`pipeline`) on the host itself. The caller runs it (`RunAsync`).
    let build
        (settings: FrontSettings)
        (loggers: ILoggerFactory)
        (configureServices: IServiceCollection -> unit)
        (pipeline: IApplicationBuilder -> unit)
        : WebApplication =
        if settings.HasTls then invalidOp "Web.Host serves plain HTTP only until unit 7.4b adds the TLS listener"
        let logger = loggers.CreateLogger "campfire"
        let builder = WebApplication.CreateSlimBuilder(StaticAssets.hostOptions hostArgs)
        builder.Services.AddSingleton<ILoggerFactory>(loggers) |> ignore
        builder.Services.Configure<HostOptions>(fun (options: HostOptions) -> options.ShutdownTimeout <- ShutdownGrace) |> ignore
        configureServices builder.Services
        builder.Services.PostConfigure<ForwardedHeadersOptions>(configureForwarding settings) |> ignore
        StaticAssets.configureHost builder
        builder.WebHost.ConfigureKestrel(fun (options: KestrelServerOptions) ->
            configureKestrel options
            listen settings logger options)
        |> ignore
        let app = builder.Build()
        pipeline app
        app
