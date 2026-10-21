// Phase 3, unit 3.5 (plans/design-cable.md section 5, A1-A6): the cable's authentication, re-check, scopes and after-commit
// publishing, through the app's real pipeline and host. A4 (a scope per command) is in Campfire.Cable.Tests (TransportTests);
// A6 (closing runs `absent`) is the presence case of ChannelsTests; the ban, deactivation and membership revocations are
// RevocationTests, which now authenticate through the same scheme.
module Campfire.App.Tests.CableAuthTests

open System
open System.Collections.Generic
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Xunit
open Campfire.App.Channels
open Campfire.App.Tests.Support
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat

let private davidSessionId = 481019661L

let private connect (test: Test) (cookie: string option) : Task<ClientWebSocket> =
    task {
        let ws = new ClientWebSocket()
        ws.Options.CollectHttpResponseDetails <- true
        ws.Options.AddSubProtocol "actioncable-v1-json"
        ws.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{test.Port}")
        match cookie with
        | Some cookie -> ws.Options.SetRequestHeader("Cookie", cookie)
        | None -> ()
        do! ws.ConnectAsync(Uri $"ws://127.0.0.1:{test.Port}/cable", CancellationToken.None)
        return ws
    }

/// The next message, or the close frame's status, within ten seconds. Pings are skipped.
let rec private next (ws: ClientWebSocket) : Task<Choice<string, WebSocketCloseStatus>> =
    task {
        use limit = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
        let buffer = Array.zeroCreate<byte> 65536
        use message = new IO.MemoryStream()
        let mutable fin = false
        let mutable closed = ValueNone
        while not fin do
            let! r = ws.ReceiveAsync(Memory<byte>(buffer), limit.Token)
            if r.MessageType = WebSocketMessageType.Close then
                closed <- ValueSome ws.CloseStatus.Value
                fin <- true
            else
                message.Write(buffer, 0, r.Count)
                fin <- r.EndOfMessage
        match closed with
        | ValueSome status -> return Choice2Of2 status
        | ValueNone ->
            let text = Encoding.UTF8.GetString(message.ToArray())
            if text.Contains "\"type\":\"ping\"" then return! next ws else return Choice1Of2 text
    }

let private text (ws: ClientWebSocket) : Task<string> =
    task {
        match! next ws with
        | Choice1Of2 text -> return text
        | Choice2Of2 status -> return failwith $"expected text, got close {status}"
    }

let private send (ws: ClientWebSocket) (json: string) : Task =
    ws.SendAsync(ReadOnlyMemory(Encoding.UTF8.GetBytes json), WebSocketMessageType.Text, true, CancellationToken.None).AsTask()

let private subscribe (ws: ClientWebSocket) (identifier: string) : Task =
    send ws (JsonSerializer.Serialize {| command = "subscribe"; identifier = identifier |})

let private welcome = """{"type":"welcome"}"""
let private unauthorized = """{"type":"disconnect","reason":"unauthorized","reconnect":false}"""

let private connectionsGone (test: Test) : Task =
    task {
        let mutable waited = 0
        while (test.App.Cable.ConnectionCount > 0 || test.App.Cable.StreamCount > 0) && waited < 100 do
            do! Task.Delay 50
            waited <- waited + 1
        Assert.Equal(0, test.App.Cable.ConnectionCount)
        Assert.Equal(0, test.App.Cable.StreamCount)
    }

[<Fact>]
let ``A1 the Rails minted session cookie opens the cable; no cookie, a forged one and a flipped digit get the unauthorized frame and a close`` () =
    task {
        use! test = bootSeeded "default"
        use! david = connect test (Some(davidCookie ()))
        let! first = text david
        Assert.Equal(welcome, first)
        let _, _, forged = sessionVectors ()
        let cookie = davidCookie ()
        let flipped = cookie.Substring(0, cookie.Length - 1) + (if cookie.EndsWith "0" then "1" else "0")
        for rejected in [ None; Some forged; Some flipped ] do
            use! ws = connect test rejected
            // Rails accepts the upgrade and tells the client: the golden "unauthenticated" session.
            let! first = text ws
            Assert.Equal(unauthorized, first)
            match! next ws with
            | Choice2Of2 status -> Assert.Equal(WebSocketCloseStatus.NormalClosure, status)
            | Choice1Of2 other -> failwith other
            do! ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        do! david.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        do! connectionsGone test
    }

[<Fact>]
let ``A2 an upgrade writes no sessions row and sets no cookie, even for a session two hours stale`` () =
    task {
        use! test = bootSeeded "default"
        let stale = DateTime.UtcNow - TimeSpan.FromHours 2.0
        do! test.Execute("UPDATE sessions SET last_active_at = ? WHERE id = ?", [ box (RailsTime.toDb stale); box davidSessionId ])
        let read () =
            test.Sql(fun conn ->
                use command = conn.CreateCommand()
                command.CommandText <- "SELECT last_active_at, updated_at, ip_address, user_agent FROM sessions WHERE id = @id"
                command.Parameters.AddWithValue("@id", davidSessionId) |> ignore
                use reader = command.ExecuteReader()
                Assert.True(reader.Read())
                [ for i in 0..3 -> if reader.IsDBNull i then "" else reader.GetString i ])
        let before = read ()
        use! ws = connect test (Some(davidCookie ()))
        let! first = text ws
        Assert.Equal(welcome, first)
        Assert.False((nonNull ws.HttpResponseHeaders).ContainsKey "Set-Cookie")
        Assert.Equal<string list>(before, read ())
        do! ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        do! connectionsGone test
    }

[<Fact>]
let ``A3 a session deleted between the first lookup and the registration is heard by the re-check`` () =
    task {
        use! test = bootSeeded "default"
        test.App.Cable.AfterRegistered <- fun () -> test.Execute("DELETE FROM sessions WHERE id = ?", [ box davidSessionId ]) :> Task
        use! ws = connect test (Some(davidCookie ()))
        let! first = text ws
        Assert.Equal(welcome, first)
        let! second = text ws
        Assert.Equal(unauthorized, second)
        match! next ws with
        | Choice2Of2 status -> Assert.Equal(WebSocketCloseStatus.NormalClosure, status)
        | Choice1Of2 other -> failwith other
        do! ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        // And the same cookie is unauthorized from the first look now.
        test.App.Cable.AfterRegistered <- fun () -> Task.CompletedTask
        use! again = connect test (Some(davidCookie ()))
        let! frame = text again
        Assert.Equal(unauthorized, frame)
        do! connectionsGone test
    }

[<Fact>]
let ``A5 publishing inside a write throws; a posted message reaches the room's subscriber only after its row is committed`` () =
    task {
        use! test = bootSeeded "default"
        let app = test.App
        // The guard (C16): `Writes.run` holds `InWrite` from BEGIN to COMMIT.
        use db = app.DbFactory.CreateDbContext()
        let! guarded =
            task {
                try
                    let! result = Campfire.Db.Ef.Writes.run app.Gate app.Env db (fun _ -> Publish.broadcast app.Cable "anything" (Payload.ofString "x"))
                    return Choice1Of2 result
                with e ->
                    return Choice2Of2 e
            }
        match guarded with
        | Choice2Of2 e -> Assert.IsType<InvalidOperationException> e |> ignore
        | Choice1Of2(Error error) -> Assert.Contains("inside a write", DbError.display error)
        | Choice1Of2(Ok _) -> failwith "a broadcast inside a write was published"
        // After the commit it is fine.
        Assert.Equal(0, Publish.broadcast app.Cable "anything" (Payload.ofString "x"))

        let room =
            use db = app.DbFactory.CreateDbContext()
            match Campfire.Db.Ef.Reads.run db (fun db -> Room.find db ALL_TALK) with
            | Ok room -> room
            | Error error -> failwith (DbError.display error)
        let gid = GlobalId.toParam (Gid.roomGid room)
        let signed = Turbo.signedStreamName app.Secrets [ gid; "messages" ]
        use! ws = connect test (Some(davidCookie ()))
        let! first = text ws
        Assert.Equal(welcome, first)
        let turbo = JsonSerializer.Serialize {| channel = "RoomMessagesChannel"; signed_stream_name = signed |}
        let unread = JsonSerializer.Serialize {| channel = "UnreadRoomsChannel" |}
        do! subscribe ws turbo
        do! subscribe ws unread
        for _ in 1..2 do
            let! confirmed = text ws
            Assert.True(confirmed.Contains "confirm_subscription", confirmed + test.Logs)
        let count () = test.Sql(fun conn -> RawSql.scalar<int64> conn "SELECT COUNT(*) FROM messages WHERE room_id = ?" [ box ALL_TALK ])
        let before = count ()
        let bot = anonymous test
        let! created = bot.Send((request "POST" $"/rooms/{ALL_TALK}/{BENDER_KEY}/messages").WithBody "Beep boop")
        Assert.Equal(201, created.Status)
        let mutable appended = false
        let mutable unreadSeen = false
        while not (appended && unreadSeen) do
            let! frame = text ws
            if frame.Contains "turbo-stream" then
                appended <- true
                Assert.Contains("append", frame)
                // The frame is published after the commit: the row is there when it arrives.
                Assert.Equal(before + 1L, count ())
            elif frame.Contains "roomId" then
                unreadSeen <- true
                Assert.Contains(string ALL_TALK, frame)
                Assert.Equal(before + 1L, count ())
            else
                failwith frame
        do! ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        do! connectionsGone test
    }
