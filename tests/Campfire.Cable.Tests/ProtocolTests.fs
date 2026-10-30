// Port of rust/crates/cable/tests/protocol.rs
//
// The Action Cable protocol, driven over a real socket.
module Campfire.Cable.Tests.ProtocolTests

open System
open System.Diagnostics
open System.Threading.Tasks
open Xunit
open Campfire.Rails
open Campfire.Cable
open Campfire.Cable.Tests.Support

[<Literal>]
let private Welcome = """{"type":"welcome"}"""

let private str (s: string) = Value.String s
let private int' (n: int) = Value.Int(int64 n)

let private room (id: int) : string =
    identifier (object' [ "channel", str "RoomChannel"; "room_id", int' id ])

let private heartbeatId = identifier (object' [ "channel", str "HeartbeatChannel" ])

let private confirm (identifier: string) : string =
    $"""{{"identifier":{Json.generate (str identifier)},"type":"confirm_subscription"}}"""

let private reject (identifier: string) : string =
    $"""{{"identifier":{Json.generate (str identifier)},"type":"reject_subscription"}}"""

let private message (identifier: string) (message: string) : string =
    $"""{{"identifier":{Json.generate (str identifier)},"message":{message}}}"""

let private withServerAs (assumeSsl: bool) (options: CableOptions) (body: TestServer -> Task) : Task =
    task {
        let! app = startWith options assumeSsl
        try
            do! (body app).WaitAsync(TimeSpan.FromSeconds 30.0)
        finally
            (app :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

let private withServer (options: CableOptions) (body: TestServer -> Task) : Task = withServerAs false options body

let private eq (expected: 'a) (actual: 'a) = Assert.Equal<'a>(expected, actual)

/// The next text frame is `expected`.
let private expectText (expected: string) (client: Client) : Task =
    task {
        let! text = client.NextText()
        eq expected text
    }

let private expectGot (expected: Got) (client: Client) : Task =
    task {
        let! got = client.Next()
        eq expected got
    }

/// The close frame arrives within `deadline`, whatever else (pings, a late confirmation) comes first; the wait ends the moment it is read,
/// so the test has no fixed wait, and a loaded host only makes it later, not wrong.
let private expectCloseWithin (deadline: TimeSpan) (expected: (uint16 * string) option) (client: Client) : Task =
    task {
        let stop = Diagnostics.Stopwatch.StartNew()
        let mutable closing = ValueNone
        while closing.IsNone do
            let remaining = deadline - stop.Elapsed
            if remaining <= TimeSpan.Zero then failwith $"no close frame within {deadline.TotalSeconds:F0} s"
            match! client.Next().WaitAsync remaining with
            | Closed _ as got -> closing <- ValueSome got
            | Ended -> failwith "the connection ended without a close frame"
            | _ -> ()
        eq (Closed expected) closing.Value
    }

let private logOf (app: TestServer) : string list = lock app.Log (fun () -> List.ofSeq app.Log)

[<Fact>]
let ``non-websocket requests get Rails 404`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! response = httpGet app.Authority []
            eq 404 response.Status
            eq (Some "text/plain; charset=utf-8") (response.Header "content-type")
            eq "Page not found" response.Body
        })

[<Fact>]
let ``cross-origin upgrades get Rails 404`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! result = Client.Open(app.Authority, [ "Origin", "http://evil.example"; "Cookie", "session_token=1" ])
            match result with
            | Choice2Of2 response -> eq 404 response.Status
            | Choice1Of2 _ -> failwith "expected an HTTP 404, got an upgrade"
        })

[<Fact>]
let ``same-origin under assume_ssl means https`` () =
    withServerAs true (testOptions ()) (fun app ->
        task {
            let! result = Client.Open(app.Authority, [ "Origin", app.Origin ])
            Assert.True((match result with Choice2Of2 _ -> true | _ -> false), "http:// origin must be refused when SSL is assumed")
            let httpsOrigin = app.Origin.Replace("http://", "https://")
            let! client = connectWith app (Some 1UL) httpsOrigin
            do! expectText Welcome client
        })

[<Fact>]
let ``negotiates the actioncable subprotocol and welcomes`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            eq (Some "actioncable-v1-json") client.Protocol
            do! expectText Welcome client
        })

[<Fact>]
let ``unauthorized connections are told not to reconnect and closed`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connectWith app None app.Origin
            do! expectText """{"type":"disconnect","reason":"unauthorized","reconnect":false}""" client
            do! expectGot (Closed(Some(1000us, ""))) client
        })

[<Fact>]
let ``subscribe confirms and duplicates are ignored`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            do! expectText Welcome client

            let room = room 1
            do! client.Subscribe room
            do! expectText (confirm room) client

            // Same identifier string: no reply at all.
            do! client.Subscribe room
            do! client.Subscribe heartbeatId
            do! expectText (confirm heartbeatId) client

            // A differently spelled identifier for the same room is a separate subscription.
            let respelled = """{"room_id":1,"channel":"RoomChannel"}"""
            do! client.Subscribe respelled
            do! expectText (confirm respelled) client
        })

[<Fact>]
let ``rejection runs unsubscribed and can be retried`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()

            let room = room 2
            do! client.Subscribe room
            do! expectText (reject room) client
            eq [ "subscribed rejected=true"; "unsubscribed rejected=true" ] (logOf app)

            // The rejected subscription was removed, so subscribing again is answered again.
            do! client.Subscribe room
            do! expectText (reject room) client
        })

[<Fact>]
let ``unknown channels and malformed commands get no reply`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()

            do! client.Subscribe(identifier (object' [ "channel", str "NopeChannel" ]))
            do! client.Subscribe "not json"
            do! client.SendText "not json"
            do! client.Send(object' [ "command", str "dance" ])
            do! client.Unsubscribe(room 1)
            do! client.Perform(room 1, object' [ "action", str "echo" ])
            do! client.AssertSilent()

            // The connection is still usable afterwards.
            let room = room 1
            do! client.Subscribe room
            do! expectText (confirm room) client
        })

[<Fact>]
let ``leading colons resolve like safe_constantize`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            let heartbeat = identifier (object' [ "channel", str "::HeartbeatChannel" ])
            do! client.Subscribe heartbeat
            do! expectText (confirm heartbeat) client
        })

/// A channel's broadcastings are named after its class, not after how the client spelled it.
[<Fact>]
let ``leading colons stream and broadcast under the class name`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! prefixed = connect app 1UL
            let! plain = connect app 1UL
            let! _ = prefixed.NextText()
            let! _ = plain.NextText()
            let prefixedRoom = identifier (object' [ "channel", str "::RoomChannel"; "room_id", int' 1 ])
            do! prefixed.Subscribe prefixedRoom
            do! expectText (confirm prefixedRoom) prefixed
            let plainRoom = room 1
            do! plain.Subscribe plainRoom
            do! expectText (confirm plainRoom) plain

            eq 2 (app.Server.BroadcastTo("RoomChannel", [ "room-1" ], Payload.ofValue {| roomId = 1 |}))
            do! expectText (message prefixedRoom """{"roomId":1}""") prefixed
            do! expectText (message plainRoom """{"roomId":1}""") plain

            do! prefixed.Perform(prefixedRoom, object' [ "action", str "start" ])
            let start = """{"action":"start","user":{"id":1}}"""
            do! expectText (message plainRoom start) plain
            do! expectText (message prefixedRoom start) prefixed
        })

[<Fact>]
let ``broadcasts reach subscribers as JSON`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            let room = room 1
            do! client.Subscribe room
            let! _ = client.NextText()

            eq 1 (app.Server.BroadcastTo("RoomChannel", [ "room-1" ], Payload.ofValue {| roomId = 1 |}))
            do! expectText (message room """{"roomId":1}""") client

            // Relaxed escaping (S6): `<`, `>` and `&` go out as they are; the frame decodes to what Rails sends.
            app.Server.Broadcast("room:room-1", Payload.ofString "<b>&</b>") |> ignore
            do! expectText (message room "\"<b>&</b>\"") client
        })

[<Fact>]
let ``perform dispatches actions and defaults to receive`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! other = connect app 2UL
            let! _ = client.NextText()
            let! _ = other.NextText()
            let room = room 1
            do! client.Subscribe room
            do! other.Subscribe room
            let! _ = client.NextText()
            let! _ = other.NextText()

            do! client.Perform(room, object' [ "action", str "echo"; "text", str "hi" ])
            do! expectText (message room """{"action":"echo","text":"hi"}""") client

            do! client.Perform(room, object' [ "text", str "hi" ])
            do! expectText (message room """{"received":{"text":"hi"}}""") client

            do! client.Perform(room, object' [ "action", str "start" ])
            let typing = message room """{"action":"start","user":{"id":1}}"""
            do! expectText typing client
            do! expectText typing other

            do! client.Perform(room, object' [ "action", str "not_an_action" ])
            do! client.AssertSilent()
        })

[<Fact>]
let ``unsubscribe stops delivery silently`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            let room = room 1
            do! client.Subscribe room
            let! _ = client.NextText()

            do! client.Unsubscribe room
            do! client.AssertSilent()
            app.Server.Broadcast("room:room-1", Payload.ofValue (obj ())) |> ignore
            do! client.AssertSilent()
            eq 0 app.Server.StreamCount
        })

[<Fact>]
let ``remote disconnect closes every connection for the identifier`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! first = connect app 1UL
            let! second = connect app 1UL
            let! bystander = connect app 2UL
            for client in [ first; second; bystander ] do
                let! _ = client.NextText()
                ()
            let room = room 1
            do! first.Subscribe room
            let! _ = first.NextText()

            eq 2 (app.Server.Disconnect("user-1", true))
            for client in [ first; second ] do
                do! expectText """{"type":"disconnect","reason":"remote","reconnect":true}""" client
                do! expectGot (Closed(Some(1000us, ""))) client
            do! bystander.AssertSilent()

            // Once the client completes the close handshake, the server unsubscribes its channels.
            let! got = first.Next()
            Assert.True((match got with Ended | Failed _ -> true | _ -> false), $"{got}")
            do! Task.Delay 50
            eq "unsubscribed rejected=false" (List.last (logOf app))

            let! again = connect app 1UL
            let! _ = again.NextText()
            app.Server.Disconnect("user-1", false) |> ignore
            do! expectText """{"type":"disconnect","reason":"remote","reconnect":false}""" again
        })

[<Fact>]
let ``restart closes with server_restart`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            app.Server.Restart()
            do! expectText """{"type":"disconnect","reason":"server_restart","reconnect":true}""" client
        })

[<Fact>]
let ``L1 (capacity 2) a subscriber that cannot keep up is cut, not waited for`` () =
    let options = testOptions ()
    options.QueueCapacity <- 2
    withServer options (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            let room = room 1
            do! client.Subscribe room
            let! _ = client.NextText()

            // The broadcaster never waits: it writes faster than the socket drains, the queue fills and the connection is aborted.
            let started = Stopwatch.StartNew()
            for i in 0..9999 do
                app.Server.Broadcast("room:room-1", Payload.ofValue i) |> ignore
            Assert.True(started.Elapsed < TimeSpan.FromSeconds 5.0, $"{started.Elapsed}")
            let delivered = Collections.Generic.List<string>()
            let mutable reading = true
            while reading do
                match! client.Next() with
                | Said frame -> delivered.Add frame
                | _ -> reading <- false
            Assert.True(delivered.Count < 10000)
            let mutable waited = 0
            while (app.Server.ConnectionCount > 0 || app.Server.StreamCount > 0) && waited < 100 do
                do! Task.Delay 50
                waited <- waited + 1
            eq 0 app.Server.ConnectionCount
            eq 0 app.Server.StreamCount
            eq "unsubscribed rejected=false" (List.last (logOf app))
        })

[<Fact>]
let ``pings every three seconds with a unix timestamp`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            do! expectText Welcome client
            let started = Stopwatch.StartNew()
            let! got = client.NextIncludingPings()
            let ping =
                match got with
                | Said ping -> ping
                | other -> failwith $"expected a ping, got {other}"
            Assert.True(started.Elapsed <= TimeSpan.FromMilliseconds 3100.0, $"{started.Elapsed}")
            match Json.parse (Text.Encoding.UTF8.GetBytes ping) with
            | Some(Value.Object entries) ->
                eq [ "type"; "message" ] (entries |> List.map fst)
                eq (str "ping") (entries |> List.find (fun (k, _) -> k = "type") |> snd)
                let now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                match entries |> List.find (fun (k, _) -> k = "message") |> snd with
                | Value.Int sent -> Assert.True(abs (sent - now) <= 1L)
                | other -> failwith $"{other}"
            | _ -> failwith ping
        })

[<Fact>]
let ``turbo streams channel verifies and guards stream names`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()

            let turbo (signed: Value option) =
                identifier (object' ([ "channel", str "Turbo::StreamsChannel" ] @ (signed |> Option.map (fun s -> "signed_stream_name", s) |> Option.toList)))

            let rooms = turbo (Some(str "signed(rooms)"))
            do! client.Subscribe rooms
            do! expectText (confirm rooms) client

            Turbo.broadcastRemoveTo app.Server [ "rooms" ] "list_room_1" |> ignore
            do!
                expectText
                    (message rooms "\"<turbo-stream action=\\\"remove\\\" target=\\\"list_room_1\\\"></turbo-stream>\"")
                    client

            let forged = turbo (Some(str "rooms"))
            do! client.Subscribe forged
            do! expectText (reject forged) client

            let missing = turbo None
            do! client.Subscribe missing
            do! expectText (reject missing) client

            // RoomStreamsAreAuthorized: a validly signed room message stream is still turned away.
            let guarded = turbo (Some(str "signed(Z2lk:messages)"))
            do! client.Subscribe guarded
            do! expectText (reject guarded) client
            Turbo.broadcastAppendTo app.Server [ "Z2lk"; "messages" ] "messages" "<p>x</p>" |> ignore
            do! client.AssertSilent()

            // A non-string name raises in MessageVerifier: neither confirmed nor rejected.
            let numeric = turbo (Some(int' 1))
            do! client.Subscribe numeric
            do! client.AssertSilent()
        })

[<Fact>]
let ``P3 welcome is the first frame, then one ping per beat`` () =
    let options = testOptions ()
    options.BeatInterval <- TimeSpan.FromMilliseconds 50.0
    withServer options (fun app ->
        task {
            let! client = connect app 1UL
            let! first = client.NextIncludingPings()
            eq (Said Welcome) first
            for _ in 1..3 do
                match! client.NextIncludingPings() with
                | Said ping when ping.StartsWith("{\"type\":\"ping\",\"message\":", StringComparison.Ordinal) -> ()
                | other -> failwith $"expected a ping, got {other}"
        })

[<Fact>]
let ``a client close is answered with its code`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            do! expectText Welcome client
            do! client.SendBytes(clientFrame 0x8uy true false [| 0x03uy; 0xe9uy |])
            do! expectGot (Closed(Some(1001us, ""))) client
        })

/// websocket-driver's code for the failure, not a blanket 1002.
[<Fact>]
let ``L5 an invalid UTF-8 text message closes 1007, websocket-driver's code and not a blanket 1002`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            do! expectText Welcome client
            do! client.SendBytes(clientFrame 0x1uy true false [| 0xffuy; 0xfeuy |])
            do! expectCloseWithin (TimeSpan.FromSeconds 30.0) (Some(1007us, "")) client
        })

[<Fact>]
let ``L3 a connection holds a bounded number of subscriptions and identifiers of bounded size`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            do! expectText Welcome client
            for nonce in 0..63 do
                let heartbeat = identifier (object' [ "channel", str "HeartbeatChannel"; "nonce", int' nonce ])
                do! client.Subscribe heartbeat
                do! expectText (confirm heartbeat) client
            // Past the limit, and an oversized identifier: ignored, no reply.
            do! client.Subscribe(identifier (object' [ "channel", str "HeartbeatChannel"; "nonce", int' 64 ]))
            do! client.AssertSilent()
            let! other = connect app 2UL
            do! expectText Welcome other
            do! other.Subscribe(identifier (object' [ "channel", str "HeartbeatChannel"; "pad", str (String('x', 5000)) ]))
            do! other.AssertSilent()
        })

[<Fact>]
let ``L6 a client's ping is answered with a pong`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            do! expectText Welcome client
            do! client.SendBytes(clientFrame 0x9uy true false (Text.Encoding.UTF8.GetBytes "hi"))
            let reply = Array.zeroCreate<byte> 4
            use limit = new Threading.CancellationTokenSource(TimeSpan.FromSeconds 5.0)
            let mutable got = 0
            while got < 4 do
                let! n = client.Stream.ReadAsync(Memory<byte>(reply, got, 4 - got), limit.Token)
                got <- got + n
            eq [| 0x8auy; 0x02uy; byte 'h'; byte 'i' |] reply
        })

[<Fact>]
let ``a stream that stops after the connection closes is released from the hub`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            let room = room 1
            do! client.Subscribe room
            let! _ = client.NextText()
            eq 1 app.Server.StreamCount
            (client :> IDisposable).Dispose()
            let mutable waited = 0
            while app.Server.StreamCount > 0 && waited < 100 do
                do! Task.Delay 50
                waited <- waited + 1
            eq 0 app.Server.StreamCount
            eq "unsubscribed rejected=false" (List.last (logOf app))
        })

/// `ActionCable::Server::Base#allow_request_origin?` beyond the cases the Rust tests cover.
[<Fact>]
let ``origins: the allowed list, the forwarded protocol and disabling the check`` () =
    task {
        let attempt (app: TestServer) (headers: (string * string) list) : Task<bool> =
            task {
                match! Client.Open(app.Authority, ("Cookie", "session_token=1") :: headers) with
                | Choice1Of2 client ->
                    (client :> IDisposable).Dispose()
                    return true
                | Choice2Of2 response ->
                    eq 404 response.Status
                    return false
            }
        do!
            withServer (CableOptions(AllowedRequestOrigins = [| "http://other.example" |])) (fun app ->
                task {
                    let! listed = attempt app [ "Origin", "http://other.example" ]
                    let! unlisted = attempt app [ "Origin", "http://evil.example" ]
                    let! missing = attempt app []
                    Assert.True listed
                    Assert.False unlisted
                    Assert.False missing
                })
        do!
            withServer (testOptions ()) (fun app ->
                task {
                    // The origin is the request's scheme and host: https needs a request that is https.
                    let httpsOrigin = "Origin", app.Origin.Replace("http://", "https://")
                    let! plain = attempt app [ httpsOrigin ]
                    let! sameOrigin = attempt app [ "Origin", app.Origin ]
                    Assert.False plain
                    Assert.True sameOrigin
                })
        do!
            withServer (CableOptions(DisableRequestForgeryProtection = true)) (fun app ->
                task {
                    let! anywhere = attempt app [ "Origin", "http://evil.example" ]
                    let! none = attempt app []
                    Assert.True anywhere
                    Assert.True none
                })
    }

[<Fact>]
let ``a connection's request is what the authenticator sees`` () =
    task {
        // What the request says is read while it is open: Kestrel resets its headers when it ends.
        let seen = Collections.Concurrent.ConcurrentQueue<string * string * string>()
        let authenticate (ctx: Microsoft.AspNetCore.Http.HttpContext) =
            seen.Enqueue((ctx.Request.Path.Value |> nonNull, ctx.Request.Headers["X-Custom"].ToString(), ctx.Request.Headers["Cookie"].ToString()))
            Task.FromResult(Some "someone")
        let server = Server.builder (testOptions ()) authenticate id |> ServerBuilder.build
        let! app, address = host server
        try
            let authority = (Uri address).Authority
            let headers =
                [ "Origin", $"http://{authority}"
                  "Cookie", "a=1"
                  "X-Custom", "yes" ]
            match! Client.Open(authority, headers) with
            | Choice1Of2 client ->
                use _ = client
                let! welcome = client.NextText()
                eq Welcome welcome
            | Choice2Of2 response -> failwith $"{response.Status}"
            // Asked twice, once before the internal channel is subscribed to and once after.
            eq 2 seen.Count
            let uri, custom, cookie = seen.ToArray()[0]
            eq "/cable" uri
            eq "yes" custom
            eq "a=1" cookie
        finally
            app.DisposeAsync().AsTask().Wait()
    }

/// `Turbo::StreamsChannel` with the app's verifier: names signed as turbo-rails signs them.
[<Fact>]
let ``turbo streams channel verifies names signed with the app's secret`` () =
    task {
        let secrets = Secrets.create "a secret key base for the cable tests"
        let signed = Campfire.Rails.Turbo.signedStreamName secrets [ "Z2lk"; "messages" ]
        let rooms = Campfire.Rails.Turbo.signedStreamName secrets [ "rooms" ]
        let server =
            Server.builder (testOptions ()) cookieAuth identify
            |> ServerBuilder.channel Campfire.Cable.Turbo.StreamsChannelName (fun () ->
                Campfire.Cable.Turbo.StreamsChannel.create secrets
                |> Campfire.Cable.Turbo.StreamsChannel.guardedBy (fun name -> name.EndsWith ":messages")
                |> Campfire.Cable.Turbo.StreamsChannel.channel)
            |> ServerBuilder.build
        let! app, address = host server
        try
            let authority = (Uri address).Authority
            match! Client.Open(authority, [ "Origin", $"http://{authority}"; "Cookie", "session_token=1" ]) with
            | Choice2Of2 response -> failwith $"{response.Status}"
            | Choice1Of2 client ->
                use _ = client
                do! expectText Welcome client
                let turbo (name: string) = identifier (object' [ "channel", str Campfire.Cable.Turbo.StreamsChannelName; "signed_stream_name", str name ])
                do! client.Subscribe(turbo rooms)
                do! expectText (confirm (turbo rooms)) client
                // The guard turns away a name that verifies.
                do! client.Subscribe(turbo signed)
                do! expectText (reject (turbo signed)) client
                // A name signed with another secret doesn't verify.
                let other = Campfire.Rails.Turbo.signedStreamName (Secrets.create "another") [ "rooms" ]
                do! client.Subscribe(turbo other)
                do! expectText (reject (turbo other)) client
                eq 1 (Campfire.Cable.Turbo.broadcastUpdateTo server [ "rooms" ] "list" "<b>1</b>")
                let tag = Campfire.Cable.Turbo.actionTag Campfire.Cable.Turbo.Update (Campfire.Cable.Turbo.Target "list") (Some "<b>1</b>") []
                do! expectText (message (turbo rooms) (Text.Encoding.UTF8.GetString(Payload.toBytes (Payload.ofString tag)))) client
        finally
            app.DisposeAsync().AsTask().Wait()
    }

/// Not in Rust: serde_json reads an object in linear time, so a message of the largest size the socket
/// allows (1 MiB) must not hold a thread for long. Json.parse once looked up each key by scanning.
[<Fact>]
let ``a message of many keys is read in linear time`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            do! expectText Welcome client
            let keys (count: int) = String.Join(",", [ for n in 0 .. count - 1 -> $"\"k{n}\":{n}" ])
            let big = $"{{\"command\":\"subscribe\",\"identifier\":{Json.generate (str (room 1))},{keys 60_000}}}"
            Assert.InRange(big.Length, 600_000, 1048576)
            let started = Stopwatch.GetTimestamp()
            do! client.SendText big
            do! expectText (confirm (room 1)) client
            // Perform's `data` is itself JSON in a string.
            let data = "{" + keys 40_000 + ",\"action\":\"start\"}"
            let perform =
                Json.generate (object' [ "command", str "message"; "identifier", str (room 1); "data", str data ])
            Assert.InRange(perform.Length, 400_000, 1048576)
            do! client.SendText perform
            do! expectText (message (room 1) """{"action":"start","user":{"id":1}}""") client
            do! client.Subscribe heartbeatId
            do! expectText (confirm heartbeatId) client
            let elapsed = Stopwatch.GetElapsedTime started
            Assert.True(elapsed < TimeSpan.FromSeconds 5.0, $"{elapsed}")
        })

/// Not in Rust: what the 101 carries. websocket-driver writes `Connection: Upgrade` (the Rust port, which
/// writes the handshake itself, has `connection: upgrade`), and Kestrel adds `Date` (and `Server`, unless
/// the host turns it off).
[<Fact>]
let ``the 101 answers as websocket-driver does, plus the host's Date`` () =
    withServer (testOptions ()) (fun app ->
        task {
            use tcp = new Net.Sockets.TcpClient()
            do! tcp.ConnectAsync(Net.IPAddress.Loopback, int (app.Authority.Substring(app.Authority.LastIndexOf ':' + 1)))
            let stream = tcp.GetStream()
            let key = Convert.ToBase64String(Security.Cryptography.RandomNumberGenerator.GetBytes 16)
            let request =
                $"GET /cable HTTP/1.1\r\nHost: {app.Authority}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\nCookie: session_token=1\r\nOrigin: {app.Origin}\r\n\r\n"
            do! stream.WriteAsync(Text.Encoding.ASCII.GetBytes request)
            let! response = readHttp stream
            eq 101 response.Status
            eq (Some "websocket") (response.Header "Upgrade" |> Option.map (fun v -> v.ToLowerInvariant()))
            eq (Some "Upgrade") (response.Header "Connection")
            Assert.True((response.Header "Date").IsSome, "Kestrel adds a Date")
        })

/// `dispatch_websocket_message` only runs `if websocket.alive?`: once the server has begun closing, a command the client still
/// sends (it has not read the disconnect yet) runs no callback and registers no stream.
[<Fact>]
let ``commands sent after the server began closing are not processed`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            let first = room 1
            do! client.Subscribe first
            do! expectText (confirm first) client
            eq 1 (app.Server.Disconnect("user-1", true))
            let late = identifier (object' [ "channel", str "RoomChannel"; "room_id", int' 1; "late", Value.Bool true ])
            do! client.Subscribe late
            do! expectText """{"type":"disconnect","reason":"remote","reconnect":true}""" client
            do! expectGot (Closed(Some(1000us, ""))) client
            do! Task.Delay 100
            eq 1 (logOf app |> List.filter (fun line -> line.StartsWith("subscribed", StringComparison.Ordinal)) |> List.length)
        })
