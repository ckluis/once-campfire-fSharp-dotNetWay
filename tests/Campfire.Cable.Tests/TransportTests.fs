// Phase 3 (plans/design-cable.md section 5, unit 3.4): the transport on ASP.NET Core's WebSocket middleware, driven by
// `ClientWebSocket` over Kestrel on loopback: P1 (handshake), P8 (order), P11 (close), P12 (host stop).
module Campfire.Cable.Tests.TransportTests

open System
open System.Collections.Generic
open System.Diagnostics
open System.Net.WebSockets
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Xunit
open Campfire.RailsCompat
open Campfire.Cable
open Campfire.Cable.Tests.Support

let private seconds (n: float) = TimeSpan.FromSeconds n

/// A `ClientWebSocket` to the server's /cable, with the headers a browser sends.
let private open' (app: TestServer) (protocols: string list) (userId: uint64 option) (configure: ClientWebSocketOptions -> unit) : Task<ClientWebSocket> =
    task {
        let ws = new ClientWebSocket()
        ws.Options.CollectHttpResponseDetails <- true
        ws.Options.SetRequestHeader("Origin", app.Origin)
        match userId with
        | Some id -> ws.Options.SetRequestHeader("Cookie", $"session_token={id}")
        | None -> ()
        for protocol in protocols do
            ws.Options.AddSubProtocol protocol
        configure ws.Options
        use limit = new CancellationTokenSource(seconds 10.0)
        do! ws.ConnectAsync(Uri app.Url, limit.Token)
        return ws
    }

/// The next message: its text, or `Close` with its status.
let private next (ws: ClientWebSocket) : Task<Choice<string, WebSocketCloseStatus * string>> =
    task {
        use limit = new CancellationTokenSource(seconds 10.0)
        let buffer = Array.zeroCreate<byte> 65536
        use message = new IO.MemoryStream()
        let mutable fin = false
        let mutable closed = ValueNone
        while not fin do
            let! r = ws.ReceiveAsync(Memory<byte>(buffer), limit.Token)
            if r.MessageType = WebSocketMessageType.Close then
                closed <- ValueSome(ws.CloseStatus.Value, ws.CloseStatusDescription |> Option.ofObj |> Option.defaultValue "")
                fin <- true
            else
                message.Write(buffer, 0, r.Count)
                fin <- r.EndOfMessage
        match closed with
        | ValueSome status -> return Choice2Of2 status
        | ValueNone -> return Choice1Of2(Encoding.UTF8.GetString(message.ToArray()))
    }

let private text (ws: ClientWebSocket) : Task<string> =
    task {
        match! next ws with
        | Choice1Of2 text -> return text
        | Choice2Of2 status -> return failwith $"expected text, got close {status}"
    }

let private send (ws: ClientWebSocket) (json: string) : Task =
    ws.SendAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes json), WebSocketMessageType.Text, true, CancellationToken.None).AsTask()

let private withApp (options: CableOptions) (body: TestServer -> Task) : Task =
    task {
        let! app = start options
        try
            do! (body app).WaitAsync(seconds 30.0)
        finally
            (app :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

[<Fact>]
let ``P1 the subprotocol is the first of the client's list that Action Cable speaks, and no extension is negotiated`` () =
    withApp (testOptions ()) (fun app ->
        task {
            use! both = open' app [ "actioncable-v1-json"; "actioncable-unsupported" ] (Some 1UL) ignore
            Assert.Equal(Some "actioncable-v1-json", Option.ofObj both.SubProtocol)
            use! reversed = open' app [ "actioncable-unsupported"; "actioncable-v1-json" ] (Some 1UL) ignore
            Assert.Equal(Some "actioncable-unsupported", Option.ofObj reversed.SubProtocol)
            use! only = open' app [ "actioncable-unsupported" ] (Some 1UL) ignore
            Assert.Equal(Some "actioncable-unsupported", Option.ofObj only.SubProtocol)
            use! none = open' app [] (Some 1UL) ignore
            Assert.True(String.IsNullOrEmpty(string none.SubProtocol))
            // A client offering permessage-deflate gets no Sec-WebSocket-Extensions (C3).
            use! deflate = open' app [ "actioncable-v1-json" ] (Some 1UL) (fun o -> o.DangerousDeflateOptions <- WebSocketDeflateOptions())
            Assert.False((nonNull deflate.HttpResponseHeaders).ContainsKey "Sec-WebSocket-Extensions")
            let! welcome = text deflate
            Assert.Equal("""{"type":"welcome"}""", welcome)
        })

[<Fact>]
let ``P8 a subscribe transmits, then confirms, then the stream's broadcasts arrive in order with no gap`` () =
    task {
        let total = 1000
        let server =
            Server.builder (testOptions ()) cookieAuth identify
            |> ServerBuilder.channel "OrderedChannel" (fun () ->
                { Channel.empty with
                    Subscribed =
                        fun sub ->
                            sub.StreamFrom "ordered"
                            sub.Transmit(Payload.ofString "transmitted")
                            Task.CompletedTask })
            |> ServerBuilder.build
        let! host', address = host server
        try
            let authority = (Uri address).Authority
            use ws = new ClientWebSocket()
            ws.Options.SetRequestHeader("Origin", $"http://{authority}")
            ws.Options.SetRequestHeader("Cookie", "session_token=1")
            ws.Options.AddSubProtocol "actioncable-v1-json"
            do! ws.ConnectAsync(Uri $"ws://{authority}/cable", CancellationToken.None)
            let! welcome = text ws
            Assert.Equal("""{"type":"welcome"}""", welcome)
            // Another thread broadcasts numbered messages throughout the subscribe.
            let identifier = Json.generate (Value.Object [ "channel", Value.String "OrderedChannel" ])
            let stop = ref false
            let broadcaster =
                Task.Run(fun () ->
                    let mutable n = 0
                    while n < total do
                        server.Broadcast("ordered", Payload.ofValue n) |> ignore
                        n <- n + 1
                        if n % 50 = 0 then Thread.Sleep 1)
            do! Task.Delay 5
            do! send ws (Json.generate (Value.Object [ "command", Value.String "subscribe"; "identifier", Value.String identifier ]))
            do! broadcaster
            ignore stop
            let frames = List<string>()
            let mutable confirmed = false
            while not confirmed do
                let! frame = text ws
                if frame.Contains "confirm_subscription" then confirmed <- true else frames.Add frame
            Assert.Equal(1, frames.Count)
            Assert.Contains("\"message\":\"transmitted\"", frames[0])
            // After the confirmation: increasing numbers, no gaps from the first seen to the last broadcast.
            let seen = List<int>()
            use limit = new CancellationTokenSource(seconds 5.0)
            while (seen.Count = 0 || seen[seen.Count - 1] < total - 1) && not limit.IsCancellationRequested do
                let! frame = text ws
                use document = Text.Json.JsonDocument.Parse frame
                seen.Add(document.RootElement.GetProperty("message").GetInt32())
            Assert.NotEmpty seen
            for i in 1 .. seen.Count - 1 do
                Assert.Equal(seen[i - 1] + 1, seen[i])
        finally
            host'.StopAsync().Wait()
            host'.DisposeAsync().AsTask().Wait()
    }

[<Fact>]
let ``P11 a client that closes is answered and the connection leaves the registries`` () =
    withApp (testOptions ()) (fun app ->
        task {
            use! ws = open' app [ "actioncable-v1-json" ] (Some 1UL) ignore
            let! _ = text ws
            do! ws.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "", CancellationToken.None)
            Assert.Equal(Nullable WebSocketCloseStatus.EndpointUnavailable, ws.CloseStatus)
            let mutable waited = 0
            while app.Server.ConnectionCount > 0 && waited < 40 do
                do! Task.Delay 50
                waited <- waited + 1
            Assert.Equal(0, app.Server.ConnectionCount)
        })

/// An empty text message ends at the zero-byte read that announces it: the read loop must not take the next frame (here the
/// client's close) as its body, or the close goes unanswered with the client's code.
[<Fact>]
let ``an empty message is a message of its own, and a close after it is still echoed`` () =
    withApp (testOptions ()) (fun app ->
        task {
            use! ws = open' app [ "actioncable-v1-json" ] (Some 1UL) ignore
            let! _ = text ws
            do! ws.SendAsync(ReadOnlyMemory<byte>.Empty, WebSocketMessageType.Text, true, CancellationToken.None).AsTask()
            do! ws.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, "", CancellationToken.None)
            Assert.Equal(Nullable WebSocketCloseStatus.EndpointUnavailable, ws.CloseStatus)
        })

[<Fact>]
let ``P11 a client that never answers the server's close is cut within CloseTimeout`` () =
    let options = testOptions ()
    options.CloseTimeout <- TimeSpan.FromMilliseconds 300.0
    withApp options (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            let started = Stopwatch.StartNew()
            Assert.Equal(1, app.Server.Disconnect("user-1", true))
            // The client does not read: it never completes the closing handshake.
            let mutable waited = 0
            while app.Server.ConnectionCount > 0 && waited < 60 do
                do! Task.Delay 50
                waited <- waited + 1
            Assert.Equal(0, app.Server.ConnectionCount)
            Assert.True(started.Elapsed < seconds 3.0, $"{started.Elapsed}")
            (client :> IDisposable).Dispose()
        })

[<Fact>]
let ``P12 stopping the host closes every connection with server_restart`` () =
    task {
        let! app = start (testOptions ())
        try
            let! sockets = Task.WhenAll [| for id in [ 1UL; 2UL; 1UL ] -> open' app [ "actioncable-v1-json" ] (Some id) ignore |]
            for ws in sockets do
                let! welcome = text ws
                Assert.Equal("""{"type":"welcome"}""", welcome)
            let started = Stopwatch.StartNew()
            let stopping = app.Host.StopAsync()
            for ws in sockets do
                let! frame = text ws
                Assert.Equal("""{"type":"disconnect","reason":"server_restart","reconnect":true}""", frame)
                match! next ws with
                | Choice2Of2(status, description) ->
                    Assert.Equal(WebSocketCloseStatus.NormalClosure, status)
                    Assert.Equal("", description)
                | Choice1Of2 other -> failwith other
                do! ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
            do! stopping
            Assert.True(started.Elapsed < seconds 2.0, $"{started.Elapsed}")
            for ws in sockets do
                ws.Dispose()
        finally
            (app :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

[<Fact>]
let ``P12 a connection accepted after the host began stopping is sent server_restart right after its welcome`` () =
    withApp (testOptions ()) (fun app ->
        task {
            // `ApplicationStopping` has run, but Kestrel has not stopped yet: an upgrade accepted now must not wait out the shutdown.
            app.Server.Restart()
            use! ws = open' app [ "actioncable-v1-json" ] (Some 1UL) ignore
            let! welcome = text ws
            Assert.Equal("""{"type":"welcome"}""", welcome)
            let! restart = text ws
            Assert.Equal("""{"type":"disconnect","reason":"server_restart","reconnect":true}""", restart)
            match! next ws with
            | Choice2Of2(status, description) ->
                Assert.Equal(WebSocketCloseStatus.NormalClosure, status)
                Assert.Equal("", description)
            | Choice1Of2 other -> failwith other
            do! ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        })

/// C10: the connection looks its session up again once it is registered (a remote disconnect would now reach it), in a scope of its
/// own. A3's hook runs between the registration and that look.
[<Fact>]
let ``A3 the re-check hears a session that went between the first lookup and the registration`` () =
    task {
        let gone = ref false
        let rechecked = List<IServiceProvider>()
        let server =
            Server.builder (testOptions ()) cookieAuth identify
            |> ServerBuilder.withRecheck (fun _ services ->
                rechecked.Add services
                Task.FromResult(not gone.Value))
            |> ServerBuilder.build
        server.AfterRegistered <-
            fun () ->
                gone.Value <- true
                Task.CompletedTask
        let! host', address = host server
        try
            let authority = (Uri address).Authority
            use ws = new ClientWebSocket()
            ws.Options.SetRequestHeader("Origin", $"http://{authority}")
            ws.Options.SetRequestHeader("Cookie", "session_token=1")
            ws.Options.AddSubProtocol "actioncable-v1-json"
            do! ws.ConnectAsync(Uri $"ws://{authority}/cable", CancellationToken.None)
            let! welcome = text ws
            Assert.Equal("""{"type":"welcome"}""", welcome)
            let! disconnect = text ws
            Assert.Equal("""{"type":"disconnect","reason":"unauthorized","reconnect":false}""", disconnect)
            match! next ws with
            | Choice2Of2(status, _) -> Assert.Equal(WebSocketCloseStatus.NormalClosure, status)
            | Choice1Of2 other -> failwith other
            do! ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
            Assert.Equal(1, rechecked.Count)
        finally
            host'.StopAsync().Wait()
            host'.DisposeAsync().AsTask().Wait()
    }

type private Marker() =
    member val Disposed = false with get, set

    interface IDisposable with
        member this.Dispose() = this.Disposed <- true

[<Fact>]
let ``A4 every command, and every unsubscribed on close, gets a scope of its own, disposed when it ends`` () =
    task {
        let provider = ServiceCollection().AddScoped<Marker>().BuildServiceProvider()
        let markers = List<Marker>()
        let note (sub: Subscription<User>) =
            lock markers (fun () ->
                markers.Add(sub.Services.GetRequiredService<Marker>()))
        let server =
            Server.builder (testOptions ()) cookieAuth identify
            |> ServerBuilder.withServices provider
            |> ServerBuilder.channel "ScopedChannel" (fun () ->
                { Channel.empty with
                    Subscribed =
                        fun sub ->
                            note sub
                            Task.CompletedTask
                    Unsubscribed =
                        fun sub ->
                            note sub
                            Task.CompletedTask
                    Perform =
                        fun _ _ sub ->
                            note sub
                            sub.Transmit(Payload.ofString "noted")
                            Task.FromResult true })
            |> ServerBuilder.build
        let! host', address = host server
        try
            let authority = (Uri address).Authority
            use ws = new ClientWebSocket()
            ws.Options.SetRequestHeader("Origin", $"http://{authority}")
            ws.Options.SetRequestHeader("Cookie", "session_token=1")
            ws.Options.AddSubProtocol "actioncable-v1-json"
            do! ws.ConnectAsync(Uri $"ws://{authority}/cable", CancellationToken.None)
            let! _ = text ws
            let identifier = Json.generate (Value.Object [ "channel", Value.String "ScopedChannel" ])
            do! send ws (Json.generate (Value.Object [ "command", Value.String "subscribe"; "identifier", Value.String identifier ]))
            let! confirmed = text ws
            Assert.Contains("confirm_subscription", confirmed)
            let perform =
                Json.generate (
                    Value.Object
                        [ "command", Value.String "message"
                          "identifier", Value.String identifier
                          "data", Value.String(Json.generate (Value.Object [ "action", Value.String "mark" ])) ]
                )
            for _ in 1..2 do
                do! send ws perform
                let! noted = text ws
                Assert.Contains("noted", noted)
            // A command's scope is gone when the command ends.
            Assert.Equal(3, markers.Count)
            Assert.All(markers, fun marker -> Assert.True marker.Disposed)
            do! ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
            let mutable waited = 0
            while markers.Count < 4 && waited < 100 do
                do! Task.Delay 20
                waited <- waited + 1
            Assert.Equal(4, markers.Count)
            Assert.Equal(4, (markers |> Seq.distinct |> Seq.length))
            Assert.All(markers, fun marker -> Assert.True marker.Disposed)
        finally
            host'.StopAsync().Wait()
            host'.DisposeAsync().AsTask().Wait()
    }
