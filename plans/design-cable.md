# Design note: the Cable layer on ASP.NET Core WebSockets (Phase 3)

Status: design, 2026-10-13, before Phase 3 builds. Author: the Opus designer (plan section 5). Binding for the Phase 3 builders
together with `plans/dotnetway.md` sections 3-5, D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`) and
`plans/design-kit-auth.md` (Phase 2, whose cookie scheme this phase authenticates with). Where this note and plan section 2.5
disagree, this note wins: it is later and rests on the spikes in section 2.

Scope: the `/cable` endpoint, the Action Cable protocol, the per-connection outbound queue and fan-out, the heartbeat, connection
authentication, remote disconnects and restarts, how the app publishes broadcasts, and the channel API the app's channels
(`src/Campfire.App/Channels/`) are written against. Out of scope: what the channels do (they keep Rails' behaviour, now over EF from
Phase 1), the partials broadcasts carry (Phase 4), TLS and HTTP/2 (Phase 7).

**Preconditions.** Phase 1 closed: net11.0 on the .NET 11 SDK, `CampfireDb` (EF Core) in DI, `Writes.run` (the process-wide write
semaphore of plan 2.1), a `TimeProvider` carrying the harness clock. Phase 2 closed: `Services.add`/`Services.pipeline` and the
hosting seam (`configureServices` reaching Front's builder), the `Session` cookie scheme with `SessionCookie.validatePrincipal`,
`Current` in `HttpContext.Features`, `UseForwardedHeaders` and the `assume_ssl` middleware ahead of routing. If either left a piece
out, the first unit that needs it stops and reports rather than building a stand-in.

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity forces it), with the reason.

| # | Decision | Reason |
|---|---|---|
| C1 | **Transport: `app.UseWebSockets()` and `HttpContext.WebSockets.AcceptWebSocketAsync(WebSocketAcceptContext(SubProtocol = negotiated, DangerousEnableCompression = false, KeepAliveInterval = TimeSpan.Zero))`**, the shared framework's middleware and `ManagedWebSocket`. A request is a Cable upgrade when `ctx.WebSockets.IsWebSocketRequest`. | D4. The framework does the handshake, framing, masking, UTF-8 validation of text messages, control frames (pongs to client pings) and close codes; `Socket.fs` (402 lines of RFC 6455 and RFC 7692) goes. `IsWebSocketRequest` also covers HTTP/2 WebSockets (RFC 8441, extended CONNECT), which the port's GET-plus-`Upgrade`-header check would 404 once Phase 7 serves HTTP/2 over TLS. |
| C2 | **Rejected: SignalR, and `MapConnectionHandler` (`Microsoft.AspNetCore.Http.Connections`).** | SignalR's handshake (`{"protocol":"json","version":1}` then `0x1E`) is fixed in `HubConnectionHandler`; Rails' JS client sends nothing and waits for `welcome`. The connections layer's WebSocket transport turns messages into a byte pipe and sends whatever one pipe read holds as one message, so two queued frames could leave as one WebSocket message, which `JSON.parse` in the Action Cable client rejects. The design below takes SignalR's server shape instead: groups as a `ConcurrentDictionary` of connection sets, one writer per connection, a DI scope per invocation. |
| C3 | **No compression.** `DangerousEnableCompression = false`; a client's `permessage-deflate` offer gets no `Sec-WebSocket-Extensions` (spike S1). `UseResponseCompression` in the pipeline does not touch the upgraded stream (S3). | Rails negotiates none (`websocket-driver` without extensions), so this is back to Rails. The framework's compression deflates per socket, per send; the port's saving came from one deflate per broadcast shared by every subscriber, which the framework cannot do. The Rust/port "permessage-deflate" known difference goes. |
| C4 | **No WebSocket-level keep-alive pings** (`KeepAliveInterval = Zero`). Liveness is Action Cable's own `ping` frame every 3 s (C8) plus the slow-consumer rules (C7). | Rails sends no protocol pings. The 3-s application ping already keeps NATs open and is what the JS `ConnectionMonitor` watches. |
| C5 | **Endpoint rules stay Rails'**: anything that is not a WebSocket request, or whose `Origin` is not allowed, gets `404`, `text/plain; charset=utf-8`, `Page not found` before any upgrade (S2). The origin check is `allow_request_origin?`: off with `DisableRequestForgeryProtection`; else `Origin = Request.Scheme + "://" + Request.Host`, or `Origin` in `AllowedRequestOrigins`. The subprotocol is the first of the client's `WebSocketRequestedProtocols` in `[ "actioncable-v1-json"; "actioncable-unsupported" ]`, none if none. | Parity forces the 404 page and the negotiation order (websocket-driver walks the client's list). `WebSocketOptions.AllowedOrigins` answers 403 with a static list, so it can't express either. Scheme and host come from the framework's request after Phase 2's `UseForwardedHeaders` and `assume_ssl`, so the port's `X-Forwarded-*` sniffing (`Server.SslRequest`) goes. |
| C6 | **Fan-out: one bounded `Channel<ReadOnlyMemory<byte>>` per connection** (`System.Threading.Channels`, capacity `QueueCapacity` = 256, `FullMode = Wait`, `SingleReader = true`, `SingleWriter = false`), written only with `TryWrite`, drained by one writer loop per connection into `WebSocket.SendAsync` (one Action Cable frame per WebSocket message, `endOfMessage = true`). **Every send of a connection goes through that loop**: welcome, confirmations, broadcasts, pings, disconnects, and the close frame (sent when the channel is completed and drained). A **stream registry** maps broadcasting -> `ImmutableArray<Subscriber>` sorted by identifier; a broadcast encodes its payload once, builds one frame per distinct identifier (Turbo subscribers of one stream share their identifier, so usually one frame) and `TryWrite`s it to each subscriber. | Plan 2.5. `ManagedWebSocket` allows one outstanding send (close included), so a single writer is the framework's own contract, not a choice. A broadcaster (an HTTP request that just committed) never waits on a socket: `TryWrite` is non-blocking. Frames are immutable `byte[]` shared by every connection they go to. |
| C7 | **Slow consumers are cut, not waited for.** (a) A `TryWrite` that finds the queue full aborts that connection (`HttpContext.Abort()`; no disconnect frame, which could only queue behind the backlog). (b) A `SendAsync` that has not completed after `SendTimeout` = 30 s is cancelled, which aborts the socket (S4b). Either way the read loop ends, the connection unsubscribes everything (`unsubscribed` callbacks run, Presence marks the membership disconnected) and leaves the registries; the JS client reconnects through its `ConnectionMonitor`. | Rails buffers without bound (Ruby `websocket-driver`), which parity cannot keep without unbounded memory per connection. The plan's class B entry: "slow Cable clients cut at 256 queued frames"; the 30-s rule is Rust's. Spike S4: a client that never reads absorbed 70 KB to 580 KB in the socket and Kestrel's pipe (macOS loopback buffers vary) before sends blocked, then 256 frames later the `TryWrite` failed and the abort tore everything down in the same millisecond. S4b: with less than 256 frames behind, the 30-s timer (1.5 s in the spike) aborted it. |
| C8 | **Heartbeat: `CableHeartbeat`, a `BackgroundService`** with `new PeriodicTimer(BeatInterval = 3 s, timeProvider)`, calling `CableServer.Beat()`, which builds `{"type":"ping","message":<unix seconds>}` once from `TimeProvider.GetUtcNow()` and `TryWrite`s it to every open connection. | The framework idiom the decisions list names. One timer and one frame per beat for all connections, as Rails' `setup_heartbeat_timer`. The `TimeProvider` overload lets tests step it with `FakeTimeProvider`. The message is the app clock's seconds, as Rails' `Time.now.to_i` is under the reference's libfaketime; the harness drops pings (`parity/capture/network.ts`). It starts at boot, not on the first request (class A). |
| C9 | **Authentication is the Phase 2 cookie scheme**: before accepting, `ctx.AuthenticateAsync Schemes.Session`; success yields `Current.user ctx`, kept as `CableUser = { Id; Name }` (`identified_by :current_user`, loaded once). `SessionCookie.validatePrincipal` skips `Session#resume` (no `sessions` write, no `ShouldRenew`) when `ctx.WebSockets.IsWebSocketRequest`. Failure: accept anyway, send `{"type":"disconnect","reason":"unauthorized","reconnect":false}`, close 1000. | Rails' `ApplicationCable::Connection` includes only `Authentication::SessionLookup` (`Session.find_by(token: cookies.signed[:session_token])&.user`), no `resume_session`, no cookie re-sign; the golden "unauthenticated" session is upgrade, disconnect, close. Same scheme, same ticket format, same lookup as every page: the port's second cookie reader (`SessionAuthenticator` over `CookieJar`) goes. |
| C10 | **The port's re-check stays**: after the connection is registered under its identifier (so a remote disconnect would reach it), it looks the session up once more and closes as unauthorized if that fails. The re-check is **not** a second `AuthenticateAsync`: an `AuthenticationHandler` caches its result for the request ("calling Authenticate more than once should always return the original value", and the handler provider keeps one handler per request), so on the same `HttpContext` it would return the first answer. It takes the token from the first result's ticket (`Schemes.TokenClaim`) and runs the same lookup `SessionCookie.validatePrincipal` uses (`sessions` row, then user) in a fresh DI scope. | A ban or sign-out that disconnected the user between the first lookup and the registration would otherwise go unheard (Rails has this gap; the port closed it). One more indexed query per connect. |
| C11 | **Remote disconnects and restarts are methods, not pub/sub messages.** `CableServer.Disconnect(identifier, reconnect)` closes every connection registered under that identifier with `{"type":"disconnect","reason":"remote","reconnect":...}` then close 1000 `""`. `CableServer.Restart()` does the same for every connection with `server_restart`, `reconnect: true`, and is registered on `IHostApplicationLifetime.ApplicationStopping`, so a stopping host closes its WebSockets instead of waiting out the shutdown timeout. | In-process, the internal channel (`action_cable/<gid>`) is only a way to reach connections; an index by identifier is the direct form. The frames are the golden ones. The lifetime hook is the framework's place for "on shutdown". |
| C12 | **Commands run in arrival order, one at a time, each in its own DI scope** (`IServiceScopeFactory.CreateAsyncScope()`, as SignalR does per hub invocation). Channel callbacks resolve `CampfireDb` from `sub.Services`. A scope is also made for each `unsubscribed` on close. | The connection outlives any sensible `DbContext`; the request scope of the upgrade request lasts for hours. A pooled context per command is the EF and ASP.NET Core norm. Sequential processing keeps Rails' per-connection ordering. |
| C13 | **Confirmation before any broadcast on a new stream.** `StreamFrom` during `subscribed` only records the broadcasting; after the callback returns the connection enqueues the callback's transmissions, then `confirm_subscription` (or runs `unsubscribed` and enqueues `reject_subscription`), and only then adds the streams to the registry. | Rails sends the confirmation once the pub/sub adapter has acknowledged the subscription, so the confirmation precedes the stream's messages; the port kept the same order. A broadcast made before registration is not delivered, as in Rails before the adapter's acknowledgement. Per-subscription frame order is the plan's class C line. |
| C14 | **JSON is System.Text.Json with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`.** Payloads are encoded once per broadcast: `Payload.ofValue` = `JsonSerializer.SerializeToUtf8Bytes(value, CableJson.Options)` (F# anonymous records: `{| roomId = room.Id |}`), `Payload.ofString` for Turbo stream tags (a JSON string). Frames are written with `Utf8JsonWriter` (`WriteRawValue` for the identifier's encoded string and the payload). Inbound commands are read with `JsonDocument`; the identifier is parsed once at subscribe and its root `Clone()`d into the subscription. | The plan's frames rule is "decode to the same JSON". Rails escapes `<`, `>`, `&` as `<`; relaxed escaping writes them raw and `"` as `\"` (the default encoder writes `"`, 3x the bytes for every quote in a Turbo stream's HTML); both decode the same (S6). Escaping inside WebSocket frames has no XSS role: the client `JSON.parse`s them. F# anonymous records serialize their fields **alphabetically** (S7); every payload Campfire sends today is alphabetical in Rails too (`action`, `user`; `id`, `name`; `roomId`; `room_id`), and the golden comparison (C15) is key-order-sensitive, so a future non-alphabetical payload fails a test instead of drifting. `RailsCompat.Json.Value` leaves the Cable API. |
| C15 | **Golden frames are compared decoded.** Each recorded and replayed frame is parsed and re-serialized with one canonical writer (`JsonNode.Parse(s).ToJsonString(relaxed options)`), which keeps key order and string contents (an `identifier` is compared as the decoded string, byte for byte, because the JS client finds subscriptions by `identifier ===`) and drops escaping differences (S6). Frame order per connection stays exact in `GoldenTests`; `ChannelsGoldenTests` keeps its per-socket, per-step sorted comparison (the Rails race it documents). | The plan's class A/C line for Cable. The comparator is a test helper of about 15 lines. |
| C16 | **Broadcasts are published by the app, explicitly, after the write has committed**: after `SaveChangesAsync` returns (or `CommitAsync` for an explicit transaction), at the call sites Rails has (controllers, jobs, model functions), never from an EF interceptor. Every app broadcast goes through one function, `Publish.broadcast` (`Broadcasts`, `Revocation`, Presence's `readRoom`), which throws `InvalidOperationException` when called inside `Writes.run`'s transaction (design-data-ef E11's `WriteGate.InWrite`, an `AsyncLocal<bool>` true from `BEGIN` to `COMMIT`; the after-commit queue, where the event sink hands `DisconnectUser` to `Revocation`, runs with it false). | Plan 2.1 settled explicit after-commit calls ("not in interceptors, where ordering would be implicit"). A `SavedChanges` interceptor would fire inside an outer transaction, before commit. The guard turns "broadcast before commit" (a client could fetch a row that a rollback then removes) from a review item into a test failure. Rails' `deactivate`/`ban` disconnect inside the transaction; the port's after-commit order (a Rust known difference) stays. |
| C17 | **Limits stay Rust's**: 64 subscriptions per connection and 4 KiB identifiers (beyond them the subscribe is logged and ignored), 1 MiB messages (larger closes with 1009 `MessageTooBig`, through the writer). Received messages are read with a **zero-byte `ReceiveAsync` first** and only then into a pooled buffer, which grows up to the limit for a fragmented message. | Rust known differences carried by the plan. The zero-byte read (S8) means an idle connection holds no receive buffer, SignalR's technique; it also returns `Close` by itself (S8), which the loop must handle before its buffered read. |
| C18 | **`Campfire.Cable` stays a project** (one of the plan's seven), referencing only the shared framework and `Campfire.RailsCompat` (Turbo's signed stream names, GlobalID params). Configuration is `CableOptions` through `IOptions<CableOptions>`, bound in `Services.add` from the same settings as now (`DISABLE_SSL` no longer matters to Cable: the scheme comes from the request). | Plan section 3 "Projects shrink to seven". |

What stays hand-written, and why (parity): the Action Cable protocol (commands, frames, subscription and rejection semantics,
`perform` dispatch with Rails' `receive` default), the origin check and the 404 page, the subprotocol pick, the stream registry's
grouping by identifier and the slow-consumer glue, Turbo's `<turbo-stream>` tag builder and stream naming, the channels.

## 2. Spikes (scratch project, 2026-10-13, .NET SDK 10.0.401, ASP.NET Core 10.0.12, Kestrel on loopback)

`/private/tmp/claude-501/-Users-clank-Desktop-projects/fea4ed62-8823-4b82-bed5-fc54b6214d57/scratchpad/cablespike/` (`Program.fs`,
`anon.fsx`, `spike-output.txt`; build output deleted). A `WebApplication` with `UseResponseCompression`, `UseWebSockets` and a
`/cable` handler shaped like C1-C7; clients: `ClientWebSocket`, a raw `TcpClient` that completes the handshake and never reads, and
`TestServer`. No .NET 11 SDK on the Mac; every API used exists since .NET 8 (`PeriodicTimer(TimeSpan, TimeProvider)`,
`WebSocketAcceptContext.KeepAliveInterval`, `CancellationTokenSource.TryReset`).

| # | Question | Result |
|---|---|---|
| S1 | Client offers `actioncable-v1-json, actioncable-unsupported` and `permessage-deflate` | 101, subprotocol `actioncable-v1-json`, no `Sec-WebSocket-Extensions`; first frame `{"type":"welcome"}` |
| S2 | Plain `GET /cable`; upgrade with `Origin: http://evil.example` | `404 text/plain; charset=utf-8 'Page not found'`; the cross-origin upgrade 404 |
| S3 | `UseResponseCompression` (HTTPS enabled) ahead of the endpoint, client sends `Accept-Encoding: gzip, br` | no `Content-Encoding` on the 101; frames intact |
| S4 | Client never reads; broadcasts of 600-byte frames | sends block after 88 (70 KB) in one run, 733 (580 KB) in another; the queue then fills; the first refused `TryWrite` aborts the connection; read loop ends (`OperationCanceledException`, state `Aborted`), writer ends, connection removed, all within the broadcasting request |
| S4b | Client never reads; 60 frames of 64 KB (fills the buffers, not the queue); send timeout 1.5 s | the cancelled `SendAsync` aborts the socket (`OperationCanceledException`), the read loop ends (`ConnectionAbortedException`), connection removed after 1,527-1,532 ms. `TryReset()` after each completed send kept the timer from firing early |
| S5 | `TestServer.CreateWebSocketClient()` | negotiates the subprotocol and carries `Origin`; **its socket never pushes back** (2,000 frames to a client that doesn't read: 903-1,995 accepted, the rest refused only because the broadcaster outran an in-memory writer). Slow-consumer tests must use Kestrel on loopback |
| S6 | Escaping: default encoder vs `UnsafeRelaxedJsonEscaping`; canonical comparison | default: `<`, `"`, `é`, `😀`; relaxed: `<`, `\"`, `é`, `😀`, ` `, `\u0007`. A relaxed frame and Rails' `<` frame compare equal after `JsonNode` round trip; swapped keys compare unequal |
| S7 | `JsonSerializer` on F# anonymous records | `{"action":"start","user":{"id":773523958,"name":"Cable <Tester> é"}}`, `{"roomId":5}`; `{| zeta = 1; alpha = 2 |}` -> `{"alpha":2,"zeta":1}` (alphabetical) |
| S8 | Zero-byte `ReceiveAsync(Memory.Empty)` on the server socket | returns `Text/0/false` when a message arrives, then the buffered read gets it; returns `Close/0/true` for a close frame (the close is consumed: a second receive throws, state `Closed`) |
| S9 | Server-initiated close through the writer: disconnect frame, complete, `CloseOutputAsync(NormalClosure, "")` | client gets the frame, then `Close NormalClosure ''` (the golden `close Some((1000, ""))`) |

## 3. Types and modules

Names are binding; bodies are the builder's. `Campfire.Cable` compile order: `Options.fs`, `Frames.fs`, `Naming.fs`, `Channel.fs`,
`Streams.fs`, `Connection.fs`, `Server.fs`, `Endpoint.fs`, `Heartbeat.fs`, `Turbo.fs`.

```fsharp
namespace Campfire.Cable

// Options.fs
type CableOptions() =
    member val DisableRequestForgeryProtection = false with get, set
    member val AllowedRequestOrigins: string[] = [||] with get, set
    member val QueueCapacity = 256 with get, set
    member val SendTimeout = TimeSpan.FromSeconds 30.0 with get, set
    member val CloseTimeout = TimeSpan.FromSeconds 5.0 with get, set
    member val BeatInterval = TimeSpan.FromSeconds 3.0 with get, set
    member val MaxSubscriptions = 64 with get, set
    member val MaxIdentifierBytes = 4096 with get, set
    member val MaxMessageBytes = 1048576 with get, set

// Frames.fs (about 60 lines)
module CableJson =
    val Options: JsonSerializerOptions            // Encoder = UnsafeRelaxedJsonEscaping
    val WriterOptions: JsonWriterOptions          // same encoder
[<Struct>] type Payload = internal { Json: byte[] }   // one broadcast's message, encoded once
module Payload =
    val ofValue: 'T -> Payload                    // JsonSerializer.SerializeToUtf8Bytes(v, CableJson.Options)
    val ofString: string -> Payload               // a JSON string: Turbo stream tags
type DisconnectReason = Unauthorized | InvalidRequest | ServerRestart | Remote
module Frames =
    val Protocols: string[]                       // [| "actioncable-v1-json"; "actioncable-unsupported" |]
    val welcome: ReadOnlyMemory<byte>             // {"type":"welcome"}, built once
    val ping: unixSeconds: int64 -> ReadOnlyMemory<byte>
    val disconnect: DisconnectReason option -> reconnect: bool -> ReadOnlyMemory<byte>  // {"type","reason","reconnect"}
    val identifierJson: identifier: string -> byte[]          // the identifier as an encoded JSON string
    val confirmation: identifierJson: byte[] -> ReadOnlyMemory<byte>   // {"identifier":..,"type":"confirm_subscription"}
    val rejection: identifierJson: byte[] -> ReadOnlyMemory<byte>
    val message: identifierJson: byte[] -> Payload -> ReadOnlyMemory<byte>   // {"identifier":..,"message":..}

// Naming.fs: unchanged (channel_name, broadcasting_for, stream_name_from, gidParam)

// Channel.fs (about 75 lines)
type ICableBroadcaster =
    abstract Broadcast: broadcasting: string * payload: Payload -> int   // subscribers reached
[<Sealed>]
type Subscription<'U> =
    member Identifier: string                     // raw, as the client sent it
    member Params: JsonElement                    // the decoded identifier object, channel included
    member Param: string -> JsonElement voption
    member CurrentUser: 'U
    member Services: IServiceProvider             // the current command's scope (C12)
    member Broadcaster: ICableBroadcaster
    member BroadcastingFor: string list -> string
    member BroadcastTo: string list * Payload -> unit
    member StreamFrom: string -> unit             // recorded; registered after the confirmation (C13)
    member StreamFor: string list -> unit
    member StopStreamFrom: string -> unit
    member StopAllStreams: unit -> unit
    member StreamOrRejectFor: string list option -> unit
    member Reject: unit -> unit
    member Rejected: bool
type Channel<'U> =
    { Subscribed: Subscription<'U> -> Task        // an exception = Rails' rescued error: logged, nothing sent
      Unsubscribed: Subscription<'U> -> Task      // also after a rejection, as in Rails
      Perform: string -> JsonElement -> Subscription<'U> -> Task<bool> }   // false = "Unable to process"
module Channel =
    val empty<'U> : Channel<'U>
type ChannelRegistry<'U> = FrozenDictionary<string, unit -> Channel<'U>>   // Ruby class name -> factory
module ChannelRegistry =
    val create: (string * (unit -> Channel<'U>)) list -> ChannelRegistry<'U>
    val resolve: ChannelRegistry<'U> -> string -> struct (string * (unit -> Channel<'U>)) voption  // "::X" = "X"

// Streams.fs (internal, about 60 lines)
type internal IOutbound =                         // what the registries need of a connection
    abstract TryWrite: ReadOnlyMemory<byte> -> bool   // false: full, the connection has been aborted
    abstract Close: DisconnectReason option * reconnect: bool -> unit
type internal Subscriber = { Outbound: IOutbound; Identifier: string; IdentifierJson: byte[] }
type internal StreamRegistry() =
    member Add: broadcasting: string * Subscriber -> unit        // ImmutableArray kept sorted by Identifier (ordinal)
    member Remove: broadcasting: string * Subscriber -> unit     // TryUpdate / TryRemove(KeyValuePair) retry loop
    member Broadcast: broadcasting: string * Payload -> int      // one frame per distinct identifier
    member Count: int                                            // broadcastings with subscribers (tests)
type internal ConnectionIndex() =                 // every open connection, and by identifier
    member Add / Remove: IOutbound * identifier: string -> unit
    member All: seq<IOutbound>
    member ByIdentifier: string -> ImmutableArray<IOutbound>

// Connection.fs (internal, about 210 lines): one WebSocket. Order at start: enqueue `welcome`, then join the ConnectionIndex
// (so no ping or disconnect can precede the welcome), then the re-check (C10), then the read loop.
type internal CableConnection<'U> =
    new: ws: WebSocket * ctx: HttpContext * user: 'U * server: CableServerCore<'U> -> CableConnection<'U>
    member RunAsync: unit -> Task                 // welcome, read loop (C12, C17), close (C7), cleanup
    interface IOutbound
// The writer loop: WaitToReadAsync/TryRead -> SendAsync(frame, Text, true, sendTimeout token); after the channel completes,
// CloseOutputAsync(closeStatus, "") unless the socket is already closed. The read loop: zero-byte receive; Close -> echo the
// client's status (set closeStatus, complete the channel); Binary -> log "Couldn't handle non-string message", ignore; Text ->
// assemble up to MaxMessageBytes, then Dispatch (subscribe / unsubscribe / message, Rails' rules as the port's Connection.fs
// has them). On exit: unsubscribe all (each in a scope), leave both registries, complete the channel, await the writer with
// CloseTimeout, then ctx.Abort() if it has not finished.

// Server.fs (about 85 lines)
[<Sealed>]
type CableServer<'U> =
    new: options: IOptions<CableOptions> * channels: ChannelRegistry<'U>
         * authenticate: (HttpContext -> Task<'U option>) * identify: ('U -> string)
         * scopes: IServiceScopeFactory * time: TimeProvider * lifetime: IHostApplicationLifetime
         * logger: ILogger<CableServer<'U>> -> CableServer<'U>
    member Broadcast: broadcasting: string * Payload -> int
    member BroadcastTo: className: string * broadcastables: string list * Payload -> int
    member Disconnect: identifier: string * reconnect: bool -> int    // C11
    member Restart: unit -> unit                                      // C11, on ApplicationStopping
    member Beat: unit -> unit                                         // C8
    member ConnectionCount: int
    member StreamCount: int
    interface ICableBroadcaster

// Endpoint.fs (about 35 lines)
module Endpoint =
    val handle: CableServer<'U> -> HttpContext -> Task   // C5 checks, C9 authenticate, accept (C1), CableConnection.RunAsync

// Heartbeat.fs (about 15 lines)
type CableHeartbeat<'U>(server: CableServer<'U>, time: TimeProvider, options: IOptions<CableOptions>) =
    inherit BackgroundService()                   // while! timer.WaitForNextTickAsync ct do server.Beat()

// Turbo.fs (about 115 lines): StreamsChannel (verifier + guard) as now, over JsonElement params;
// actionTag/refreshTag (strings, attribute order as now); broadcastActionTo etc. build the tag string and send
// Payload.ofString. The JsonStringWriter sinks and the HtmlUtf8 template case go (bytes from a fragment are decoded to a
// string until Phase 4 makes partials strings).
```

App side (`src/Campfire.App/Channels/`):

```fsharp
// Identity.fs: CableUser = { Id: int64; Name: string }; type Cable = CableServer<CableUser>; Gid as now.
// Auth.fs (replaces Connection.fs, about 15 lines)
module CableAuth =
    val authenticate: HttpContext -> Task<CableUser option>   // AuthenticateAsync Schemes.Session; Current.user (C9)
// Publish.fs (about 15 lines)
module Publish =
    val broadcast: ICableBroadcaster -> string -> Payload -> int   // throws inside Writes.run (C16)
    val disconnectUser: Cable -> userId: int64 -> reconnect: bool -> int
// CableServices.fs (replaces Registry.fs, about 30 lines)
module CableServices =
    val add: IServiceCollection -> unit   // Configure<CableOptions>; the ChannelRegistry (the nine Ruby class names);
                                          // AddSingleton<Cable> by factory; AddHostedService<CableHeartbeat<CableUser>>;
                                          // AddSingleton<Broadcasts>
    val map: Cable -> HttpEndpoint        // Falco: any "/cable" (Endpoint.handle cable)
```

`RoomChannel`, `Presence`, `RoomMessages`, `TypingNotifications`, `ReadRooms`, `UnreadRooms`: same Rails behaviour, `ChannelResult`
replaced by exceptions (the messages the port gives them, so the logs match), params read from `JsonElement` (`castId` over
`ValueKind`: `Number` -> `TryGetInt64` or truncated double, `String` -> `Ruby.integerCast`, `True`/`False` -> 1/0, else none),
`CampfireDb` from `sub.Services`, writes through `Writes.run`. `Broadcasts` takes `ICableBroadcaster`, sends `Payload.ofString
(Turbo.actionTag ...)` and the unread fan-out as one `Payload.ofValue {| roomId = room.Id |}` reused for every member.

Pipeline (Phase 2's `Services.pipeline`): `UseWebSockets()` goes right after `UseSession`, before `UseRouting`; `/cable` stays the
first Falco endpoint. Boot: `AppState.Cable`/`Broadcasts` come from DI (`ctx.RequestServices` or the built provider), `Jobs.SetCable`
and the explicit `app.Cable.Restart()` in `serve` go (C11), unless test P12 shows Front's host does not raise `ApplicationStopping`
before it waits on requests; then the explicit call stays and the unit says so.

Packages: none new. Shared framework (`Microsoft.AspNetCore.App` 11): WebSockets middleware, `System.Net.WebSockets`,
`System.Threading.Channels`, `System.Text.Json`, `System.Collections.Immutable`, `System.Collections.Frozen`, `BackgroundService`,
`IHostApplicationLifetime`. Tests: `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 (plan Appendix A; `FakeTimeProvider` for
the heartbeat) and `Microsoft.Extensions.Diagnostics.Testing` 10.10.0 (`FakeLogger`, already added by Phase 2). Not added:
`Microsoft.AspNetCore.TestHost` (S5: no backpressure; Kestrel on loopback is the test transport).

## 4. Units, in build order

One Sonnet builder per unit, 90 minutes each, from a clean `git status` on `dotnetway`, one commit per unit. Common acceptance for
every unit: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green; `bin/kloc` in the commit message; and, from 3.3 on, `bin/pixel-parity --fast
--only "realtime/<state>"` equal on screenshots, aria and loose DOM (judged against the Phase 0 residue) for `message_sent`,
`typing/one`, `presence_read` and `removed_from_room`, **one state per run** (Phase 0: a whole `realtime/**` slice crashes the
harness's network log; `removed_from_room` needed the harness's retries then).

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 3.1 Decoded golden comparison | Test-only. `tests/Shared/JsonFrames.fs`: `canonical : string -> string` (C15) and `assertSameFrames`; `Campfire.Cable.Tests.GoldenTests`, `Campfire.App.Tests.ChannelsGoldenTests` and `BroadcastsTests` compare through it. Against the **current** server | G1-G3 (section 5); every golden test still passes on the untouched transport, so the new one is judged by the same comparator |
| 3.2 Frames and registry | New `Options.fs`, `Frames.fs` (with `CableJson`, `Payload`), `Streams.fs` beside the old files (nothing uses them yet); in-process tests with a fake `IOutbound` | F1-F4, R1-R6; `Frames` output equal to the recorded frames under `canonical` |
| 3.3 Channel API | `Channel.fs` to the section 3 shape (JsonElement params, exceptions, `Payload` broadcasts, `Services` = the server's root provider until 3.5) **on the old transport** (the old hub gets `Payload` bytes); `Turbo.fs` on strings; `JsonText.fs` deleted; the nine App channels, `Broadcasts`, `BroadcastNames.readRoom` and their tests moved | every Cable and App channel test passes unchanged in meaning; `ChannelsGoldenTests` and `BroadcastsTests` green; `realtime/*` four states equal |
| 3.4 Transport | `Connection.fs`, `Server.fs`, `Endpoint.fs`, `Heartbeat.fs` on the WebSocket middleware (C1-C8, C11, C13, C17); `CableServices.add`/`map`; `UseWebSockets` in the pipeline; **deleted**: `Socket.fs`, `Pubsub.fs`, `Config.fs`, the old `Connection.fs`/`Server.fs`/`Endpoint.fs`/`Protocol.fs`, `SocketTests`, `PubsubTests`, `JsonTextTests`, the deflate cases. Cable tests rewritten over `ClientWebSocket` on Kestrel loopback. Authentication still through the App's `SessionAuthenticator` as Phase 2 left it, called with the upgrade's `HttpContext` (`HttpContext -> Task<CableUser option>`) | P1-P12 and the golden replay (`replays reference frames`, 26 exchanges, 25 frames) green; `bench/cable/fsharp` compiles against the new API; `realtime/*` four states equal; `ConnectionCount` and `StreamCount` back to 0 after every test |
| 3.5 Auth, scopes, revocation, after-commit | `CableAuth.authenticate` over `AuthenticateAsync Schemes.Session` + the WebSocket condition in `SessionCookie.validatePrincipal` (C9); re-check after registration (C10); per-command and per-unsubscribe scopes (C12); `Publish` and the `Writes.run` guard (C16); `Revocation` through DI; `Restart` on `ApplicationStopping`; **deleted**: `SessionAuthenticator` (and whatever cookie reading Phase 2 left in it), `Jobs.SetCable`, `Registry.fs` | A1-A6; every App test that boots the seed; `realtime/removed_from_room` and `realtime/room_deleted_then_send` equal (revocation paths) |
| 3.6 Slow consumers, limits, bench, docs | the Kestrel-loopback tests L1-L7; `bench/cable/fsharp/Program.fs` on the new API and `bench/cable/run fsharp` run (plain only: `driver.py`'s deflate mode now negotiates nothing, report it as such); README "Known differences" (section 7); the phase's KLOC | L1-L7; `bench/cable/run fsharp --reps 2` numbers recorded in `bench/results/cable-fanout-dotnetway.md` beside the stored F# plain row (1.6-1.75 us/delivery burst, 30 kB/client) |

Then the **phase gates** (gate runner, plan section 3): `bin/verify`; golden frames (decoded) green; `realtime/*` **all 13 states on
every engine** of the lean matrix, one state per run, every seed the states use; `bench/cable/run fsharp` reported; `bench/quick` post a
message and room page against a same-day control (broadcast on the write path; reported unless under 5x Rails); `bin/kloc`.
`bin/db-differential` and `bin/cookie-roundtrip` are not phase gates here (no schema, model or cookie change), but the gate runner
runs `bin/cookie-roundtrip` once if 3.5 touched `SessionCookie.validatePrincipal` beyond the WebSocket condition.

## 5. Tests

Cable tests (`tests/Campfire.Cable.Tests`) host a minimal `WebApplication` on Kestrel loopback (`Support.host`, as now, with
`CableServer` from the same `CableServices`-style registration and test channels) and talk to it with `ClientWebSocket`
(`CollectHttpResponseDetails = true` for handshake headers), or with the existing raw TCP client where a test needs a client that
never reads. App tests (`tests/Campfire.App.Tests`) boot the seed through `Support.bootSeeded` (the real `Services.add` and
pipeline), which is this repo's WebApplicationFactory level until Phase 7 (`plans/design-kit-auth.md` section 5).

| Id | Test |
|---|---|
| G1 | `canonical`: Rails' `<`/`>`/`&` escaping and ours compare equal; a changed value, a swapped key order, a different number form (`1` vs `1.0`) compare unequal |
| G2 | the golden replay compares decoded frames per step, in order, 26 exchanges and 25 frames (the counts asserted now) |
| G3 | `ChannelsGoldenTests`/`BroadcastsTests` decoded, per socket and step |
| F1 | `Frames.welcome`, `ping`, `disconnect` (each reason, `reason: null` for none, `reconnect` true/false), `confirmation`, `rejection`, `message` canonical-equal to the recorded frames |
| F2 | an identifier with quotes, `<`, non-ASCII and an emoji: decoding the frame's `identifier` gives the identifier string back exactly |
| F3 | `Payload.ofValue {| action = "start"; user = {| id = 1L; name = "x" |} |}` writes `action` before `user` (pins S7) |
| F4 | `Payload.ofString` of a Turbo tag with `"`, `<`, `&`, U+2028: decodes to the tag |
| R1 | two subscribers with the same identifier on one stream receive the **same** frame instance (one frame built); two identifiers: two frames |
| R2 | `Remove` of the last subscriber removes the broadcasting (`Count` 0); concurrent `Add`/`Remove`/`Broadcast` from 8 threads, 10,000 ops: no lost subscriber, no exception |
| R3 | `Broadcast` returns the number of subscribers written; a subscriber whose `TryWrite` is false is counted out and the others still get theirs |
| R4 | `ConnectionIndex.ByIdentifier` after adds and removes |
| R5 | a broadcast to a stream with no subscribers: 0, no frame built |
| R6 | 10,000 `TryWrite`s to a full fake outbound cost under 10 ms (the broadcaster never waits) |
| P1 | handshake: client protocols `actioncable-v1-json, actioncable-unsupported` -> `actioncable-v1-json`; only `actioncable-unsupported` -> it; none -> no `Sec-WebSocket-Protocol`; a `permessage-deflate` offer -> no `Sec-WebSocket-Extensions` |
| P2 | `GET /cable` and `POST /cable` without upgrade, and a cross-origin upgrade: 404 `text/plain; charset=utf-8` `Page not found`; same origin under `assume_ssl` (`Origin: https://host`) accepted; `AllowedRequestOrigins`; `DisableRequestForgeryProtection` |
| P3 | `welcome` is the first frame; with `FakeTimeProvider`, `Advance(3 s)` gives exactly one `ping` per open connection with `message` = the provider's unix seconds, never before `welcome` |
| P4 | subscribe confirms; the same identifier again: no reply; unknown channel, missing or invalid identifier, unknown command, invalid JSON: no reply, connection open; `::RoomChannel` resolves to `RoomChannel` and streams under that class name |
| P5 | rejection runs `unsubscribed` and the identifier can be subscribed again |
| P6 | `perform`: named action, blank action -> `receive`, unknown -> "Unable to process" logged (`FakeLogger`), after unsubscribe -> "Unable to find subscription" |
| P7 | an exception in `subscribed`: no confirmation, no rejection, logged, the subscription stays (Rails) |
| P8 | **order**: a channel whose `subscribed` transmits, while another thread broadcasts 1,000 numbered messages on the stream it starts: the client sees the transmission, then the confirmation, then broadcasts in increasing order with no gap after the first one it sees |
| P9 | unauthorized (authenticator returns none): 101, `{"type":"disconnect","reason":"unauthorized","reconnect":false}`, close 1000 `""` |
| P10 | `Disconnect(id, true)`: every connection of that identifier (two sockets) gets the remote disconnect and close 1000; another identifier's connection stays open; `unsubscribed` ran for each subscription |
| P11 | client close 1001: the server answers 1001 and the handler returns; a client that sends close and then nothing: handler returns within `CloseTimeout` |
| P12 | host `StopAsync` with three open connections: each gets `server_restart`, `reconnect: true`, close 1000; the stop completes in under 2 s |
| L1 | a raw client that never reads, broadcasts of 600-byte frames until refused: the connection is aborted, `unsubscribed` ran, `ConnectionCount`/`StreamCount` back to 0, another client on the same stream received every frame |
| L2 | `SendTimeout` = 1 s, 40 frames of 64 KB to a client that never reads (less than `QueueCapacity` queued): aborted after 1-2 s, cleaned up as L1 |
| L3 | the 65th subscription and a 4,097-byte identifier: ignored and logged, earlier subscriptions unaffected |
| L4 | a 1 MiB + 1 text message (also sent in 4 fragments): close 1009; a 1 MiB message is processed |
| L5 | a binary message: logged, ignored, connection open; an invalid UTF-8 text message: closed 1007 by the framework |
| L6 | a client ping frame is answered with a pong (framework) |
| L7 | 1,000 idle connections: managed memory per connection (`GC.GetTotalMemory` before/after) recorded in the test output and under 16 KB (the port's cable measured about 9 KB; no receive buffer per idle connection, C17) |
| A1 | the Rails-minted `session_token` of `campfire_sessions.json` on `/cable`: `welcome`; no cookie, the forged vector, a flipped digit: unauthorized frame and close |
| A2 | a session whose `last_active_at` is 2 h old connecting to `/cable`: no `sessions` write (`updated_at` unchanged), no `Set-Cookie` on the 101 |
| A3 | the re-check (C10) sees a session deleted between the first lookup and the registration (a test hook between the two steps): unauthorized frame and close; ban or deactivate a connected user: `{"type":"disconnect","reason":"remote","reconnect":false}`, then a reconnect with the same cookie is unauthorized; removing a membership: `reconnect: true` and the room's channels reject on resubscribe |
| A4 | two commands on one connection resolve different `CampfireDb` instances; the upgrade request's scope is not used by a command |
| A5 | `Publish.broadcast` inside `Writes.run` throws `InvalidOperationException`; posting a message: the room's subscriber gets the `append` frame and the members' unread frames, and the `messages` row is readable from another connection when the frame arrives |
| A6 | Presence: closing the socket runs `absent` (the membership's `connected_at` cleared), in its own scope |

Kept as they are (moved to the new API): `NamingTests`, `TurboTests`, App `ChannelsTests`, `RevocationTests`, `MessagesTests`'
broadcast cases. Retired with their code in 3.4: `SocketTests`, `PubsubTests`, `JsonTextTests`, `FanOutTests` (the in-process fan-out
of the ring hub; `bench/cable/run` is the measurement now), `ServerTests`, and in `ProtocolTests` the cases about framing,
compression and the hand-made handshake (`reads text, fragments, compressed messages and control frames`, `rejects protocol errors`,
`refuses a too large fragment from its header`, `writes shared frames, compressed only when worth it`, `handshakes and negotiates
compression`, `a handshake with a key that isn't 16 bytes of canonical Base64 is refused`, `compression is concurrent-safe`, `a
broadcast is compressed once ...`, `the 101 answers as websocket-driver does`, `cable over TLS`: the framework's now; TLS is Phase 7).
The protocol cases of `ProtocolTests` become P1-P12.

## 6. What is deleted

`src/Campfire.Cable/Socket.fs` (RFC 6455 framing, handshake, `permessage-deflate`), `Pubsub.fs` (seqlock rings, `Wake`, `Hub`),
`JsonText.fs` (`JsonStringWriter`), `Config.fs` (and `ConnectRequest`), the old `Protocol.fs`, and the port's `Connection.fs`,
`Server.fs`, `Endpoint.fs` (rewritten); in the App, `Channels/Connection.fs` (`SessionAuthenticator`, the port's second reader of the signed cookie),
`Channels/Registry.fs` (replaced by `CableServices.fs`), `Jobs.SetCable` and its mutable field, the explicit `Cable.Restart()` in
`Boot.serve` (C11); the tests listed in section 5; `bench/cable/run`'s compression mode and `bench/results/cable-fanout.md`'s
deflate rows stay as history, marked superseded by the new results file.

## 7. KLOC projection

cloc 2.10 code lines (`bin/kloc --files`, corrected = raw here) at `0d3504f`: `Campfire.Cable` **1,603** (Socket 402, Connection 371,
Server 169, Pubsub 160, Turbo 160, JsonText 98, Channel 92, Endpoint 63, Protocol 37, Naming 28, Config 23); App `Channels/` **499**.

| | KLOC |
|---|---:|
| Cable after: Options 0.02, Frames 0.06, Naming 0.03, Channel 0.075, Streams 0.06, Connection 0.21, Server 0.085, Endpoint 0.035, Heartbeat 0.015, Turbo 0.115 | 0.70 (from 1.60) |
| App `Channels/` after (Result plumbing out, Auth/Publish/CableServices in) | 0.44 (from 0.50) |
| Boot, Jobs, AppState call sites | -0.01 |
| **Phase 3 net** | **-0.97** |

Project: **about 33.3 -> 32.3 KLOC** if Phases 1 and 2 close where their notes project (38.0, then -4.7; the plan's row says 31.7 -> 30.8;
both are a 0.9-1.0 drop, the base differs by Phase 1's kept SQL and Phase 2's added `Web/` code). Cable's 0.70 against the plan's
0.65 is the registry and limits. End-of-project projection: about 18.5 KLOC (the plan's 17.0, +0.6 from design-data-ef, +1.0 from
design-kit-auth, -0.07 here), 60% of Rust's 31.0.

## 8. Known differences this phase records (README "Known differences", plan section 4)

- **WebSocket compression: none, as Rails** (removes the Rust/port entry "permessage-deflate without context takeover").
- **Slow clients (class B, replaces Rust's 30-second entry):** a client 256 frames behind, or whose socket has taken nothing for 30
  s, is disconnected without a `disconnect` frame (TCP abort); its JS client reconnects and resubscribes. Rails buffers without limit.
- **Origin check (class B):** compares `Origin` with the request's scheme and host as ASP.NET Core sees them after the trusted-proxy
  forwarded headers; behind a proxy that rewrites `Host` and sends `X-Forwarded-Host`, the forwarded host is the one compared
  (Rails compares the raw `Host`).
- **Byte-level (class A):** JSON escaping in frames (`<`, `>`, `&` literal, characters outside the BMP as surrogate escapes); key
  order of payloads written from anonymous records is alphabetical (the same as Rails' for every current payload); the 101
  response's headers; HTTP/2 WebSockets over TLS where the browser chooses them (Phase 7); the heartbeat starts at boot.
- Unchanged from Rust and the port: the limits (64 subscriptions, 4 KiB identifiers, 1 MiB messages); remote disconnects after
  commit; the re-check after registration.

## 9. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | Frames reordered on a subscription (confirmation after a broadcast; two broadcasts swapped) | P8 in 3.4; G2/G3; `realtime/message_sent` and `typing/one` `--fast` in 3.4 |
| 2 | Escaping changes the identifier the JS client matches on (a subscription never confirmed in the browser) | F2 and G2 in 3.2/3.4; in the harness a never-confirmed subscription shows as `unconfirmed` in the cable layer and stalls the realtime readiness check |
| 3 | Two sends at once on one `ManagedWebSocket` (`InvalidOperationException: There is already one outstanding 'SendAsync' call`) because a close, ping or reject bypassed the writer | P8 run with `BeatInterval` = 10 ms in the test options; any such exception in a test log fails the test (`FakeLogger` assertion on the connection's error log) |
| 4 | The heartbeat never fires under the harness clock (a frozen `TimeProvider` whose timers are frozen too), so `ConnectionMonitor` sees a stale connection and reconnects mid-capture | A test in 3.4 boots with the harness's frozen-clock provider exactly as `bootSeeded` builds it and waits for a ping within 4 s; `realtime/*` reconnect loops in the harness's `cableLog` |
| 5 | Cable authentication resumes the session (writes `sessions`, re-signs the cookie on the 101) | A2 in 3.5 |
| 6 | A `DbContext` held for the connection's lifetime (request scope), then "a second operation was started on this context" under concurrent commands | A4 in 3.5; any EF concurrency exception in App test logs |
| 7 | A slow or dead client stalls broadcasters, or leaks registry entries after an abort | R6, L1, L2 (`ConnectionCount`/`StreamCount` back to 0) in 3.6; L1's timing |
| 8 | Shutdown waits on open WebSockets (Kestrel's graceful stop) because `ApplicationStopping` does not reach `Restart` through Front's host | P12 in 3.4 against the real `bootSeeded` host; if it fails, keep the explicit `Restart()` in `Boot.serve` and record it in the unit's commit |
| 9 | `UseWebSockets` under Front's host (until Phase 7): the upgrade goes through Front's middleware (compression, cache) | the App-level A1 runs the real pipeline in 3.5; S3 showed `UseResponseCompression` is safe; `realtime/*` `--fast` from 3.4 |
| 10 | Fan-out CPU per delivery higher than the port's (one `SendAsync` per frame, no write coalescing, no shared deflate) | `bench/cable/run fsharp` in 3.6 against the stored F# plain row; `bench/quick` post a message vs a same-day control at the gate (floor 5x Rails at c=16: Rails 330 req/s, so ~1,650) |
| 11 | Memory per idle connection grows (a receive buffer per socket) | L7; `bench/cable/run`'s memory column against the stored 30 kB/client |
| 12 | A payload written from an anonymous record whose Rails key order is not alphabetical | G1's order sensitivity and F3; the golden replays |
| 13 | A broadcast made inside a write (before commit) | A5's guard test; the guard throws in every test that boots the app |
| 14 | `realtime/*` harness flakiness (Phase 0: slice crashes in `network.ts`, `removed_from_room` needing retries) read as a Cable failure | run states one at a time; compare an error status with the Phase 0 log before debugging; re-run the cell with `--only` |
| 15 | Disk: the gates need 12 GB free (15 GB for benches); the Mac had 8.6-13 GB free during this note while another project filled the disk | `df -h /` before each gate; `go clean -cache` only when `pgrep -f 'go (test|build|run|vet)'` is empty; `bin/clean-docker` after image rebuilds |

## 10. Questions for Chris

None. Compression (Rails has none), the heartbeat (a `BackgroundService`, as the decisions list), authentication (the Phase 2 cookie
scheme), the slow-consumer policy (the plan's class B line, 256 frames, plus Rust's 30 s) and after-commit publishing (plan 2.1's
explicit calls) are settled by D4, D9 and the plan.
