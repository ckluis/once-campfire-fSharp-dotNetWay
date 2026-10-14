// Design: plans/design-data-ef.md section 3.1 (E6-E8, E10).
namespace Campfire.Db.Ef

open System
open System.Linq
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Infrastructure

module Data =

    /// The options every context uses: `Data Source=<path>`, a 1 s command timeout (so Microsoft.Data.Sqlite's busy retry ends
    /// when SQLite's busy handler has), snake case, no tracking, the three interceptors, plus `extra` (tests: `LogTo`).
    let options (path: string) (time: TimeProvider) (extra: DbContextOptionsBuilder<CampfireDb> -> unit) : DbContextOptions<CampfireDb> =
        let builder =
            DbContextOptionsBuilder<CampfireDb>()
                .UseSqlite($"Data Source={path}", fun sqlite -> sqlite.CommandTimeout 1 |> ignore)
                .UseSnakeCaseNamingConvention()
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                .AddInterceptors(SqlitePragmas(), RailsTimestamps time, RailsParameterGuard())
        extra builder
        builder.Options

    /// A pooled factory (E7): a disposed context returns to the pool, and the next lease gets it back.
    let factory (options: DbContextOptions<CampfireDb>) : IDbContextFactory<CampfireDb> =
        PooledDbContextFactory<CampfireDb>(options, 1024)

    /// `FirstOrDefault` as an option (F# sees EF's result as nullable).
    let tryFirst (query: IQueryable<'T>) : 'T option when 'T : not null =
        match query.FirstOrDefault() with
        | null -> None
        | found -> Some found
