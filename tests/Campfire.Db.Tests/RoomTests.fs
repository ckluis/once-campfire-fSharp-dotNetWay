// Port of rust/crates/db/src/tests/room_test.rs
// `test/models/room_test.rb`, `rooms/direct_test.rb`, `rooms/open_test.rb`
module Campfire.Db.Tests.RoomTests

open Xunit
open Campfire.Db
open Campfire.Db.Tests.Support

let private memberIds (t: TestDb) (roomId: int64) : int64 list = t.ReadEf(fun c -> Room.userIds c (Room.find c roomId))

let private room (t: TestDb) (label: string) : Room =
    let roomId = id label
    t.ReadEf(fun c -> Room.find c roomId)

[<Fact>]
let ``grant membership to user`` () =
    use t = new TestDb()
    let watercooler = room t "watercooler"
    t.WriteEf(fun tx -> Room.grantTo tx watercooler [ id "kevin" ])
    Assert.Contains(id "kevin", memberIds t (id "watercooler"))

[<Fact>]
let ``revoke membership from user`` () =
    use t = new TestDb()
    let watercooler = room t "watercooler"
    t.WriteEf(fun tx -> Room.revokeFrom tx watercooler [ id "david" ])
    Assert.DoesNotContain(id "david", memberIds t (id "watercooler"))
    Assert.Equal<Event list>([ DisconnectUser(id "david", true) ], t.Events())

[<Fact>]
let ``revise memberships`` () =
    use t = new TestDb()
    let watercooler = room t "watercooler"
    t.WriteEf(fun tx -> Room.revise tx watercooler [ id "kevin" ] [ id "david" ])
    let members = memberIds t (id "watercooler")
    Assert.Contains(id "kevin", members)
    Assert.DoesNotContain(id "david", members)

[<Fact>]
let ``create for users by giving them immediate membership`` () =
    use t = new TestDb()
    let room = t.WriteEf(fun tx -> Room.createFor tx RoomType.Closed (Some "Hello!") (id "david") [ id "kevin"; id "david" ])
    let members = memberIds t room.Id
    Assert.True(List.contains (id "kevin") members && List.contains (id "david") members)

[<Fact>]
let ``type predicates`` () =
    use t = new TestDb()
    Assert.True(Room.isOpen (room t "pets"))
    Assert.False(Room.isDirect (room t "pets"))
    Assert.True(Room.isDirect (room t "david_and_jason"))
    Assert.True(Room.isClosed (room t "designers"))

[<Fact>]
let ``default involvement for new users`` () =
    use t = new TestDb()
    let room = t.WriteEf(fun tx -> Room.createFor tx RoomType.Closed (Some "Hello!") (id "david") [ id "kevin"; id "david" ])
    let memberships = t.ReadEf(fun c -> Room.memberships c room)
    Assert.Equal(2, List.length memberships)
    Assert.True(memberships |> List.forall (fun m -> Membership.involvedIn m Involvement.Mentions))

[<Fact>]
let ``granted memberships are stamped by sqlite`` () =
    // insert_all lets SQLite fill the timestamps: STRFTIME('%Y-%m-%d %H:%M:%f', 'NOW').
    use t = new TestDb()
    let room = t.WriteEf(fun tx -> Room.createFor tx RoomType.Closed (Some "Hello!") (id "david") [ id "kevin" ])
    let createdAt =
        t.Sql(fun c -> Raw.row c "SELECT created_at FROM memberships WHERE room_id = ?" [ room.Id ] (fun r -> r.GetString 0))
    Assert.True((createdAt.Length = "2026-09-26 12:25:26.826".Length), createdAt)

[<Fact>]
let ``direct rooms keep their type`` () =
    use t = new TestDb()
    let direct = room t "david_and_jason"
    match t.TryWriteEf(fun tx -> Room.update tx direct None (Some RoomType.Open)) with
    | Error(RecordInvalid errors) -> Assert.Equal<string list>([ "can't be changed for a direct room" ], Errors.on "type" errors)
    | other -> failwith $"expected invalid, got {other}"

[<Fact>]
let ``destroying a room destroys its messages and memberships`` () =
    use t = new TestDb()
    let watercooler = room t "watercooler"
    t.WriteEf(fun tx -> Room.destroy tx watercooler)
    Assert.True(List.isEmpty (t.ReadEf(fun c -> Message.forRoom c (id "watercooler"))))
    Assert.True(List.isEmpty (t.ReadEf(fun c -> Membership.forRoom c (id "watercooler"))))
    Assert.True((t.ReadEf(fun c -> Room.findById c (id "watercooler"))).IsNone)
    Assert.Equal(
        0L,
        t.Sql(fun c -> Raw.row c "SELECT COUNT(*) FROM boosts WHERE message_id IN (?, ?)" [ id "thirteenth"; id "fourth" ] (fun r -> r.GetInt64 0))
    )

// Rooms::Direct

[<Fact>]
let ``create direct room for same users`` () =
    use t = new TestDb()
    let room = t.WriteEf(fun tx -> Room.findOrCreateDirectFor tx [ id "jz"; id "kevin" ] (id "jz"))
    let members = memberIds t room.Id
    Assert.True(List.contains (id "jz") members && List.contains (id "kevin") members)
    Assert.DoesNotContain(id "jason", members)

[<Fact>]
let ``only one direct room will exist for the same users`` () =
    use t = new TestDb()
    let room1 = t.WriteEf(fun tx -> Room.findOrCreateDirectFor tx [ id "jz"; id "kevin" ] (id "jz"))
    let room2 = t.WriteEf(fun tx -> Room.findOrCreateDirectFor tx [ id "kevin"; id "jz" ] (id "kevin"))
    Assert.Equal(room1.Id, room2.Id)

    let existing = t.WriteEf(fun tx -> Room.findOrCreateDirectFor tx [ id "david"; id "kevin" ] (id "david"))
    Assert.Equal(id "david_and_kevin", existing.Id)

[<Fact>]
let ``direct default involvement for new users`` () =
    use t = new TestDb()
    let room = t.WriteEf(fun tx -> Room.findOrCreateDirectFor tx [ id "jz"; id "kevin" ] (id "jz"))
    Assert.True(t.ReadEf(fun c -> Room.memberships c room) |> List.forall (fun m -> Membership.involvedIn m Involvement.Everything))

// Rooms::Open

[<Fact>]
let ``open room grants access to all users after creation`` () =
    use t = new TestDb()
    let room = t.WriteEf(fun tx -> Room.create tx RoomType.Open (Some "My open room with everyone!") (id "david"))
    Assert.Equal(t.ReadEf User.count, int64 (List.length (memberIds t room.Id)))

[<Fact>]
let ``open room grants access to all users after becoming open`` () =
    use t = new TestDb()
    let watercooler = room t "watercooler"
    t.WriteEf(fun tx -> Room.update tx watercooler None (Some RoomType.Open) |> ignore)
    Assert.Equal(t.ReadEf User.count, int64 (List.length (memberIds t (id "watercooler"))))
    Assert.Equal(RoomType.Open, (room t "watercooler").Type)
    let stored = t.Sql(fun c -> Raw.row c "SELECT type FROM rooms WHERE id = ?" [ id "watercooler" ] (fun r -> r.GetString 0))
    Assert.Equal("Rooms::Open", stored)

[<Fact>]
let ``user room scopes`` () =
    use t = new TestDb()
    let david = id "david"
    Assert.Equal(2, List.length (t.ReadEf(fun c -> Room.forUserOfType c david RoomType.Direct)))
    Assert.Equal(4, List.length (t.ReadEf(fun c -> Room.forUserWithoutDirects c david)))
    Assert.True((t.ReadEf(fun c -> Room.findForUser c (id "kevin") (id "pets"))).IsNone)
    // The sidebar's one statement (`SqlQuery`): ordered by lower-cased name, invisible memberships left out.
    let ordered = t.ReadEf(fun c -> Membership.visibleWithOrderedRoom c david)
    let names = ordered |> List.map (fun (_, r) -> r.Name)
    Assert.Equal<string list>([ "All Pets"; "All Talk"; "Designers"; "HQ" ], List.skip (List.length names - 4) (names |> List.map string))

[<Fact>]
let ``the sidebar's memberships and rooms are the entities LINQ reads`` () =
    use t = new TestDb()
    for userId in [ id "david"; id "kevin"; id "jz" ] do
        let sidebar = t.ReadEf(fun c -> Membership.withOrderedRoom c userId)
        let linq =
            t.ReadEf(fun c -> Membership.forUser c userId |> List.map (fun m -> m, Room.find c m.RoomId))
        Assert.Equal(List.length linq, List.length sidebar)
        for membership, room in sidebar do
            Assert.Contains((membership, room), linq)
        let visible = t.ReadEf(fun c -> Membership.visibleWithOrderedRoom c userId)
        Assert.True(visible |> List.forall (fun (m, _) -> not (Membership.involvedIn m Involvement.Invisible)))

[<Fact>]
let ``a direct room is found by its members in any order`` () =
    use t = new TestDb()
    let found = t.ReadEf(fun c -> Room.findDirectFor c [ id "kevin"; id "david" ])
    Assert.Equal(Some(id "david_and_kevin"), found |> Option.map (fun r -> r.Id))
    Assert.True((t.ReadEf(fun c -> Room.findDirectFor c [ id "kevin"; id "jz" ])).IsNone)
    Assert.True((t.ReadEf(fun c -> Room.findDirectFor c [])).IsNone)

[<Fact>]
let ``granting more users than one batch holds`` () =
    use t = new TestDb()
    t.Sql(fun c ->
        Raw.exec
            c
            "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < 1100) INSERT INTO users (name, role, status, created_at, updated_at) SELECT 'bulk ' || i, 0, 0, '2026-01-01 00:00:00', '2026-01-01 00:00:00' FROM n")
    let ids = t.Sql(fun c -> Raw.rows c "SELECT id FROM users WHERE name LIKE 'bulk %'" [] (fun r -> r.GetInt64 0))
    let watercooler = room t "watercooler"
    t.WriteEf(fun tx -> Room.grantTo tx watercooler ids)
    let members = memberIds t watercooler.Id
    Assert.True(ids |> List.forall (fun id -> List.contains id members))
    // Granting again skips the members already there.
    t.WriteEf(fun tx -> Room.grantTo tx watercooler ids)
    Assert.Equal(List.length members, List.length (memberIds t watercooler.Id))
