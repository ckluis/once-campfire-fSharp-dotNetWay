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
        for r in rooms do
            Assert.Equal(Some r, FastReads.roomById db r.Id)
        Assert.Equal(None, FastReads.roomById db -1L)
        Assert.Equal(Some(db.Rooms.OrderBy(fun r -> r.CreatedAt).First()), FastReads.originalRoom db)
        for u in users do
            for r in rooms do
                let expected = memberships.FirstOrDefault(fun m -> m.UserId = u.Id && m.RoomId = r.Id)
                let expected = if obj.ReferenceEquals(expected, null) then None else Some expected
                Assert.Equal(expected, FastReads.membershipByRoomAndUser db r.Id u.Id)
        let messages = db.Messages.ToList()
        Assert.True(messages.Count > 40)
        for m in messages do
            Assert.Equal(Some m, FastReads.messageById db m.Id)
        Assert.Equal(None, FastReads.messageById db -1L)
        for r in rooms do
            let inRoom = messages.Where(fun m -> m.RoomId = r.Id).ToList()
            let last = inRoom.OrderByDescending(fun m -> m.CreatedAt).Take(40).Reverse().ToList() |> List.ofSeq
            Assert.Equal<Message list>(last, FastReads.lastPage db r.Id)
            for m in inRoom do
                let before = Message.pageBefore db r.Id m
                let expected =
                    db.Messages.Where(fun x -> x.RoomId = r.Id && x.CreatedAt < m.CreatedAt).OrderByDescending(fun x -> x.CreatedAt).Take(40).ToList()
                    |> List.ofSeq
                    |> List.rev
                Assert.Equal<Message list>(expected, before)
        let attachments = db.Attachments.ToList()
        for a in attachments do
            let blob = db.Blobs.First(fun b -> b.Id = a.BlobId)
            let first = attachments.Where(fun x -> x.RecordType = a.RecordType && x.RecordId = a.RecordId && x.Name = a.Name).OrderBy(fun x -> x.Id).First()
            Assert.Equal(Some(db.Blobs.First(fun b -> b.Id = first.BlobId)), FastReads.attachedBlob db a.RecordType a.RecordId a.Name)
            ignore blob
        Assert.Equal(None, FastReads.attachedBlob db "Nope" 1L "x")
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
