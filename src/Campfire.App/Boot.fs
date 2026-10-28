// Port of rust/crates/campfire/src/app.rs (boot, the HTTP stack and the commands)
//
// Boot (plans/design-jobs.md J14): one `WebApplication` builder (`Boot.build`) with the configuration, the database (`db:prepare`
// in `DatabaseStartup`), storage, the cable server, jobs, the outgoing clients and the HTTP stack registered in its provider,
// wired the way the reference's middleware and initializers are. `AppState` (`AppState.fs`) is what controllers, channels, jobs
// and integrations share, a singleton made from those. Actions reach it with `Data.app`.
//
// Request flow (mirroring the Rails middleware order): the pre-routing middleware (`Web.Edge`)
// (`ActionDispatch::SSL`, request id, `_method` override) -> the asset endpoints
// (`ActionDispatch::Static`'s place, `StaticAssets.map`) -> `UseResponseCompression` -> `/cable` (Action Cable) or the Rails route table
// (`RouteTable.dispatch`). The `/cable` endpoint is Falco's; the route table is what Falco falls back to
// for every other path, so ASP.NET Core's own routing never sees (and never rewrites) a path.
namespace Campfire.App

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks
open Falco
open Falco.Routing
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Campfire.App.Channels
open Campfire.App.Integrations
open Campfire.Db
open Campfire.Rails
open Campfire.App.Platform
open Campfire.Storage
open Campfire.Views

/// What the app's services are made from: the configuration, the loggers, the edge settings and the clock (registered as the
/// `TimeProvider`, which the services resolve, so a test replaces it in `overrides`).
[<NoComparison; NoEquality>]
type BootInputs =
    { Config: CampfireOptions
      Loggers: ILoggerFactory
      Edge: Web.EdgeConfig
      Time: TimeProvider }

/// `db:prepare` and `Membership.disconnect_all` (config/puma.rb), run by the host before any hosted service starts and before Kestrel
/// binds (`IHostedLifecycleService.StartingAsync`); registered first.
[<Sealed>]
type DatabaseStartup
    (
        config: CampfireOptions,
        time: TimeProvider,
        factory: IDbContextFactory<Campfire.Db.Ef.CampfireDb>,
        gate: WriteGate,
        env: Campfire.Db.Ef.Env
    ) =
    /// `storage/{db,files}` exist (config/initializers/storage_paths.rb), the schema goes in through EF before anything else
    /// opens the file (E15), and every membership's connection count resets.
    member _.Prepare() : Task =
        task {
            StoragePaths.createDirs config.Storage
            do!
                Task.Run(fun () ->
                    use context = factory.CreateDbContext()
                    Campfire.Db.Schema.prepare context config.Environment time |> ignore)
            use bootContext = factory.CreateDbContext()
            match! Campfire.Db.Ef.Writes.run gate env bootContext (fun tx -> Membership.disconnectAll tx |> ignore) with
            | Ok() -> ()
            | Error error -> failwith (DbError.display error)
        }

    interface IHostedLifecycleService with
        member this.StartingAsync(_: Threading.CancellationToken) : Task = this.Prepare()
        member _.StartAsync(_: Threading.CancellationToken) : Task = Task.CompletedTask
        member _.StartedAsync(_: Threading.CancellationToken) : Task = Task.CompletedTask
        member _.StoppingAsync(_: Threading.CancellationToken) : Task = Task.CompletedTask
        member _.StopAsync(_: Threading.CancellationToken) : Task = Task.CompletedTask
        member _.StoppedAsync(_: Threading.CancellationToken) : Task = Task.CompletedTask

module Boot =
    // --- The HTTP stack ----------------------------------------------------------------------------------

    /// The error pages the app renders (`ActionDispatch::PublicExceptions`): empty here, filled from the web root's
    /// `404/422/500/502.html` when the pipeline is built (`Web.Services.pipeline`).
    let errorPages () : Web.ErrorPages = Web.ErrorPages()

    /// The HTTP pipeline: the asset endpoints (assets and public files), then `/cable`, then the Rails route table. The framework's
    /// response compression (K11) is behind the asset endpoints, which serve precompressed files.
    let pipeline (edge: Web.EdgeConfig) (builder: IApplicationBuilder) : unit =
        Web.Services.pipeline edge (fun http -> Campfire.Cable.Endpoint.handle (http.RequestServices.GetRequiredService<Cable>()) http) RouteTable.dispatch builder

    // --- Registrations -----------------------------------------------------------------------------------

    /// Everything the app is made of, in `services`: the data services, the job queues, storage, the cable, the outgoing clients (J2),
    /// the fragment cache, Web Push, the request layer's services (`Web.Services.add`), and `AppState`, made from those. No hosted
    /// service but the cable's (`addWorkers` adds the app's, `DatabaseStartup` the database's). The loggers are the caller's to
    /// register as the host's (`build`).
    let addServices (services: IServiceCollection) (inputs: BootInputs) : unit =
        let config = inputs.Config
        let time = inputs.Time
        let loggers = inputs.Loggers
        let secrets = Secrets.create config.SecretKeyBase
        // `build` has registered `CampfireOptions` as the validated `IOptions` value (J13) and that registration stands; a test host's
        // second wiring (no options registration) gets the bootstrap read, which the same configuration makes equal.
        services.TryAddSingleton<CampfireOptions>(config)
        services.AddSingleton<Secrets>(secrets).AddSingleton<TimeProvider>(time) |> ignore
        services.AddSingleton<IDbContextFactory<Campfire.Db.Ef.CampfireDb>>(fun sp ->
            Campfire.Db.Ef.Data.factory (
                Campfire.Db.Ef.Data.options (sp.GetRequiredService<CampfireOptions>()).Storage.Database (sp.GetRequiredService<TimeProvider>()) ignore
            ))
        |> ignore
        services.AddScoped<Campfire.Db.Ef.CampfireDb>(fun sp ->
            sp.GetRequiredService<IDbContextFactory<Campfire.Db.Ef.CampfireDb>>().CreateDbContext())
        |> ignore
        services.AddSingleton<WriteGate>(fun _ -> WriteGate()) |> ignore
        services.AddSingleton<Jobs>(fun sp -> Jobs(JobLimits.QueueCapacity, loggers.CreateLogger "campfire.jobs", sp.GetRequiredService<CableHandle>()))
        |> ignore
        services.AddSingleton<Campfire.Db.Ef.Env>(fun sp ->
            let time = sp.GetRequiredService<TimeProvider>()
            let env: Campfire.Db.Ef.Env =
                { Time = time
                  Sink = sp.GetRequiredService<Jobs>()
                  RichText = AppRichText(secrets, time, loggers.CreateLogger "campfire.rich_text")
                  BcryptCost = Password.Cost }
            env)
        |> ignore
        services.AddSingleton<Storage>(fun sp ->
            Storage.create (DiskService.create (sp.GetRequiredService<CampfireOptions>()).Storage.Files "local") (Secrets.appVerifier secrets "ActiveStorage"))
        |> ignore
        services.AddSingleton<Registry<AppState>>(fun _ ->
            let registry = CoreJobs.withCoreJobs ()
            IntegrationJobs.registerJobs registry
            registry)
        |> ignore
        // The fragment cache: one HybridCache over a size-limited memory cache, with no L2 (the session's distributed cache is not one).
        Campfire.Views.Fragments.add services (config.FragmentBytes + config.ResponseCacheBytes)
        services.AddSingleton<Web.PageCacheStats>(fun _ -> Web.PageCacheStats()) |> ignore
        services.AddSingleton<Microsoft.Extensions.Caching.Hybrid.IHybridCacheSerializer<Web.PageEntry>, Web.PageEntrySerializer>() |> ignore
        // The outgoing clients (J2), in the host's provider: the factory and its handlers are disposed with the host.
        Clients.add services
        // config/initializers/web_push.rb: off without a valid VAPID key pair.
        match IntegrationJobs.vapidOrOff config (loggers.CreateLogger "campfire.web_push") with
        | Some vapid ->
            services.AddSingleton<PushDeliveries>(fun sp ->
                let push: PushEnv =
                    { Http = sp.GetRequiredService<IHttpClientFactory>()
                      Resolver = sp.GetRequiredService<IResolver>()
                      Time = sp.GetRequiredService<TimeProvider>() }
                IntegrationJobs.pushDeliveries
                    vapid
                    (sp.GetRequiredService<IDbContextFactory<Campfire.Db.Ef.CampfireDb>>())
                    (sp.GetRequiredService<WriteGate>())
                    (sp.GetRequiredService<Campfire.Db.Ef.Env>())
                    push
                    (loggers.CreateLogger "campfire.web_push"))
            |> ignore
        | None -> ()
        services.AddSingleton<AppState>(fun sp ->
            AppState(
                sp.GetRequiredService<CampfireOptions>(),
                secrets,
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<WriteGate>(),
                sp.GetRequiredService<IDbContextFactory<Campfire.Db.Ef.CampfireDb>>(),
                sp.GetRequiredService<Campfire.Db.Ef.Env>(),
                sp.GetRequiredService<Storage>(),
                sp.GetRequiredService<CableHandle>(),
                sp.GetRequiredService<Jobs>(),
                (match sp.GetService<PushDeliveries>() with
                 | null -> None
                 | deliveries -> Some deliveries),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(),
                loggers,
                sp.GetRequiredService<IHttpClientFactory>()
            ))
        |> ignore
        Web.Services.add inputs.Edge secrets services
        CableServices.add services (CableHandle())

    /// The hosted services that work off the app's queues: Web Push deliveries (when there are keys), then the job runner. Hosted
    /// services stop in reverse order, so jobs stop before deliveries (a job queues them), and the web host, registered last, stops
    /// before both.
    let addWorkers (services: IServiceCollection) (config: CampfireOptions) : unit =
        match VapidConfig.FromConfig config with
        | Ok _ -> services.AddHostedService<PushDeliveries>(fun sp -> sp.GetRequiredService<PushDeliveries>()) |> ignore
        | Error _ -> ()
        services.AddHostedService<JobRunner<AppState>>(fun sp ->
            new JobRunner<AppState>(
                sp.GetRequiredService<Jobs>(),
                sp.GetRequiredService<AppState>(),
                sp.GetRequiredService<Registry<AppState>>(),
                (sp.GetRequiredService<CampfireOptions>()).JobConcurrency
            ))
        |> ignore

    let internal servingEdge (front: Web.FrontSettings) (edge: Web.EdgeConfig) : Web.EdgeConfig =
        { edge with
            LogRequests = front.LogRequests
            CacheSize = front.CacheSize
            MaxCacheItemSize = front.MaxCacheItemSize
            MaxRequestBody = if front.MaxRequestBody > 0L then min front.MaxRequestBody edge.MaxRequestBody else edge.MaxRequestBody }

    // --- Build -----------------------------------------------------------------------------------------------

    /// The edge settings, from the two option types (`DISABLE_SSL`, then Thruster's `LOG_REQUESTS`, cache and body limits).
    let edgeOf (config: CampfireOptions) (settings: Web.FrontSettings) : Web.EdgeConfig =
        servingEdge settings (Web.EdgeConfig.production config.DisableSsl (errorPages ()))

    /// The app's host (J14): one `WebApplication` builder. The configuration (`CampfireOptions`, `FrontSettings`) is the builder's, which
    /// `configure` may extend before anything is read (an in-memory provider); `overrides` runs after the registrations, so that what it
    /// adds replaces them. Both option types are `IOptions` (J13), filled from it, validated and checked at start: a bad configuration
    /// fails `StartAsync` with its message (`DatabaseStartup` is the first to resolve it), before a listener binds. What the shape of
    /// the host depends on (the TLS store, the edge, the secrets the registrations read) is read once more at build time through
    /// `CampfireOptions.read`, which records a problem instead of throwing; the options themselves are never read until the host starts.
    /// `DatabaseStartup` is registered first, so the database is ready before any hosted service starts and before Kestrel binds. The
    /// caller runs the host (`RunAsync`) and then `closeDatabase`.
    let build (loggers: ILoggerFactory) (configure: WebApplicationBuilder -> unit) (overrides: IServiceCollection -> unit) : WebApplication =
        let builder = WebApplication.CreateSlimBuilder(Web.StaticAssets.hostOptions Web.Host.hostArgs)
        configure builder
        let lookup = CampfireOptions.ofConfiguration builder.Configuration
        let config = CampfireOptions.read lookup
        let settings = Web.FrontSettings.fromLookup lookup
        // The process clock: `CAMPFIRE_FROZEN_TIME` freezes `Time.current` (the parity runs pin it), the system clock otherwise. A bad
        // value is a validation failure at start, so the clock falls back to the system's until then.
        let time =
            match HarnessClock.fromValue (match config.FrozenTime with Some value -> value | None -> null) with
            | Ok time -> time
            | Error _ -> TimeProvider.System
        let inputs =
            { Config = config
              Loggers = loggers
              Edge = edgeOf config settings
              Time = time }
        let services = builder.Services
        services.AddSingleton<ILoggerFactory>(loggers) |> ignore
        CampfireOptionsRegistration.add services
        Web.FrontSettingsRegistration.add services
        services.AddHostedService<DatabaseStartup>(fun sp ->
            DatabaseStartup(
                sp.GetRequiredService<CampfireOptions>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<IDbContextFactory<Campfire.Db.Ef.CampfireDb>>(),
                sp.GetRequiredService<WriteGate>(),
                sp.GetRequiredService<Campfire.Db.Ef.Env>()
            ))
        |> ignore
        addServices services inputs
        addWorkers services config
        overrides services
        let store = Web.Host.configure builder settings (loggers.CreateLogger "campfire")
        let app = builder.Build()
        Web.Host.mapHttpPort app settings store
        pipeline inputs.Edge app
        app

    // --- Commands ----------------------------------------------------------------------------------------

    /// What the old writer thread did on `Dispose`: a `TRUNCATE` checkpoint (the WAL's pages go into the database file and the WAL
    /// is emptied), then every connection of Microsoft.Data.Sqlite's pool closed so that the file is released. On a connection of
    /// its own, since the host (and the pooled contexts with it) is gone by then.
    let internal closeDatabaseAt (path: string) (logger: ILogger) : unit =
        try
            let builder = SqliteConnectionStringBuilder()
            builder.DataSource <- path
            builder.Mode <- SqliteOpenMode.ReadWrite
            builder.Pooling <- false
            use connection = new SqliteConnection(builder.ToString())
            connection.Open()
            use command = connection.CreateCommand()
            command.CommandText <- "PRAGMA busy_timeout = 5000; PRAGMA wal_checkpoint(TRUNCATE)"
            command.ExecuteNonQuery() |> ignore
        with e ->
            logger.LogWarning(e, "the shutdown checkpoint failed")
        SqliteConnection.ClearAllPools()

    let internal closeDatabase (app: AppState) : unit =
        closeDatabaseAt app.Config.Storage.Database (app.Loggers.CreateLogger "campfire")

    [<Literal>]
    let Usage = "usage: campfire [server|backup]"

    /// How long in-flight requests and queued jobs get after SIGTERM/SIGINT.
    let ShutdownGrace = Web.Host.ShutdownGrace

    /// `bin/boot` (F1, F13): the app's own host (`build`) on `HTTP_PORT` and `TARGET_PORT`, and with `TLS_DOMAIN` on `HTTPS_PORT`, run
    /// under ConsoleLifetime (SIGTERM, SIGINT) with a 10 s stop; then the database closes (fold the WAL into the file and close every
    /// pooled connection; Rust drops its writer with the app). The host's stop has already drained the jobs and the Web Push deliveries,
    /// as hosted services.
    let serve (loggers: ILoggerFactory) : Task =
        task {
            let logger = loggers.CreateLogger "campfire"
            let host = build loggers ignore ignore
            let settings = host.Services.GetRequiredService<Web.FrontSettings>()
            let databasePath = host.Services.GetRequiredService<CampfireOptions>().Storage.Database
            // The cable server closes every WebSocket (`server_restart`) on `ApplicationStopping` (test P12 stops the app's own host).
            host.Lifetime.ApplicationStarted.Register(fun () ->
                if settings.HasTls then
                    logger.LogInformation("Server started http=:{Http} https=:{Https} tls_domain={Domains}", settings.HttpPort, settings.HttpsPort, String.Join(",", settings.TlsDomains))
                else
                    logger.LogInformation("Server started http=:{Http}", settings.HttpPort))
            |> ignore
            host.Lifetime.ApplicationStopping.Register(fun () -> logger.LogInformation "shutting down") |> ignore
            // A failed start (a port in use) still closes the database, then fails the command.
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
            closeDatabaseAt databasePath logger
            match failure with
            | Some e -> System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw()
            | None -> ()
            logger.LogInformation "Server stopped"
        }

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
    let backup (config: CampfireOptions) (logger: ILogger) : unit =
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

    /// A logger factory at the level `RAILS_LOG_LEVEL` names (or `CAMPFIRE_LOG`, which replaces it); the framework's own categories
    /// at Warning, except the request log (`Microsoft.AspNetCore.HttpLogging`, F8), which is at Information whatever the level.
    let createLoggerFactory (config: CampfireOptions) : ILoggerFactory =
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
    let internal execute (config: CampfireOptions) (loggers: ILoggerFactory) (command: string option) : int =
        try
            match command with
            | None
            | Some "server" ->
                match OpenFiles.raiseOpenFileLimit () with
                | ValueSome limit -> (loggers.CreateLogger "campfire").LogInformation("open files limit={Limit}", limit)
                | ValueNone -> ()
                (serve loggers).GetAwaiter().GetResult()
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
    /// - `campfire` / `campfire server`: serve the app on its own host (`Web/Host.fs`: Kestrel's listeners, TLS and
    ///   ACME), which does what Thruster in front of `bin/start-app` did for the reference's `bin/boot`.
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
            match CampfireOptions.fromEnv () with
            | Error message ->
                eprintfn "Error: %s" message
                1
            | Ok config ->
                use loggers = createLoggerFactory config
                execute config loggers command
