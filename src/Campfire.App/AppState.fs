// Port of `AppState`, `AppCtx` and `db_error` in rust/crates/campfire/src/app.rs
//
// `AppState` is what controllers, channels, jobs and integrations share. Actions reach it with
// `c.App` (`CtxApp`). Boot (`App.boot`) builds it.
namespace Campfire.App

open System
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.Caching.Hybrid
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Campfire.App.Channels
open Campfire.App.Integrations
open Campfire.Db
open Campfire.RailsCompat
open Campfire.Storage
open Campfire.Views

module AppErrors =
    /// A database error raised in an action, as Active Record's `rescue_responses` answer it
    /// (activerecord/lib/active_record/railtie.rb): `RecordNotFound` is a 404, `RecordInvalid` a 422,
    /// and anything else a 500.
    let dbError (error: DbError) : Campfire.App.Web.Error =
        match error with
        | RecordNotFound _ -> Campfire.App.Web.NotFound
        | RecordInvalid _ -> Campfire.App.Web.WithStatus(422, Exception(DbError.display error))
        | Sqlite e -> Campfire.App.Web.Internal e
        | Other e -> Campfire.App.Web.Internal e

/// Everything that outlives a request.
[<Sealed; NoComparison; NoEquality>]
type AppState
    (
        config: AppConfig,
        secrets: Secrets,
        time: TimeProvider,
        gate: WriteGate,
        dbFactory: IDbContextFactory<Campfire.Db.Ef.CampfireDb>,
        env: Campfire.Db.Ef.Env,
        storage: Storage,
        cable: CableHandle,
        jobs: Jobs,
        webPush: PushDeliveries option,
        fragments: HybridCache,
        loggers: ILoggerFactory,
        http: System.Net.Http.IHttpClientFactory
    ) =
    member _.Config = config
    member _.Secrets = secrets
    member _.Time: TimeProvider = time

    /// EF Core: `Writes.run app.Gate app.Env ctx` for jobs, channels and integrations
    /// (`use ctx = app.DbFactory.CreateDbContext()`), a scoped `CampfireDb` from `c.Db` in actions.
    member _.DbFactory = dbFactory

    /// The one writer at a time (SQLite has one): every `Writes.run` holds it from before `BEGIN IMMEDIATE` to the end of its
    /// after-commit work.
    member _.Gate : WriteGate = gate

    member _.Env = env
    member _.Storage = storage
    /// The cable server and the app's broadcasts: DI singletons of the host (`CableServices`), read through the handle.
    member _.Cable: Cable = cable.Resolve<Cable>()
    member _.Broadcasts: Broadcasts = cable.Resolve<Broadcasts>()
    member _.Jobs = jobs

    /// `config.x.web_push_pool`; `None` when Web Push is off (no valid VAPID keys).
    member _.WebPush = webPush

    /// `Rails.cache` for view fragments (`cache message do`) and the kept compressed pages: HybridCache over a size-limited memory
    /// cache, the host's `HybridCache` as well (`Web.Services.add`). Fetched up front, awaited, by the presenters, the controllers
    /// and the broadcasts.
    member _.Fragments: HybridCache = fragments

    /// The named outgoing clients (`Clients`): "opengraph", "push" and "webhook".
    member _.Http: System.Net.Http.IHttpClientFactory = http

    /// For what Rust logs with `tracing` from anywhere (integrations, mostly).
    member _.Loggers = loggers

    /// The key pages offer browsers to subscribe with: none while Web Push is off, so that browsers
    /// don't subscribe to notifications that would never be sent.
    member _.VapidPublicKey: string option =
        match webPush with
        | Some _ -> config.VapidPublicKey
        | None -> None

    /// `Reads.run` over a context leased for the call, for what has no request (jobs, integrations), with errors mapped as `Read`
    /// maps them.
    member _.ReadEf<'T>(f: Campfire.Db.Ef.CampfireDb -> 'T) : Task<Result<'T, Campfire.App.Web.Error>> =
        use context = dbFactory.CreateDbContext()
        Task.FromResult(Campfire.Db.Ef.Reads.run context f |> Result.mapError AppErrors.dbError)

    /// `Writes.run` over a context leased for the write, for what has no request, with errors mapped as `Write` maps them.
    member _.WriteEf<'T>(f: Campfire.Db.Ef.Tx -> 'T) : Task<Result<'T, Campfire.App.Web.Error>> =
        task {
            use context = dbFactory.CreateDbContext()
            let! result = Campfire.Db.Ef.Writes.run gate env context f
            return result |> Result.mapError AppErrors.dbError
        }

type App = AppState
