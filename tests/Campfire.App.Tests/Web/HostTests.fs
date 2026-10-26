// plans/design-front.md section 5, units 7.4a and 7.4b: HS1-HS12 (HS2 and HS4 are the TLS listener and F6's branch). Hosts are `Web.Host.build` itself, on free loopback ports, over the app's own services and pipeline.
module Campfire.App.Tests.Web.HostTests

open System
open System.Diagnostics
open System.IO
open System.Net
open System.Net.Security
open System.Net.Sockets
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Net.WebSockets
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Options
open Xunit
open Campfire.App
open Campfire.App.Web
open Campfire.App.Tests.Support

let private freePort () : int =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

/// Thruster's variables as a lookup, with two free ports for `HTTP_PORT` and `TARGET_PORT` unless the test names them.
let private settingsOf (vars: (string * string) list) : FrontSettings =
    let given = Map.ofList vars
    let all =
        given
        |> Map.add "HTTP_PORT" (match Map.tryFind "HTTP_PORT" given with Some p -> p | None -> string (freePort ()))
        |> Map.add "TARGET_PORT" (match Map.tryFind "TARGET_PORT" given with Some p -> p | None -> string (freePort ()))
    FrontSettings.fromLookup (fun name -> match Map.tryFind name all with Some v -> v | None -> null)

/// `Host.build` over a booted app's services and pipeline, started. Disposing the returned host stops it.
let private startHostWith
    (booted: Booted)
    (pipeline: Microsoft.AspNetCore.Builder.IApplicationBuilder -> unit)
    (settings: FrontSettings)
    : Task<Campfire.App.Tests.Support.Host * Microsoft.AspNetCore.Builder.WebApplication> =
    task {
        let app = Web.Host.build settings NullLoggerFactory.Instance booted.ConfigureServices pipeline
        do! app.StartAsync()
        return new Campfire.App.Tests.Support.Host(app, settings.HttpPort, BodyWrites()), app
    }

let private startHost (booted: Booted) (settings: FrontSettings) = startHostWith booted booted.Pipeline settings

/// The app's pipeline with a `/cable` that throws: an exception outside an action, which reaches `UseExceptionHandler` itself.
let private throwingCable (builder: Microsoft.AspNetCore.Builder.IApplicationBuilder) : unit =
    let edge = builder.ApplicationServices.GetRequiredService<EdgeConfig>()
    Web.Services.pipeline edge (fun _ -> failwith "boom") RouteTable.dispatch builder

/// The same app, reached on another of its ports (it stops with the host it shares).
let private onPort (app: Microsoft.AspNetCore.Builder.WebApplication) (port: int) : Campfire.App.Tests.Support.Host =
    new Campfire.App.Tests.Support.Host(app, port, BodyWrites())

let private addresses (app: Microsoft.AspNetCore.Builder.WebApplication) : string list =
    (nonNull (app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>())).Addresses |> List.ofSeq

[<Fact>]
let ``HS1 /up answers on HTTP_PORT and on TARGET_PORT, which is bound to loopback`` () =
    task {
        use! test = bootSeeded "default"
        let settings = settingsOf []
        let! host, app = startHost test.Booted settings
        use _host = host
        let! up = host.Send(get "/up")
        Assert.Equal(200, up.Status)
        let! target = (onPort app settings.TargetPort).Send(get "/up")
        Assert.Equal(200, target.Status)
        let listening = addresses app
        Assert.Contains($"http://127.0.0.1:{settings.TargetPort}", listening)
        Assert.Single(listening |> List.filter (fun a -> a.EndsWith $":{settings.HttpPort}")) |> ignore
        Assert.DoesNotContain(listening, fun a -> a.Contains "127.0.0.1" && a.EndsWith $":{settings.HttpPort}")
        // TARGET_BIND opens the app listener to every address.
        let open' = settingsOf [ "TARGET_BIND", "0.0.0.0" ]
        let! host2, app2 = startHost test.Booted open'
        use _host2 = host2
        Assert.Contains($"http://0.0.0.0:{open'.TargetPort}", addresses app2)
        // TARGET_PORT = HTTP_PORT: one listener, no failure.
        let port = freePort ()
        let same = settingsOf [ "HTTP_PORT", string port; "TARGET_PORT", string port ]
        let! host3, app3 = startHost test.Booted same
        use _host3 = host3
        Assert.Equal(1, (addresses app3).Length)
        let! up3 = host3.Send(get "/up")
        Assert.Equal(200, up3.Status)
    }

[<Fact>]
let ``HS3 without TLS the HTTP port serves the app`` () =
    task {
        use! test = bootSeeded "default"
        let! host, _ = startHost test.Booted (settingsOf [])
        use _host = host
        let! page = host.Send(get "/session/new")
        Assert.Equal(200, page.Status)
        Assert.Contains("<form", page.Text)
        let! signedOut = host.Send(get $"/rooms/{ALL_TALK}")
        Assert.Equal(302, signedOut.Status)
    }

let private hsts = "max-age=63072000; includeSubDomains"

[<Fact>]
let ``HS5 HSTS is on an HTTPS page, an asset, an error page and any host, and absent with DISABLE_SSL`` () =
    task {
        use! test = bootSeededConfigured ignore [ "DISABLE_SSL", " " ] "default"
        let! host, _ = startHost test.Booted (settingsOf [])
        use _host = host
        let page = host.Send(get "/session/new")
        let! page = page
        Assert.Equal(200, page.Status)
        Assert.Equal(Some hsts, page.Header "strict-transport-security")
        // Rails sends it to every host: `localhost` is excluded by the middleware's defaults.
        let! local = host.Send((get "/session/new").With("host", "localhost"))
        Assert.Equal(Some hsts, local.Header "strict-transport-security")
        let! other = host.Send((get "/session/new").With("host", "chat.example.com:8443"))
        Assert.Equal(Some hsts, other.Header "strict-transport-security")
        // An asset (served before the app's routes) and a public file.
        let! file = host.Send(get "/robots.txt")
        Assert.Equal(200, file.Status)
        Assert.Equal(Some hsts, file.Header "strict-transport-security")
        // A 500 page: `UseExceptionHandler` clears the response's headers, `ActionDispatch::SSL` is outermost in Rails.
        let! error = host.Send(get $"/rooms/{ALL_TALK}?bot_key[a]=b")
        Assert.Equal(500, error.Status)
        Assert.Equal(Some hsts, error.Header "strict-transport-security")
        // An exception outside an action reaches the middleware's handler, which clears the headers.
        let! throwing, _ = startHostWith test.Booted throwingCable (settingsOf [])
        use _throwing = throwing
        let! thrown = throwing.Send((get "/cable").With("accept", "text/html"))
        Assert.Equal(500, thrown.Status)
        Assert.Equal(Some hsts, thrown.Header "strict-transport-security")
        // A 404 from the route table.
        let! missing = host.Send(get "/no/such/page")
        Assert.Equal(404, missing.Status)
        Assert.Equal(Some hsts, missing.Header "strict-transport-security")
        // The redirect `force_ssl` also does is gone: with `assume_ssl` nothing is plain HTTP.
        Assert.NotEqual(301, page.Status)
    }

[<Fact>]
let ``HS5 without force_ssl (DISABLE_SSL) there is no HSTS`` () =
    task {
        use! test = bootSeeded "default"
        let! host, _ = startHost test.Booted (settingsOf [])
        use _host = host
        let! page = host.Send(get "/session/new")
        Assert.Equal(200, page.Status)
        Assert.Equal(None, page.Header "strict-transport-security")
        let! error = host.Send(get $"/rooms/{ALL_TALK}?bot_key[a]=b")
        Assert.Equal(500, error.Status)
        Assert.Equal(None, error.Header "strict-transport-security")
    }

[<Fact>]
let ``HS6 the head limits of Puma and hyper through Host.build`` () =
    task {
        use! test = bootSeeded "default"
        let! host, _ = startHost test.Booted (settingsOf [])
        use _host = host
        let! cookie = host.Send((get "/session/new").With("cookie", "a=" + String('x', 40_000) + "; b=seen"))
        Assert.Equal(200, cookie.Status)
        let! header = host.Send((get "/session/new").With("x-big", String('z', 70_000)))
        Assert.Equal(200, header.Status)
        let! longUrl = host.Send(get ("/session/new?q=" + String('q', 65_000)))
        Assert.Equal(200, longUrl.Status)
        let! tooBig = host.Send((get "/session/new").With("x-big", String('z', 450_000)))
        Assert.Equal(431, tooBig.Status)
        let! tooLong = host.Send(get ("/session/new?q=" + String('q', 70_000)))
        Assert.Equal(414, tooLong.Status)
        // No `Server` header, and header values as UTF-8.
        Assert.Equal(None, cookie.Header "server")
    }

/// The ban check's view of a client: a post from the banned address (`ips.banned`) is a 429.
let private bannedIp = "203.0.113.9"

let private post (b: Browser) (headers: (string * string) list) : Task<Reply> =
    let req =
        (request "POST" $"/rooms/{ALL_TALK}/messages")
            .With("accept", "text/vnd.turbo-stream.html, text/html, application/xhtml+xml")
            .With("sec-fetch-site", "same-origin")
            .Form(formBody [ "message[body]", "hello" ])
    b.Send(headers |> List.fold (fun (r: Req) (k, v) -> r.With(k, v)) req)

/// A browser over a host of `Host.build`'s, signed in as David.
let private browserOn (test: Test) (host: Campfire.App.Tests.Support.Host) : Browser =
    let b = Browser(Test(test.Booted, test.Dir, host, new CapturingLoggers()), None)
    b.AbsorbCookieHeader(davidCookie ())
    b

[<Fact>]
let ``HS7 remote_ip follows FORWARD_HEADERS: Rails' proxies when on, the peer when off`` () =
    task {
        use! test = bootSeeded "default"
        // On (Thruster's default without TLS): the loopback peer is a trusted proxy, so its forwarded address is the client's.
        let! on, _ = startHost test.Booted (settingsOf [ "FORWARD_HEADERS", "true" ])
        use _on = on
        let! banned = post (browserOn test on) [ "x-forwarded-for", bannedIp ]
        Assert.Equal(429, banned.Status)
        let! viaChain = post (browserOn test on) [ "x-forwarded-for", $"{bannedIp}, 10.1.2.3, 192.168.0.4" ]
        Assert.Equal(429, viaChain.Status)
        let! other = post (browserOn test on) [ "x-forwarded-for", "198.51.100.77" ]
        Assert.True((other.Status < 400), $"{other.Status}")
        // The default without TLS is on.
        Assert.True (settingsOf []).ForwardHeaders
        // Off: a forged header never counts, whoever the peer is (a private one here), for the address, the scheme and the host.
        let! off, _ = startHost test.Booted (settingsOf [ "FORWARD_HEADERS", "false" ])
        use _off = off
        let! forged = post (browserOn test off) [ "x-forwarded-for", bannedIp ]
        Assert.True((forged.Status < 400), $"a forged X-Forwarded-For was honoured: {forged.Status}")
        let! proto = off.Send((get $"/rooms/{ALL_TALK}").With("x-forwarded-proto", "https").With("x-forwarded-host", "chat.example.com"))
        Assert.Equal(Some "http://campfire.test/session/new", proto.Location)
        let! trusted = on.Send((get $"/rooms/{ALL_TALK}").With("x-forwarded-proto", "https").With("x-forwarded-host", "chat.example.com"))
        Assert.Equal(Some "https://chat.example.com/session/new", trusted.Location)
    }

/// Reads from `stream` until the connection is closed (0 bytes), at most `limit`; returns whether it closed and what came before.
let private readUntilClosed (stream: NetworkStream) (limit: TimeSpan) : Task<bool * string> =
    task {
        use cts = new CancellationTokenSource(limit)
        let received = new MemoryStream()
        let buffer = Array.zeroCreate<byte> 4096
        let mutable closed = false
        let mutable go = true
        while go do
            try
                let! n = stream.ReadAsync(Memory<byte> buffer, cts.Token)
                if n = 0 then
                    closed <- true
                    go <- false
                else
                    received.Write(buffer, 0, n)
            with
            | :? OperationCanceledException -> go <- false
            | :? IOException ->
                closed <- true
                go <- false
        return closed, Encoding.Latin1.GetString(received.ToArray())
    }

[<Fact>]
let ``HS8 HTTP_IDLE_TIMEOUT closes an idle keep-alive connection, HTTP_READ_TIMEOUT a slow request`` () =
    task {
        use! test = bootSeeded "default"
        let settings = settingsOf [ "HTTP_IDLE_TIMEOUT", "2"; "HTTP_READ_TIMEOUT", "2" ]
        Assert.Equal(TimeSpan.FromSeconds 2.0, settings.IdleTimeout)
        let! host, _ = startHost test.Booted settings
        use _host = host
        // One request on a keep-alive connection, then silence: the connection closes after about the idle timeout.
        use idle = new TcpClient()
        do! idle.ConnectAsync(IPAddress.Loopback, host.Port)
        let stream = idle.GetStream()
        do! stream.WriteAsync(Encoding.ASCII.GetBytes "GET /up HTTP/1.1\r\nHost: campfire.test\r\n\r\n")
        let first = Array.zeroCreate<byte> 4096
        let! n = stream.ReadAsync(Memory<byte> first)
        Assert.StartsWith("HTTP/1.1 200", Encoding.ASCII.GetString(first, 0, n))
        let started = Stopwatch.StartNew()
        let! closed, _ = readUntilClosed stream (TimeSpan.FromSeconds 8.0)
        Assert.True(closed, "the idle connection stayed open")
        // The lower bound is the point (a timeout that fired at once is none); the upper only tells 2 s from never.
        Assert.True(started.Elapsed >= TimeSpan.FromMilliseconds 1500.0 && started.Elapsed < TimeSpan.FromSeconds 7.0, $"closed after {started.Elapsed}")
        // Request headers that never finish: cut after about the read timeout (Kestrel answers 408 first).
        use slow = new TcpClient()
        do! slow.ConnectAsync(IPAddress.Loopback, host.Port)
        let slowStream = slow.GetStream()
        do! slowStream.WriteAsync(Encoding.ASCII.GetBytes "GET /up HTTP/1.1\r\nHost: x\r\n")
        let began = Stopwatch.StartNew()
        let! slowClosed, answer = readUntilClosed slowStream (TimeSpan.FromSeconds 8.0)
        Assert.True(slowClosed, "the slow request stayed open")
        Assert.True(began.Elapsed >= TimeSpan.FromMilliseconds 1500.0 && began.Elapsed < TimeSpan.FromSeconds 7.0, $"closed after {began.Elapsed}")
        Assert.True(answer = "" || answer.StartsWith "HTTP/1.1 408", answer)
    }

[<Fact>]
let ``HS9 MAX_REQUEST_BODY below 16 MiB is the app's 413, above it the 16 MiB cap stays`` () =
    task {
        let dir = Directory.CreateTempSubdirectory("campfire-app-test").FullName
        try
            let boot (vars: (string * string) list) =
                task {
                    let settings = settingsOf vars
                    let! booted = Boot.bootEdge (Boot.servingEdge settings) None ignore (configFor dir []) (new CapturingLoggers())
                    let! host, _ = startHost booted settings
                    return host, booted
                }
            let formOf (n: int) = (request "POST" "/session").Form("email_address=" + String('a', n))
            let! lowered, booted = boot [ "MAX_REQUEST_BODY", "1024" ]
            use _lowered = lowered
            let! small = lowered.Send(formOf 100)
            Assert.NotEqual(413, small.Status)
            let! big = lowered.Send(formOf 2000)
            Assert.Equal(413, big.Status)
            // Above the cap: the cap is still 16 MiB (a value of 99 GB is no larger).
            let edgeOf (vars: (string * string) list) = Boot.servingEdge (settingsOf vars) (EdgeConfig.production true (Boot.errorPages ()))
            Assert.Equal(1024L, (edgeOf [ "MAX_REQUEST_BODY", "1024" ]).MaxRequestBody)
            Assert.Equal(Edge.MaxRequestBody, (edgeOf [ "MAX_REQUEST_BODY", "99000000000" ]).MaxRequestBody)
            Assert.Equal(Edge.MaxRequestBody, (edgeOf [ "MAX_REQUEST_BODY", "0" ]).MaxRequestBody)
            Assert.Equal(Edge.MaxRequestBody, (edgeOf []).MaxRequestBody)
            Boot.closeDatabase booted.App
        finally
            try Directory.Delete(dir, true) with _ -> ()
    }

[<Fact>]
let ``HS10 a large value of a timeout means none, and the host starts`` () =
    task {
        use! test = bootSeeded "default"
        let settings = settingsOf [ "HTTP_IDLE_TIMEOUT", "99999999999"; "HTTP_READ_TIMEOUT", "0" ]
        Assert.Equal(TimeSpan.MaxValue, settings.IdleTimeout)
        Assert.Equal(TimeSpan.MaxValue, settings.ReadTimeout)
        let! host, app = startHost test.Booted settings
        use _host = host
        let limits = app.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value.Limits
        Assert.Equal(TimeSpan.MaxValue, limits.KeepAliveTimeout)
        Assert.Equal(TimeSpan.MaxValue, limits.RequestHeadersTimeout)
        let! up = host.Send(get "/up")
        Assert.Equal(200, up.Status)
    }

let private nextFrame (ws: ClientWebSocket) : string * WebSocketMessageType =
    use limit = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
    let buffer = Array.zeroCreate<byte> 4096
    let r = ws.ReceiveAsync(ArraySegment<byte>(buffer), limit.Token).GetAwaiter().GetResult()
    Encoding.UTF8.GetString(buffer, 0, r.Count), r.MessageType

[<Fact>]
let ``HS11 stopping the host closes the cable sockets with server_restart first, then the host stops within the grace`` () =
    task {
        use! test = bootSeeded "default"
        let settings = settingsOf []
        let! host, app = startHost test.Booted settings
        Assert.Equal(Boot.ShutdownGrace, app.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout)
        Assert.Equal(TimeSpan.FromSeconds 10.0, Boot.ShutdownGrace)
        let connect () =
            task {
                let ws = new ClientWebSocket()
                ws.Options.AddSubProtocol "actioncable-v1-json"
                ws.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{host.Port}")
                ws.Options.SetRequestHeader("Cookie", davidCookie ())
                do! ws.ConnectAsync(Uri $"ws://127.0.0.1:{host.Port}/cable", CancellationToken.None)
                return ws
            }
        let! sockets = Task.WhenAll [| connect (); connect () |]
        for ws in sockets do
            Assert.Equal("""{"type":"welcome"}""", fst (nextFrame ws))
        let started = Stopwatch.StartNew()
        let stopping = host.StopAsync()
        for ws in sockets do
            let mutable frame, kind = nextFrame ws
            while kind = WebSocketMessageType.Text && frame.Contains "\"type\":\"ping\"" do
                let f, k = nextFrame ws
                frame <- f
                kind <- k
            Assert.Equal("""{"type":"disconnect","reason":"server_restart","reconnect":true}""", frame)
            let _, closed = nextFrame ws
            Assert.Equal(WebSocketMessageType.Close, closed)
            do! ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        do! stopping
        Assert.True(started.Elapsed < Boot.ShutdownGrace, $"{started.Elapsed}")
        for ws in sockets do
            ws.Dispose()
        do! (host :> IAsyncDisposable).DisposeAsync()
    }

let private lookupOf (vars: (string * string) list) : string -> string | null =
    let map = dict vars
    fun name ->
        match map.TryGetValue name with
        | true, v -> v
        | _ -> null

[<Fact>]
let ``HS12 FrontSettings.fromLookup reads Thruster's variables: the THRUSTER_ prefix, Go's booleans and integers, the defaults`` () =
    let none = FrontSettings.fromLookup (lookupOf [])
    Assert.Equal((80, 443, 3000), (none.HttpPort, none.HttpsPort, none.TargetPort))
    Assert.Equal(IPAddress.Loopback, none.TargetBind)
    Assert.Equal(64L * 1024L * 1024L, none.CacheSize)
    Assert.Equal(1024L * 1024L, none.MaxCacheItemSize)
    Assert.Equal(TimeSpan.FromSeconds 60.0, none.IdleTimeout)
    Assert.Equal(TimeSpan.FromSeconds 30.0, none.ReadTimeout)
    Assert.Equal("./storage/thruster", none.StoragePath)
    Assert.Equal(FrontSettings.LetsEncryptUrl, none.AcmeDirectory)
    Assert.True(none.ForwardHeaders && none.LogRequests && not none.HasTls)
    Assert.Equal(0L, none.MaxRequestBody)
    Assert.True none.ExternalAccount.IsNone
    // The prefixed name wins.
    let prefixed = FrontSettings.fromLookup (lookupOf [ "HTTP_PORT", "8080"; "THRUSTER_HTTP_PORT", "9090"; "HTTPS_PORT", "8443" ])
    Assert.Equal((9090, 8443), (prefixed.HttpPort, prefixed.HttpsPort))
    // A value that does not parse is the default (Go's Atoi and ParseBool are strict).
    let bad =
        FrontSettings.fromLookup (lookupOf [ "HTTP_PORT", "eighty"; "HTTP_READ_TIMEOUT", "5s"; "LOG_REQUESTS", "yes"; "TARGET_PORT", "70000"; "CACHE_SIZE", "1.5" ])
    Assert.Equal((80, 3000), (bad.HttpPort, bad.TargetPort))
    Assert.Equal(TimeSpan.FromSeconds 30.0, bad.ReadTimeout)
    Assert.True bad.LogRequests
    Assert.Equal(64L * 1024L * 1024L, bad.CacheSize)
    for word in [ "1"; "t"; "T"; "TRUE"; "true"; "True" ] do
        Assert.True((FrontSettings.fromLookup (lookupOf [ "LOG_REQUESTS", "0"; "FORWARD_HEADERS", word ])).ForwardHeaders, word)
    for word in [ "0"; "f"; "F"; "FALSE"; "false"; "False" ] do
        Assert.False((FrontSettings.fromLookup (lookupOf [ "LOG_REQUESTS", word ])).LogRequests, word)
    // TARGET_BIND: four plain decimal octets or IPv6.
    Assert.Equal(IPAddress.Any, (FrontSettings.fromLookup (lookupOf [ "TARGET_BIND", "0.0.0.0" ])).TargetBind)
    Assert.Equal("::", (FrontSettings.fromLookup (lookupOf [ "TARGET_BIND", "::" ])).TargetBind.ToString())
    for odd in [ "everywhere"; "127.1"; "0x7f.0.0.1" ] do
        Assert.Equal(IPAddress.Loopback, (FrontSettings.fromLookup (lookupOf [ "TARGET_BIND", odd ])).TargetBind)
    // Timeouts: zero, negative and 49 days or more are none.
    let timeouts = FrontSettings.fromLookup (lookupOf [ "HTTP_IDLE_TIMEOUT", "9999999999999"; "HTTP_READ_TIMEOUT", "-5" ])
    Assert.Equal((TimeSpan.MaxValue, TimeSpan.MaxValue), (timeouts.IdleTimeout, timeouts.ReadTimeout))
    Assert.Equal(TimeSpan.FromDays 1.0, (FrontSettings.fromLookup (lookupOf [ "HTTP_IDLE_TIMEOUT", "86400" ])).IdleTimeout)
    // TLS_DOMAIN turns TLS on and FORWARD_HEADERS off by default.
    let tls = FrontSettings.fromLookup (lookupOf [ "TLS_DOMAIN", " chat.example.com, ,other.example.com " ])
    Assert.Equal<string list>([ "chat.example.com"; "other.example.com" ], tls.TlsDomains)
    Assert.True(tls.HasTls && not tls.ForwardHeaders)
    Assert.True((FrontSettings.fromLookup (lookupOf [ "TLS_DOMAIN", "a.example.com"; "FORWARD_HEADERS", "true" ])).ForwardHeaders)
    Assert.False((FrontSettings.fromLookup (lookupOf [ "SSL_DOMAIN", "a.example.com" ])).HasTls)
    Assert.False((FrontSettings.fromLookup (lookupOf [ "TLS_DOMAIN", " , " ])).HasTls)
    // The external account binding needs both halves.
    Assert.Equal(ValueSome("kid", "key"), (FrontSettings.fromLookup (lookupOf [ "EAB_KID", "kid"; "EAB_HMAC_KEY", "key" ])).ExternalAccount)
    Assert.True((FrontSettings.fromLookup (lookupOf [ "EAB_KID", "kid" ])).ExternalAccount.IsNone)
    // No longer read: cleartext HTTP/2, the gzip layer, the write timeout and DEBUG change nothing.
    let ignored =
        FrontSettings.fromLookup (lookupOf [ "H2C_ENABLED", "1"; "GZIP_COMPRESSION_ENABLED", "0"; "GZIP_COMPRESSION_JITTER", "5"; "HTTP_WRITE_TIMEOUT", "1"; "DEBUG", "1" ])
    Assert.Equal(none, ignored)

[<Fact>]
let ``the open-file limit is raised to the hard one`` () =
    if OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() then
        let struct (softBefore, _) = (OpenFiles.openFileLimits ()).Value
        let limit = (OpenFiles.raiseOpenFileLimit ()).Value
        let struct (soft, hard) = (OpenFiles.openFileLimits ()).Value
        Assert.Equal(limit, soft)
        Assert.True(soft >= softBefore)
        // macOS refuses a soft limit past `kern.maxfilesperproc`, and its hard limit is "unlimited".
        if OperatingSystem.IsLinux() then Assert.Equal(hard, soft)

// --- HS2 and HS4: TLS_DOMAIN (unit 7.4b) -------------------------------------------------------------------------------------------

/// A self-signed certificate for `name`, written to `storage` as the ACME cache file, valid for 90 days from now: loaded at start, never due.
let private cachedCertificate (storage: string) (name: string) : string =
    use key = ECDsa.Create ECCurve.NamedCurves.nistP256
    let request = CertificateRequest("CN=" + name, key, HashAlgorithmName.SHA256)
    let san = SubjectAlternativeNameBuilder()
    san.AddDnsName name
    request.CertificateExtensions.Add(san.Build false)
    let now = DateTimeOffset.UtcNow
    use certificate = request.CreateSelfSigned(now - TimeSpan.FromDays 1.0, now + TimeSpan.FromDays 89.0)
    DirCache.write storage name key (certificate.ExportCertificatePem())
    certificate.Thumbprint

let private tempStorage () : string =
    let path = Path.Combine(Path.GetTempPath(), "campfire-host-" + Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory path |> ignore
    path

/// A TLS install: `TLS_DOMAIN` names, the ACME cache in `storage` and a directory nothing listens on, so no order can succeed.
let private tlsSettings (storage: string) (domains: string) (extra: (string * string) list) : FrontSettings =
    settingsOf (
        [ "TLS_DOMAIN", domains
          "HTTPS_PORT", string (freePort ())
          "STORAGE_PATH", storage
          "ACME_DIRECTORY", "http://127.0.0.1:1/directory" ]
        @ extra
    )

/// A TLS client to `port` offering `serverName` (null: no name) that trusts anything: the certificate's thumbprint, or the failure.
let private handshake (port: int) (serverName: string | null) : Task<Result<string * string, string>> =
    task {
        use client = new TcpClient()
        do! client.ConnectAsync(IPAddress.Loopback, port)
        use ssl = new SslStream(client.GetStream(), false)
        let options = SslClientAuthenticationOptions()
        options.TargetHost <- (match serverName with null -> "127.0.0.1" | n -> n)
        options.RemoteCertificateValidationCallback <- (fun _ _ _ _ -> true)
        options.ApplicationProtocols <- Collections.Generic.List<SslApplicationProtocol>([ SslApplicationProtocol.Http11 ])
        use cancel = new CancellationTokenSource(TimeSpan.FromSeconds 20.0)
        try
            do! ssl.AuthenticateAsClientAsync(options, cancel.Token)
            let thumbprint = (new X509Certificate2(nonNull ssl.RemoteCertificate)).Thumbprint
            // One request over the connection: the app answers HTTPS.
            let request = Encoding.ASCII.GetBytes "GET /up HTTP/1.1\r\nHost: chat.example.test\r\nConnection: close\r\n\r\n"
            do! ssl.WriteAsync(ReadOnlyMemory<byte> request, cancel.Token)
            let buffer = Array.zeroCreate<byte> 256
            let! n = ssl.ReadAsync(Memory<byte> buffer, cancel.Token)
            return Ok(thumbprint, Encoding.ASCII.GetString(buffer, 0, n).Split('\r').[0])
        with e ->
            return Error e.Message
    }

[<Fact>]
let ``HS2 with TLS_DOMAIN the HTTP port answers the ACME challenge for its names, 421s others, and 301s the rest to HTTPS`` () =
    task {
        use! test = bootSeeded "default"
        let storage = tempStorage ()
        cachedCertificate storage "chat.example.test" |> ignore
        let settings = tlsSettings storage "chat.example.test" []
        let! host, app = startHost test.Booted settings
        use _host = host
        let store = app.Services.GetRequiredService<CertificateStore>()
        store.AddChallenge("tok-1", "tok-1.thumb")
        let on (name: string) (path: string) = (get path).With("host", name)
        // A TLS_DOMAIN host: a permanent redirect to the same target over HTTPS, the HTTPS port in it, and nothing of the app.
        let! moved = host.Send(on "chat.example.test" "/rooms/1?a=b&c=%20d")
        Assert.Equal(301, moved.Status)
        Assert.Equal(Some("https://chat.example.test:" + string settings.HttpsPort + "/rooms/1?a=b&c=%20d"), moved.Header "location")
        // The name is normalized (case, a port) before the check and in the Location.
        let! mixed = host.Send(on "CHAT.Example.Test:8080" "/x")
        Assert.Equal(301, mixed.Status)
        Assert.Equal(Some $"https://chat.example.test:{settings.HttpsPort}/x", mixed.Header "location")
        let! up = host.Send(on "chat.example.test" "/up")
        Assert.Equal(301, up.Status)
        // A POST gets the same redirect, without a body.
        let! posted = host.Send(request "POST" "/session" |> fun r -> r.With("host", "chat.example.test"))
        Assert.Equal(301, posted.Status)
        // A Host outside TLS_DOMAIN, whatever its form, is a 421: never a cacheable redirect to a name the host cannot serve.
        for other in [ "other.example.test"; "localhost"; "127.0.0.1"; "chat.example.test.evil.test"; "[::1]:80" ] do
            let! reply = host.Send(on other "/rooms/1")
            Assert.True((reply.Status = 421), $"{other}: {reply.Status}")
            Assert.Equal(None, reply.Header "location")
        // The challenge: the token's key authorization for a TLS_DOMAIN host, 404 for an unknown token, 403 for another host.
        let! challenge = host.Send(on "chat.example.test" "/.well-known/acme-challenge/tok-1")
        Assert.Equal(200, challenge.Status)
        Assert.Equal("tok-1.thumb", challenge.Text)
        let! unknown = host.Send(on "chat.example.test" "/.well-known/acme-challenge/nope")
        Assert.Equal(404, unknown.Status)
        let! foreign = host.Send(on "other.example.test" "/.well-known/acme-challenge/tok-1")
        Assert.Equal(403, foreign.Status)
        // TARGET_PORT is the app itself, plain.
        let! target = (onPort app settings.TargetPort).Send(get "/up")
        Assert.Equal(200, target.Status)
    }

[<Fact>]
let ``HS4 the HTTPS port hands each TLS_DOMAIN name its own certificate, refuses every other name, and serves the app`` () =
    task {
        use! test = bootSeeded "default"
        let storage = tempStorage ()
        let chat = cachedCertificate storage "chat.example.test"
        let other = cachedCertificate storage "other.example.test"
        Assert.NotEqual<string>(chat, other)
        let settings = tlsSettings storage "chat.example.test,other.example.test" []
        let! host, _ = startHost test.Booted settings
        use _host = host
        let! first = handshake settings.HttpsPort "chat.example.test"
        Assert.Equal(Ok(chat, "HTTP/1.1 200 OK"), first)
        let! second = handshake settings.HttpsPort "OTHER.example.test."
        Assert.Equal(Ok(other, "HTTP/1.1 200 OK"), second)
        // A name outside TLS_DOMAIN, no name, an IP and a name `normalize` refuses get no certificate: the handshake fails.
        for name in ([ "evil.example.test"; null; "chat.example.test.evil.test"; "localhost"; "a/b.example.test" ]: (string | null) list) do
            let! refused = handshake settings.HttpsPort name
            Assert.True(refused.IsError, $"{name}: {refused}")
    }

[<Fact>]
let ``HS4 the first handshake of a name with no certificate yet fails within HTTP_READ_TIMEOUT, and is bounded when that is none`` () =
    task {
        use! test = bootSeeded "default"
        let storage = tempStorage ()
        // No cache file: the order fails (nothing listens on the directory), so the handshake waits for a certificate that never comes.
        let settings = tlsSettings storage "chat.example.test" [ "HTTP_READ_TIMEOUT", "2" ]
        let! host, _ = startHost test.Booted settings
        use _host = host
        let clock = Stopwatch.StartNew()
        let! refused = handshake settings.HttpsPort "chat.example.test"
        Assert.True(refused.IsError, $"{refused}")
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds 12.0, $"the handshake took {clock.Elapsed}")
        // "No timeout" is a finite handshake budget all the same (checkpoint note 4).
        let none = tlsSettings storage "chat.example.test" [ "HTTP_READ_TIMEOUT", "0" ]
        Assert.Equal(TimeSpan.MaxValue, none.ReadTimeout)
        let options = Web.Host.handshakeOptions none (CertificateStore none.TlsDomains)
        Assert.True(options.HandshakeTimeout < TimeSpan.FromMinutes 5.0, $"{options.HandshakeTimeout}")
        Assert.True(options.HandshakeTimeout > TimeSpan.Zero)
    }

[<Fact>]
let ``TLS_DOMAIN names that normalize refuses are logged, and a list with none usable fails the boot`` () =
    let logs = Collections.Generic.List<string>()
    let logger =
        { new Microsoft.Extensions.Logging.ILogger with
            member _.BeginScope _ = null
            member _.IsEnabled _ = true
            member _.Log(level, _, state, e, formatter) = logs.Add(string level + ": " + formatter.Invoke(state, e)) }
    let one = FrontSettings.fromLookup (lookupOf [ "TLS_DOMAIN", "chat.example.test,nodot,a/b.example.test" ])
    Web.Host.validateDomains one logger
    Assert.Equal(2, logs.Count)
    Assert.Contains(logs, fun l -> l.Contains "nodot")
    // Go's `idna.Lookup` uses STD3 rules: an underscore name is refused, and so is it here.
    Assert.True((ServerNames.normalize "a_b.example.test").IsError)
    Assert.Equal(Ok "xn--bcher-kva.example.test", ServerNames.normalize "Bücher.Example.Test")
    let none = FrontSettings.fromLookup (lookupOf [ "TLS_DOMAIN", "nodot,localhost" ])
    Assert.Throws<InvalidOperationException>(fun () -> Web.Host.validateDomains none logger) |> ignore
    // Plain HTTP has nothing to validate.
    Web.Host.validateDomains (FrontSettings.fromLookup (lookupOf [])) logger
