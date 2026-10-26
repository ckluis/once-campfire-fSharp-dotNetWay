// plans/design-front.md section 5, unit 7.3: AC1-AC10 (the cache, the schedule, the names, the store, the challenge endpoint and one
// renewal pass; no timers, no wall-clock bounds and no counter another thread changes: the clock is a value each call is given, and
// every assertion reads state after the awaited call returned) and AC11 (a real Pebble CA; skipped without PEBBLE_DIRECTORY).
module Campfire.App.Tests.Web.AcmeTests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Security
open System.Net.Sockets
open System.Security.Authentication
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Xunit
open Campfire.App.Tests.Support
open Campfire.App.Web

// --- Fixtures -------------------------------------------------------------------------------------------------------------------

let private tempDir () : string =
    let path = Path.Combine(Path.GetTempPath(), "campfire-acme-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory path |> ignore
    path

/// A self-signed certificate for `names` and a key, valid from `notBefore` to `notAfter`. Returns its PEM and the key.
let private certificate (names: string list) (notBefore: DateTimeOffset) (notAfter: DateTimeOffset) : string * ECDsa =
    let key = ECDsa.Create ECCurve.NamedCurves.nistP256
    let request = CertificateRequest("CN=" + List.head names, key, HashAlgorithmName.SHA256)
    let san = SubjectAlternativeNameBuilder()
    for name in names do
        san.AddDnsName name
    request.CertificateExtensions.Add(san.Build false)
    use certificate = request.CreateSelfSigned(notBefore, notAfter)
    certificate.ExportCertificatePem(), key

/// The clock every test hands the code: a value, never `DateTimeOffset.UtcNow`.
let private now = DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.Zero)

let private days (n: float) = TimeSpan.FromDays n

/// A cache file's text as the deleted port wrote it, for a leaf and an issuing certificate in that order.
let private cacheFile (key: ECDsa) (chain: string list) : string = key.ExportECPrivateKeyPem() + "\n" + String.Join("\n", chain) + "\n"

let private dummyCertificate (notAfter: DateTimeOffset) : LoadedCertificate =
    { Options = SslServerAuthenticationOptions(); NotBefore = notAfter - days 90.0; NotAfter = notAfter }

let private settingsFor (storage: string) (domains: string list) (directory: string) : FrontSettings =
    { HttpPort = 80
      HttpsPort = 443
      TargetPort = 3000
      TargetBind = IPAddress.Loopback
      TlsDomains = domains
      AcmeDirectory = directory
      ExternalAccount = ValueNone
      StoragePath = storage
      IdleTimeout = TimeSpan.FromSeconds 60.0
      ReadTimeout = TimeSpan.FromSeconds 30.0
      ForwardHeaders = false
      MaxRequestBody = 0L
      CacheSize = 0L
      MaxCacheItemSize = 0L
      LogRequests = false }

type private Factory(make: unit -> HttpClient) =
    interface IHttpClientFactory with
        member _.CreateClient(_: string) : HttpClient = make ()

/// Answers every request with a fixed status, and says (in a field only this test's own awaited call reads) whether it was asked.
type private Status(code: HttpStatusCode) =
    inherit HttpMessageHandler()
    member val Asked = false with get, set

    override this.SendAsync(_: HttpRequestMessage, _: CancellationToken) : Task<HttpResponseMessage> =
        this.Asked <- true
        Task.FromResult(new HttpResponseMessage(code))

type private TypedLogger<'T>(inner: ILogger) =
    interface ILogger<'T>

    interface ILogger with
        member _.BeginScope<'S when 'S: not null>(state: 'S) : IDisposable | null = inner.BeginScope state
        member _.IsEnabled(level: LogLevel) = inner.IsEnabled level
        member _.Log<'S>(level, id, state: 'S, ex, formatter) = inner.Log(level, id, state, ex, formatter)

type private HostLifetime() =
    interface IHostApplicationLifetime with
        member _.ApplicationStarted = CancellationToken.None
        member _.ApplicationStopping = CancellationToken.None
        member _.ApplicationStopped = CancellationToken.None
        member _.StopApplication() = ()

let private service (settings: FrontSettings) (store: CertificateStore) (factory: IHttpClientFactory) (logger: ILogger) : AcmeService =
    new AcmeService(settings, store, factory, new HostLifetime(), TimeProvider.System, (TypedLogger<AcmeService> logger :> ILogger<AcmeService>))

let private isPrivate (path: string) (mode: UnixFileMode) = File.GetUnixFileMode path = mode

// --- AC1-AC3: the cache -----------------------------------------------------------------------------------------------------------

[<Fact>]
let ``AC1 DirCache write then read round trips: SEC1 key, chain order kept, 0600 file in a 0700 directory, no temporary left`` () =
    let storage = Path.Combine(tempDir (), "thruster")
    let leaf, key = certificate [ "chat.example.com" ] (now - days 1.0) (now + days 89.0)
    let issuer, issuerKey = certificate [ "ca.example.com" ] (now - days 10.0) (now + days 900.0)
    use _issuerKey = issuerKey
    use _key = key
    DirCache.write storage "chat.example.com" key (leaf + "\n" + issuer)
    let path = Path.Combine(storage, "chat.example.com")
    let text = File.ReadAllText path
    Assert.StartsWith("-----BEGIN EC PRIVATE KEY-----\n", text)
    // The chain is in the order given: leaf, then issuer.
    let order = X509Certificate2Collection()
    order.ImportFromPem text
    Assert.Equal(2, order.Count)
    Assert.Equal("CN=chat.example.com", order[0].Subject)
    Assert.Equal("CN=ca.example.com", order[1].Subject)
    match DirCache.read storage "chat.example.com" now with
    | Ok loaded ->
        Assert.Equal(DateTimeOffset order[0].NotAfter, loaded.NotAfter)
        Assert.Equal(DateTimeOffset order[0].NotBefore, loaded.NotBefore)
        Assert.NotNull loaded.Options.ServerCertificateContext
    | Error reason -> failwith reason
    if not (OperatingSystem.IsWindows()) then
        Assert.True(isPrivate path (UnixFileMode.UserRead ||| UnixFileMode.UserWrite), "the file is 0600")
        Assert.True(isPrivate storage (UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute), "the directory is 0700")
    Assert.Equal<string list>([ "chat.example.com" ], Directory.GetFiles storage |> Array.map Path.GetFileName |> List.ofArray)

[<Fact>]
let ``AC2 DirCache read refuses an expired, a not yet valid, a mismatched key and a wrong name file, and accepts a wildcard`` () =
    let storage = tempDir ()
    let put name (text: string) = File.WriteAllText(Path.Combine(storage, name), text)
    let refused name (fragment: string) =
        match DirCache.read storage name now with
        | Ok _ -> failwith $"{name} should be refused"
        | Error reason -> Assert.Contains(fragment, reason)
    let expired, k1 = certificate [ "expired.example.com" ] (now - days 100.0) (now - days 10.0)
    put "expired.example.com" (cacheFile k1 [ expired ])
    refused "expired.example.com" "expired"
    let early, k2 = certificate [ "early.example.com" ] (now + days 1.0) (now + days 91.0)
    put "early.example.com" (cacheFile k2 [ early ])
    refused "early.example.com" "not yet valid"
    let theirs, _ = certificate [ "other-key.example.com" ] (now - days 1.0) (now + days 89.0)
    use stranger = ECDsa.Create ECCurve.NamedCurves.nistP256
    put "other-key.example.com" (cacheFile stranger [ theirs ])
    refused "other-key.example.com" "certificate:"
    let named, k4 = certificate [ "named.example.com" ] (now - days 1.0) (now + days 89.0)
    put "wrong.example.com" (cacheFile k4 [ named ])
    refused "wrong.example.com" "not valid for wrong.example.com"
    refused "absent.example.com" "no cached certificate"
    let wild, k5 = certificate [ "*.example.com" ] (now - days 1.0) (now + days 89.0)
    put "chat.example.com" (cacheFile k5 [ wild ])
    Assert.True((DirCache.read storage "chat.example.com" now).IsOk, "a wildcard covers one label")
    put "a.b.example.com" (cacheFile k5 [ wild ])
    refused "a.b.example.com" "not valid for"

[<Fact>]
let ``AC3 a cache file written by the deleted port reads, and its SEC1 key is written back byte for byte`` () =
    let storage = tempDir ()
    let fixture = Path.Combine(AppContext.BaseDirectory, "testdata", "acme", "cached-by-port.pem")
    File.Copy(fixture, Path.Combine(storage, "chat.example.com"))
    Assert.True((DirCache.read storage "chat.example.com" now).IsOk)
    // The key as Go and openssl write it (`openssl ecparam -name prime256v1 -genkey -noout`): the curve named, the public key included.
    let sec1 = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "acme", "key.pem"))
    use key = ECDsa.Create()
    key.ImportFromPem sec1
    let leaf = File.ReadAllText fixture
    let rewritten = Path.Combine(tempDir (), "thruster")
    DirCache.write rewritten "chat.example.com" key leaf
    let text = File.ReadAllText(Path.Combine(rewritten, "chat.example.com"))
    Assert.StartsWith(sec1, text)
    Assert.True((DirCache.read rewritten "chat.example.com" now).IsOk)

// --- AC4-AC5: the schedule and the names -------------------------------------------------------------------------------------------

[<Fact>]
let ``AC4 Renewal due is 30 days before NotAfter for a 90 day certificate and two days before for a 6 day one, less the jitter`` () =
    let jitter = TimeSpan.FromMinutes 17.0
    let long = { dummyCertificate (now + days 90.0) with NotBefore = now }
    Assert.Equal(long.NotAfter - days 30.0 - jitter, Renewal.due long jitter)
    Assert.Equal(long.NotAfter - days 30.0, Renewal.due long TimeSpan.Zero)
    let short = { dummyCertificate (now + days 6.0) with NotBefore = now }
    Assert.Equal(short.NotAfter - days 2.0 - jitter, Renewal.due short jitter)
    // A fixed 30 days would be due before the certificate existed: the service would order forever.
    Assert.True(Renewal.due short TimeSpan.Zero > short.NotBefore)
    // The failure retry is 30 to 60 minutes.
    Assert.Equal(TimeSpan.FromMinutes 30.0, Renewal.retryAfterFailure 0.0)
    Assert.Equal(TimeSpan.FromMinutes 45.0, Renewal.retryAfterFailure 0.5)

[<Fact>]
let ``AC5 ServerNames normalize lower-cases, drops a trailing dot, punycodes, and refuses an IP, a single label and a slash`` () =
    Assert.Equal(Ok "chat.example.com", ServerNames.normalize "Chat.Example.COM.")
    Assert.Equal(Ok "xn--bcher-kva.example", ServerNames.normalize "bücher.example")
    Assert.True((ServerNames.normalize null).IsError)
    Assert.True((ServerNames.normalize "").IsError)
    Assert.True((ServerNames.normalize "localhost").IsError, "no dot")
    Assert.True((ServerNames.normalize "192.168.1.1").IsError, "an IP address")
    Assert.True((ServerNames.normalize "[::1]").IsError, "an IPv6 literal")
    Assert.True((ServerNames.normalize "a/b.example.com").IsError, "a slash")
    Assert.True((ServerNames.normalize "a\\b.example.com").IsError, "a backslash")

// --- AC6-AC7: the store and the account key ----------------------------------------------------------------------------------------

let private handshakeContext (serverName: string) : TlsHandshakeCallbackContext =
    // `SslClientHelloInfo` has no public constructor (the server builds it from the ClientHello).
    let info =
        Activator.CreateInstance(
            typeof<SslClientHelloInfo>,
            Reflection.BindingFlags.Instance ||| Reflection.BindingFlags.NonPublic ||| Reflection.BindingFlags.Public,
            null,
            [| box serverName; box SslProtocols.Tls13 |],
            null
        )
    let context = TlsHandshakeCallbackContext()
    (nonNull (typeof<TlsHandshakeCallbackContext>.GetProperty "ClientHelloInfo")).SetValue(context, info)
    context

[<Fact>]
let ``AC6 the handshake gives the stored options, fails for a name outside TLS_DOMAIN, and waits for a first certificate until Set`` () =
    task {
        let store = CertificateStore [ "chat.example.com" ]
        let stored = dummyCertificate (now + days 60.0)
        // Before any certificate: a TLS_DOMAIN name waits; it completes when Set is called (awaited after Set returned: no race).
        let waiting = store.HandshakeOptions(handshakeContext "Chat.Example.com").AsTask()
        Assert.False waiting.IsCompleted
        store.Set("chat.example.com", stored)
        let! options = waiting
        Assert.Same(stored.Options, options)
        // Stored: answered at once, and a replacement certificate is what the next handshake gets.
        let! again = store.HandshakeOptions(handshakeContext "chat.example.com").AsTask()
        Assert.Same(stored.Options, again)
        let renewed = dummyCertificate (now + days 90.0)
        store.Set("chat.example.com", renewed)
        let! after = store.HandshakeOptions(handshakeContext "chat.example.com").AsTask()
        Assert.Same(renewed.Options, after)
        // Outside TLS_DOMAIN, an IP address and a missing name: the handshake fails, and nothing is remembered for them.
        for name in [ "other.example.com"; "192.168.1.1"; "localhost" ] do
            let! failure = Assert.ThrowsAnyAsync<exn>(fun () -> store.HandshakeOptions(handshakeContext name).AsTask())
            Assert.NotNull failure
        Assert.True((store.Get "other.example.com").IsNone)
        Assert.Equal<string list>([ "chat.example.com" ], store.Domains)
    }

[<Fact>]
let ``AC7 accountKeyPem creates a SEC1 key once and returns the same text after`` () =
    let storage = Path.Combine(tempDir (), "thruster")
    let first = DirCache.accountKeyPem storage
    Assert.StartsWith("-----BEGIN EC PRIVATE KEY-----\n", first)
    Assert.Equal(first, DirCache.accountKeyPem storage)
    Assert.Equal(first, File.ReadAllText(Path.Combine(storage, "acme_account+key")))
    if not (OperatingSystem.IsWindows()) then
        Assert.True(isPrivate (Path.Combine(storage, "acme_account+key")) (UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
    // Certes reads it as the account key (what Thruster's account file looks like to it).
    Assert.NotNull(Certes.KeyFactory.FromPem first)

// --- AC8: the challenge endpoint ---------------------------------------------------------------------------------------------------

let private callChallenge (store: CertificateStore) (path: string) : int * string * string =
    let http = DefaultHttpContext()
    http.Request.Path <- PathString path
    use body = new MemoryStream()
    http.Response.Body <- body
    (AcmeEndpoints.challenge store).Invoke(http).GetAwaiter().GetResult()
    let contentType = match http.Response.ContentType with null -> "" | c -> c
    http.Response.StatusCode, contentType, Encoding.ASCII.GetString(body.ToArray())

[<Fact>]
let ``AC8 the challenge endpoint answers a stored token as text plain, 404 otherwise, and only under the well-known path`` () =
    let store = CertificateStore [ "chat.example.com" ]
    store.AddChallenge("tok-1", "tok-1.thumbprint")
    Assert.Equal((200, "text/plain", "tok-1.thumbprint"), callChallenge store "/.well-known/acme-challenge/tok-1")
    Assert.Equal(404, (let s, _, _ = callChallenge store "/.well-known/acme-challenge/unknown" in s))
    Assert.Equal(404, (let s, _, _ = callChallenge store "/tok-1" in s))
    Assert.Equal(404, (let s, _, _ = callChallenge store "/other/.well-known/acme-challenge/tok-1" in s))
    Assert.Equal(404, (let s, _, _ = callChallenge store "/.well-known/acme-challenge/" in s))
    Assert.Equal(404, (let s, _, _ = callChallenge store "/.well-known/acme-challenge/tok-1/more" in s))
    store.RemoveChallenge "tok-1"
    Assert.Equal(404, (let s, _, _ = callChallenge store "/.well-known/acme-challenge/tok-1" in s))

// --- AC9-AC10: one renewal pass ----------------------------------------------------------------------------------------------------

[<Fact>]
let ``AC9 a pass with every domain loaded and not due makes no HTTP request and returns the earliest due time`` () =
    task {
        let storage = tempDir ()
        let long, k1 = certificate [ "a.example.com" ] (now - days 10.0) (now + days 80.0)
        let short, k2 = certificate [ "b.example.com" ] (now - days 1.0) (now + days 5.0)
        File.WriteAllText(Path.Combine(storage, "a.example.com"), cacheFile k1 [ long ])
        File.WriteAllText(Path.Combine(storage, "b.example.com"), cacheFile k2 [ short ])
        let handler = new Status(HttpStatusCode.InternalServerError)
        let made = ref false
        let factory = Factory(fun () -> made.Value <- true; new HttpClient(handler))
        let store = CertificateStore [ "a.example.com"; "b.example.com" ]
        let logs = new CapturingLoggers()
        let acme = service (settingsFor storage [ "a.example.com"; "b.example.com" ] "https://acme.invalid/directory") store factory (logs.Logger "acme")
        acme.LoadCached now
        let before = [ store.Get "a.example.com"; store.Get "b.example.com" ]
        let! next = acme.RenewDueAsync(now, CancellationToken.None)
        Assert.False(made.Value, "no HTTP client was made")
        Assert.False handler.Asked
        // The 6 day certificate is due two days before NotAfter (now + 3 days) less up to an hour of jitter; the 90 day one much later.
        let earliest = DateTimeOffset(X509Certificate2.CreateFromPem(short).NotAfter) - days 2.0
        Assert.InRange(next, earliest - Renewal.JitterWindow, earliest)
        Assert.Equal<LoadedCertificate voption list>(before, [ store.Get "a.example.com"; store.Get "b.example.com" ])
    }

[<Fact>]
let ``AC10 a failing order leaves the store unchanged, logs the domain and returns now plus 30 to 60 minutes`` () =
    task {
        let storage = tempDir ()
        let handler = new Status(HttpStatusCode.InternalServerError)
        let factory = Factory(fun () -> new HttpClient(handler))
        let store = CertificateStore [ "chat.example.com" ]
        let logs = new CapturingLoggers()
        let acme = service (settingsFor storage [ "chat.example.com" ] "https://acme.invalid/directory") store factory (logs.Logger "acme")
        let! next = acme.RenewDueAsync(now, CancellationToken.None)
        Assert.True handler.Asked
        Assert.True((store.Get "chat.example.com").IsNone)
        Assert.InRange(next, now + TimeSpan.FromMinutes 30.0, now + TimeSpan.FromMinutes 60.0)
        Assert.Contains("Error", logs.Text)
        Assert.Contains("chat.example.com", logs.Text)
    }

// --- AC11: Pebble -----------------------------------------------------------------------------------------------------------------

/// Trusting `root` (a PEM file) and nothing else.
let private trusting (root: string) : SocketsHttpHandler =
    let handler = new SocketsHttpHandler()
    let trust = new X509Certificate2Collection()
    trust.ImportFromPemFile root
    let policy = new X509ChainPolicy(TrustMode = X509ChainTrustMode.CustomRootTrust, DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck)
    policy.CustomTrustStore.AddRange trust
    handler.SslOptions.CertificateChainPolicy <- policy
    handler

type private Running = { App: WebApplication; Store: CertificateStore; Service: AcmeService; HttpsPort: int }

/// The HTTP listener on `httpPort` answering HTTP-01 and an HTTPS listener on a free port serving `store`, with the ACME service hosted.
let private startHost (logs: CapturingLoggers) (settings: FrontSettings) (httpPort: int) (acmeHandler: unit -> HttpMessageHandler) : Task<Running> =
    task {
        let builder = WebApplication.CreateSlimBuilder()
        builder.Logging.ClearProviders() |> ignore
        builder.Services.AddSingleton<ILoggerFactory>(logs) |> ignore
        let store = CertificateStore settings.TlsDomains
        builder.Services.AddSingleton(settings).AddSingleton(store).AddSingleton(TimeProvider.System) |> ignore
        builder.Services.AddHttpClient("acme").ConfigurePrimaryHttpMessageHandler(fun () -> acmeHandler ()) |> ignore
        builder.Services.AddSingleton<AcmeService>().AddHostedService(fun sp -> sp.GetRequiredService<AcmeService>()) |> ignore
        builder.WebHost.ConfigureKestrel(fun (kestrel: KestrelServerOptions) ->
            kestrel.Listen(IPAddress.Any, httpPort)
            kestrel.Listen(
                IPAddress.Loopback,
                0,
                fun listen ->
                    let tls = TlsHandshakeCallbackOptions()
                    tls.OnConnection <- Func<TlsHandshakeCallbackContext, ValueTask<SslServerAuthenticationOptions>>(store.HandshakeOptions)
                    listen.UseHttps tls |> ignore
            ))
        |> ignore
        let app = builder.Build()
        app.Run(AcmeEndpoints.challenge store)
        do! app.StartAsync()
        let https =
            (nonNull (app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>())).Addresses
            |> Seq.map Uri
            |> Seq.find (fun u -> u.Scheme = "https")
        return { App = app; Store = store; Service = app.Services.GetRequiredService<AcmeService>(); HttpsPort = https.Port }
    }

/// A TLS handshake as a client trusting only `issuingRoot` (no AIA fetch): the leaf and the chain elements the platform built.
let private handshake (port: int) (domain: string) (issuingRoot: X509Certificate2) : Task<X509Certificate2 * int> =
    task {
        use client = new TcpClient()
        do! client.ConnectAsync(IPAddress.Loopback, port)
        use ssl = new SslStream(client.GetStream(), false)
        let policy = new X509ChainPolicy(TrustMode = X509ChainTrustMode.CustomRootTrust, DisableCertificateDownloads = true, RevocationMode = X509RevocationMode.NoCheck)
        policy.CustomTrustStore.Add issuingRoot |> ignore
        let elements = ref 0
        let options = SslClientAuthenticationOptions(TargetHost = domain)
        options.CertificateChainPolicy <- policy
        options.RemoteCertificateValidationCallback <-
            RemoteCertificateValidationCallback(fun _ _ chain errors ->
                elements.Value <- (nonNull chain).ChainElements.Count
                errors = SslPolicyErrors.None)
        do! ssl.AuthenticateAsClientAsync options
        return new X509Certificate2(nonNull ssl.RemoteCertificate), elements.Value
    }

/// Runs `body` against a started host and always stops it (a failed assertion must not leave a listener that keeps the test process alive).
/// A line in the file PEBBLE_TIMES names (when set): the order times, and each step reached.
let private note (line: string) : unit =
    match Environment.GetEnvironmentVariable "PEBBLE_TIMES" with
    | null | "" -> ()
    | file -> File.AppendAllText(file, line + "\n")

/// A step that must finish: a hang becomes a failure naming the step (nothing here is a timing assertion, only a bound on waiting).
let private within (name: string) (seconds: int) (work: Task<'T>) : Task<'T> =
    task {
        try
            return! work.WaitAsync(TimeSpan.FromSeconds(float seconds))
        with :? TimeoutException ->
            return failwith $"{name} did not finish within {seconds} s"
    }

let private withHost (start: Task<Running>) (body: Running -> Task<'T>) : Task<'T> =
    task {
        let! host = start
        try
            return! body host
        finally
            (try (within "stopping the host" 20 (task { do! host.App.StopAsync() })).GetAwaiter().GetResult() with _ -> ())
            (try (within "disposing the host" 20 (task { do! host.App.DisposeAsync() })).GetAwaiter().GetResult() with _ -> ())
    }

[<Fact>]
let ``AC11 Pebble: a certificate is ordered at start over HTTP-01, cached, served with its intermediate, and a second host loads it without ordering`` () =
    task {
        match Environment.GetEnvironmentVariable "PEBBLE_DIRECTORY", Environment.GetEnvironmentVariable "PEBBLE_ROOT" with
        | (null | ""), _
        | _, (null | "") -> Assert.Skip "PEBBLE_DIRECTORY and PEBBLE_ROOT name a Pebble CA (the recipe is in AGENTS.md); skipped without them"
        | directory, root ->
            let domain = "campfire.test"
            let httpPort = match Environment.GetEnvironmentVariable "PEBBLE_HTTP_PORT" with null | "" -> 5002 | p -> int p
            let storage = Path.Combine(tempDir (), "thruster")
            let settings = settingsFor storage [ domain ] directory
            // Pebble's issuing root changes with every run; its management API serves it (TLS by the same minica as the directory).
            let management = match Environment.GetEnvironmentVariable "PEBBLE_MANAGEMENT" with null | "" -> "https://localhost:15000" | m -> m
            use manager = new HttpClient(trusting root)
            let! issuing = within "Pebble's root" 30 (manager.GetStringAsync $"{management}/roots/0")
            use issuingRoot = X509Certificate2.CreateFromPem issuing
            let logs = new CapturingLoggers()
            // First host: nothing cached, so it orders after ApplicationStarted.
            let! leaf, served =
                withHost (within "starting the first host" 30 (startHost logs settings httpPort (fun () -> trusting root))) (fun first ->
                    task {
                        let clock = Diagnostics.Stopwatch.StartNew()
                        // An order that fails is logged, not thrown: say what the service logged when no certificate comes.
                        let! leaf =
                            task {
                                try
                                    return! first.Store.WaitFor(domain, (new CancellationTokenSource(TimeSpan.FromSeconds 60.0)).Token)
                                with :? OperationCanceledException ->
                                    return failwith $"no certificate within 60 s; the service logged:\n{logs.Text}"
                            }
                        let report = $"AC11 order time: {clock.ElapsedMilliseconds} ms"
                        TestContext.Current.SendDiagnosticMessage report
                        note report
                        Assert.True(leaf.NotAfter > DateTimeOffset.UtcNow)
                        Assert.True(File.Exists(Path.Combine(storage, domain)), "the certificate is cached")
                        Assert.True(File.Exists(Path.Combine(storage, "acme_account+key")), "the account key is kept")
                        let! served, elements = within "the first handshake" 30 (handshake first.HttpsPort domain issuingRoot)
                        Assert.True(served.MatchesHostname(domain, true, false), "the leaf covers the name")
                        // Leaf, Pebble's intermediate (sent by the server: no AIA fetch) and the root the client trusts.
                        Assert.Equal(3, elements)
                        note "first host: handshake and chain checked"
                        return leaf, served
                    })
            // Second host, same storage: the cache is loaded at start and no order is made (its CA client fails the test if used).
            let refusing = new Status(HttpStatusCode.InternalServerError)
            do!
                withHost (within "starting the second host" 30 (startHost logs settings httpPort (fun () -> refusing :> HttpMessageHandler))) (fun second ->
                    task {
                        let! cached = second.Store.WaitFor(domain, (new CancellationTokenSource(TimeSpan.FromSeconds 30.0)).Token)
                        Assert.Equal(leaf.NotAfter, cached.NotAfter)
                        // One pass called directly, so the check that nothing was ordered is not left to the background loop's timing.
                        let! next = within "the second host's pass" 30 (second.Service.RenewDueAsync(DateTimeOffset.UtcNow, CancellationToken.None))
                        Assert.True(next > DateTimeOffset.UtcNow)
                        let! again, _ = within "the second handshake" 30 (handshake second.HttpsPort domain issuingRoot)
                        Assert.Equal(served.Thumbprint, again.Thumbprint)
                    })
            note "second host: done"
            Assert.False(refusing.Asked, "the second host made no order")
    }
