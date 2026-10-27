// Port of rust/crates/cable/tests/support/mod.rs
//
// A tiny app on top of the cable server, and a raw WebSocket client for driving it. The client is
// written here rather than taken from `System.Net.WebSockets` so that tests can send what a browser
// wouldn't (invalid UTF-8, an unmasked frame), see the close frames and the 404s exactly as the server
// sent them, and stop reading (a slow consumer).
module Campfire.Cable.Tests.Support

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Sockets
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Campfire.Rails
open Campfire.Cable

type User = { Id: uint64; RoomIds: uint64 list }

let identify (user: User) : string = $"user-{user.Id}"

/// `Cookie: session_token=<user id>`; users 1 and 2 exist, and both are members of room 1 only.
let cookieAuth (ctx: HttpContext) : Task<User option> =
    let cookie = ctx.Request.Headers["Cookie"].ToString()
    let user =
        if cookie.StartsWith("session_token=", StringComparison.Ordinal) then
            match UInt64.TryParse(cookie.Substring 14) with
            | true, id when id = 1UL || id = 2UL -> Some { Id = id; RoomIds = [ 1UL ] }
            | _ -> None
        else
            None
    Task.FromResult user

type Log = List<string>

/// One line of a `RecordingLogger`.
type LogEntry = { Level: LogLevel; Text: string; Error: exn | null }

/// An `ILogger` that keeps what it is given, for tests that assert on what the server logged (or did not). Records `minLevel` and up.
type RecordingLogger(minLevel: LogLevel) =
    let entries = List<LogEntry>()
    member _.Entries: LogEntry list = lock entries (fun () -> List.ofSeq entries)
    /// Every entry whose text, or whose exception's message, contains `fragment`.
    member this.Containing(fragment: string) : LogEntry list =
        this.Entries
        |> List.filter (fun e ->
            e.Text.Contains(fragment, StringComparison.Ordinal)
            || (match e.Error with
                | null -> false
                | error -> error.ToString().Contains(fragment, StringComparison.Ordinal)))

    interface ILogger with
        member _.BeginScope<'S when 'S: not null>(_state: 'S) : IDisposable | null = null
        member _.IsEnabled(level: LogLevel) = level >= minLevel

        member _.Log<'S>(level: LogLevel, _eventId: EventId, state: 'S, error: exn | null, formatter: Func<'S, exn | null, string>) =
            if level >= minLevel then
                let text = formatter.Invoke(state, error)
                lock entries (fun () -> entries.Add { Level = level; Text = text; Error = error })

let private uintParam (value: System.Text.Json.JsonElement voption) : uint64 option =
    match value with
    | ValueSome v when v.ValueKind = System.Text.Json.JsonValueKind.Number ->
        match v.TryGetUInt64() with
        | true, n -> Some n
        | _ -> None
    | _ -> None

/// Like `RoomChannel` plus `TypingNotificationsChannel`'s actions.
let roomChannel (log: Log) () : Channel<User> =
    let room = ref None
    { Subscribed =
        fun sub ->
            match uintParam (sub.Param "room_id") |> Option.filter (fun id -> List.contains id sub.CurrentUser.RoomIds) with
            | Some id ->
                let name = $"room-{id}"
                sub.StreamFor [ name ]
                room.Value <- Some name
                lock log (fun () -> log.Add("subscribed rejected=" + (if sub.Rejected then "true" else "false")))
                Task.CompletedTask
            | None ->
                sub.Reject()
                lock log (fun () -> log.Add("subscribed rejected=" + (if sub.Rejected then "true" else "false")))
                Task.CompletedTask
      Unsubscribed =
        fun sub ->
            lock log (fun () -> log.Add("unsubscribed rejected=" + (if sub.Rejected then "true" else "false")))
            Task.CompletedTask
      Perform =
        fun action data sub ->
            match action with
            | "start" ->
                sub.BroadcastTo([ room.Value.Value ], Payload.ofValue {| action = "start"; user = {| id = sub.CurrentUser.Id |} |})
                Task.FromResult true
            | "echo" ->
                sub.Transmit(Payload.ofValue data)
                Task.FromResult true
            | "receive" ->
                sub.Transmit(Payload.ofValue {| received = data |})
                Task.FromResult true
            | _ -> Task.FromResult false }

/// Signed names in tests are `signed(<name>)`.
let testVerifier (signed: string) : string option =
    if signed.StartsWith("signed(", StringComparison.Ordinal) && signed.EndsWith(")", StringComparison.Ordinal) then
        Some(signed.Substring(7, signed.Length - 8))
    else
        None

/// Plain options: the origin check compares with `http://host` unless the host assumes SSL.
let testOptions () : CableOptions = CableOptions(CloseTimeout = TimeSpan.FromSeconds 1.0)

/// Kestrel on a loopback port with the WebSocket middleware, `server` mounted at /cable, and the heartbeat and shutdown
/// hosted services. `assumeSsl` makes every request look like HTTPS, as the app's edge middleware does.
let hostWith (server: CableServer<'U>) (assumeSsl: bool) : Task<WebApplication * string> =
    task {
        let builder = WebApplication.CreateSlimBuilder()
        builder.Logging.ClearProviders() |> ignore
        builder.WebHost.UseUrls "http://127.0.0.1:0" |> ignore
        builder.Services.AddHostedService<CableHeartbeat<'U>>(fun _ -> new CableHeartbeat<'U>(server)) |> ignore
        let app = builder.Build()
        // A server built outside the host has no lifetime to hook (`ServerBuilder.withLifetime`): the host's stop restarts it.
        app.Lifetime.ApplicationStopping.Register(fun () -> server.Restart()) |> ignore
        if assumeSsl then
            app.Use(fun (ctx: HttpContext) (next: Func<Task>) ->
                ctx.Request.Scheme <- "https"
                next.Invoke())
            |> ignore
        app.UseWebSockets() |> ignore
        app.Map(Endpoint.MountPath, RequestDelegate(fun ctx -> Endpoint.handle server ctx)) |> ignore
        do! app.StartAsync()
        let address = (nonNull (app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<IServerAddressesFeature>())).Addresses |> Seq.head
        return app, address
    }

let host (server: CableServer<'U>) : Task<WebApplication * string> = hostWith server false

type TestServer =
    { Server: CableServer<User>
      Host: WebApplication
      /// host:port
      Authority: string
      Origin: string
      Log: Log
      /// What the server logged (Information and up).
      Logger: RecordingLogger }

    member this.Url = $"ws://{this.Authority}/cable"

    interface IAsyncDisposable with
        member this.DisposeAsync() =
            task {
                do! this.Host.StopAsync()
                // Nothing is left behind by the test: every connection and stream is gone (acceptance of unit 3.4).
                let mutable waited = 0
                while (this.Server.ConnectionCount > 0 || this.Server.StreamCount > 0) && waited < 100 do
                    do! Task.Delay 50
                    waited <- waited + 1
                let connections, streams = this.Server.ConnectionCount, this.Server.StreamCount
                do! this.Host.DisposeAsync()
                if connections <> 0 || streams <> 0 then failwith $"left behind: {connections} connections, {streams} streams"
            }
            |> ValueTask

let startWith (options: CableOptions) (assumeSsl: bool) : Task<TestServer> =
    task {
        let log = Log()
        let logger = RecordingLogger LogLevel.Information
        let server =
            Server.builder options cookieAuth identify
            |> ServerBuilder.withLogger logger
            |> ServerBuilder.channel "RoomChannel" (roomChannel log)
            |> ServerBuilder.channel "HeartbeatChannel" (fun () -> Channel.empty)
            |> ServerBuilder.channel Turbo.StreamsChannelName (fun () ->
                Turbo.StreamsChannel.withVerifier testVerifier
                |> Turbo.StreamsChannel.guardedBy (fun name ->
                    match name.IndexOf ':' with
                    | -1 -> false
                    | i -> name.Substring(i + 1) = "messages")
                |> Turbo.StreamsChannel.channel)
            |> ServerBuilder.build
        let! app, address = hostWith server assumeSsl
        let authority = (Uri address).Authority
        return { Server = server; Host = app; Authority = authority; Origin = $"http://{authority}"; Log = log; Logger = logger }
    }

let start (options: CableOptions) : Task<TestServer> = startWith options false

let identifier (value: Value) : string = Json.generate value

let object' (entries: (string * Value) list) : Value = Value.Object entries

// --- A raw WebSocket client --------------------------------------------------------------------

/// What the server sent, as the Rust tests' `Frame`.
type Got =
    | Said of string
    | Closed of (uint16 * string) option
    | Failed of string
    | Ended

type Http =
    { Status: int
      Headers: (string * string) list
      Body: string }

    member this.Header(name: string) : string option =
        this.Headers |> List.tryFind (fun (k, _) -> String.Equals(k, name, StringComparison.OrdinalIgnoreCase)) |> Option.map snd

let private timeout () = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)

let private readExact (stream: Stream) (buffer: byte[]) (count: int) : Task =
    task {
        use limit = timeout ()
        let mutable got = 0
        while got < count do
            let! n = stream.ReadAsync(Memory<byte>(buffer, got, count - got), limit.Token)
            if n = 0 then raise (EndOfStreamException())
            got <- got + n
    }

/// Reads an HTTP response head (and a Content-Length body, if there is one) off `stream`.
let readHttp (stream: Stream) : Task<Http> =
    task {
        let head = StringBuilder()
        let one = Array.zeroCreate<byte> 1
        let mutable fin = false
        while not fin do
            do! readExact stream one 1
            head.Append(char one[0]) |> ignore
            fin <- head.Length >= 4 && head.ToString(head.Length - 4, 4) = "\r\n\r\n"
        let lines = head.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
        let status = int ((lines[0].Split ' ')[1])
        let headers =
            [ for line in lines |> Array.skip 1 ->
                  let i = line.IndexOf ':'
                  line.Substring(0, i), line.Substring(i + 1).Trim() ]
        let length =
            headers
            |> List.tryFind (fun (k, _) -> k.Equals("content-length", StringComparison.OrdinalIgnoreCase))
            |> Option.map (snd >> int)
            |> Option.defaultValue 0
        let body = Array.zeroCreate<byte> length
        if length > 0 then do! readExact stream body length
        return { Status = status; Headers = headers; Body = Encoding.UTF8.GetString body }
    }

/// A client frame, masked.
let clientFrame (opcode: byte) (fin: bool) (rsv1: bool) (payload: byte[]) : byte[] =
    let mask = [| 0x12uy; 0x34uy; 0x56uy; 0x78uy |]
    use frame = new MemoryStream()
    frame.WriteByte((if fin then 0x80uy else 0uy) ||| (if rsv1 then 0x40uy else 0uy) ||| opcode)
    match payload.Length with
    | len when len < 126 -> frame.WriteByte(0x80uy ||| byte len)
    | len when len <= 65535 ->
        frame.WriteByte(0x80uy ||| 126uy)
        frame.WriteByte(byte (len >>> 8))
        frame.WriteByte(byte len)
    | len ->
        frame.WriteByte(0x80uy ||| 127uy)
        for shift in 7 .. -1 .. 0 do
            frame.WriteByte(byte (uint64 len >>> (shift * 8)))
    frame.Write(mask, 0, 4)
    frame.Write(payload |> Array.mapi (fun i b -> b ^^^ mask[i % 4]), 0, payload.Length)
    frame.ToArray()

type Client(tcp: TcpClient, stream: Stream, protocol: string option, extensions: string option) =
    let gate = obj ()
    let mutable closed = false
    let mutable pendingRaw: Task<Got> = Unchecked.defaultof<_>
    let mutable pendingNext: Task<Got> = Unchecked.defaultof<_>

    member _.Protocol = protocol
    member _.Extensions = extensions
    member _.Stream = stream

    static member Open(authority: string, headers: (string * string) list) : Task<Choice<Client, Http>> =
        Client.Open(authority, headers, false)

    /// Connects, over TLS when `tls` (trusting whatever certificate the server has), and upgrades.
    static member Open(authority: string, headers: (string * string) list, tls: bool) : Task<Choice<Client, Http>> =
        Client.Open(authority, headers, tls, 0)

    /// As above, with the socket's receive buffer fixed at `receiveBuffer` bytes (0 = the system's, which grows by autotuning).
    /// A client that never reads is only slow once the kernel stops absorbing for it, and an autotuned buffer can take megabytes.
    static member Open(authority: string, headers: (string * string) list, tls: bool, receiveBuffer: int) : Task<Choice<Client, Http>> =
        task {
            let host, port =
                let i = authority.LastIndexOf ':'
                authority.Substring(0, i), int (authority.Substring(i + 1))
            let tcp = new TcpClient()
            if receiveBuffer > 0 then tcp.ReceiveBufferSize <- receiveBuffer
            do! tcp.ConnectAsync(host, port)
            tcp.NoDelay <- true
            let! stream =
                task {
                    if tls then
                        let ssl = new System.Net.Security.SslStream(tcp.GetStream(), false, (fun _ _ _ _ -> true))
                        do! ssl.AuthenticateAsClientAsync(host)
                        return ssl :> Stream
                    else
                        return tcp.GetStream() :> Stream
                }
            let key = Convert.ToBase64String(RandomNumberGenerator.GetBytes 16)
            let request = StringBuilder()
            request.Append($"GET /cable HTTP/1.1\r\nHost: {authority}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n") |> ignore
            for (name, value) in headers do
                request.Append($"{name}: {value}\r\n") |> ignore
            request.Append "\r\n" |> ignore
            let bytes = Encoding.ASCII.GetBytes(request.ToString())
            do! stream.WriteAsync(bytes, 0, bytes.Length)
            let! response = readHttp stream
            if response.Status = 101 then
                let accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")))
                if response.Header "Sec-WebSocket-Accept" <> Some accept then failwith "wrong Sec-WebSocket-Accept"
                return Choice1Of2(new Client(tcp, stream, response.Header "Sec-WebSocket-Protocol", response.Header "Sec-WebSocket-Extensions"))
            else
                tcp.Dispose()
                return Choice2Of2 response
        }

    member _.SendBytes(bytes: byte[]) : Task =
        task {
            do! stream.WriteAsync(bytes, 0, bytes.Length)
            do! stream.FlushAsync()
        }

    member this.SendText(text: string) : Task =
        this.SendBytes(clientFrame 0x1uy true false (Encoding.UTF8.GetBytes text))

    member this.Send(command: Value) : Task = this.SendText(Json.generate command)

    member this.Subscribe(identifier: string) : Task =
        this.Send(object' [ "command", Value.String "subscribe"; "identifier", Value.String identifier ])

    member this.Unsubscribe(identifier: string) : Task =
        this.Send(object' [ "command", Value.String "unsubscribe"; "identifier", Value.String identifier ])

    member this.Perform(identifier: string, data: Value) : Task =
        this.Send(object' [ "command", Value.String "message"; "identifier", Value.String identifier; "data", Value.String(Json.generate data) ])

    member private this.ReadFrame() : Task<Got> =
        task {
            if closed then
                return Ended
            else
                try
                    let head = Array.zeroCreate<byte> 8
                    let mutable result = ValueNone
                    while result.IsNone do
                        do! readExact stream head 2
                        let fin, opcode = head[0] &&& 0x80uy <> 0uy, head[0] &&& 0x0fuy
                        if head[1] &&& 0x80uy <> 0uy then failwith "server frames are unmasked"
                        let! length =
                            task {
                                match head[1] &&& 0x7fuy with
                                | 126uy ->
                                    do! readExact stream head 2
                                    return (int head[0] <<< 8) ||| int head[1]
                                | 127uy ->
                                    do! readExact stream head 8
                                    return int (Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(ReadOnlySpan head))
                                | n -> return int n
                            }
                        let payload = Array.zeroCreate<byte> length
                        if length > 0 then do! readExact stream payload length
                        ignore fin
                        match opcode with
                        | 0x1uy ->
                            result <- ValueSome(Said(Encoding.UTF8.GetString payload))
                        | 0x8uy ->
                            closed <- true
                            // Complete the closing handshake, as a WebSocket library does on reading a Close.
                            try
                                do! this.SendBytes(clientFrame 0x8uy true false payload)
                            with _ ->
                                ()
                            result <-
                                ValueSome(
                                    Closed(
                                        if payload.Length >= 2 then
                                            Some((uint16 payload[0] <<< 8) ||| uint16 payload[1], Encoding.UTF8.GetString(payload, 2, payload.Length - 2))
                                        else
                                            None
                                    )
                                )
                        | 0x9uy
                        | 0xauy -> ()
                        | other -> failwith $"unexpected opcode {other}"
                    return result.Value
                with
                | :? EndOfStreamException
                | :? IOException ->
                    closed <- true
                    return Ended
                | :? OperationCanceledException -> return failwith "no frame within 5s"
        }

    /// The next frame. A read that was given up on (see `AssertSilent`) is picked up by the next call,
    /// so the socket is only ever read by one.
    member this.NextIncludingPings() : Task<Got> =
        lock gate (fun () ->
            if isNull (box pendingRaw) then
                let read =
                    task {
                        let! got = this.ReadFrame()
                        lock gate (fun () -> pendingRaw <- Unchecked.defaultof<_>)
                        return got
                    }
                // One that finished already cleared nothing yet to clear.
                if not read.IsCompleted then pendingRaw <- read
                read
            else
                pendingRaw)

    /// The next frame, skipping pings.
    member this.Next() : Task<Got> =
        lock gate (fun () ->
            if isNull (box pendingNext) then
                let read =
                    task {
                        let mutable result = ValueNone
                        while result.IsNone do
                            match! this.NextIncludingPings() with
                            | Said text when text.StartsWith("{\"type\":\"ping\"", StringComparison.Ordinal) -> ()
                            | got -> result <- ValueSome got
                        lock gate (fun () -> pendingNext <- Unchecked.defaultof<_>)
                        return result.Value
                    }
                if not read.IsCompleted then pendingNext <- read
                read
            else
                pendingNext)

    member this.NextText() : Task<string> =
        task {
            match! this.Next() with
            | Said text -> return text
            | other -> return failwith $"expected a text frame, got {other}"
        }

    /// Asserts nothing arrives (pings aside) for a little while.
    member this.AssertSilent() : Task =
        task {
            let pending = this.Next()
            let! finished = Task.WhenAny(pending, Task.Delay 200)
            if obj.ReferenceEquals(finished, pending) then failwith $"expected no frame, got {pending.Result}" 
        }

    interface IDisposable with
        member _.Dispose() = tcp.Dispose()

let connectWith (server: TestServer) (userId: uint64 option) (origin: string) : Task<Client> =
    task {
        let headers =
            [ "Origin", origin
              "Sec-WebSocket-Protocol", "actioncable-v1-json, actioncable-unsupported" ]
            @ (match userId with
               | Some id -> [ "Cookie", $"session_token={id}" ]
               | None -> [])
        match! Client.Open(server.Authority, headers) with
        | Choice1Of2 client -> return client
        | Choice2Of2 response -> return failwith $"upgrade refused: {response.Status}"
    }

let connect (server: TestServer) (userId: uint64) : Task<Client> = connectWith server (Some userId) server.Origin

/// A client that will not read (L1, L2): its receive buffer is small and fixed, so the kernel's absorption of what the server
/// writes is bounded and the test does not depend on how far macOS autotunes loopback buffers.
let connectNonReading (server: TestServer) (userId: uint64) : Task<Client> =
    task {
        let headers =
            [ "Origin", server.Origin
              "Sec-WebSocket-Protocol", "actioncable-v1-json, actioncable-unsupported"
              "Cookie", $"session_token={userId}" ]
        match! Client.Open(server.Authority, headers, false, 4096) with
        | Choice1Of2 client -> return client
        | Choice2Of2 response -> return failwith $"upgrade refused: {response.Status}"
    }

/// A plain GET to `/cable`, for what a non-WebSocket request is answered with.
let httpGet (authority: string) (headers: (string * string) list) : Task<Http> =
    task {
        use tcp = new TcpClient()
        let host, port =
            let i = authority.LastIndexOf ':'
            authority.Substring(0, i), int (authority.Substring(i + 1))
        do! tcp.ConnectAsync(host, port)
        let stream = tcp.GetStream()
        let request =
            $"GET /cable HTTP/1.1\r\nHost: {authority}\r\nConnection: close\r\n"
            + String.Join("", headers |> List.map (fun (k, v) -> $"{k}: {v}\r\n"))
            + "\r\n"
        let bytes = Encoding.ASCII.GetBytes request
        do! stream.WriteAsync(bytes, 0, bytes.Length)
        return! readHttp stream
    }
