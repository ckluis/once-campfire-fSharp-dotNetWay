// Port of rust/crates/kit/src/front.rs
//
// The front server: what Thruster 0.1.23 (github.com/basecamp/thruster, `internal/`) did in front of
// the reference's Puma (`thrust bin/start-app`), in process.
//
// - HTTP/1.1 on HTTP_PORT (80), and HTTP/2 without TLS with H2C_ENABLED (`server.go`).
// - With TLS_DOMAIN: HTTPS with HTTP/2 on HTTPS_PORT (443) and certificates from ACME (`Acme.fs`,
//   stored where Thruster stores them), while HTTP_PORT only answers HTTP-01 challenges and redirects
//   to HTTPS (`Tls.fs`).
// - For every request: the in-memory response cache (`Cache.fs`), compression on top of the app's own
//   gzip (`Compression.fs`), `X-Forwarded-*` and `X-Request-Start`, MAX_REQUEST_BODY, the
//   HTTP_*_TIMEOUTs and a request log line (`Handler.fs`, `Conn.fs`).
// - The app still listens on TARGET_PORT (3000) by itself, as Puma did behind Thruster, but only on
//   loopback unless TARGET_BIND says otherwise, and with the front's timeouts and MAX_REQUEST_BODY.
//
// Not carried over, because nothing reaches them: X-Sendfile (Rack 3.2's `Rack::Sendfile` no longer
// honours the `X-Sendfile-Type` request header Thruster sends, and the reference configures no
// `x_sendfile_header`, so it never sends `X-Sendfile`), BAD_GATEWAY_PAGE (there's no upstream process
// to be unreachable), and RSA certificates (autocert gives them only to clients that can't do ECDSA).
//
// Kestrel serves all the listeners from one host, the app's pipeline built once and shared: a request
// is told apart by the port it came in on. Where Rust has hyper serve each connection with the
// service for its listener, here Kestrel's one pipeline calls `dispatch`, which picks the service.
namespace Campfire.Kit

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Connections
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions

/// The host serves its requests until `Front.serve`'s shutdown task completes; nothing else stops it
/// (the signals are the caller's, see `Server.shutdownSignal`).
[<Sealed>]
type internal NoLifetime() =
    interface IHostLifetime with
        member _.WaitForStartAsync(_: Threading.CancellationToken) = Task.CompletedTask
        member _.StopAsync(_: Threading.CancellationToken) = Task.CompletedTask

module Front =
    /// hyper's default `max_buf_size` (8192 + 4096 * 100): the most bytes of request line and headers the
    /// Rust port reads before it answers 431.
    let private maxRequestHead = 417_792
    /// hyper's `MAX_URI_LEN` (414 past it), plus room for the method and ` HTTP/1.1`, since Kestrel limits
    /// the whole request line where hyper limits the target alone.
    let private maxRequestLine = 65_534 + "OPTIONS ".Length + " HTTP/1.1".Length

    /// Settings Campfire's Kestrel needs, whatever else the host configures: no `Server` header,
    /// the app's own body limits (413) instead of Kestrel's 30 MB, and header values written as UTF-8
    /// the way Puma wrote them.
    let configureKestrel (options: KestrelServerOptions) : unit =
        options.AddServerHeader <- false
        options.Limits.MaxRequestBodySize <- Nullable()
        // Kestrel's 32 KB of headers and 8 KB request line refuse requests Puma (112 KB of headers, 12 KB
        // of URI) and hyper (a 417,792-byte read buffer) serve: a browser with many cookies on a shared
        // domain would be locked out of every page. The ceiling is hyper's, the larger of the two
        // (`MaxRequestBufferSize` stays at its 1 MB, above both).
        options.Limits.MaxRequestHeadersTotalSize <- maxRequestHead
        options.Limits.MaxRequestLineSize <- maxRequestLine
        options.ResponseHeaderEncodingSelector <- (fun _ -> System.Text.Encoding.UTF8)

    /// How long connections get to finish after shutdown (Thruster's `Stop`).
    let StopGrace = TimeSpan.FromSeconds 5.0

    /// Serves `app` (the pipeline of the kit, `Adapter.app` and what comes before it, built onto the
    /// builder it's given) the way Thruster served the reference until `shutdown` completes.
    /// `acme` replaces the configuration's ACME options (tests use a local CA), and `loggerFactory`
    /// is where the server and Kestrel log (`configureServices` registers the app's services, which `HttpContext.RequestServices` resolves; the requests, as `thruster`, unless `lines` is given: the host then
    /// writes them itself, as bytes, see `RequestLog`).
    let serveWithLines
        (config: FrontConfig)
        (app: IApplicationBuilder -> unit)
        (acme: AcmeOptions voption)
        (loggerFactory: ILoggerFactory)
        (configureServices: IServiceCollection -> unit)
        (lines: RequestLines | null)
        (shutdown: Task)
        : Task =
        task {
            let logger = loggerFactory.CreateLogger "thruster"
            let services = FrontServices(config, logger, lines)
            let timeouts =
                { Idle = config.HttpIdleTimeout
                  Read = config.HttpReadTimeout
                  Write = config.HttpWriteTimeout }
            let certs =
                if config.HasTls then
                    new CertManager(acme |> ValueOption.defaultWith (fun () -> AcmeOptions.fromConfig logger config), logger)
                else
                    null
            let upstream =
                if config.TargetPort = config.HttpPort || (config.HasTls && config.TargetPort = config.HttpsPort) then
                    logger.LogWarning("TARGET_PORT is the front server's port; not listening on it separately port={Port}", config.TargetPort)
                    false
                else
                    true
            let h2c = config.H2cEnabled && not config.HasTls
            if h2c && not FrontH2c.available then
                logger.LogWarning("H2C_ENABLED, but Kestrel's connection internals aren't as expected; serving HTTP/1.1 only")

            // appsettings.json is read with reloadOnChange by default, which starts a FileSystemWatcher on the whole content
            // root (recursive): every write SQLite makes under storage/ then wakes the "File Watch" thread, which lstats the
            // path (about 5% of a post's CPU). Nothing here reloads configuration, so the host is told not to watch.
            let builder = WebApplication.CreateSlimBuilder [| "--hostBuilder:reloadConfigOnChange=false" |]
            builder.Services.AddSingleton<ILoggerFactory>(loggerFactory) |> ignore
            builder.Services.AddSingleton<IHostLifetime, NoLifetime>() |> ignore
            configureServices builder.Services
            builder.WebHost.ConfigureKestrel(fun (options: KestrelServerOptions) ->
                configureKestrel options
                FrontConn.configureLimits options timeouts
                if upstream then
                    options.Listen(config.TargetBind, config.TargetPort, fun listen -> listen.Protocols <- HttpProtocols.Http1)
                options.ListenAnyIP(
                    config.HttpPort,
                    fun listen ->
                        if h2c && FrontH2c.available then
                            listen.Protocols <- HttpProtocols.Http1AndHttp2
                            listen.Use(FrontH2c.sniff (max timeouts.Idle timeouts.Read)) |> ignore
                        else
                            listen.Protocols <- HttpProtocols.Http1
                )
                if config.HasTls then
                    options.ListenAnyIP(
                        config.HttpsPort,
                        fun listen ->
                            listen.Protocols <- HttpProtocols.Http1AndHttp2
                            listen.Use(FrontTls.sniffAcme timeouts.Read) |> ignore
                            listen.UseHttps(FrontTls.handshakeOptions certs timeouts.Read) |> ignore
                    ))
            |> ignore
            let host = builder.Build()

            // The app's pipeline, built once for the front listeners and the app's own.
            let branch = (host :> IApplicationBuilder).New()
            app branch
            let appDelegate = branch.Build()
            let handler = FrontHandler(services, appDelegate)
            let maxBody = services.MaxRequestBody
            let tls = config.HasTls
            let dispatch: RequestDelegate =
                RequestDelegate(fun ctx ->
                    let port = ctx.Connection.LocalPort
                    if upstream && port = config.TargetPort then
                        task {
                            let! within = FrontProxy.withinLimit ctx maxBody
                            if within then do! appDelegate.Invoke ctx else FrontProxy.tooLarge ctx
                        }
                        :> Task
                    elif tls && port = config.HttpPort then
                        FrontTls.httpHandler certs ctx
                    else
                        handler.Invoke ctx)
            (host :> IApplicationBuilder).Run(FrontConn.withDeadlines timeouts dispatch)

            do! host.StartAsync()
            if config.HasTls then
                logger.LogInformation("Server started http=:{Http} https=:{Https} tls_domain={Domains}", config.HttpPort, config.HttpsPort, String.Join(",", config.TlsDomains))
            else
                logger.LogInformation("Server started http=:{Http}", config.HttpPort)
            do! shutdown
            use grace = new Threading.CancellationTokenSource(StopGrace)
            try
                do! host.StopAsync grace.Token
            with :? OperationCanceledException ->
                ()
            do! host.DisposeAsync()
            match box certs with
            | :? IDisposable as disposable -> disposable.Dispose()
            | _ -> ()
            logger.LogInformation "Server stopped"
        }

    /// `serveWithLines` with the requests logged through `loggerFactory`.
    let serveWith
        (config: FrontConfig)
        (app: IApplicationBuilder -> unit)
        (acme: AcmeOptions voption)
        (loggerFactory: ILoggerFactory)
        (shutdown: Task)
        : Task =
        serveWithLines config app acme loggerFactory ignore null shutdown

    /// `serveWith`, with the configuration's ACME options and no logging but the requests'.
    let serve (config: FrontConfig) (app: IApplicationBuilder -> unit) (shutdown: Task) : Task =
        serveWith config app ValueNone NullLoggerFactory.Instance shutdown
