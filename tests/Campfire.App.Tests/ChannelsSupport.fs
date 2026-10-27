// Port of rust/crates/campfire/src/channels/tests/support.rs
//
// A cable server with Campfire's channels over a fixtures database, and a WebSocket client. The client is
// `ClientWebSocket` (Rust's is tokio-tungstenite), with a read that stays pending across calls: cancelling a
// receive aborts a .NET socket, so a wait that times out must leave the receive running for the next call.
module Campfire.App.Tests.ChannelsSupport

open System
open System.IO
open System.Net.WebSockets
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Campfire.App
open Campfire.App.Channels
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Microsoft.Extensions.Time.Testing

[<Literal>]
let SecretKeyBase = "channels-test-secret-key-base"

/// Routes `Event.DisconnectUser` to the cable server, as app core's sink does.
type CableSink() =
    member val Server: Cable | null = null with get, set

    interface EventSink with
        member this.Emit(event: Event) : unit =
            match this.Server with
            | null -> ()
            | server -> Revocation.handleEvent server event |> ignore

/// Stand-in partials that name what they render, so frames show which partial and record.
type FakePartials() =
    interface IPartials with
        member _.Message(message: Message) =
            System.ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes $"""<div id="message_{message.ClientMessageId}">message {message.Id}</div>""")
        member _.MessagePresentation(message: Message) = $"<div>presentation {message.Id} & more</div>"
        member _.Boost(boost: Boost) = System.ReadOnlyMemory<byte>(System.Text.Encoding.UTF8.GetBytes $"<div>boost {boost.Id}</div>")
        member _.SharedRoom(room: Room) = $"<li>shared {room.Id}</li>"
        member _.DirectRoom(membership: Membership) = $"<li>direct {membership.Id}</li>"

let fakePartials: IPartials = FakePartials()

/// A fixture's id, by label.
let id (label: string) : int64 = Fixtures.identify label

let identifier (value: Value) : string = Json.generate value

let roomIdentifier (channel: string) (roomId: int64) : string =
    identifier (Value.Object [ "channel", Value.String channel; "room_id", Value.Int roomId ])

let confirmation (identifier: string) : string =
    Json.generate (Value.Object [ "identifier", Value.String identifier; "type", Value.String "confirm_subscription" ])

let rejection (identifier: string) : string =
    Json.generate (Value.Object [ "identifier", Value.String identifier; "type", Value.String "reject_subscription" ])

/// The frame a broadcast of `message` (already ActiveSupport-JSON-encoded) arrives in.
let delivery (identifier: string) (encodedMessage: string) : string =
    $"""{{"identifier":{Json.encode (Value.String identifier)},"message":{encodedMessage}}}"""

/// A JSON string literal the way ActiveSupport encodes HTML in it: quotes and backslashes escaped, and `<`, `>`, `&`
/// as `<`, `>`, `&`.
let htmlJson (html: string) : string =
    let escaped =
        html.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "\\u003c").Replace(">", "\\u003e").Replace("&", "\\u0026")
    $"\"{escaped}\""

type WsFrame =
    | WsText of string
    /// The server's close frame: its status and reason, if it carried any.
    | WsClose of (int * string) option
    | WsEnd

type Client(socket: ClientWebSocket) =
    let mutable pending: Task<WsFrame> | null = null

    /// The next whole message, skipping pings.
    let rec receive () : Task<WsFrame> =
        task {
            let buffer = Array.zeroCreate<byte> 8192
            use message = new MemoryStream()
            let mutable result: WsFrame option = None
            try
                let mutable more = true
                while more do
                    let! got = socket.ReceiveAsync(ArraySegment<byte>(buffer), CancellationToken.None)
                    match got.MessageType with
                    | WebSocketMessageType.Close ->
                        // Sends the client's half of the close handshake, so the server finishes closing (unsubscribing
                        // everything) without waiting out its close timeout.
                        try
                            do! socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
                        with _ ->
                            ()
                        result <-
                            Some(
                                WsClose(
                                    got.CloseStatus
                                    |> Option.ofNullable
                                    |> Option.map (fun status -> int status, defaultArg (Option.ofObj got.CloseStatusDescription) "")
                                )
                            )
                        more <- false
                    | _ ->
                        message.Write(buffer, 0, got.Count)
                        if got.EndOfMessage then more <- false
                if result.IsNone then
                    result <- Some(WsText(Encoding.UTF8.GetString(message.ToArray())))
            with _ ->
                result <- Some WsEnd
            match result with
            | Some(WsText text) when text.StartsWith("{\"type\":\"ping\"", StringComparison.Ordinal) -> return! receive ()
            | Some frame -> return frame
            | None -> return WsEnd
        }

    member _.Socket = socket

    member _.Send(command: Value) : Task =
        let bytes = Encoding.UTF8.GetBytes(Json.generate command)
        socket.SendAsync(ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)

    member this.Subscribe(identifier: string) : Task =
        this.Send(Value.Object [ "command", Value.String "subscribe"; "identifier", Value.String identifier ])

    member this.Unsubscribe(identifier: string) : Task =
        this.Send(Value.Object [ "command", Value.String "unsubscribe"; "identifier", Value.String identifier ])

    member this.Perform(identifier: string, data: Value) : Task =
        this.Send(
            Value.Object [ "command", Value.String "message"; "identifier", Value.String identifier; "data", Value.String(Json.generate data) ]
        )

    /// The next frame, within `wait`; `None` if nothing arrives (the receive carries on for the next call).
    member _.TryNext(wait: TimeSpan) : Task<WsFrame option> =
        task {
            let current =
                match pending with
                | null ->
                    let started = receive ()
                    pending <- started
                    started
                | started -> started
            let! first = Task.WhenAny(current, Task.Delay wait)
            if obj.ReferenceEquals(first, current) then
                pending <- null
                let! frame = current
                return Some frame
            else
                return None
        }

    /// The next frame, skipping pings (which don't extend the 5s it has to arrive in, so a missing frame fails the
    /// test rather than waiting through heartbeats forever).
    member this.Next() : Task<WsFrame> =
        task {
            match! this.TryNext(TimeSpan.FromSeconds 5.0) with
            | Some frame -> return frame
            | None -> return failwith "a frame within 5s"
        }

    member this.NextText() : Task<string> =
        task {
            match! this.Next() with
            | WsText text -> return text
            | other -> return failwith $"expected a text frame, got {other}"
        }

    /// Subscribes and returns the confirm or reject frame.
    member this.SubscribeReply(identifier: string) : Task<string> =
        task {
            do! this.Subscribe identifier
            return! this.NextText()
        }

    member this.Confirm(identifier: string) : Task =
        task {
            let! reply = this.SubscribeReply identifier
            Assert.True((reply = confirmation identifier), $"subscribing to {identifier}: {reply}")
        }

    member this.Reject(identifier: string) : Task =
        task {
            let! reply = this.SubscribeReply identifier
            Assert.True((reply = rejection identifier), $"subscribing to {identifier}: {reply}")
        }

    /// Asserts nothing arrives (pings aside) for a moment.
    member this.AssertSilent() : Task =
        task {
            match! this.TryNext(TimeSpan.FromMilliseconds 250.0) with
            | None -> ()
            | Some frame -> failwith $"expected no frame, got {frame}"
        }

    /// Reads until the server closes the socket, returning the text frames before the close.
    member this.UntilClosed() : Task<string list> =
        task {
            let frames = ResizeArray<string>()
            let mutable go = true
            while go do
                match! this.Next() with
                | WsText text -> frames.Add text
                | WsClose _ -> ()
                | WsEnd -> go <- false
            return List.ofSeq frames
        }

    interface IDisposable with
        member _.Dispose() = socket.Dispose()

/// Waits until `check` holds, for state the server changes after a frame is sent (unsubscribe has no reply to wait
/// for).
let eventually (check: unit -> Task<bool>) : Task =
    task {
        let mutable held = false
        let mutable tries = 0
        while not held && tries < 100 do
            let! ok = check ()
            if ok then
                held <- true
            else
                tries <- tries + 1
                do! Task.Delay 20
        if not held then failwith "condition never held"
    }

type TestApp
    (
        gate: WriteGate,
        server: Cable,
        broadcasts: Broadcasts,
        secrets: Secrets,
        clock: FakeTimeProvider,
        host: WebApplication,
        url: string,
        origin: string,
        dir: string,
        factory: Microsoft.EntityFrameworkCore.IDbContextFactory<Campfire.Db.Ef.CampfireDb>,
        efEnv: Campfire.Db.Ef.Env
    ) =
    member _.Gate = gate
    member _.Dir = dir

    /// A statement a model does not make (a rename, a stale value), run on a connection of its own while holding the write gate.
    member _.Execute(sql: string, [<ParamArray>] args: (obj | null)[]) : Task<unit> =
        task {
            do! gate.Enter()
            try
                let path = Path.Combine(dir, "test.sqlite3")
                use connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False")
                connection.Open()
                Campfire.App.Tests.Support.RawSql.run connection sql (List.ofArray args)
            finally
                gate.Exit()
        }

    /// `Writes.run` over the same file, the same gate and the same sink: the models' writes.
    member _.WriteEf(f: Campfire.Db.Ef.Tx -> 'T) : Task<Result<'T, DbError>> =
        task {
            use context = factory.CreateDbContext()
            return! Campfire.Db.Ef.Writes.run gate efEnv context f
        }

    /// `Reads.run` over the same file, on a leased context: the EF models' reads.
    member _.ReadEf(f: Campfire.Db.Ef.CampfireDb -> 'T) : 'T =
        use context = factory.CreateDbContext()
        match Campfire.Db.Ef.Reads.run context f with
        | Ok value -> value
        | Error error -> failwith (DbError.display error)

    member _.Server = server
    member _.Broadcasts = broadcasts
    member _.Secrets = secrets
    member _.Time = clock
    member _.Url = url
    member _.Origin = origin

    /// `Now` as the UTC `DateTime` the EF entities carry.
    member _.Utc() : System.DateTime = Campfire.Db.RailsTime.truncate (clock.GetUtcNow().UtcDateTime)

    member _.NowOffset() : Campfire.RailsCompat.Timestamp = clock.GetUtcNow()

    /// A session cookie for the fixture user (`cookies.signed[:session_token]`).
    member this.CookieFor(user: string) : Task<string> =
        task {
            let userId = id user
            match! this.WriteEf(fun tx -> Session.start tx userId (Some "test") (Some "8.8.8.8")) with
            | Ok session -> return this.CookieWithToken session.Token
            | Error error -> return failwith (DbError.display error)
        }

    member this.CookieWithToken(token: string) : string = this.CookieWithTokenExpiring(token, None)

    member _.CookieWithTokenExpiring(token: string, expiresAt: Campfire.RailsCompat.Timestamp option) : string =
        let signed = Cookies.sign secrets "session_token" token expiresAt
        "session_token=" + signed.Replace("+", "%2B").Replace("/", "%2F").Replace("=", "%3D")

    /// Connects as the fixture user and reads the welcome.
    member this.Connect(user: string) : Task<Client> =
        task {
            let! cookie = this.CookieFor user
            let! client = this.ConnectWithCookie(Some cookie)
            let! welcome = client.NextText()
            Assert.Equal("""{"type":"welcome"}""", welcome)
            return client
        }

    member _.ConnectWithCookie(cookie: string option) : Task<Client> =
        task {
            let socket = new ClientWebSocket()
            socket.Options.SetRequestHeader("Origin", origin)
            socket.Options.AddSubProtocol "actioncable-v1-json"
            socket.Options.AddSubProtocol "actioncable-unsupported"
            match cookie with
            | Some cookie -> socket.Options.SetRequestHeader("Cookie", cookie)
            | None -> ()
            do! socket.ConnectAsync(Uri url, CancellationToken.None)
            return new Client(socket)
        }

    member _.SignedStreamName(streamables: string list) : string = Turbo.signedStreamName secrets streamables

    member this.Room(label: string) : Task<Room> =
        task {
            let roomId = id label
            return this.ReadEf(fun db -> Room.find db roomId)
        }

    member this.Membership(room: string, user: string) : Task<Membership option> =
        task {
            let roomId, userId = id room, id user
            return this.ReadEf(fun db -> Membership.findByRoomAndUser db roomId userId)
        }

    member this.Message(label: string) : Task<Message> =
        task {
            let messageId = id label
            return this.ReadEf(fun db -> Message.find db messageId)
        }

    member this.Boost(label: string) : Task<Boost> =
        task {
            let boostId = id label
            return this.ReadEf(fun db -> Boost.find db boostId)
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            ValueTask(
                task {
                    try
                        do! host.StopAsync((new CancellationTokenSource(TimeSpan.FromSeconds 2.0)).Token)
                    with _ ->
                        ()
                    do! host.DisposeAsync()
                    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
                    try
                        Directory.Delete(dir, true)
                    with _ ->
                        ()
                }
            )

/// Kestrel on a loopback port with the app's cable services (`CableServices.add`, the session scheme, the data services) and
/// the cable mounted at /cable, as the app's own host has them: the host, its `host:port` and the server it built.
let serveCable
    (secrets: Secrets)
    (time: TimeProvider)
    (factory: Microsoft.EntityFrameworkCore.IDbContextFactory<Campfire.Db.Ef.CampfireDb>)
    (gate: WriteGate)
    (env: Campfire.Db.Ef.Env)
    : Task<WebApplication * string * Cable> =
    task {
        let builder = WebApplication.CreateSlimBuilder()
        builder.WebHost.UseUrls "http://127.0.0.1:0" |> ignore
        builder.Logging.ClearProviders() |> ignore
        let services = builder.Services
        services.AddSingleton<Secrets>(secrets) |> ignore
        Campfire.Db.Ef.Data.addServices services factory gate time
        services.AddSingleton<Campfire.Db.Ef.Env>(env) |> ignore
        Campfire.App.Web.Services.addAuthentication services
        Campfire.App.Channels.CableServices.add services (CableHandle())
        let app = builder.Build()
        app.UseWebSockets() |> ignore
        app.Map(Endpoint.MountPath, Microsoft.AspNetCore.Http.RequestDelegate(fun ctx -> Endpoint.handle (ctx.RequestServices.GetRequiredService<Cable>()) ctx))
        |> ignore
        do! app.StartAsync()
        let address = (nonNull (app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<IServerAddressesFeature>())).Addresses |> Seq.head
        return app, (Uri address).Authority, app.Services.GetRequiredService<Cable>()
    }

let start () : Task<TestApp> =
    task {
        let dir = Directory.CreateTempSubdirectory("campfire-channels-test").FullName
        let sink = CableSink()
        let clock = FakeTimeProvider(DateTimeOffset.UtcNow)
        let secrets = Secrets.create SecretKeyBase
        let richText = AppRichText(secrets, clock, NullLogger.Instance)
        let gate = WriteGate()
        let path = Path.Combine(dir, "test.sqlite3")
        let time : TimeProvider = clock
        Schema.prepareFile path "test" time |> ignore
        let factory = Campfire.Db.Ef.Data.factory (Campfire.Db.Ef.Data.options path time ignore)
        let efEnv: Campfire.Db.Ef.Env =
            { Time = time
              Sink = sink
              RichText = richText
              BcryptCost = 4 }
        Testing.loadFixtures gate efEnv factory
        let! app, authority, server = serveCable secrets time factory gate efEnv
        sink.Server <- server
        return TestApp(gate, server, app.Services.GetRequiredService<Broadcasts>(), secrets, clock, app, $"ws://{authority}/cable", $"http://{authority}", dir, factory, efEnv)
    }
