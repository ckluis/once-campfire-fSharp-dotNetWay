/// The data layer against databases the Rails app wrote: the parity seeds (`parity/bin/seed build`,
/// which loads them through the reference app). Each test copies the seed it needs into a temporary
/// directory (the seeds are in WAL mode, so the `-wal` and `-shm` files come along) and never writes to
/// `parity/.seed`. Without the seeds they skip with a message; `CAMPFIRE_REQUIRE_SEED=1` fails instead.
///
/// The Rust crate has no tests of this kind (its model tests run on the reference fixtures); these check
/// what the fixtures can't: that rows Rails wrote read back through every model, in the formats Rails
/// writes them, and that writes land beside them.
module Campfire.Db.Tests.SeedTests

open System
open System.IO
open System.Linq
open System.Text.Json
open Xunit
open Campfire.Db
open Campfire.Db.Tests.Support
open Campfire.RailsCompat.Clock

/// Copies seed `name` into a temporary directory and runs `f` with the copy's database path and that
/// directory (so that `f` can find the seed's other files). Skips when the seed isn't built.
let private withSeed (name: string) (f: string -> string -> unit) : unit =
    match Campfire.Tests.Repo.seed name with
    | None -> Assert.Skip $"parity seed {name} is not built (parity/bin/seed build)"
    | Some seed ->
        use dir = new TempDir()
        let source = Path.Combine(seed, "db")
        for file in [ "production.sqlite3"; "production.sqlite3-wal"; "production.sqlite3-shm" ] do
            let from = Path.Combine(source, file)
            if File.Exists from then File.Copy(from, dir.File file)
        f (dir.File "production.sqlite3") seed

let private label (seed: string) (key: string) : int64 =
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(seed, "labels.json")))
    document.RootElement.GetProperty(key).GetInt64()

let private factories = System.Collections.Concurrent.ConcurrentDictionary<string, Microsoft.EntityFrameworkCore.IDbContextFactory<Ef.CampfireDb>>()

/// EF Core over the same copy (one pooled factory per path, as the app has one per process).
let private factoryFor (path: string) =
    factories.GetOrAdd(path, fun path -> Ef.Data.factory (Ef.Data.options path TimeProvider.System ignore))

let private efRead (path: string) (f: Ef.CampfireDb -> 'T) : 'T =
    use context = (factoryFor path).CreateDbContext()
    unwrap (Ef.Reads.run context f)

/// `Writes.run` on the copy, under `gate` (the one the app holds).
let private efWrite (path: string) (gate: WriteGate) (sink: RecordingSink) (clock: TestClock) (f: Ef.Tx -> 'T) : 'T =
    // Its own factory: the timestamps interceptor reads the test's clock, not the system's.
    let time = Ef.ClockTimeProvider clock
    use context = (Ef.Data.factory (Ef.Data.options path time ignore)).CreateDbContext()
    let env : Ef.Env = { Time = time; Sink = sink; RichText = BasicRichText(); BcryptCost = 4 }
    unwrap ((Ef.Writes.run gate env context f).GetAwaiter().GetResult())

/// Boots the copy as the app does (`Schema.prepare`, with the clock the test controls), and returns the write gate the app would hold.
let private openSeed (path: string) (clock: TestClock) (_sink: RecordingSink) : WriteGate =
    Schema.prepareFile path "production" (Ef.ClockTimeProvider clock) |> ignore
    WriteGate()

[<Fact>]
let ``a database Rails wrote opens as up to date and gains only the paging index`` () =
    withSeed "default" (fun path _ ->
        let indexes () =
            use raw = Raw.openFile path
            Raw.names raw "SELECT name FROM sqlite_master WHERE type = 'index'" |> Set.ofList
        let before = indexes ()
        Assert.DoesNotContain("index_messages_on_room_id_and_created_at", before)

        let _ = openSeed path (TestClock()) (RecordingSink())
        let after = indexes ()
        Assert.Equal<string list>([ "index_messages_on_room_id_and_created_at" ], Set.difference after before |> Set.toList)
        Assert.Equal(Schema.UpToDate, Schema.prepareFile path "production" TimeProvider.System))

/// The reference's `db:prepare` and `schema.sql` agree (what `reference-tools/db/differential.sh` checks for
/// the Rust crate), here against the seed's own schema.
[<Fact>]
let ``the schema Rails created is the schema we load`` () =
    withSeed "default" (fun path _ ->
        let schema (file: string) =
            use raw = Raw.openFile file
            Raw.names
                raw
                "SELECT sql || ';' FROM sqlite_master WHERE sql IS NOT NULL AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'message_search_index_%' ORDER BY rowid"
        use dir = new TempDir()
        let fresh = dir.File "fresh.sqlite3"
        Schema.prepareFile fresh "production" TimeProvider.System |> ignore
        // Ours adds the paging index, last.
        let ours = schema fresh |> List.filter (fun sql -> not (sql.Contains "index_messages_on_room_id_and_created_at"))
        Assert.Equal<string list>(schema path, ours))

[<Fact>]
let ``every model reads the rows Rails wrote`` () =
    withSeed "default" (fun path seed ->
        let _ = openSeed path (TestClock()) (RecordingSink())

        Assert.Equal(10L, efRead path User.count)
        Assert.Equal(11, List.length (efRead path Room.all))
        Assert.Equal(169L, efRead path Message.count)
        Assert.Equal(39L, efRead path Membership.count)
        Assert.Equal(1L, efRead path Account.count)
        Assert.Equal(3L, efRead path Search.count)
        Assert.Equal(5L, efRead path PushSubscription.count)

        let david = efRead path (fun c -> User.find c (label seed "users.david"))
        Assert.Equal(("David", Role.Administrator, Status.Active), (david.Name, david.Role, david.Status))
        Assert.Equal("david@37signals.com", david.EmailAddress)
        Assert.True(User.authenticate david "secret123456" || not (isNull david.PasswordDigest))

        let rita = efRead path (fun c -> User.find c (label seed "users.rita"))
        Assert.Equal(Status.Deactivated, rita.Status)
        Assert.Contains("-deactivated-", rita.EmailAddress)
        let mallory = efRead path (fun c -> User.find c (label seed "users.mallory"))
        Assert.Equal(Status.Banned, mallory.Status)
        Assert.True(efRead path (fun c -> Ban.banned c "203.0.113.9"))
        Assert.Equal(1, List.length (efRead path (fun c -> Ban.forUser c mallory.Id)))

        // Bots, with and without a webhook.
        let bender = efRead path (fun c -> User.find c (label seed "users.bender"))
        Assert.Equal(Role.Bot, bender.Role)
        Assert.Equal(Some "http://example.com/bender", efRead path (fun c -> User.webhookUrl c bender))
        Assert.True((efRead path (fun c -> User.authenticateBot c (User.botKey bender))).IsSome)
        Assert.Equal(2, List.length (efRead path User.activeBotsOrdered))
        Assert.Equal(7, List.length (efRead path User.activeOrdered)) // not Rita (deactivated), Mallory (banned) or Old Bot (deactivated)

        // Room types, and a user's rooms.
        let rooms = efRead path Room.all
        Assert.Equal<RoomType list>([ RoomType.Open; RoomType.Open; RoomType.Open ], rooms |> List.filter Room.isOpen |> List.map (fun r -> r.Type))
        Assert.Equal(4, rooms |> List.filter Room.isDirect |> List.length)
        Assert.True(efRead path (fun c -> Room.forUser c david.Id) |> List.length >= 5)
        let session = efRead path (fun c -> Session.findByToken c "AxJs94fteQ5Autv2VrKsH68c")
        Assert.Equal(david.Id, session.Value.UserId))

[<Fact>]
let ``every datetime Rails wrote reads and writes back as the same text`` () =
    withSeed "default" (fun path _ ->
        use raw = Raw.openFile path
        let tables = Raw.names raw "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE 'message_search_index%'"
        let mutable checkedValues = 0
        for table in tables do
            let columns = Raw.names raw $"SELECT name FROM pragma_table_info('{table}') WHERE type LIKE 'datetime%%'"
            for column in columns do
                let values = Raw.names raw $"SELECT \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL"
                for text in values do
                    Assert.Equal(text, RailsTime.toDb (RailsTime.ofDb text))
                    checkedValues <- checkedValues + 1
        Assert.True(checkedValues > 500, $"{checkedValues} datetimes checked"))

[<Fact>]
let ``messages page, search and read their bodies and attachments as Rails stored them`` () =
    withSeed "default" (fun path seed ->
        let _ = openSeed path (TestClock()) (RecordingSink())
        use raw = Raw.openFile path
        let richText = BasicRichText() :> RichText

        // Every room's pages agree with SQL, and the paging index serves them.
        for room in efRead path Room.all do
            let page = efRead path (fun c -> Message.lastPage c room.Id)
            let expected =
                Raw.query raw $"SELECT id FROM messages WHERE room_id = {room.Id} ORDER BY created_at DESC LIMIT 40" (fun r -> r.GetInt64 0)
                |> List.rev
            Assert.Equal<int64 list>(expected, page |> List.map (fun m -> m.Id))
            Assert.Equal(efRead path (fun c -> Message.countInRoom c room.Id) > 40L, efRead path (fun c -> Message.paged c room.Id))

        // The full-text index Rails fills, searched as the app does.
        let indexed = Raw.query raw "SELECT rowid, body FROM message_search_index WHERE length(body) > 20 LIMIT 20" (fun r -> r.GetInt64 0, r.GetString 1)
        Assert.True(List.length indexed > 5)
        let mutable searched = 0
        for messageId, body in indexed do
            match System.Text.RegularExpressions.Regex.Match(body, "[A-Za-z]{5,}") with
            | m when m.Success ->
                let message = efRead path (fun c -> Message.find c messageId)
                let found = efRead path (fun c -> Message.searchInRoom c message.RoomId m.Value)
                Assert.True(found |> List.exists (fun f -> f.Id = messageId), $"\"{m.Value}\" finds message {messageId}")
                searched <- searched + 1
            | _ -> ()
        Assert.True(searched > 5, $"{searched} searches")

        // Plain text and attachments.
        let withAttachment =
            Raw.query raw "SELECT record_id FROM active_storage_attachments WHERE record_type = 'Message' AND name = 'attachment'" (fun r -> r.GetInt64 0)
        Assert.Equal(5, List.length withAttachment)
        for messageId in withAttachment do
            let message = efRead path (fun c -> Message.find c messageId)
            let attachment = efRead path (fun c -> Message.attachment c message)
            let _, blob = attachment.Value
            Assert.False(String.IsNullOrEmpty blob.Filename)
            Assert.Equal(ContentType.Attachment, efRead path (fun c -> Message.contentType c richText message))
            Assert.False(String.IsNullOrEmpty(efRead path (fun c -> Message.plainTextBody c richText message)))

        // Boosts, in the order they were made.
        let boosted =
            Raw.query raw "SELECT DISTINCT message_id FROM boosts" (fun r -> r.GetInt64 0)
            |> List.map (fun messageId -> efRead path (fun c -> Message.boosts c (Message.find c messageId)))
        Assert.Equal(12, List.sumBy List.length boosted)
        for boosts in boosted do
            Assert.Equal<DateTime list>(boosts |> List.map (fun b -> b.CreatedAt) |> List.sort, boosts |> List.map (fun b -> b.CreatedAt))

        // A seeded message reads by its label.
        Assert.Equal(label seed "messages.first", (efRead path (fun c -> Message.find c (label seed "messages.first"))).Id))

[<Fact>]
let ``memberships Rails wrote read with every involvement and connection state`` () =
    withSeed "default" (fun path _ ->
        let _ = openSeed path (TestClock()) (RecordingSink())
        let memberships = efRead path (fun c -> c.Memberships.OrderBy(fun m -> m.Id).ToList() |> List.ofSeq)
        let involvements = memberships |> List.map (fun m -> Option.ofNullable m.Involvement) |> List.distinct |> Set.ofList
        Assert.Equal<Involvement option Set>(
            Set.ofList [ Some Involvement.Everything; Some Involvement.Mentions; Some Involvement.Nothing; Some Involvement.Invisible ],
            involvements
        )
        let user = memberships[0].UserId
        let withRooms = efRead path (fun c -> Membership.withOrderedRoom c user)
        Assert.False(List.isEmpty withRooms)
        let visible = efRead path (fun c -> Membership.visibleWithOrderedRoom c user)
        Assert.True(List.length visible <= List.length withRooms))

[<Fact>]
let ``writes land beside the rows Rails wrote`` () =
    withSeed "default" (fun path seed ->
        let clock = TestClock()
        clock.TravelTo(DateTimeOffset(RailsTime.ofDb "2026-03-03 09:30:15.123456"))
        let sink = RecordingSink()
        let gate = openSeed path clock sink
        let david, designers = label seed "users.david", label seed "rooms.designers"
        let before = efRead path (fun c -> Message.countInRoom c designers)

        let message =
            efWrite path gate sink clock (fun tx ->
                let m =
                    Message.create
                        tx
                        { NewMessage.create designers david with
                            ClientMessageId = Some "fsharp-seed-1"
                            Body = Some "Written by <b>F#</b> beside Rails" }
                Boost.create tx m.Id david "\U0001F680" |> ignore
                m)
        Assert.Equal<Event list>([ PushMessage(designers, message.Id) ], sink.Events())
        Assert.Equal(before + 1L, efRead path (fun c -> Message.countInRoom c designers))
        let last = efRead path (fun c -> Message.lastPage c designers) |> List.last
        Assert.Equal(message.Id, last.Id)
        Assert.Equal("2026-03-03 09:30:15.123456", RailsTime.toDb last.CreatedAt)
        Assert.Equal<int64 list>([ message.Id ], efRead path (fun c -> Message.searchInRoom c designers "beside") |> List.map (fun m -> m.Id))

        // Read back by a connection of our own, as the reference app would.
        use raw = Raw.openFile path
        Assert.Equal<string list>(
            [ "2026-03-03 09:30:15.123456" ],
            Raw.names raw "SELECT created_at FROM messages WHERE client_message_id = 'fsharp-seed-1'"
        )
        Assert.Equal(1L, Raw.scalarInt raw $"SELECT COUNT(*) FROM boosts WHERE message_id = {message.Id}"))

[<Fact>]
let ``an account with restricted room creation and an account with custom styles read their settings`` () =
    withSeed "restricted" (fun path _ ->
        let _ = openSeed path (TestClock()) (RecordingSink())
        let account = efRead path (fun c -> (Account.first c).Value)
        Assert.True(AccountSettings.restrictRoomCreationToAdministrators (Account.settings account))
        Assert.Equal("""{"restrict_room_creation_to_administrators":true}""", account.Settings))
    withSeed "custom_styles" (fun path _ ->
        let gate = openSeed path (TestClock()) (RecordingSink())
        let account = efRead path (fun c -> (Account.first c).Value)
        Assert.False(AccountSettings.restrictRoomCreationToAdministrators (Account.settings account))
        Assert.False(String.IsNullOrEmpty account.CustomStyles)
        // Updating the styles leaves the settings JSON as Rails wrote it, and writes only what changed.
        let updated = efWrite path gate (RecordingSink()) (TestClock()) (fun tx -> Account.update tx account None (Some(Some "body { color: red }")) None)
        Assert.Equal(account.Settings, updated.Settings)
        Assert.Equal("body { color: red }", (efRead path (fun c -> (Account.first c).Value)).CustomStyles))

[<Fact>]
let ``the first run seed has no account and FirstRun creates one`` () =
    withSeed "first_run" (fun path _ ->
        let gate = openSeed path (TestClock()) (RecordingSink())
        Assert.Equal(0L, efRead path Account.count)
        let digest = PasswordDigest.create "secret123456" 4
        let admin = efWrite path gate (RecordingSink()) (TestClock()) (fun tx -> FirstRun.create tx "First" "first@example.com" digest)
        Assert.True(User.isAdministrator admin)
        let rooms = efRead path (fun c -> Room.forUser c admin.Id)
        Assert.Equal<string list>([ "All Talk" ], rooms |> List.map (fun r -> string r.Name))
        Assert.Equal(1L, efRead path Account.count))

[<Fact>]
let ``a crowd of users all join a new open room`` () =
    withSeed "crowd" (fun path seed ->
        let gate = openSeed path (TestClock()) (RecordingSink())
        let users = efRead path User.count
        Assert.True(users > 500L, $"{users} users")
        let active = efRead path (fun c -> List.length (User.active c))
        let room = efWrite path gate (RecordingSink()) (TestClock()) (fun tx -> Room.create tx RoomType.Open (Some "Everyone") (label seed "users.david"))
        let members = efRead path (fun c -> Room.userIds c room)
        Assert.Equal(active, List.length members)
        // A new user is granted every open room, in one statement per batch.
        let user = efWrite path gate (RecordingSink()) (TestClock()) (fun tx -> User.create tx (NewUser.create "Latecomer"))
        let openRooms = efRead path (fun c -> Room.countOfType c RoomType.Open)
        Assert.Equal(openRooms, int64 (List.length (efRead path (fun c -> Membership.forUser c user.Id)))))
