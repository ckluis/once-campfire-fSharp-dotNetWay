// plans/design-kit-auth.md section 3 (Web/Services.fs): what Phase 2 registers, and the pipeline.
//
// Unit 2.1 is the seam: the services later units use are resolvable from a request (the edge settings and the app state, the
// authentication schemes, ASP.NET Core session, the framework's CSRF protection), and nothing runs them yet, so a
// request behaves as before. Units 2.2-2.8 add the cookie scheme, the bot-key scheme, `UseSession`, the forwarded
// headers, compression and the rest.
namespace Campfire.App.Web

open System
open Falco
open Falco.Routing
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Diagnostics
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.ResponseCompression
open Microsoft.Extensions.Options
open Microsoft.Extensions.DependencyInjection
open Campfire.App
open Campfire.RailsCompat

/// K11: `UseResponseCompression`, replacing `Rack::Deflater`.
module Compression =
    /// Bodies shorter than this go out as they are (a 204's nothing, a redirect's few bytes, a turbo-stream `remove`).
    [<Literal>]
    let MinimumLength = 256L

    let private mimeTypes =
        Seq.append
            ResponseCompressionDefaults.MimeTypes
            [ "text/vnd.turbo-stream.html"; "text/javascript"; "image/svg+xml"; "application/manifest+json" ]

    let configure (options: ResponseCompressionOptions) : unit =
        options.EnableForHttps <- true
        options.MimeTypes <- mimeTypes
        options.Providers.Add<BrotliCompressionProvider>()
        options.Providers.Add<GzipCompressionProvider>()
        // F10: after Brotli and Gzip, so a browser that offers `br` keeps it and a zstd-only client still gets zstd.
        options.Providers.Add<ZstandardCompressionProvider>()

    /// An error status with no body (`head :not_found`, `head :forbidden`): Rails' `Rack::Deflater` gzips it into a few bytes of
    /// gzip, and Firefox, given an error document of zero bytes, shows its own "empty response" error page instead of a blank one.
    let private emptyError (response: HttpResponse) : bool =
        response.StatusCode >= 400 && response.ContentLength.HasValue && response.ContentLength.Value = 0L

    /// Starts an empty error response that the handlers left unstarted, so the compression middleware (it sits just outside
    /// this) compresses it as Rails does when the client takes gzip or Brotli; without a write it would leave it alone.
    let compressEmptyErrors: Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task> =
        Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task>(fun http next ->
            task {
                do! next.Invoke http
                if not http.Response.HasStarted && emptyError http.Response then do! http.Response.StartAsync()
            }
            :> System.Threading.Tasks.Task)

    /// The framework's provider, except that a response whose length the app already said is under `MinimumLength` is
    /// left alone (every response `Pages` and `Respond` send carries its `Content-Length`), unless it is an empty error.
    type SkipSmall(inner: IResponseCompressionProvider) =
        interface IResponseCompressionProvider with
            member _.GetCompressionProvider http = inner.GetCompressionProvider http
            member _.CheckRequestAcceptsCompression http = inner.CheckRequestAcceptsCompression http

            member _.ShouldCompressResponse http =
                let length = http.Response.ContentLength
                (not length.HasValue || length.Value >= MinimumLength || emptyError http.Response)
                && inner.ShouldCompressResponse http

module Services =
    /// Cookie name of ASP.NET Core's session, which holds the flash and `return_to_after_authenticating` (K6): not
    /// Rails' `_campfire_session`, which stays untouched.
    [<Literal>]
    let FlashCookie = "_campfire_flash"

    /// K2: the one registered scheme must not become the default, or every request (assets too) authenticates.
    [<Literal>]
    let SuppressAutoDefaultScheme = "Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme"

    /// The raw request path, still percent-encoded and without its query (`PATH_INFO`).
    let rawPath (http: HttpContext) : string = Respond.rawPath http

    /// K1: the `session_token` cookie; K4: the bot key, a second scheme (no policy scheme, no default). Needs `Secrets` and
    /// `TimeProvider` in the container (the cookie's signed ticket), and the scoped `CampfireDb` for the session lookup.
    let addAuthentication (services: IServiceCollection) : unit =
        AppContext.SetSwitch(SuppressAutoDefaultScheme, true)
        services
            .AddAuthentication()
            .AddCookie(Schemes.Session, SessionCookie.configure)
            .AddScheme<AuthenticationSchemeOptions, BotKeyAuthenticationHandler>(Schemes.BotKey, ignore)
        |> ignore
        services
            .AddOptions<CookieAuthenticationOptions>(Schemes.Session)
            .Configure<Secrets, TimeProvider>(fun options secrets time ->
                options.TicketDataFormat <- RailsSignedTicketFormat(secrets, time)
                options.TimeProvider <- time
                options.Events.OnValidatePrincipal <- Func<CookieValidatePrincipalContext, System.Threading.Tasks.Task>(SessionCookie.validatePrincipal))
        |> ignore


    /// Registers the app's services on the host (`Front.serveHosted`'s `configureServices`, and the tests' hosts).
    let add (edge: EdgeConfig) (app: AppState) (services: IServiceCollection) : unit =
        AppContext.SetSwitch(SuppressAutoDefaultScheme, true)
        services.AddSingleton<EdgeConfig>(edge) |> ignore
        services.AddSingleton<Secrets>(app.Secrets) |> ignore
        services.AddSingleton<AppState>(app) |> ignore
        // K6: session state lives in server memory, so its keys are ephemeral too.
        services.AddDataProtection().UseEphemeralDataProtectionProvider() |> ignore
        services.AddDistributedMemoryCache(fun options -> options.SizeLimit <- 64L * 1024L * 1024L) |> ignore
        services.AddSession(fun options ->
            options.Cookie.Name <- FlashCookie
            options.Cookie.HttpOnly <- true
            options.Cookie.SameSite <- SameSiteMode.Lax
            options.Cookie.IsEssential <- true
            options.IdleTimeout <- TimeSpan.FromHours 2.0)
        |> ignore
        // K10: cookies set through `Response.Cookies` (`last_room`, the session's own) are `secure` when the request is HTTPS,
        // never below `Lax`.
        services.Configure<CookiePolicyOptions>(fun (options: CookiePolicyOptions) ->
            options.Secure <- CookieSecurePolicy.SameAsRequest
            options.MinimumSameSitePolicy <- SameSiteMode.Lax)
        |> ignore
        // K10: `X-Forwarded-For/-Proto/-Host` from Rails' `TRUSTED_PROXIES` only; form limits for `_method`'s reader.
        services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(fun options ->
            Edge.configureForwardedHeaders edge options)
        |> ignore
        services.Configure<FormOptions>(Edge.configureForms) |> ignore
        // K11: gzip and Brotli at their fastest, what Rails compresses plus Turbo's streams; a body already smaller than
        // `Compression.MinimumLength` is not worth the CPU.
        services.AddResponseCompression(Compression.configure) |> ignore
        if edge.LogRequests then HttpLog.add services
        services.AddSingleton<IResponseCompressionProvider>(fun sp ->
            Compression.SkipSmall(ResponseCompressionProvider(sp, sp.GetRequiredService<IOptions<ResponseCompressionOptions>>()))
            :> IResponseCompressionProvider)
        |> ignore
        // V5, V7: the kept compressed pages (and from unit 4.3 the fragments), in HybridCache over a size-limited memory cache.
        services.AddSingleton<Microsoft.Extensions.Caching.Hybrid.HybridCache>(app.Fragments) |> ignore
        // K15: ten sign-in attempts per client IP in three minutes.
        services.AddSingleton<System.Threading.RateLimiting.PartitionedRateLimiter<string>>(fun _ -> LoginThrottle.create ()) |> ignore
        addAuthentication services

    /// A bot API path: `/rooms/:room_id/:bot_key/messages...`, the only routes that read `raw_request_body`.
    let private isBotApiPath (path: string) : bool =
        path.StartsWith("/rooms/", StringComparison.Ordinal)
        && (let segments = path.Split '/'
            segments.Length >= 5 && (segments[4] = "messages" || segments[4].StartsWith("messages.", StringComparison.Ordinal)))

    /// Keeps the bytes of a bot API request's body for `Params.rawBody`, ahead of the method override, which reads a form body
    /// and would consume them (`curl -d 'Hello'` is a form post). The stream is rewound for the readers after it. A multipart body
    /// is not kept: its `raw_request_body` is not what the bot posted, and the attachment is the message.
    let keepRawBody: Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task> =
        Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task>(fun http next ->
            task {
                let request = http.Request
                let hasBody = not (HttpMethods.IsGet request.Method || HttpMethods.IsHead request.Method)
                let multipart = (match request.ContentType with null -> false | ct -> ct.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                if hasBody && not multipart && isBotApiPath (rawPath http) then
                    use buffer = new System.IO.MemoryStream()
                    do! request.Body.CopyToAsync buffer
                    let bytes = buffer.ToArray()
                    http.Items[Params.RawBodyKey] <- box bytes
                    request.Body <- new System.IO.MemoryStream(bytes, false)
                return! next.Invoke http
            }
            :> System.Threading.Tasks.Task)

    /// The app's branch of the host: the pre-routing middleware (`Edge`), the asset endpoints (assets and public files), `/cable`, then the route table
    /// (`dispatch`) as Falco's not-found handler, so ASP.NET Core's own routing never rewrites a path.
    let pipeline (edge: EdgeConfig) (cable: HttpHandler) (dispatch: Campfire.App.Web.Action) (builder: IApplicationBuilder) : unit =
        // F8: first, so the line's duration covers everything and an exception page's status is the one logged.
        if edge.LogRequests then builder.UseHttpLogging() |> ignore
        builder.UseExceptionHandler(ExceptionHandlerOptions(ExceptionHandler = PublicExceptions.exceptionHandler)) |> ignore
        builder.UseForwardedHeaders() |> ignore
        builder.Use(Edge.assumeSsl edge) |> ignore
        builder.Use(Edge.defaultHeaders edge) |> ignore
        // K11: assets and public files are served precompressed by the endpoints (`ActionDispatch::Static`'s place), so only what the
        // app renders pays for compression.
        StaticAssets.map builder
        // `public/404.html` and the others, read from the web root now that it is known (`ActionDispatch::PublicExceptions`).
        let env = builder.ApplicationServices.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>()
        edge.ErrorPages.Fill
            [ for status in [ 404; 422; 500; 502 ] do
                  let file = StaticAssets.webRootFile env $"{status}.html"
                  if file.Exists then
                      use stream = file.CreateReadStream()
                      use buffer = new System.IO.MemoryStream()
                      stream.CopyTo buffer
                      status, ReadOnlyMemory<byte>(buffer.ToArray()) ]
        builder.UseResponseCompression() |> ignore
        builder.Use(Compression.compressEmptyErrors) |> ignore
        builder.Use(Edge.bodyLimit) |> ignore
        builder.Use(keepRawBody) |> ignore
        Edge.useMethodOverride builder
        builder.UseCookiePolicy() |> ignore
        builder.UseSession() |> ignore
        builder.UseWebSockets() |> ignore
        builder.UseRouting() |> ignore
        StaticAssets.appEndpointsOnly builder
        builder.UseFalco [ any Campfire.Cable.Endpoint.MountPath cable ] |> ignore
        builder.UseFalcoNotFound(Campfire.App.Web.Action.run dispatch) |> ignore
