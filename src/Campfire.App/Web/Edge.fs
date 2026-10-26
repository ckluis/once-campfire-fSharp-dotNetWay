// plans/design-kit-auth.md section 4, unit 2.13 (Web/Edge.fs): the middleware that runs ahead of the route table.
//
// `ActionDispatch::SSL` (`force_ssl`'s redirect and HSTS, secure cookies), `ActionDispatch::AssumeSSL`, `ActionDispatch::RequestId`,
// `Rack::Runtime`, `Rack::MethodOverride` and the 16 MiB body cap, over the framework's forwarded headers and form options.
// `EdgeConfig` is what they read (and the public error pages): a singleton in the host's services.
namespace Campfire.App.Web

open System
open System.Diagnostics
open System.Net
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.Extensions.Primitives

/// The `public/<status>.html` pages, held in memory. Empty until `Fill` (the pipeline reads them from the web root through
/// `StaticAssets.webRootFile` when it is built); a page asked for before then is absent.
[<Sealed>]
type ErrorPages() =
    let mutable pages = System.Collections.Generic.Dictionary<int, ReadOnlyMemory<byte>>()

    static member Empty = ErrorPages()

    member _.Fill(loaded: (int * ReadOnlyMemory<byte>) seq) : unit =
        System.Threading.Volatile.Write(&pages, System.Collections.Generic.Dictionary<int, ReadOnlyMemory<byte>>(dict loaded))

    member _.Get(status: int) : ReadOnlyMemory<byte> voption =
        match System.Threading.Volatile.Read(&pages).TryGetValue status with
        | true, page -> ValueSome page
        | _ -> ValueNone

/// The edge's settings.
type EdgeConfig =
    { /// `config.action_dispatch.trusted_proxies`; Rails' `TRUSTED_PROXIES` by default.
      TrustedProxies: System.Net.IPNetwork[]
      /// `config.assume_ssl`: treat every request as HTTPS (TLS terminates at the proxy).
      AssumeSsl: bool
      /// `config.force_ssl`: send HSTS and flag cookies `secure`. Production pairs it with `assume_ssl`, which makes every request HTTPS, so the redirect `force_ssl` also does
      /// can never fire: the only HTTP-to-HTTPS redirect an install sends is the HTTP port's with TLS (F6).
      ForceSsl: bool
      /// The HSTS header value sent with `ForceSsl` (`ssl_options = { hsts: { subdomains: true } }`: 730 days, `HstsOptions`' values).
      /// Written by `onStarting`, not by `UseHsts`: the framework's middleware sets its header before the next one runs, and
      /// `UseExceptionHandler` clears the headers of a 500 (HS5), where Rails' outermost `ActionDispatch::SSL` still sends it.
      Hsts: string
      /// `action_dispatch.default_headers` (`load_defaults 7.1`).
      DefaultHeaders: (string * string)[]
      /// `public/404.html`, `422.html`, `500.html`, ... (`ActionDispatch::PublicExceptions`).
      ErrorPages: ErrorPages
      /// `LOG_REQUESTS` (production, default on): one `UseHttpLogging` line per request. The test hosts leave it off.
      LogRequests: bool
      /// `CACHE_SIZE` (the output cache's capacity in bytes) and `MAX_CACHE_ITEM_SIZE`; a size of 0 leaves the cache out (the test hosts).
      CacheSize: int64
      MaxCacheItemSize: int64
      /// The cap on a request body: 16 MiB (`Edge.MaxRequestBody`), or `MAX_REQUEST_BODY` when that is set lower.
      MaxRequestBody: int64 }

module EdgeConfig =
    /// `ActionDispatch::RemoteIp::TRUSTED_PROXIES`.
    let defaultTrustedProxies: System.Net.IPNetwork[] =
        [| "127.0.0.0/8"; "::1/128"; "fc00::/7"; "10.0.0.0/8"; "172.16.0.0/12"; "192.168.0.0/16"; "169.254.0.0/16"; "fe80::/10" |]
        |> Array.map System.Net.IPNetwork.Parse

    /// Campfire's production settings: `assume_ssl` and `force_ssl` unless `DISABLE_SSL` is set
    /// (`reference/config/environments/production.rb`).
    let production (disableSsl: bool) (errorPages: ErrorPages) : EdgeConfig =
        { TrustedProxies = defaultTrustedProxies
          AssumeSsl = not disableSsl
          ForceSsl = not disableSsl
          Hsts = "max-age=63072000; includeSubDomains"
          DefaultHeaders =
            [| "x-frame-options", "SAMEORIGIN"
               "x-xss-protection", "0"
               "x-content-type-options", "nosniff"
               "x-permitted-cross-domain-policies", "none"
               "referrer-policy", "strict-origin-when-cross-origin" |]
          ErrorPages = errorPages
          LogRequests = false
          CacheSize = 0L
          MaxCacheItemSize = 0L
          MaxRequestBody = 16L * 1024L * 1024L }

/// What the middleware learned about a request, kept in `HttpContext.Items`.
[<Sealed>]
type RequestState(http: HttpContext) =
    member _.Http = http

    /// `ActionDispatch::RequestId`, for the `X-Request-Id` header.
    member val RequestId = "" with get, set

    member val Started = Stopwatch.GetTimestamp() with get, set

    /// The response comes from outside the middleware the app installs below `ActionDispatch::Static`
    /// (`Rack::Runtime`, `ActionDispatch::RequestId`): public files.
    member val SkipRequestHeaders = false with get, set

module Edge =
    let private stateKey = "campfire.web.edge"

    /// The state for this request, made on first use.
    let state (http: HttpContext) : RequestState =
        match http.Items.TryGetValue stateKey with
        | true, (:? RequestState as state) -> state
        | _ ->
            let state = RequestState(http)
            http.Items[stateKey] <- state
            state

    /// The cap on a request body, the 16 MiB Rust's front server had: more is a 413.
    [<Literal>]
    let MaxRequestBody = 16L * 1024L * 1024L

    /// `ActionDispatch::SSL#flag_cookies_as_secure!`
    let private flagCookiesAsSecure (headers: IHeaderDictionary) : unit =
        let cookies = headers.SetCookie
        if cookies.Count > 0 then
            let flagged =
                [| for cookie in cookies do
                       match cookie with
                       | null -> ()
                       | cookie ->
                           let attributes = cookie.Split ';'
                           let secure =
                               attributes |> Array.skip 1 |> Array.exists (fun a -> a.Trim().Equals("secure", StringComparison.OrdinalIgnoreCase))
                           if secure then cookie else cookie + "; secure" |]
            headers.SetCookie <- StringValues flagged

    /// Headers that go on every response, as the host starts sending it: `X-Request-Id`, `X-Runtime`, HSTS and secure cookies.
    let private onStarting (config: EdgeConfig) : Func<obj, Task> =
        Func<obj, Task>(fun o ->
            match o with
            | :? RequestState as state ->
                let headers = state.Http.Response.Headers
                if not state.SkipRequestHeaders then
                    if state.RequestId <> "" then headers["x-request-id"] <- state.RequestId
                    if not (headers.ContainsKey "x-runtime") then
                        let seconds = Stopwatch.GetElapsedTime(state.Started).TotalSeconds
                        headers["x-runtime"] <- seconds.ToString("F6", Globalization.CultureInfo.InvariantCulture)
                // The session middleware marks the response it establishes its cookie on as uncacheable (`no-cache,no-store`,
                // `Pragma`, `Expires`), over what the action said; Rails' session cookie leaves the cache headers alone.
                // This callback was registered first, so it runs after the middleware's: put back what a response of this
                // status carries by default.
                if headers.CacheControl.ToString() = "no-cache,no-store"
                   && headers.Pragma.ToString() = "no-cache"
                   && headers.Expires.ToString() = "-1" then
                    headers.Remove "Pragma" |> ignore
                    headers.Remove "Expires" |> ignore
                    let response = state.Http.Response
                    headers.CacheControl <-
                        (if (response.StatusCode = 200 || response.StatusCode = 201) && response.ContentLength.GetValueOrDefault() > 0L then
                             "max-age=0, private, must-revalidate"
                         else
                             "no-cache")
                if config.ForceSsl && state.Http.Request.IsHttps then
                    headers["strict-transport-security"] <- config.Hsts
                    flagCookiesAsSecure headers
            | _ -> ()
            Task.CompletedTask)

    /// `ActionDispatch::RequestId#make_request_id`
    let private makeRequestId (incoming: string | null) : string =
        if String.IsNullOrWhiteSpace incoming then
            Guid.NewGuid().ToString()
        else
            let id = StringBuilder()
            let incoming = nonNull incoming
            let mutable i = 0
            while i < incoming.Length && id.Length < 255 do
                let c = incoming[i]
                if Char.IsLetter c || Char.IsNumber c || c = '_' || c = '-' || c = '@' then id.Append c |> ignore
                i <- i + 1
            id.ToString()

    /// The first value of a request header, null when there is none or it is not visible ASCII (`HeaderValue::to_str`).
    let private visibleHeader (headers: IHeaderDictionary) (name: string) : string | null =
        match headers.TryGetValue name with
        | true, values when values.Count > 0 ->
            match values[0] with
            | null -> null
            | value ->
                let mutable ok = true
                for c in value do
                    if (c < ' ' && c <> '\t') || c > '~' then ok <- false
                if ok then value else null
        | _ -> null

    /// The request id, `X-Runtime`, HSTS and secure cookies (`onStarting`): what `ActionDispatch::RequestId`, `Rack::Runtime` and
    /// `ActionDispatch::SSL` do before routing (`force_ssl`'s redirect is gone, F7). It runs after
    /// `UseForwardedHeaders` and the `assume_ssl` middleware, so `Request.IsHttps` is settled.
    let defaultHeaders (config: EdgeConfig) : Func<HttpContext, RequestDelegate, Task> =
        let starting = onStarting config
        Func<HttpContext, RequestDelegate, Task>(fun http next ->
            let state = state http
            state.Started <- Stopwatch.GetTimestamp()
            state.RequestId <- makeRequestId (visibleHeader http.Request.Headers "x-request-id")
            // The app has its own limits, answered with a 413 of its own.
            match http.Features.Get<IHttpMaxRequestBodySizeFeature>() with
            | null -> ()
            | feature -> if not feature.IsReadOnly then feature.MaxRequestBodySize <- Nullable()
            http.Response.OnStarting(starting, (state :> obj))
            next.Invoke http)

    /// `ForwardedHeadersOptions` for the trusted proxies (K10): `X-Forwarded-For`, `-Proto` and `-Host`, honoured from
    /// `TRUSTED_PROXIES` ranges only and with no forward limit, which is how `ActionDispatch::RemoteIp` walks the list.
    let configureForwardedHeaders (config: EdgeConfig) (options: ForwardedHeadersOptions) : unit =
        options.ForwardedHeaders <-
            ForwardedHeaders.XForwardedFor ||| ForwardedHeaders.XForwardedProto ||| ForwardedHeaders.XForwardedHost
        options.ForwardLimit <- Nullable()
        options.RequireHeaderSymmetry <- false
        options.KnownProxies.Clear()
        options.KnownIPNetworks.Clear()
        for net in config.TrustedProxies do
            options.KnownIPNetworks.Add net

    /// `ActionDispatch::AssumeSSL`: with `assume_ssl` (production, unless `DISABLE_SSL`) every request is HTTPS, so cookies
    /// are `secure`, URLs are `https` and a browser's `Origin` matches.
    let assumeSsl (config: EdgeConfig) : Func<HttpContext, RequestDelegate, Task> =
        let assume = config.AssumeSsl
        Func<HttpContext, RequestDelegate, Task>(fun http next ->
            if assume then http.Request.Scheme <- "https"
            next.Invoke http)

    let private wireMethodKey = "campfire.web.wire-post"

    /// Whether a HEAD request is a POST on the wire that `_method=head` turned into one. The host holds a POST's response to
    /// its `Content-Length`, so a body dropped as for any HEAD must not announce one.
    let headFromPost (http: HttpContext) : bool = HttpMethods.IsHead http.Request.Method && http.Items.ContainsKey wireMethodKey

    /// `Rack::MethodOverride` (K10): `UseHttpMethodOverride` reads `_method` from the form, then the method is upper-cased
    /// (the framework sets it as sent, `delete`). The form it reads is the framework's cached one, which `Web.Params` reads
    /// too. A `_method` that is no HTTP verb makes the request that verb, which no route answers (404);
    /// `X-HTTP-Method-Override` is not read.
    let useMethodOverride (app: IApplicationBuilder) : unit =
        let options = HttpMethodOverrideOptions(FormFieldName = "_method")
        app.Use(
            Func<HttpContext, RequestDelegate, Task>(fun http next ->
                if HttpMethods.IsPost http.Request.Method then
                    http.Items[wireMethodKey] <- box true
                next.Invoke http)
        )
        |> ignore
        app.UseHttpMethodOverride options |> ignore
        app.Use(
            Func<HttpContext, RequestDelegate, Task>(fun http next ->
                let request = http.Request
                if http.Items.ContainsKey wireMethodKey && not (HttpMethods.IsPost request.Method) then
                    request.Method <- request.Method.ToUpperInvariant()
                next.Invoke http)
        )
        |> ignore

    /// Limits for the framework's form reader, which `_method` goes through: Rack's `params_limit` (4096 pairs) and the
    /// body cap.
    let configureForms (config: EdgeConfig) (options: Microsoft.AspNetCore.Http.Features.FormOptions) : unit =
        options.ValueCountLimit <- 4096
        options.KeyLengthLimit <- 65536
        options.ValueLengthLimit <- int config.MaxRequestBody
        options.MultipartBodyLengthLimit <- config.MaxRequestBody

    /// Caps the body at the config's `MaxRequestBody`; Kestrel answers a longer one with a 413 as it is read, whoever reads it:
    /// the form the method override parses, `Web.Params`.
    let bodyLimit (config: EdgeConfig) : Func<HttpContext, RequestDelegate, Task> =
        let cap = Nullable config.MaxRequestBody
        Func<HttpContext, RequestDelegate, Task>(fun http next ->
            match http.Features.Get<IHttpMaxRequestBodySizeFeature>() with
            | null -> ()
            | feature -> if not feature.IsReadOnly then feature.MaxRequestBodySize <- cap
            next.Invoke http)
