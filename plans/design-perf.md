# Design note: performance on upstream's harness (Phase 10)

Status: design, 2026-10-28, before Phase 10 builds. Author: the Opus designer (plan section 5). Binding for the Phase 10 builders
together with `plans/dotnetway.md` sections 3-5, D1-D9 and "Phase 10: performance on upstream's harness (Chris, 2026-10-28)" in
`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`. Evidence for every number below:
`bench/results/phase10-design/` (the harness runs, the perf and trace profiles, the scripts, and `spike3.diff`, the scratch code
the spikes measured).

Where this note and the brief disagree, this note wins. It is later and rests on the spikes in section 2. The disagreements:
- **The response cache is not the OutputCache middleware. It is a cache in the four actions, on HybridCache.** OutputCache never
  stores a response that carries `Set-Cookie`, whatever the policy says (S6). Rails' room page sets `last_room` on every visit to
  another room, and on every request in the harness, because its load generator never sends the cookie back. So OutputCache could
  never keep the room page there. A policy also cannot turn a found entry away (S6), and upstream's own cache runs inside the action,
  after `set_room` (`CachedResponses#cache_read_response`). HybridCache is the framework's application-level cache, and it already
  holds the fragments and the compressed pages. OutputCache keeps the public families (F9). (P6)
- **The plateau is not a lock, the thread pool, the logger or GC.** It is CPU per request: 440-550 us on every page against the
  port's 110. The room page is half the others for a different reason: tiered compilation is still warming up when the harness
  measures it (S2, S3). (P1, P2)

Scope: the five normal-profile workloads and the four mixed reads on upstream's harness, and the per-request paths they run. That
means `Web/Data.fs`, `Web/Pages.fs`, the four GET actions (`RoomsController.show`, `MessagesController.index`,
`Users.Sidebars.show`, `SearchesController.index`), `Campfire.Db.WriteGate` and `Ef/Writes.fs`, the HybridCache registration
(`Campfire.Views.Fragments.add`), `Config.fs` and the `Dockerfile`'s runtime settings, plus the tooling (`bench/upstream-quick`).
Out of scope: Cable, the post path's logic, the views' markup, the landing page. The six-ways rerun comes after this phase.

**Preconditions.** Phase 9 is closed: the gate is at `f9f4637` and the six-ways bench at `838f700`. The six-ways bench found the floor
failing against upstream's Rails (room 0.68x, messages 1.51x, sidebar 1.47x, search 1.42x, post 5.68x). Its conditions still bind:
- Keep H1, the markup lint and its self-test, and the Dapper, EF and DateTime guards.
- Keep Phase 6's asset conditions, `Boot.build` as the one boot, the cookie round trip, db-differential, the realtime states, the
  harness's `--validate`, and `bench/quick`'s 5x floor.
- Any change to `Web/Host.fs`, `Acme.fs` or `OutputCaching.fs` re-runs HS2/HS4 under `bin/linux-tests App`, AC11 and
  `pebble-image.sh`. This design touches none of the three.

## 1. Decisions

Each one is settled by D9: framework configuration and the framework's own fast paths first, then narrowing what runs per request,
and hand-written code only where parity or the floor forces it. Each gives its reason.

| # | Decision | Reason |
|---|---|---|
| P1 | **No change to the thread pool, GC, Kestrel or sockets.** `DOTNET_HillClimbing_Disable=1` and `DOTNET_GCgen0size=0x4000000` stay. No `ForceMinWorkerThreads`, no `DOTNET_PROCESSOR_COUNT` (the runtime already counts the cpuset's 4: `dotnet.process.cpu.count` 4), Server GC with its 4 heaps. | S1 and S4. The ceiling is CPU per request. Unprofiled cgroup CPU: room 439 us at 7,720 req/s on 3.39 cores (harness seed); port 109. Turning knobs only lost: hill climbing on -7-10%, 16 minimum workers -15-20%, the request log off ±0 on a miss. Lock contention (3,763/s, the console logger's queue) and GC pauses (11% of wall time) are real, but they are symptoms of about 500 KB allocated and 9 statements per request. |
| P2 | **`DOTNET_TC_CallCountingDelayMs=0` in the `Dockerfile`**, beside the two runtime settings already there, with its measurement comment. Not `TieredPGO=0` and not `TieredCompilation=0`. | S3. The harness warms each route 2 s at c=4 on a fresh container, and the room page is always its first route. With the default 100 ms call-counting delay, a stream of new methods keeps hot code in R2R/tier-0. Measured: room 3,402 -> **7,471** req/s, messages 7,493 -> 7,342, sidebar 7,788 -> 7,674 (inside the noise). `TieredPGO=0` also fixed the room page (6,350) but cost 12-13% on the others. `TieredCompilation=0` with R2R off cost 15%. 10.2 confirms with `bench/quick` (30-90 s warm-up) that the steady state does not lose. |
| P3 | **One SQLite connection per request.** `Web.Data`'s readers and writers open the request's `CampfireDb` connection once (`db.Database.OpenConnection()`, EF's own API: EF then neither opens nor closes it per command). The request scope closes it when it disposes the context, which goes back to the pool. Opened lazily on first use by `Web.Data`, **not** when the scope creates the context: a WebSocket request's scope lives as long as the socket. Cable channels and jobs keep their per-command leases. | S1, S5. Each of the page's 9 statements opened and closed a pooled connection. Every `Open` re-registers EF's SQL functions and collation (`sqlite3_create_function_v2`, `CompareBuf.Equals`: about 8% of busy time) and takes Microsoft.Data.Sqlite's pool lock. Every close finalizes the statements (`Deactivate`). Measured with P2: messages 7,466 -> **9,910**, sidebar 7,728 -> **10,643**, search 7,238 -> **8,917**, room -> **10,244**, post 2,002 -> **2,454** (u0 -> u1). This also closes Phase 9's open item "the `AddCustomAttributes` cost in the data layer's connection handling", in part (P4). |
| P4 | **Narrowing on a miss (unit 10.3), measured, each step kept only if it pays.** (a) Find, from trace stacks, what calls `CustomAttribute.CreateCustomAttributeInstance`, `GetCustomAttributeRecords` and `AddCustomAttributes` on the read path: about 8% of CPU after P3 (~25-30 us a request). Remove it by framework means: cache the value, or use the API that does not reflect. (b) Memoize per request the account row (`Account.first`, read twice on room, messages and sidebar) and `last_room_visited`'s room (the room through the membership, read twice on the room page and the search page): 9 -> 7 statements on the room page. **Prepared-statement reuse is not taken**: it would replace Dapper's calls with a hand-written per-connection command cache. | S5 after P3: the data layer is still 230 us a page (Campfire.Db 124, SQLite engine 74, Microsoft.Data.Sqlite 31). D9: hybrid reads are Dapper (Chris, 2026-10-19). The cache (P5-P13) reaches the target without a statement cache. The miss path matters for the first view after a write and for `bench/quick`, which is already 12-30x its own Rails. |
| P5 | **The response cache is needed.** A alone (P1-P4) reaches about 2x Rails on the reads. | S7 (u1): room 2.08x, messages 2.04x, sidebar 2.03x, search 1.74x, post 6.39x against the same session's Rails. 5x Rails on 4 cores means at most ~160 us of CPU a request, and a miss costs ~350 us after P3. A hit costs 73-89 us (S5). Upstream Rails, Rust and onCrack all run with response caches in this harness. |
| P6 | **The cache lives in the actions, on HybridCache** (`Web/PageCache.fs`), at the point where upstream's `around_action :cache_read_response` sits: after the before-actions and `set_room`. It is not the OutputCache middleware. OutputCache keeps the public families (F9, unchanged). | S6: OutputCache stores no response with `Set-Cookie`, whatever the policy. A cookie added on a miss (`OnStarting`, `ServeResponseAsync`) is either not stored or not sent. `ServeFromCacheAsync` cannot turn a found entry away (`IsCacheEntryFresh` is internal, and `AllowCacheLookup = false` there still served it). So the room page could never be kept in the harness, and S7's spike2, which stored only responses without `Set-Cookie`, confirmed it: room 10,293 (no hits) while sidebar and search reached 41k. D9: HybridCache is the framework's application-level cache (stampede protection, tags), and it is already this app's page and fragment store (V5, V7). |
| P7 | **Fresh authorization by construction.** A hit runs everything a miss runs before the lookup: `Before.chain` (`restore_authentication`: the session by token and its user, every request; `deny_bots`; `allow_browser`), then `set_room` for the room and messages pages (the membership and the room, every request), then `remember_last_room_visited`. Only what follows is skipped: the page's queries, the render and the compression. | Upstream's contract and onCrack's `CAMPFIRE_FRESH_AUTH` semantics: a revoked session, a removed membership or a deactivated user takes effect on the next request. Upstream's own `RecordCache` serves the session from its epoch, so this is stricter than upstream. The hit cost includes it: 73-89 us measured (S5). |
| P8 | **The key** is the SHA-256 (hex) of these fields, joined with U+0001: a format tag `pc1`; the epoch (P11); `controller#action` (`Pages.endpoint`); scheme; host; the raw path and query; `Accept`; `Turbo-Frame`; the chosen content encoding; `User-Agent`; the user id; the session id; the raw `last_room` cookie. Not in the key: `Referer`, other cookies and the flash. A request with a flash is not looked up (P9). Test PC16 pins that `Referer` does not change the four pages. If it fails, `Referer` goes into the key. | Everything the four pages read, from `Pages.load`/`Pages.render`'s `ViewContext`: `CurrentUser` (user id), `Account` (DB, so the epoch), `Platform` (`User-Agent`, as in upstream's key), `RequestUrl`/`BaseUrl` (scheme, host, path), `LastRoomVisitedId` (the cookie plus the DB), `Referrer` (PC16), and the format (`Accept`, `Turbo-Frame`). The session id stands for upstream's token: a session's token never changes. Hashing keeps keys under HybridCache's 1,024-character key limit and keeps the user agent and ids out of memory dumps. |
| P9 | **Eligibility (lookup and store):** `GET` only (HEAD and every other method render as now, as `request.get?` does upstream); signed in by the session cookie (`AuthenticatedBy = BySession`, never a bot key); the flash empty; no `If-None-Match` and no `If-Modified-Since` (the messages page's 304 stays the action's); the compression provider picks gzip or Brotli (an `identity` request renders); the budget is above 0. | Upstream's `cacheable_read_request?`, item for item. Identity requests are the one narrowing: a page that is not compressed has no kept bytes to store (`Pages.send` writes parts). No browser and no harness asks for identity. |
| P10 | **The store.** `Pages.send` writes as now. Then, if the request carries a lookup ticket, it stores the entry, but only when all of these hold: status 200; the body went out compressed from the kept page (`CompressedPage`); `Response.Headers.SetCookie.Count` equals its value at the lookup, so the render added no cookie (a session or flash cookie made by the render is never stored); `WriteGate.Epoch` equals the ticket's epoch, so no commit happened during the render; the body is at most 1 MB. The entry is the encoding, the bytes, the content type and the headers Rails' `CACHE_HEADERS` names, plus the preload `Link`: `ETag`, `Last-Modified`, `Cache-Control`, `Vary`, `Link`. **Never `Set-Cookie`.** A hit writes status 200, those headers, `Content-Encoding`, `Content-Length` and the bytes. Lookup is `HybridCache.GetOrCreateAsync` with `HybridCacheEntryFlags.DisableUnderlyingData` (a read that never calls a factory). The store is `SetAsync` with tag `pages` and `LocalCacheExpiration` 60 s. | Upstream's `cache_completed_response` checks that the session did not change and that the version still holds; this does the same. The cookies the before-actions set (`last_room`, an hourly session resume) are set again on every request by the before-actions themselves, before the lookup, so hit and miss carry the same cookies (S7's spike3 passed the harness's `--validate` on all four pages). The whitelist keeps the messages page's `fresh_when` headers on a hit. The 60 s lifetime bounds memory for epochs no request asks for any more, and bounds a foreign write (P11). |
| P11 | **The epoch is a process-wide commit counter**: `WriteGate.Epoch`. It is bumped right after `COMMIT` in `Writes.transact`, before the after-commit queue emits any event (the broadcasts), and again in `Writes.run`'s `finally`, before `gate.Exit()`, which covers the after-commit hooks' own autocommit statements. Every key carries it, so one commit retires every page. There is no `PRAGMA data_version` observer. | The single writer (`SingleWriterGuard`, `WriteGate`) means every write this process makes passes through `Writes.run`, and theDotNetWay is one process. Upstream needs `data_version` because Puma workers and Resque write from other processes. Bumping before the broadcast means a client that reloads on a Cable frame never gets the page from before it. A write from outside the app (`sqlite3` on a live database: unsupported, but possible) shows within 60 s or at the app's next commit, recorded as a class B difference (section 8). A global epoch is upstream's own choice: "invalidated by writes and by Cable-visible changes" holds because every Cable-visible change (messages, boosts, memberships and involvement, profiles, the account, presence) is a commit. |
| P12 | **The budget**: `CAMPFIRE_RESPONSE_CACHE_MB`, upstream's name. Default 64. `0` turns the page cache off. It is added to the shared memory cache's `SizeLimit` (fragments' `CAMPFIRE_FRAGMENT_CACHE_MB`, default 32, plus pages: 96 MB by default). | One `HybridCache` registration and one memory cache (J14). Upstream gives its response caches one budget, 64 MB by default. The harness passes nothing, so the default applies. |
| P13 | **No render locks.** Concurrent misses on one key each render. The last store wins (the bytes are the same). | S7's mixed run (10 posts/s, so the epoch moves at least 10 times a second): room 33,320, messages 33,065, sidebar 40,556, search 40,241, against Rails' 2,491-4,449. Upstream stripes 16 render mutexes. HybridCache's own stampede protection needs a factory, and a factory cannot hand back a response the action has already written. Revisit only if a mixed number says so. |
| P14 | **The request log line stays** (`UseHttpLogging`, F8, J12). | On a hit it costs 15-17% (S7: room 35,104 -> 41,173 with `LOG_REQUESTS=false`). It is Thruster's line, and Rails and Rust log every request in this harness too. The target holds with it. |
| P15 | **`bench/upstream-quick`** is the measuring tool of this phase (unit 10.1, spec in section 3). Every unit measures with it against current Rails in the same session. **The floor from this phase on**: at least 5x that session's Rails on every normal-profile route, with `bench/quick`'s 5x (stored Rails) kept as well. | The brief. The two Rails differ about 10x (bench/quick's pinned Rails at 240-600 req/s on a page, upstream's harness Rails at 4,800-5,200), so the floor is stated against the harness's from now on. |
| P16 | **The harness adapter's metadata follows the app.** When the cache lands (10.6), the local harness commit on `oncrack-adapter` changes the `dotnetway` row: response cache "pages (HybridCache, fresh authorization)", and `dotnetway` leaves the `%w[c cpp fsharp dotnetway]` list so `response_cache_mb` is recorded. Local only, never pushed. | The topology row is part of the published table's provenance. It must not say "none for pages" once there is a page cache. |

What stays hand-written, and why:
- the key and the eligibility rules (P8, P9): upstream's contract, about 40 lines;
- the store check (P10): Rails' `cache_completed_response`;
- the epoch's two bumps (P11);
- `bench/upstream-quick` (tooling, not counted).

## 2. Spikes (scratch under the session scratchpad and the colima VM's `/var/tmp`, 2026-10-28, deleted after)

Method: upstream's harness exactly as the six-ways bench ran it. `once-campfire-verification` branch `oncrack-adapter` (`f3103c7`),
`bin/benchmark --duration 8 --concurrencies 16 --cpus 0-3 --client-cpus 4-7`, 2 rounds per run, routes as named. Seed built by
`bin/seed --image campfire-reference:app` (sha `4b584669`; seeds are not byte-reproducible, and the six-ways seed was `b775e125`).
Rails `ca9b963` (`WEB_CONCURRENCY=4`, `RAILS_MAX_THREADS=1`), the port `9954ad0`, theDotNetWay `campfire-dotnetway:app` (`dcc9434` =
`f9f4637` in `src`). Env variants passed through `DOTNETWAY_BENCH_ENV`. Code variants were scratch builds published like the
`Dockerfile` (toolchain image, R2R, linux-arm64) and layered over `campfire-dotnetway:app` as `:spike1`-`:spike3`, switched by
environment variables; `spike3.diff` is their code. Profiles: `bench/breakdown perf` (cgroup CPU per request unprofiled, then
`perf record`) and `bench/breakdown profile` (dotnet-trace sampled thread time plus `dotnet-counters`), with the seed swapped to the
harness's where named. Host probe 1.03-1.08 on every run used here. The two discarded runs (probe 2.1, and builds running beside
them) are listed in the results README.

| # | Question | Result |
|---|---|---|
| S1 | Where a page's CPU goes now (perf, room page, c=16, 15 s warm-up, harness seed) | **438.6 us/request** at 7,720 req/s on 3.39 cores. Data layer 307 us: `Campfire.Db` 115.6, Microsoft.Data.Sqlite and P/Invoke 106.1, SQLite engine 85.1. Kestrel 32.2, templates 30.3, cookies/HMAC 22.8, GC reclamation 12.8, log line 9.1. The port: 109.1 us room, 112.2 sidebar, of which SQLite about 31. On the parity seed: 551.5 us at 6,146 req/s. |
| S2 | Locks, pool, GC, logger, under load (dotnet-trace and counters, parity seed, room c=16) | Thread pool 4 threads, queue 6 (max 9). **Allocation 2.81 GB/s at 5,613 req/s, about 500 KB a request.** gen0 44/s, pause 11.3% of wall time. Lock contentions 3,763/s, owned by the console logger's queue (`Monitor` + `Condition.SignalAll`). JIT still compiling 40 methods/s after 15 s. 9 statements a page (H2's log: room 9, messages 9, sidebar 9, search 8, post 19, with duplicates; P4). |
| S3 | Why the room page is half the others (harness, base image, env only) | Tiering. Control t0: room 3,402 / messages 7,493 / sidebar 7,788. `TC_CallCountingDelayMs=0`: **7,471** / 7,342 / 7,674. `TieredPGO=0`: 6,350 / 6,540 / 6,900. `TieredCompilation=0` + `ReadyToRun=0`: 6,434 / 6,184 / 6,572. The same image with a 15 s warm-up at c=16 (S1) does 7,720 on the room page. |
| S4 | Runtime knobs (harness, base image; control e0: room 2,997, messages 7,416, sidebar 7,456, search 7,191) | `LOG_REQUESTS=false` (round 2, clean): 3,204 / 7,058 / 7,708 / 6,895, no gain on a miss. `HillClimbing_Disable=0`: 2,899 / 6,710 / 7,052 / 6,605. `ForceMinWorkerThreads=16`: 2,675 / 6,043 / 6,193 / 6,100. Both log off and 16 workers: 2,955 / 6,331 / 6,526 / 6,416. |
| S5 | What a connection per request and a hit cost (perf, harness seed) | **P2+P3, miss:** room 354.6 us (10,236 req/s), sidebar 349.4 us (10,497). `Campfire.Db` 124.4, SQLite engine 73.8, Microsoft.Data.Sqlite 30.5, templates 33.7, Kestrel 31.9, cookies 25.6. Custom-attribute reflection about 8% (P4a). **P2+P3+cache, hit:** room **89.3 us** (35,071 req/s on 3.13 cores), sidebar **73.3 us** (41,891). Kestrel 24.2, SQLite engine 20.0, Microsoft.Data.Sqlite 13.5, cookies 10.1, log 4.5. |
| S6 | OutputCache with a fresh-authorization policy (`outputcache-spike.cs`, net11.0 RC2) | `CacheRequestAsync` can refuse a lookup per request: an auth check there works. `ServeFromCacheAsync` **cannot** refuse a found entry. A response carrying `Set-Cookie` is **never stored**, with or without `DefaultPolicy`: from the action (rendered twice), from `OnStarting` in `CacheRequestAsync` (rendered twice), or appended in `ServeResponseAsync` (not stored and not sent). A cookie appended in `CacheRequestAsync` survives on a hit. |
| S7 | A and A+B on the harness against Rails (2 rounds each, same session) | Control u0 (Rails / port / ours): room 4,916 / 36,049 / 3,368; messages 4,850 / 41,988 / 7,466; sidebar 5,242 / 38,207 / 7,728; search 5,128 / 33,868 / 7,238; post 384 / 8,124 / 2,002. **A (P2+P3), u1:** 10,244 / 9,910 / 10,643 / 8,917 / 2,454, which is **2.08x / 2.04x / 2.03x / 1.74x / 6.39x** u0's Rails. **A+B (spike3), v0, Rails in the same run:** room **35,104** (Rails 4,850, **7.24x**), messages **33,992** (4,872, **6.98x**), sidebar **42,340** (5,116, **8.28x**), search **41,456** (5,134, **8.07x**), post **2,381** (383, **6.22x**). **Mixed, v1:** room 33,320 (2,491, 13.4x), messages 33,065 (3,166, 10.4x), sidebar 40,556 (4,449, 9.1x), search 40,241 (3,799, 10.6x). Every sample was 2xx and passed the harness's contract validation. Without the log line (v2): 41,173 / 39,561 / 50,331 / 49,052. |

So the target is reachable by D9-compatible means, with the response cache. Expected after 10.6 on the harness: about 7x Rails on the
room and messages pages, 8x on the sidebar and search, 6x on posting, and 9-13x on the mixed reads. Without the cache it is about 2x
(P5).

## 3. Types and modules

All in `src/Campfire.App` unless named. Compile order: `Web/PageCache.fs` after `Web/Respond.fs` and `Web/Current.fs`, before
`Web/Pages.fs`.

**`Dockerfile`** (P2): `DOTNET_TC_CallCountingDelayMs=0` added to the `ENV` with the two existing runtime settings, under a comment
in the same style (S3's numbers, `bench/results/phase10-design`).

**`src/Campfire.Db/WriteGate.fs`** (P11)

```fsharp
static member Epoch : int64                   // Volatile.Read of a static counter
static member internal Bump() : unit          // Interlocked.Increment
```

**`src/Campfire.Db/Ef/Writes.fs`**: `transact` calls `WriteGate.Bump()` right after `transaction.Commit()`. `run`'s `finally` calls
it before `gate.Exit()`.

**`Web/Data.fs`** (P3, P4b)

```fsharp
let private opened (http: HttpContext) : CampfireDb          // the request's context, its connection opened once (EF's OpenConnection)
let read / readAsync / present / presentAsync / write / tryWrite   // unchanged signatures, all through `opened`
let account (http: HttpContext) : Result<Account option, Error>    // Account.first, memoized in Current for the request
```

The connection stays open until the request scope disposes the context. Nothing opens it at scope creation (P3). `Pages.load`,
`Rooms.renderShow`, `Sidebars.show` and `Searches.index` take the account from `Data.account`. The `last_room_visited` room is
memoized beside it when it equals the room `set_room` already found.

**`Web/PageCache.fs`** (new, about 150 lines with comments; P6-P10, P12)

```fsharp
module PageCache =
    /// What a hit writes: the content encoding, the kept bytes, the content type and Rails' cache headers (never Set-Cookie).
    [<Sealed; System.ComponentModel.ImmutableObject(true)>]
    type Entry(encoding: string, body: byte[], contentType: string, headers: struct (string * string)[]) = ...
    type EntrySerializer() = interface IHybridCacheSerializer<Entry>        // for the size only, as CompressedPage's

    /// The lookup's record of the request, in HttpContext.Items: the key, the epoch and Set-Cookie's count at the lookup.
    type internal Ticket = { Key: string; Epoch: int64; Cookies: int }

    /// Upstream's `cache_read_response` at the point it sits: eligible (P9) and found, the entry's handler; else `render ()` with
    /// a ticket left for `capture`.
    val serve : HttpContext -> (unit -> Task<Result<HttpHandler, Error>>) -> Task<Result<HttpHandler, Error>>

    /// Called by Pages.send after a kept compressed page went out: stores the entry if the ticket's conditions hold (P10).
    val capture : HttpContext -> status: int -> contentType: string -> page: CompressedPage -> unit
```

`serve` reads the encoding from the same provider `Pages.encoderFor` uses (`encoderFor` becomes internal and is shared). The budget
check reads `CampfireOptions.ResponseCacheBytes`.

**`Web/Pages.fs`**: `send` calls `PageCache.capture` in the kept-compressed branch, after `WriteAsync`.

**The four actions.** `Rooms.show` wraps `renderShow`. `Messages.index` wraps everything after `Before.setRoom`, so
`findPagedMessages`, `fresh_when` and the render are skipped on a hit. `Sidebars.show` and `Searches.index` wrap everything after
`Before.chain` (as `spike3.diff`, without its environment switches).

**`Config.fs`** (P12): `ResponseCacheBytes` from `CAMPFIRE_RESPONSE_CACHE_MB` (default 64, `0` off, Ruby's `to_i` like the
fragment one). **`Campfire.Views.Fragments.add`** takes the sum for `SizeLimit` and registers `EntrySerializer`.

**`bench/upstream-quick`** (bash, runs inside colima like `bench/quick`; plus `bench/lib/upstream_quick.py` for the summary; about
200 lines; not counted by `bin/kloc`)

```
colima ssh -- bash -c "cd $PWD && nohup bench/upstream-quick [--commit REF|--image IMG|--mounted DIR] [--apps rails,fsharp,dotnetway]
    [--routes room_show,messages_page,sidebar,search,post_message] [--rounds 2] [--mixed] [--env JSON] [--out DIR] [--keep] &"
```

1. Disk: Mac free (`df` on `/Users/clank`) at least 15 GB, else `blocked: disk` in the status file.
2. The dotnetway source is a clone of `--commit` (default `HEAD`) under the VM's scratch dir: `git clone --shared --no-checkout`, then
   `checkout --detach`. The harness reads its `rev-parse` and `status`. No worktree is registered in the repo.
3. The image: `--image` as given; `--mounted DIR` (`bin/publish-mounted`'s output) layered over `campfire-dotnetway:app` as
   `campfire-dotnetway:uq-<short>` (seconds, for iteration); otherwise `docker build` from that clone with `SOURCE_REVISION` and the
   label checked (the official measurement).
4. The harness: a 3 GB tmpfs at `/mnt/upstream-quick`, a clone of `/Users/clank/Desktop/projects/once-campfire-verification` at
   `HARNESS_REF` (default `oncrack-adapter`), the static loadgen from `upstream-campfire/tools/loadgen-1.98.1-static`. The seed comes from
   `bin/seed --image campfire-reference:app`, kept between runs in the VM's `/var/tmp/upstream-quick/seed-<harness sha>`. The workspace
   is clones of Rails `ca9b963` (`upstream-campfire/once-campfire`) and the port `9954ad0`, plus onCrack, Rust or C when asked (their
   images as in the six-ways `env.sh`).
5. The run: host probe and load before (`bench/lib/hostprobe.py`), `ruby bin/benchmark --workspace ... --rounds R --duration 8
   --concurrencies 16 --cpus 0-3 --client-cpus 4-7 --apps ... --routes ... [--mixed-write-rate 10] --output OUT/raw`, then the
   probe after. `RAILS_BENCH_ENV` as in six-ways; `DOTNETWAY_BENCH_ENV` from `--env`.
6. The output: `OUT` (default `bench/results/upstream-quick-<stamp>`) holds `summary.md` and `summary.json`. For each route and app:
   the median, min and max over the rounds, **x Rails** (median over median, same session), the commit, the clean flag, the image id,
   the seed sha, the probe and the load. The raw harness JSON is kept, the runtime copies of the database are not. The table is
   printed at the end. The status file is `OUT/STATUS`: `0`, a harness exit code, or `blocked: ...`.
7. Cleanup (unless `--keep`): the tmpfs unmounted, the clones removed, `uq-*` images removed.

## 4. Units, in build order

One Sonnet builder per unit, about 90 minutes, from a clean `git status` on `dotnetway`, one commit per logical step (read `git diff
--cached --stat` first), a Phase log entry per unit (KLOC from `bin/kloc`, gates run, numbers, differences recorded).

**Common acceptance for every unit:**
- `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green: the markup lint and its self-test, the Dapper, EF and DateTime guards, and H1.
- `df -h /` first (12 GB; 15 GB before a bench). `bin/clean-docker` after image rebuilds, `bin/clean-parity` after pixel runs.
- Long jobs (`bench/upstream-quick`, `bench/quick`, pixel slices) run detached with a status file and are polled in the foreground
  with bounded loops. One parity compare at a time; `realtime/**` one state per run. Never end a turn while one runs.
- **`bench/upstream-quick` against Rails in the same session** (from 10.2: `--rounds 2`, the five normal routes, apps
  `rails,fsharp,dotnetway`), with the numbers and the x Rails column in the Phase log entry.
- The control image is `campfire-dotnetway:p10ctl`, tagged from the current `campfire-dotnetway:app` (revision label `dcc9434` or
  `f9f4637`) before the first rebuild. It is removed when the phase closes.
- Rebuild `campfire-dotnetway:app` from HEAD before any image gate (`CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build`,
  label checked).
- The app stays pixel-identical after every unit.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 10.1 Tooling | `bench/upstream-quick` and `bench/lib/upstream_quick.py` (section 3), with a usage block in the style of `bench/quick`'s header. `AGENTS.md`: the tool, and the new floor (P15) in "Performance rules". | One full run on `--image campfire-dotnetway:app` (`f9f4637` code), 2 rounds, five routes, `rails,fsharp,dotnetway`, recorded as `bench/results/phase10-baseline/`. Its ratios fall inside the six-ways ranges: room 0.6-0.8x, messages/sidebar/search 1.3-1.7x, post 5-6.5x. A second, `--mixed` run, recorded. `--commit HEAD` builds and measures the clean clone, and the summary shows the commit with `dirty=false`. After the run `docker images` and `mount` show nothing left. |
| 10.2 Configuration and connections (P2, P3) | The `Dockerfile` setting with its comment. `Web.Data` opens the request's connection once. A test (DC1) counts physical connection opens per request with EF's `ConnectionOpened` event on the test host: 1 for each of the four pages and for a post. DC2: a Cable connection's request scope opens nothing that outlives the handshake (the WebSocket test host: the open count does not grow with idle sockets). | Upstream-quick: room at least 9,000, messages, sidebar and search at least 8,500, post at least 2,300, so near S7's u1. `bench/quick` (3 reps, five workloads, c=1 and 16, interleaved with `p10ctl`) loses nowhere beyond the control's range, so the steady state did not lose to P2. Pixel slices `rooms/show/*`, `messages/*`, `users/sidebars/*`, `searches/*` on `default` (lean). `bin/cookie-roundtrip`. H3 (16 concurrent posts all land). |
| 10.3 Narrowing a miss (P4) | (a) `bench/breakdown profile --routes room_show,sidebar` with the stacks kept (`layers.py --top`), the caller of the custom-attribute reflection named in the Phase log, and the fix if a framework means exists (else recorded with its cost). (b) `Data.account` and the memoized `last_room_visited` room. H2's statement counts drop: room 9 -> 7, sidebar and search lose their duplicates. | Upstream-quick with the cache not built yet: every read at least +5% over 10.2's run, or the step reverted and recorded. The same pixel slices; `bin/db-differential` (the account read moved). |
| 10.4 Page cache: core, room and messages (P6-P11) | `Web/PageCache.fs`; the epoch in `WriteGate`/`Writes`; `Pages.send`'s capture; `Rooms.show` and `Messages.index` on `PageCache.serve`; the budget in `Config.fs` and `Fragments.add`; tests PC1-PC12 and PC15-PC17 (section 5). First test written: PC0, that `GetOrCreateAsync` with `DisableUnderlyingData` returns the default without calling a factory. If it does not, the store is the same `IMemoryCache` read with `TryGetValue`, and that is recorded. | Upstream-quick: room and messages at least 5x the same session's Rails (S7: 7.2x and 7.0x). Pixel slices `rooms/show/*` and `messages/*` on every seed (lean), plus `realtime/**` one state at a time (they reload pages after Cable frames). `bin/cookie-roundtrip`. The harness's `--validate` passed on every sample (upstream-quick's exit 0). |
| **checkpoint** | Opus, after 10.4 (below) | |
| 10.5 Page cache: sidebar and search, budget, README (P8, P12) | `Sidebars.show` and `Searches.index` on `PageCache.serve`; PC13, PC14 and PC16 on all four pages; the README's known difference (section 8) and a "Response cache" paragraph (what is cached, the key, fresh authorization, the epoch, `CAMPFIRE_RESPONSE_CACHE_MB`); `--mixed` run. | Upstream-quick normal: every read at least 5x Rails (S7: sidebar 8.3x, search 8.1x). Mixed: every read at least 5x the session's Rails. Pixel slices `users/sidebars/*` and `searches/*` on every seed (lean). |
| 10.6 Gate | The phase gates below, the harness adapter's metadata (P16: a local commit on `oncrack-adapter`, never pushed), `p10ctl` removed, the Phase log's close entry. | Below. |

Then **the phase gates** (gate runner, in this order):
1. `CAMPFIRE_REQUIRE_SEED=1 bin/verify`.
2. **The lean pixel matrix on all seeds**: directory slices on `default`, the 13 `realtime/**` states one at a time, the other four
   seeds in one run. Every cell equal to Rails', plus `aria` and the loose DOM.
3. `bin/cookie-roundtrip`; `bin/db-differential`.
4. **`bench/quick`** five workloads, c=1 and c=16, 3 reps, interleaved with `p10ctl`, against the stored Rails (`phase7-final`): no
   cell under 5x.
5. **`bench/upstream-quick --commit HEAD --rounds 3`**: the normal profile and `--mixed`, `rails,fsharp,dotnetway`. Every normal route
   at least 5x Rails, same session. The table goes in the Phase log with the probe, the load, the seed sha and the commits.
6. `bin/kloc`.

**The checkpoint after 10.4** (Opus) reviews 10.1-10.4 against this note before 10.5:
- Every lookup comes after the before-actions and `set_room`. No hit path skips `restore_authentication` or the membership read
  (read the four call sites, and PC2-PC4).
- `capture` stores nothing that carries `Set-Cookie`, nothing rendered across a commit (PC8, PC12), and nothing for a bot key or a
  flash.
- The epoch is bumped before the after-commit queue emits (read `Writes.transact`), and PC6/PC17 pass under load.
- The key covers every `ViewContext` field the four pages read (P8's list against `Pages.load` and `Pages.render`).
- The upstream-quick numbers are in the log with Rails from the same session.

## 5. Tests

In `tests/Campfire.App.Tests` unless named. Hosts through `Boot.build` (as every test). `Browser` with real cookies.
Byte-compare bodies after gunzip, and headers minus `Date` and `X-Request-Id`.

| # | Test |
|---|---|
| DC1 | One physical connection open per request on the room page, the messages page, the sidebar, search and a post (EF `ConnectionOpened` count on the test host's options). |
| DC2 | Idle WebSocket connections (Cable test host, 20 sockets) leave the open-connection count unchanged once they are subscribed. |
| PC0 | HybridCache with `DisableUnderlyingData` returns the default for a missing key without calling the factory (decides P10's lookup). |
| PC1 | A second GET of each of the four pages is a hit (a counter on `capture`/`serve`, or the render's statement count is 0 beyond the before-actions'), and its body and cache headers equal the first response's. The `last_room` `Set-Cookie` is on both when the request lacks the cookie, and on neither when it carries it. |
| PC2 | The session row deleted between two GETs: the second redirects to sign in (Rails' answer), not a cached page. |
| PC3 | The membership removed between two GETs of the room page and of the messages page: Rails' `set_room` answer (the 404 or redirect it gives today). |
| PC4 | The user deactivated between two GETs: whatever Rails answers today (the existing test's expectation), never the cached page. |
| PC5 | Two users (and two sessions of one user) on the same URL get their own pages. A body that names its user is never served to another (bytes compared, entries counted). |
| PC6 | After a post's 2xx, the next GET of the room page and the messages page shows the message. Likewise a boost, an involvement change, a profile name change and the account's custom styles, each made through its own action. |
| PC7 | A Cable-visible write made outside an HTTP request (a channel command, a job's after-commit write) bumps the epoch, and the next GET renders. |
| PC8 | A render that adds a cookie (the hourly session resume, by advancing the `FakeTimeProvider` past `Session.needsResume`) is not stored. A request whose flash is set (after a redirect with a notice) is neither looked up nor stored. |
| PC9 | `Accept`, `Turbo-Frame`, `Accept-Encoding` (gzip vs br) and `User-Agent` variants get separate entries. `identity` always renders and stores nothing. |
| PC10 | `If-None-Match` with the messages page's ETag still gets its 304 (bypassing the cache), and a cached messages page carries the same `ETag`, `Last-Modified` and `Cache-Control` as the render. |
| PC11 | HEAD renders as before (no body, its `Content-Length`) and is never served from an entry. |
| PC12 | A commit while a page renders (a write released from a hook in the render path on a test-only seam, or an epoch bump injected through `WriteGate.Bump` between lookup and capture) leaves nothing stored. |
| PC13 | `CAMPFIRE_RESPONSE_CACHE_MB=0`: no entry stored, every request renders. |
| PC14 | A body over 1 MB is not stored (a room page made large with seeded messages, or the limit lowered on a test seam). |
| PC15 | A bot-key request to the messages page never looks up or stores. |
| PC16 | The four pages' bytes do not change with `Referer` (absent, same-origin, another page): the reason `Referer` is not in the key. |
| PC17 | 16 readers and 1 writer for 3 s (as H3): after each post's 2xx, that writer's next GET contains its message. |

## 6. What is deleted

Nothing in `src`. Spike code was never committed (`spike3.diff` is evidence, not a patch to apply as is: its `SPIKE_*` switches and
its `ConcurrentDictionary` go, and P6-P12 replace them). `campfire-dotnetway:p10ctl` is removed at the close. `bench/upstream-quick`
cleans up after itself.

## 7. KLOC projection

`bin/kloc` before: 26,210 corrected (25,763 cloc). Added: `PageCache.fs` about 150 lines with comments (about 100 counted); the four
actions' wrapping (+12); `Web/Data.fs` open-once and the memos (+25); `WriteGate`/`Writes` (+8); `Config.fs` and `Fragments.add`
(+10); `Pages.send` (+4). Removed by 10.3's narrowing: about 10 at the call sites. **About +150 counted, about 26.36 KLOC corrected
(25.9 cloc).** Still under Rust's 31.0. Tests (about +400) and `bench/` are not counted.

## 8. Known differences (README "Known differences", plan section 4)

- **Class B: a write made outside the app shows on a cached page within 60 s, or at the app's next own commit.** Upstream Rails
  watches `PRAGMA data_version`, because its web and job processes are separate. theDotNetWay is one process and its only writer
  (P11), so this covers only a database edited live by another program, which neither app supports. Pinned by a test that writes
  with a raw `SqliteConnection`, then shows the stale page, then the fresh one after the lifetime (a `FakeTimeProvider` on
  HybridCache's clock if it takes one; otherwise a lowered lifetime on a test seam).
- Nothing visible otherwise. The bytes of a hit are the bytes of the render (PC1). The cookies come from the before-actions on every
  request. `identity` requests render, as all requests did before.

## 9. Risks and how a builder sees each early

1. **A stale page after a write** (read-your-write, the realtime states): the epoch bumped too late, or a render stored across a
   commit. Seen in PC6, PC12, PC17 and the `realtime/**` states. The bump is in `transact`, before the queue emits.
2. **A per-request value in a stored body** (one user's page served to another, a host or URL wrong). Seen in PC5, PC9, PC16 and the
   pixel slices on every seed. Before writing the key, list every `ViewContext` field the four pages read (P8).
3. **The open connection outliving the request**: a WebSocket scope, or a job taking `Web.Data`. Seen in DC2. Open lazily in
   `Web.Data` only.
4. **P2 costs the steady state** (more methods tiered sooner): seen in 10.2's `bench/quick` against `p10ctl`. If a cell drops beyond
   the control's range, record it and weigh it against S3's +120% on the harness's room page.
5. **HybridCache semantics**: the lookup without a factory (PC0), entry sizing (the serializer is called for the size), the
   1,024-character key limit (the key is a hash). Seen in PC0 and PC13.
6. **Memory**: one entry per user, page, user agent and format, 25-40 KB compressed. The size limit evicts least recently used.
   `dotnet.gc.last_collection.heap.size` in 10.5's trace stays under the budget plus today's heap.
7. **Harness variance**: the Mac's load moves Rails and ours together. Same-session ratios only, the probe recorded, a run with the
   probe above 1.10 repeated. S4's first `LOG_REQUESTS` round ran beside a build and was discarded.
8. **The messages page's `fresh_when`**: the lookup moves before it. Conditional requests bypass (P9), and PC10 checks the headers.

## 10. Questions for Chris

None. The two calls this phase needed are settled by D9 and the spikes:
- HybridCache in the actions instead of the OutputCache middleware (P6, S6);
- the request log kept at its 15-17% hit cost (P14).

The target is met by the spikes: 7.0-8.3x Rails on the normal reads and 6.2x on posting, same session. The landing page states it
plainly with the cache named, as Rails, Rust and onCrack run with theirs.

## 11. Security review (2026-10-29, Opus reviewer, on `e30e84d`; fixes in `d89fdd2`)

**Read.** Phase 10's `src` changes whole (`git diff 838f700 e30e84d -- src Dockerfile`, 16 files): `Web/PageCache.fs`, the four GET actions
(`Rooms.show`, `Messages.index`, `Users.Sidebars.show`, `Searches.index`), `Web/Pages.fs` (`send`'s capture), `Web/Data.fs` (the
connection opened once, the memos), `Current.fs`, `Web/Current.fs`, `Campfire.Db/WriteGate.fs` and `Ef/Writes.fs` (the epoch),
`Ef/FastReads.fs` and `Models.fs` (`user.rooms.original` on Dapper, the test log sink), `Config.fs`, `Boot.fs`, the `Dockerfile`;
beside them the before-action chain (`Web/Before.fs`), the cookie scheme (`Web/Authentication.fs`), `Pages.load`/`render` (every
`ViewContext` field), `Csrf.fs`, the HybridCache registration (`Fragments.add`), the single-writer guard (`Ef/Interceptors.fs`) and every
`Writes.run` caller. Tests: `PageCacheTests` PC0-PC18, `FastReadsTests`, `HotPathTests` H4.

**Findings.**

| # | Where | Severity | Status | What, and the test |
|---|---|---|---|---|
| PS1 | `Web/PageCache.fs:205` (`serve`), `Web/Current.fs:21` | Medium | fixed (`d89fdd2`) | **A render made from rows read before a commit could be stored under the epoch after it.** `serve` took `WriteGate.Epoch` at the lookup, after the before-actions had read the session and user (`restore_authentication`) and `set_room` the room and membership (memoized for the render; the room page renders from that room). A commit between those reads and the lookup (a rename, a role change, the account's settings) bumped the epoch first, so the render stored the old name or button under the new epoch, and a client that reloads on that commit's Cable frame got it back until the next commit or 60 s (P11's "never the page from before" did not hold). Per user and session only, never another user's page. `Current` now records the gate's epoch when it is made, which the cookie scheme does before its lookup; `serve` keys and tickets under it, so any commit after the request began retires the render (an hourly session resume now leaves its request unstored, as a cookie-setting render already did). **PC19**: a rename committed on the new `BeforeLookup` seam (after `set_room`, before the key) and the next GET: it failed before (the cached "All Talk" came back), passes after. |
| PS2 | key and eligibility (P8, P9) | Info | holds | **Another user's or session's page:** the key carries the user id and the session id (server values), so an entry is only ever served to the session that made it (PC5). The fields are joined by U+0001 and hashed; a header holding the separator can only add fields to the attacker's own sequence, which then has more fields than any other session's, so no key collides across sessions. **Host and headers:** `Host`, `User-Agent`, `Accept`, `Turbo-Frame` and the query are in the key, so a forged value makes a new entry for the sender alone; nothing a viewer sends can reach another session's entry. |
| PS3 | P7, the four call sites | Info | holds | **Revoked session, removed membership, deactivated user, ban, bot key:** every hit runs `Before.chain` (banned IP, `restore_authentication` with the session and user read on every request, `deny_bots`, `allow_browser`) and, on the room and messages pages, `set_room` before the lookup; a bot key, HEAD, a conditional request and a flash never look up. PC2, PC3, PC4, PC8, PC10, PC11, PC15. |
| PS4 | P11, `Ef/Interceptors.fs:148` | Info | holds | **A stale page after a write:** every write is `Writes.run` on the one gate (the guard refuses a write statement outside it), and it bumps after `COMMIT` and at the gate's exit; messages, boosts, edits, deletes, memberships, involvement, profiles, the account and presence are all commits there. PC6, PC7, PC17, now PC19. A write by another program shows within 60 s (PC18, the README's class B entry). |
| PS5 | P10, `capture` | Info | holds | **`Set-Cookie`, errors, CSRF:** an entry holds `ETag`, `Last-Modified`, `Cache-Control`, `Vary` and `Link` only; a render that added a cookie is not stored, so the cookies on a hit are the before-actions' own (PC1, PC8). Only a 200 sent from a kept compressed page is stored; a hit is always that 200. Forgery protection is token-less (`Sec-Fetch-Site`/`Origin`), so no page carries a token to replay. |
| PS6 | P12, `Fragments.add` | Info | holds, accepted | **Growth:** one memory cache bounded by `CAMPFIRE_FRAGMENT_CACHE_MB` + `CAMPFIRE_RESPONSE_CACHE_MB` (HybridCache sizes each entry by its serialized length), entries at most 1 MB, 60 s. **PC20**: 300 keys a signed-in client controls (user agent and query) at a 2 MB limit, the estimated size stays under it. The budget is shared, so one signed-in user can churn the fragments out (a cost in CPU, not in memory); accepted, as upstream's response caches share theirs. |
| PS7 | `Web/Data.fs` (memos), `FastReads.originalRoomForUser` | Info | holds | **The read path's authorization:** `set_room` is still `user.rooms.find_by(id:)` through the membership join; the memo lives in the request's `Items` (never across requests) and a `Data.tryWrite` drops it; the `Writes.run` calls that bypass `tryWrite` (session start, resume, destroy) run before anything is memoized or end in a redirect. `user.rooms.original` on Dapper is the same join ordered by `created_at` (`FastReadsTests`). |
| PS8 | logging | Info | holds | Phase 10 changed no logging (P14 kept the line). The Dapper statement sink (`FastReads.logTo`) is test-only: nothing in `src` registers one, and it receives SQL text with placeholders, never values. |

The six-ways numbers after the fix: see the Phase log entry "Security review of Phases 9 and 10".
