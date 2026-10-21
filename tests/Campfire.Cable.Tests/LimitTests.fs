// Phase 3 (plans/design-cable.md section 5, unit 3.6): slow consumers and limits over Kestrel on loopback, driven by the raw TCP
// client (it can stop reading, send a frame in pieces, and see the close code): L1, L2, L4, L5 (binary). L3 and L6 and the
// 1007 half of L5 are in ProtocolTests; L7 is in MemoryTests.
module Campfire.Cable.Tests.LimitTests

open System
open System.Diagnostics
open System.Text
open System.Threading.Tasks
open Xunit
open Campfire.RailsCompat
open Campfire.Cable
open Campfire.Cable.Tests.Support

let private str (s: string) = Value.String s
let private int' (n: int) = Value.Int(int64 n)

let private roomId (id: int) : string =
    identifier (object' [ "channel", str "RoomChannel"; "room_id", int' id ])

let private heartbeatId = identifier (object' [ "channel", str "HeartbeatChannel" ])

let private confirmation (identifier: string) : string =
    $"""{{"identifier":{Json.generate (str identifier)},"type":"confirm_subscription"}}"""

let private withServer (options: CableOptions) (body: TestServer -> Task) : Task =
    task {
        let! app = start options
        try
            do! (body app).WaitAsync(TimeSpan.FromSeconds 60.0)
        finally
            (app :> IAsyncDisposable).DisposeAsync().AsTask().Wait()
    }

let private waitUntil (what: string) (condition: unit -> bool) : Task =
    task {
        let mutable waited = 0
        while not (condition ()) && waited < 200 do
            do! Task.Delay 50
            waited <- waited + 1
        if not (condition ()) then failwith $"timed out waiting for {what}"
    }

let private subscribed (client: Client) (identifier: string) : Task =
    task {
        do! client.Subscribe identifier
        let! frame = client.NextText()
        Assert.Equal(confirmation identifier, frame)
    }

let private unsubscribedCount (app: TestServer) : int =
    lock app.Log (fun () -> app.Log |> Seq.filter (fun line -> line.StartsWith "unsubscribed") |> Seq.length)

/// The next text frame, within `seconds`. The raw client's own read limit restarts with every ping, so a missing frame would
/// otherwise wait as long as the server keeps pinging.
let private within (seconds: float) (read: Task<string>) : Task<string> = read.WaitAsync(TimeSpan.FromSeconds seconds)

/// Streams register before the confirmation (C13): one broadcast made as soon as a client has read it reaches every subscriber.
let private broadcastUntilReached (app: TestServer) (subscribers: int) (payload: Payload) : Task =
    task {
        let reached = app.Server.Broadcast("room:room-1", payload)
        Assert.Equal(subscribers, reached)
    }

/// C13: a client that has its confirmation is on the stream. The registration used to follow the confirmation, so a broadcast made
/// in between reached nobody although the client had been confirmed; each round here broadcasts once, right after the read. The old
/// window was microseconds wide, so this is a stress guard, not a deterministic one (it passed 5 runs against the old order); L1 and
/// L2 broadcast once too, and under CPU contention they are what first showed the window.
[<Fact>]
let ``C13 a broadcast made as soon as the confirmation is read reaches the subscriber, every time`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            for round in 1..300 do
                do! subscribed client (roomId 1)
                let reached = app.Server.Broadcast("room:room-1", Payload.ofValue round)
                Assert.True((reached = 1), $"round {round}: the confirmed subscriber was not on the stream")
                let! frame = within 5.0 (client.NextText())
                Assert.Contains($"\"message\":{round}}}", frame)
                do! client.Unsubscribe(roomId 1)
            (client :> IDisposable).Dispose()
        })

[<Fact>]
let ``L1 a client that never reads is aborted when its queue fills, and another client on the stream gets every frame`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! slow = connectNonReading app 1UL
            let! _ = slow.NextText()
            do! subscribed slow (roomId 1)
            let! fast = connect app 2UL
            let! _ = fast.NextText()
            do! subscribed fast (roomId 1)
            // Frame n is its own number padded to 600 bytes. Batches of 20 go out from the thread pool, as a request's would, and `fast`
            // reads each batch before the next, so only `slow` falls behind. `slow` never reads: its small receive buffer, the server's
            // send buffer and Kestrel's pipe absorb a few hundred KB, then the queue (256 frames) fills and the next write is refused.
            // Both subscribers are on the stream before the numbered frames start; `fast` reads the one frame that proves it.
            do! broadcastUntilReached app 2 (Payload.ofString "sync")
            let! sync = within 5.0 (fast.NextText())
            Assert.Contains("sync", sync)
            let number (n: int) = Payload.ofString (n.ToString("D8") + String('x', 592))
            let mutable next = 0
            let mutable refused = false
            let mutable firstSeen = -1
            let mutable lastSeen = -1
            while not refused && next < 20000 do
                let first = next
                let! reached = Task.Run(fun () -> [ for i in first .. first + 19 -> app.Server.Broadcast("room:room-1", number i) ])
                next <- first + 20
                if reached |> List.exists (fun r -> r < 2) then refused <- true
                // Read until the batch's last frame.
                while lastSeen < next - 1 do
                    let! frame = within 5.0 (fast.NextText())
                    let n = int (frame.Substring(frame.IndexOf "message\":\"" + 10, 8))
                    if firstSeen < 0 then firstSeen <- n
                    else Assert.Equal(lastSeen + 1, n)
                    lastSeen <- n
            TestContext.Current.SendDiagnosticMessage $"L1: the slow client was refused after {next} frames of 600 bytes"
            Assert.True(refused, $"{next} frames were broadcast and the slow client was never refused")
            Assert.Equal(0, firstSeen)
            do! waitUntil "the slow connection to leave and unsubscribe" (fun () -> app.Server.ConnectionCount = 1 && unsubscribedCount app = 1)
            Assert.Equal(1, app.Server.StreamCount)
            // The survivor still gets what is broadcast next.
            app.Server.Broadcast("room:room-1", Payload.ofString "after") |> ignore
            let! last = within 5.0 (fast.NextText())
            Assert.Contains("after", last)
            (slow :> IDisposable).Dispose()
        })

[<Fact>]
let ``L2 a send that stalls longer than SendTimeout aborts the connection, with less than a full queue behind it`` () =
    let options = testOptions ()
    options.SendTimeout <- TimeSpan.FromSeconds 1.0
    withServer options (fun app ->
        task {
            let! slow = connectNonReading app 1UL
            let! _ = slow.NextText()
            do! subscribed slow (roomId 1)
            // 40 frames of 64 KB is 2.5 MB: more than the socket and Kestrel's pipe take, far less than 256 frames.
            let frame = Payload.ofString (String('y', 65000))
            do! broadcastUntilReached app 1 frame
            let! reached = Task.Run(fun () -> [ for _ in 1..39 -> app.Server.Broadcast("room:room-1", frame) ])
            Assert.All(reached, fun r -> Assert.Equal(1, r))
            let started = Stopwatch.StartNew()
            do! waitUntil "the stalled connection to be cut" (fun () -> app.Server.ConnectionCount = 0)
            let elapsed = started.Elapsed
            // The stalled send began a few milliseconds before the loop ended; it is cut one SendTimeout after it began.
            Assert.True(elapsed >= TimeSpan.FromSeconds 0.5 && elapsed < TimeSpan.FromSeconds 5.0, $"cut after {elapsed}")
            do! waitUntil "the stream to be released" (fun () -> app.Server.StreamCount = 0)
            do! waitUntil "unsubscribed to run" (fun () -> unsubscribedCount app = 1)
            (slow :> IDisposable).Dispose()
        })

/// A subscribe command for the heartbeat channel, padded with an ignored key to exactly `size` bytes of UTF-8.
let private paddedSubscribe (size: int) : byte[] =
    let prefix = $"""{{"command":"subscribe","identifier":{Json.generate (str heartbeatId)},"pad":""" + "\""
    let suffix = "\"}"
    let pad = size - Encoding.UTF8.GetByteCount prefix - Encoding.UTF8.GetByteCount suffix
    Assert.True(pad > 0)
    Encoding.UTF8.GetBytes(prefix + String('p', pad) + suffix)

/// The same bytes as a text message in four fragments (a text frame, then continuations).
let private fragments (bytes: byte[]) : byte[] list =
    let size = bytes.Length / 4
    [ for i in 0..3 do
          let start = i * size
          let piece = if i = 3 then bytes[start..] else bytes[start .. start + size - 1]
          clientFrame (if i = 0 then 0x1uy else 0x0uy) (i = 3) false piece ]

let private limit = 1048576

[<Fact>]
let ``L4 a message of exactly 1 MiB is processed, whole or in four fragments`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            do! client.SendText(Encoding.UTF8.GetString(paddedSubscribe limit))
            let! first = client.NextText()
            Assert.Equal(confirmation heartbeatId, first)
            // A second subscribe of the same identifier gets no reply, so use another identifier for the fragmented one.
            let other = identifier (object' [ "channel", str "HeartbeatChannel"; "nonce", int' 1 ])
            let prefix = $"""{{"command":"subscribe","identifier":{Json.generate (str other)},"pad":""" + "\""
            let bytes = Encoding.UTF8.GetBytes(prefix + String('p', limit - Encoding.UTF8.GetByteCount prefix - 2) + "\"}")
            Assert.Equal(limit, bytes.Length)
            for piece in fragments bytes do
                do! client.SendBytes piece
            let! second = client.NextText()
            Assert.Equal(confirmation other, second)
        })

[<Fact>]
let ``L4 a message of 1 MiB and one byte closes the connection with 1009`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            do! client.SendBytes(clientFrame 0x1uy true false (paddedSubscribe (limit + 1)))
            let! got = client.Next()
            Assert.Equal(Closed(Some(1009us, "")), got)
            do! waitUntil "the connection to leave" (fun () -> app.Server.ConnectionCount = 0)
            Assert.NotEmpty(app.Logger.Containing "message too big")
        })

[<Fact>]
let ``L4 a message of 1 MiB and one byte in four fragments closes the connection with 1009`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            for piece in fragments (paddedSubscribe (limit + 1)) do
                do! client.SendBytes piece
            let! got = client.Next()
            Assert.Equal(Closed(Some(1009us, "")), got)
            do! waitUntil "the connection to leave" (fun () -> app.Server.ConnectionCount = 0)
        })

[<Fact>]
let ``L5 a binary message is logged and ignored, and the connection stays open`` () =
    withServer (testOptions ()) (fun app ->
        task {
            let! client = connect app 1UL
            let! _ = client.NextText()
            do! client.SendBytes(clientFrame 0x2uy true false [| 1uy; 2uy; 3uy |])
            do! subscribed client heartbeatId
            Assert.NotEmpty(app.Logger.Containing "non-string message")
            Assert.Equal(1, app.Server.ConnectionCount)
        })
