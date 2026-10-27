// Port of rust/crates/campfire/src/test_support.rs and rust/crates/campfire/src/controllers/presenters/test_support.rs
//
/// What the app's tests share: a real Kestrel server on a loopback port running a pipeline, a raw
/// HTTP/1.1 client for it (`Uri` rewrites a bare `%` in a query to `%25`, and these tests need the bytes
/// they wrote on the wire), and a booted app over a copy of a reference-built parity seed
/// (`parity/bin/seed build`, which needs Docker), skipped with a message when it hasn't been built
/// (`CAMPFIRE_REQUIRE_SEED=1` fails the test instead).
module Campfire.App.Tests.Support

open System
open System.Collections.Generic
open System.Buffers
open System.IO
open System.IO.Pipelines
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.Extensions.Configuration
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.AspNetCore.Server.Kestrel.Core
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Logging.Testing
open Xunit
open Microsoft.EntityFrameworkCore
open Campfire.App
open Campfire.App.Channels
open Campfire.App.Integrations
open Campfire.Db
open Campfire.Storage
open Campfire.Tests

/// The pinned values of the parity environment (`parity/.env.reference`).
let parityEnv (name: string) : string option =
    let path = Repo.path "parity/.env.reference"
    if File.Exists path then
        File.ReadAllLines path
        |> Array.tryPick (fun line -> if line.StartsWith(name + "=", StringComparison.Ordinal) then Some(line.Substring(name.Length + 1)) else None)
    else
        None

let str (element: JsonElement) : string =
    match element.GetString() with
    | null -> failwith "not a string"
    | text -> text

// --- Requests and replies --------------------------------------------------------------------------------

/// A request as it goes on the wire.
type Req =
    { Method: string
      Target: string
      Headers: (string * string) list
      Body: byte[] option }

    /// Set a header, replacing any the request already has under that name.
    member this.With(name: string, value: string) : Req =
        let others = this.Headers |> List.filter (fun (k, _) -> not (k.Equals(name, StringComparison.OrdinalIgnoreCase)))
        { this with Headers = others @ [ name, value ] }

    member this.WithBody(body: string) : Req = { this with Body = Some(Encoding.UTF8.GetBytes body) }

    /// A form post's content type and body.
    member this.Form(body: string) : Req =
        { this.With("content-type", "application/x-www-form-urlencoded") with Body = Some(Encoding.UTF8.GetBytes body) }

let request (meth: string) (target: string) : Req =
    { Method = meth
      Target = target
      Headers = [ "host", "campfire.test" ]
      Body = None }

let get (target: string) : Req = request "GET" target

let getWithCookie (target: string) (cookie: string) : Req = (get target).With("cookie", cookie)

/// A response, read off the wire.
type Reply =
    { Status: int
      Headers: (string * string) list
      Body: byte[] }

    /// The first header called `name`, if any.
    member this.Header(name: string) : string option =
        this.Headers |> List.tryFind (fun (k, _) -> k.Equals(name, StringComparison.OrdinalIgnoreCase)) |> Option.map snd

    member this.HeaderValues(name: string) : string list =
        this.Headers |> List.filter (fun (k, _) -> k.Equals(name, StringComparison.OrdinalIgnoreCase)) |> List.map snd

    member this.Text: string = Encoding.UTF8.GetString this.Body

/// Everything after the headers, with chunked framing removed.
let private decodeChunked (body: byte[]) : byte[] =
    use output = new MemoryStream()
    let mutable i = 0
    let mutable go = true
    while go && i < body.Length do
        let eol = Array.IndexOf(body, byte '\r', i)
        let line = Encoding.ASCII.GetString(body, i, eol - i)
        let size = Convert.ToInt32(((line.Split ';')[0]).Trim(), 16)
        i <- eol + 2
        if size = 0 then
            go <- false
        else
            output.Write(body, i, size)
            i <- i + size + 2
    output.ToArray()

let private parseReply (bytes: byte[]) : Reply =
    let text = Encoding.Latin1.GetString bytes
    let split = text.IndexOf "\r\n\r\n"
    let head = text.Substring(0, split).Split("\r\n")
    let status = int (head[0].Split(' ')[1])
    let headers =
        [ for line in head[1..] do
              let colon = line.IndexOf ':'
              // Header values are UTF-8 on this server.
              let value = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(line.Substring(colon + 1).Trim()))
              line.Substring(0, colon), value ]
    let body = bytes[split + 4 ..]
    let chunked = headers |> List.exists (fun (k, v) -> k.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase) && v = "chunked")
    { Status = status
      Headers = headers
      Body = if chunked then decodeChunked body else body }

/// A Kestrel server on a free loopback port running a pipeline.
/// Counts what goes through a response's `BodyWriter`: each `Advance` that commits bytes is one write the
/// handler made. A page in parts is written part by part (`HtmlOutput.WriteTo`), so the count
/// stands in for the Rust `Reply::frames` (the data frames of a hyper body), which a raw HTTP/1.1 client
/// over a socket can't see.
type BodyWrites() =
    let mutable writes = 0L

    /// The writes counted so far, over every response of the host.
    member _.Total: int64 = Interlocked.Read &writes

    member internal _.Add() : unit = Interlocked.Increment &writes |> ignore

/// Hands the handler one large buffer, so that a `Write` of a span is one `Advance` however big it is
/// (Kestrel's own writer hands out 4 KB blocks and would split every write into many), and passes what
/// it was given on to the host's writer.
type private CountingWriter(inner: PipeWriter, counter: BodyWrites) =
    inherit PipeWriter()

    let mutable buffer: byte[] = Array.zeroCreate (512 * 1024)

    override _.Advance(bytes: int) =
        if bytes > 0 then
            counter.Add()
            inner.Write(ReadOnlySpan<byte>(buffer, 0, bytes))

    override _.GetMemory(sizeHint: int) =
        if sizeHint > buffer.Length then buffer <- Array.zeroCreate sizeHint
        Memory<byte> buffer

    override this.GetSpan(sizeHint: int) = (this.GetMemory sizeHint).Span
    override _.FlushAsync(cancellationToken: CancellationToken) = inner.FlushAsync cancellationToken
    override _.CancelPendingFlush() = inner.CancelPendingFlush()
    override _.Complete(exn: exn | null) = inner.Complete exn
    override _.CompleteAsync(exn: exn | null) = inner.CompleteAsync exn
    override _.CanGetUnflushedBytes = inner.CanGetUnflushedBytes
    override _.UnflushedBytes = inner.UnflushedBytes

/// The response body feature of the host with its writer counted; everything else is the host's.
type private CountingBody(inner: IHttpResponseBodyFeature, counter: BodyWrites) =
    let writer = CountingWriter(inner.Writer, counter)

    interface IHttpResponseBodyFeature with
        member _.Stream = inner.Stream
        member _.Writer = writer :> PipeWriter
        member _.DisableBuffering() = inner.DisableBuffering()
        member _.StartAsync(cancellationToken) = inner.StartAsync cancellationToken
        member _.SendFileAsync(path, offset, count, cancellationToken) = inner.SendFileAsync(path, offset, count, cancellationToken)
        member _.CompleteAsync() = inner.CompleteAsync()

type Host(host: WebApplication, port: int, writes: BodyWrites) =
    member _.Port = port

    /// Stops the host as `Boot.serve`'s ConsoleLifetime does at shutdown: `ApplicationStopping` first, then the server drains.
    member _.StopAsync() : Task = host.StopAsync()

    /// How many writes the app's handlers have made to response bodies.
    member _.BodyWrites: BodyWrites = writes

    /// Send `req` on a connection of its own and read the reply to the end.
    member _.Send(req: Req) : Task<Reply> =
        task {
            use client = new TcpClient()
            do! client.ConnectAsync(IPAddress.Loopback, port)
            let stream = client.GetStream()
            let head = StringBuilder()
            head.Append($"{req.Method} {req.Target} HTTP/1.1\r\n") |> ignore
            for (name, value) in req.Headers do
                head.Append($"{name}: {value}\r\n") |> ignore
            match req.Body with
            | Some body -> head.Append($"content-length: {body.Length}\r\n") |> ignore
            | None -> ()
            head.Append("connection: close\r\n\r\n") |> ignore
            do! stream.WriteAsync(Encoding.UTF8.GetBytes(head.ToString()))
            match req.Body with
            | Some body -> do! stream.WriteAsync(body)
            | None -> ()
            use received = new MemoryStream()
            do! stream.CopyToAsync received
            return parseReply (received.ToArray())
        }

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            ValueTask(
                task {
                    do! host.StopAsync()
                    do! host.DisposeAsync()
                }
            )

/// Serve `pipeline` on a free loopback port, with `configureServices` registered (the app's `ConfigureServices`), the host created
/// with `options` and `configureHost` applied (the app's `StaticAssets.hostOptions` and `configureHost`).
let startPipelineOn
    (options: WebApplicationOptions)
    (configureHost: WebApplicationBuilder -> unit)
    (configureServices: IServiceCollection -> unit)
    (pipeline: IApplicationBuilder -> unit)
    : Task<Host> =
    task {
        let builder = WebApplication.CreateSlimBuilder options
        configureHost builder
        configureServices builder.Services
        builder.WebHost.ConfigureKestrel(fun (options: KestrelServerOptions) ->
            Campfire.App.Web.Host.configureKestrel options
            options.Listen(IPAddress.Loopback, 0))
        |> ignore
        builder.Logging.ClearProviders() |> ignore
        let app = builder.Build()
        let writes = BodyWrites()
        app.Use(
            Func<HttpContext, RequestDelegate, Task>(fun http next ->
                match http.Features.Get<IHttpResponseBodyFeature>() with
                | null -> ()
                | inner -> http.Features.Set<IHttpResponseBodyFeature>(CountingBody(inner, writes))
                next.Invoke http)
        )
        |> ignore
        pipeline app
        do! app.StartAsync()
        let addresses = (nonNull (app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>())).Addresses
        return new Host(app, Uri(Seq.head addresses).Port, writes)
    }

/// Serve `pipeline` on a free loopback port, as the app's own host is created (A8).
let startPipelineWith (configureServices: IServiceCollection -> unit) (pipeline: IApplicationBuilder -> unit) : Task<Host> =
    startPipelineOn (Campfire.App.Web.StaticAssets.hostOptions [||]) Campfire.App.Web.StaticAssets.configureHost configureServices pipeline

/// Serve `pipeline` on a free loopback port.
let startPipeline (pipeline: IApplicationBuilder -> unit) : Task<Host> = startPipelineWith ignore pipeline

/// Log lines written to a factory: a `FakeLoggerProvider` over a `FakeLogCollector` (Microsoft.Extensions.Diagnostics.Testing),
/// with `Text` over the snapshot, formatted as `Level category: message` (the console's without its timestamp).
type CapturingLoggers() =
    let collector = FakeLogCollector()
    let factory =
        LoggerFactory.Create(fun builder -> builder.SetMinimumLevel(LogLevel.Trace).AddProvider(new FakeLoggerProvider(collector)) |> ignore)

    member _.Collector: FakeLogCollector = collector

    member _.Text: string =
        String.Join('\n', collector.GetSnapshot() |> Seq.map (fun record -> $"{record.Level} {record.Category}: {record.Message}"))

    member this.Logger(category: string) : ILogger = (this :> ILoggerFactory).CreateLogger category

    interface ILoggerFactory with
        member _.AddProvider(provider: ILoggerProvider) = factory.AddProvider provider
        member _.CreateLogger(category: string) : ILogger = factory.CreateLogger category
        member _.Dispose() = factory.Dispose()

// --- A booted app ------------------------------------------------------------------------------------------

/// Plain ADO.NET over the app's database file, for what the tests check on disk or put in without a model (nothing here knows
/// the entities).
module RawSql =
    open Microsoft.Data.Sqlite

    let private command (conn: SqliteConnection) (sql: string) (args: (obj | null) list) : SqliteCommand =
        // `?` marks become numbered named parameters (Microsoft.Data.Sqlite wants names).
        let command = conn.CreateCommand()
        let parts = sql.Split '?'
        command.CommandText <- String.Join("", parts |> Array.mapi (fun i part -> if i = 0 then part else $"@p{i - 1}{part}"))
        args |> List.iteri (fun i arg -> command.Parameters.AddWithValue($"@p{i}", arg) |> ignore)
        command

    /// The first column of the first row.
    let scalar<'T> (conn: SqliteConnection) (sql: string) (args: (obj | null) list) : 'T =
        use command = command conn sql args
        Convert.ChangeType(command.ExecuteScalar(), typeof<'T>) :?> 'T

    /// The first column of the first row, or none when there is no row.
    let tryScalar<'T> (conn: SqliteConnection) (sql: string) (args: (obj | null) list) : 'T option =
        use command = command conn sql args
        match command.ExecuteScalar() with
        | null -> None
        | :? DBNull -> None
        | value -> Some(Convert.ChangeType(value, typeof<'T>) :?> 'T)

    let run (conn: SqliteConnection) (sql: string) (args: (obj | null) list) : unit =
        use command = command conn sql args
        command.ExecuteNonQuery() |> ignore

/// What a second test host needs to serve the state of a booted app: its registrations (`Boot.addServices` over the same inputs, then the
/// host's own singletons shared in, so that both hosts and `App` see one app) and its pipeline. Not a second app: the hosted services
/// that work off the app's queues (`Boot.addWorkers`) go only to the host that serves it, since stopping a runner closes the queues for good.
[<NoComparison; NoEquality>]
type Wiring =
    { App: AppState
      Pipeline: IApplicationBuilder -> unit
      ConfigureServices: IServiceCollection -> unit
      Workers: IServiceCollection -> unit }

/// A booted app over its own copy of a seed (or an empty storage directory): `Boot.build`'s host on loopback ports, with a raw
/// client for it.
type Test(booted: Wiring, dir: string, host: Host, logs: CapturingLoggers) =
    member _.Booted = booted

    /// The loopback port the app listens on.
    member _.Port = host.Port

    /// What the app logged.
    member _.Logs: string = logs.Text
    member _.Captured: CapturingLoggers = logs
    member _.App = booted.App
    member _.Dir = dir

    /// A plain connection on the app's database file for the length of `f` (reads; it takes no gate).
    member _.Sql(f: Microsoft.Data.Sqlite.SqliteConnection -> 'T) : 'T =
        use conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={booted.App.Config.Storage.Database};Pooling=False")
        conn.Open()
        f conn

    /// A statement no model makes, run on a connection of its own while holding the write gate.
    member this.Execute(sql: string, args: (obj | null) list) : Task<unit> =
        task {
            do! booted.App.Gate.Enter()
            try
                this.Sql(fun conn -> RawSql.run conn sql args)
            finally
                booted.App.Gate.Exit()
        }
    member _.Host = host

    /// The writes the app's handlers have made to response bodies (see `BodyWrites`).
    member _.BodyWrites: int64 = host.BodyWrites.Total

    /// The request to the booted app's whole pipeline.
    member _.Send(req: Req) : Task<Reply> = host.Send req

    /// The seed's `labels.json` (what its fixtures' names stand for: `emails.david`, `users.kevin`, ...), if
    /// the app was booted over one.
    member val Labels: JsonElement option = None with get, set

    /// A label of the seed as the text a URL or form takes: its string, or the number's digits.
    member this.Label(key: string) : string =
        match this.Labels with
        | None -> failwith "this app was not booted over a seed"
        | Some labels ->
            let value = labels.GetProperty key
            match value.ValueKind with
            | JsonValueKind.String -> str value
            | _ -> value.GetRawText()

    interface IAsyncDisposable with
        member _.DisposeAsync() =
            ValueTask(
                task {
                    do! (host :> IAsyncDisposable).DisposeAsync()
                    Boot.closeDatabase booted.App
                    try
                        Directory.Delete(dir, true)
                    with _ ->
                        ()
                }
            )

let private copyDir (from: string) (target: string) : unit =
    let rec copy (from: string) (target: string) =
        Directory.CreateDirectory target |> ignore
        for file in Directory.GetFiles from do
            File.Copy(file, Path.Combine(target, Path.GetFileName file))
        for sub in Directory.GetDirectories from do
            copy sub (Path.Combine(target, Path.GetFileName sub))
    copy from target

let freePort () : int =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

/// The app's environment over `root` as its storage, with the parity secret.
let configVars (root: string) (extra: (string * string) list) : (string * string) list =
    let secret = defaultArg (parityEnv "SECRET_KEY_BASE") "test-secret-key-base"
    [ "SECRET_KEY_BASE", secret
      "DISABLE_SSL", "true"
      "APP_VERSION", "parity"
      "GIT_REVISION", "parity"
      "CAMPFIRE_STORAGE_PATH", root ]
    @ extra

/// The app's configuration over `root` as its storage, with the parity secret.
let configFor (root: string) (extra: (string * string) list) : CampfireOptions =
    let vars = Map.ofList (configVars root extra)
    match CampfireOptions.fromLookup (fun name -> match Map.tryFind name vars with Some value -> value | None -> null) with
    | Ok config -> config
    | Error message -> failwith message

/// Counts what goes through a response's body writer, ahead of everything the app adds (the host's first middleware).
type private CountBodies(writes: BodyWrites) =
    interface IStartupFilter with
        member _.Configure(next: Action<IApplicationBuilder>) : Action<IApplicationBuilder> =
            Action<IApplicationBuilder>(fun app ->
                app.Use(
                    Func<HttpContext, RequestDelegate, Task>(fun http next ->
                        match http.Features.Get<IHttpResponseBodyFeature>() with
                        | null -> ()
                        | inner -> http.Features.Set<IHttpResponseBodyFeature>(CountingBody(inner, writes))
                        next.Invoke http)
                )
                |> ignore
                next.Invoke app)

/// The instance the host's provider holds, registered in `target` (last registration wins).
let private shareFrom<'T when 'T: not null and 'T: not struct> (root: IServiceProvider) (target: IServiceCollection) : unit =
    target.AddSingleton<'T>(root.GetRequiredService<'T>()) |> ignore

let private wiringOf (app: WebApplication) (logs: CapturingLoggers) : Wiring =
    let root = app.Services
    let state = root.GetRequiredService<AppState>()
    let inputs: BootInputs =
        { Config = state.Config
          Loggers = logs
          Edge = root.GetRequiredService<Campfire.App.Web.EdgeConfig>()
          Time = root.GetRequiredService<TimeProvider>() }
    let share (target: IServiceCollection) : unit =
        shareFrom<AppState> root target
        shareFrom<CableHandle> root target
        shareFrom<Jobs> root target
        shareFrom<WriteGate> root target
        shareFrom<IDbContextFactory<Campfire.Db.Ef.CampfireDb>> root target
        shareFrom<Storage> root target
        shareFrom<Campfire.Db.Ef.Env> root target
        shareFrom<Registry<AppState>> root target
        shareFrom<Microsoft.Extensions.Caching.Hybrid.HybridCache> root target
        shareFrom<TimeProvider> root target
        match root.GetService<PushDeliveries>() with
        | null -> ()
        | deliveries -> target.AddSingleton<PushDeliveries>(deliveries) |> ignore
    { App = state
      Pipeline = Boot.pipeline inputs.Edge
      ConfigureServices =
        fun target ->
            Boot.addServices target inputs
            share target
      Workers = fun target -> Boot.addWorkers target state.Config }

/// The app as `Boot.build` makes it, over an in-memory configuration (`vars`; `LOG_REQUESTS` off unless a test sets it) on two free
/// loopback-or-any ports, with the capture as its loggers, the clock `time` and the EF context options `dbOptions` (tests: `LogTo`)
/// replacing the registrations, then `more`. Started; `Test.Port` is its `TARGET_PORT`.
let private startApp time dbOptions (more: IServiceCollection -> unit) (vars: (string * string) list) (dir: string) : Task<Test> =
    task {
        let logs = new CapturingLoggers()
        let writes = BodyWrites()
        let httpPort, targetPort = freePort (), freePort ()
        let all = [ "HTTP_PORT", string httpPort; "TARGET_PORT", string targetPort; "LOG_REQUESTS", "false"; "CACHE_SIZE", "0" ] @ vars
        let configure (builder: WebApplicationBuilder) : unit =
            builder.Logging.ClearProviders() |> ignore
            builder.Configuration.AddInMemoryCollection(all |> Map.ofList |> Seq.map (fun (KeyValue(k, v)) -> KeyValuePair<string, string | null>(k, v))) |> ignore
        let overrides (services: IServiceCollection) : unit =
            services.AddSingleton<IStartupFilter>(CountBodies writes) |> ignore
            match time with
            | Some(time: TimeProvider) -> services.AddSingleton<TimeProvider>(time) |> ignore
            | None -> ()
            services.AddSingleton<IDbContextFactory<Campfire.Db.Ef.CampfireDb>>(fun sp ->
                let config = sp.GetRequiredService<CampfireOptions>()
                Campfire.Db.Ef.Data.factory (Campfire.Db.Ef.Data.options config.Storage.Database (sp.GetRequiredService<TimeProvider>()) dbOptions))
            |> ignore
            more services
        let app = Boot.build logs configure overrides
        do! app.StartAsync()
        return new Test(wiringOf app logs, dir, new Host(app, targetPort, writes), logs)
    }

/// `startApp`, again on other ports if a port picked free was taken before Kestrel bound it.
let rec private startAppRetrying (attempts: int) time dbOptions more vars dir : Task<Test> =
    task {
        try
            return! startApp time dbOptions more vars dir
        with e when attempts > 1 && (string e).Contains "address already in use" ->
            return! startAppRetrying (attempts - 1) time dbOptions more vars dir
    }

let private startWithServices time dbOptions (more: IServiceCollection -> unit) (vars: (string * string) list) (dir: string) : Task<Test> =
    startAppRetrying 5 time dbOptions more (configVars dir vars) dir

/// A private copy of seed `name` under a new directory, or a skip when the seed isn't built.
let private seedCopy (name: string) : (string * string) option =
    match Repo.seed name with
    | None ->
        Assert.Skip $"parity seed {name} is not built (parity/bin/seed build {name}); CAMPFIRE_REQUIRE_SEED=1 fails it instead"
        None
    | Some seed ->
        let dir = Directory.CreateTempSubdirectory("campfire-app-test").FullName
        Directory.CreateDirectory(Path.Combine(dir, "db")) |> ignore
        for file in [ "production.sqlite3"; "production.sqlite3-wal"; "production.sqlite3-shm" ] do
            let from = Path.Combine(seed, "db", file)
            if File.Exists from then File.Copy(from, Path.Combine(dir, "db", file))
        let storage = Path.Combine(seed, "storage")
        if Directory.Exists storage then copyDir storage (Path.Combine(dir, "files"))
        Some(dir, seed)

let private withLabels (seed: string) (test: Test) : Test =
    let labels = Path.Combine(seed, "labels.json")
    if File.Exists labels then test.Labels <- Some((JsonDocument.Parse(File.ReadAllText labels)).RootElement.Clone())
    test

/// An app booted over a private copy of seed `name`, with `dbOptions` applied to the EF context options (tests: `LogTo`).
let bootSeededServices (time: TimeProvider option) dbOptions (more: IServiceCollection -> unit) (extra: (string * string) list) (name: string) : Task<Test> =
    task {
        match seedCopy name with
        | None -> return Unchecked.defaultof<Test>
        | Some(dir, seed) ->
            let! test = startWithServices time dbOptions more extra dir
            return withLabels seed test
    }

/// `bootSeeded` with the request log on and captured (unit 8.6's RD tests).
let bootSeededRequestLog (name: string) : Task<Test> = bootSeededServices None ignore ignore [ "LOG_REQUESTS", "true" ] name

let bootSeededTimed (time: TimeProvider option) dbOptions (extra: (string * string) list) (name: string) : Task<Test> =
    bootSeededServices time dbOptions ignore extra name

let bootSeededConfigured dbOptions (extra: (string * string) list) (name: string) : Task<Test> =
    bootSeededTimed None dbOptions extra name

/// An app booted over seed `name` on `time` (the parity harness's `FrozenTimeProvider`, or a `FakeTimeProvider` the test moves).
let bootSeededFrozen (time: TimeProvider) (name: string) : Task<Test> =
    bootSeededTimed (Some time) ignore [] name

/// `bootSeededConfigured` with no environment of its own.
let bootSeededWith dbOptions (name: string) : Task<Test> = bootSeededConfigured dbOptions [] name

/// An app booted over a private copy of seed `name`, or a skip when the seed isn't built.
let bootSeeded (name: string) : Task<Test> = bootSeededWith ignore name

/// An app booted over an empty storage directory (a fresh install's database), with `extra` as its environment.
let bootEmptyWith (extra: (string * string) list) : Task<Test> =
    task {
        let dir = Directory.CreateTempSubdirectory("campfire-app-test").FullName
        return! startAppRetrying 5 None ignore ignore ([ "SECRET_KEY_BASE_DUMMY", "1"; "CAMPFIRE_STORAGE_PATH", dir ] @ extra) dir
    }

/// An app booted over an empty storage directory (a fresh install's database).
let bootEmpty () : Task<Test> = bootEmptyWith []

/// The Rails-issued session cookies and blob paths of `vectors/campfire_sessions.json`.
type SessionVector = { UserName: string; CookieHeader: string }

let sessionVectors () : SessionVector list * string list * string =
    let root = (Repo.vector "campfire_sessions").RootElement
    let sessions =
        [ for s in root.GetProperty("sessions").EnumerateArray() ->
              { UserName = str (s.GetProperty "user_name")
                CookieHeader = str (s.GetProperty "cookie_header") } ]
    let blobs = [ for b in root.GetProperty("blobs").EnumerateArray() -> str (b.GetProperty "redirect_path") ]
    sessions, blobs, str (root.GetProperty("forged").GetProperty "cookie_header")


// --- Port of controllers/presenters/test_support.rs --------------------------------------------------------------
//
// Request-level support for the controllers' tests: the whole app over a private copy of the
// reference-built `default` parity seed, signed in with a Rails-issued session cookie
// (`vectors/campfire_sessions.json`), with a tiny cookie jar and CSRF token handling.
//
// `TestApp::boot` is `bootSeeded "default"`. The Rust `Reply::frames` (how many data frames a body came in)
// can't be seen through a raw HTTP/1.1 client over a socket; `Host.BodyWrites` counts the writes the handler made
// to the response body instead (see `BodyWrites`).

let DAVID = 127326141L
let JASON = 149087659L
let KEVIN = 712064548L
let BENDER = 394959859L
let BENDER_KEY = "394959859-BenderBot123"
/// Rooms::Closed "All Talk" (David, Jason, Bender): 131 messages.
let ALL_TALK = 486777696L
/// Rooms::Open "HQ" (David can't see messages; no messages).
let HQ = 201306877L
/// Rooms::Closed "Quiet Corner", created by Kevin, David a member.
let QUIET_CORNER = 699448326L
/// A Rooms::Direct between David and Jason.
let DIRECT_DAVID_JASON = 186869642L
/// Kevin and Bender's direct room: David isn't in it.
let DIRECT_KEVIN_BENDER = 340026324L

let private chrome =
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"

/// David's Rails-issued `session_token` cookie header.
let davidCookie () : string =
    let sessions, _, _ = sessionVectors ()
    (sessions |> List.find (fun s -> s.UserName = "David")).CookieHeader

/// `Reply`'s helpers in `test_support.rs`.
type Reply with
    member this.Location: string option = this.Header "location"
    member this.ContentType: string option = this.Header "content-type"

    member this.Json: JsonDocument = JsonDocument.Parse this.Body

    member this.SetCookies: string list = this.HeaderValues "set-cookie"

/// `Req::new(method, path).form(...)`: a form post's body, URL-encoded.
let formBody (pairs: (string * string) list) : string =
    pairs |> List.map (fun (k, v) -> $"{Campfire.Rails.Ruby.Ruby.cgiEscape k}={Campfire.Rails.Ruby.Ruby.cgiEscape v}") |> String.concat "&"

/// `Req::multipart`: a body with text fields and one file field (name, filename, content type, data).
let multipartReq (req: Req) (fields: (string * string) list) (name: string, filename: string, contentType: string, data: byte[]) : Req =
    let boundary = "----campfiretestboundary"
    use body = new MemoryStream()
    let write (text: string) = body.Write(Encoding.UTF8.GetBytes text)
    for (fieldName, value) in fields do
        write $"--{boundary}\r\nContent-Disposition: form-data; name=\"{fieldName}\"\r\n\r\n{value}\r\n"
    write
        $"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"; filename=\"{filename}\"\r\nContent-Type: {contentType}\r\n\r\n"
    body.Write data
    write $"\r\n--{boundary}--\r\n"
    { req.With("content-type", $"multipart/form-data; boundary={boundary}") with Body = Some(body.ToArray()) }

/// A client that keeps cookies between requests, with a remote IP of its own (the sign-in rate limit is per IP).
type Browser(test: Test, ip: string option) =
    let cookies = SortedDictionary<string, string>(StringComparer.Ordinal)

    member _.Test = test

    /// Sets a cookie as a response would have (the value as it goes in the header).
    member _.SetCookie(name: string, value: string) : unit = cookies[name] <- value

    member _.Cookie(name: string) : string option =
        match cookies.TryGetValue name with
        | true, value -> Some value
        | _ -> None

    member _.AbsorbCookieHeader(header: string) : unit =
        for pair in header.Split ';' do
            match pair.Trim().IndexOf '=' with
            | -1 -> ()
            | at ->
                let pair = pair.Trim()
                cookies[pair.Substring(0, at)] <- pair.Substring(at + 1)

    member private _.AbsorbSetCookies(reply: Reply) : unit =
        for cookie in reply.HeaderValues "set-cookie" do
            let pair = cookie.Split(';')[0]
            match pair.IndexOf '=' with
            | -1 -> ()
            | at ->
                let name, value = pair.Substring(0, at), pair.Substring(at + 1)
                let lower = cookie.ToLowerInvariant()
                if value = "" || lower.Contains "max-age=0" || lower.Contains "1970" then cookies.Remove name |> ignore
                else cookies[name] <- value

    member this.Send(req: Req) : Task<Reply> =
        task {
            let has (name: string) = req.Headers |> List.exists (fun (k, _) -> k.Equals(name, StringComparison.OrdinalIgnoreCase))
            let mutable r = req
            if not (has "user-agent") then r <- r.With("user-agent", chrome)
            if not (has "accept") then r <- r.With("accept", "text/html,application/xhtml+xml")
            match ip with
            | Some ip -> r <- r.With("x-forwarded-for", ip)
            | None -> ()
            if cookies.Count > 0 then
                r <- r.With("cookie", cookies |> Seq.map (fun (KeyValue(k, v)) -> $"{k}={v}") |> String.concat "; ")
            let! reply = test.Send r
            this.AbsorbSetCookies reply
            return reply
        }

    member this.Get(path: string) : Task<Reply> = this.Send(get path)

    /// A write as the app's own pages make it: same-origin, by `Sec-Fetch-Site`.
    member this.Write(req: Req) : Task<Reply> = this.Send(req.With("sec-fetch-site", "same-origin"))

    /// A form post, with `_method` for the other verbs (what `button_to` and `form_with` send).
    member this.Form(meth: string, path: string, fields: (string * string) list) : Task<Reply> =
        let pairs = (if meth <> "post" then [ "_method", meth ] else []) @ fields
        this.Write((request "POST" path).Form(formBody pairs))

    /// Signs in through the sign-in page, as a browser does.
    member this.SignIn(email: string) : Task<unit> =
        task {
            let! page = this.Get "/session/new"
            Assert.True((page.Status = 200), page.Text)
            let! reply = this.Form("post", "/session", [ "email_address", email; "password", "secret123456" ])
            Assert.True((reply.Status = 302), $"sign in as {email}: {reply.Text}")
        }

/// A browser with its own remote IP.
let browser (test: Test) (ip: string) : Browser = Browser(test, Some ip)

/// `TestApp::david`: a browser signed in as David by a Rails-issued session cookie.
let david (test: Test) : Browser =
    let b = Browser(test, None)
    b.AbsorbCookieHeader(davidCookie ())
    b

/// `TestApp::anonymous`
let anonymous (test: Test) : Browser = Browser(test, None)
