# Spike: Dapper for the reads, EF Core for everything else

Branch `spike-dapper` (worktree off `063b940`, the end of Phase 1). 2026-10-19. Question: is doing the READS with Dapper (EF keeps the
model, writes, transactions, interceptors, schema) a real gain over Phase 1's EF reads (LINQ plus `FromSql` via `Data.sqlFirst`)?

Answer in one line: yes on the read set itself (2.6-2.8x faster, 3.5x fewer bytes), and the room and messages pages are read-bound, so the
estimated end-to-end gain is about 1.5-1.8x on those pages. Nothing was run through `bench/quick`, docker or colima (told not to), so the
end-to-end figure is derived, not measured on the gate's rig.

## What changed (all behind `CAMPFIRE_READS=dapper`, one build holds both)

- `Directory.Packages.props`: `Dapper 2.1.89` (latest stable on nuget.org today).
- `src/Campfire.Db/Ef/FastReads.fs` (new): 11 Dapper reads on the context's own connection, returning the same entity records. Opened and
  closed through `db.Database.OpenConnection()/CloseConnection()` so EF's `SqlitePragmas` interceptor still runs on a new physical
  connection. A `RailsDateTimeHandler` (the same `RailsTime.ofDb`/`toDb` as the EF converter) and `DefaultTypeMap.MatchNamesWithUnderscores`.
- One-line dispatch (`if FastReads.enabled then ... else <the EF code, unchanged>`) at each call site: `Session.findByToken`, `User.findById`,
  `Membership.findByRoomAndUser`, `Room.findById`, `Room.findForUser`, `Room.original`, `Message.findById`, `Message.lastPage`,
  `Message.pageBefore`, `Account.first`, `Storage.Blob.attached`. Callers (controllers, presenters) are untouched.
- `tests/Campfire.App.Tests/DapperSpikeTests.fs`: the equivalence test (always runs) and the three measurements (`CAMPFIRE_SPIKE=1`,
  output to `CAMPFIRE_SPIKE_OUT`).
- Not done: the sidebar, search and post paths (their reads are `SqlQuery<Row>`/`json_each`/`NOT IN` shapes; the sidebar's three
  reads are where the next 100+ us are, but I ran out of the two hours). The sidebar was therefore not measured; the numbers below are the room and messages pages only.

## The read sets (from `bench/results/dotnetway-p1-10/command-log.md`, warm fragment cache)

Room page, 9 statements: session by token, user, room via membership join, original room, last page of messages (40), account, room via
membership join again, account again, account logo blob (attachment join). The message creators, boosts, rich text and attachments are
NOT read on a warm request: the message fragments come from the fragment cache (that is why the log has none of them).
Messages page, 9 statements: session, user, membership, room, the pivot message, the page before it, room join, account, logo blob.
Both are all read through Dapper in the switched build (confirmed: under the switch the H2 test sees "room page ran no statements" in EF's command log).

## (a) The read sets alone, in process

Single thread, warm (200 iterations), 20,000 iterations per cell, default seed, one pooled context leased per page iteration as a request would.
EF and Dapper run back to back, 6 rounds (round 1 dropped as warm-up, 5 kept). Host load average during it 4.5-7.6 (another agent building); the spread is tight anyway.

| read set | EF us/page (min-max) | Dapper us/page (min-max) | speed-up | EF B/page | Dapper B/page |
|---|---:|---:|---:|---:|---:|
| room page (9 stmts) | 377 (370-396) | 140 (138-143) | 2.7x | 462,173 | 130,464 |
| messages page (9 stmts) | 356 (350-370) | 129 (127-130) | 2.8x | 490,743 | 133,248 |

Per statement: EF about 41 us and 51 KB, Dapper about 15 us and 14 KB. Dapper's remaining cost is Microsoft.Data.Sqlite's command and reader (and the context lease), not Dapper.

## (b) The whole page through the app's pipeline, in process

`Support.bootSeeded "default"`, a signed-in `Browser`, sequential GETs over loopback TCP, 300 warm-up + 2,000 timed requests per cell, 6 rounds,
EF/Dapper order alternated by round (A/B, B/A, ...) so host noise and position cancel; round 1 dropped (tiering). Load average in the raw file
per cell (4.0-6.3 throughout, 1-min). IMPORTANT caveat: the test harness is in the same process and reads a 380-414 KB response per request into a
MemoryStream, so absolute times (1-2 ms) and 3.5 MB/request allocation are mostly the harness, not the server. Only the EF-vs-Dapper DIFFERENCE is meaningful.

| page | metric | EF mean (range) | Dapper mean (range) | EF / Dapper |
|---|---|---:|---:|---:|
| room | wall mean per request | 2,145 us (1,921-2,503) | 1,530 us (1,347-1,810) | 1.40x |
| room | wall p50 | 1,442 us (1,210-1,833) | 967 us (786-1,239) | 1.49x |
| room | process CPU per request | 3,389 us | 2,626 us | 1.29x |
| room | bytes allocated per request | 3.91 MB | 3.57 MB | delta 340 KB |
| messages | wall mean | 1,923 us (1,724-2,570) | 1,554 us (1,303-1,936) | 1.24x |
| messages | wall p50 | 1,153 us (921-1,870) | 823 us (645-1,351) | 1.40x |
| messages | process CPU per request | 2,923 us | 2,412 us | 1.21x |
| messages | bytes allocated per request | 3.63 MB | 3.28 MB | delta 350 KB |

Absolute saving per request: room p50 475 us, CPU 763 us; messages p50 330 us, CPU 511 us. The allocation delta (340-350 KB) matches (a) exactly.
The time saving is about 2x what (a) predicts (233 us): a plausible cause is cache and GC pressure (EF's 330 KB extra churn per request on top of the harness's 3.5 MB), which I did not isolate. A first run with EF always first in each pair (`raw-b-first-run-ef-first.txt`) gave the same picture, so it is not a position effect.

c=16 (16 signed-in browsers, 3 s per cell, room page, 6 interleaved rounds, load 4-6): EF 1,324 req/s mean, Dapper 1,748 req/s mean; per-round ratios 1.46, 1.27, 1.18, 1.23, 1.44, 1.31 (mean 1.32x, range 1.18-1.46). This is a floor on the gain: the in-process client at c=16 shares the CPUs with the server and reads 414 KB per response. `bench/quick` was not run.

## (c) Identical pages and tests

- Pages: GET room page (414,046 chars) and messages page (381,684 chars) four times each, EF, Dapper, EF, Dapper, csrf-token meta and authenticity_token values masked: all four bodies equal for both pages. (The message fragments come from the fragment cache after the first request, so a data difference there would be hidden by the cache. That is why the next check exists.)
- Entities: `dapper reads return the same records as EF for every row of the default seed` compares, as F# structural equality, every user, room, membership, session, message (131+ in All Talk), `lastPage` and `pageBefore` of every message of every room, the original room, the account, and every attachment's blob, against plain EF LINQ over the same rows. Passes (it covers `Nullable<DateTime>` set and null, the Involvement and RoomType strings, 7-digit-free microsecond times).
- Existing suites with `CAMPFIRE_REQUIRE_SEED=1` (`raw-tests.log`; Release): Db.Tests 161 total, 0 failed, 3 skipped, identical with `CAMPFIRE_READS=dapper`. App.Tests 207 total: 6 failed with EF reads and the same 6 failed with Dapper reads (MessagesTests uploads/bot api/plain-text-raises, AttachmentTests logo and avatar, AppTests writes-while-variant); the ones I opened fail on `libvips.42.dylib` not being installed on this Mac, so they are environmental, not Dapper. Storage.Tests: 3 failed, libvips too. With Dapper reads one more App test fails: HotPathTests H2, by design (see pitfalls). H1 (zero query compilations) and H3 (16 concurrent posts, 0 not 2xx) pass under both.

## Code size

- Dapper read code: `FastReads.fs` 140 lines, 108 non-blank non-comment, for 11 reads: 16 are the prelude (handler, switch, connection helper), 21 the two private row records (see friction), 71 the reads themselves (3-8 lines each).
- EF code it replaces: about 20 lines (14 one-liners `FromSql ... |> sqlFirst`, a 4-line join `FromSql`, a 4-line `pageBefore` LINQ, a 5-line `Blob.attached` LINQ). So roughly 5x more lines in a real swap, not fewer. Per-statement the Dapper SQL is the same text the EF code already has (the EF hot paths were already hand-written SQL strings), so nothing new is learned about SQL, but each result type now needs mapping for string-backed enums.
- Call-site dispatch: 15 lines changed. In a real hybrid they would be replaced, not branched.

## F# idiom friction

1. Dapper ignores a `TypeHandler` for an enum type (it parses the column with `Enum.Parse`): `Rooms::Open` and the involvement names blew up with `ArgumentException: Requested value 'Rooms::Open' was not found`. Fix: read into a private row record with `Type: string` / `Involvement: string | null` and map by hand (`Names.roomTypeOfDb`). That is where the 21 extra lines come from, and it is a mapping layer EF's `ValueConverter` gave for free. Role and Status (integer enums) work.
2. `DateTime` needs a global `SqlMapper.AddTypeHandler` (process-wide state, set lazily) because the column is text; without it Dapper calls `GetDateTime` and parses with the wrong kind and rules. `Nullable<DateTime>` is covered by the same handler.
3. Snake case needs `DefaultTypeMap.MatchNamesWithUnderscores <- true`, also global.
4. Naming the row records `RoomRow`/`MembershipRow` in the namespace made F# record inference pick them for `{ Id = ...; Name = ...; Type = ... }` literals in Db.Tests (broke the build); they are private nested types now.
5. F# anonymous records as parameters work (`{| id = id |}`), `[<CLIMutable>]` records materialise fine (Dapper uses the default ctor), and `string | null` fields are fine. `QueryFirstOrDefault` returns null for a record type; `Option.ofObj` needs the nullness annotation noise the codebase already has.
6. Nothing to do with F# lambdas: Dapper takes a SQL string and a parameter object, so the 60 us expression-tree rebuild and the captured-local compile (1 ms) problems are gone by construction.

## Pitfalls beyond idiom

- Dapper statements bypass EF's observability and interceptors: `LogTo` and the command log (H2 test: fails by design, "room page ran no statements"), `RailsParameterGuard` (a `DateTime` parameter could reach SQL; the reads here pass strings and ids only, and the handler writes `RailsTime.toDb`), and H1's compile counter is vacuous for these reads. A hybrid needs its own guard and a log hook (`SqliteConnection` events or a Dapper-side wrapper) before it is called equivalent.
- The Dapper read runs on the context's connection, so inside a `Writes.run` it sees the open transaction's uncommitted rows, which is what EF would do. I did not pass `CurrentTransaction` to Dapper; Microsoft.Data.Sqlite did not require it (the post tests passed), but a hybrid should pass it explicitly.
- Both stacks share the pooled `SqliteConnection`, so no second pool; the SQLite per-connection pragmas still run once per physical connection.
- A forgotten column in a hand-written `SELECT`/row record silently yields a default (0/null) in Dapper instead of EF's model error. The equivalence test above is the guard; a real hybrid wants it in `bin/verify`.
- Schema drift: EF's model remains the schema owner for writes; Dapper's SQL strings are a second place that names columns.

## Estimate and recommendation

From the gate (`plans/dotnetway.md`, Phase 1 final): room page 532 us CPU/request at c=16 (6,013 req/s), messages page 665 us. Taking (a)'s saving of 237 us (room) and 227 us (messages) off those: room about 295 us (1.8x, about 10,900 req/s), messages about 438 us (1.5x, about 7,900 req/s). Different rig (Linux arm64 docker) from this macOS in-process run, so treat 1.5-1.8x as the likely range; my in-process pipeline run saw 1.2-1.5x because the harness is most of the cost there (c=16 harness-bound floor 1.32x). Still about 0.25-0.3x of the port's 35,000 req/s on those pages: Dapper closes about a quarter of the gap; it does not make the page the port's speed.

Recommendation: hybrid is worth it for read-dominated, per-request lookup workloads (room page, messages page, the sidebar and search once done), where data access is about 70% of the request's CPU in EF and about 45% with Dapper (140 of about 300 us). It is not worth it for the write path (post a message: 1,672 us CPU, mostly gate, transaction and callbacks; reads there are 6 of 21 statements) nor anywhere a LINQ query is not hot. Keep EF for writes, transactions, interceptors and the model, and put the reads in one module like `FastReads.fs` behind the existing function boundaries so callers do not change. Cost to accept: about 5x the lines on the reads moved, a second mapping layer for string-backed enums and times, and the lost interceptor/log coverage (add a guard and a log hook, a `bin/verify` equivalence test like the one here). A cheaper first step is to measure whether replacing only the four hottest tiny lookups (session, user, account, room-join) captures most of it: they are 5 of the 9 statements.

Not done: sidebar/search/post reads, `bench/quick`/docker numbers, BenchmarkDotNet (Stopwatch loops instead), server-only timing (the harness is in process), EF 11.
