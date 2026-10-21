// Design: plans/design-data-ef.md section 3.1 (E6-E8, E10).
namespace Campfire.Db.Ef

open System
open System.Linq
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure
open Microsoft.Extensions.DependencyInjection
open Campfire.Db

module Library =
    /// SQLite's library-wide settings for every connection of the process: each connection is used by one thread at a time (a
    /// pooled context is leased to one request), so SQLite's per-connection mutex (`SQLITE_CONFIG_MULTITHREAD`, taken on every
    /// API call) and the memory statistics' global mutex (`SQLITE_CONFIG_MEMSTATUS` off, taken on every allocation) are pure
    /// cost. They must be set before the first connection initializes SQLite; a library already initialized refuses them and
    /// the connections keep SQLite's defaults.
    let private configure =
        lazy
            (SQLitePCL.Batteries_V2.Init()
             SQLitePCL.raw.sqlite3_config 2 |> ignore
             SQLitePCL.raw.sqlite3_config (9, 0) |> ignore)

    let ensureConfigured () : unit = configure.Force()

module Data =

    /// The options every context uses: `Data Source=<path>`, a 1 s command timeout (so Microsoft.Data.Sqlite's busy retry ends
    /// when SQLite's busy handler has), snake case, no tracking, the four interceptors, plus `extra` (tests: `LogTo`).
    let options (path: string) (time: TimeProvider) (extra: DbContextOptionsBuilder<CampfireDb> -> unit) : DbContextOptions<CampfireDb> =
        // SQLite's library-wide settings (no memory statistics mutex) must be made before the first connection of the process.
        Library.ensureConfigured ()
        FastReads.ensureConfigured ()
        let builder =
            DbContextOptionsBuilder<CampfireDb>()
                .UseSqlite($"Data Source={path}", fun sqlite -> sqlite.CommandTimeout 1 |> ignore)
                .UseSnakeCaseNamingConvention()
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .AddInterceptors(SqlitePragmas(), RailsTimestamps time, RailsParameterGuard(), SingleWriterGuard())
        extra builder
        builder.Options

    /// The first row of a `FromSql` query as an option, without composing it (no lambda: nothing for F# to rebuild into an expression
    /// tree, ~60 us a statement, measured in unit 1.10). For the lookups every request makes; the SQL ends in `LIMIT 1`.
    let sqlFirst (query: IQueryable<'T>) : 'T option when 'T : not null =
        query.AsEnumerable() |> Seq.tryHead

    /// A pooled factory (E7): a disposed context returns to the pool, and the next lease gets it back.
    let factory (options: DbContextOptions<CampfireDb>) : IDbContextFactory<CampfireDb> =
        PooledDbContextFactory<CampfireDb>(options, 1024)

    /// `FirstOrDefault` as an option (F# sees EF's result as nullable).
    let tryFirst (query: IQueryable<'T>) : 'T option when 'T : not null =
        match query.FirstOrDefault() with
        | null -> None
        | found -> Some found

    /// The context's lifetime in the request pipeline: the pooled factory as the singleton, a scoped context leased from it
    /// (returned to the pool when the request's scope ends), the write gate and the clock (design section 3.5).
    let addServices (services: IServiceCollection) (factory: IDbContextFactory<CampfireDb>) (gate: WriteGate) (time: TimeProvider) : unit =
        services.AddSingleton<IDbContextFactory<CampfireDb>>(factory) |> ignore
        services.AddScoped<CampfireDb>(fun _ -> factory.CreateDbContext()) |> ignore
        services.AddSingleton<WriteGate>(gate) |> ignore
        services.AddSingleton<TimeProvider>(time) |> ignore

    /// `find_by!`: the record, or `RecordNotFound model` (a 404 in an action, as Active Record's rescue responses have it).
    let found (model: string) (value: 'T | null) : 'T when 'T : not null =
        match value with
        | null -> Err.fail (RecordNotFound model)
        | found -> found

    /// `Model.create!`: add, save at once (inside the write's `BEGIN IMMEDIATE`) and return the entity, detached, with its id and
    /// `RailsTimestamps`' times filled in (the tracker stays empty, as with `NoTracking`).
    let insert (db: CampfireDb) (entity: 'T) : 'T when 'T : not null and 'T : not struct =
        let entry = db.Add entity
        db.SaveChanges() |> ignore
        entry.State <- EntityState.Detached
        entity

    /// `record.update!`: `after` is the record with its changes; only the properties that differ from `before` are written, and
    /// nothing at all (not even `updated_at`) when none does. `RailsTimestamps` stamps `updated_at` on the saved entity.
    let update (db: CampfireDb) (before: 'T) (after: 'T) : 'T when 'T : not null and 'T : not struct =
        let entry = db.Attach after
        for property in entry.Metadata.GetProperties() do
            match property.PropertyInfo with
            | null -> ()
            | info ->
                if not (Object.Equals(info.GetValue before, info.GetValue after)) then
                    entry.Property(property.Name).IsModified <- true
        db.SaveChanges() |> ignore
        entry.State <- EntityState.Detached
        after

    /// Deletes a row (`record.delete`: no callbacks).
    let remove (db: CampfireDb) (entity: 'T) : unit when 'T : not null and 'T : not struct =
        db.Remove entity |> ignore
        db.SaveChanges() |> ignore
