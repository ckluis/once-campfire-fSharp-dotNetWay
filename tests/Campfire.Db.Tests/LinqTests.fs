/// Design: plans/design-data-ef.md revision 2026-10-19 (LINQ first). L1 is unit 1.3's spike: how F# lambdas keep EF's query cache hitting.
module Campfire.Db.Tests.LinqTests

open System
open System.Collections.Generic
open System.Linq
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Diagnostics
open Microsoft.Extensions.Logging
open Xunit
open Campfire.Db
open Campfire.Db.Ef
open Campfire.Db.Tests.Support

type private Probe = { Wanted: int64 }

/// A prepared, empty database whose context counts query compilations.
let private counting (dir: TempDir) : CampfireDb * (unit -> int) =
    let path = dir.File "linq.sqlite3"
    Schema.prepareFile path "production" TimeProvider.System |> ignore
    // Test setup writes straight through the context, outside any Writes.run: exempt this test's flow (never disposed; it ends with the test).
    WriteGate.Exempt() |> ignore
    let compilations = ref 0
    let options =
        Data.options path TimeProvider.System (fun b ->
            b.LogTo(
                Action<string>(fun _ -> compilations.Value <- compilations.Value + 1),
                [| CoreEventId.QueryCompilationStarting |],
                LogLevel.Debug
            )
            |> ignore)
    (Data.factory options).CreateDbContext(), (fun () -> compilations.Value)

let private compilationsFor (run: CampfireDb -> int64 -> unit) : int =
    use dir = new TempDir()
    let db, count = counting dir
    run db 1L // warm: the first call compiles
    let warm = count ()
    for n in 2L .. 40L do
        run db n
    count () - warm

[<Fact>]
let ``L1 a captured local is a constant: every new value compiles a query`` () =
    let compiled = compilationsFor (fun db id -> db.Users.Where(fun u -> u.Id = id).FirstOrDefault() |> ignore)
    Assert.True(compiled >= 39, $"{compiled} compilations in 39 calls")

[<Fact>]
let ``L1 EF.Parameter around a captured value keeps the cache hitting`` () =
    let compiled = compilationsFor (fun db id -> db.Users.Where(fun u -> u.Id = EF.Parameter id).FirstOrDefault() |> ignore)
    Assert.Equal(0, compiled)

[<Fact>]
let ``L1 a record field keeps the cache hitting`` () =
    let compiled =
        compilationsFor (fun db id ->
            let p = { Wanted = id }
            db.Users.Where(fun u -> u.Id = p.Wanted).FirstOrDefault() |> ignore)
    Assert.Equal(0, compiled)

/// Unit 1.5a's finding: EF 10's `ExecuteUpdate(Action<UpdateSettersBuilder>)` turns the value a setter is given into a
/// parameter, so the plain captured value of `s.SetProperty(f, now)` keeps the cache hitting (no `EF.Parameter` is needed, or possible:
/// it throws outside a query expression).
[<Fact>]
let ``L2 an ExecuteUpdate setter value is a parameter, not a constant`` () =
    let compiled =
        compilationsFor (fun db id ->
            let now = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(float id)
            db.Messages
                .Where(fun m -> m.Id = EF.Parameter id)
                .ExecuteUpdate(fun s -> s.SetProperty((fun m -> m.UpdatedAt), now) |> ignore)
            |> ignore)
    Assert.Equal(0, compiled)

/// Unit 1.8: the queries of `Ban`, `PushSubscription` and the push subscriber lookups compile once whatever their values
/// (`Ban.banned` runs on every non-GET request; the subscriber queries on every message).
[<Fact>]
let ``L3 ban and push queries keep the cache hitting with new values`` () =
    let compiled =
        compilationsFor (fun db n ->
            let now = DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(float n)
            Ban.banned db $"203.0.113.{n}" |> ignore
            Ban.forUser db n |> ignore
            (try PushSubscription.find db n |> ignore with DbException(RecordNotFound _) -> ())
            PushSubscription.forUser db n |> ignore
            PushSubscription.forUsersInvolvedInEverything db n (n + 1L) now |> ignore
            PushSubscription.forMentionedUsers db n (n + 1L) [ for i in 1L .. n -> i ] now |> ignore)
    Assert.Equal(0, compiled)
