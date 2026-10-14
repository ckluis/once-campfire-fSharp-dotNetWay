# Design note: the Data layer on EF Core (Phase 1)

Status: design, 2026-10-13, before Phase 1 builds. Author: the Opus designer (plan section 5). Binding for the Phase 1 builders
together with `plans/dotnetway.md` sections 3-5 and D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`).
Where this note and plan section 2.1 disagree, this note wins: it is later and rests on the spikes in section 2, on Chris's
answer to Q1 (option 1, recorded in the decisions file under "Decided after the design") and on his .NET 11 decision.

Scope: `Campfire.Db` (15 Rails tables, the FTS5 index, the schema check, fixtures) and the SQL in `Campfire.Storage`, moved onto
EF Core over the unchanged Rails schema; the write path (one writer, `BEGIN IMMEDIATE`, Active Record's callbacks and
after-commit work); every call site in `Campfire.App` and `Campfire.Storage`. Out of scope: controllers' move off `Ctx`
(Phase 2), Cable's per-command DI scopes (Phase 3), the fragment cache (Phase 4).

Phase 1 unit 1.0 (the .NET 11 SDK move) comes first, as Chris decided; everything after it targets `net11.0`.

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity forces it), with the reason.

| # | Decision | Reason |
|---|---|---|
| E1 | **`Microsoft.EntityFrameworkCore.Sqlite` 10.0.13** (LTS; pulls `Microsoft.Data.Sqlite` 10.0.13) on the net11.0 runtime, plus **`EFCore.NamingConventions` 10.0.1** (`UseSnakeCaseNamingConvention()`). EF Core 11 waits for its GA (November 2026) *and* a NamingConventions build for it, taken in the same unit that moves the SDK to .NET 11 GA. | EF 10 is the current stable LTS and runs on .NET 11. NamingConventions (efcore org, 81 M downloads, pushed 2026-10-09) is the mainstream way to map PascalCase properties onto Rails' snake_case columns; its 10.0.1 declares `Microsoft.EntityFrameworkCore [10.0.1, 11.0.0)`, so EF 11 RC2 would raise NU1608, an error under `TreatWarningsAsErrors`. EF 11 brings nothing this project uses (plan decisions: "little for SQLite"). |
| E2 | **One `CampfireDb : DbContext`**, entities as `[<CLIMutable>]` F# records named as the port names them (`User`, `Session`, `Room`, `Membership`, `Message`, `Boost`, `RichTextRecord`, `Account`, `Search`, `Ban`, `Webhook`, `PushSubscription`, `Blob`, `Attachment`, `VariantRecord`), `ToTable` with Rails' table names, columns by the snake-case convention. No navigations, no `Include`. | Rails' associations here are mostly polymorphic (`record_type`/`record_id`) or used one direction; joins are written where they are needed. Navigations on records would need mutable collections. Spike S1/S2: the convention maps every column of the tables tried, nullability is read from F# 9 nullness (`email_address` nullable, `name` not). |
| E3 | **Nullable columns are `string \| null` and `Nullable<'T>`**, never `option`, in entities. F# logic converts at the edge (`Option.ofObj`, `Option.ofNullable`). Enums are the port's F# enums (`Role`, `Status` stored as integers, natively); `Involvement` and `RoomType` (Rails' STI strings `Rooms::Open/Closed/Direct`) through a `ValueConverter` each. One `Room` entity with a `Type: RoomType` property, not EF TPH. | EF has no `option` mapping (plan 2.1). The STI strings must stay byte-identical; TPH would add a discriminator EF owns. Spike S1: involvements, room types and `Nullable<DateTime>` read back from the seed correctly. |
| E4 | **Timestamps are `DateTime` (UTC) in entities**, converted to and from Active Record's text by one `RailsTimeConverter` applied to every `DateTime` and `Nullable<DateTime>` property in `ConfigureConventions` (`Properties<DateTime>().HaveConversion<RailsTimeConverter>()`). The port's `Timestamp` struct goes; `RailsTime` keeps its rules (truncate to microseconds; `YYYY-MM-DD HH:MM:SS` plus `.ffffff` only when non-zero; parse 0-9 fraction digits). | The .NET type; LINQ comparisons on converted columns translate to text comparisons, which order correctly for Rails' format (Rails compares the same text). Spike S2: `2026-10-13 12:00:00` and `.123000` written exactly as Rails; a LINQ `CreatedAt < before` sends `'2026-03-02 12:00:00.001230'`. `DateTimeOffset` was not chosen: EF's SQLite provider restricts it in `ORDER BY`. |
| E5 | **No `DateTime` ever reaches raw SQL.** In `FromSql`/`ExecuteSql`/`SqlQuery` interpolations, times are passed as `RailsTime.toDb t` strings. A `RailsParameterGuard : DbCommandInterceptor`, always on, throws `InvalidOperationException` on any `DateTime`/`DateTimeOffset` parameter value. | Spike S2: Microsoft.Data.Sqlite binds a `DateTime` as `'2026-10-13 12:00:00.1230004'` (7 digits), which Rails never writes and which compares wrongly against stored text. LINQ parameters go through the converter and are text already, so the guard only ever fires on raw SQL. |
| E6 | **The synchronous EF API everywhere** (`ToList`, `FirstOrDefault`, `Find`, `SaveChanges`, `ExecuteUpdate`, `ExecuteSql`, `BeginTransaction`). Model functions stay `CampfireDb -> ... -> 'T` and `Tx -> ... -> 'T`. Only the write gate is awaited (`SemaphoreSlim.WaitAsync`). | Microsoft's own guidance for this provider: "SQLite doesn't support asynchronous I/O. Async ADO.NET methods will execute synchronously in Microsoft.Data.Sqlite. Avoid calling them." (learn.microsoft.com, Microsoft.Data.Sqlite "Async limitations"). Spike S4: sync and async cost the same (room page 117 vs 121 us), and sync keeps the presenters' synchronous lookups (`Presenter.User`, `Message.bodyHtml`) as they are instead of turning view-model building async. |
| E7 | **Contexts come from a `PooledDbContextFactory<CampfireDb>`** built once at boot (no DI needed for it), held by `AppState` as `IDbContextFactory<CampfireDb>`, and registered in the host's DI as that singleton plus `AddScoped<CampfireDb>(fun sp -> factory.CreateDbContext())`. Requests use the scoped instance (`c.Db`); jobs, channels and boot take `use db = app.DbFactory.CreateDbContext()` per unit of work. | Both are EF's documented pooling patterns ("DbContext pooling without dependency injection"; the scoped-from-factory registration). Boot builds `AppState` before Front builds the host, so the factory cannot come from the host's container. Spike S3: a disposed context returns to the pool and the next lease (direct or through a DI scope) gets the same instance; renting and returning costs 0.1 us. |
| E8 | **No tracking by default** (`UseQueryTrackingBehavior NoTracking`). Updates use `ExecuteUpdate`/`ExecuteSql` for column updates (`update_columns`, `touch`, counters) and, where a loaded record is changed as a whole, `db.Attach(old).CurrentValues.SetValues(newRecord)` then `SaveChanges`. Inserts are `Add` + `SaveChanges`; the id is read from the entity afterwards (EF writes it into the record). `Writes.run` clears the change tracker when it ends. | Reads dominate. Spike S2: `SetValues` marks only the changed property (`["UnreadAt"]`) and the timestamps interceptor then stamps `updated_at`; `ExecuteUpdate` with `Connections + 1` increments in SQL. |
| E9 | **Active Record's `created_at`/`updated_at`** come from `RailsTimestamps : SaveChangesInterceptor` over the app's `TimeProvider`: `Added` entities get both when unset, `Modified` entities get `updated_at` unless it was set. `ExecuteUpdate`/`ExecuteSql` writes set `updated_at` explicitly when Rails would (`touch`, `increment!(touch: true)`), and leave it when Rails does (`update_columns`). | EF's documented audit-timestamp idiom. One `now` per `SaveChanges`, as Active Record uses one per save. The harness's frozen clock reaches it through the `TimeProvider` (E14). |
| E10 | **Connection settings once per physical connection**: `SqlitePragmas : DbConnectionInterceptor` runs Rails' pragmas (`busy_timeout = 5000`, `foreign_keys = ON`, `journal_mode = wal`, `synchronous = normal`, `journal_size_limit = 67108864`, `cache_size = 2000`) in `ConnectionOpened` the first time it sees a `sqlite3` handle (`ConditionalWeakTable`). EF's `CommandTimeout` is 1 s, so Microsoft.Data.Sqlite's own busy-retry loop ends when SQLite's busy handler has. SQLite's auto-checkpoint (1,000 pages, Rails' behaviour) does the WAL; the checkpointer thread goes. | EF opens and closes the connection per query and Microsoft.Data.Sqlite pools the physical connections, so a plain interceptor would run six pragmas per query. Spike S4: 1 handle configured for every single-threaded run, 11 after 16 concurrent writers. |
| E11 | **One writer: `Writes.run`.** A process-wide `WriteGate` (`SemaphoreSlim(1)`) is awaited, then `BeginTransaction()` (Microsoft.Data.Sqlite's default is `BEGIN IMMEDIATE`, Rails' `default_transaction_mode: immediate`; plan Appendix B), the body, `SaveChanges()`, `Commit()`; then the after-commit queue in order (events to the sink, hooks as their own autocommit statements), still holding the gate; then the gate is released. Reads never take it. The gate also sets an `AsyncLocal<bool>` (`WriteGate.InWrite`) that Phase 3's `Publish.broadcast` guard reads (design-cable C16): true from `BEGIN` to `COMMIT` (the body and its `SaveChanges`), false again before the after-commit queue runs, because that queue is where publishing belongs (the event sink hands `DisconnectUser` to `Revocation` synchronously, and a hook may broadcast). | The port's writer thread, without the thread. Holding the gate across the after-commit statements keeps the port's (and Rails' single-writer) order: no other write lands between a commit and its `after_commit` rows. Spike S5: under 16 concurrent writers the gate serialises cleanly; no `SQLITE_BUSY`. |
| E12 | **Query style (Q1 = option 1).** On the five workloads' paths every statement is `FromSql`/`SqlQuery`/`ExecuteSql` with interpolated holes, or `DbSet.Find`. Elsewhere LINQ with F# lambdas, under three rules: (a) **every value a lambda uses comes from a record field** (`let p = {\| Room = roomId \|}` then `m.RoomId = p.Room`), never a captured local or argument; (b) **no lambda inside a lambda** (no `Any`/`Where`/`Count` subqueries; use `Join` with a result selector returning one entity, or SQL); (c) **no tuple or anonymous-record projections** (one entity per query; two-entity rows through `SqlQuery<'Row>` onto a `[<CLIMutable>]` row record). `ExecuteUpdate` setters use the expression overload over the same record (`(fun _ -> p.Now)`), never the value overload. Never F#'s `query { }`. **No `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw`** outside `Schema.prepare` (which runs the constant `schema.sql`): the interpolated forms are the parameterized ones, and a `bin/verify` grep fails on a `*Raw` call anywhere else (SQL injection by construction). | Spike S4, the finding that shapes the phase: **F# turns a captured local into a constant in the expression tree, EF inlines it into the SQL, and every new value compiles a new query: 1,062-1,081 us per call** against 30-34 us when parameterized. The post-a-message write with captured locals in `ExecuteUpdate` managed **435 posts/s** with 16 writers (under the 1,650 floor); the same with record parameters 1,974; with `ExecuteSql` touches 4,185. Nested lambdas fail at run time in plain LINQ too (`InvalidCastException`, the same `QuotationToLambdaExpression` cause as the compiled-query failure); struct-tuple projections fail (`ArgumentException: The tuple index '1' was out of range`). |
| E13 | **FTS5 through raw SQL**: reads `db.Messages.FromSql($"SELECT messages.* FROM messages ... JOIN message_search_index idx ON messages.id = idx.rowid WHERE ... (idx.body MATCH {terms}) ...")`, writes `db.Database.ExecuteSql($"INSERT INTO message_search_index(rowid, body) VALUES ({id}, {plain})")` (and the update/delete twins) in after-commit hooks. `matchTerms` (the port's quoting) stays. The index is not in the EF model. | EF has no FTS5 support; plan 2.1. Spike S4: 13 hits for `coffee`, the port's 13. |
| E14 | **`TimeProvider` arrives in Phase 1**: `ClockTimeProvider(clock: SharedClock)` (about 15 lines, `GetUtcNow` from the shared clock, so the harness's frozen clock reaches EF), registered as the `TimeProvider` singleton and given to `RailsTimestamps` and `Env`. | The Kit and Cable notes take it as a Phase 1 precondition. `KitClock`/`RailsCompat.Clock` stay until Phase 2 deletes them. |
| E15 | **Schema and boot without migrations**: `Schema.prepare (db: CampfireDb)` loads `schema.sql` with `ExecuteSqlRaw` inside `BeginTransaction` into an empty database, checks `schema_migrations` with `SqlQuery<string>`, records `ar_internal_metadata`, and adds the paging index, as now. Never `EnsureCreated`, never `Migrate`. The `backup` command keeps its `SqliteConnection.BackupDatabase` (SQLite's online backup, no EF equivalent). | Rails owns the schema (plan 2.1); `EnsureCreated` would create EF's idea of it. |
| E16 | **Active Record's callbacks stay explicit in the model functions**, as the port has them (table in section 3.6). No `SavedChanges`/transaction interceptors for them. | Plan 2.1 ("ordering would be implicit"); Cable C16 depends on it. |

What stays hand-written, and why (parity): `RailsTime` and its converter (Active Record's text), the two enum-string converters
(Rails' STI and enum strings), the pragma and timestamp interceptors (Rails' connection settings and stamping), the parameter
guard, `Writes.run` and `Tx` (Rails' transaction and `after_commit` order), the FTS5 SQL, the hot-path SQL (Q1), and the domain
logic the port already has (validations, payloads, initials, membership rules).

## 2. Spikes (scratch project, 2026-10-13, .NET SDK 10.0.401, EF Core 10.0.13, EFCore.NamingConventions 10.0.1)

`/private/tmp/claude-501/-Users-clank-Desktop-projects/fea4ed62-8823-4b82-bed5-fc54b6214d57/scratchpad/efspike/` (`Model.fs`,
`Program.fs`, `spike-output.txt`; build output deleted). A copy of `parity/.seed/default/db/production.sqlite3` with the port's
boot index added, a `PooledDbContextFactory`, the interceptors of E9/E10, nine entities. Release build, one thread unless said,
best of 5 after a 2 s warm-up, on this Mac with load average 2.3-4.9 from other projects (numbers are for comparing variants, not
results). No .NET 11 SDK on the Mac (and 6.6-8.0 GB free disk during this work); nothing used is new in EF 10 except
`EF.Parameter` (EF 9).

| # | Question | Result |
|---|---|---|
| S1 | Snake-case convention + `[<CLIMutable>]` records + enum converters on the seed | users, sessions, rooms, memberships read; `Role.Administrator`, involvements `Mentions/Everything/Nothing/Invisible`, room types `Open/Closed/Direct`; `memberships.connected_at`/`unread_at` nullable with `RailsTimeConverter`; `FirstOrDefault` of a missing row through `Option.ofObj` is `None` (F# 9 sees EF's result as nullable) |
| S2 | Rails' text on write; parameters | `created_at = '2026-10-13 12:00:00'`, `updated_at = '2026-10-13 12:00:00.123000'` from `12:00:00.1230004`; LINQ comparison parameter `'2026-03-02 12:00:00.001230'`; raw `SqliteParameter(DateTime)` binds `'2026-10-13 12:00:00.1230004'` (E5); `SetValues` marks only `UnreadAt`, interceptor stamps `updated_at`; `ExecuteUpdate(Connections + 1)` gives 1 |
| S2b | Captured locals vs parameters (`LogTo` on `CoreEventId.QueryCompilationStarting`) | three ids: captured local **3 compilations** (SQL shows `"room_id" = 486777696` inline), record member 1 (`@Room`), `EF.Parameter` 1, `FromSql` 1 |
| S3 | Pooling | `PooledDbContextFactory` returns the same instance after dispose; a DI scope resolving `CampfireDb` from the factory registration gets the pooled instance back |
| S4 | Cost per call (us, and bytes allocated) | one statement: `FromSql` by id 17.4-19.5, `Find` 18.0, LINQ parameterized 33-36, LINQ with a **new constant every call 1,062-1,081 (197 KB)**, `SqlQuery SELECT 1` 12.4. Pages (port `bench/db-micro` the same day in brackets, which includes ~27 us of SQLite sorting because db-micro opens the seed without the boot index): session + user 40-41 FromSql / 80-142 LINQ [2.4]; room page 4 queries 112-121 FromSql / 315-327 LINQ, sync 110-117 / 307-324 [37.3]; messages page 47-49 / 102-108 [28.8]; sidebar 152-171 FromSql+SqlQuery / 627-641 LINQ [20.7]; search 66 / 96-101 [14.4] |
| S5 | Post a message: 2 reads, then under the gate a transaction (2 inserts, 2 touches, commit) and 2 after-commit statements | gate held: v1 `ExecuteUpdate` with captured locals 549-569 us; v2 `ExecuteUpdate` with record parameters 298-304; **v3 `Add`/`SaveChanges` inserts + `ExecuteSql` touches 185-188**; v4 all `SqlQuery`/`ExecuteSql` 166-171; v5 = v3 on the sync API 212; raw prepared ADO.NET (the port's shape) 100. **16 concurrent writers, 3 s: v1 435-1,598 posts/s, v2 1,974-2,939, v3 4,185-4,520, v5 3,878.** No busy errors; 171,460 rows written across the runs |
| S6 | Plain LINQ shapes | `Where(fun r -> db.Memberships.Any(fun m -> ...))`: `InvalidCastException` (`MethodCallExpression1` to `LambdaExpression`); `Join(..., fun m r -> struct (m, r))` then `OrderBy` on it: `ArgumentException` (tuple index); `Join(..., fun _ r -> r)` works; `SqlQuery<SidebarRow>` onto an unmapped `[<CLIMutable>]` record with snake-case aliases works |
| S7 | Nullness | `r.Name.ToLower()` inside a LINQ lambda over a `string \| null` column raises FS3261 (an error under the repo's settings) |

## 3. Types and modules

### 3.1 `Campfire.Db` file order (`Campfire.Db.fsproj`)

`RailsTime.fs`, `Error.fs`, `Events.fs`, `Entities.fs`, `CampfireDb.fs`, `Interceptors.fs`, `Data.fs`, `Writes.fs`, `Schema.fs`,
`RichText.fs`, then the model modules as now (`Sound`, `Session`, `Ban`, `Search`, `RichTextRecord`, `ActiveStorage`, `Account`,
`Models`, `PushSubscription`, `FirstRun`), `Fixtures.fs`. `schema.sql` stays embedded. References: `Campfire.RailsCompat`;
packages `Microsoft.EntityFrameworkCore.Sqlite`, `EFCore.NamingConventions`. `Campfire.Storage` gains a reference to
`Campfire.Db` and loses its `Microsoft.Data.Sqlite` one.

```fsharp
module Campfire.Db.RailsTime                       // replaces Time.fs (188 lines -> ~40)
val truncate: DateTime -> DateTime                 // to microseconds, Kind = Utc
val now: TimeProvider -> DateTime                  // truncate (GetUtcNow().UtcDateTime)
val toDb: DateTime -> string                       // "YYYY-MM-DD HH:MM:SS" [+ ".ffffff" when non-zero]
val ofDb: string -> DateTime                       // 0-9 fraction digits, as Rails and SQLite's STRFTIME write them
val toUsec: DateTime -> string                     // Time#to_fs(:usec), for cache_version
val toEpoch: DateTime -> int64                     // to_fs(:epoch)

type RailsTimeConverter() = inherit ValueConverter<DateTime, string>((fun d -> RailsTime.toDb d), (fun s -> RailsTime.ofDb s))
```

```fsharp
// Entities.fs: the port's enums unchanged (Role, Status, Involvement, RoomType), and one record per table
[<CLIMutable>]
type User =
    { Id: int64; Name: string; EmailAddress: string | null; PasswordDigest: string | null; Role: Role; Status: Status
      Bio: string | null; BotToken: string | null; CreatedAt: DateTime; UpdatedAt: DateTime }
[<CLIMutable>]
type Membership =
    { Id: int64; RoomId: int64; UserId: int64; Involvement: Nullable<Involvement>; UnreadAt: Nullable<DateTime>
      ConnectedAt: Nullable<DateTime>; Connections: int64; CreatedAt: DateTime; UpdatedAt: DateTime }
[<CLIMutable>] type Room = { Id: int64; Name: string | null; Type: RoomType; CreatorId: int64; CreatedAt: DateTime; UpdatedAt: DateTime }
// ... Session, Message, Boost, RichTextRecord (record_type/record_id/name/body), Account (Settings: string | null, the JSON text),
// Search, Ban, Webhook, PushSubscription (P256dhKey: HasColumnName "p256dh_key" if the convention splits the digits otherwise),
// Blob (Metadata: string | null, JSON text), Attachment, VariantRecord.
// Row records for two-entity reads, e.g.:
[<CLIMutable>]
type MembershipRoomRow = { MembershipId: int64; RoomId: int64; Involvement: string | null; UnreadAt: Nullable<DateTime>; ... }
```

Property names follow the columns (the convention derives the column from the name), so where the port's field name differs it
changes (`Account.SettingsJson` -> `Settings`, `Room.RoomType` -> `Type`) or gets `HasColumnName`. Nothing depends on column
order: EF reads by name (the port's `Columns.fs` existed because a migrated and a fresh Rails database order columns differently).

```fsharp
type CampfireDb(options: DbContextOptions<CampfireDb>) =
    inherit DbContext(options)
    member this.Users = this.Set<User>()      // one DbSet per table, 15
    ...
    override _.ConfigureConventions(b) = b.Properties<DateTime>().HaveConversion<RailsTimeConverter>() |> ignore
    override _.OnModelCreating(b) = // ToTable for each; RoomType and Involvement converters; HasColumnName where needed

module Data =
    /// The options every context uses: `Data Source=<path>`, CommandTimeout 1 s, snake case, NoTracking,
    /// interceptors (SqlitePragmas, RailsTimestamps time, RailsParameterGuard), plus `extra` (tests: LogTo).
    val options: path: string -> time: TimeProvider -> extra: (DbContextOptionsBuilder<CampfireDb> -> unit) -> DbContextOptions<CampfireDb>
    val factory: DbContextOptions<CampfireDb> -> IDbContextFactory<CampfireDb>     // PooledDbContextFactory, poolSize 1024
    val tryFirst: IQueryable<'T> -> 'T option                                       // FirstOrDefault |> Option.ofObj
```

### 3.2 Interceptors (`Interceptors.fs`, about 70 lines)

- `SqlitePragmas() : DbConnectionInterceptor`: E10 (`ConnectionOpened` and `ConnectionOpenedAsync`; a static counter of
  configured handles that tests read).
- `RailsTimestamps(time: TimeProvider) : SaveChangesInterceptor`: E9 (`SavingChanges`, `SavingChangesAsync`); looks up
  `CreatedAt`/`UpdatedAt` by property name on each entry's metadata.
- `RailsParameterGuard() : DbCommandInterceptor`: E5, in `ReaderExecuting`, `NonQueryExecuting`, `ScalarExecuting` (and their
  async twins, for completeness).

### 3.3 The write path (`Writes.fs`, about 90 lines)

```fsharp
type Env = { Time: TimeProvider; Sink: EventSink; RichText: RichText; BcryptCost: int }

[<Sealed>]
type WriteGate =
    new: unit -> WriteGate
    member Enter: unit -> Task          // SemaphoreSlim(1,1).WaitAsync
    member Exit: unit -> unit
    static member InWrite: bool         // AsyncLocal<bool>, true from BEGIN to COMMIT in Writes.run, false in the after-commit queue (design-cable C16)

[<Sealed; NoEquality; NoComparison>]
type Tx =                               // the port's Tx, over a CampfireDb
    member Db: CampfireDb
    member Env: Env
    member Now: unit -> DateTime        // RailsTime.now Env.Time
    member RichText: RichText
    member EmitNow: Event -> unit
    member EmitAfterCommit: Event -> unit
    member AfterCommit: (Tx -> unit) -> unit
    member InTransaction: bool

module Writes =
    /// Enter the gate; BEGIN IMMEDIATE; body; SaveChanges; COMMIT. An exception (or DbException) in the body or the commit rolls
    /// back and drops the queue. Then the queue in order: events to Env.Sink, hooks with an after-commit Tx (each statement its
    /// own autocommit; a hook that tracks entities calls SaveChanges); a failing hook is logged, the rest run, and the first
    /// failure is returned. ChangeTracker.Clear(); exit the gate.
    val run: WriteGate -> Env -> CampfireDb -> (Tx -> 'T) -> Task<Result<'T, DbError>>

module Reads =
    /// The port's Database.Read boundary: DbException and SqliteException become DbError.
    val run: CampfireDb -> (CampfireDb -> 'T) -> Result<'T, DbError>
```

`DbError` keeps its cases; `Sqlite` now also comes from a `DbUpdateException` whose inner exception is a `SqliteException`
(`isRecordNotUnique` looks at that). `WriterGone` goes.

### 3.4 Model functions

Signatures keep the port's shape with `Conn` -> `CampfireDb` and `Tx` -> the new `Tx`; return types swap `Timestamp` for
`DateTime` and keep `option` where the port returns one. Bodies, for example:

```fsharp
/// `room.messages.page_before(message)` (a five-workloads query: SQL)
let pageBefore (db: CampfireDb) (roomId: int64) (message: Message) : Message list =
    let before = RailsTime.toDb message.CreatedAt
    db.Messages.FromSql($"SELECT * FROM messages WHERE room_id = {roomId} AND created_at < {before} ORDER BY created_at DESC LIMIT 40")
    |> List.ofSeq |> List.rev

/// `user.rooms.closeds` (not hot: LINQ, values from a record)
let forUserOfType (db: CampfireDb) (userId: int64) (roomType: RoomType) : Room list =
    let p = {| User = userId; Type = roomType |}
    db.Memberships.Where(fun m -> m.UserId = p.User).Join(db.Rooms, (fun m -> m.RoomId), (fun r -> r.Id), (fun _ r -> r))
        .Where(fun r -> r.Type = p.Type) |> List.ofSeq

/// `room.messages.create!` (the post path, spike S5 v3/v5)
let create (tx: Tx) (attributes: NewMessage) : Message =
    let message = { Id = 0L; RoomId = attributes.RoomId; CreatorId = attributes.CreatorId; ClientMessageId = ...; CreatedAt = DateTime.MinValue; UpdatedAt = DateTime.MinValue }
    tx.Db.Messages.Add message |> ignore
    tx.Db.SaveChanges() |> ignore                                   // id and timestamps now in `message`
    // body -> RichTextRecord.create (Add + SaveChanges), attachment -> Attachment.create, then the touches:
    let now = RailsTime.toDb (tx.Now())
    tx.Db.Database.ExecuteSql($"UPDATE messages SET updated_at = {now} WHERE id = {message.Id}") |> ignore
    tx.Db.Database.ExecuteSql($"UPDATE rooms SET updated_at = {now} WHERE id = {message.RoomId}") |> ignore
    tx.AfterCommit(fun tx -> createInIndex tx message; Room.receive tx message.RoomId message)
    { message with UpdatedAt = ... }
```

`insert_all` (granting memberships to open rooms, to a new room's users) stays one multi-row `ExecuteSql` per 1,000 rows with
SQLite's `STRFTIME('%Y-%m-%d %H:%M:%f','NOW')` timestamps, as Rails writes them (millisecond text). Fixtures stay a generic
`FixtureSet` port, now executing on `db.Database.GetDbConnection()` (static data, outside the count).

### 3.5 In `Campfire.App` (and the Kit seam)

- `Ctx.Http: HttpContext` (Adapter passes it; design-kit-auth 2.1 then reuses it) and `Front.serveWithLines` /
  `tests/Campfire.App.Tests/Support.startPipeline` gain `configureServices: IServiceCollection -> unit`.
- `Data.addServices (services) (factory) (gate) (time)`: `IDbContextFactory<CampfireDb>` (the instance), scoped `CampfireDb`
  from it, `WriteGate`, `TimeProvider`.
- `AppState` holds `DbFactory`, `Gate`, `Env` instead of `Db: Database`. `c.Db` (App extension) =
  `c.Http.RequestServices.GetRequiredService<CampfireDb>()`. `c.App.Read(c, fun db -> ...)` and `c.App.Write(c, fun tx -> ...)`
  keep returning `Task<Result<'T, Kit.Error>>` so controller code changes by one argument; `ReadOffloaded` goes (a read runs on the
  request's thread, as EF does it). Jobs, channels and integrations use `use db = app.DbFactory.CreateDbContext()` per job or
  command and `Writes.run app.Gate app.Env db`.
- `ClockTimeProvider` (E14) and the `RichText` interface's `conn` arguments become `CampfireDb`.

### 3.6 Active Record callbacks, where each lives

No `counter_cache` exists in `reference/app/models`; `connections` is an explicit counter.

| Rails | Where it runs |
|---|---|
| `Message before_create` `client_message_id ||= uuid`; `has_rich_text :body` and `has_one_attached :attachment` (both `touch` the message); `belongs_to :room, touch: true` | `Message.create` inside the transaction (inserts, then `ExecuteSql` touches of the message and the room) |
| `Message after_create_commit` `room.receive` and `create_in_index` | `tx.AfterCommit`: FTS insert, unread memberships (`ExecuteSql`), `EmitAfterCommit PushMessage` |
| body update (`after_update_commit update_in_index`); `Boost belongs_to :message, touch: true` | `Message.updateBody`/`Message.touch`: touch message and room, hook reindexes |
| `Message has_many :boosts, dependent: :destroy`; `after_destroy_commit remove_from_index`; attachment `purge_later` | `Message.destroy`: boosts, rich text, attachment rows; hook deletes the index row; `EmitAfterCommit PurgeBlob` |
| `Membership after_destroy_commit` reset remote connections; `User` deactivate/ban disconnects | `EmitAfterCommit DisconnectUser`, as the port (deactivate and ban too: after commit, the Rust known difference), never `EmitNow`, which would trip design-cable C16's guard |
| `User after_create_commit grant_membership_to_open_rooms`; `Rooms::Open after_save_commit grant_access_to_all_users` | hooks running the `insert_all` `ExecuteSql` |
| `Search after_create trim_recent_searches`; `find_or_create_by(query:).touch` | `Search.record` inside the transaction |
| `Session before_create last_active_at ||= now`; `Account before_create join_code` | the `create` functions |
| `increment!/decrement!(:connections, touch: true)`, `touch :connected_at` | `ExecuteUpdate` with record parameters (Cable presence; not a five-workloads path) |
| `created_at`/`updated_at` on save | `RailsTimestamps` (E9) |

## 4. Units, in build order

One Sonnet builder per unit, 90 minutes each, from a clean `git status` on `dotnetway`, one commit per unit. Common acceptance:
`CAMPFIRE_REQUIRE_SEED=1 bin/verify` green, `bin/kloc` in the commit message, and `bin/pixel-parity --fast --only <the unit's globs>`
equal on screenshots, aria and loose DOM (loose DOM judged against Phase 0's residue: the whitespace-beside-hidden-input cells are
not this phase's). Units 1.3-1.9 also run `bin/db-differential` (they write rows). **Migration rule:** until 1.11 the old
`Database` (reader pool, writer thread) and EF share the file; `AppState.Write` and every direct `app.Db.Write` take the
`WriteGate` around the old writer's job too, so there is exactly one writer at a time throughout the phase.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 1.0 .NET 11 | `df -h /` at least 12 GB free (15 for the bench) or stop; `global.json` `11.0.100-rc.2.26504.105` (`rollForward: latestFeature`); `net11.0` in `Directory.Build.props`; `DOTNET_VERSION` in `Dockerfile` and `Dockerfile.toolchain` (stages stay identical); other packages unchanged | `bin/verify` on net11.0; the image rebuilt (`CAMPFIRE_REVISION`), `bin/clean-docker`; **gate runner: `bench/quick` before (the 1.0 parent image) and after, same session**, recorded in the phase log |
| 1.1 Foundation | packages (E1; `Microsoft.Data.Sqlite` 10.0.13 in `Directory.Packages.props`); `RailsTime`, `Entities`, `CampfireDb`, `Interceptors`, `Data`; tests D1-D6 | D1 (every row of all 15 tables of the `default` and `crowd` seeds read and written back to a fresh prepared database, `sqlite3 .dump` of both identical) passes; nothing outside Db changes |
| 1.2 Write path and seam | `Writes`, `Reads`, `Tx`, `Env`, `WriteGate`; `ClockTimeProvider`; `Ctx.Http`; `configureServices` in Front and `Support`; `Data.addServices`; `AppState` gains `DbFactory`/`Gate`/`Env` beside the old `Db`; the old writes go through the gate | W1-W7; an App test resolves `CampfireDb` and `TimeProvider` from a request's `RequestServices`; the whole App suite unchanged |
| 1.3 Users, sessions, accounts | `User`, `Session`, `Account`, `FirstRun` (and `PasswordDigest`) on EF; auth's two lookups as `FromSql` (hot); `Concerns`, sessions, accounts, users, first-run controllers, `Presenters/Accounts` | `UserTests`, `AccountTests`, `FirstRunTests` ported; `auth/**`, `account/**`, `users/**` |
| 1.4 Rooms, memberships | `Room` (STI converter, `insert_all` grants, directs), `Membership` (counters, unread, involvement), sidebar as `SqlQuery<MembershipRoomRow>`; rooms controllers, sidebars, `Presence`, `RoomMessages` channels | `RoomTests`, `MembershipTests` ported; `rooms/**` |
| 1.5a Messages, rich text, boosts: model | `Message` (create, touch, updateBody, destroy, pages and `findReachable` as `FromSql`), `RichTextRecord`, `Boost`, `Webhook.payload`'s reads; `RichText` interface on `CampfireDb` | `MessageTests`, `CallbacksTests` ported; P1 |
| 1.5b Messages: call sites | `Controllers/Messages*`, `Boosts`, `ByBots`, `BoostsByBots`, `Presenter`, `RichText.fs` (`DbResolver`), `Broadcasts`, `CoreJobs` | `messages/**`, `rooms/**`, `interactions/**` (except the attachment states, 1.7) |
| 1.6 Search | FTS reads and index writes (E13); `Search.record`/trim; searches controller | `search/**`; P2 |
| 1.7 Storage rows | `Blob`, `Attachment`, `VariantRecord` entities; `Campfire.Storage` on `CampfireDb` (`Blob.fs`, `Storage.fs`), `Storage/Sql.fs` deleted; `ActiveStorage.fs`, `AttachmentWrites`, avatars, logos | `Campfire.Storage.Tests`; `bin/storage-media-vectors` in colima; `interactions/composer/attachment*`, `interactions/lightbox`, `users/avatar*`, `account/edit` |
| 1.8 Push, webhooks, bans | `PushSubscription` (its `now`-cutoff queries), `Webhook`, `Ban` (`banned` as `FromSql`: it runs on every non-GET); `IntegrationJobs`, `WebPushPool`, push and bot controllers | `PushTests` ported; `bot_api/**`, `users/**` |
| 1.9 Boot, schema, fixtures | `Schema.prepare` on EF (E15); boot's `disconnectAll`; `Fixtures` on the context's connection; `Testing.fs` (sinks, `BasicRichText`) moved to `tests/Shared`; `DifferentialTests` and `SeedTests` on EF | `bin/db-differential` green (fixtures, scenario, schema identity, Rails boots on our file) |
| 1.10 Hot paths and guards | the five workloads' statements listed from H2's command log in the phase log; each is `FromSql`/`SqlQuery`/`ExecuteSql`/`Find`; tests H1-H3 | H1 zero compilations in steady state; H3 posts/s recorded; `bench/quick` (gate runner) vs a same-day control of the 1.0 image |
| 1.11 Delete the topology | `Database.fs`, `Sql.fs`, `Columns.fs`, the `Timestamp` struct (`Time.fs`), `Conn`, `AppState.Db`, `ReadOffloaded`; `SqlTests`, `ColumnsTests`, `DatabaseTests`, `TimeTests` retired (their Rails-behaviour cases live in W*, D2, D3) | no `Conn` left in `src`; `Timestamp` only as RailsCompat's own alias (`= DateTimeOffset`); `bin/verify`; `bin/kloc` |

Then the **phase gates** (gate runner, plan section 3): `bin/db-differential`; `bin/pixel-parity --fast` on every state, then the
lean matrix on `default` and `crowd`; `bench/quick` against a same-day control (or a quiet host), five workloads at c=1 and c=16;
`bin/kloc`. A workload under 5x Rails at c=16 blocks the phase (plan gate 5); the first thing to check is H1.

## 5. Tests

Db-level tests open a `CampfireDb` from `Data.factory` over a copy of a seed or a freshly prepared database
(`tests/Campfire.Db.Tests/Support.fs`). App-level tests boot the whole app over a seed (`Support.bootSeeded`: the app's real
pipeline and the real `configureServices` on loopback Kestrel), which is the WebApplicationFactory level here; the factory itself
waits for Phase 7, when `Boot.serve` becomes the builder (design-kit-auth section 5).

| Id | Test |
|---|---|
| D1 | row round trip: every row of the 15 tables (seeds `default`, `crowd`) through EF into a fresh prepared database; `.dump` identical to the source's for those tables |
| D2 | `RailsTime`: `toDb` with zero and non-zero microseconds, truncation of 100 ns ticks, `ofDb` of `HH:MM:SS`, `.123` (insert_all), `.123456`, `.1234567`; `toUsec`, `toEpoch` (the port's `TimeTests` cases) |
| D3 | an `Add` of each entity with unset timestamps stores the `FakeTimeProvider`'s time in Rails' text; `SetValues` with a change stamps `updated_at`, without one writes nothing |
| D4 | pragmas: on three pooled connections opened concurrently, `PRAGMA busy_timeout`, `foreign_keys`, `synchronous`, `journal_mode` read Rails' values; the configured-handle counter equals the physical connection count |
| D5 | `RailsParameterGuard`: `FromSql` with a `DateTime` hole throws; with `RailsTime.toDb` it runs |
| D6 | enums and STI: `Rooms::Closed` and `involvement = 'everything'` round-trip; an unknown room type fails loudly |
| W1 | `Writes.run` holds `BEGIN IMMEDIATE`: while it is inside its body, a second connection's `BEGIN IMMEDIATE` gets `SQLITE_BUSY` |
| W2 | after-commit order: hooks and events run in queue order after commit, outside the transaction; a hook queued during a hook runs at once |
| W3 | an exception in the body rolls back (no row), drops the queue, clears the tracker; a `DbUpdateException` from a unique index maps to `isRecordNotUnique` |
| W4 | a failing hook: logged, later hooks run, the first error is returned |
| W5 | `WriteGate.InWrite` is true inside the body and the commit, false in the after-commit hooks and the event sink (a `DisconnectUser` emitted after commit reaches `Revocation` without tripping design-cable C16's guard), after `run` returns, and in a concurrent read |
| W6 | 16 concurrent `Writes.run` posts: all rows present, no `SQLITE_BUSY`, never two bodies inside at once (a counter) |
| W7 | an old-`Database` write and a `Writes.run` write started together both commit (the shared gate, migration rule) |
| P1 | post through `Message.create`: the message, rich text, room `updated_at`, FTS row, unread memberships and the `PushMessage` event, with timestamps equal to the frozen clock; a boost touches message and room and reindexes |
| P2 | search: `searchReachable` hits equal the port's on the seed for `coffee`, a quoted phrase, a NUL-separated query and `""` |
| H1 | steady state: with a `LogTo` counter on `CoreEventId.QueryCompilationStarting`, request each of the five workloads as two different users, rooms and pages (post: two different rooms and bodies), then again with new ids: **zero compilations in the second round** |
| H2 | each five-workloads request's command log (`CommandExecuted` texts) contains no statement EF generated from LINQ (EF's aliasing `FROM "x" AS "y"` without a raw subquery) other than `Find` |
| H3 | 16 concurrent `POST /rooms/:id/messages` through the app for 3 s: every 2xx has its rows; posts/s printed (the floor's early warning: 1,650) |

Ported, re-pointed at `CampfireDb` and `Tx`: `AccountTests`, `CallbacksTests`, `ErrorTests`, `FirstRunTests`, `FixturesTests`,
`MembershipTests`, `MessageTests`, `PushTests`, `RoomTests`, `SchemaTests`, `SeedTests`, `UnitTests`, `UserTests`,
`DifferentialTests` (the three reference-image tests keep their skips). Storage tests follow 1.7. Retired: `SqlTests`,
`ColumnsTests`, `DatabaseTests` (reader pool, writer thread, checkpointer; their transaction and after-commit cases become W1-W5),
`TimeTests` (into D2).

## 6. What is deleted

`Database.fs` (writer thread, reader pool, read queue, checkpointer), `Sql.fs` (`Conn`, statement cache, row accessors, the raw
SQLite calls of Phase 7), `Columns.fs`, `Time.fs` (`Timestamp`; `RailsTime` replaces it), `Testing.fs` (moved to tests), the SQL
strings and row readers in `Models.fs` and the other model modules, `Campfire.Storage/Sql.fs`, the `Database` wrappers in
`AppState`, `ReadOffloaded`, and `bench/db-micro` (it measures the deleted topology; spike S4's scenarios replace it if a
tier-1 database bench is wanted again). `Microsoft.Data.Sqlite` stays only as EF's provider and for `Boot`'s backup.

## 7. KLOC projection

`bin/kloc --files`, corrected column, at `e1d28aa`: `Campfire.Db` 3,307 (Models 1,163, Database 423, Sql 367, PushSubscription
218, Time 188, Account 141, Ban 124, Testing 118, Schema 84, ActiveStorage 84, Sound 77, Search 71, Session 69, Error 53,
RichTextRecord 50, Columns 45, FirstRun 19, Events 9, RichText 4); `Campfire.Storage` `Sql.fs` 60, `Blob.fs` 185, `Storage.fs` 241.

| | KLOC |
|---|---:|
| Deleted: Database, Sql, Columns (0.84), Time to RailsTime (-0.15), Testing moved (-0.12), Schema and Error trims (-0.04) | -1.15 |
| Model modules: SQL text and row readers replaced by queries of about the same length; Models 1.16 -> ~0.87, the small modules -0.15 | -0.44 |
| New: `CampfireDb` + converters 0.12, interceptors 0.07, `Writes`/`Reads`/`Tx` 0.09, `Data` + DI 0.04, `ClockTimeProvider` 0.02 | +0.34 |
| Storage: `Sql.fs` gone, `Blob.fs` -0.06, `Storage.fs` -0.01 | -0.13 |
| App: `AppState` wrappers, `ReadOffloaded` (-0.03); call sites (one argument each, `Timestamp` -> `DateTime`) ~0 | -0.03 |
| **Phase 1 net** | **-1.4** |

`Campfire.Db` **3.3 -> about 2.0 KLOC**, project **39.4 -> about 38.0** against the plan's 37.4. The plan assumed the SQL would
shrink with the plumbing; it doesn't much: a one-line SQL literal becomes a one- or two-line `FromSql` or LINQ chain, and Q1
keeps hot-path SQL as text. The end-of-project projection moves by the same +0.6 (with design-kit-auth's +1.0: about 18.6 against
Rust's 31.0, 60%).

## 8. Speed: the five workloads against the 5x-Rails floor

Projection: the port's CPU per request at c=16 (`bench/results/phase7-final.md`, 4 server CPUs) plus EF's extra per request from
spike S4 (EF with `FromSql` minus the port's same statements, the port's ~27 us sort removed from db-micro's numbers, and ~17 us
for each statement of the page the spike did not cover), at 3.8 busy cores. Mac-measured costs applied to the Linux container:
indicative; the gate's `bench/quick` decides.

| Workload | statements (port trace) | port CPU us/req | EF extra us | projected us/req | projected req/s | Rails req/s | x Rails | floor |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| room page | 8 | 100 | 39 + 107 + 34 = 180 | 280 | ~13,600 | 239 | ~57x | over |
| messages page | 8 | 102 | 39 + 47 + 85 = 171 | 273 | ~13,900 | 381 | ~37x | over |
| sidebar | 9 | 102 | 39 + 150 = 189 | 291 | ~13,100 | 599 | ~22x | over |
| search | 7 | 107 | 39 + 52 + 51 = 142 | 249 | ~15,300 | 408 | ~37x | over |
| post a message | ~10 | 353 | ~190 CPU; gate held ~190-210 us | ~545 CPU, gate-bound | ~3,900 (S5 v3/v5, database only) | 330 | ~12x | over |

The post is bounded by the gate, not the CPU: 1 / 0.21 ms is ~4,700 posts/s, and S5 measured 3,878-4,520 with 16 writers. If the
hot paths used parameterized LINQ instead of SQL, the pages would land near 7,000-8,000 req/s (still 13-33x). **If a captured
local reaches a LINQ query on a hot path, a page pays ~1 ms per such statement (room page ~2x Rails) and posting drops to 435-1,598
posts/s, under the floor.** That is why E12 and H1 exist.

Under Q1's other options (not taken): option 2 (a ~60-line rewriter turning F#'s `QuotationToLambdaExpression(SubstHelper ...)`
calls back into quoted lambdas, then `EF.CompileQuery` values in modules) would make nested-lambda LINQ work, drop `FromSql` from
the hot paths and cut the per-statement cost below `FromSql`'s (unmeasured: it is unbuilt); it adds a module of our own over EF's
expression contract. Option 3 (a C# project holding `EF.CompileQuery` statics or EF's precompiled queries) needs the entities and
`CampfireDb` in C# as well (the C# project cannot reference an F# project that references it back), which breaks D1. Nothing in
section 8 calls for either.

## 9. Known differences this phase records (README "Known differences", plan section 4)

- **Class A**: none visible. Rows are byte-identical to Active Record's (D1, `bin/db-differential`).
- **The WAL checkpoint returns to SQLite's auto-checkpoint** on the committing connection (Rails' behaviour); the port's
  background checkpointer and its README note go. A commit that crosses 1,000 WAL pages pays the checkpoint, as in Rails.
- The port's "SQLite index" known difference (`index_messages_on_room_id_and_created_at`, additive) stays.

## 10. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | A captured local in an F# LINQ query (E12a): a compilation per call, ~1 ms, and the query cache filling | S2b's counter as H1; a builder adding any LINQ query runs H1 locally before committing. In code review: any lambda that mentions a name not bound to the lambda's parameter or a record field |
| 2 | Nested lambdas or tuple projections (E12b, c) | they throw on first execution: every new query needs a test that executes it (the ported model tests do) |
| 3 | `DateTime` in raw SQL (E5): 7-digit text that Rails never writes | `RailsParameterGuard` throws in every test; D5 |
| 4 | Post throughput under the gate (spike: ~2x the port's gate time) | W6 and H3 print posts/s; under ~2,500 locally means an `ExecuteUpdate` with values or an extra `SaveChanges` crept into `Message.create` |
| 5 | Two writers during the migration (old writer thread and EF) | W7; any `SQLITE_BUSY` or a 5 s stall in a test log during 1.2-1.10 |
| 6 | Rows Rails reads differently (timestamps, nulls, enums, STI, JSON text) | D1 in 1.1, before any model moves; `bin/db-differential` from 1.3 on |
| 7 | Pragmas missing on some pooled connections (e.g. `foreign_keys` off: Rails' cascades checks skipped) | D4 |
| 8 | Change-tracker leftovers between writes (`Attach` of an id already tracked throws) | W3 and `ChangeTracker.Clear` in `Writes.run`'s `finally`; tests that run two writes on one context |
| 9 | F# records mutated by EF (the id after `SaveChanges`); code that kept the record's id as 0 | P1 asserts the returned ids; rule: read `entity.Id` after `SaveChanges`, never before |
| 10 | Nullness errors (FS3261) on `string \| null` columns inside LINQ lambdas | the compiler; write those queries as SQL (sidebar) or compare without dereferencing |
| 11 | EF 11 / NamingConventions mismatch when someone bumps EF | NU1608 at restore (an error here); E1 says when to bump |
| 12 | Disk: the .NET 11 SDK (~1 GB), images and the bench on a Mac that had 6.6-8 GB free while this note was written | unit 1.0 starts with `df -h /`: under 12 GB (15 for the bench) it stops and reports |
| 13 | The harness's frozen clock not reaching EF's stamps | D3 under `FakeTimeProvider`; `bin/db-differential`'s scenario compares timestamps with Ruby's under a frozen clock |
| 14 | A heavy read (FTS on a big database) holding a thread-pool thread, now that `ReadOffloaded` is gone | `bench/quick` search c=16 against the control; the port's offload existed for a 236k-message database, not the seed |

## 11. Questions for Chris

None. Q1 is answered (option 1) and .NET 11 is decided; E1 (EF 10 until EF 11 GA plus NamingConventions) and E6 (the synchronous
API, Microsoft's guidance for this provider) are settled by D9 with the evidence above.
