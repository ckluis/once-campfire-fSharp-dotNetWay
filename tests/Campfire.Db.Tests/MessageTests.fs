// Port of rust/crates/db/src/tests/message_test.rs
// `test/models/message_test.rb`, `message/searchable_test.rb`, `message/attachment_test.rb`
// (the parts that are data, not image processing), and pagination.
module Campfire.Db.Tests.MessageTests

open System
open System.Linq
open Microsoft.EntityFrameworkCore
open Xunit
open Campfire.Db
open Campfire.Db.Tests.Support

let private create (t: TestDb) (room: string) (creator: string) (body: string) (clientMessageId: string) : Message =
    let attributes =
        { NewMessage.create (id room) (id creator) with
            ClientMessageId = Some clientMessageId
            Body = Some body }
    t.WriteEf(fun tx -> Message.create tx attributes)

/// A blob row, as the storage layer leaves it before the message that attaches it.
let private insertBlob (tx: Ef.Tx) (key: string) (metadata: string option) : Ef.Blob =
    Ef.Data.insert
        tx.Db
        { Id = 0L
          Key = key
          Filename = "moon.jpg"
          ContentType = "image/jpeg"
          Metadata = Option.toObj metadata
          ServiceName = "local"
          ByteSize = 1L
          Checksum = null
          CreatedAt = DateTime.MinValue }

let private search (t: TestDb) (room: string) (query: string) : int64 list =
    let roomId = id room
    t.ReadEf(fun c -> Message.searchInRoom c roomId query) |> List.map (fun m -> m.Id)

[<Fact>]
let ``creating a message enqueues to push later`` () =
    use t = new TestDb()
    let message = create t "designers" "jason" "Hello" "123"
    let pushes = t.Events() |> List.filter (fun e -> match e with PushMessage _ -> true | _ -> false)
    Assert.Equal<Event list>([ PushMessage(id "designers", message.Id) ], pushes)

/// Which mentioned users are mentionees; reading the mentions is `Campfire.RichText`'s.
[<Fact>]
let ``mentionees`` () =
    use t = new TestDb()
    let pets = id "pets"
    let mentionees (userIds: int64 list) =
        t.ReadEf(fun c -> Message.mentioneesInRoom c pets userIds) |> List.map (fun u -> u.Id)

    Assert.Equal<int64 list>([ id "david" ], mentionees [ id "david" ])
    Assert.Equal<int64 list>([ id "david" ], mentionees [ id "david"; id "david" ])
    // Kevin isn't in All Pets.
    Assert.True(List.isEmpty (mentionees [ id "kevin" ]))

[<Fact>]
let ``message body is indexed and searchable`` () =
    use t = new TestDb()
    let message = create t "designers" "david" "My hovercraft is full of eels" "earth"
    Assert.Equal<int64 list>([ message.Id ], search t "designers" "eel")

    let message = t.WriteEf(fun tx -> Message.updateBody tx message "My hovercraft is full of sharks")
    Assert.Equal<int64 list>([ message.Id ], search t "designers" "sharks")

    t.WriteEf(fun tx -> Message.destroy tx message)
    Assert.True(List.isEmpty (search t "designers" "sharks"))

[<Fact>]
let ``search words are never query syntax`` () =
    use t = new TestDb()
    let message = create t "designers" "jason" "Do NOT feed the eel OR the shark" "c1"
    for query in [ "NOT"; "OR"; "AND"; "NEAR"; "eel NOT"; "NOT eel"; "shark OR"; "\""; "*"; ""; "a\000b"; "\000" ] do
        search t "designers" query |> ignore
    Assert.Equal<int64 list>([ message.Id ], search t "designers" "NOT eel")
    Assert.True(List.isEmpty (search t "designers" "eel dolphin"))
    Assert.Equal<int64 list>([ message.Id ], search t "designers" "eel\000shark")

[<Fact>]
let ``search results are returned in message order`` () =
    use t = new TestDb()
    let ids =
        [ "first cat"; "second cat"; "third cat"; "cat cat cat" ]
        |> List.map (fun body ->
            t.Travel 1L
            (create t "designers" "david" body body).Id)
    Assert.Equal<int64 list>(ids, search t "designers" "cat")

[<Fact>]
let ``rich text body is converted to plain text for indexing`` () =
    use t = new TestDb()
    let message = create t "designers" "david" "<span>My hovercraft is full of eels</span>" "earth"
    Assert.True(List.isEmpty (search t "designers" "span"))
    Assert.Equal<int64 list>([ message.Id ], search t "designers" "eel")

[<Fact>]
let ``search reachable only finds messages in the users rooms`` () =
    use t = new TestDb()
    let message = create t "designers" "david" "hovercraft" "earth"
    let found = t.ReadEf(fun c -> Message.searchReachable c (id "kevin") "hovercraft")
    Assert.Equal<int64 list>([ message.Id ], found |> List.map (fun m -> m.Id))
    Assert.True(List.isEmpty (t.ReadEf(fun c -> Message.searchReachable c (id "bender") "hovercraft")))

/// P2: `searchReachable` finds what the port's hand-written FTS5 query finds, for a word, a quoted phrase, a NUL-separated query and "".
[<Fact>]
let ``search reachable equals the ports query for a word a quoted phrase a nul separated query and nothing`` () =
    use t = new TestDb()
    for body in [ "coffee beans"; "my coffee is cold"; "cold brew"; "say \"coffee\" twice" ] do
        t.Travel 1L
        create t "designers" "david" body "earth" |> ignore
    let david = id "david"
    // (query, the MATCH string the port's `match_terms` makes of it)
    let cases =
        [ "coffee", "\"coffee\""
          "\"my coffee\"", "\"\"\"my\" \"coffee\"\"\""
          "cold\000brew", "\"cold\" \"brew\""
          "", "" ]
    for query, terms in cases do
        let expected =
            if terms = "" then
                []
            else
                t.Sql(fun c ->
                    Raw.rows
                        c
                        "SELECT messages.id FROM messages INNER JOIN rooms ON messages.room_id = rooms.id INNER JOIN memberships ON rooms.id = memberships.room_id JOIN message_search_index idx ON messages.id = idx.rowid WHERE memberships.user_id = ? AND (idx.body MATCH ?) ORDER BY messages.created_at DESC LIMIT 100"
                        [ david; terms ]
                        (fun r -> r.GetInt64 0))
                |> List.rev
        let found = t.ReadEf(fun c -> Message.searchReachable c david query) |> List.map (fun m -> m.Id)
        Assert.Equal<int64 list>(expected, found)
        if terms <> "" then Assert.NotEmpty found

[<Fact>]
let ``creating a blank message with attachment uses filename as plain text body`` () =
    use t = new TestDb()
    let message =
        t.WriteEf(fun tx ->
            let blob = insertBlob tx "abc123" (Some """{"identified":true}""")
            Message.create
                tx
                { NewMessage.create (id "hq") (id "david") with
                    ClientMessageId = Some "message"
                    AttachmentBlobId = Some blob.Id })
    let richText = BasicRichText() :> RichText
    Assert.Equal("moon.jpg", t.ReadEf(fun c -> Message.plainTextBody c richText message))
    Assert.Equal(ContentType.Attachment, t.ReadEf(fun c -> Message.contentType c richText message))
    Assert.Equal<int64 list>([ message.Id ], search t "hq" "moon")

    t.WriteEf(fun tx -> Message.destroy tx message)
    Assert.True(t.Events() |> List.exists (fun e -> match e with PurgeBlob _ -> true | _ -> false))
    Assert.Equal(0L, t.ReadEf(fun c -> c.Attachments.Count()))

[<Fact>]
let ``sound messages`` () =
    use t = new TestDb()
    let richText = BasicRichText() :> RichText
    let message = create t "designers" "david" "/play trombone" "x"
    Assert.Equal(ContentType.Sound, t.ReadEf(fun c -> Message.contentType c richText message))
    Assert.Equal("trombone", (t.ReadEf(fun c -> Message.sound c richText message)).Value.Name)
    let message = create t "designers" "david" "/play nosuchsound" "y"
    Assert.Equal(ContentType.Text, t.ReadEf(fun c -> Message.contentType c richText message))

/// `create_message` answers with what `Message.create` returns, without reading it back, so that
/// has to be the row as stored: after the commit's hooks, at the microseconds the columns keep.
[<Fact>]
let ``create returns the row as stored`` () =
    use t = new TestDb()
    let blobId (key: string) = t.WriteEf(fun tx -> (insertBlob tx key None).Id)
    let make (body: string option) (attachmentBlobId: int64 option) (clientMessageId: string option) : NewMessage =
        { NewMessage.create (id "designers") (id "david") with
            ClientMessageId = clientMessageId
            Body = body
            AttachmentBlobId = attachmentBlobId }
    let cases =
        [ make (Some "<p>Hello <strong>there</strong></p>") None (Some "text")
          make (Some "") None (Some "empty body")
          make None None (Some "nothing")
          make None (Some(blobId "attachment")) (Some "attachment")
          make (Some "With a picture") (Some(blobId "both")) (Some "both")
          make (Some "no client id") None None ]
    cases
    |> List.iteri (fun n attributes ->
        if n = 1 then
            // A frozen clock with nanoseconds, which the columns don't keep.
            t.Time.SetUtcNow(System.DateTimeOffset.UnixEpoch.AddSeconds(1_900_000_000.0).AddTicks(1_234_567L))
        let created = t.WriteEf(fun tx -> Message.create tx attributes)
        Assert.True((created = t.ReadEf(fun c -> Message.find c created.Id)), $"case {n}")
    )

[<Fact>]
let ``client message id defaults to a uuid`` () =
    use t = new TestDb()
    let message =
        t.WriteEf(fun tx -> Message.create tx { NewMessage.create (id "hq") (id "bender") with Body = Some "beep" })
    Assert.Equal(36, message.ClientMessageId.Length)

[<Fact>]
let ``pagination`` () =
    use t = new TestDb()
    let watercooler = id "watercooler"
    let all = t.ReadEf(fun c -> Message.lastPage c watercooler)
    Assert.Equal(10, List.length all) // watercooler fixtures
    Assert.True(all |> List.pairwise |> List.forall (fun (a, b) -> a.CreatedAt <= b.CreatedAt))
    Assert.False(t.ReadEf(fun c -> Message.paged c watercooler))

    let middle = all[5]
    let before = t.ReadEf(fun c -> Message.pageBefore c watercooler middle)
    let after = t.ReadEf(fun c -> Message.pageAfter c watercooler middle)
    Assert.Equal<Message list>(List.take 5 all, before)
    Assert.Equal<Message list>(List.skip 6 all, after)
    let around = t.ReadEf(fun c -> Message.pageAround c watercooler middle)
    Assert.Equal<Message list>(all, around)
    Assert.True(t.ReadEf(fun c -> Message.existsBefore c watercooler middle))
    Assert.False(t.ReadEf(fun c -> Message.existsAfter c watercooler all[9]))

    for n in 0L .. Message.PageSize - 1L do
        t.Travel 1L
        create t "watercooler" "david" $"message {n}" $"c{n}" |> ignore
    Assert.True(t.ReadEf(fun c -> Message.paged c watercooler))
    let last = t.ReadEf(fun c -> Message.lastPage c watercooler)
    Assert.Equal(Message.PageSize, int64 (List.length last))
    Assert.Equal(all[0], (t.ReadEf(fun c -> Message.firstPage c watercooler))[0])

    let since = all[9].CreatedAt
    let created = t.ReadEf(fun c -> Message.pageCreatedSince c watercooler since)
    Assert.Equal(Message.PageSize, int64 (List.length created))
    let createdIds = created |> List.map (fun m -> m.Id)
    let updated = t.ReadEf(fun c -> Message.pageUpdatedSince c watercooler since createdIds)
    Assert.True(updated |> List.forall (fun m -> not (List.contains m.Id createdIds)))

/// P1 (design-data-ef section 5): a post through `Message.create`. The message, its rich text, the room's `updated_at`, the FTS row,
/// the unread memberships and the `PushMessage` event, every timestamp equal to the frozen clock; then a boost touches the message
/// and the room and reindexes.
[<Fact>]
let ``P1 post and boost with the clock frozen`` () =
    let frozen = DateTimeOffset(2026, 3, 4, 10, 11, 12, 123, TimeSpan.Zero).AddTicks(4560L)
    use t = new TestDb(start = frozen)
    let now = t.Utc()
    let designers = id "designers"
    let posted =
        t.WriteEf(fun tx ->
            Message.create
                tx
                { NewMessage.create designers (id "david") with
                    ClientMessageId = Some "p1"
                    Body = Some "<p>Hello <b>planet</b></p>" })

    Assert.Equal((now, now), (posted.CreatedAt, posted.UpdatedAt))
    let stored = t.ReadEf(fun db -> Message.find db posted.Id)
    Assert.Equal(posted, stored)
    let rich = t.ReadEf(fun db -> (Message.body db stored).Value)
    Assert.Equal<string | null>("<p>Hello <b>planet</b></p>", rich.Body)
    Assert.Equal((now, now), (rich.CreatedAt, rich.UpdatedAt))
    Assert.Equal(now, (t.ReadEf(fun db -> Room.find db designers)).UpdatedAt)
    let indexed =
        t.ReadEf(fun db -> db.Database.SqlQuery<string>($"SELECT body AS Value FROM message_search_index WHERE rowid = {posted.Id}").ToList())
    Assert.Equal<string list>([ "Hello planet" ], List.ofSeq indexed)
    // Unread: the others who can see the room, from this message's creation; not its creator.
    let memberships = t.ReadEf(fun db -> Membership.forRoom db designers)
    Assert.True(memberships |> List.exists (fun m -> m.UserId <> id "david" && m.UnreadAt = Nullable now))
    Assert.True(memberships |> List.forall (fun m -> m.UserId <> id "david" || not m.UnreadAt.HasValue))
    Assert.Equal<Event list>([ PushMessage(designers, posted.Id) ], t.Events())

    t.Time.SetUtcNow(frozen.AddSeconds 30.0)
    let later = t.Utc()
    let boost = t.WriteEf(fun tx -> Boost.create tx posted.Id (id "jason") "hi")
    Assert.Equal((later, later), (boost.CreatedAt, boost.UpdatedAt))
    Assert.Equal(later, (t.ReadEf(fun db -> Message.find db posted.Id)).UpdatedAt)
    Assert.Equal(later, (t.ReadEf(fun db -> Room.find db designers)).UpdatedAt)
    Assert.Equal(now, (t.ReadEf(fun db -> Message.find db posted.Id)).CreatedAt)

[<Fact>]
let ``reachable messages are those of the user's rooms`` () =
    use t = new TestDb()
    let reachable (user: string) (message: string) =
        t.TryReadEf(fun db -> Message.findReachable db (id user) (id message)) |> Result.isOk
    // `first` is in Designers: Kevin is a member of it, Bender is not.
    Assert.True(reachable "kevin" "first")
    Assert.False(reachable "bender" "first")

[<Fact>]
let ``destroying a message removes its boosts, body and attachment and touches the room`` () =
    use t = new TestDb()
    let message = create t "designers" "david" "bye" "bye"
    t.WriteEf(fun tx -> Boost.create tx message.Id (id "jason") "x" |> ignore)
    t.Travel 5L
    let before = t.ReadEf(fun db -> Room.find db (id "designers"))
    t.WriteEf(fun tx -> Message.destroy tx message)
    Assert.True((t.ReadEf(fun db -> Message.findById db message.Id)).IsNone)
    Assert.True(List.isEmpty (t.ReadEf(fun db -> Boost.forMessage db message.Id)))
    Assert.True((t.ReadEf(fun db -> Message.body db message)).IsNone)
    Assert.True((t.ReadEf(fun db -> Room.find db (id "designers"))).UpdatedAt > before.UpdatedAt)
    Assert.True(List.isEmpty (search t "designers" "bye"))
