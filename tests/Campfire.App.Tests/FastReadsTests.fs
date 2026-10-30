/// The hybrid reads (src/Campfire.Db/Ef/FastReads.fs): every Dapper read equals EF's answer over the default seed. A column left out of a
/// hand-written SELECT silently defaults in Dapper, so this is the guard (it runs in bin/verify with the rest).
module Campfire.App.Tests.FastReadsTests

open System.Linq
open Microsoft.EntityFrameworkCore
open Xunit
open Campfire.App.Tests.Support
open Campfire.Db
open Campfire.Db.Ef

/// The default seed has no membership with a `connected_at` (and four with an `unread_at`): the Dapper rows' nullable `connected_at`
/// would never be compared against a value. `Membership.present` connects a few before each comparison.
let private connectSome (test: Test) =
    task {
        use db = test.App.DbFactory.CreateDbContext()
        let! result =
            Writes.run test.App.Gate test.App.Env db (fun tx ->
                for m in tx.Db.Memberships.OrderBy(fun m -> m.Id).Take(6).ToList() do
                    Membership.present tx m)
        match result with
        | Ok() -> ()
        | Error e -> failwith (DbError.display e)
        Assert.True((db.Memberships.ToList() |> Seq.filter (fun m -> m.ConnectedAt.HasValue) |> Seq.length) >= 6)
    }

[<Fact>]
let ``dapper reads return the same records as EF for every row of the default seed`` () =
    task {
        use! test = bootSeeded "default"
        do! connectSome test
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
        // `user.rooms.original` (10.3): the user's oldest room (a tie in `created_at` may answer with either), None without memberships.
        for u in users do
            let mine = memberships |> Seq.filter (fun m -> m.UserId = u.Id) |> Seq.map (fun m -> rooms.First(fun r -> r.Id = m.RoomId)) |> List.ofSeq
            match FastReads.originalRoomForUser db u.Id with
            | None -> Assert.Empty mine
            | Some found ->
                Assert.Contains(found, mine)
                Assert.Equal((mine |> List.map (fun r -> r.CreatedAt) |> List.min), found.CreatedAt)
        Assert.Equal(None, FastReads.originalRoomForUser db -1L)
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

/// Unit H.2: the sidebar and search reads. Each reference below is the EF statement the Dapper read replaced (kept here as the oracle).
[<Fact>]
let ``dapper sidebar and search reads equal EF's answers`` () =
    task {
        use! test = bootSeeded "default"
        do! connectSome test
        use db = test.App.DbFactory.CreateDbContext()
        let users = db.Users.ToList()
        let rooms = db.Rooms.ToList()
        let direct = Names.roomTypeToDb RoomType.Direct
        let mutable nonEmptySidebar = 0
        for u in users do
            let userId = u.Id
            let visible =
                db.Database
                    .SqlQuery<MembershipRoomRow>(
                        $"SELECT memberships.id AS membership_id, memberships.room_id AS room_id, memberships.user_id AS user_id, memberships.involvement AS involvement, memberships.unread_at AS unread_at, memberships.connected_at AS connected_at, memberships.connections AS connections, memberships.created_at AS membership_created_at, memberships.updated_at AS membership_updated_at, rooms.name AS room_name, rooms.type AS room_type, rooms.creator_id AS room_creator_id, rooms.created_at AS room_created_at, rooms.updated_at AS room_updated_at FROM memberships INNER JOIN rooms ON rooms.id = memberships.room_id WHERE memberships.user_id = {userId} AND memberships.involvement != 'invisible' ORDER BY LOWER(rooms.name)"
                    )
                    .ToList()
                |> List.ofSeq
            if not visible.IsEmpty then nonEmptySidebar <- nonEmptySidebar + 1
            Assert.Equal<MembershipRoomRow list>(visible, FastReads.visibleMembershipRoomRows db userId)
            for roomType in [ Names.roomTypeToDb RoomType.Direct; Names.roomTypeToDb RoomType.Open; Names.roomTypeToDb RoomType.Closed ] do
                let expected =
                    db.Rooms
                        .FromSql($"SELECT rooms.* FROM rooms INNER JOIN memberships ON rooms.id = memberships.room_id WHERE memberships.user_id = {userId} AND rooms.type = {roomType}")
                        .AsEnumerable()
                    |> List.ofSeq
                Assert.Equal<Room list>(expected, FastReads.roomsOfType db userId roomType)
            let expectedIds =
                db.Memberships
                    .FromSql(
                        $"SELECT * FROM memberships WHERE room_id IN (SELECT rooms.id FROM rooms INNER JOIN memberships AS own ON rooms.id = own.room_id WHERE own.user_id = {userId} AND rooms.type = {direct})"
                    )
                    .AsEnumerable()
                    .Select(fun m -> m.UserId)
                    |> List.ofSeq
            // The order of an unordered statement is the planner's (Rails' too) and the one caller only builds a set from it, so compare sorted.
            Assert.Equal<int64 list>(List.sort expectedIds, List.sort (FastReads.memberUserIdsOfRoomsOfType db userId direct))
        Assert.True(nonEmptySidebar > 3)
        Assert.True(rooms.Count > 3)
        // activeExcluding: a few exclusion sets and limits.
        for ids in [ [||]; [| users[0].Id |]; users.Take(3).Select(fun u -> u.Id).ToArray(); [| -1L |] ] do
            for limit in [ 1; 3; 50 ] do
                let json = "[" + System.String.Join(",", ids) + "]"
                let expected =
                    db.Users
                        .FromSql($"SELECT * FROM users WHERE status = 0 AND id NOT IN (SELECT value FROM json_each({json})) ORDER BY created_at LIMIT {limit}")
                        .AsEnumerable()
                    |> List.ofSeq
                Assert.Equal<User list>(expected, FastReads.activeUsersExcluding db ids limit)
        // searches, after recording a few
        for q in [ "alpha"; "bravo"; "charlie" ] do
            let! r = Writes.run test.App.Gate test.App.Env db (fun tx -> Search.record tx DAVID q)
            match r with
            | Ok _ -> ()
            | Error e -> failwith (DbError.display e)
        let expectedSearches = db.Searches.Where(fun s -> s.UserId = DAVID).OrderByDescending(fun s -> s.UpdatedAt).ToList() |> List.ofSeq
        Assert.True(expectedSearches.Length >= 3)
        Assert.Equal<Search list>(expectedSearches, FastReads.searchesOrdered db DAVID)
        Assert.Equal<Search list>([], FastReads.searchesOrdered db -1L)
        // search: reachable messages
        let mutable hits = 0
        for u in users do
            for terms in [ "\"the\""; "\"hello\""; "\"a\" \"the\""; "\"zzzznomatch\"" ] do
                let userId = u.Id
                let expected =
                    db.Messages
                        .FromSql(
                            $"SELECT messages.* FROM messages INNER JOIN rooms ON messages.room_id = rooms.id INNER JOIN memberships ON rooms.id = memberships.room_id JOIN message_search_index idx ON messages.id = idx.rowid WHERE memberships.user_id = {userId} AND (idx.body MATCH {terms})"
                        )
                        .OrderByDescending(fun m -> m.CreatedAt)
                        .Take(100)
                        .ToList()
                    |> List.ofSeq
                    |> List.rev
                hits <- hits + expected.Length
                Assert.Equal<Message list>(expected, FastReads.searchReachable db userId terms)
        Assert.True(hits > 0)
    }
