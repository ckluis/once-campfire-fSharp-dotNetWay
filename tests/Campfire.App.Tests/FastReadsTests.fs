/// The hybrid reads (src/Campfire.Db/Ef/FastReads.fs): every Dapper read equals EF's answer over the default seed. A column left out of a
/// hand-written SELECT silently defaults in Dapper, so this is the guard (it runs in bin/verify with the rest).
module Campfire.App.Tests.FastReadsTests

open System.Linq
open Microsoft.EntityFrameworkCore
open Xunit
open Campfire.App.Tests.Support
open Campfire.Db
open Campfire.Db.Ef

[<Fact>]
let ``dapper reads return the same records as EF for every row of the default seed`` () =
    task {
        use! test = bootSeeded "default"
        use db = test.App.DbFactory.CreateDbContext()
        let users = db.Users.ToList()
        let rooms = db.Rooms.ToList()
        let memberships = db.Memberships.ToList()
        let sessions = db.Sessions.ToList()
        Assert.True(users.Count > 3 && rooms.Count > 3 && memberships.Count > 3 && sessions.Count > 0)
        for u in users do
            Assert.Equal(Some u, FastReads.userById db u.Id)
        Assert.Equal(None, FastReads.userById db -1L)
        for m in memberships do
            Assert.Equal(Some(rooms.First(fun r -> r.Id = m.RoomId)), FastReads.roomForUser db m.UserId m.RoomId)
        // Every (user, room) pair, member or not: the join answers exactly for the members.
        for u in users do
            for r in rooms do
                let member' = memberships.Any(fun m -> m.UserId = u.Id && m.RoomId = r.Id)
                Assert.Equal((if member' then Some r else None), FastReads.roomForUser db u.Id r.Id)
        for s in sessions do
            Assert.Equal(Some s, FastReads.sessionByToken db s.Token)
        Assert.Equal(None, FastReads.sessionByToken db "nope")
        Assert.Equal(Some(db.Accounts.OrderBy(fun a -> a.Id).First()), FastReads.accountFirst db)
        // Both involvement of RoomType kinds and a few nullable shapes were covered by the seed.
        Assert.True(rooms.Select(fun r -> r.Type).Distinct().Count() >= 2)
    }

/// Inside a write the Dapper read sees the transaction's uncommitted rows (the context's transaction is passed explicitly).
[<Fact>]
let ``a dapper read inside a write sees the write's own rows`` () =
    task {
        use! test = bootSeeded "default"
        use db = test.App.DbFactory.CreateDbContext()
        let! result =
            Writes.run test.App.Gate test.App.Env db (fun tx ->
                let bot = User.createBot tx "Hybrid Reader" None
                FastReads.userById tx.Db bot.Id |> Option.map (fun x -> x.Name))
        match result with
        | Ok seen -> Assert.Equal(Some "Hybrid Reader", seen)
        | Error e -> failwith (DbError.display e)
    }
