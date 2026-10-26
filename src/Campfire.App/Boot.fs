// Port of rust/crates/campfire/src/app.rs (boot, the HTTP stack and the commands)
//
// Boot: configuration, the database (`db:prepare`), storage, the cable server, jobs and the HTTP stack,
// wired the way the reference's middleware and initializers are. `AppState` (`AppState.fs`) is what
// controllers, channels, jobs and integrations share. Actions reach it with `Data.app`.
//
// Request flow (mirroring the Rails middleware order): the pre-routing middleware (`Web.Edge`)
// (`ActionDispatch::SSL`, request id, `_method` override) -> the asset endpoints
// (`ActionDispatch::Static`'s place, `StaticAssets.map`) -> `UseResponseCompression` -> `/cable` (Action Cable) or the Rails route table
// (`RouteTable.dispatch`). The `/cable` endpoint is Falco's; the route table is what Falco falls back to
// for every other path, so ASP.NET Core's own routing never sees (and never rewrites) a path.
namespace Campfire.App

open System
open System.IO
open System.Threading.Tasks
open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Campfire.App.Channels
open Campfire.App.Integrations
open Campfire.Db
open Campfire.RailsCompat
open Campfire.RailsCompat.Clock
open Campfire.Kit
open Campfire.Storage
open Campfire.Views

/// A booted app: its state, the HTTP pipeline, and the job runner.
[<NoComparison; NoEquality>]
type Booted =
    { App: AppState
      /// What `Front.serve` builds the app's pipeline with (the app's `Web.Services.pipeline`).
      Pipeline: IApplicationBuilder -> unit
      /// Registers `CampfireDb`, its factory, the `WriteGate` and the `TimeProvider` (`Front.serveHosted`'s `configureServices`).
      ConfigureServices: IServiceCollection -> unit
      Jobs: Runner }

module Boot =
    // --- The HTTP stack ----------------------------------------------------------------------------------

    /// The error pages the app renders (`ActionDispatch::PublicExceptions`): empty here, filled from the web root's
    /// `404/422/500/502.html` when the pipeline is built (`Web.Services.pipeline`).
    let errorPages () : Web.ErrorPages = Web.ErrorPages()

    /// The HTTP pipeline: the asset endpoints (assets and public files), then `/cable`, then the Rails route table. The framework's
    /// response compression (K11) is behind the asset endpoints, which serve precompressed files.
    let pipeline (edge: Web.EdgeConfig) (builder: IApplicationBuilder) : unit =
        Web.Services.pipeline edge (fun http -> Campfire.Cable.Endpoint.handle (http.RequestServices.GetRequiredService<Cable>()) http) RouteTable.dispatch builder

    // --- Boot ----------------------------------------------------------------------------------------------

    /// The process clock: frozen at `CAMPFIRE_FROZEN_TIME` (an RFC 3339 timestamp such as `2024-06-01T12:00:00Z`) when set, the
    /// system clock otherwise. The parity runs pin it.
    let private clockFromEnv () : Result<SharedClock, string> =
        match Environment.GetEnvironmentVariable "CAMPFIRE_FROZEN_TIME" with
        | null -> Ok(SystemClock())
        | value when String.IsNullOrWhiteSpace value -> Ok(SystemClock())
        | value ->
            match Timestamps.tryParse (value.Trim()) with
            | Some now -> Ok(TestClock.FrozenAt now)
            | None -> Error $"CAMPFIRE_FROZEN_TIME={value} is not an RFC 3339 timestamp"

    /// Boots the app from `config`: prepares the database, restores the reference's boot-time side
    /// effects, and builds the HTTP stack.
    /// `bootWith` on a given clock (tests hand in a frozen one instead of setting the process-wide environment variable).
    let bootEdge
        (tune: Web.EdgeConfig -> Web.EdgeConfig)
        (clockOverride: SharedClock option)
        (dbOptions: Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Campfire.Db.Ef.CampfireDb> -> unit)
        (config: AppConfig)
        (loggers: ILoggerFactory)
        : Task<Booted> =
        task {
            StoragePaths.createDirs config.Storage
            let secrets = Secrets.create config.SecretKeyBase
            let clock =
                match clockOverride with
                | Some clock -> clock
                | None ->
                    match clockFromEnv () with
                    | Ok clock -> clock
                    | Error message -> failwith message

            let cableHandle = CableHandle()
            let jobs = Jobs(JobLimits.QueueCapacity, loggers.CreateLogger "campfire.jobs", cableHandle)
            let richText = AppRichText(secrets, clock, loggers.CreateLogger "campfire.rich_text")
            let time = Campfire.Db.Ef.ClockTimeProvider clock
            let dbFactory = Campfire.Db.Ef.Data.factory (Campfire.Db.Ef.Data.options config.Storage.Database time dbOptions)
            // `db:prepare` (E15): the schema goes in through EF before anything else opens the file.
            do!
                Task.Run(fun () ->
                    use context = dbFactory.CreateDbContext()
                    Campfire.Db.Schema.prepare context config.Environment time |> ignore)
            let gate = WriteGate()

            let storage =
                Storage.create (DiskService.create config.Storage.Files "local") (RailsCompat.appVerifier secrets "ActiveStorage")

            // `assume_ssl` is the edge middleware's: the cable's origin check sees the request's scheme.
            let efEnv: Campfire.Db.Ef.Env =
                { Time = time
                  Sink = jobs
                  RichText = richText
                  BcryptCost = Password.Cost }

            // config/puma.rb: `Membership.disconnect_all` when the server boots.
            use bootContext = dbFactory.CreateDbContext()
            match! Campfire.Db.Ef.Writes.run gate efEnv bootContext (fun tx -> Membership.disconnectAll tx |> ignore) with
            | Ok() -> ()
            | Error error -> failwith (DbError.display error)

            let edge = tune (Web.EdgeConfig.production config.DisableSsl (errorPages ()))

            // The fragment cache is built here, before the host, because jobs and broadcasts render outside a request; the host
            // registers this instance as its `HybridCache` (`Web.Services.add`).
            let fragments =
                let services = ServiceCollection()
                Campfire.Views.Fragments.add services config.FragmentBytes
                services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>()
            // config/initializers/web_push.rb: off without a valid VAPID key pair.
            let webPush = IntegrationJobs.webPushPool config dbFactory gate efEnv (loggers.CreateLogger "campfire.web_push")
            let app =
                AppState(config, secrets, clock, gate, dbFactory, efEnv, storage, cableHandle, jobs, webPush, fragments, loggers)

            let registry = CoreJobs.withCoreJobs ()
            IntegrationJobs.registerJobs registry
            let runner = JobRunner.start jobs app registry config.JobConcurrency

            return
                { App = app
                  Pipeline = pipeline edge
                  ConfigureServices =
                    fun services ->
                        Campfire.Db.Ef.Data.addServices services dbFactory gate time
                        services.AddSingleton<Campfire.Db.Ef.Env>(efEnv) |> ignore
                        Web.Services.add edge app services
                        CableServices.add services cableHandle
                  Jobs = runner }
        }

    /// `bootEdge` with no request log: the test hosts' (the log is `LOG_REQUESTS`'s, read by `serve`).
    let bootWithClock clockOverride dbOptions config loggers : Task<Booted> = bootEdge id clockOverride dbOptions config loggers

    /// Boots the app on the process clock (`CAMPFIRE_FROZEN_TIME` or the system clock).
    let bootWith dbOptions config loggers : Task<Booted> = bootWithClock None dbOptions config loggers

    /// `bootWith` with no extra context options (production).
    let boot (config: AppConfig) (loggers: ILoggerFactory) : Task<Booted> = bootWith ignore config loggers

    let internal servingEdge (front: Web.FrontSettings) (edge: Web.EdgeConfig) : Web.EdgeConfig =
        { edge with
            LogRequests = front.LogRequests
            CacheSize = front.CacheSize
            MaxCacheItemSize = front.MaxCacheItemSize
            MaxRequestBody = if front.MaxRequestBody > 0L then min front.MaxRequestBody edge.MaxRequestBody else edge.MaxRequestBody }

    /// Production's boot: the request log (`UseHttpLogging`, F8 of plans/design-front.md) is on unless `LOG_REQUESTS` says no, the
    /// output cache has `CACHE_SIZE` (F9), and `MAX_REQUEST_BODY` lowers the body cap below 16 MiB (F12).
    let private bootServing (front: Web.FrontSettings) (config: AppConfig) (loggers: ILoggerFactory) : Task<Booted> =
        bootEdge (servingEdge front) None ignore config loggers

    // --- Commands ----------------------------------------------------------------------------------------

    /// What the old writer thread did on `Dispose`: a `TRUNCATE` checkpoint (the WAL's pages go into the database file and the WAL
    /// is emptied), then every connection of Microsoft.Data.Sqlite's pool closed so that the file is released.
    let internal closeDatabase (app: AppState) : unit =
        try
            use context = app.DbFactory.CreateDbContext()
            context.Database.ExecuteSql($"PRAGMA wal_checkpoint(TRUNCATE)") |> ignore
        with e ->
            (app.Loggers.CreateLogger "campfire").LogWarning(e, "the shutdown checkpoint failed")
        SqliteConnection.ClearAllPools()

    [<Literal>]
    let Usage = "usage: campfire [server|backup]"

    /// How long in-flight requests and queued jobs get after SIGTERM/SIGINT.
    let ShutdownGrace = Web.Host.ShutdownGrace

    /// What follows the server's stop: jobs first (pushing a message queues its notifications on the Web Push pool), then the pool,
    /// and the database last (fold the WAL into the file and close every pooled connection; Rust drops its writer with the app).
    let private drain (booted: Booted) : Task =
        task {
            do! booted.Jobs.Shutdown ShutdownGrace
            match booted.App.WebPush with
            | Some pool -> do! pool.Shutdown()
            | None -> ()
            closeDatabase booted.App
        }

    /// `bin/boot` (F1, F13): the app's own host (`Web.Host`) on `HTTP_PORT` and `TARGET_PORT`, and with `TLS_DOMAIN` on `HTTPS_PORT`, run
    /// under ConsoleLifetime (SIGTERM, SIGINT) with a 10 s stop.
    let private serveHosted (settings: Web.FrontSettings) (config: AppConfig) (loggers: ILoggerFactory) : Task =
        task {
            let logger = loggers.CreateLogger "campfire"
            let! booted = bootServing settings config loggers
            let host = Web.Host.build settings loggers booted.ConfigureServices booted.Pipeline
            // The cable server closes every WebSocket (`server_restart`) on `ApplicationStopping` (test P12 stops the app's own host).
            host.Lifetime.ApplicationStarted.Register(fun () ->
                if settings.HasTls then
                    logger.LogInformation("Server started http=:{Http} https=:{Https} tls_domain={Domains}", settings.HttpPort, settings.HttpsPort, String.Join(",", settings.TlsDomains))
                else
                    logger.LogInformation("Server started http=:{Http}", settings.HttpPort))
            |> ignore
            host.Lifetime.ApplicationStopping.Register(fun () -> logger.LogInformation "shutting down") |> ignore
            // A failed start (a port in use) still drains what booted, then fails the command.
            let! failure =
                task {
                    try
                        do! host.RunAsync()
                        return None
                    with
                    | :? OperationCanceledException ->
                        logger.LogWarning "requests still running at shutdown were abandoned"
                        return None
                    | e -> return Some e
                }
            do! drain booted
            match failure with
            | Some e -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw()
            | None -> ()
            logger.LogInformation "Server stopped"
        }

    /// `bin/boot`: the app's own host.
    let serve (config: AppConfig) (loggers: ILoggerFactory) : Task =
        serveHosted (Web.FrontSettings.fromEnv ()) config loggers

    /// SQLite's online backup of the live database at `source` into a new file at `target`: `sqlite3_backup_step(-1)`
    /// (every page in one step), retried `attempts` times, `delay` apart, while the source is busy or locked
    /// (`copy_database`). `busyTimeout` is the source connection's own busy handler, in milliseconds.
    let internal copyDatabaseWith (busyTimeout: int) (attempts: int) (delay: TimeSpan) (source: string) (target: string) : unit =
        let open' (path: string) (mode: SqliteOpenMode) =
            let builder = SqliteConnectionStringBuilder()
            builder.DataSource <- path
            builder.Mode <- mode
            builder.Pooling <- false
            let connection = new SqliteConnection(builder.ToString())
            connection.Open()
            connection
        use source = open' source SqliteOpenMode.ReadOnly
        use command = source.CreateCommand()
        command.CommandText <- $"PRAGMA busy_timeout = {busyTimeout}"
        command.ExecuteNonQuery() |> ignore
        use target = open' target SqliteOpenMode.ReadWriteCreate
        let fail (message: string) (code: int) : exn = SqliteException(message, code)
        let backup = SQLitePCL.raw.sqlite3_backup_init (target.Handle, "main", source.Handle, "main")
        if isNull backup then
            raise (fail ((SQLitePCL.raw.sqlite3_errmsg target.Handle).utf8_to_string()) (SQLitePCL.raw.sqlite3_errcode target.Handle))
        try
            let mutable tried = 0
            let mutable finished = false
            while not finished do
                match SQLitePCL.raw.sqlite3_backup_step (backup, -1) with
                | rc when rc = SQLitePCL.raw.SQLITE_DONE -> finished <- true
                | rc when (rc = SQLitePCL.raw.SQLITE_BUSY || rc = SQLitePCL.raw.SQLITE_LOCKED) ->
                    if tried < attempts then
                        tried <- tried + 1
                        Threading.Thread.Sleep delay
                    else
                        raise (fail "backup did not finish: source is busy or locked" rc)
                | rc -> raise (fail ((SQLitePCL.raw.sqlite3_errmsg source.Handle).utf8_to_string()) rc)
        finally
            SQLitePCL.raw.sqlite3_backup_finish backup |> ignore

    let private copyDatabase (source: string) (target: string) : unit =
        copyDatabaseWith 5000 50 (TimeSpan.FromMilliseconds 100.0) source target

    /// `script/admin/prepare-backup`: `SQLite3::Backup` of the live database, all pages in one step,
    /// into `storage/backups/<database file name>`.
    let backup (config: AppConfig) (logger: ILogger) : unit =
        let destination = StoragePaths.backupFile config.Storage
        let dir =
            match Path.GetDirectoryName destination with
            | null
            | "" -> "."
            | dir -> dir
        Directory.CreateDirectory dir |> ignore
        // Written to a file of its own beside the destination and renamed over it, so a failed,
        // interrupted or concurrent backup never leaves a torn file where ONCE (and `post-restore`)
        // expect the last good one. A failed one's file is deleted.
        let partial = Path.Combine(dir, $".backup-{Guid.NewGuid():N}.sqlite3")
        try
            copyDatabase config.Storage.Database partial
            File.Move(partial, destination, true)
            logger.LogInformation("backup written path={Path}", destination)
        finally
            if File.Exists partial then File.Delete partial

    /// The console logger's options: one line, UTC microsecond timestamps, and no colour (a terminal would otherwise get ANSI
    /// codes around the level).
    let internal configureConsole (options: Microsoft.Extensions.Logging.Console.SimpleConsoleFormatterOptions) : unit =
        options.SingleLine <- true
        options.UseUtcTimestamp <- true
        options.TimestampFormat <- "yyyy-MM-ddTHH:mm:ss.ffffffZ "
        options.ColorBehavior <- Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Disabled

    /// A logger factory at the level `RAILS_LOG_LEVEL` names (or `CAMPFIRE_LOG`, which replaces it), 
    let createLoggerFactory (config: AppConfig) : ILoggerFactory =
        let level =
            match
                (match Environment.GetEnvironmentVariable "CAMPFIRE_LOG" with
                 | null
                 | "" -> config.LogLevel
                 | value -> value)
                    .ToLowerInvariant()
            with
            | "trace" -> LogLevel.Trace
            | "debug" -> LogLevel.Debug
            | "warn" -> LogLevel.Warning
            | "error"
            | "fatal"
            | "unknown" -> LogLevel.Error
            | _ -> LogLevel.Information
        LoggerFactory.Create(fun builder ->
            builder
                .AddSimpleConsole(configureConsole)
                .SetMinimumLevel(level)
                .AddFilter("Microsoft", LogLevel.Warning)
                .AddFilter("System", LogLevel.Warning)
                .AddFilter("Microsoft.AspNetCore.HttpLogging", LogLevel.Information)
            |> ignore)

    /// Run a command; a failure is `Error: <message>` on stderr and exit code 1, as `anyhow`'s `main` ends.
    let internal execute (config: AppConfig) (loggers: ILoggerFactory) (command: string option) : int =
        try
            match command with
            | None
            | Some "server" ->
                match OpenFiles.raiseOpenFileLimit () with
                | ValueSome limit -> (loggers.CreateLogger "campfire").LogInformation("open files limit={Limit}", limit)
                | ValueNone -> ()
                (serve config loggers).GetAwaiter().GetResult()
                0
            | Some "backup" ->
                backup config (loggers.CreateLogger "campfire")
                0
            | Some other ->
                eprintfn "Error: unknown command %A\n%s" other Usage
                1
        with ex ->
            eprintfn "Error: %s" ex.Message
            1

    /// The binary's entry point.
    ///
    /// - `campfire` / `campfire server`: serve the app behind the front server, as `bin/boot` did with
    ///   Thruster in front of `bin/start-app` (see `Campfire.Kit.Front`).
    /// - `campfire backup`: the ONCE `pre-backup` hook (`script/admin/prepare-backup`): snapshot the live
    ///   database into `storage/backups/` with SQLite's online backup API.
    ///
    /// The ONCE `post-restore` hook stays the reference's shell script (`hooks/post-restore`): copy
    /// `storage/backups/<env>.sqlite3` over `storage/db/<env>.sqlite3` and delete its `-wal` and `-shm`
    /// files; the next boot's `db:prepare` picks it up.
    let run (argv: string[]) : int =
        let command = Array.tryHead argv
        if command = Some "-h" || command = Some "--help" then
            printfn "%s" Usage
            0
        else
            match AppConfig.fromEnv () with
            | Error message ->
                eprintfn "Error: %s" message
                1
            | Ok config ->
                use loggers = createLoggerFactory config
                execute config loggers command
