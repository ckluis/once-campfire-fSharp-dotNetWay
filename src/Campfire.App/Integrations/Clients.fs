// The outgoing HTTP clients (plans/design-jobs.md J2, J3, J4): three named clients from `IHttpClientFactory`, beside the host's "acme".
//
// None follows a redirect, uses a proxy or keeps cookies, all inflate gzip and deflate, and each has a pipeline of timeouts and nothing
// else: no retry (Rails retries nothing, and a POST must reach a bot once), no circuit breaker (one dead bot must not stop every other
// bot's deliveries), no hedging. A pipeline's timeout is the time to the response head; a delivery keeps its own deadline over the
// body. "opengraph" and "push" connect through `Guard.connect`; "webhook" is unguarded, as Rails' (`webhook.rb`: operators point bots at
// their own internal services).
namespace Campfire.App.Integrations

open System
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Polly
open Polly.Timeout
open Campfire.Ruby

/// How a request failed to produce a response, by Rails' exception.
[<RequireQualifiedAccess>]
type TransportError =
    /// `Net::OpenTimeout`
    | OpenTimeout
    /// `Net::ReadTimeout`
    | ReadTimeout
    /// `Errno::ECONNREFUSED`
    | Refused
    /// `SocketError`
    | Unresolvable of string
    /// `OpenSSL::SSL::SSLError`
    | Tls of string
    /// Anything else: a reset, an unreadable body, a guard refusal.
    | Io of string

module TransportError =
    let message (error: TransportError) : string =
        match error with
        | TransportError.OpenTimeout -> "execution expired"
        | TransportError.ReadTimeout -> "Net::ReadTimeout"
        | TransportError.Refused -> "Connection refused"
        | TransportError.Unresolvable message -> $"getaddrinfo: {message}"
        | TransportError.Tls message -> $"SSL error: {message}"
        | TransportError.Io message -> message

    let rec private hasInner<'T when 'T :> exn> (e: exn | null) : 'T option =
        match e with
        | null -> None
        | :? 'T as found -> Some found
        | e -> hasInner<'T> e.InnerException

    /// The failure of a `SendAsync` or a body read, by kind (J4).
    let classify (e: exn) : TransportError =
        match e with
        | :? TimeoutRejectedException -> TransportError.ReadTimeout
        | :? HttpRequestException as http ->
            match http.HttpRequestError with
            | HttpRequestError.NameResolutionError -> TransportError.Unresolvable http.Message
            | HttpRequestError.SecureConnectionError -> TransportError.Tls http.Message
            | HttpRequestError.ConnectionError ->
                match hasInner<SocketException> http with
                | Some socket when socket.SocketErrorCode = SocketError.ConnectionRefused -> TransportError.Refused
                | _ -> if (hasInner<TimeoutException> http).IsSome then TransportError.OpenTimeout else TransportError.Io http.Message
            | _ -> TransportError.Io http.Message
        | e -> TransportError.Io e.Message

/// Copies the address `Clients.withPin` set for this logical call onto the request: a library that builds its own request (Web Push)
/// cannot call `Clients.pin`. The connect callback still checks the address.
[<Sealed>]
type PinHandler() =
    inherit DelegatingHandler()
    static let current = AsyncLocal<IPAddress | null>()
    static member Current with get () = current.Value and set (value: IPAddress | null) = current.Value <- value

    override _.SendAsync(request: HttpRequestMessage, cancellation: CancellationToken) : Task<HttpResponseMessage> =
        match PinHandler.Current with
        | null -> ()
        | address -> request.Options.Set(Guard.Pinned, address)
        base.SendAsync(request, cancellation)

module Clients =
    [<Literal>]
    let Opengraph = "opengraph"

    [<Literal>]
    let Push = "push"

    [<Literal>]
    let Webhook = "webhook"

    /// Where Rails passes `ipaddr:`; see `Guard.Pinned`.
    let Pinned: HttpRequestOptionsKey<IPAddress> = Guard.Pinned

    /// `Webhook::ENDPOINT_TIMEOUT` (`open_timeout` and `read_timeout`).
    let private webhookTimeout = TimeSpan.FromSeconds 7.0
    let private opengraphTimeout = TimeSpan.FromSeconds 5.0
    let private pushTimeout = TimeSpan.FromSeconds 10.0

    /// A push service's error body is read whole by the library; nothing legitimate is longer than this.
    let private pushBodyLimit = 16L * 1024L

    /// Unfurls allow this many requests at once (the port's limit, kept).
    [<Literal>]
    let OpengraphConcurrency = 16

    let internal handler
        (guarded: bool)
        (connectTimeout: TimeSpan)
        (oneConnectionPerRequest: bool)
        (provider: IServiceProvider)
        : SocketsHttpHandler =
        let handler =
            new SocketsHttpHandler(
                AllowAutoRedirect = false,
                UseProxy = false,
                UseCookies = false,
                AutomaticDecompression = (DecompressionMethods.GZip ||| DecompressionMethods.Deflate),
                ConnectTimeout = connectTimeout
            )
        if oneConnectionPerRequest then handler.PooledConnectionLifetime <- TimeSpan.Zero
        if guarded then
            let resolver = provider.GetRequiredService<IResolver>()
            let dialer = provider.GetRequiredService<IDialer>()
            let dial (address: IPEndPoint) (cancellation: CancellationToken) = dialer.Connect(address, cancellation)
            handler.ConnectCallback <- Func<SocketsHttpConnectionContext, CancellationToken, ValueTask<Stream>>(Guard.connect resolver dial)
        handler

    /// Net::HTTP's `Accept` and `User-Agent`: what a page serves can depend on them.
    let private netHttpHeaders (client: HttpClient) : unit =
        client.DefaultRequestHeaders.Accept.ParseAdd "*/*"
        client.DefaultRequestHeaders.UserAgent.ParseAdd "Ruby"

    let private register
        (services: IServiceCollection)
        (name: string)
        (guarded: bool)
        (connectTimeout: TimeSpan)
        (oneConnectionPerRequest: bool)
        (netHttp: bool)
        (concurrency: int option)
        : unit =
        // The pipeline's timeout is the time to the response head, so the client's own (100 s) never applies: a delivery keeps a deadline.
        let builder =
            services
                .AddHttpClient(name, fun (client: HttpClient) ->
                    client.Timeout <- Timeout.InfiniteTimeSpan
                    if name = Push then client.MaxResponseContentBufferSize <- pushBodyLimit
                    if netHttp then netHttpHeaders client)
                .ConfigurePrimaryHttpMessageHandler(fun provider -> handler guarded connectTimeout oneConnectionPerRequest provider :> HttpMessageHandler)
                // The factory's default handlers log each request's URL at Information: a webhook URL often holds the bot's secret in
                // its path and a push endpoint is the subscription's capability URL. Rails logs neither.
                .RemoveAllLoggers()
        if name = Push then builder.AddHttpMessageHandler(fun () -> new PinHandler() :> DelegatingHandler) |> ignore
        builder.AddResilienceHandler(
            name,
            fun (pipeline: ResiliencePipelineBuilder<HttpResponseMessage>) ->
                match concurrency with
                | Some permits -> pipeline.AddConcurrencyLimiter(permits, 1024) |> ignore
                | None -> ()
                pipeline.AddTimeout connectTimeout |> ignore
        )
        |> ignore

    /// The three clients. The resolver and the dial are singletons the guard reads (tests register their own first).
    let add (services: IServiceCollection) : unit =
        services.TryAddSingleton<IResolver>(fun _ -> SystemResolver() :> IResolver)
        services.TryAddSingleton<IDialer>(fun _ -> TcpDialer() :> IDialer)
        register services Opengraph true opengraphTimeout true true (Some OpengraphConcurrency)
        register services Push true pushTimeout false false None
        register services Webhook false webhookTimeout true true None

    /// The request pinned to `address` (Rails' `http.ipaddr =`): the guard checks it again at connect.
    let pin (address: IPAddress) (request: HttpRequestMessage) : HttpRequestMessage =
        request.Options.Set(Pinned, address)
        request

    /// Runs `send` with every request it makes through a "push" client pinned to `address` (`PinHandler`).
    let withPin (address: IPAddress) (send: unit -> Task) : Task =
        task {
            let before = PinHandler.Current
            PinHandler.Current <- address
            try
                do! send ()
            finally
                PinHandler.Current <- before
        }

    /// The body through `limit` bytes after decompression: the bytes, or `ValueNone` when it is longer (reading stops just past the limit).
    let readLimited (content: HttpContent) (limit: int) (cancellation: CancellationToken) : Task<byte[] voption> =
        task {
            use! source = content.ReadAsStreamAsync cancellation
            use output = new MemoryStream()
            let buffer = Array.zeroCreate<byte> 65536
            let mutable tooLarge = false
            let mutable reading = true
            while reading do
                let! n = source.ReadAsync(buffer.AsMemory(), cancellation)
                if n = 0 then
                    reading <- false
                elif int64 output.Length + int64 n > int64 limit then
                    tooLarge <- true
                    reading <- false
                else
                    output.Write(buffer, 0, n)
            return (if tooLarge then ValueNone else ValueSome(output.ToArray()))
        }

    /// `Net::HTTPHeader#content_type` over the raw header: the media type before any `;`, main and sub type each stripped (not downcased), or
    /// just the main type when there is no `/`.
    let contentType (response: HttpResponseMessage) : string option =
        match response.Content.Headers.NonValidated.TryGetValues "Content-Type" with
        | true, values ->
            let header = values.ToString()
            let media = (header.Split ';')[0]
            let parts = media.Split '/'
            let main = Ruby.strip parts[0]
            Some(if parts.Length > 1 then $"{main}/{Ruby.strip parts[1]}" else main)
        | _ -> None
