# Design note: the close (Phase 9)

Status: design, 2026-10-27, before Phase 9 builds. Author: the Opus designer (plan section 5). Binding for the Phase 9 builders
together with `plans/dotnetway.md` sections 2.9-2.11 and 3-5 and D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`).
Where this note and plan sections 2.9-2.11 disagree, this note wins: it is later and rests on the spikes in section 2. The disagreements,
each argued below:
- **Active Support JSON stays hand-written** (plan 2.10 said `System.Text.Json` with an encoder). `Utf8JsonWriter` writes 470 of the 3,655
  values in the vectors differently, among them the floats of stored blob metadata (`320` for Ruby's `320.0`, which Rails then reads as an
  Integer) and every emoji (as a surrogate pair). That would break the golden vectors and the data Rails reads back (C1, S4).
- **`ContentDisposition.format` stays** (plan 2.10 said `ContentDispositionHeaderValue.SetHttpFileName`, class B). `vectors/storage.json`'s
  16 `filenames` cases pin Rails' exact header, and the disposition is inside the signed disk keys the vectors compare. Its 195-line I18n table
  is a generated file (`reference-tools/storage/dump_tables.rb`, the script that writes Marcel's `Tables.fs`). Upstream's rule leaves generated
  files out of the count, so `bin/kloc` moves it to the static-data list beside `Tables.fs`, with both totals printed (C2).
- **Rack's byte ranges go to the framework, but the vectors stay as a test.** The 200 `byte_ranges` vectors become an HTTP-level test
  against the framework's file results. The 95 cases that differ are listed in the test and fail both ways (C3, S3).
- **The static rows are fixed by keeping the request log's middleware off three route families** (`UseWhen`). Tuning the line does not help.
  In colima the line costs nothing measurable. The `HttpLoggingMiddleware` itself costs about 33 us a request at c=1, with any field set,
  with the line suppressed by its interceptor, and with Docker's log driver off. Only a request that never enters it gets the time back
  (C5, S2).
- **About -0.9 KLOC instead of -1.5** (section 7). The plan's 18.5 -> 17.0 is out of reach: Phase 9 starts from 27.1 KLOC, not 18.5.

Scope: `src/Campfire.Storage` (the trims of plan 2.9), `src/Campfire.Ruby`, `src/Campfire.RailsCompat` and `src/Campfire.Routes` (the merge of
plan 2.10), the App's Active Storage serving (`ActiveStorage.fs`' `PartsStream`/`partsBody`, `Controllers/ActiveStorage.fs`' disk and proxy
range code), the request log's reach (`Web/Services.fs`' pipeline, `Web/HttpLog.fs`, `Web/StaticAssets.fs`), the job and push readers
(`Jobs.fs`, `Integrations/PushDeliveries.fs`), every test project (plan 2.11), `bench/lib/layers.py`, `bench/lib/perf_layers.py`,
`bench/lib/logcount`, `bin/kloc`, `README.md` and `AGENTS.md`. Out of scope, as separate steps after the units: the full pixel gate, the
final bench ("Campfire, six ways"), the security review, the landing page and the independent verification.

**Preconditions.** Phase 8 closes at `c8cce8f` (its `src` is `7d58f54`'s; the Phase log entry is `8f72e40`). `campfire-dotnetway:app`
carries the label `7d58f54`. `campfire-dotnetway:p8ctl` (8.1's state, `6da5ef7`, from before 8.2) is the only older control left in
colima. Phase 8's conditions bind this phase:
- Keep H1 (zero query compilations in steady state), the markup lint and its self-test, the Dapper, EF and DateTime guards, and Phase 6's
  asset conditions green. Phase 6's asset conditions are the startup validation, "nothing renders before `StaticAssets.map`",
  `StaticAssets.map`'s marker middleware, `appEndpointsOnly`, and `bin/asset-check` after any change to the pipeline's order.
- `Boot.build` stays the one way the app and the tests boot.
- Any change to `Web/Host.fs`, `Acme.fs` or `OutputCaching.fs` re-runs HS2/HS4 under `bin/linux-tests App`, AC11 and `pebble-image.sh`.
  No unit here needs to change them: 9.3 reads `OutputCaching.families` and does not edit it.
- Keep the integration facts green through the test consolidation: CL1, GD1-GD4, OG1-OG4, SR-J1/J2/J5, WP1-WP8, RD1-RD7, OP1-OP8, BB1-BB3.
- Re-project the KLOC from 27.1 (section 7). Post a message at c=16 stays the thinnest cell (5.98-6.5x): every bench in this phase
  reports it when it runs it.

## 1. Decisions

Each is settled by D9 (the framework and mainstream packages first, hand-written code only where parity or the 5x floor forces it). The
reason is given with each.

| # | Decision | Reason |
|---|---|---|
| C1 | **`Campfire.Rails.Json` (today's `Campfire.RailsCompat.Json`) stays as it is.** `Value` is the order-keeping JSON value. Parsing is already `JsonDocument`. The writer stays the json gem's text: serde_json's escaping, fpconv's float forms, and `escapeHtmlEntities`. `Storage/JsonValue.fs` (31 lines of `Value` helpers) folds into `Value` members (`Set`, `Merge`) and `Json.parseInt64Only`, and the file goes. | S4: of 3,655 values taken from the vectors (`rails_compat.json`, `storage.json`, `campfire_sessions.json`, `ruby_core.json`'s strings and floats), `Utf8JsonWriter` with `UnsafeRelaxedJsonEscaping` (plus Rails' entity escaping) writes **470 differently**: 58 strings (U+2028/U+2029 escaped, emoji as `😀`), 411 floats (`1E+15` against the gem's `1e+15`, `1E-05`) and stored blob metadata (`"width":320` where Rails wrote `320.0`, so Ruby would read an Integer back). The default encoder differs on 788 values. The signed cookies and the Turbo stream names in the vectors are byte comparisons, and blob metadata is data Rails reads. Parity forces the writer to stay. |
| C2 | **`ContentDisposition.format` and its I18n table stay.** `bin/kloc` adds `ContentDispositionApproximations.fs` to its static-data list. It prints the corrected total both ways once in the Phase log of the unit that does it, so the move can be seen. | `vectors/storage.json` `filenames` (16 cases, `content disposition formats like rails`) pins Rails' exact `inline`/`attachment` header. The disposition string is also inside the signed disk key (`GoldenTests`: `"disposition":"inline; filename=\"a\\u0026b.png\""`), so a different format changes the signed bytes. `SetHttpFileName` writes an unquoted token and `_` for non-ASCII, and escapes `filename*` differently. The table is generated (`@generated by reference-tools/storage/dump_tables.rb from i18n 1.14.7`), as `Storage/Tables.fs` is, and upstream's rule (plan "Counting") leaves generated files and static data out. Counting one and not the other was the inconsistency. |
| C3 | **The disk service's `show` and the proxy's range responses are the framework's file results.** Both become `TypedResults.PhysicalFile(path, contentType, lastModified = ..., enableRangeProcessing = true)`. The headers the controllers set themselves (`content-disposition`, Puma's split of a line break into two header values, `content-transfer-encoding`, `cache-control`) are set on `Response.Headers` before the result runs: the file result does not touch them (S3). `Storage/FileServer.fs`, `Ruby/Rack.fs`, `ActiveStorage.fs`' `PartsStream`, `partsBody` and `partsLen`, and the multipart branch of `sendBlobByteRangeData` go. `OutgoingBody.Streamed` goes too, if nothing else uses it. **The 200 `byte_ranges` vectors stay as a test of the new behaviour.** `FR1` sends each one, as its `Range` header, to a file of the vector's size through the same helper both controllers call. It compares the answer (200 = Rack's `nil`, 416 = `[]`, 206 = the one range) with Ruby's, against a checked-in list of the 95 cases that differ, grouped with reasons. The list fails both ways, like the rich text corpus's `known-differences.json`. | D9 and plan 2.9/2.10 ("Rack byte ranges by the framework", "`FileServer.fs` becomes `Results.File` with ranges"). S3, on the vectors: **105 of 200 answer as Rack does**. Every single, well-formed range a browser sends (`bytes=0-`, `bytes=N-M`, `bytes=-N`) answers the same. The 95 that differ fall in five groups, all class B: (a) several ranges get the whole file with 200, which RFC 9110 allows (Rack sends `multipart/byteranges`); (b) a `Range` on an empty file gets 416 (Rack serves the empty body); (c) headers Rack's `to_i` parsing reads leniently (`bytes=1_0-2_0`, `bytes=+1-2`, `bytes=0d5-0d9`, `bytes=a-b`, `bytes= 1-2`, `bytes=1-2-3`, two `bytes=` parts) get the whole file; (d) numbers past `Int64` get the whole file; (e) `Bytes=`/`items=` units are honoured. HEAD, `If-Modified-Since` (304), 416 with `Content-Range: bytes */N`, and the preset `Content-Disposition` (two values) all behave as the controllers need. The proxy (`send_blob_byte_range_data`) answered 416 for a header Rack could not read, and now serves the file (part of (c)). `Accept-Ranges: bytes` now also appears on the disk service's answers (class A). |
| C4 | **The storage helpers move to the framework or a mainstream package:** `TempFile` is `Directory.CreateTempSubdirectory(prefix)` (0700) holding the one file named with the suffix ffmpeg and libvips need, and disposing it deletes the directory (about 15 lines, from 50). `TempDir` goes (nothing uses it). Storage's hand-written `ResultBuilder` goes for FsToolkit.ErrorHandling's `result { }`, which the App already uses. `Paths.blobProxyPath` and `representationProxyPath` stay: `VectorTests` pins the URL format the proxy endpoints accept. NetVips, ffmpeg, Marcel's tables, the disk key layout and the Marshal digest stay, as plan 2.9 says. `Process.fs` stays: it is `System.Diagnostics.Process`, and its sync timeout and pipe draining are what the blocking pool needs. | S6: `CreateTempSubdirectory` makes 0700 directories and `GetTempFileName` 0600 files on macOS and Linux. `GetTempFileName` cannot take the suffix that picks libvips' and ffmpeg's output format, so the file goes inside a framework-made directory. `result { }` covers `let!`, `use`, `for`, `while` and `try ... finally`, which is everything the builder is used for. Direct uploads already keep the 16 MiB cap and the 422 for a non-numeric size, through Phase 2's `Edge.bodyLimit` and the controller. Plan 2.9's `FormOptions` line needs no change; 9.1 confirms it with the existing tests. |
| C5 | **The request log stays the framework's `UseHttpLogging`, wrapped in `UseWhen`.** Three route families never enter it: **the static asset endpoints** (every `MapStaticAssets` route, including the public files, matched by path against the loaded table: `StaticAssets.isAssetRoute`), **the avatar family** (`OutputCaching.families`' `"avatar"` predicate, read and not edited), and **`/up`**. Everything the app runs is still logged: pages, posts, bot calls, redirects, 404s and errors. The line, its fields, SR1-SR8, LG1-LG3 and the redaction are unchanged. | Spike S2 measured each rung in colima on the bench's CPU split, with the spike app inside the app's image and the bench's loadgen. Static file at c=1, req/s: line on 13,873-14,066; line suppressed by the interceptor (`LoggingFields = None`, at the request or the response) 13,712-14,167; only request fields 13,973-14,084; status added by the interceptor instead of the field 13,612-14,085; Docker's log driver off 13,335-14,662; **the middleware's fields set to None (it returns at once) 25,487-25,763; no middleware 25,122-26,638; `UseWhen` around it 25,385-25,661.** So 8.6's ladder could not have worked: the cost is the middleware entering its logging path, not the line. In the VM that costs about 33 us a request at c=1 (4.5 us on the Mac). The framework's own way to keep a middleware off some requests is `UseWhen`. Which requests: Rails silences `/up` in its own log (`config.silence_healthcheck_path = "/up"`, `reference/config/environments/production.rb`). Fingerprinted assets and avatars are the bulk of a page's requests and carry nothing an operator acts on. Measured gain, 8.6's e4 (the middleware off) on the real app: `static_css` 11,868 -> about 18,300 req/s at c=1 (0.78x -> about 1.2x Rails) and 85,971 -> about 108,000 at c=16 (0.87x -> about 1.09x); avatar 12,896 -> about 20,200 at c=1 (1.0x -> about 1.6x). **What it costs (class B):** no request line for a fingerprinted asset, a public file, an avatar or `/up`, where Thruster logged each with `cache=hit`. A 500 on those paths still writes `PublicExceptions`' `request failed ... path=` line. Every request is still counted by the framework's meters (`http.server.request.duration`, `kestrel.*`; `dotnet-counters monitor -n campfire Microsoft.AspNetCore.Hosting`), which is the framework's own record of asset traffic. Reversible with one predicate line. Not chosen: an interceptor that drops the line (S2: no gain); moving `StaticAssets.map` or `UseOutputCache` ahead of the logging middleware (it would change which statuses and exceptions are logged, F8's order, and the cache's key inputs); a writer of our own (D9). |
| C6 | **The job and push readers wake one reader per item: `ReadAsync` in a loop, ended by `ChannelClosedException` when the queue is closed and drained.** This replaces `WaitToReadAsync` + `TryRead`. It applies to `JobWorkers.work` and `PushDeliveries.ReadDeliveries`; the single invalidation reader may keep its loop. Drain semantics are unchanged: closing the writer still lets the readers empty the queue first, and the readers still take no stopping token. | The post c=1 CPU gap (+3-5% since 8.2) has a mechanism. A `BoundedChannel` completes **every** pending `WaitToReadAsync` when one item arrives, so each queued push delivery woke all 50 `PushDeliveries` readers (49 find nothing and park again). The bench's `.env.reference` has VAPID keys, so every post queues deliveries to the neutered subscriptions. `ReadAsync` completes one waiter. S1, one item a millisecond (posts at c=1), process CPU per item: 50 readers 267-348 us with `WaitToReadAsync` against 129-144 us with `ReadAsync` (idle baseline 12-18 us). With 2 readers the two are equal within noise (137-169 us). 8.2 replaced `WebPushPool`'s single taking thread with the 50 readers, which is when the gap appeared. The fix is the framework's own API, and cheap. 9.4 confirms it with `bench/quick` against `p8ctl`, the control from before 8.2. |
| C7 | **One `Campfire.Rails` project, namespaces after the folders.** `Campfire.RailsCompat` becomes `Campfire.Rails` (the project root). `Campfire.Ruby` becomes `Campfire.Rails.Ruby` (folder `Ruby/`). `Campfire.Routes` becomes `Campfire.Rails.Routes` (file `Routes.fs`, still `module Campfire.Rails.Routes.Routes`). `open Campfire.Routes` maps to **`open Campfire.Rails.Routes`, never `open Campfire.Rails`**. The three test projects become `tests/Campfire.Rails.Tests` (`Ruby/` subfolder for the Ruby tests). The trims come first, as their own commit, and the rename follows as a commit with no behaviour change. The trims are: `Float.toF` and its strtod port (about 265 lines that nothing in `src` calls; the `to_f` group of `ruby_core.json` is retired with its test, and the vector file is unchanged); `Erb`'s three dead writers (`writeHtmlEscaped`, `pushHtmlEscaped`, `writeHtmlEscapedUtf8`, the UTF-8-writer era's); `Uri.fs` (`urlEncode` = `Uri.EscapeDataString`, `cgiEscape` = `Uri.EscapeDataString(s).Replace("%20", "+")`, one line each in `Ruby.fs`); `Rack.fs` (gone in 9.1). `Erb.htmlEscape` stays (Rails' five, `&#39;` included, which `WebUtility.HtmlEncode` does not write). Kept as plan 2.10 lists: verifier, encryptor, key generator, metadata envelope, Marshal reader, GlobalID/SGID, signed ids, `Password` (BCrypt.Net-Next), secrets, cookies, Turbo stream names, routes, `Float#to_s`, `to_i`, `strip`. | Plan 2.10 and the "seven projects" decision (plan section 6). S5: `Uri.EscapeDataString` equals `url_encode` and, with `%20` as `+`, `CGI.escape` on all 427 `ruby_core.json` strings, on lone surrogates, on emoji and on a 70,000-character input. S7 (the merge, done in a scratch copy of HEAD): with the mapping above, the whole solution builds with 0 warnings and the merged `Campfire.Rails.Tests` passes 88 of 88, golden vectors included. Mapping `open Campfire.Routes` to `open Campfire.Rails` instead broke dozens of controller files: RailsCompat's `Error` type shadowed `Campfire.App.Web.Error` and every `taskResult` bind failed. Hence the explicit rule. Namespaces that follow the project and folder are what a .NET reader expects (D9: the most familiar code). |
| C8 | **Test consolidation, as far as it pays.** Plan 2.11's main move (kit, view and Cable tests to HTTP-level tests) happened phase by phase. What is left: (1) tests of code this phase deletes go with it, each named in the unit's Phase log (`FileServerTests` 9, the `Rack`/`to_f`/`Erb`-writer facts and `MiscTests`' "byte ranges are not read into memory"); (2) the renames: `Support.CapturingLoggers` becomes `Support.LogCapture` (it is a `FakeLoggerProvider` over a `FakeLogCollector` with `Text`), `Test.Booted` becomes `Test.Wiring` (the `Support.Wiring` record it already is), and the unit-letter names become what they test (`ControllersATests`..`DTests` by controller group, `SeamTests` folded into `RouteTableTests`/`AuthenticationTests`); (3) every fact of the Phase 8 list carries its ID in its name, so `dotnet test --list-tests` finds all 38 (today `CL`, `GD`, `OG` and `WP` are only in comments); (4) the two load-sensitive Cable facts are made deterministic (L7 takes the least of three measurements, since heap noise only adds; L5 reads the close frame within a deadline instead of after a fixed wait). No new test project, no `WebApplicationFactory` (the tests boot through `Boot.build`, Phase 8's condition). | D9 for tests is the framework's test packages, already adopted (xunit.v3, `FakeTimeProvider`, `FakeLogger`). The rest is naming and deleting what has no subject left. A blanket rewrite to `WebApplicationFactory` would add a second boot path, which Phase 8's condition forbids. |
| C9 | **The bench's layer tables follow the code.** `bench/lib/layers.py` and `perf_layers.py` lose their `Campfire.Kit` buckets. They get the front server's middleware (`Microsoft.AspNetCore.HttpLogging`, `OutputCaching`, `ResponseCompression`, `Microsoft.Extensions.Logging.Console`), the request layer (`Campfire.App!Campfire.App.Web`), `Campfire.Rails` in place of `Campfire.RailsCompat`, and the channel readers (`System.Threading.Channels`). | Phase 7 and 8 verifier open item. It is also 9.4's own instrument: the post c=1 diagnosis runs `bench/breakdown perf`. |
| C10 | **README is theDotNetWay's.** The intro, status, image name and "Running it" are theDotNetWay's. "Performance" holds the Phase 8 gate's table against Rails until the final bench replaces it. "Known differences" is self-contained: class C stated once, class A once, class B by layer, each line naming its test, and Rust's differences that still apply restated rather than pointed to. "How this was built" is short: phases, what the framework replaced layer by layer, the hybrid Dapper reads, KLOC by phase. AGENTS.md drops the port's withdrawn rules and gets the seven-project table (9.7). | The publishing decision: the landing page and the repo are read by people who never saw the port. Every phase verifier checked its own README entries, so this is mostly a re-shaping plus the class B lines of this phase. |

What stays hand-written, and why:
- the json gem's writer (C1: vectors, stored data) and the I18n disposition table (C2: vectors, signed keys);
- Rails' message formats, Marshal subsets, Ruby number text (`Float#to_s`, `to_i`), `Erb.htmlEscape`, `strip`, the route helpers (plan 2.10's kept list);
- the libvips/ffmpeg glue, Marcel and the disk key layout (plan 2.9);
- the three `UseWhen` predicates (C5).

## 2. Spikes (scratch projects under the session scratchpad in `/private/tmp`, 2026-10-27, deleted after)

.NET SDK 11.0.100-rc.2.26504.105 (net11.0). The spike project was an F# console on `Microsoft.NET.Sdk.Web`, referencing the repo's built
`Campfire.Ruby.dll` and `Campfire.RailsCompat.dll` (Release, `7d58f54`'s code) by path. The colima runs used that project published
self-contained for linux-arm64, run inside `campfire-dotnetway:app` (entrypoint overridden, `--cpuset-cpus 0-3`, host network, the image's
environment, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1`), loaded by the bench's own `loadgen http` on CPUs 4-7. That is the bench's split,
with nothing else running in the VM (load 0.2). Mac load 1.3-1.6. Numbers rank options; they do not gate.

| # | Question | Result |
|---|---|---|
| S1 | Does `WaitToReadAsync` wake every reader of a bounded channel, and what does it cost at one item a millisecond? | Process CPU per item, 3,000 items 1 ms apart, two runs: 50 readers `WaitToReadAsync` 347.8 / 267.1 us, `ReadAsync` 143.6 / 129.4 us; 2 readers 169.4 / 148.5 against 149.0 / 136.8 us; idle (no writes) 12-18 us. **50 waiting readers cost about twice the CPU per item.** |
| S2 | Where does the request log's c=1 cost come from, and which framework setting removes it? | Static file (30 KB) at c=1 in colima, two rounds each, req/s: the full line 13,818-14,107; line dropped by the interceptor at the response (`Age` header or endpoint metadata) 13,712-14,167, or at the request 13,848-13,944; request fields only 13,973-14,084; separate request and response lines 14,252-14,346; Docker `--log-driver none` 13,335-14,663; **`LoggingFields = None` 25,487-25,763; no middleware 25,089-26,638; `UseWhen` excluding the path 25,385-25,661.** Avatar-like output-cache hit: line 14,922-15,195, interceptor-suppressed 14,559-14,765, no middleware 28,442-28,769. At c=16 the middleware costs 3-6% (110-116k against 117-120k). Checked on the way: in .NET 11 RC2 an interceptor can drop the combined line by setting `LoggingFields = None` in `OnRequestAsync` or `OnResponseAsync`; an output-cache hit carries `Age`; adding `StatusCode` from the interceptor reproduces the line exactly. None of these help speed. On the Mac the same middleware costs about 4.5 us at c=1. The VM's thread hand-offs are what make it about 33 us. |
| S3 | The framework's file results against Rack on the 200 `byte_ranges` vectors, and the headers the controllers need | `TypedResults.PhysicalFile(..., lastModified, enableRangeProcessing = true)` on files of 0, 1, 10 and 100 bytes, behind Kestrel: **105 of 200 equal**; the 95 others fall into C3's five groups (listed by the spike, to be the test's list). Several ranges: 200 with the whole file. `bytes=500-` on 100 bytes: 416, `Content-Range: bytes */100`, empty body. HEAD: 200, `Content-Length`, no body. `If-Modified-Since` equal to the file's date: 304; earlier: 200. A `Content-Disposition` set before the result with two values survives as two header lines. Non-ASCII in the `Range` header (sent over a raw socket): Kestrel answers 200. |
| S4 | `Utf8JsonWriter` against the json gem's text | 3,655 values from the vectors: `UnsafeRelaxedJsonEscaping` 3,185 equal (differ: 58 strings, 411 floats, 1 object: stored blob metadata `320.0` -> `320`); `JavaScriptEncoder.Default` 2,867 equal. |
| S5 | `Uri.EscapeDataString` against `url_encode` and `CGI.escape` | 427 of 427 `ruby_core.json` strings equal for both (with `%20` -> `+` for `CGI.escape`); lone surrogates, emoji and a 70,000-character string equal to the port's functions. |
| S6 | Temporary files on the framework | `Directory.CreateTempSubdirectory("ActiveStorage-")`: mode 0700; `Path.GetTempFileName()`: mode 0600, name `tmpXXXXXX.tmp` (no suffix). |
| S7 | The project merge in a scratch copy of HEAD (`git archive`), sources moved and namespaces rewritten by script, slnx and project references updated | With C7's mapping: `dotnet build CampfireFs.slnx -c Release` 0 warnings, 0 errors; merged `Campfire.Rails.Tests` 88 passed of 88. The first attempt mapped `open Campfire.Routes` to `open Campfire.Rails`. Every controller with a `taskResult` failed to compile: the opened `Error` type of `Campfire.Rails` shadowed `Campfire.App.Web.Error` (dozens of files). Other users of the projects' names: `bench/crypto-micro/CryptoMicro.fsproj`, `reference-tools/richtext/review/review.fsproj`, `parity/docker/candidate/entrypoint` (a comment), `bin/kloc`'s data path, `AGENTS.md`, `README.md`. |

## 3. Types and modules

After the phase, seven source projects (plan section 6): `Campfire.Rails`, `Campfire.Db`, `Campfire.RichText`, `Campfire.Storage`,
`Campfire.Cable`, `Campfire.Views`, `Campfire.App`, and one test project for each.

**`src/Campfire.Rails/`** (`Campfire.Rails.fsproj`; references only BCrypt.Net-Next; `InternalsVisibleTo Campfire.Rails.Tests`). Compile
order: `Ruby/RubyString.fs`, `Ruby/Integer.fs`, `Ruby/Float.fs` (`floatToS` only), `Ruby/Erb.fs` (`htmlEscape`), `Ruby/Ruby.fs` (the
re-exports, plus `cgiEscape`/`urlEncode` on `Uri.EscapeDataString`), then today's RailsCompat files in today's order (`Timestamp.fs` ...
`ContentDisposition.fs`), then `Routes.fs`. Namespaces per C7. `Json.fs` gains `Value.Set`, `Value.Merge` and `Json.parseInt64Only` (from
`Storage/JsonValue.fs`).

**`src/Campfire.Storage/`** loses `FileServer.fs` and `JsonValue.fs`. Its `ResultBuilder` gives way to FsToolkit (it adds the
`FsToolkit.ErrorHandling` package reference; the version is already in `Directory.Packages.props`). `TempFile.fs` becomes:

```fsharp
/// A file in a directory of its own under the temp path, both deleted on dispose (the suffix picks libvips' and ffmpeg's format).
[<Sealed>]
type TempFile private (directory: DirectoryInfo, path: string) =
    member _.Path = path
    static member Create(prefix: string, suffix: string) =
        let directory = Directory.CreateTempSubdirectory prefix
        let path = Path.Combine(directory.FullName, "file" + suffix)
        (new FileStream(path, FileMode.CreateNew, FileAccess.Write)).Dispose()
        new TempFile(directory, path)
    interface IDisposable with
        member _.Dispose() = try directory.Delete true with :? IOException | :? UnauthorizedAccessException -> ()
```

**`src/Campfire.App/`**
- `ActiveStorage.fs`: `PartsStream`, `partsBody`, `partsLen` go. A helper both controllers call: `ActiveStorage.fileResult (http:
  HttpContext) (path: string) (contentType: string) (lastModified: DateTimeOffset option) : IResult`. It is
  `TypedResults.PhysicalFile(path, contentType, lastModified = ..., enableRangeProcessing = true)`, run through Falco as a handler. The
  caller sets the response headers that are not the file result's before it runs.
- `Controllers/ActiveStorage.fs`: `diskServe` keeps `Disk.decodeVerifiedKey`, the 404s and `cache-control`, then `fileResult` with the
  key's content type and disposition. A missing file is 404, checked with `File.Exists` before the result, as now. `sendBlobStream` and
  `sendBlobByteRangeData` become one path: forced disposition, headers, `fileResult`. The `416 when Rack reads nothing` branch goes (C3 (c)).
- `Web/StaticAssets.fs`: `isAssetRoute (path: PathString) : bool`, a `HashSet` lookup in the loaded table's routes (the set `map` builds,
  kept in `Loaded`). Before `map` has loaded the table it answers `false`.
- `Web/Services.fs` `pipeline`: `if edge.LogRequests then builder.UseWhen(Func<HttpContext, bool>(fun http -> HttpLog.logged http.Request.Path), fun b -> b.UseHttpLogging() |> ignore)`.
  It stays first in the pipeline, where `UseHttpLogging` is now.
- `Web/HttpLog.fs`: `logged (path: PathString) : bool` = not `/up`, not an avatar route (`OutputCaching.families`' `"avatar"` predicate),
  not `StaticAssets.isAssetRoute`. A comment there names C5 and its class B line.
- `Jobs.fs` `JobWorkers.work` and `Integrations/PushDeliveries.fs` `ReadDeliveries`: the `ReadAsync` loop (C6).

**`tests/`**: `Campfire.Rails.Tests` (merged), the `Support` renames (C8), `FR`, `LS` and `CH` facts (section 5).

**`bin/kloc`**: the data list gains `src/Campfire.Rails/ContentDispositionApproximations.fs`, under its path after the merge. It does not
change name. The script's header comment says why.

## 4. Units, in build order

One Sonnet builder per unit, about 90 minutes, from a clean `git status` on `dotnetway`. Commit each logical step, after reading `git diff
--cached --stat`. Write one Phase log entry per unit (KLOC from `bin/kloc`, gates run, differences recorded). The order keeps the app
pixel-identical and green after every unit. The two serving changes come first, while the old projects still exist, so their diffs are
small. The rename comes after the trims, so it carries no behaviour. The pipeline and readers come next, then tests, docs and cleanup,
which touch no request path.

**Common acceptance for every unit:**
- `df -h /` first: stop and report "blocked: disk" under 12 GB free, or under 15 GB before a bench. `bin/clean-docker` after image
  rebuilds, `bin/clean-parity` after pixel runs, `colima ssh -- sudo fstrim -av` if the Mac's free space does not come back.
- `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green: the Release build with warnings as errors, every test, the markup lint and its self-test, the
  Dapper, EF and DateTime guards, and H1.
- The `--fast` pixel slices the unit's change touches, on every seed, with screenshot, aria and loose DOM equal. One parity compare at a
  time. `realtime/**` one state per run. Long runs start detached (nohup, a status file at the end) and are polled in the foreground with
  bounded loops (`timeout 540 bash -c 'until [ -f STATUS ]; do sleep 20; done'`, repeated until done). Never end a turn while a job runs.
- `bench/quick` when the unit touches a request path: interleaved against a same-session control, with the host probe and load recorded.
  A five-workload cell under 5x Rails blocks: fix it first and record it.
- **The controls.** Before 9.1's first rebuild, tag `campfire-dotnetway:app` (label `7d58f54`, Phase 8's closing code) as
  `campfire-dotnetway:p9ctl`. If its label is anything else, build it from `7d58f54` in a scratch worktree under `/private/tmp`, the way
  Phase 7's gate built its control. `p8ctl` (`6da5ef7`, before 8.2) stays until 9.4 has measured against it, then 9.4 removes it.
  `p9ctl` stays for the gate.
- Rebuild `campfire-dotnetway:app` from HEAD before any image gate: `CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build`
  in colima, then check the label.
- `bin/kloc` before and after, recorded.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 9.1 Storage trims and framework file results | C3, C4. `ActiveStorage.fileResult`. Disk `show` and the proxy's whole and ranged answers on it. `FileServer.fs`, `Rack.fs` (and the Ruby project's compile item), `PartsStream`, `partsBody`, `partsLen` and the multipart code go. `TempFile` on `CreateTempSubdirectory`; `TempDir` goes. `JsonValue.fs` folded into `Value`. Storage's `ResultBuilder` becomes FsToolkit's `result { }`. A dead-code pass over Storage (anything nothing in `src` or a vector test calls). Tests: `FileServerTests`, the Rack facts in `Campfire.Ruby.Tests` and `MiscTests`' parts-stream fact go; FR1-FR6 (section 5) come in, with FR1's list of the 95 cases (`tests/Campfire.App.Tests/testdata/byte-ranges-known-differences.json`, group and reason per case). `AppTests`' "blob byte ranges are served from the file" is updated for C3 (a): several ranges now get 200 with the whole file. README: the Active Storage class B lines (section 8). | FR1-FR6 green; `AppTests`' proxy and disk facts green ("proxied blobs name their file like rails", "proxied blobs write odd dispositions as puma does"); `bin/storage-media-vectors` passes (`TempFile` touches every variant, preview and analysis); `bin/db-differential` rc 0 (blob metadata written through `Value.Set`/`Merge`); image rebuilt; `bin/pixel-parity --fast --only 'interactions/composer/attachment*,interactions/lightbox*,users/avatar*,account/edit*,messages/**,rooms/show/designers'` every seed, and `realtime/attachment_sent` alone; **`bench/quick --routes avatar,room_show` c=1 and c=16, 3 reps, against `p9ctl`** (nothing should move; this checks it). |
| 9.2 Campfire.Rails | C7. **Commit 1 (trims):** `Float.toF` and its helpers, `Erb`'s three dead writers, `Uri.fs` onto `Uri.EscapeDataString`, and their facts (the `to_f` group of `RubyCoreTests` and the writer facts; the url/cgi facts stay, now against the framework calls). `bin/kloc`'s data list gains `ContentDispositionApproximations.fs` (C2). **Commit 2 (merge, no behaviour change):** `src/Campfire.Rails` and `tests/Campfire.Rails.Tests` as S7 did them. Then the namespace rewrite (`Campfire.RailsCompat` -> `Campfire.Rails`, `Campfire.Ruby` -> `Campfire.Rails.Ruby`, `Campfire.Routes` -> `Campfire.Rails.Routes`, `open Campfire.Routes` -> `open Campfire.Rails.Routes`). Then the slnx, every `ProjectReference`, `bench/crypto-micro`, `reference-tools/richtext/review`, the entrypoint comment, `bin/kloc`'s data path, and AGENTS.md's layout table rows for the three projects. | **Golden vectors green**: `rails_compat.json`, `campfire_sessions.json`, `campfire_routes.json`, `ruby_core.json` (strings and floats; the retired groups named in the Phase log), `storage.json` filenames; `Campfire.Rails.Tests` count = the three old projects' counts minus the retired facts, each retired fact named; `git grep -n -e 'Campfire\.RailsCompat' -e 'Campfire\.Ruby\b' -e 'Campfire\.Routes'` over `src tests bin bench/crypto-micro bench/lib reference-tools/richtext AGENTS.md parity/docker` finds nothing; `bin/kloc` prints the total with and without C2's move once; `dotnet build bench/crypto-micro -c Release` and `reference-tools/richtext/review` build; image rebuilt; **`bin/cookie-roundtrip` 20/20**; `bin/pixel-parity --fast --only 'auth/**,rooms/show/designers,search/**,users/profile/**'` every seed (URL escaping: the mailto links, pagination, the sign-in redirect's `email_address`) |
| 9.3 The static rows | C5: `HttpLog.logged`, `StaticAssets.isAssetRoute`, `UseWhen` in `pipeline`. LS1-LS5. `HttpLogTests` LG4 moves off `/up` to a logged path. `bench/lib/logcount` moves off `/up` (to `/session/new`, logged and cheap) and still counts every line. README: the request log's class B line. | LS1-LS5, LG1-LG4, SR1-SR8, RD1-RD7 green; `bin/asset-check` (colima) all pass (1,806 today; the asset endpoints' answers must not change); `bench/lib/logcount 20000` OK; image rebuilt; `--fast --only 'rooms/show/designers,users/avatar*,account/edit*'` every seed; **`bench/quick --routes avatar,static_css,up,room_show,post_message` c=1 and c=16, 3 reps interleaved against `p9ctl`**, recording each static row against Rails' stored medians (`static_css` 15,167 / 98,776, avatar 12,838 / 69,481, `up` 2,101 / 4,229). **Target: `static_css` and avatar at or above 1.0x Rails at both concurrencies.** If a row misses it, record the measured rows and stop: no other change is made in this unit. The page and post rows within the control's range. |
| **checkpoint** | Opus, after 9.3 (below) | |
| 9.4 Post c=1 CPU and the bench's buckets | C9 first: `layers.py`/`perf_layers.py` buckets. Then the diagnosis: `bench/breakdown perf` on post a message at c=1, new (HEAD) against `p8ctl`, read with the new buckets. Then C6, the `ReadAsync` readers. CH1-CH3. If the breakdown shows another cause the fix does not touch, record it, fix it only if it is cheap (under about 30 lines, no new mechanism), and otherwise record why it stays. | CH1-CH3, JB1-JB6, the `PushDeliveries` pool facts (50 running, 10,000 queued, drops logged, stop right after start) and WP8 green; `bench/lib/logcount 20000` (the SIGTERM drain) OK; `git grep -n 'Campfire\.Kit' bench/lib` finds nothing; each bucket matches at least one frame of the new breakdown's profile, and "other" is no larger than in the last recorded fsharp profile; image rebuilt; `--fast --only 'bot_api/**'` every seed and `realtime/message_sent` alone; **`bench/quick --routes post_message` c=1 and c=16, 5 reps interleaved, against `p8ctl` and `p9ctl`**: CPU per post at c=1 within 2% of `p8ctl`'s median, or the gap's cause recorded with the breakdown's numbers; then remove `p8ctl` |
| 9.5 Tests | C8: tests of deleted code already gone (check); the renames (`LogCapture`, `Test.Wiring`, the controller-group names, `SeamTests` folded in); the IDs in the 38 names; L5 and L7 made deterministic; `tests/Shared` keeps only what more than one project uses. | `dotnet test --solution CampfireFs.slnx --list-tests` before and after, saved to the unit's evidence: every test missing after a rename appears under its new name, and every removed one is named in the Phase log with its reason; `--list-tests` finds each of CL1, GD1-GD4, OG1-OG4, SR-J1, SR-J2, SR-J5, WP1-WP8, RD1-RD7, OP1-OP8, BB1-BB3; `git grep -n -e CapturingLoggers -e '\.Booted\b'` over `tests` finds nothing; `bin/verify` run three times in a row green (L5/L7 under the parallel run); `bin/linux-tests App` green (HS2, HS4, THP ran; AC11 skipped without Pebble) because `Support` changed; no `src` change |
| 9.6 README | C10: README's intro, status, Running it (`campfire-dotnetway:app`), Performance (the Phase 8 gate table against Rails' stored medians, marked as replaced by the final bench), **Known differences** rebuilt from every design note's section 8 (`design-data-ef`, `design-kit-auth`, `design-cable`, `design-views`, `design-richtext`, `design-assets`, `design-front`, `design-jobs`, this note) and every Phase log "Differences recorded" line, and **How this was built**. | A checklist in the unit's evidence: each class A and B line of the sources, with the README line that carries it (none dropped; a line that no longer holds is removed with its reason in the Phase log); every class B line names a test that exists, checked by a throwaway script in the evidence that pulls each named test from the README and greps `tests/` (0 missing); the Rust README is no longer needed to read the list; the markdown renders (headings, tables) |
| 9.7 Delete and clean up | The leftovers: `git grep` over `src tests bin bench/lib parity/docker reference-tools AGENTS.md README.md plans/dotnetway.md` (not the Phase log) for `FileServer`, `PartsStream`, `Rack\.`, `TempDir`, `JsonValue`, `RailsCompat`, `Campfire\.Ruby\b`, `Campfire\.Routes`, `CapturingLoggers`, `Booted`, `Campfire\.Kit`, `WaitToReadAsync`; AGENTS.md as theDotNetWay's guide (the seven-project table, the port's withdrawn rules removed: "match Rust's implementation", the UTF-8 writer performance rules, "project references follow the crate graph"; each tool section kept); `plans/dotnetway.md`'s status line and section 6's ".NET 10" line pointed at the decisions that replaced them; the open-items ledger (section 9's table, appended to the Phase log as "Phase 9: open items"); the build outputs of the three removed projects under `src/*/obj` and `bin` (untracked) deleted; `bin/kloc` final. | The grep finds only history; `bin/verify` green; image rebuilt from HEAD (label checked) for the gate; `--fast --only 'rooms/show/designers'` every seed; `bin/cookie-roundtrip` 20/20; `bin/kloc` recorded against section 7; `docker images 'campfire-dotnetway:*'` lists only `app` and `p9ctl` |

Then the separate steps, not units: the full pixel gate, every seed and the full matrix (it re-checks the Firefox empty-404 pair and the
WebKit WebP avatar cell); `bin/cookie-roundtrip`; `bin/db-differential`; the final bench (the "Campfire, six ways" table); the security
review (it includes C3's range handling and C5's log scope); the landing page; the independent verification.

**The checkpoint after 9.3** (Opus) reviews 9.1-9.3 against this note before the readers, tests and docs:
- C3's list: FR1's 95 cases are the five groups, the list fails both ways, and no well-formed single range differs.
- No unit wrote a header with CR/LF through the framework. The odd-disposition fact still shows Puma's two header lines and drops the
  control characters.
- The merge was behaviour-free: commit 2's diff is renames, project files and the AGENTS.md rows only (`git diff --stat -M`, plus a read of
  every hunk that is not a rename).
- C5: what is not logged is exactly the three families. A page under `/assets/` that is not a table route (a 404) is logged; an avatar 500
  is logged by `PublicExceptions`; the HTTP logging middleware is not in the asset path (LS4).
- The static rows against Rails, and whether 9.3 met its target.
- Whether 9.4's diagnosis needs more than the readers (read the breakdown plan before it runs).

## 5. Tests

In `tests/Campfire.App.Tests` unless named. They run on `Support`'s boot through `Boot.build`, or on a minimal host where named.

- **File results (9.1):**
  - FR1: the 200 `ruby_core.json` `byte_ranges` vectors through `ActiveStorage.fileResult`, behind a loopback host with one route (files of
    the vector's size). Each answer read as Rack's value (200 = `nil`, 416 = `[]`, 206 = `[[from, to]]`) equals Ruby's, except the cases in
    `byte-ranges-known-differences.json`. Those must differ as listed. A listed case that stops differing fails, and so does an unlisted
    case that starts.
  - FR2: disk `show` through the app: whole file 200 with `content-type`, `content-disposition`, `cache-control: max-age=3600, public`,
    `last-modified`, `content-length`; HEAD has no body; one range 206 with `content-range`; past the end 416 with `content-range: bytes */N`;
    `If-Modified-Since` at the file's date 304; a missing file 404; a bad key 404.
  - FR3: proxy with a range: one range 206 with Rails' `content-disposition` and `content-transfer-encoding`; several ranges 200 with the
    whole file (C3 (a)); an unreadable header 200 (C3 (c)).
  - FR4: a 0-byte blob: no range 200 empty; with `Range: bytes=0-` 416 (C3 (b)).
  - FR5: `TempFile` creates a 0700 directory and a 0600 file, the file name ends in the suffix, and dispose removes both, even when the
    file was replaced (ffmpeg writes to the path).
  - FR6: `Value.Set`/`Merge` keep Ruby's hash order (the old `JsonValue` facts, moved).
- **Request log scope (9.3)**, over `LogCapture` with `LOG_REQUESTS` on:
  - LS1: a page, a post, a 404 under `/assets/` that is not a table route, and a bot call each log one line.
  - LS2: a fingerprinted asset, a public file (`/robots.txt`), an avatar hit and miss, and `/up` log none.
  - LS3: an avatar request whose handler throws logs `PublicExceptions`' line with status 500.
  - LS4: the HTTP logging middleware does not run for an asset request (an `IHttpLoggingInterceptor` registered by the test sees no call
    for it, and one call for a page).
  - LS5: `HttpLog.logged` answers false before `StaticAssets.map` has loaded only for `/up` and avatars (no asset route is known yet), and
    nothing throws.
- **Channels (9.4):**
  - CH1: with 50 readers waiting on `PushDeliveries`, one queued notification is delivered once. A test hook counts reader wake-ups: at
    most 2 for 1,000 notifications queued 1 ms apart, where the old loop woke 50 per notification.
  - CH2: closing the queue with 10,000 waiting delivers all of them before the readers end, and each reader ends exactly once (no
    `ChannelClosedException` escapes).
  - CH3: the job workers drain a closed queue the same way (JB5 extended).
- **Ported or renamed:** every fact of `FileServerTests` that still has a subject becomes FR2/FR3; `RubyCoreTests`' `byte_ranges` group
  becomes FR1; the `to_f` group and the dead-writer facts are retired (named in the Phase log); `MiscTests`' "byte ranges are not read into
  memory" goes with `PartsStream`; `HttpLogTests` LG4 off `/up`.

## 6. What is deleted

| What | Lines (corrected `bin/kloc`) | Unit |
|---|---:|---|
| `Storage/FileServer.fs` | 94 | 9.1 |
| `Ruby/Rack.fs` | 93 | 9.1 |
| `ActiveStorage.fs`' `PartsStream`, `partsBody`, `partsLen`; the multipart and Rack branches of `Controllers/ActiveStorage.fs` (net of `fileResult`) | about 110 | 9.1 |
| `Storage/TempFile.fs`' own name generator and `TempDir` (net) | about 35 | 9.1 |
| `Storage/JsonValue.fs` (net of the members it becomes) | about 20 | 9.1 |
| Storage's `ResultBuilder` | about 40 | 9.1 |
| `Ruby/Float.fs`' `toF` and its strtod port | about 265 | 9.2 |
| `Ruby/Erb.fs`' three dead writers | about 53 | 9.2 |
| `Ruby/Uri.fs` (net of two lines in `Ruby.fs`) | about 13 | 9.2 |
| `ContentDispositionApproximations.fs` out of the count (moved to static data, not deleted) | 195 | 9.2 |
| The three project files and three test projects (merged) | 0 | 9.2 |
| Tests: `FileServerTests` (9 facts), Rack and `to_f` facts, the writer facts, the parts-stream fact | not counted | 9.1, 9.2 |
| `bench/lib/layers.py`, `perf_layers.py` Kit buckets | not counted | 9.4 |
| Not deleted (the plan expected them to go): the json gem writer (C1), `ContentDisposition.format` (C2), `Process.fs` (C4) | | |

## 7. KLOC projection

`bin/kloc` at `8f72e40` (as at `c8cce8f`): **27,114 corrected** (26,667 cloc); App 12,329, Views 5,219, Db 2,455, RichText 2,268, Storage
1,646, RailsCompat 1,433, Cable 1,051, Ruby 631, Routes 82.

| | lines |
|---|---:|
| Storage: `FileServer` -94, `TempFile` -35, `JsonValue` -20, `ResultBuilder` -40, dead code about -10 | -199 |
| App: parts stream and range branches -110, `fileResult` +15, `logged`/`isAssetRoute`/`UseWhen` +15, readers 0 | -80 |
| Ruby: `Rack` -93, `toF` -265, `Erb` writers -53, `Uri` -13 | -424 |
| RailsCompat: the I18n table to static data -195, dead code about -15 | -210 |
| **Phase 9 net** | **about -910** |

Project: **27.1 -> about 26.2 KLOC** (range 26.0-26.4). That is 26.4 without C2's move, which `bin/kloc` prints once beside it.
`Campfire.Rails` is about 1.5 KLOC (Ruby 0.21, the Rails formats 1.22, routes 0.08) against the plan's 1.1. Storage is about 1.45 against
1.3. The plan's row is 18.5 -> 17.0: the base drifted by 8.6, mostly the parity-forced code later phases kept (the user-agent and QR ports,
Thruster's ACME and listener branches, the boot, options and logging code, the templates). Rust's bar is 31.0. Rails is 5.5.

## 8. Known differences this phase records (README "Known differences", plan section 4)

- **Class B:**
  - Active Storage serving is the framework's file result (FR1-FR4), so five kinds of `Range` request answer differently from Rack and
    Active Storage. Several ranges get 200 with the whole file, where Rack sent `multipart/byteranges`. A range on an empty file gets 416,
    where Rack served it whole. A header Rack's lenient parsing read (`bytes=1_0-2_0`, `bytes=+1-2`, `bytes= 1-2`, two `bytes=` parts) gets
    the whole file, and on the proxy that replaces Rails' 416. Positions past 64 bits get the whole file. `Bytes=` and other units are
    honoured. The list is FR1's 95 cases. A disk 416 has an empty body, where Rack wrote "Byte range unsatisfiable". `If-Modified-Since`
    compares dates, where Rack compared the strings.
  - The request log has no line for fingerprinted assets, public files, avatars and `/up` (LS2), where Thruster logged each request.
    Rails' own log silences `/up` too. Errors on those paths are still logged (LS3), and the framework's meters count every request.
- **Class A, once:** `Accept-Ranges: bytes` on the disk service's answers.
- **Retired vector groups, not differences:** `ruby_core.json`'s `to_f` group (no code reads a float from a string) and the Rack
  `byte_ranges` group as a Rack port test (it is FR1 now). The vector file is unchanged.
- **Kept, no new difference:** the JSON text (C1), `Content-Disposition` (C2), URL escaping (S5: identical), temporary files (not
  observable), the job and push behaviour (C6: the same order, limits and drain).

## 9. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | The framework's ranges break media playback in a browser (WebKit's and Firefox's video and lightbox cells) | 9.1's `interactions/lightbox*` and attachment slices; FR2/FR3 single ranges equal; the full gate's three engines after the units |
| 2 | A header with CR/LF reaches Kestrel and throws (the odd-disposition case) | "proxied blobs write odd dispositions as puma does" runs through the new path in 9.1; the checkpoint reads it |
| 3 | The merge's name resolution (S7's `Error` shadowing, a `Routes`/`Ruby` namespace-or-module ambiguity) | C7's mapping; build after the namespace rewrite before anything else; a failing build is fixed by the mapping, never by a type annotation |
| 4 | `UseWhen`'s predicate drops a line operators need, or keeps the middleware in the asset path | LS1-LS4; the checkpoint; `bench/quick`'s `static_css` row (if it does not move, the middleware is still there) |
| 5 | The `ReadAsync` loop changes drain or stop order (a delivery lost at shutdown, a reader that never ends) | CH2, CH3, JB5, the pool's stop facts, `bench/lib/logcount`'s SIGTERM run |
| 6 | The c=1 gap has another cause besides the herd | 9.4 diagnoses with the breakdown before it changes code, and measures against `p8ctl`; it records what it finds either way |
| 7 | Renamed tests silently lost | `--list-tests` before and after in 9.5's evidence; the 38 IDs |
| 8 | The README names tests that do not exist, or drops a recorded difference | 9.6's checklist and script |
| 9 | C2's counting move read as gaming | `bin/kloc` prints both totals; the table's header cites upstream's rule and the shared generator |
| 10 | Noise on the bench host (other sessions on the Mac) | same-session controls, interleaved reps, the host probe; a rep started above a probe ratio of 1.10 is re-run |
| 11 | Disk: an image rebuild in 9.1-9.4 and 9.7, the toolchain image for `bin/storage-media-vectors`, the controls | `df -h /` before each; `bin/clean-docker`; `p8ctl` removed after 9.4 |

Open items carried into this phase, and where each ends. 9.7 appends this ledger to the Phase log with its evidence.

| Item (from the Phase 8 verifier) | Ends |
|---|---|
| Static rows below Rails | 9.3 |
| Post c=1 CPU +3-5% | 9.4 |
| `layers.py`/`perf_layers.py` Kit buckets | 9.4 |
| Cable L5/L7 under load | 9.5 |
| Stale control images | 9.4 removes `p8ctl`. The older tags (`p7gate`, `p7pre`, `p6ctl`, `p5ctl`, `control-8abdb6b`, `before-net11`) are already gone from colima (`docker images`, 2026-10-27). `campfire-candidate-p6ctl` is not a `campfire-dotnetway:*` image and is left to the orchestrator |
| Firefox empty-404 pair | Fixed in Phase 4's verification (`49e1e09`, `Compression.compressEmptyErrors`, RespondTests X4b); later lists carried it by mistake. The full gate's Firefox cells re-check it |
| WebKit WebP avatar flake | A harness flake; the full gate reports it as it finds it |
| The 16 KB Cable bound | Closed by the decision of 2026-10-21 (restated as "Cable's own part under 5 KB, the whole under 20 KB", L7) |
| The two test packages | Closed: adopted in Phase 8 (8.1) |
| `CpuTimer` flake | Closed in 8.7a (thread CPU time) |
| S4-S6 of Phase 8's security review | Accepted or informational, recorded there |
| Q1 of Phase 5 (report AngleSharp's defects upstream) | Waits on Chris (section 10) |
| F6 of Phase 5 (replay the harness's outputs in real browsers in the parity image), fuzz 125/177/325, the corpus's `filtered` field | Open, outside this phase's units; the verifier decides whether the gate needs them |
| The operator's Let's Encrypt staging run | Human-gated, before a release that touches `Web/Host.fs` or `Acme.fs` |

## 10. Questions for Chris

One, carried and not blocking: **Q1 of Phase 5.** Should the AngleSharp 1.8.4 defects (a `NullReferenceException` in select mode, and the
quadratic DOM calls AGENTS.md lists) be reported upstream as issues under your account, opening with an AI-agent disclosure line? Options:
(1) yes, after you have read the drafts; (2) no, the README entry is enough.

Settled by D9, the vectors and the spikes, and recorded above:
- The json gem writer stays (C1, S4: the vectors and stored data).
- `Content-Disposition` stays, and its generated table is counted as static data (C2: the vectors, upstream's rule).
- Ranges on the framework, with the vector list as the test (C3, S3).
- Temp files, the result builder and JSON helpers on the framework or FsToolkit (C4).
- The log scope (C5, S2: what the static rows cost is the middleware, not the line; `UseWhen` is the framework's way; the observability
  cost recorded).
- The channel readers (C6, S1).
- The project and namespaces (C7, S7).
- The test consolidation, without a second boot path (C8).
