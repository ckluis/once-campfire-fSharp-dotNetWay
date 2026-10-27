/// Design: plans/design-data-ef.md section 5, tests D1-D6: the EF Core mapping over the unchanged Rails schema. One module, so the
/// tests run one after another (D4 counts physical connections through a process-wide counter).
module Campfire.Db.Tests.EfTests

open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Text
open System.Threading
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Xunit
open Campfire.Db
open Campfire.Db.Ef
open Campfire.Db.Tests.Support
open Microsoft.Extensions.Time.Testing

let private at2026 = DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_567L)

/// A database Rails (here: our schema loader) prepared, empty.
let private freshDatabase (dir: TempDir) (name: string) : string =
    let path = dir.File name
    Schema.prepareFile path "production" TimeProvider.System |> ignore
    // The connection `prepare` used stays in Microsoft.Data.Sqlite's pool, configured; D4 counts the ones it opens itself.
    SqliteConnection.ClearAllPools()
    path

let private contextOver (path: string) (time: TimeProvider) : CampfireDb =
    // Test setup writes straight through the context, outside any Writes.run: exempt this test's flow (never disposed; it ends with the test).
    WriteGate.Exempt() |> ignore
    (Data.factory (Data.options path time ignore)).CreateDbContext()

let private withSeedCopy (name: string) (f: string -> TempDir -> unit) : unit =
    match Campfire.Tests.Repo.seed name with
    | None -> Assert.Skip $"parity seed {name} is not built (parity/bin/seed build)"
    | Some seed ->
        use dir = new TempDir()
        let source = Path.Combine(seed, "db")
        for file in [ "production.sqlite3"; "production.sqlite3-wal"; "production.sqlite3-shm" ] do
            let from = Path.Combine(source, file)
            if File.Exists from then File.Copy(from, dir.File file)
        f (dir.File "production.sqlite3") dir

let private tables =
    [ "users"; "accounts"; "rooms"; "sessions"; "memberships"; "messages"; "boosts"; "action_text_rich_texts"; "searches"; "bans"
      "webhooks"; "push_subscriptions"; "active_storage_blobs"; "active_storage_attachments"; "active_storage_variant_records" ]

/// Every row of the 15 tables as SQLite's `quote()` renders it (type-preserving: 1 vs '1' vs 1.0), columns by name, rows by id.
let private canonicalRows (path: string) : (string * string list) list =
    use connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False")
    connection.Open()
    [ for table in tables do
          let columns =
              use command = connection.CreateCommand()
              command.CommandText <- $"SELECT name FROM pragma_table_info('{table}') ORDER BY name"
              use reader = command.ExecuteReader()
              [ while reader.Read() do
                    yield reader.GetString 0 ]
          use command = connection.CreateCommand()
          let select = columns |> List.map (fun c -> $"quote(\"{c}\")") |> String.concat " || ',' || "
          command.CommandText <- $"SELECT {select} FROM \"{table}\" ORDER BY id"
          use reader = command.ExecuteReader()
          yield table, [ while reader.Read() do
                             yield reader.GetString 0 ] ]

// ---- D1: every row of the 15 tables, read through EF and written back to a fresh prepared database ----

let private copyAll (source: CampfireDb) (target: CampfireDb) : unit =
    let save (rows: 'T seq) =
        target.AddRange(rows |> Seq.cast<obj>)
        target.SaveChanges() |> ignore
        target.ChangeTracker.Clear()
    save (source.Users.OrderBy(fun r -> r.Id).ToList())
    save (source.Accounts.OrderBy(fun r -> r.Id).ToList())
    save (source.Rooms.OrderBy(fun r -> r.Id).ToList())
    save (source.Sessions.OrderBy(fun r -> r.Id).ToList())
    save (source.Memberships.OrderBy(fun r -> r.Id).ToList())
    save (source.Messages.OrderBy(fun r -> r.Id).ToList())
    save (source.Boosts.OrderBy(fun r -> r.Id).ToList())
    save (source.RichTexts.OrderBy(fun r -> r.Id).ToList())
    save (source.Searches.OrderBy(fun r -> r.Id).ToList())
    save (source.Bans.OrderBy(fun r -> r.Id).ToList())
    save (source.Webhooks.OrderBy(fun r -> r.Id).ToList())
    save (source.PushSubscriptions.OrderBy(fun r -> r.Id).ToList())
    save (source.Blobs.OrderBy(fun r -> r.Id).ToList())
    save (source.Attachments.OrderBy(fun r -> r.Id).ToList())
    save (source.Variants.OrderBy(fun r -> r.Id).ToList())

let private roundTrip (seed: string) : unit =
    withSeedCopy seed (fun path dir ->
        let target = freshDatabase dir "round-trip.sqlite3"
        do
            use source = contextOver path TimeProvider.System
            use sink = contextOver target TimeProvider.System
            // The seed is loaded the way Rails loads fixtures, with foreign keys off, and holds a message whose creator is
            // not a user: that row must round-trip too. The connection stays open, so the setting holds for every save.
            sink.Database.OpenConnection()
            (let connection = sink.Database.GetDbConnection() in
             use command = connection.CreateCommand()
             command.CommandText <- "PRAGMA foreign_keys = OFF"
             command.ExecuteNonQuery() |> ignore)
            copyAll source sink
        let expected = canonicalRows path
        let actual = canonicalRows target
        for (table, rows), (_, written) in List.zip expected actual do
            Assert.True((rows.Length = written.Length), $"{seed}/{table}: {rows.Length} rows read, {written.Length} written")
            for row, again in List.zip rows written do
                Assert.Equal(row, again)
        // The seed exercises the tables this phase moves (the others are empty in it, which the comparison also covers).
        let count table = expected |> List.find (fun (t, _) -> t = table) |> snd |> List.length
        for table in [ "users"; "rooms"; "memberships"; "messages"; "action_text_rich_texts"; "sessions"; "accounts" ] do
            Assert.True(count table > 0, $"{seed}/{table} has rows")
        // The EF reads cover every timestamp format the seed holds, not just the 19-character one.
        use db = contextOver path TimeProvider.System
        Assert.Equal(count "messages", db.Messages.Count()))

[<Fact>]
let ``D1 every row of the 15 tables of the default seed survives a round trip through EF`` () = roundTrip "default"

[<Fact>]
let ``D1 every row of the 15 tables of the crowd seed survives a round trip through EF`` () = roundTrip "crowd"

// ---- D2: RailsTime ----

let private utc (y, mo, d, h, mi, s) = DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc)

[<Fact>]
let ``D2 toDb writes the fraction only when it is not zero, truncated to microseconds`` () =
    let whole = utc (2026, 9, 26, 12, 34, 56)
    Assert.Equal("2026-09-26 12:34:56", RailsTime.toDb whole)
    Assert.Equal("2026-09-26 12:34:56.123456", RailsTime.toDb (whole.AddTicks 1_234_560L))
    // 100 ns ticks past the sixth digit are cut, not rounded.
    Assert.Equal("2026-09-26 12:34:56.123456", RailsTime.toDb (whole.AddTicks 1_234_569L))
    Assert.Equal("2026-09-26 12:34:56.120000", RailsTime.toDb (whole.AddTicks 1_200_000L))
    Assert.Equal("2026-01-01 00:00:00.000001", RailsTime.toDb ((utc (2026, 1, 1, 0, 0, 0)).AddTicks 10L))
    // Under a microsecond is no fraction at all.
    Assert.Equal("2026-09-26 12:34:56", RailsTime.toDb (whole.AddTicks 9L))
    Assert.Equal(DateTimeKind.Utc, (RailsTime.truncate (whole.AddTicks 9L)).Kind)

[<Fact>]
let ``D2 ofDb reads zero to nine fraction digits`` () =
    let whole = utc (2026, 9, 26, 12, 34, 56)
    Assert.Equal(whole, RailsTime.ofDb "2026-09-26 12:34:56")
    // insert_all: SQLite's STRFTIME('%f') writes milliseconds.
    Assert.Equal(whole.AddTicks 1_230_000L, RailsTime.ofDb "2026-09-26 12:34:56.123")
    Assert.Equal(whole.AddTicks 1_234_560L, RailsTime.ofDb "2026-09-26 12:34:56.123456")
    Assert.Equal(whole.AddTicks 1_234_560L, RailsTime.ofDb "2026-09-26 12:34:56.1234567")
    Assert.Equal(whole.AddTicks 1_234_560L, RailsTime.ofDb "2026-09-26 12:34:56.123456789")
    Assert.Equal(whole.AddTicks 5_000_000L, RailsTime.ofDb "2026-09-26 12:34:56.5")
    Assert.Equal(DateTimeKind.Utc, (RailsTime.ofDb "2026-09-26 12:34:56.5").Kind)
    // Also what only the long way reads, and what nothing reads.
    Assert.Equal(whole.AddTicks 5_000_000L, RailsTime.ofDb "2026-09-26T12:34:56.5Z")
    Assert.Equal(whole.AddTicks 5_000_000L, RailsTime.ofDb "2026-09-26 12:34:56.5 UTC")
    for bad in [ "yesterday"; "2026-13-01 00:00:00"; "2026-02-30 00:00:00"; "2026-09-26 12:34:56."; "2026-09-26 12:34:56.x"; "" ] do
        Assert.True((try RailsTime.ofDb bad |> ignore; false with :? FormatException -> true), $"'{bad}' should not parse")

[<Fact>]
let ``D2 toUsec and toEpoch are Time#to_fs(usec) and to_fs(epoch)`` () =
    let t = (utc (2026, 9, 26, 12, 34, 56)).AddTicks 1_234_569L
    Assert.Equal("20260926123456123456", RailsTime.toUsec t)
    Assert.Equal("20260926123456000000", RailsTime.toUsec (utc (2026, 9, 26, 12, 34, 56)))
    Assert.Equal(1_790_426_096L, RailsTime.toEpoch t)
    Assert.Equal(0L, RailsTime.toEpoch DateTime.UnixEpoch)

[<Fact>]
let ``D2 now is the provider's UTC time truncated to microseconds`` () =
    let time = FakeTimeProvider(at2026)
    Assert.Equal("2026-10-13 12:00:00.123456", RailsTime.toDb (RailsTime.now time))

// ---- D3: created_at / updated_at ----

let private row (path: string) (sql: string) : string list =
    use connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False")
    connection.Open()
    use command = connection.CreateCommand()
    command.CommandText <- sql
    use reader = command.ExecuteReader()
    [ while reader.Read() do
          yield! [ for i in 0 .. reader.FieldCount - 1 -> if reader.IsDBNull i then "NULL" else string (reader.GetValue i) ] ]

[<Fact>]
let ``D3 an Add of each entity with unset timestamps stores the provider's time in Rails' text`` () =
    use dir = new TempDir()
    let path = freshDatabase dir "stamps.sqlite3"
    let time = FakeTimeProvider(at2026)
    use db = contextOver path time
    let unset = Unchecked.defaultof<DateTime>
    let add (entity: obj) =
        db.Add entity |> ignore
        db.SaveChanges() |> ignore
        db.ChangeTracker.Clear()
    add { Id = 0L; Name = "Jason"; EmailAddress = "j@example.com"; PasswordDigest = null; Role = Role.Member; Status = Status.Active; Bio = null; BotToken = null; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; Name = "All talk"; Type = RoomType.Open; CreatorId = 1L; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; RoomId = 1L; CreatorId = 1L; ClientMessageId = "c1"; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; MessageId = 1L; BoosterId = 1L; Content = "+1"; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; RecordType = "Message"; RecordId = 1L; Name = "body"; Body = "<p>hi</p>"; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; Name = "Co"; JoinCode = "abcd-efgh"; CustomStyles = null; Settings = null; SingletonGuard = 0L; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; UserId = 1L; Query = "coffee"; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; UserId = 1L; IpAddress = "10.0.0.1"; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; UserId = 1L; Url = "https://example.com/hook"; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; UserId = 1L; Endpoint = "https://push.example.com/x"; P256dhKey = "p"; AuthKey = "a"; UserAgent = null; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; UserId = 1L; Token = "tok"; IpAddress = null; UserAgent = null; LastActiveAt = at2026.UtcDateTime; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; RoomId = 1L; UserId = 1L; Involvement = Nullable Involvement.Everything; UnreadAt = Nullable(); ConnectedAt = Nullable(); Connections = 0L; CreatedAt = unset; UpdatedAt = unset }
    add { Id = 0L; Key = "k"; Filename = "f.png"; ContentType = "image/png"; Metadata = "{}"; ServiceName = "local"; ByteSize = 3L; Checksum = null; CreatedAt = unset }
    add { Id = 0L; Name = "avatar"; RecordType = "User"; RecordId = 1L; BlobId = 1L; CreatedAt = unset }
    add { Id = 0L; BlobId = 1L; VariationDigest = "d" }
    let expected = "2026-10-13 12:00:00.123456"
    for table in tables |> List.filter (fun t -> t <> "active_storage_variant_records") do
        let columns = if table.StartsWith "active_storage_" && table <> "active_storage_variant_records" then "created_at" else "created_at, updated_at"
        let columns = if table = "active_storage_blobs" || table = "active_storage_attachments" then "created_at" else columns
        let found = row path $"SELECT {columns} FROM {table}"
        Assert.True(found |> List.forall (fun v -> v = expected), $"{table}: {found}")
    // A value the caller set is left alone.
    let mine = utc (2020, 1, 2, 3, 4, 5)
    add { Id = 0L; UserId = 1L; Query = "mine"; CreatedAt = mine; UpdatedAt = mine }
    Assert.Equal<string list>([ "2020-01-02 03:04:05"; "2020-01-02 03:04:05" ], row path "SELECT created_at, updated_at FROM searches WHERE query = 'mine'")

[<Fact>]
let ``D3 SetValues with a change stamps updated_at, without one writes nothing`` () =
    use dir = new TempDir()
    let path = freshDatabase dir "set-values.sqlite3"
    let time = FakeTimeProvider(at2026)
    use db = contextOver path time
    let user = { Id = 0L; Name = "Jason"; EmailAddress = "j@example.com"; PasswordDigest = null; Role = Role.Member; Status = Status.Active; Bio = null; BotToken = null; CreatedAt = Unchecked.defaultof<DateTime>; UpdatedAt = Unchecked.defaultof<DateTime> }
    db.Add user |> ignore
    db.SaveChanges() |> ignore
    let stored = db.Users.AsNoTracking().Single()
    db.ChangeTracker.Clear()
    Assert.Equal<string list>([ "2026-10-13 12:00:00.123456" ], row path "SELECT updated_at FROM users")

    time.Advance(TimeSpan.FromSeconds 60.0)
    // Unchanged: the entry is not Modified, so no UPDATE and no stamp.
    let entry = db.Attach stored
    entry.CurrentValues.SetValues { stored with Bio = null }
    Assert.Equal(0, db.SaveChanges())
    db.ChangeTracker.Clear()
    Assert.Equal<string list>([ "2026-10-13 12:00:00.123456" ], row path "SELECT updated_at FROM users")

    // Changed: only Bio is modified, and the interceptor stamps updated_at.
    let entry = db.Attach stored
    entry.CurrentValues.SetValues { stored with Bio = "hello" }
    Assert.Equal<string list>([ "Bio" ], [ for p in entry.Properties do if p.IsModified then p.Metadata.Name ])
    Assert.Equal(1, db.SaveChanges())
    db.ChangeTracker.Clear()
    Assert.Equal<string list>([ "hello"; "2026-10-13 12:01:00.123456"; "2026-10-13 12:00:00.123456" ], row path "SELECT bio, updated_at, created_at FROM users")

// ---- D4: pragmas ----

[<Fact>]
let ``D4 every pooled connection has Rails' pragmas and the counter equals the physical connections`` () =
    use dir = new TempDir()
    let path = freshDatabase dir "pragmas.sqlite3"
    let factory = Data.factory (Data.options path TimeProvider.System ignore)
    let before = SqlitePragmas.Applied
    let barrier = new Barrier 3
    let results = Array.zeroCreate<(int64 * string * int64 * int64 * nativeint)> 3
    let threads =
        [| for i in 0..2 ->
               Thread(fun () ->
                   use db = factory.CreateDbContext()
                   db.Database.OpenConnection()
                   // All three are open at once, so they are three physical connections.
                   barrier.SignalAndWait() |> ignore
                   let connection = db.Database.GetDbConnection() :?> SqliteConnection
                   let scalar (sql: string) =
                       use command = connection.CreateCommand()
                       command.CommandText <- sql
                       command.ExecuteScalar()
                   results[i] <-
                       (Convert.ToInt64(scalar "PRAGMA busy_timeout"),
                        string (scalar "PRAGMA journal_mode"),
                        Convert.ToInt64(scalar "PRAGMA foreign_keys"),
                        Convert.ToInt64(scalar "PRAGMA synchronous"),
                        (nonNull connection.Handle).DangerousGetHandle())
                   barrier.SignalAndWait() |> ignore
                   db.Database.CloseConnection()) |]
    for t in threads do t.Start()
    for t in threads do t.Join()
    for (busy, journal, foreignKeys, synchronous, _) in results do
        Assert.Equal(5000L, busy)
        Assert.Equal("wal", journal)
        Assert.Equal(1L, foreignKeys)
        Assert.Equal(1L, synchronous) // NORMAL
    let handles = results |> Array.map (fun (_, _, _, _, h) -> h) |> Array.distinct
    Assert.Equal(3, handles.Length)
    Assert.Equal(handles.Length, SqlitePragmas.Applied - before)
    // Later queries on the pooled connections add none.
    for _ in 1..20 do
        use db = factory.CreateDbContext()
        db.Users.Count() |> ignore
    Assert.Equal(handles.Length, SqlitePragmas.Applied - before)

// ---- D5: the parameter guard ----

[<Fact>]
let ``D5 a DateTime hole in raw SQL throws and Rails' text runs`` () =
    use dir = new TempDir()
    let path = freshDatabase dir "guard.sqlite3"
    use db = contextOver path TimeProvider.System
    let cutoff = utc (2026, 3, 2, 12, 0, 0)
    let ex = Assert.ThrowsAny<Exception>(fun () -> db.Users.FromSql($"SELECT * FROM users WHERE created_at < {cutoff}").ToList() |> ignore)
    Assert.Contains("RailsTime.toDb", ex.Message)
    let offset = DateTimeOffset(cutoff)
    Assert.ThrowsAny<Exception>(fun () -> db.Database.ExecuteSql($"UPDATE users SET bio = NULL WHERE created_at < {offset}") |> ignore) |> ignore
    let text = RailsTime.toDb cutoff
    Assert.Empty(db.Users.FromSql($"SELECT * FROM users WHERE created_at < {text}").ToList())
    // LINQ parameters go through the converter, so they are text already and pass.
    Assert.Empty(db.Users.Where(fun u -> u.CreatedAt < cutoff).ToList())

// ---- D6: enums and STI ----

[<Fact>]
let ``D6 Rooms::Closed and involvement everything round-trip as Rails' strings, an unknown type fails loudly`` () =
    use dir = new TempDir()
    let path = freshDatabase dir "enums.sqlite3"
    use db = contextOver path (FakeTimeProvider(at2026))
    let unset = Unchecked.defaultof<DateTime>
    db.Add { Id = 0L; Name = "Jason"; EmailAddress = "j@example.com"; PasswordDigest = null; Role = Role.Administrator; Status = Status.Deactivated; Bio = null; BotToken = null; CreatedAt = unset; UpdatedAt = unset } |> ignore
    db.SaveChanges() |> ignore
    db.Add { Id = 0L; Name = "Closed"; Type = RoomType.Closed; CreatorId = 1L; CreatedAt = unset; UpdatedAt = unset } |> ignore
    db.Add { Id = 0L; Name = null; Type = RoomType.Direct; CreatorId = 1L; CreatedAt = unset; UpdatedAt = unset } |> ignore
    db.SaveChanges() |> ignore
    db.Add { Id = 0L; RoomId = 1L; UserId = 1L; Involvement = Nullable Involvement.Everything; UnreadAt = Nullable(); ConnectedAt = Nullable(); Connections = 0L; CreatedAt = unset; UpdatedAt = unset } |> ignore
    db.Add { Id = 0L; RoomId = 2L; UserId = 1L; Involvement = Nullable(); UnreadAt = Nullable(); ConnectedAt = Nullable(); Connections = 0L; CreatedAt = unset; UpdatedAt = unset } |> ignore
    db.SaveChanges() |> ignore
    db.ChangeTracker.Clear()
    Assert.Equal<string list>([ "Rooms::Closed"; "Rooms::Direct" ], row path "SELECT type FROM rooms ORDER BY id")
    Assert.Equal<string list>([ "everything"; "NULL" ], row path "SELECT COALESCE(involvement, 'NULL') FROM memberships ORDER BY id")
    Assert.Equal<string list>([ "1"; "1" ], row path "SELECT role, status FROM users")
    let rooms = db.Rooms.OrderBy(fun r -> r.Id).ToList()
    Assert.Equal<RoomType list>([ RoomType.Closed; RoomType.Direct ], [ for r in rooms -> r.Type ])
    let memberships = db.Memberships.OrderBy(fun m -> m.Id).ToList()
    Assert.Equal(Nullable Involvement.Everything, memberships[0].Involvement)
    Assert.False(memberships[1].Involvement.HasValue)
    let user = db.Users.Single()
    Assert.Equal(Role.Administrator, user.Role)
    Assert.Equal(Status.Deactivated, user.Status)
    // An STI class this port does not know is a bug in the data, not a default.
    let weird = "Rooms::Weird"
    db.Database.ExecuteSql($"UPDATE rooms SET type = {weird} WHERE id = 1") |> ignore
    let ex = Assert.ThrowsAny<Exception>(fun () -> db.Rooms.OrderBy(fun r -> r.Id).ToList() |> ignore)
    Assert.Contains("Rooms::Weird", ex.ToString())
