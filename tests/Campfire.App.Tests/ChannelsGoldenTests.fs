// Port of rust/crates/campfire/src/channels/tests/golden.rs
//
// Frame sequences recorded from the reference app's channels, replayed against ours.
//
// `testdata/channels_golden.json` (Rust's `golden/reference.json`) holds what `golden/fixtures.rb` created in the
// reference (the rows the replay loads into a fresh database, the session cookies and the signed stream names) and
// the frames each socket received at every step of `script`. Server-side events (an unread fanout, a message removal,
// revoking a membership, deactivating a user) ran through `golden/trigger.rb` inside the reference, and run through the
// equivalent calls here on replay. Both sides use `SECRET_KEY_BASE` from `parity/.env.reference`, so the cookies and
// signed names work on either. Re-recording needs a running reference app (`record_reference`, an `#[ignore]`d test in
// Rust) and isn't ported: use Rust's recorder and copy its output here.
//
// Frames that reach one socket in one step by different paths (a confirmation and a broadcast) race in Rails, where
// the confirmation waits for Redis to acknowledge the subscription, so each step's frames are compared per socket as a
// sorted list. Pings are dropped.
module Campfire.App.Tests.ChannelsGoldenTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Campfire.App
open Campfire.App.Channels
open Campfire.App.Tests.ChannelsSupport
open Campfire.App.Tests.Support
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Campfire.RailsCompat.Clock
open Campfire.Tests

/// How long a replay waits for a frame the recording says is coming.
let private expectedFrameWait = TimeSpan.FromSeconds 10.0

type private Step =
    /// Opens socket `.0` with the named cookie, or none.
    | Connect of socket: string * cookie: string option
    | Send of socket: string * text: string
    /// A server-side event, with token names as its arguments.
    | Trigger of event: string * args: string list

let private subscribe (identifier: Value) : string =
    Json.generate (Value.Object [ "command", Value.String "subscribe"; "identifier", Value.String(Json.generate identifier) ])

let private perform (identifier: Value) (data: Value) : string =
    Json.generate (
        Value.Object
            [ "command", Value.String "message"
              "identifier", Value.String(Json.generate identifier)
              "data", Value.String(Json.generate data) ]
    )

let private script (tokens: IReadOnlyDictionary<string, string>) : (string * Step) list =
    let t (name: string) = tokens[name]
    let roomId = Int64.Parse(t "ROOM_ID")
    let closedId = Int64.Parse(t "CLOSED_ID")
    let channel (name: string) (entries: (string * Value) list) = Value.Object(("channel", Value.String name) :: entries)

    let reads = channel "ReadRoomsChannel" []
    let unreads = channel "UnreadRoomsChannel" []
    let presence = channel "PresenceChannel" [ "room_id", Value.Int roomId ]
    let room = channel "RoomChannel" [ "room_id", Value.Int roomId ]
    let typing = channel "TypingNotificationsChannel" [ "room_id", Value.Int roomId ]
    let messages = channel "RoomMessagesChannel" [ "signed_stream_name", Value.String(t "ROOM_MESSAGES_SIGNED") ]
    let turbo (signed: string) = channel "Turbo::StreamsChannel" [ "signed_stream_name", Value.String signed ]
    let guarded = turbo (t "ROOM_MESSAGES_SIGNED")
    let rooms = turbo (t "ROOMS_SIGNED")
    let forged = "InJvb21zIg==--0000"
    let action (name: string) = Value.Object [ "action", Value.String name ]

    [ "connect A", Connect("A", Some "A")
      "connect B", Connect("B", Some "B")
      "connect without a cookie", Connect("X", None)
      "A heartbeat", Send("A", subscribe (channel "HeartbeatChannel" []))
      "A base channel", Send("A", subscribe (channel "ApplicationCable::Channel" []))
      "A reads", Send("A", subscribe reads)
      "A unreads", Send("A", subscribe unreads)
      "A presence", Send("A", subscribe presence)
      "A presence in a room A isn't in", Send("A", subscribe (channel "PresenceChannel" [ "room_id", Value.Int closedId ]))
      "A presence with a non-numeric room", Send("A", subscribe (channel "PresenceChannel" [ "room_id", Value.String "abc" ]))
      "A presence with a numeric string room",
      Send("A", subscribe (channel "PresenceChannel" [ "room_id", Value.String(string roomId) ]))
      "A room", Send("A", subscribe room)
      "A room A isn't in", Send("A", subscribe (channel "RoomChannel" [ "room_id", Value.Int closedId ]))
      "A room without an id", Send("A", subscribe (channel "RoomChannel" []))
      "A typing", Send("A", subscribe typing)
      "B typing", Send("B", subscribe typing)
      "A starts typing", Send("A", perform typing (action "start"))
      "B stops typing", Send("B", perform typing (Value.Object [ "action", Value.String "stop"; "extra", Value.Int 1L ]))
      "A performs an unknown typing action", Send("A", perform typing (action "dance"))
      "A room messages", Send("A", subscribe messages)
      "A room messages for a room A isn't in",
      Send("A", subscribe (channel "RoomMessagesChannel" [ "signed_stream_name", Value.String(t "CLOSED_MESSAGES_SIGNED") ]))
      "A room messages without a name", Send("A", subscribe (channel "RoomMessagesChannel" []))
      "A room messages with a forged name",
      Send("A", subscribe (channel "RoomMessagesChannel" [ "signed_stream_name", Value.String forged ]))
      "A room messages with the rooms name",
      Send("A", subscribe (channel "RoomMessagesChannel" [ "signed_stream_name", Value.String(t "ROOMS_SIGNED") ]))
      "A turbo rooms", Send("A", subscribe rooms)
      "A turbo own rooms", Send("A", subscribe (turbo (t "A_ROOMS_SIGNED")))
      "A turbo guarded room messages", Send("A", subscribe guarded)
      "A turbo forged", Send("A", subscribe (turbo forged))
      "A presence present", Send("A", perform presence (action "present"))
      "A presence refresh", Send("A", perform presence (action "refresh"))
      "A unreads subscribed again", Send("A", perform unreads (action "subscribed"))
      "unread fanout", Trigger("unread", [ "MESSAGE_ID" ])
      "message removed", Trigger("remove_message", [ "MESSAGE_ID" ])
      "B presence", Send("B", subscribe presence)
      "A typing again", Send("A", perform typing (action "start"))
      "revoke A", Trigger("revoke", [ "ROOM_ID"; "A_ID" ])
      "reconnect A", Connect("A2", Some "A")
      "A2 presence", Send("A2", subscribe presence)
      "A2 room", Send("A2", subscribe room)
      "A2 typing", Send("A2", subscribe typing)
      "A2 room messages", Send("A2", subscribe messages)
      "A2 turbo guarded room messages", Send("A2", subscribe guarded)
      "A2 turbo rooms", Send("A2", subscribe rooms)
      "A2 unreads", Send("A2", subscribe unreads)
      "B typing after A left", Send("B", perform typing (action "start"))
      "deactivate B", Trigger("deactivate", [ "B_ID" ])
      "reconnect B", Connect("B2", Some "B") ]

/// What each socket received during a step (quiet sockets are left out), by socket name.
type private Exchange = { Step: string; Frames: Map<string, string list> }

type private Recording =
    { Cookies: Map<string, string>
      Tokens: Map<string, string>
      Rows: (string * (string * JsonElement) list list) list
      Steps: Exchange list }

let private parseRecording (document: JsonDocument) : Recording =
    let root = document.RootElement
    let fixtures = root.GetProperty "fixtures"
    let strings (element: JsonElement) =
        [ for property in element.EnumerateObject() -> property.Name, str property.Value ] |> Map.ofList
    { Cookies = strings (fixtures.GetProperty "cookies")
      Tokens = strings (fixtures.GetProperty "tokens")
      Rows =
        [ for table in fixtures.GetProperty("rows").EnumerateObject() ->
              table.Name, [ for row in table.Value.EnumerateArray() -> [ for column in row.EnumerateObject() -> column.Name, column.Value ] ] ]
      Steps =
        [ for step in root.GetProperty("steps").EnumerateArray() ->
              { Step = str (step.GetProperty "step")
                Frames =
                  [ for socket in step.GetProperty("frames").EnumerateObject() ->
                        socket.Name, [ for frame in socket.Value.EnumerateArray() -> str frame ] ]
                  |> Map.ofList } ] }

let private sqlValue (value: JsonElement) : obj | null =
    match value.ValueKind with
    | JsonValueKind.Null -> box DBNull.Value
    | JsonValueKind.True -> box 1L
    | JsonValueKind.False -> box 0L
    | JsonValueKind.Number ->
        match value.TryGetInt64() with
        | true, n -> box n
        | _ -> box (value.GetDouble())
    | JsonValueKind.String -> box (str value)
    | _ -> box (value.GetRawText())

let private ok (result: Result<'T, DbError>) : 'T =
    match result with
    | Ok value -> value
    | Error error -> failwith (DbError.display error)

/// A fresh database holding the reference's rows, and our channels over it.
let private startOurs (recording: Recording) (dir: string) : Task<WriteGate * Cable * Broadcasts * Microsoft.AspNetCore.Builder.WebApplication * string * (Microsoft.EntityFrameworkCore.IDbContextFactory<Campfire.Db.Ef.CampfireDb> * Campfire.Db.Ef.Env)> =
    task {
        let sink = CableSink()
        let clock = SystemClock()
        let gate = WriteGate()
        let path = Path.Combine(dir, "production.sqlite3")
        Testing.prepare path "production" |> ignore
        do
            // The recorded rows, as plain ADO.NET in one transaction (nothing here knows the entities).
            use connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False")
            connection.Open()
            use transaction = connection.BeginTransaction()
            for table in [ "users"; "rooms"; "memberships"; "sessions"; "messages" ] do
                for row in recording.Rows |> List.find (fun (name, _) -> name = table) |> snd do
                    let columns = row |> List.map (fun (column, _) -> $"\"{column}\"") |> String.concat ", "
                    let holes = String.Join(", ", Array.create row.Length "?")
                    RawSql.run connection $"INSERT INTO {table} ({columns}) VALUES ({holes})" (row |> List.map (snd >> sqlValue))
            transaction.Commit()

        let secrets = Secrets.create (parityEnv "SECRET_KEY_BASE" |> Option.defaultWith (fun () -> failwith "parity/.env.reference has no SECRET_KEY_BASE"))
        let factory = Campfire.Db.Ef.Data.factory (Campfire.Db.Ef.Data.options (Path.Combine(dir, "production.sqlite3")) (Campfire.Db.Ef.ClockTimeProvider clock) ignore)
        let efEnv: Campfire.Db.Ef.Env =
            { Time = Campfire.Db.Ef.ClockTimeProvider clock
              Sink = sink
              RichText = BasicRichText()
              BcryptCost = 4 }
        let deps: Deps = { DbFactory = factory; Gate = gate; Env = efEnv; Secrets = secrets; Clock = clock }
        // The reference runs with DISABLE_SSL, so without assume_ssl.
        let server = Registry.server deps (Campfire.Cable.CableOptions()) NullLogger.Instance
        sink.Server <- server
        let! host, authority = serveCable server
        return gate, server, Broadcasts server, host, authority, (factory, efEnv)
    }

/// Frames until the socket has sent `atLeast` of them and then been quiet for a moment.
let private collect (client: Client) (quiet: TimeSpan) (atLeast: int) : Task<string list> =
    task {
        let frames = ResizeArray<string>()
        let mutable go = true
        while go do
            let wait = if frames.Count < atLeast then expectedFrameWait else quiet
            match! client.TryNext wait with
            | None -> go <- false
            | Some(WsText text) -> frames.Add text
            | Some(WsClose status) ->
                let shown =
                    match status with
                    | Some(code, reason) -> $"Some(({code}, \"{reason}\"))"
                    | None -> "None"
                frames.Add $"close {shown}"
            | Some WsEnd -> go <- false
        return List.ofSeq frames
    }

[<Fact>]
let ``replays reference frames`` () =
    task {
        use golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", "channels_golden.json")))
        let recording = parseRecording golden
        let dir = Directory.CreateTempSubdirectory("campfire-channels-golden").FullName
        let! gate, _, broadcasts, host, authority, (factory, efEnv) = startOurs recording dir
        let url = Uri $"ws://{authority}/cable"
        let origin = $"http://{authority}"
        let sockets = SortedDictionary<string, Client>(StringComparer.Ordinal)
        try
            let sorted (frames: string list) = List.sort frames
            let expectedSteps = recording.Steps
            let mutable index = 0
            let tokens = recording.Tokens :> IReadOnlyDictionary<string, string>
            for (name, step) in script tokens do
                match step with
                | Connect(socket, cookie) ->
                    let ws = new System.Net.WebSockets.ClientWebSocket()
                    ws.Options.SetRequestHeader("Origin", origin)
                    ws.Options.AddSubProtocol "actioncable-v1-json"
                    ws.Options.AddSubProtocol "actioncable-unsupported"
                    match cookie with
                    | Some cookie -> ws.Options.SetRequestHeader("Cookie", recording.Cookies[cookie])
                    | None -> ()
                    do! ws.ConnectAsync(url, Threading.CancellationToken.None)
                    sockets[socket] <- new Client(ws)
                | Send(socket, text) ->
                    let bytes = Text.Encoding.UTF8.GetBytes text
                    do! sockets[socket].Socket.SendAsync(ArraySegment<byte>(bytes), System.Net.WebSockets.WebSocketMessageType.Text, true, Threading.CancellationToken.None)
                | Trigger(event, args) ->
                    let ids = args |> List.map (fun arg -> Int64.Parse recording.Tokens[arg])
                    match event with
                    | "unread" ->
                        use context = factory.CreateDbContext()
                        Campfire.Db.Ef.Reads.run context (fun db ->
                            let message = Message.find db ids[0]
                            broadcasts.UnreadRoom(db, Room.find db message.RoomId))
                        |> ok
                    | "remove_message" ->
                        use context = factory.CreateDbContext()
                        Campfire.Db.Ef.Reads.run context (fun db ->
                            let message = Message.find db ids[0]
                            broadcasts.MessageRemove(Room.find db message.RoomId, message))
                        |> ok
                    | "revoke" ->
                        (task {
                            use context = factory.CreateDbContext()
                            return! Campfire.Db.Ef.Writes.run gate efEnv context (fun tx -> Room.revokeFrom tx (Room.find tx.Db ids[0]) [ ids[1] ])
                         })
                        |> fun write -> write.Result |> ok
                    | "deactivate" ->
                        (task {
                            use context = factory.CreateDbContext()
                            return! Campfire.Db.Ef.Writes.run gate efEnv context (fun tx -> User.deactivate tx (User.find tx.Db ids[0]) |> ignore)
                         })
                        |> fun write -> write.Result |> ok
                    | other -> failwith $"unknown trigger {other}"

                let expected = expectedSteps[index]
                Assert.Equal(expected.Step, name)
                let frames = Dictionary<string, string list>()
                for KeyValue(socket, client) in sockets do
                    let atLeast = expected.Frames |> Map.tryFind socket |> Option.map List.length |> Option.defaultValue 0
                    let! received = collect client (TimeSpan.FromMilliseconds 100.0) atLeast
                    if not received.IsEmpty then frames[socket] <- sorted received
                let actual = frames |> Seq.map (fun (KeyValue(k, v)) -> k, v) |> Map.ofSeq
                let wanted = expected.Frames |> Map.map (fun _ frames -> sorted frames)
                // Decoded comparison (C15), per socket: the same sockets heard, each with the same frames (sorted, see above).
                Assert.True(
                    (actual |> Map.keys |> List.ofSeq) = (wanted |> Map.keys |> List.ofSeq),
                    $"step \"{expected.Step}\": sockets that heard frames: expected {wanted |> Map.keys |> List.ofSeq} but got {actual |> Map.keys |> List.ofSeq}"
                )
                for KeyValue(socket, wantedFrames) in wanted do
                    try
                        JsonFrames.assertSameFramesUnordered wantedFrames actual[socket]
                    with e ->
                        failwith $"step \"{expected.Step}\", socket {socket}: {e.Message}"
                index <- index + 1
            Assert.Equal(expectedSteps.Length, index)
        finally
            for client in sockets.Values do
                (client :> IDisposable).Dispose()
            host.StopAsync((new Threading.CancellationTokenSource(TimeSpan.FromSeconds 2.0)).Token).Wait()
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
            try
                Directory.Delete(dir, true)
            with _ ->
                ()
    }
