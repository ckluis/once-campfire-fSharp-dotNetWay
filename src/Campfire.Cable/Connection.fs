// Phase 3 (plans/design-cable.md C1, C6, C7, C12, C13, C17): one WebSocket connection on ASP.NET Core's WebSocket
// middleware. `ActionCable::Connection::Base` and `Connection::Subscriptions` as the port had them, with the
// transport's work (handshake, framing, masking, control frames) left to the framework.
//
// Every send of a connection goes through its writer loop, which drains one bounded queue into `WebSocket.SendAsync`
// (one Action Cable frame per message): welcome, confirmations, broadcasts, pings, disconnects and the close frame.
// Broadcasters only ever `TryWrite`; a full queue is a slow consumer and aborts the connection (C7). Commands run in
// arrival order, one at a time, on the read loop.
namespace Campfire.Cable

open System
open System.Buffers
open System.Collections.Generic
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// What a connection needs of its server: the registries, the channels, the settings.
[<Sealed>]
type internal ServerCore<'U>
    (
        options: CableOptions,
        channels: ChannelRegistry<'U>,
        authenticate: HttpContext -> Task<'U option>,
        recheck: HttpContext -> IServiceProvider -> Task<bool>,
        identify: 'U -> string,
        scopes: IServiceScopeFactory,
        logger: ILogger
    ) =
    let streams = StreamRegistry()
    let mutable stopping = 0

    member _.Options: CableOptions = options
    member _.Channels: ChannelRegistry<'U> = channels
    /// Each command, and each `unsubscribed` on close, gets a scope of its own (C12).
    member _.Scopes: IServiceScopeFactory = scopes
    /// Set when the host begins stopping: a connection that registers after `Restart` has walked the index closes itself.
    member _.Stopping: bool = Volatile.Read(&stopping) <> 0
    member _.BeginStopping() : unit = Volatile.Write(&stopping, 1)
    /// A test hook between a connection's registration and its re-check (A3).
    member val AfterRegistered: unit -> Task = (fun () -> Task.CompletedTask) with get, set
    member _.Logger: ILogger = logger
    member _.Streams: StreamRegistry = streams
    /// What a `Subscription` hands its channel: the server that owns this core (set by `ServerBuilder.build`).
    member val Broadcaster: ICableBroadcaster = Unchecked.defaultof<ICableBroadcaster> with get, set
    member val Connections = ConnectionIndex()
    member _.Authenticate(ctx: HttpContext) : Task<'U option> = authenticate ctx
    /// C10: the session looked up again, in a scope of its own.
    member _.Recheck(ctx: HttpContext, services: IServiceProvider) : Task<bool> = recheck ctx services
    member _.Identify(user: 'U) : string = identify user

    /// `ActionCable.server.broadcast(broadcasting, message)`; the payload is already encoded.
    member _.Broadcast(broadcasting: string, payload: Payload) : int =
        if logger.IsEnabled LogLevel.Debug then logger.LogDebug("[ActionCable] Broadcasting {Broadcasting}", broadcasting)
        streams.Broadcast(broadcasting, payload)

    interface ICableBroadcaster with
        member this.Broadcast(broadcasting, payload) = this.Broadcast(broadcasting, payload)

module private Parse =
    /// The JSON text as a document, or None when it is not JSON.
    let document (bytes: ReadOnlyMemory<byte>) : JsonDocument option =
        try
            Some(JsonDocument.Parse bytes)
        with :? JsonException ->
            None

    let text (element: JsonElement) (name: string) : string voption =
        match element.TryGetProperty name with
        | true, value when value.ValueKind = JsonValueKind.String ->
            match value.GetString() with
            | null -> ValueNone
            | text -> ValueSome text
        | _ -> ValueNone

type private Entry<'U> =
    { Channel: Channel<'U>
      Sub: Subscription<'U>
      Subscriber: StreamSubscriber
      /// How many times each broadcasting is registered (a stream started twice is delivered twice).
      Registered: Dictionary<string, int> }

/// Runs `callback`, treating an exception as Rails' rescued error: `Error message`.
module private Safely =
    let run (callback: unit -> Task<'T>) : Task<Result<'T, string>> =
        task {
            try
                let! value = callback ()
                return Ok value
            with e ->
                return Error e.Message
        }

    let runUnit (callback: unit -> Task) : Task<Result<unit, string>> =
        run (fun () ->
            task {
                do! callback ()
            })

[<Sealed>]
type internal CableConnection<'U>(ws: WebSocket, ctx: HttpContext, user: 'U voption, core: ServerCore<'U>) =
    let options = core.Options
    let logger = core.Logger
    let queue =
        Channels.Channel.CreateBounded<ReadOnlyMemory<byte>>(
            Channels.BoundedChannelOptions(options.QueueCapacity, FullMode = Channels.BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false)
        )
    /// Keyed by the raw identifier string, in subscription order (a Ruby hash).
    let entries = List<Entry<'U>>()
    let transmissions = List<ReadOnlyMemory<byte>>()
    let ops = List<StreamOp>()
    /// Frames a broadcast delivered while a command runs: they follow the command's own frames, as a broadcast
    /// reaches a Rails connection after the command that caused it (pub/sub is asynchronous there).
    let held = List<ReadOnlyMemory<byte>>()
    let mutable inCommand = 0
    let mutable closing = 0
    let mutable aborted = 0
    let mutable closeStatus = WebSocketCloseStatus.NormalClosure
    let mutable identifier: string | null = null

    member private _.Position(requested: string) : int =
        entries.FindIndex(fun entry -> String.Equals(entry.Sub.Identifier, requested))

    /// Cuts the connection: no further frame is sent, the socket is torn down, and the read loop ends.
    member private _.Abort() : unit =
        if Interlocked.Exchange(&aborted, 1) = 0 then
            queue.Writer.TryComplete() |> ignore
            // A broadcaster may hold this connection from a registry snapshot taken before it left; once the request has
            // ended its HttpContext is gone, and the broadcaster (an HTTP request of its own) must not fail on it.
            try
                ctx.Abort()
            with :? ObjectDisposedException ->
                ()

    /// A refused write: full is a slow consumer (abort), closed is a connection going away on its own. Never called while
    /// `held` is locked: `Abort` runs `ctx.Abort`, whose continuations may run inline and come back into this connection.
    member private this.Refused() : unit =
        if Volatile.Read(&closing) = 0 then this.Abort()

    member private this.Offer(frame: ReadOnlyMemory<byte>) : bool =
        if queue.Writer.TryWrite frame then true
        else
            this.Refused()
            false

    /// The connection's own frames go straight to the queue, ahead of what a command held back.
    member private this.Enqueue(frame: ReadOnlyMemory<byte>) : unit =
        if Volatile.Read(&aborted) = 0 then this.Offer frame |> ignore

    /// Runs a command with broadcasts held back until its own frames are queued.
    member private this.InCommand(work: unit -> Task) : Task =
        task {
            Volatile.Write(&inCommand, 1)
            try
                do! work ()
            finally
                // The flag drops only after the held frames are queued: a broadcaster that sees it down (outside the lock)
                // must not overtake a frame of the same stream still being flushed.
                let refused =
                    lock held (fun () ->
                        let mutable refused = false
                        for frame in held do
                            if not (queue.Writer.TryWrite frame) then refused <- true
                        held.Clear()
                        Volatile.Write(&inCommand, 0)
                        refused)
                // Abort only after leaving the lock.
                if refused then this.Refused()
        }

    /// Runs one command (or one `unsubscribed` on close) in a DI scope of its own: the connection outlives any sensible
    /// `DbContext` (C12).
    member private _.WithScope(work: IServiceProvider -> Task) : Task =
        task {
            use scope = core.Scopes.CreateAsyncScope()
            do! work scope.ServiceProvider
        }

    member private _.WithScopeResult(work: IServiceProvider -> Task<bool>) : Task<bool> =
        task {
            use scope = core.Scopes.CreateAsyncScope()
            return! work scope.ServiceProvider
        }

    /// Close after what is queued: the status goes out with the close frame.
    member private _.BeginClose(status: WebSocketCloseStatus) : unit =
        if Interlocked.Exchange(&closing, 1) = 0 then
            closeStatus <- status
            queue.Writer.TryComplete() |> ignore

    interface IOutbound with
        member this.TryWrite(frame: ReadOnlyMemory<byte>) : bool =
            if Volatile.Read(&aborted) <> 0 then false
            elif Volatile.Read(&inCommand) = 0 then this.Offer frame
            else
                // 0: written or held; 1: the queue refused it; 2: the held backlog is over capacity. Either refusal aborts
                // after the lock is released (see `Refused`).
                let outcome =
                    lock held (fun () ->
                        if inCommand = 0 then
                            if queue.Writer.TryWrite frame then 0 else 1
                        elif held.Count < options.QueueCapacity then
                            held.Add frame
                            0
                        else
                            2)
                match outcome with
                | 0 -> true
                | 1 ->
                    this.Refused()
                    false
                | _ ->
                    this.Abort()
                    false

        member this.Close(reason: DisconnectReason option, reconnect: bool) : unit =
            if Interlocked.Exchange(&closing, 1) = 0 then
                closeStatus <- WebSocketCloseStatus.NormalClosure
                if queue.Writer.TryWrite(Frames.disconnect reason reconnect) then queue.Writer.TryComplete() |> ignore
                else this.Abort()

    /// Drains the queue into the socket, one frame per message; then sends the close frame. A send that takes longer
    /// than `SendTimeout` is cancelled, which aborts the socket (S4b).
    member private this.WriteLoop() : Task =
        task {
            use sending = new CancellationTokenSource()
            try
                let reader = queue.Reader
                let mutable more = true
                while more do
                    let! available = reader.WaitToReadAsync()
                    if not available then
                        more <- false
                    else
                        let mutable frame = ReadOnlyMemory<byte>.Empty
                        while reader.TryRead(&frame) do
                            sending.CancelAfter options.SendTimeout
                            do! ws.SendAsync(frame, WebSocketMessageType.Text, true, sending.Token)
                            if not (sending.TryReset()) then raise (OperationCanceledException())
                if Volatile.Read(&aborted) = 0 && (ws.State = WebSocketState.Open || ws.State = WebSocketState.CloseReceived) then
                    sending.CancelAfter options.SendTimeout
                    do! ws.CloseOutputAsync(closeStatus, "", sending.Token)
            with e ->
                if Volatile.Read(&aborted) = 0 && Volatile.Read(&closing) = 0 then logger.LogDebug(e, "Cable writer ended")
                this.Abort()
        }

    member private this.Flush() : unit =
        for frame in transmissions do
            this.Enqueue frame
        transmissions.Clear()

    /// Starts and stops the streams the last callback asked for, once its own frames are queued (C13).
    member private _.ApplyOps(entry: Entry<'U>) : unit =
        for op in ops do
            match op with
            | Start broadcasting ->
                entry.Registered[broadcasting] <- (match entry.Registered.TryGetValue broadcasting with
                                                   | true, n -> n + 1
                                                   | _ -> 1)
                core.Streams.Add(broadcasting, entry.Subscriber)
            | Stop broadcasting ->
                match entry.Registered.TryGetValue broadcasting with
                | true, n ->
                    if n = 1 then entry.Registered.Remove broadcasting |> ignore else entry.Registered[broadcasting] <- n - 1
                    core.Streams.Remove(broadcasting, entry.Subscriber)
                | _ -> ()
        ops.Clear()

    member private this.Collect(entry: Entry<'U>) : unit =
        entry.Sub.DrainTransmissions transmissions
        entry.Sub.DrainOps ops

    /// `Subscriptions#remove_subscription` -> `Channel::Base#unsubscribe_from_channel`.
    member private this.RemoveSubscription(index: int, services: IServiceProvider) : Task =
        task {
            let entry = entries[index]
            entries.RemoveAt index
            entry.Sub.Services <- services
            entry.Sub.Unsubscribed <- true
            match! Safely.runUnit (fun () -> entry.Channel.Unsubscribed entry.Sub) with
            | Error error -> logger.LogError("Could not execute command: {Error}", error)
            | Ok() -> ()
            entry.Sub.StopAllStreams()
            this.Collect entry
            this.Flush()
            this.ApplyOps entry
        }

    /// `Channel::Base#subscribe_to_channel`: the callback's transmissions, then the confirmation, then the streams.
    member private this.SubscribeToChannel(entry: Entry<'U>, services: IServiceProvider) : Task =
        task {
            let! result = Safely.runUnit (fun () -> entry.Channel.Subscribed entry.Sub)
            this.Collect entry
            this.Flush()
            match result with
            | Error error ->
                logger.LogError("Could not execute command {Identifier}: {Error}", entry.Sub.Identifier, error)
                this.ApplyOps entry
            | Ok() ->
                if entry.Sub.Rejected then
                    let index = this.Position entry.Sub.Identifier
                    if index >= 0 then do! this.RemoveSubscription(index, services)
                    this.Enqueue(Frames.rejection entry.Sub.IdentifierJson)
                else
                    this.Enqueue(Frames.confirmation entry.Sub.IdentifierJson)
                    this.ApplyOps entry
        }

    /// `Subscriptions#add`. A repeated identifier (byte for byte) is ignored without a reply.
    member private this.Add(command: JsonElement, services: IServiceProvider) : Task =
        task {
            match Parse.text command "identifier" with
            | ValueNone -> logger.LogError "Could not execute command: missing identifier"
            | ValueSome identifier ->
                let parameters =
                    match Parse.document (ReadOnlyMemory(Encoding.UTF8.GetBytes identifier)) with
                    | Some document ->
                        use document = document
                        if document.RootElement.ValueKind = JsonValueKind.Object then ValueSome(document.RootElement.Clone()) else ValueNone
                    | None -> ValueNone
                match parameters with
                | ValueNone -> logger.LogError("Could not execute command: invalid identifier {Identifier}", identifier)
                | ValueSome parameters ->
                    if this.Position identifier < 0 then
                        // Bounds on what one socket can make the server hold (Rails has none). A page
                        // subscribes to six channels with identifiers of a few hundred bytes.
                        if entries.Count >= options.MaxSubscriptions || Encoding.UTF8.GetByteCount identifier > options.MaxIdentifierBytes then
                            logger.LogError("Could not execute command: subscription limit reached ({Subscriptions})", entries.Count)
                        else
                            let requested = defaultArg (Parse.text parameters "channel" |> ValueOption.toOption) ""
                            match ChannelRegistry.resolve core.Channels requested with
                            | ValueNone -> logger.LogError("Subscription class not found {Channel}", requested)
                            | ValueSome(struct (className, factory)) ->
                                let json = Frames.identifierJson identifier
                                let sub =
                                    Subscription<'U>(core.Broadcaster, services, className, identifier, json, parameters, user.Value)
                                let entry =
                                    { Channel = factory ()
                                      Sub = sub
                                      Subscriber = { Outbound = this; Identifier = identifier; IdentifierJson = json }
                                      Registered = Dictionary<string, int>(StringComparer.Ordinal) }
                                entries.Add entry
                                do! this.SubscribeToChannel(entry, services)
        }

    /// `Subscriptions#remove`: no reply either way.
    member private this.Remove(command: JsonElement, services: IServiceProvider) : Task =
        task {
            match Parse.text command "identifier" with
            | ValueSome identifier when this.Position identifier >= 0 -> do! this.RemoveSubscription(this.Position identifier, services)
            | _ -> logger.LogError "Unable to find subscription with identifier"
        }

    /// `Subscriptions#perform_action` -> `Channel::Base#perform_action`.
    member private this.PerformAction(command: JsonElement, services: IServiceProvider) : Task =
        task {
            let index =
                match Parse.text command "identifier" with
                | ValueSome identifier -> this.Position identifier
                | ValueNone -> -1
            if index < 0 then
                logger.LogError "Unable to find subscription with identifier"
            else
                // The data is itself JSON in a string; it is parsed for this call only.
                let data =
                    match Parse.text command "data" with
                    | ValueSome text ->
                        match Parse.document (ReadOnlyMemory(Encoding.UTF8.GetBytes text)) with
                        | Some document when document.RootElement.ValueKind = JsonValueKind.Object -> Some document
                        | Some document ->
                            document.Dispose()
                            None
                        | None -> None
                    | ValueNone -> None
                match data with
                | None -> logger.LogError "Could not execute command: invalid data"
                | Some document ->
                    use document = document
                    let payload = document.RootElement
                    let action =
                        match payload.TryGetProperty "action" with
                        | true, action ->
                            match action.ValueKind with
                            | JsonValueKind.Null -> Some "receive"
                            | JsonValueKind.String ->
                                let text = action.GetString() |> nonNull
                                if String.IsNullOrWhiteSpace text then Some "receive" else Some text
                            | _ -> None
                        | _ -> Some "receive"
                    match action with
                    | None -> logger.LogError "Could not execute command: invalid action"
                    | Some action ->
                        let entry = entries[index]
                        entry.Sub.Services <- services
                        let! result = Safely.run (fun () -> entry.Channel.Perform action payload entry.Sub)
                        this.Collect entry
                        this.Flush()
                        this.ApplyOps entry
                        match result with
                        | Ok true -> ()
                        | Ok false -> logger.LogError("Unable to process {Action}", action)
                        | Error error -> logger.LogError("Could not execute command {Action}: {Error}", action, error)
        }

    /// `Subscriptions#execute_command`. Anything malformed raises in Rails, which is logged and
    /// otherwise ignored; the connection stays open.
    member private this.Dispatch(bytes: ReadOnlyMemory<byte>) : Task =
        task {
            // `dispatch_websocket_message` runs only `if websocket.alive?`: once the server has begun closing (a remote
            // disconnect, a restart, an unauthorized connection), what the client still sends is not processed.
            if Volatile.Read(&closing) <> 0 then
                logger.LogError("Ignoring message processed after the WebSocket was closed: {Message}", Encoding.UTF8.GetString bytes.Span)
            else
                match Parse.document bytes with
                | Some document ->
                    use document = document
                    let root = document.RootElement
                    if root.ValueKind <> JsonValueKind.Object then
                        logger.LogError("Could not execute command {Message}", Encoding.UTF8.GetString bytes.Span)
                    else
                        match Parse.text root "command" with
                        | ValueSome "subscribe" -> do! this.InCommand(fun () -> this.WithScope(fun services -> this.Add(root, services)))
                        | ValueSome "unsubscribe" -> do! this.InCommand(fun () -> this.WithScope(fun services -> this.Remove(root, services)))
                        | ValueSome "message" -> do! this.InCommand(fun () -> this.WithScope(fun services -> this.PerformAction(root, services)))
                        | _ -> logger.LogError("Received unrecognized command {Message}", Encoding.UTF8.GetString bytes.Span)
                | None -> logger.LogError("Could not execute command {Message}", Encoding.UTF8.GetString bytes.Span)
        }

    /// Reads messages until the socket closes or fails. An idle connection holds no receive buffer: each message is
    /// announced by a zero-byte read (C17).
    member private this.ReadLoop() : Task =
        task {
            let ct = ctx.RequestAborted
            try
                let mutable running = true
                while running do
                    let! announced = ws.ReceiveAsync(Memory<byte>.Empty, ct)
                    if announced.MessageType = WebSocketMessageType.Close then
                        this.BeginClose(if ws.CloseStatus.HasValue then ws.CloseStatus.Value else WebSocketCloseStatus.Empty)
                        running <- false
                    else
                        let mutable buffer = ArrayPool<byte>.Shared.Rent 4096
                        try
                            let mutable total = 0
                            // An empty message ends at its announcement; reading on would take the next message (or the close) as its body.
                            let mutable complete = announced.EndOfMessage
                            let mutable tooBig = false
                            let mutable closed = false
                            while not complete && not tooBig && not closed do
                                if total = buffer.Length then
                                    let bigger = ArrayPool<byte>.Shared.Rent(min (buffer.Length * 2) (options.MaxMessageBytes + 1))
                                    Buffer.BlockCopy(buffer, 0, bigger, 0, total)
                                    ArrayPool<byte>.Shared.Return buffer
                                    buffer <- bigger
                                let! part = ws.ReceiveAsync(Memory<byte>(buffer, total, buffer.Length - total), ct)
                                // A close may arrive between the fragments of a message: the message is dropped, the close echoed.
                                if part.MessageType = WebSocketMessageType.Close then
                                    closed <- true
                                else
                                    total <- total + part.Count
                                    complete <- part.EndOfMessage
                                    if total > options.MaxMessageBytes then tooBig <- true
                            if closed then
                                this.BeginClose(if ws.CloseStatus.HasValue then ws.CloseStatus.Value else WebSocketCloseStatus.Empty)
                                running <- false
                            elif tooBig then
                                logger.LogError("Could not execute command: message too big")
                                this.BeginClose WebSocketCloseStatus.MessageTooBig
                                running <- false
                            elif announced.MessageType = WebSocketMessageType.Text then
                                do! this.Dispatch(ReadOnlyMemory(buffer, 0, total))
                            else
                                logger.LogError "Couldn't handle non-string message: Array"
                        finally
                            ArrayPool<byte>.Shared.Return buffer
            with e ->
                // A cut connection, a protocol error the framework answered with its own close, or a cancelled read.
                if Volatile.Read(&aborted) = 0 && Volatile.Read(&closing) = 0 && not (e :? OperationCanceledException) then
                    logger.LogDebug(e, "Cable read ended")
        }

    /// Welcome, register, then read commands until the socket closes; then unsubscribe everything and leave the registries.
    member this.RunAsync() : Task =
        task {
            let writer = this.WriteLoop()
            try
                match user with
                | ValueNone ->
                    // `Connection::Base#respond_to_invalid_request` for an unauthorized connection.
                    logger.LogError "An unauthorized connection attempt was rejected"
                    (this :> IOutbound).Close(Some Unauthorized, false)
                | ValueSome current ->
                    this.Enqueue Frames.welcome
                    identifier <- core.Identify current
                    core.Connections.Add(this, identifier |> nonNull)
                    // `Restart` may have walked the index just before this connection joined it: the flag is set first, so look.
                    if core.Stopping then
                        (this :> IOutbound).Close(Some ServerRestart, true)
                    else
                        do! core.AfterRegistered()
                        // A ban or sign-out that disconnected this user between the first lookup and the registration
                        // would go unheard, so look again now that a remote disconnect would be heard (Rails has this gap).
                        // The lookup is the session's own, in a scope of its own: not a second `AuthenticateAsync`, which
                        // would answer with the first result (C10).
                        let! valid = this.WithScopeResult(fun services -> core.Recheck(ctx, services))
                        if not valid then
                            logger.LogError "An unauthorized connection attempt was rejected"
                            (this :> IOutbound).Close(Some Unauthorized, false)
                let reading = this.ReadLoop()
                // Once the writer has sent the close frame, the client gets `CloseTimeout` to answer it.
                let! first = Task.WhenAny(reading, writer)
                if not (obj.ReferenceEquals(first, reading)) then
                    try
                        do! reading.WaitAsync options.CloseTimeout
                    with :? TimeoutException ->
                        ctx.Abort()
                do! reading
            with e ->
                logger.LogError(e, "Cable connection failed")
            // `Connection::Base#handle_close`: unsubscribe everything, each in turn.
            if not (isNull identifier) then core.Connections.Remove(this, identifier |> nonNull)
            try
                do!
                    this.InCommand(fun () ->
                        task {
                            while entries.Count > 0 do
                                do! this.WithScope(fun services -> this.RemoveSubscription(0, services))
                        })
            with e ->
                logger.LogError(e, "Cable unsubscribe failed")
            // Gone: a late `TryWrite` or `Close` (from a snapshot taken before the connection left the registries) finds the
            // queue closed and returns, instead of counting as a slow consumer and aborting a finished request.
            Volatile.Write(&closing, 1)
            queue.Writer.TryComplete() |> ignore
            try
                do! writer.WaitAsync options.CloseTimeout
            with :? TimeoutException ->
                this.Abort()
                do! writer
        }
