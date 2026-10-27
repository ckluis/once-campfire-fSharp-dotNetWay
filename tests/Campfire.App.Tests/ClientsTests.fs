// The named clients (plans/design-jobs.md J2, J3; tests CL1-CL5 and GD1-GD4 of section 5).
module Campfire.App.Tests.ClientsTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Xunit
open Campfire.App.Integrations
open Campfire.App.Tests.IntegrationsSupport

let private factoryOf (provider: ServiceProvider) : IHttpClientFactory = provider.GetRequiredService<IHttpClientFactory>()

let private publicHost = IPAddress.Parse "93.184.216.34"

/// Clients whose guard sees `public.example` as a public address that the dial sends to `server`.
let private guardedClients (server: IPEndPoint) : ServiceProvider * MappingDialer =
    let dialer = MappingDialer(HashSet [ publicHost ], server)
    let provider =
        httpClients (fun services ->
            services.AddSingleton<IResolver>(FakeResolver([ "public.example", [ string publicHost ] ]) :> IResolver) |> ignore
            services.AddSingleton<IDialer>(dialer :> IDialer) |> ignore)
    provider, dialer

let private deliver (provider: ServiceProvider) (url: string) =
    WebhookClient.deliver (factoryOf provider) TimeProvider.System url """{"message":"hi"}"""

/// CL1: a POST reaches the server exactly once, whether it is answered 500, reset or never answered.
[<Fact>]
let ``a webhook POST is sent once on a 500`` () =
    task {
        use server = FakeServer.Start [ Route.create "POST" "*" "/hook" 500 |> Route.header "Content-Type" "text/plain" |> Route.text "no" ]
        use clients = httpClients ignore
        let! outcome = deliver clients $"http://{server.Addr}/hook"
        match outcome with
        | Ok delivery -> Assert.Equal(Some 500, delivery.Status)
        | Error error -> failwith (WebhookError.message error)
        Assert.Equal(1, server.Received().Length)
    }

[<Fact>]
let ``a webhook POST is sent once on a reset`` () =
    task {
        let handle, address, accepted =
            countingServer (fun client ->
                task {
                    let! _ = client.GetStream().ReadAsync(Array.zeroCreate<byte> 4096, 0, 4096)
                    client.Client.LingerState <- LingerOption(true, 0)
                    client.Close()
                })
        use _server = handle
        use clients = httpClients ignore
        let! outcome = deliver clients $"http://{address}/hook"
        match outcome with
        | Error(WebhookError.Transport(TransportError.Io _)) -> ()
        | other -> failwith $"{other}"
        // Give a retry the time it would need to arrive.
        do! Task.Delay 500
        Assert.Equal(1, accepted ())
    }

[<Fact>]
let ``a webhook POST is sent once on a timeout`` () =
    task {
        use server = FakeServer.Start [ { Route.create "POST" "*" "/hook" 200 with Delay = TimeSpan.FromSeconds 9.0 } ]
        use clients = httpClients ignore
        let! outcome = deliver clients $"http://{server.Addr}/hook"
        match outcome with
        | Ok delivery -> Assert.Equal({ Status = None; Reply = ReplyText "Failed to respond within 7 seconds" }, delivery)
        | Error error -> failwith (WebhookError.message error)
        Assert.Equal(1, server.Received().Length)
    }

let private urlFor (name: string) (server: IPEndPoint) (path: string) : string =
    if name = Clients.Webhook then $"http://{server}{path}" else $"http://public.example:{server.Port}{path}"

/// CL2: no client follows a 302 by itself.
[<Fact>]
let ``no client follows a redirect`` () =
    task {
        use server =
            FakeServer.Start
                [ Route.create "GET" "*" "/start" 302 |> Route.header "Location" "/landed" |> Route.text "moved"
                  Route.create "GET" "*" "/landed" 200 |> Route.text "here" ]
        let provider, _ = guardedClients server.Addr
        use provider = provider
        for name in [ Clients.Opengraph; Clients.Push; Clients.Webhook ] do
            let client = (factoryOf provider).CreateClient name
            use! response = client.GetAsync(urlFor name server.Addr "/start")
            Assert.Equal(302, int response.StatusCode)
        Assert.Equal(3, server.Received().Length)
        Assert.True(server.Received() |> List.forall (fun r -> r.Target = "/start"))
    }

/// CL3: proxy variables in the process do not route a request.
[<Fact>]
let ``proxy variables are ignored`` () =
    task {
        let handle, proxy, proxied = countingServer (fun client -> task { client.Close() })
        use _proxy = handle
        use server = FakeServer.Start [ Route.create "GET" "*" "/direct" 200 |> Route.text "direct" ]
        let names = [ "HTTP_PROXY"; "http_proxy"; "HTTPS_PROXY"; "https_proxy"; "ALL_PROXY"; "all_proxy" ]
        let before = names |> List.map (fun name -> name, Environment.GetEnvironmentVariable name)
        try
            for name in names do
                Environment.SetEnvironmentVariable(name, $"http://{proxy}")
            let provider, _ = guardedClients server.Addr
            use provider = provider
            for name in [ Clients.Opengraph; Clients.Push; Clients.Webhook ] do
                let client = (factoryOf provider).CreateClient name
                use! response = client.GetAsync(urlFor name server.Addr "/direct")
                Assert.Equal(200, int response.StatusCode)
            Assert.Equal(0, proxied ())
        finally
            for name, value in before do
                Environment.SetEnvironmentVariable(name, value)
    }

/// CL2 and CL3 by construction, since a loopback request bypasses a proxy whatever the variables say: the handler of every client.
[<Fact>]
let ``every handler is configured without redirects, proxy or cookies`` () =
    use provider = httpClients ignore
    for guarded, timeout in [ true, 5.0; true, 10.0; false, 7.0 ] do
        use handler = Clients.handler guarded (TimeSpan.FromSeconds timeout) true provider
        Assert.False handler.AllowAutoRedirect
        Assert.False handler.UseProxy
        Assert.False handler.UseCookies
        Assert.Equal(DecompressionMethods.GZip ||| DecompressionMethods.Deflate, handler.AutomaticDecompression)
        Assert.Equal(TimeSpan.FromSeconds timeout, handler.ConnectTimeout)
        Assert.Equal(guarded, not (isNull handler.ConnectCallback))

/// CL4: Net::HTTP's `Accept` and `User-Agent` on the opengraph and webhook requests; a push request has the library's headers instead
/// (class A: push services read neither).
[<Fact>]
let ``requests carry Net::HTTP's Accept and User-Agent`` () =
    task {
        use server = FakeServer.Start [ Route.create "GET" "*" "/page" 200 |> Route.text "x" ]
        let provider, _ = guardedClients server.Addr
        use provider = provider
        for name in [ Clients.Opengraph; Clients.Webhook; Clients.Push ] do
            use! _ = (factoryOf provider).CreateClient(name).GetAsync(urlFor name server.Addr "/page")
            ()
        match server.Received() with
        | [ opengraph; webhook; push ] ->
            for request in [ opengraph; webhook ] do
                Assert.Equal(Some "*/*", request.Header "Accept")
                Assert.Equal(Some "Ruby", request.Header "User-Agent")
            Assert.Equal(None, push.Header "User-Agent")
            Assert.Equal(None, push.Header "Accept")
        | other -> failwith $"{other.Length} requests"
    }

/// CL5: `readLimited` stops a gigabyte gzip bomb at the limit, and inflates what fits.
[<Fact>]
let ``readLimited stops a gzip bomb at the limit`` () =
    task {
        use server =
            FakeServer.Start
                [ Route.create "GET" "*" "/bomb" 200 |> Route.header "Content-Encoding" "gzip" |> Route.body (gzipBomb 1024)
                  Route.create "GET" "*" "/small" 200 |> Route.header "Content-Encoding" "gzip" |> Route.body (gzipBomb 3) ]
        use clients = httpClients ignore
        let client = (factoryOf clients).CreateClient Clients.Webhook
        let started = DateTime.UtcNow
        use! bomb = client.GetAsync($"http://{server.Addr}/bomb", HttpCompletionOption.ResponseHeadersRead)
        let! bombed = Clients.readLimited bomb.Content 5242880 CancellationToken.None
        Assert.True(bombed.IsNone)
        // Inflating the whole gigabyte would take minutes; stopping at the limit takes about a second.
        Assert.True((DateTime.UtcNow - started) < TimeSpan.FromSeconds 10.0)
        use! small = client.GetAsync($"http://{server.Addr}/small", HttpCompletionOption.ResponseHeadersRead)
        let! body = Clients.readLimited small.Content 5242880 CancellationToken.None
        Assert.Equal<byte[]>(Array.zeroCreate (3 * 1024 * 1024), body.Value)
    }

[<Fact>]
let ``contentType reads the raw header like Net::HTTP`` () =
    task {
        use server =
            FakeServer.Start
                [ Route.create "GET" "*" "/a" 200 |> Route.header "Content-Type" "Text/Plain ; charset=utf-8" |> Route.text "x"
                  Route.create "GET" "*" "/b" 200 |> Route.header "Content-Type" "image" |> Route.text "x"
                  Route.create "GET" "*" "/c" 200 |> Route.text "x" ]
        use clients = httpClients ignore
        let client = (factoryOf clients).CreateClient Clients.Webhook
        let typeOf (path: string) =
            task {
                use! response = client.GetAsync($"http://{server.Addr}{path}")
                return Clients.contentType response
            }
        let! a = typeOf "/a"
        let! b = typeOf "/b"
        let! c = typeOf "/c"
        Assert.Equal(Some "Text/Plain", a)
        Assert.Equal(Some "image", b)
        Assert.Equal(None, c)
    }

/// GD1: an unpinned request to a host that resolves to 127.0.0.1 is refused at connect, and the dial is never called.
[<Fact>]
let ``an unpinned request to a private host is refused at connect`` () =
    task {
        use server = FakeServer.Start [ Route.create "GET" "*" "/" 200 |> Route.text "secret" ]
        let dialer = MappingDialer(HashSet [ IPAddress.Loopback ], server.Addr)
        use provider =
            httpClients (fun services ->
                services.AddSingleton<IResolver>(FakeResolver([ "rebinding.example", [ "127.0.0.1" ] ]) :> IResolver) |> ignore
                services.AddSingleton<IDialer>(dialer :> IDialer) |> ignore)
        let client = (factoryOf provider).CreateClient Clients.Opengraph
        let! failure = Assert.ThrowsAsync<HttpRequestException>(fun () -> client.GetAsync "http://rebinding.example/" :> Task)
        Assert.Contains("Attempt to access private IP via rebinding.example", failure.Message + string failure.InnerException)
        Assert.Empty dialer.Dialed
        Assert.Empty(server.Received())
    }

/// GD2: a request pinned to 10.0.0.1 is refused at connect; one pinned to a public address connects there.
[<Fact>]
let ``a pinned private address is refused at connect`` () =
    task {
        use server = FakeServer.Start [ Route.create "GET" "*" "/" 200 |> Route.text "ok" ]
        let provider, dialer = guardedClients server.Addr
        use provider = provider
        let client = (factoryOf provider).CreateClient Clients.Opengraph
        use planted = Clients.pin (IPAddress.Parse "10.0.0.1") (new HttpRequestMessage(HttpMethod.Get, "http://public.example/"))
        let! failure = Assert.ThrowsAsync<HttpRequestException>(fun () -> client.SendAsync planted :> Task)
        Assert.Contains("Attempt to access private IP via public.example", failure.Message + string failure.InnerException)
        Assert.Empty dialer.Dialed
        use pinned = Clients.pin publicHost (new HttpRequestMessage(HttpMethod.Get, $"http://public.example:{server.Addr.Port}/"))
        use! response = client.SendAsync pinned
        Assert.Equal(200, int response.StatusCode)
        Assert.Equal<IPEndPoint list>([ IPEndPoint(publicHost, server.Addr.Port) ], dialer.Dialed)
    }

let private bits (ip: IPAddress) : UInt128 =
    let mutable value = UInt128.Zero
    for b in ip.GetAddressBytes() do
        value <- (value <<< 8) ||| UInt128(0UL, uint64 b)
    value

let private ofBits (family: AddressFamily) (value: UInt128) : IPAddress =
    let length = if family = AddressFamily.InterNetwork then 4 else 16
    IPAddress(Array.init length (fun i -> byte (value >>> (8 * (length - 1 - i)))))

let private lastOf (network: IPNetwork) : UInt128 =
    let width = if network.BaseAddress.AddressFamily = AddressFamily.InterNetwork then 32 else 128
    bits network.BaseAddress ||| ((UInt128.One <<< (width - network.PrefixLength)) - UInt128.One)

/// GD3: the `IPNetwork` lists are the old lists: their sizes, and for every network the first and last address (by the old arithmetic)
/// inside it and its neighbours outside it; and every disallowed network's ends are blocked.
[<Fact>]
let ``the IPNetwork lists are the old lists`` () =
    Assert.Equal(16, Guard.disallowedIpv4List.Length)
    Assert.Equal(11, Guard.disallowedIpv6List.Length)
    Assert.Equal(36, Guard.ianaAllocatedIpv6Unicast.Length)
    Assert.Equal(2, Guard.globallyReachableIetfAssignments.Length)
    let lists = Guard.disallowedIpv4List @ Guard.disallowedIpv6List @ Guard.ianaAllocatedIpv6Unicast @ Guard.globallyReachableIetfAssignments
    for network in lists do
        let family = network.BaseAddress.AddressFamily
        let first = bits network.BaseAddress
        let last = lastOf network
        Assert.True(network.Contains(ofBits family first), $"{network} first")
        Assert.True(network.Contains(ofBits family last), $"{network} last")
        Assert.False(network.Contains(ofBits family (first - UInt128.One)), $"{network} before")
        Assert.False(network.Contains(ofBits family (last + UInt128.One)), $"{network} after")
    for network in Guard.disallowedIpv4List @ Guard.disallowedIpv6List do
        let family = network.BaseAddress.AddressFamily
        Assert.True(Guard.blockedAddress (ofBits family (bits network.BaseAddress)), $"{network} first blocked")
        Assert.True(Guard.blockedAddress (ofBits family (lastOf network)), $"{network} last blocked")

/// GD4: an IPv4-mapped IPv6 answer is classified as the IPv4 address inside it (a private one is blocked), and surfguard blocks the
/// mapped form outright, so a public address inside it is dropped too.
[<Fact>]
let ``an IPv4-mapped answer is classified as its IPv4 address`` () =
    task {
        for v4 in [ "10.0.0.1"; "127.0.0.1"; "169.254.169.254"; "192.168.1.1" ] do
            Assert.True(Guard.blockedAddress ((IPAddress.Parse v4).MapToIPv6()), $"::ffff:{v4}")
        Assert.True(Guard.blockedAddress ((IPAddress.Parse "8.8.8.8").MapToIPv6()))
        let resolver = FakeResolver([ "mapped.example", [ "::ffff:127.0.0.1"; "93.184.216.34" ] ]) :> IResolver
        let! publicAnswers = Guard.resolvePublicIps resolver "mapped.example"
        Assert.Equal(Ok [ publicHost ], publicAnswers)
    }
