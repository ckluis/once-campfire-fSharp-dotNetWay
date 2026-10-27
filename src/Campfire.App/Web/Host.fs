// plans/design-front.md F1-F6, F11-F13 (units 7.4a and 7.4b): the app's own host. Thruster's job is Kestrel's options and listeners plus the
// middleware of `Web.Services.pipeline`; this builds the `WebApplication` that serves them: HTTP_PORT and TARGET_PORT, and with
// `TLS_DOMAIN` the HTTPS listener (certificates from `CertificateStore`, ordered by `AcmeService`) and F6's branch on the HTTP port.
namespace Campfire.App.Web

open System
open System.Net
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
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

    /// The longest a TLS handshake may take when `HTTP_READ_TIMEOUT` means none (F3, checkpoint note 4): a name whose order keeps
    /// failing must not hold connections open for ever.
    let private fallbackHandshakeTimeout = TimeSpan.FromSeconds 30.0

    /// What a handshake negotiates (F3): the certificate for the client's server name (the first one awaited), as `store` has it.
    /// The handshake, the wait for a first certificate included, is bounded by `HTTP_READ_TIMEOUT`, or 30 s when that is none.
    let handshakeOptions (settings: FrontSettings) (store: CertificateStore) : TlsHandshakeCallbackOptions =
        let options = TlsHandshakeCallbackOptions()
        options.HandshakeTimeout <- (if settings.ReadTimeout = TimeSpan.MaxValue then fallbackHandshakeTimeout else settings.ReadTimeout)
        options.OnConnection <- Func<_, _>(store.HandshakeOptions)
        options

    /// The listeners (F2) and the timeouts (F12): `HTTP_PORT` on every address, HTTP/1.1; `TARGET_PORT` on `TARGET_BIND`
    /// (skipped with a warning when it is the HTTP port, or the HTTPS port with TLS); with a certificate store, `HTTPS_PORT` on
    /// every address, HTTP/1.1 and HTTP/2 over TLS (F3). `HTTP_IDLE_TIMEOUT` is `KeepAliveTimeout` and `HTTP_READ_TIMEOUT` is
    /// `RequestHeadersTimeout`. Bodies fall under Kestrel's default minimum data rates.
    let listen (settings: FrontSettings) (store: CertificateStore voption) (logger: ILogger) (options: KestrelServerOptions) : unit =
        options.Limits.KeepAliveTimeout <- settings.IdleTimeout
        options.Limits.RequestHeadersTimeout <- settings.ReadTimeout
        let tls = store.IsSome
        if settings.TargetPort = settings.HttpPort || (tls && settings.TargetPort = settings.HttpsPort) then
            logger.LogWarning("TARGET_PORT is the front server's port; not listening on it separately port={Port}", settings.TargetPort)
        else
            options.Listen(settings.TargetBind, settings.TargetPort, fun l -> l.Protocols <- HttpProtocols.Http1)
        options.ListenAnyIP(settings.HttpPort, fun l -> l.Protocols <- HttpProtocols.Http1)
        match store with
        | ValueSome store ->
            options.ListenAnyIP(
                settings.HttpsPort,
                fun l ->
                    l.Protocols <- HttpProtocols.Http1AndHttp2
                    l.UseHttps(handshakeOptions settings store) |> ignore
            )
        | ValueNone -> ()

    /// F6: the HTTP port with TLS on. A Host outside `TLS_DOMAIN` (normalized as the handshake normalizes names) is a 421, and its ACME
    /// challenge a 403, as Thruster's and the old port's host check answered; the ACME challenge of a `TLS_DOMAIN` host is the store's
    /// token, and everything else is a 301 to the same target over HTTPS, on `HTTPS_PORT` when it is not 443.
    let httpPortHandler (settings: FrontSettings) (store: CertificateStore) : RequestDelegate =
        let challenge = AcmeEndpoints.challenge store
        let plain (http: HttpContext) (status: int) (message: string) : Task =
            let body = Encoding.UTF8.GetBytes(message + "\n")
            http.Response.StatusCode <- status
            http.Response.ContentType <- "text/plain; charset=utf-8"
            http.Response.Headers["X-Content-Type-Options"] <- "nosniff"
            http.Response.ContentLength <- int64 body.Length
            http.Response.Body.WriteAsync(ReadOnlyMemory<byte> body).AsTask()
        let escape (s: string) = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&#34;").Replace("'", "&#39;")
        RequestDelegate(fun http ->
            let host = http.Request.Host.Host
            let domain =
                match ServerNames.normalize host with
                | Ok d when List.contains d store.Domains -> ValueSome d
                | _ -> ValueNone
            let path = match http.Request.Path.Value with null -> "" | p -> p
            match domain with
            | ValueNone when path.StartsWith(AcmeEndpoints.Prefix, StringComparison.Ordinal) ->
                plain http 403 $"acme/autocert: host \"{host}\" not configured in HostWhitelist"
            | ValueNone ->
                http.Response.Headers.Connection <- "close"
                plain http 421 "Misdirected Request"
            | ValueSome _ when path.StartsWith(AcmeEndpoints.Prefix, StringComparison.Ordinal) -> challenge.Invoke http
            | ValueSome domain ->
                let target =
                    match http.Features.Get<IHttpRequestFeature>() with
                    | null -> path + http.Request.QueryString.Value
                    | feature ->
                        if feature.RawTarget.StartsWith '/' then
                            feature.RawTarget
                        else
                            http.Request.Path.ToUriComponent() + http.Request.QueryString.ToUriComponent()
                let port = if settings.HttpsPort = 443 then "" else $":{settings.HttpsPort}"
                let url = $"https://{domain}{port}{target}"
                http.Response.Headers.Connection <- "close"
                http.Response.StatusCode <- 301
                http.Response.Headers.Location <- url
                let meth = http.Request.Method
                if meth = "GET" || meth = "HEAD" then http.Response.ContentType <- "text/html; charset=utf-8"
                if meth = "GET" then
                    let body = Encoding.UTF8.GetBytes($"<a href=\"{escape url}\">Moved Permanently</a>.\n\n")
                    http.Response.ContentLength <- int64 body.Length
                    http.Response.Body.WriteAsync(ReadOnlyMemory<byte> body).AsTask()
                else
                    http.Response.ContentLength <- 0L
                    Task.CompletedTask)

    /// `TLS_DOMAIN` names `ServerNames.normalize` refuses are never served: logged, and a list with none left is a failed boot
    /// (a TLS install with no name could not hand any client a certificate).
    let validateDomains (settings: FrontSettings) (logger: ILogger) : unit =
        let refused =
            settings.TlsDomains
            |> List.choose (fun d -> match ServerNames.normalize d with Error reason -> Some(d, reason) | Ok _ -> None)
        for domain, reason in refused do
            logger.LogError("TLS_DOMAIN name is not served: {Domain}: {Reason}", domain, reason)
        if refused.Length = settings.TlsDomains.Length && not settings.TlsDomains.IsEmpty then
            invalidOp "TLS_DOMAIN holds no usable name"

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
        let logger = loggers.CreateLogger "campfire"
        let store =
            if settings.HasTls then
                validateDomains settings logger
                ValueSome(CertificateStore settings.TlsDomains)
            else
                ValueNone
        let builder = WebApplication.CreateSlimBuilder(StaticAssets.hostOptions hostArgs)
        builder.Services.AddSingleton<ILoggerFactory>(loggers) |> ignore
        builder.Services.Configure<HostOptions>(fun (options: HostOptions) -> options.ShutdownTimeout <- ShutdownGrace) |> ignore
        configureServices builder.Services
        match store with
        | ValueSome store ->
            builder.Services.TryAddSingleton<TimeProvider>(TimeProvider.System)
            builder.Services.AddSingleton<FrontSettings>(settings).AddSingleton<CertificateStore>(store) |> ignore
            builder.Services.AddHttpClient "acme" |> ignore
            builder.Services.AddHostedService<AcmeService>() |> ignore
        | ValueNone -> ()
        builder.Services.PostConfigure<ForwardedHeadersOptions>(configureForwarding settings) |> ignore
        StaticAssets.configureHost builder
        builder.WebHost.ConfigureKestrel(fun (options: KestrelServerOptions) ->
            configureKestrel options
            listen settings store logger options)
        |> ignore
        let app = builder.Build()
        match store with
        | ValueSome store ->
            let handler = httpPortHandler settings store
            let onHttpPort (http: HttpContext) = http.Connection.LocalPort = settings.HttpPort
            let logged = match app.Services.GetService<EdgeConfig>() with null -> false | edge -> edge.LogRequests
            app.MapWhen(
                onHttpPort,
                fun branch ->
                    // The pipeline's own `UseHttpLogging` is not on this branch: log it first here too.
                    if logged then branch.UseHttpLogging() |> ignore
                    branch.Run handler
            )
            |> ignore
        | ValueNone -> ()
        pipeline app
        app
