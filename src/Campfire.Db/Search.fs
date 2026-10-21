// Port of rust/crates/db/src/models/search.rs (`reference/app/models/search.rb`) on EF Core: LINQ, every captured value
// in `EF.Parameter`. The record is in Messages.fs, beside the entities the context maps.
namespace Campfire.Db

open System
open System.Linq
open System.Runtime.CompilerServices
open Microsoft.EntityFrameworkCore
open Campfire.Db.Ef

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Search =
    /// How many recent searches `trim_recent_searches` keeps.
    [<Literal>]
    let RecentSearches = 10

    /// `user.searches.ordered`: most recent first.
    let orderedForUser (db: CampfireDb) (userId: int64) : Search list =
        FastReads.searchesOrdered db userId

    let count (db: CampfireDb) : int64 = db.Searches.LongCount()

    let countForUser (db: CampfireDb) (userId: int64) : int64 =
        db.Searches.LongCount(fun s -> s.UserId = EF.Parameter userId)

    /// `user.searches.excluding(user.searches.ordered.limit(10)).destroy_all`
    let private trimRecentSearches (tx: Tx) (userId: int64) : unit =
        let db = tx.Db
        let keep =
            db.Searches
                .Where(fun s -> s.UserId = EF.Parameter userId)
                .OrderByDescending(fun s -> s.UpdatedAt)
                .Take(RecentSearches)
                .Select(fun s -> s.Id)
                .ToArray()
        db.Searches
            .Where(fun s -> s.UserId = EF.Parameter userId && not (Enumerable.Contains(EF.Parameter keep, s.Id)))
            .ExecuteDelete()
        |> ignore

    /// `user.searches.record(query)`: `find_or_create_by(query:).touch`. Creating trims the user's searches to the ten most recent.
    let record (tx: Tx) (userId: int64) (query: string) : Search =
        let db = tx.Db
        let search =
            match Data.tryFirst (db.Searches.Where(fun s -> s.UserId = EF.Parameter userId && s.Query = EF.Parameter query)) with
            | Some search -> search
            | None ->
                let created = Data.insert db { Id = 0L; UserId = userId; Query = query; CreatedAt = DateTime.MinValue; UpdatedAt = DateTime.MinValue }
                trimRecentSearches tx userId
                created
        let now = tx.Now()
        let id = search.Id
        db.Searches.Where(fun s -> s.Id = EF.Parameter id).ExecuteUpdate(fun u -> u.SetProperty((fun s -> s.UpdatedAt), now) |> ignore) |> ignore
        { search with UpdatedAt = now }

    /// `user.searches.destroy_all`
    let destroyAllForUser (tx: Tx) (userId: int64) : unit =
        tx.Db.Searches.Where(fun s -> s.UserId = EF.Parameter userId).ExecuteDelete() |> ignore
