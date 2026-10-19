// Port of rust/crates/db/src/tests/fixtures_test.rs
// The fixture loader against facts from a Ruby-loaded fixtures database, and (skipped unless
// `CAMPFIRE_RUBY_FIXTURES_DB` is set) a row-for-row comparison with one.
module Campfire.Db.Tests.FixturesTests

open System
open System.IO
open Xunit
open Campfire.Db
open Campfire.Db.Tests.Support

[<Fact>]
let ``ids are label crcs`` () =
    use t = new TestDb()
    Assert.Equal("David", (t.ReadEf(fun c -> User.find c 127326141L)).Name)
    Assert.Equal("Designers", (t.ReadEf(fun c -> Room.find c 654632876L)).Name)

[<Fact>]
let ``associations enums and defaults`` () =
    use t = new TestDb()
    let pets = t.ReadEf(fun c -> Room.find c (id "pets"))
    Assert.Equal((RoomType.Open, id "david"), (pets.Type, pets.CreatorId))

    Assert.Equal(Role.Administrator, (t.ReadEf(fun c -> User.find c (id "david"))).Role)
    Assert.Equal(Role.Member, (t.ReadEf(fun c -> User.find c (id "jz"))).Role)
    let bender = t.ReadEf(fun c -> User.find c (id "bender"))
    Assert.Equal((Role.Bot, Some 12), (bender.Role, bender.BotToken |> Option.ofObj |> Option.map String.length))

    let kevinDesigners = t.ReadEf(fun c -> Membership.find c (id "kevin_designers"))
    Assert.Equal(Nullable Involvement.Mentions, kevinDesigners.Involvement) // column default
    Assert.Equal(0L, kevinDesigners.Connections)

    let richText =
        use raw = Raw.openFile (t.Dir.File "test.sqlite3")
        let richTextId = id "first"
        Raw.query raw $"SELECT record_id, record_type FROM action_text_rich_texts WHERE id = {richTextId}" (fun r -> r.GetInt64 0, r.GetString 1)
        |> List.exactlyOne
    Assert.Equal((id "first", "Message"), richText)

[<Fact>]
let ``erb times are whole seconds and default timestamps are now`` () =
    use t = new TestDb()
    let first = t.ReadEf(fun c -> Message.find c (id "first"))
    Assert.Equal(0L, first.CreatedAt.Ticks % TimeSpan.TicksPerSecond)
    Assert.True(first.UpdatedAt > first.CreatedAt)
    let session = t.ReadEf(fun c -> Session.find c (id "david_safari"))
    Assert.Equal(0L, session.LastActiveAt.Ticks % TimeSpan.TicksPerSecond)
    Assert.True(abs ((session.CreatedAt - session.LastActiveAt).TotalSeconds - 7200.0) <= 1.0)

[<Fact>]
let ``passwords are bcrypt of secret123456`` () =
    use t = new TestDb()
    let david = t.ReadEf(fun c -> User.find c (id "david"))
    Assert.True(User.authenticate david "secret123456")
    Assert.StartsWith("$2a$", david.PasswordDigest)

/// Dump a table with values normalized where Ruby and F# can't agree (bcrypt salts, bot tokens,
/// and timestamps, which are compared by shape: whole seconds vs. fractional).
let dump (conn: Microsoft.Data.Sqlite.SqliteConnection) (table: string) : string list =
    Raw.query conn $"SELECT * FROM \"{table}\" ORDER BY id" (fun r ->
        let fields =
            [ for name, cell in Raw.cells r ->
                  let rendered =
                      match name, cell with
                      | "password_digest", Raw.Text s -> $"bcrypt:{s.Substring(0, 4)}"
                      | "bot_token", Raw.Text s -> $"token:{s.Length}"
                      | n, Raw.Text s when n.EndsWith "_at" -> if s.Contains '.' then "time:fraction" else $"time:whole:{s.Substring(0, 10)}"
                      | _, Raw.Null -> "Null"
                      | _, Raw.Int v -> $"Integer({v})"
                      | _, Raw.Real v -> $"Real({v})"
                      | _, Raw.Text v -> $"Text(\"{v}\")"
                      | _, Raw.Blob v -> $"Blob({v.Length})"
                  name, rendered ]
        fields |> List.sort |> List.map (fun (n, v) -> $"{n}={v}") |> String.concat " ")

[<Fact>]
let ``fixtures match ruby row for row`` () =
    match Environment.GetEnvironmentVariable "CAMPFIRE_RUBY_FIXTURES_DB" with
    | null -> Assert.Skip "needs CAMPFIRE_RUBY_FIXTURES_DB, a database the reference app filled with `db:fixtures:load`"
    | path ->
        use ruby = Raw.openFile path
        use t = new TestDb()
        use ours = Raw.openFile (t.Dir.File "test.sqlite3")
        let tables =
            [ "accounts"; "action_text_rich_texts"; "boosts"; "memberships"; "messages"; "push_subscriptions"; "rooms"; "searches"; "sessions"; "users"; "webhooks" ]
        let mutable compared = 0
        for table in tables do
            let expected = dump ruby table
            let actual = dump ours table
            Assert.True((expected = actual), $"{table}\nruby: {expected}\nfsharp: {actual}")
            compared <- compared + List.length expected
        Assert.True(compared > 60, $"{compared} rows compared")

[<Fact>]
let ``export database for rails`` () =
    match Environment.GetEnvironmentVariable "CAMPFIRE_EXPORT_DB" with
    | null -> Assert.Skip "writes a database to CAMPFIRE_EXPORT_DB for the Rails rollback check"
    | path ->
        for suffix in [ ""; "-wal"; "-shm" ] do
            File.Delete(path + suffix)
        Testing.prepare path "test" |> ignore
        let gate = WriteGate()
        let time = TimeProvider.System
        let factory = Ef.Data.factory (Ef.Data.options path time ignore)
        let efEnv : Ef.Env = { Time = time; Sink = NullSink(); RichText = BasicRichText(); BcryptCost = 4 }
        Testing.loadFixtures gate efEnv factory
        // A user, a session and an account change on top of the fixtures, written through EF.
        let rusty =
            use context = factory.CreateDbContext()
            (Ef.Writes.run gate efEnv context (fun tx ->
                let user =
                    User.create
                        tx
                        { NewUser.create "Rusty" with
                            EmailAddress = Some "rusty@example.com"
                            PasswordDigest = Some(PasswordDigest.create "secret123456" 4) }
                Session.start tx user.Id (Some "ua") (Some "8.8.8.8") |> ignore
                let account = (Account.first tx.Db).Value
                Account.update tx account None None (Some [ "restrict_room_creation_to_administrators", "true" ]) |> ignore
                user))
                .GetAwaiter().GetResult()
            |> unwrap
        let rustyId = rusty.Id
        use context = factory.CreateDbContext()
        (Ef.Writes.run gate efEnv context (fun tx ->
            Search.record tx rustyId "hovercraft" |> ignore
            let message =
                Message.create
                    tx
                    { NewMessage.create (id "designers") (id "david") with
                        ClientMessageId = Some "rust-1"
                        Body = Some "Written by <b>Rust</b> hovercraft" }
            Boost.create tx message.Id (id "jason") "\U0001F980" |> ignore))
            .GetAwaiter().GetResult()
        |> unwrap
        use context = factory.CreateDbContext()
        (Ef.Writes.run gate efEnv context (fun tx ->
            Room.createFor tx RoomType.Closed (Some "Rust Room") rusty.Id [ rusty.Id; id "david" ] |> ignore))
            .GetAwaiter().GetResult()
        |> unwrap

/// Fixture names reach the statements as identifiers (a hole can't be one), so each must be one SQLite lists for the table.
[<Fact>]
let ``a fixture naming a table or column the schema lacks is refused and rolls back`` () =
    use dir = new TempDir()
    let path = dir.File "refuse.sqlite3"
    Testing.prepare path "test" |> ignore
    let factory = Ef.Data.factory (Ef.Data.options path TimeProvider.System ignore)
    let env : Ef.Env = { Time = TimeProvider.System; Sink = NullSink(); RichText = BasicRichText(); BcryptCost = 4 }
    let load (fixtures: string) =
        use context = factory.CreateDbContext()
        let result =
            (Ef.Writes.run (WriteGate()) env context (fun tx ->
                Fixtures.load tx.Db fixtures (Fixtures.Options.at (tx.Now()) 4) |> ignore))
                .GetAwaiter().GetResult()
        match result with
        | Error(DbError.Other e) -> e.Message
        | Error e -> DbError.display e
        | Ok() -> "loaded"

    let columns = Directory.CreateDirectory(Path.Combine(dir.Path, "columns")).FullName
    File.WriteAllText(Path.Combine(columns, "users.yml"), "ghost:\n  name: Ghost\n  bogus: 1\n")
    Assert.Contains("no column bogus", load columns)

    let tables = Directory.CreateDirectory(Path.Combine(dir.Path, "tables")).FullName
    File.WriteAllText(Path.Combine(tables, "nope.yml"), "ghost:\n  name: Ghost\n")
    Assert.Contains("no table nope", load tables)

    use raw = Raw.openFile path
    Assert.Equal(0L, Raw.scalarInt raw "SELECT COUNT(*) FROM users")
