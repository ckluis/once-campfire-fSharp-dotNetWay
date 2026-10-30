# theDotNetWay: Campfire the .NET way

An F# implementation of [ONCE Campfire](https://github.com/basecamp/once-campfire) written the way a .NET team would write it:
ASP.NET Core for the web layer, EF Core over SQLite for data, Falco and Falco.Markup for routing and views, AngleSharp for HTML,
Kestrel for the front server and `BackgroundService`s for jobs, with hand-written code only where parity with Rails demands it. It is
a drop-in replacement for the Rails app: the same SQLite database, storage layout and signed or encrypted cookies, the same pages
and the same behavior. It is the third Campfire in F#, a fork of the published F# port (`ckluis/once-campfire-fSharp`), from which
it replaced one layer at a time behind a pixel-parity harness.

**Project page: [ckluis.github.io/once-campfire-fSharp-dotNetWay](https://ckluis.github.io/once-campfire-fSharp-dotNetWay/)**, with
the six-ways table and every number linked to the file it came from. Its siblings are the
[F# port](https://github.com/ckluis/once-campfire-fSharp) (the same work as the Rust port) and
[onCrack](https://github.com/ckluis/once-campfire-fSharp-onCrack) (F# with every speed trick allowed).

**Status: built and verified, with its known differences listed below.** Phases 0 to 10 are closed and recorded in
[`plans/dotnetway.md`](plans/dotnetway.md). At the final full gate (Phase 10) **4,918 of 4,918 captured cells** (five seeds, three
browsers, four viewports, both themes, a breakpoint sweep) were equal to Rails on screenshot, accessibility tree and loose DOM, the
cookie round trip passed both ways (20 of 20), and the backend is **26.5 KLOC** by upstream's counting rules (corrected; 26.0 as
cloc prints it), against 31.0 for the Rust port and 5.5 for Rails. Every benchmarked workload runs at least 5 times Rails'
throughput, both on `bench/quick` against the pinned Rails and on Basecamp's own harness against current Rails in the same session
(tables below).

The Rails app it reproduces is pinned in `reference/`. The Rust port (pinned in `rust/`) and the earlier F# port supply the parity
harness, the golden vectors and the benchmark tooling this repository reuses under the MIT license. The Rails frontend ships with a
few [port-owned overrides](src/Campfire.App/Assets/OVERRIDES.md): three JavaScript files and one image that deliberately differ
from Rails.

## Running it

With [ONCE](https://github.com/basecamp/once), or Docker directly (the image is a drop-in for the reference's: uid 1000, `/rails`,
`/rails/storage/{db,files,backups}`, ports 80 and 443, the ONCE hooks). Build the image from a checkout with its submodules, then
run it:

```sh
git submodule update --init
docker build -t campfire-dotnetway:app --build-arg APP_VERSION=... --build-arg GIT_REVISION=... .
docker run -d -p 80:80 -p 443:443 \
  -e SECRET_KEY_BASE=... -e VAPID_PUBLIC_KEY=... -e VAPID_PRIVATE_KEY=... \
  -e TLS_DOMAIN=chat.example.com \
  -v campfire:/rails/storage \
  campfire-dotnetway:app
```

`TLS_DOMAIN` enables automatic Let's Encrypt certificates (HTTP-01, so port 80 must be reachable); `DISABLE_SSL` enables plain HTTP.
The settings are in [`Config.fs`](src/Campfire.App/Config.fs) (the app's) and
[`Web/FrontSettings.fs`](src/Campfire.App/Web/FrontSettings.fs) (the listeners'). For local development,
`dotnet run --project src/Campfire.App -- server` with `SECRET_KEY_BASE` (or `SECRET_KEY_BASE_DUMMY=1`) set and libvips installed
(`HTTP_PORT=3000` avoids port 80).

## Performance

Measured in a Linux VM (colima, 8 vCPUs) on an Apple M4, two ways.

**Basecamp's harness, current Rails, the same sessions** (the floor since Phase 10). Upstream's `once-campfire-verification` (its
`bin/benchmark`, a local adapter branch) ran six Campfires in each of four sessions, 3 rounds of 8 s at 16 clients, the app on 4 CPUs and
the load generator on 4, every response checked against the seed's contract and every post audited in the database. Pooled medians
over 6 rounds, requests per second, theDotNetWay at `75487b7` ([`bench/results/dotnetway-six-ways/`](bench/results/dotnetway-six-ways/),
with every app's column, commit and range):

| HTTP workload | Rails | theDotNetWay | x Rails | with a writer at 10 posts/s: Rails | theDotNetWay | x Rails |
|---|---:|---:|---:|---:|---:|---:|
| Room page | 4,944 | 34,989 | 7.08x | 2,800 | 32,886 | 11.75x |
| Messages page | 4,953 | 33,831 | 6.83x | 3,461 | 33,012 | 9.54x |
| Sidebar | 5,425 | 41,581 | 7.66x | 4,267 | 40,246 | 9.43x |
| Search | 5,280 | 41,325 | 7.83x | 4,084 | 39,610 | 9.70x |
| Post a message | 401 | 2,901 | 7.24x | | | |

The messages page is the thinnest cell there (6.83x). onCrack, the C port and upstream's Rust port are faster than theDotNetWay in every
row, and the F# port in five of the nine; this build gives up speed for the framework's way and does not claim the others'. The
security review's two fixes (`d89fdd2`, `3a6c4f8`) were re-measured on the same harness at `fed00c4`: every route within 3.3% of the
Phase 10 gate's run and at least 5x that session's Rails ([`bench/results/security-p9p10/`](bench/results/security-p9p10/)).

**`bench/quick` against the pinned Rails' stored medians** (the floor through Phase 9;
[`bench/results/phase7-final.md`](bench/results/phase7-final.md)), 3 repetitions interleaved with a control build of the Phase 9 gate.
Every response was 2xx, every post's row was counted and the decoded bodies equal the stored sizes. The Phase 10 gate
([`plans/dotnetway.md`](plans/dotnetway.md), "Phase 10: gate", evidence in [`bench/results/dotnetway-p10/`](bench/results/dotnetway-p10/)),
requests per second at 16 and at 1 concurrent clients:

| HTTP workload | c | Rails | theDotNetWay | x Rails | CPU per request |
|---|---:|---:|---:|---:|---:|
| Room page | 16 | 239 | 33,418 | 139.8x | 97 us |
| Room page | 1 | 107 | 6,438 | 60.2x | 269 us |
| Messages page | 16 | 381 | 31,425 | 82.5x | 102 us |
| Messages page | 1 | 218 | 6,268 | 28.8x | 271 us |
| Sidebar | 16 | 599 | 39,313 | 65.6x | 79 us |
| Sidebar | 1 | 287 | 7,265 | 25.3x | 248 us |
| Search | 16 | 408 | 37,663 | 92.3x | 82 us |
| Search | 1 | 193 | 7,342 | 38.0x | 250 us |
| Post a message | 16 | 330 | 2,464 | 7.47x | 1,314 us |
| Post a message | 1 | 167 | 1,247 | 7.47x | 1,524 us |
| Static stylesheet | 16 | 98,776 | 109,206 | 1.11x | 26 us |
| Static stylesheet | 1 | 15,167 | 18,476 | 1.22x | 132 us |
| Avatar | 16 | 69,481 | 117,493 | 1.69x | 22 us |
| Avatar | 1 | 12,838 | 20,940 | 1.63x | 114 us |
| `/up` | 16 | 4,229 | 96,697 | 22.9x | 30 us |
| `/up` | 1 | 2,101 | 16,862 | 8.0x | 131 us |

The pinned Rails serves a room page at 239 requests a second; current Rails in Basecamp's harness, with its page cache, serves about
4,900, which is why the second table's ratios are so much larger than the first's. The page rows are the response cache below; post a
message is the thinnest cell here (7.47x). The floor is 5x Rails per workload on the five workloads; the static rows are there to show
the request log's cost, not to meet a floor.

### Response cache

Upstream's current Rails and Rust apps keep a response cache for pages in the Once Campfire benchmark harness (the pinned Rust
port in `rust/` and the F# port do not), so this one does too (`Web/PageCache.fs`, on `HybridCache`, inside the actions where upstream's `around_action :cache_read_response` sits). It
keeps the room page, the messages page, the sidebar and the search page, as the gzip or Brotli bytes that went out, for signed-in
`GET` requests that carry no flash and no conditional header. The key is a SHA-256 of the commit epoch, the action, scheme, host,
path and query, `Accept`, `Turbo-Frame`, `X-Requested-With`, the content encoding, `User-Agent`, the user id, the session id and the
`last_room` cookie; `Referer` is not in it (a test shows the four pages ignore it). Authorization is fresh on every request: a hit
runs the before-actions and `set_room` first (the session by token, its user, the membership and the room), so a revoked session or a
removed membership takes effect on the next request. Nothing carrying `Set-Cookie` is stored.

Every commit this process makes bumps one process-wide epoch, which retires every cached page at once. That is the cost of a global
epoch, here as upstream: every Cable presence commit and every hourly session-resume commit retires every page, so on a busy install
the hit rate follows the write rate, not the page. It costs the benchmark nothing (its mixed profile posts ten messages a second and
every read still clears 5x Rails), and it is the price of never serving a page from before a commit. An entry lives at most 60
seconds, and a page over 1 MB is not kept. `CAMPFIRE_RESPONSE_CACHE_MB` (default 64, as upstream) is the budget, added to the
fragment cache's; `0` turns the page cache off.

## Development

```sh
git submodule update --init
CAMPFIRE_REQUIRE_SEED=1 bin/verify   # Release build with warnings as errors, every test, the markup lint and its self-test, the guards
```

See [`AGENTS.md`](AGENTS.md) for the layout and working rules, and [`plans/dotnetway.md`](plans/dotnetway.md) for the plan, the
Phase log and the design notes (`plans/design-*.md`).

## Known differences

This list is self-contained: it needs no other document to read. Each difference cites the Rails file it departs from where there is
one, and each class B line names the test or check that pins it (`tests/` holds all of the tests; a name in quotes is the name of
a fact). Three classes decide what may differ.

**Class C: never acceptable, so never listed.** Any pixel difference in a captured cell, any accessibility-tree difference, a
live-DOM difference that survives the harness's loose normalization (a lost `data-*`, `id`, Turbo or Stimulus hook), Cable frames
that decode to different JSON or arrive in another order on a subscription, data Rails cannot read or reads differently, a user
signed out by a switch between Rails and this app (either way), a different status code on a non-hostile request, and any
security regression (XSS, CSRF, SSRF, sanitizer bypass). These are fixed before a phase closes; none is known.

**Class A: byte-level, invisible.** Serialization and transport details that change no DOM tree, pixel, accessibility tree or
decoded datum. They are stated once, here, and not repeated below.
- **Markup bytes.** Attribute values and text are encoded by `Core/Html.fs`: every non-ASCII character as a numeric reference
  (`&#233;`, `&#128512;`) and `'` as `&#39;`. Void elements end ` />`. There is no indentation or whitespace between elements that
  the ERB does not render, the frame layout is not indented, and attribute order follows each template's list. An empty
  `rooms/refreshes/show` is an empty body (Erubi wrote a newline). A page is therefore a few percent shorter than Rails' (the room
  page 376,570 bytes against Rust's 416,122) and every `Content-Length` changes with it (`bench/lib/expected-sizes.json`).
- **Rich text.** `<` and `>` inside attribute values are written as themselves, as Nokogiri writes them; the trees are equal.
- **Cookies and headers.** Attribute order and case of cookies written through `Response.Cookies` (`last_room`); `Cache-Control`
  directive order; no body `ETag` on pages that do not call `fresh_when`; `Vary: Accept-Encoding` on every compressible response,
  also to a client that sent no `Accept-Encoding`; gzip and Brotli bytes (the decoded bytes are Rails' and Rust's); the header
  order and `Host` spelling the framework writes.
- **Cable frames.** JSON inside frames writes `<`, `>` and `&` as themselves (Rails writes `<`); payloads built from anonymous
  records keep their keys in alphabetical order, which is Rails' order for every payload Campfire sends; the `101 Switching
  Protocols` is written by Kestrel (`Connection: Upgrade`, plus `Date` and `Server`); the heartbeat starts at boot, not with the
  first connection; WebSockets run over HTTP/2 where the browser chooses it under TLS.
- **Assets.** URLs are `name.<fingerprint>.ext` (the SDK's ten-character form) where Propshaft writes `name-<digest>.ext`. A CSS
  `url()` or `@import` and a JavaScript `sourceMappingURL` are not rewritten and fetch the plain URL of the same file (the three
  `lexxy-*.css` files are fetched twice per page for it). Assets carry an `ETag`, a per-file `Last-Modified` and
  `Accept-Ranges: bytes`; text assets are precompressed (Brotli or gzip by `Accept-Encoding`) where Rails' front server compressed
  them on the fly, and images are not compressed. The import map's JSON is written by `ImportMapDefinition` (the same text after
  the digest mask) and the preload `Link` header holds 15 stylesheets where Rails' holds 16 (the same 1,000-byte rule, a different
  byte count per link). `Vary` is on text types only.
- **Storage.** The disk service's answers carry `Accept-Ranges: bytes`.
- **Front server.** `X-Request-Start` and the front's `X-Forwarded-*` no longer reach the app (the remote IP is unchanged); on a
  response-cache hit `X-Request-Id` and `X-Runtime` are the hit's own; a cached avatar, logo or QR code is served to anyone who has
  its URL, as Thruster served it.
- **Integrations.** The request target is `System.Uri`'s normalization (lower-case host, dot segments removed); the push payload's
  record size (4096) and padding, the VAPID JWT's bytes and `Authorization: vapid t=..., k=...` (a space after the comma), the
  library's header spelling and order; push requests carry no `Accept` or `User-Agent: Ruby` (push services read neither); pooled
  push connections.

**Class B: behavior visible to an operator, an integration or an edge user.** Listed by layer, each with its test.

### Application (`Campfire.App`: the request layer, controllers, sessions)

- **Forgery protection** is .NET 11's token-less `ICsrfProtection` (`Web/Csrf.fs`), called where Rails runs
  `verify_authenticity_token`: `Sec-Fetch-Site` first (`same-origin` and `none` pass; `cross-site` and **`same-site`**, another
  subdomain, are 422), then `Origin` against the request's scheme and host for a browser without Fetch Metadata. A write with
  neither header (not a browser) passes, over HTTPS too. Pages carry no tokens, so open tabs keep working across a switch from
  Rails; after a switch back to Rails a tab this app rendered needs a reload. Bot-key requests skip the check, as in Rails.
  Reference: `ActionController::RequestForgeryProtection`. Tested by `CsrfTests` (C1-C13).
- **The flash and `return_to_after_authenticating`** live in ASP.NET Core's session (server memory, cookie `_campfire_flash`), not in
  Rails' encrypted `_campfire_session`, which is neither read nor written (it stays in the browser untouched, so a switch back to
  Rails keeps it). A restart, or a switch either way in the middle of a redirect, loses a pending notice or return-to URL (sign-in
  then lands on the root). A request with no session cookie never loads the session. Reference:
  `config/initializers/session_store.rb`. Tested by `FlashTests` (F1-F8).
- **Proxy headers** are the framework's `UseForwardedHeaders` (`Web/Services.fs`): `X-Forwarded-For`, `-Proto` and `-Host` are
  honoured from Rails' `TRUSTED_PROXIES` ranges only, walked from the right with no forward limit, so a public peer's
  `X-Forwarded-For` is ignored where Rails believes it. `Forwarded`, `X-Forwarded-Ssl`, `X-Forwarded-Scheme` and `Client-Ip` are not
  read at all (so there is no `IpSpoofAttackError`, a 500 in Rails). `assume_ssl` (unless `DISABLE_SSL`) is a one-line middleware
  that makes every request `https`. Reference: `ActionDispatch::RemoteIp`, `ActionDispatch::AssumeSSL`. Tested by `RequestsTests`
  (R1, R2).
- **The method override** is `UseHttpMethodOverride` on the form field `_method` only: `X-HTTP-Method-Override` is not read, a POST
  without a content type is not a form, and a `_method` that is not an HTTP verb makes the request that verb (a 404, where Rails
  leaves it a POST). Reference: `Rack::MethodOverride`. Tested by `RequestsTests` (R3, R3b).
- **Params** are the framework's readers (route values, `Request.Form`, the query string, a JSON body) in Rails' order (path over
  body over query, the last repeated key wins). A form is read once, by the framework: a stray `%` in a form is taken literally
  where Rack answers 400, the form's raw bytes are not kept, uploads are the framework's `IFormFile`s copied to a temp file, and
  pairs that interleave other keys are grouped by name. Not reproduced: Rack's type-conflict errors (`a=1&a[]=2`) and its nesting
  depth limit. Reference: `ActionDispatch::ParamBuilder`, `Rack::QueryParser`. Tested by `ParamsTests` (P1-P4).
- **A malformed multipart body** is judged by the framework's form reader: one cut short before its closing boundary, or sent
  without a boundary, is an empty 400 as in Rails; other shapes Rack refuses may be read, or refused with another status.
  Reference: `Rack::Multipart::Parser`. Tested by `RequestsTests` (R9).
- **Format negotiation** parses the `Accept` header with the framework and keeps Rails' rules around it (`params[:format]`,
  browser-like lists ignored, path extension, `text/*` expansion, Turbo's type first). A `q` that Ruby's `to_f` reads and the
  framework does not (`q=abc`, `q=1e17`) leaves the item at 1.0, and `text/xml` is not folded into `application/xml`. Reference:
  `Mime::Type.parse`. Tested by `FormatsTests` "q values Ruby reads and the framework does not are class B".
- **Compression** is the framework's `UseResponseCompression` (Brotli, gzip and zstd; HTTPS included), not `Rack::Deflater`: only
  HTML, JSON, JavaScript, CSS, SVG, XML, plain text and Turbo's streams, nothing under 256 bytes except an empty error
  (`head :not_found`), which goes out as a few compressed bytes as Rails sends it, nothing the public files already serve
  precompressed. `Rack::Deflater` gzipped every body whatever its type or size and answered 406 to a client that refused every
  encoding. A browser that offers `br` keeps `br`; a client that takes only zstd gets zstd. `GZIP_COMPRESSION_ENABLED`,
  `GZIP_COMPRESSION_DISABLE_ON_AUTH` and `GZIP_COMPRESSION_JITTER` are not read and there is no BREACH jitter padding (Rails has
  none). A page of cached fragments is compressed once per encoding and kept by the digest of its parts. Reference: `config.ru`.
  Tested by `RespondTests` (X4, X4b), `HttpLogTests` (CZ1-CZ3) and `RoomsTests` "pages of messages go out in parts".
- **Conditional responses.** `send_file` answers a `Range` request with 206 or 416 (the framework's range processing); Rails'
  `send_file` sends the whole file. `redirect_to`, `fresh_when`, `stale?` and `expires_in` keep Rails' rules (unsafe-redirect
  checks, strict freshness, the ETag combination of the validator, a Turbo Frame and the flash) on the framework's typed
  `If-None-Match` and `If-Modified-Since`; a `fresh_when` ETag digests the same parts as Rails but writes the flash differently, so
  it is not byte-equal to Rails'. No page gets `Rack::ETag`'s body hash from a Web action. Reference:
  `ActionController::Redirecting`, `ConditionalGet`, `DataStreaming`. Tested by `RespondTests` (X1-X3, X5).
- **Sign-in throttling** (`rate_limit to: 10, within: 3.minutes` on `sessions#create`) is the framework's `PartitionedRateLimiter`, a
  fixed window per remote IP in server memory, acquired inside the action after the before-action chain. Its windows run on
  wall-clock time, not the frozen harness clock, and a restart empties them (Rails' entries are in the cache store). Reference:
  `SessionsController`. Tested by `SessionControllerTests` (N8).
- **Headers of the controllers on `HttpContext`** (sessions, first run, welcome, PWA, transfers, health, Turbo Native, Action
  Mailbox) carry Rails' default headers, a default `Content-Type` and, where they set none, `Cache-Control`
  `max-age=0, private, must-revalidate` for a 200 with a body and `no-cache` otherwise, set as the response starts; they carry no
  `ETag`. The two `ActionController::Live` controllers (avatars and logos) answer without Rails' default headers and with
  `Cache-Control: no-cache` where they set none. Tested by `SessionControllerTests` (N1, N2) and `AccountControllerTests` (J7).
- **A JSON `null` param** in the account and user controllers (accounts, bots, join codes, custom styles, logos, users, avatars,
  bans, profiles, sidebars, push subscriptions) is read as a missing key, where Rails keeps a nil value (`{"user":{"name":null}}`
  updates nothing here; Rails raises on the NOT NULL column; a nil `endpoint` in a push subscription is dropped from the lookup
  instead of matching `IS NULL`). A browser form cannot send nil, so no page of the app reaches it. Reference: `Accounts::*`,
  `Users::*`. Tested by `AccountControllerTests` (J1-J10).
- **The mention list** (`autocompletable/users`) reads a `filter` or `query` that is a hash or array as an empty string (Rails would
  search for the hash's `to_s`, which matches nothing either way). Reference: `app/controllers/autocompletable/users_controller.rb`.
  Tested by `LookupControllerTests` (K1).
- **A bot API request** (`/rooms/:room_id/:bot_key/messages...`) keeps the bytes of its body for `raw_request_body` before the method
  override reads a form body, so `curl -d 'Hello'` posts "Hello" as in Rails; no other route keeps raw bytes, and a multipart
  body's `raw_request_body` is empty. A JSON `null` for `message[attachment]` or the bot API's `attachment` is "not given" (Rails'
  `nil` removes the attachment); `""` removes it as in Rails. `involvement` given as a non-empty hash or array is a 500 (an empty
  one is blank, as in Rails). References: `app/controllers/concerns/raw_request_body.rb`,
  `app/controllers/messages/by_bots_controller.rb`. Tested by `MessagesTests` "the bot api" and `RoomControllerTests` (D5).
- **A request body is capped at 16 MiB**, files included, by Kestrel (413; Rails reads any size). Reference: `config/puma.rb`. Tested
  by `RequestsTests` (R4).
- **A request header with a byte outside visible ASCII** reaches the app as the framework hands it; only `Host` and `X-Request-Id`
  are read through the visible-ASCII rule, for the redirect and the request id. Tested by `RequestsTests` (R7).
- **A POST that `_method=head` turned into a HEAD** sends no body and announces no length; a real HEAD keeps the length of the body
  it did not send (`Rack::Head`). Tested by `RequestsTests` (R3b).
- **A request for a method a route does not have** is Rails' 404 page, with no `Allow` header (the route table is exact; Rails'
  `ActionController::RoutingError`). Tested by `RouteTableTests` "every row of the route table is a wrapped Web action" and
  `AppTests` "unknown and unported routes".
- **A response header whose value has a line break** (the `?disposition=` of a proxied blob, the one header the app builds from the
  request) keeps every line, and a line holding another control character is left out, as Puma writes it (puma 7.2.1); the Rust
  port keeps only the first line. Kestrel's `Append` would drop a lone empty first line, so `Web/Outgoing.fs` hands such a name's
  values over as one list. Tested by `AppTests` "proxied blobs write odd dispositions as puma does".
- **Route recognition** looks a path's routes up by its first segment and tries their regexes in table order. The answer is the same
  for every path of the generated corpus. Tested by `RouteTableTests` "recognizes like a first match scan".
- **`render json:` writes floats as the json gem does** (`1e+15`, `0.00000015`). Tested by `UnitTests` "floats match the json gem".
- **`GET /rooms/:room_id/refresh?since=`** clamps a `since` too large for the clock to the end of the .NET `DateTime` range; no row is
  newer than either end. Reference: `app/controllers/rooms/refreshes_controller.rb`. Tested by `RoomControllerTests` (D4).
- **Logs** are `Microsoft.Extensions.Logging`'s console lines (`RAILS_LOG_LEVEL` or `CAMPFIRE_LOG` set the level). The .NET GC is
  configured in the project file (server GC, tiered PGO); only the transparent-huge-page opt-out is kept, on Linux. Tested by
  `MiscTests` "transparent huge pages are disabled".
- **Redaction in the logs.** The bot key in a bot API path and a webhook URL in a failed delivery's log line are logged as a keyed
  hash (`Microsoft.Extensions.Compliance.Redaction`'s HMAC redactor, `<key id>:<base64>`, keyed from `secret_key_base`: stable
  within an install, different between installs), where Rails logs the path and the error as sent (only its parameters line is
  filtered). It covers the request line, the exception page's `path=` and the job's `error=`; a dropped event is logged by its
  name and not its fields. Tested by `RedactionTests` (RD1-RD7).
- **`VAPID_SUBJECT`** without a `TLS_DOMAIN` defaults to this project's URL. Tested by `OptionsTests` (OP1).

### Data (`Campfire.Db`)

- **The WAL checkpoint** is SQLite's auto-checkpoint on the committing connection, as in Rails: a commit that crosses 1,000 WAL pages
  pays the checkpoint. Tested by `EfTests` "D4 every pooled connection has Rails' pragmas and the counter equals the physical
  connections" (no pragma changes `wal_autocheckpoint`).
- **Boot adds `index_messages_on_room_id_and_created_at`** if it is missing. The index is additive, so Rails keeps working on the
  database. Tested by `SchemaTests` "prepare adds the room paging index to new and existing databases" and `SeedTests` "a database
  Rails wrote opens as up to date and gains only the paging index".
- **Hot read paths are Dapper** (`Ef/FastReads.fs`, the only file that calls it), parameters only, on the EF connection. The rows are
  Rails' and `FastReadsTests` compares each read with EF's answer; the one visible difference is that the member ids of direct
  rooms come in membership order where Rails' order is unspecified. Tested by `FastReadsTests` "dapper reads return the same
  records as EF for every row of the default seed".

### Storage (`Campfire.Storage`, Active Storage serving)

- **Files are served with the framework's file result** (`ActiveStorage.fileResult`, range processing on), not Rack's `Files`. Five
  kinds of `Range` request answer differently from Rails: several ranges get the whole file with 200, where Rack sent
  `multipart/byteranges`; a range on an empty file is 416, where Rack served it whole; a header Rack's lenient parsing reads
  (`bytes=1_0-2_0`, `bytes=+1-2`, `bytes=1-2-3`, `bytes=a-b`, two `bytes=` parts) gets the whole file, and on the proxy that
  replaces Rails' 416; `bytes= -5` (a space after the `=`) is the last five bytes, where Rack read bytes 0-5; a position past 64
  bits gets the whole file. `Bytes=` and other units are honoured. Single well-formed ranges (`bytes=0-`, `bytes=N-M`, `bytes=-N`)
  answer as Rack does: 105 of the 200 recorded cases. A disk 416 has an empty body, where Rack wrote "Byte range unsatisfiable",
  and `If-Modified-Since` compares dates, where Rack compared the strings. The 95 differing cases are listed, with their group, in
  `tests/Campfire.App.Tests/testdata/byte-ranges-known-differences.json`. Tested by `FileResultTests` (FR1 fails if a listed case
  stops differing, answers otherwise than the list records, or another case starts to differ; FR2-FR4 pin the disk service, the
  proxy and the empty file) and "a missing file is not answered as an empty 200".

### Assets (`Web/StaticAssets.fs`, `Importmap.fs`, the `CampfireStageWebRoot` build target)

References: Propshaft 1.2.1, importmap-rails 2.2.2, `ActionDispatch::Static`, `config/environments/production.rb`. Assets and the
five `public/` files are staged into the web root at build time and served by `MapStaticAssets` where Rails' `ActionDispatch::Static`
ran; the page's stylesheet tags, import map and `Link` header are made once at start from that table.
- **Cache headers** are the framework's: `max-age=31536000, immutable` on a fingerprinted URL and `no-cache` on a plain one
  (`/assets/<logical path>`, which Rails does not serve at all), against `public, max-age=2592000` on every public file.
  `/robots.txt` and the error pages are `no-cache` with an `ETag` too. Checked by `bin/asset-check` (its fingerprinted-URL and
  plain-URL checks, run in the Linux image) and, for the ETag, `AssetsTests` (H2).
- **An extensionless `/404`, `/422`, `/500` or `/502`** is the app's own 404 page, not the `.html` file with 200. Tested by
  `AssetsTests` (H5).
- **`/assets/.manifest.json` is not served**, nor is any dotfile, the build output (`/campfire.dll`, the endpoint manifest) or the web
  root by name (`/wwwroot/...`); encoded dots and backslashes are the app's 404. Tested by `AssetsTests` "nothing outside the asset
  table is served: encoded dots and backslashes, the build output, the web root by name".
- **A method other than GET or HEAD** on an asset URL is 405, where Rails' router answers 404. Tested by `AssetsTests` (H4).
- **Asset and public-file routes ignore letter case**: `/ASSETS/COMPOSER.<FINGERPRINT>.CSS`, `/Assets/Composer.css` and
  `/ROBOTS.TXT` answer the file, where Rails' `Static` (a case-sensitive file system) leaves them to the router's 404. A trailing
  slash (`/robots.txt/`) answers the file in both. Tested by `AssetsTests` (H9).
- **The vendored `lexxy.js.gz` and `lexxy.js.br`** are not served under their own names: `/assets/lexxy.js.gz` is the SDK's own
  precompressed route of `lexxy.js` and `/assets/lexxy.js.br` is the app's 404. Tested by `AssetsTests` (S1, H1).
- **Asset responses carry neither `x-request-id` nor `x-runtime`**, which Rails' `Static` also answers ahead of. Tested by
  `AssetsTests` (H3).
- Active Storage ranges are under Storage above.

### Front server (`Web/Host.fs`, `Acme.fs`, `OutputCaching.fs`, `HttpLog.fs`: Thruster's job on Kestrel and ASP.NET Core's middleware)

Every install is served by the app's own host (Kestrel's listeners and limits, `ConsoleLifetime`); Thruster 0.1.23 was the reference.
- **Kestrel always sends `Date`**, so the app's own listener on `TARGET_PORT` sends one, where Puma sends none. Tested by `HostTests`
  (HS1).
- **Request head limits are hyper's, not Puma's**: up to 417,792 bytes of headers (431 past that) and a 65,534-byte URI (414 past
  that; Kestrel limits the whole request line, so the edge moves by the method's length). Kestrel's own 8 KB line and 32 KB headers
  refused cookies and URLs Rails serves. Puma stops at 112 KB of headers and answers 400 above 12 KB of URI. Tested by
  `RequestsTests` (R7) and `HostTests` (HS6).
- **Three request-line rules** are fixed in Kestrel's parser and differ from Puma's and hyper's: a `%00` in the path is 400 (Rails
  routes it as any path); an absolute-form target whose authority differs from `Host` is 400 (Rails serves it); an HTTP/1.0 request
  is answered with an `HTTP/1.1` status line. Tested by `RequestsTests` (R8).
- **The request log** is ASP.NET Core's HTTP logging line (`Web/HttpLog.fs`: one combined entry per request with the method, path,
  query, protocol, status and duration, plus `remote_addr` and `user_agent`) written by the console logger, not Thruster's
  `Request path=... status=...` line. It carries no other header and no body; `LOG_REQUESTS=false` leaves the middleware out. The
  client address is the connection's after the forwarded headers, an IPv4-mapped address written as IPv4, with no port. A query value
  whose key Rails' `filter_parameters` names (`passw`, `email`, `token`, `_key`, `secret`, ...) is `[FILTERED]`, as in Rails' own
  `Started GET` line; paths are logged as sent, as Rails and Thruster log them, so a bot key, a join code or a session transfer
  id in a path reaches the log (percent-encoded, no control character). Tested by `HttpLogTests` (LG1-LG4, SR1-SR3).
- **The request log skips three route families**: the static asset endpoints (every fingerprinted asset and public file, matched
  against the loaded `MapStaticAssets` table), the avatar family and `/up` (Rails silences its health check too), for `GET` and
  `HEAD` only. Thruster logged each of them; here such a request writes no line, which keeps the middleware off the hot static
  path. Everything else is still logged (pages, posts, bot calls, redirects, 404s, and a 404 under `/assets/` that is not a table
  route), and a 500 on a skipped path still writes the `request failed` line. Any other method on those paths (a `DELETE` of an
  avatar, a write) is logged as usual. The framework's meters count every request. `HttpLog.logged` is the one predicate to
  change. Tested by `HttpLogTests` (LS1-LS6).
- **The response cache** is ASP.NET Core's OutputCache, one base policy per route family that Rails marks `public`, matched by the
  route's exact path shape: `/users/:id/avatar` 30 minutes, `/account/logo` 5 minutes, `/qr_code/:id` 1 year, the Active Storage
  proxy routes 100 years, the disk service's `show` 1 hour. Nothing else is cached, so a page is never looked up, stored or locked.
  `CACHE_SIZE` is the cache's capacity and `MAX_CACHE_ITEM_SIZE` the largest body it stores; keys are case-sensitive and include
  the query and the request's `Accept-Encoding`. A hit carries `Age`; there is no `X-Cache` header. A response that sets a cookie
  is never stored (the hourly session resume on an avatar), and a request with an `Authorization` or a `Range` header bypasses the
  cache. Tested by `OutputCachingTests` (OC1-OC9).
- **Listeners and timeouts.** `HTTP_PORT` and `TARGET_PORT` (loopback, or `TARGET_BIND`) both serve HTTP/1.1; cleartext HTTP/2 is
  not offered and `H2C_ENABLED` is not read; `HTTP_WRITE_TIMEOUT` and `DEBUG` are not read either. `HTTP_IDLE_TIMEOUT` is Kestrel's
  keep-alive timeout and `HTTP_READ_TIMEOUT` its request-header timeout; a body is cut by Kestrel's minimum data rate (240 bytes a
  second after 5 seconds), for requests and responses alike. A timeout of zero, a negative one or one of 49 days or more means
  none. Tested by `HostTests` (HS1, HS8, HS10, HS12).
- **`FORWARD_HEADERS`** (default on without TLS) decides whether a client's `X-Forwarded-For`, `-Proto` and `-Host` count: on, only
  from Rails' `TRUSTED_PROXIES`; off, never. The front's own rewriting of `X-Forwarded-*`, `Forwarded` and `X-Request-Start` is
  gone; the app reads none of them. Tested by `HostTests` (HS7).
- **`MAX_REQUEST_BODY`** (0 = none) lowers the app's 16 MiB cap, so the 413 is the app's whichever limit hit. Stopping is one 10
  second budget (SIGTERM or SIGINT: Cable sockets get `server_restart`, then connections drain), not Thruster's 5 seconds inside the
  app's 10. Tested by `HostTests` (HS9, HS11).
- **HSTS** is still sent on every response that reaches the edge middleware, including a 500 page; `force_ssl`'s own HTTP-to-HTTPS
  redirect is gone, because production's `assume_ssl` made every request HTTPS before it could fire. Tested by `HostTests` (HS5).
- **TLS.** With `TLS_DOMAIN`, `HTTPS_PORT` is Kestrel's own TLS listener (HTTP/1.1 and HTTP/2) choosing each handshake's certificate
  by server name from the ACME store; a name outside `TLS_DOMAIN`, or none, fails the handshake, and the first handshake of a name
  waits for its first certificate for at most `HTTP_READ_TIMEOUT` (30 seconds when that means none). The HTTP port then serves only
  this: a `Host` outside `TLS_DOMAIN` (normalized as the handshake normalizes names: IDNA with STD3 rules, so an underscore name is
  refused) is a 421, `/.well-known/acme-challenge/` for such a host a 403, the challenge of a `TLS_DOMAIN` host its key
  authorization, and everything else a 301 to `https://<normalized host>[:HTTPS_PORT]<same target>`. Names `TLS_DOMAIN` lists that
  are refused are logged at boot, and a list with none usable stops the boot. Tested by `HostTests` (HS2, HS4).
- **Certificates** are ordered by Certes in autocert's cache layout; TLS-ALPN-01 is not answered, only HTTP-01, so port 80 must be
  reachable. They are ordered at boot and renewed in the background `min(30 days, lifetime / 3)` before expiry; a cached
  certificate for a name no longer in `TLS_DOMAIN` is not served. Tested by `AcmeTests` (AC4, AC6, AC11 against Pebble).
- **Compression bytes** differ from Rust's (.NET's deflate, Brotli and zstd are not flate2 and libzstd); the decoded bytes are the
  same. Compression itself is described under Application.

### Page cache (`Web/PageCache.fs`)

- **A write made outside the app shows on a cached page within 60 seconds, or at the app's next own commit.** Upstream Rails watches
  `PRAGMA data_version` because its web and job processes are separate programs. theDotNetWay is one process and its only writer, so
  every write it makes bumps the cache's epoch and shows on the next request; only a database edited live by another program (which
  neither app supports) waits for the entry's 60 second life. Tested by `PageCacheTests` (PC18: a raw connection changes a room's
  name, the cached sidebar still shows the old one, then the new one once the entry's lifetime ends).
- **A request that accepts no compression is rendered every time**, as every request was before the cache (a page that is not
  compressed has no kept bytes to store). No browser sends `identity` only. Tested by PC9.

### Cable (`Campfire.Cable`; Action Cable 8.1 on Puma is the reference)

The transport is ASP.NET Core's WebSocket middleware, so the handshake, framing, masking, control frames and close codes are the
framework's. Tested by `ProtocolTests`, `TransportTests`, `LimitTests`, `MemoryTests` and the golden frame comparison.
- **No WebSocket compression, as Rails.** A client's `permessage-deflate` offer gets no `Sec-WebSocket-Extensions`
  (`DangerousEnableCompression = false`). Tested by `TransportTests` "P1 the subprotocol is the first of the client's list that
  Action Cable speaks, and no extension is negotiated".
- **Slow clients are cut, not buffered.** Rails buffers without limit. A client whose outbound queue is 256 frames behind, or whose
  socket has taken nothing for a send of 30 seconds, is disconnected with a TCP abort and no `disconnect` frame; its Action Cable
  client reconnects and resubscribes. `unsubscribed` runs for each of its subscriptions. Tested by `LimitTests` (L1, L2).
- **Limits (Rails has none):** 64 subscriptions and 4 KiB identifiers per connection (beyond them the command is logged and
  ignored), messages of at most 1 MiB (larger closes with 1009, also when sent in fragments), a binary message is logged and
  ignored, invalid UTF-8 in a text message closes with 1007. Tested by `ProtocolTests` (L3, L5, L6) and `LimitTests` (L4).
- **Origin check.** `Origin` is compared with the request's scheme and host as ASP.NET Core sees them after the trusted-proxy
  forwarded headers and `assume_ssl`; behind a proxy that rewrites `Host` and sends `X-Forwarded-Host` from a trusted range, the
  forwarded host is the one compared (Rails compares the raw `Host`). Tested by `CableAuthTests` "cross-origin upgrades carrying a
  valid session cookie get Rails 404 and no socket".
- **Authentication is the page cookie scheme**: the same ticket and lookup as every page, no `sessions` write and no cookie on the
  upgrade. The connection looks its session up once more after it is registered, so a ban or sign-out between the first lookup and
  the registration is heard (Rails has that gap). A remote disconnect is sent after the commit that caused it, not inside the
  transaction as Rails' `deactivate` and `ban` do. Tested by `CableAuthTests` (A1, A2, A3, A5) and `TransportTests` (A3, A4).
- **Memory.** An idle connection holds about 16 KB of managed memory on the server side, 13.6 KB of it the host's own (Kestrel, the
  middleware, a `ManagedWebSocket`) and 2.6 KB Cable's; the Rust port's resident set is about 9 KB per client. Tested by
  `MemoryTests` (L7).
- **The `101 Switching Protocols`** is Kestrel's (see class A). Tested by `ProtocolTests` "the 101 answers as websocket-driver does,
  plus the host's Date".

### Views (`Campfire.Views`: Falco.Markup)

The templates are serialized by the library, so a page is the same page as Rails' to the loose DOM (the harness's own rule) and not
the same bytes (class A above). There is no other visible difference; anything visible (a lost space, a missing `data-*`, an aria
name run together) is class C and is fixed.
- **Custom CSS** in an account's styles is the one place the text itself differs: a `</style` (any case) in it is written `<\/style`,
  so a hostile stylesheet cannot close the element (Rails writes it raw). Tested by `AccountsViewsTests` (T1).
- **The fragment cache** is the framework's HybridCache over a size-limited memory cache (32 MiB by default,
  `CAMPFIRE_FRAGMENT_CACHE_MB`), not Rails' store: entries are evicted by `MemoryCache` compaction rather than a prune to three
  quarters, they expire after seven days, and a key is Rails' `cache_key_with_version` with the partial and variant but no
  template digest (the cache does not outlive the process). Fragments are fetched up front and awaited, never inside a template.
  JSON (the bot API) is not cached, so a request's `Host` cannot change another user's links. Tested by `FragmentsTests` (F1, F2)
  and `MessagesViewsTests` "message partial is the same on every host".
- **`capitalize`, `User#initials`' word boundary and the case of a name's first letters** use tables generated from Rust's standard
  library (`Helpers/UnicodeTables.fs`), not .NET's own casing, which leaves out special casing (`ß` is `SS`), the Turkish dotless i
  and characters Unicode added since. **`String#downcase`** of a user's name (the room forms' `data-value`) is Rust's
  `str::to_lowercase`, final-sigma rule included (`ΑΣ` is `ας`, `ΑΣΑ` is `ασα`); .NET's `ToLowerInvariant` writes `σ` for every
  sigma. Tested by `HelpersTests` "capitalizes like rust" and "to_lowercase maps a capital sigma by where the word ends".
- **Timestamps in cache keys and `to_fs(:epoch)`** are `DateTimeOffset`s, which hold 100 ns where jiff holds 1 ns. A key shows
  microseconds, so no key differs; a record dated before 1970 or outside the years 1 to 9999 is not representable (no record is).
  Tested by `EfTests` "D2 now is the provider's UTC time truncated to microseconds".
- **The web app manifest** (`Templates/Pwa/Manifest.fs`; `app/views/pwa/manifest.json.erb`): the account name and the logo URLs are
  JSON strings, where Rails HTML-escaped them inside the JSON (a small logo's `src` reads `/account/logo?size=small&amp;v=...`
  there, `&v=` here). With an account logo the manifest differs from Rails in that one byte run, so the parity harness's
  `pwa/manifest/custom_styles` cell reports it. Tested by `ParityATests` "pwa manifest is valid json whatever the account is
  called".

### Rich text (`Campfire.RichText`: AngleSharp's HTML5 parser where Rails runs Nokogiri and Gumbo)

Every difference below needs a hostile, hand-made body; none appears in the 658-case corpus's ordinary bodies or the 164 seed bodies.
`tests/Campfire.RichText.Tests/corpus/known-differences.json` lists the corpus cases that differ, checked both ways by
`CorpusTests`.
- **End-tag-shaped text** inside raw-text and RCDATA elements is lowercased by AngleSharp's tokenizer: `<textarea></FooBar></textarea>`
  shows `</foobar>`. Tested by `CorpusTests`.
- **Raw-text-named elements inside `svg` or `math`**, and a few table-part shapes, follow AngleSharp's tree builder: the text is
  dropped with the foreign element, or `tbody` and `tr` land outside the table. Tested by `CorpusTests`.
- **The 2025 `select` model.** AngleSharp keeps markup inside `option` and `select` that Gumbo flattens (seven fuzz bodies and the corpus's `malformed select`, plus one body with `noscript`). `<isindex>` is still
  expanded to a search form as the 2008 standard did; the sanitizer drops the form, the sentence stays as text. Tested by
  `CorpusTests`.
- **Comments after an explicit `</body>`** that content follows arrive after that content; element-context parses keep foreign
  content inside; and one layout-only case (an `action-text-attachment` whose replacement begins with another one inside a foreign
  element stays inside it). Tested by `CorpusTests`.
- **Caps Gumbo does not have.** A body of more than 10,000 elements or 40,000 tokens, or whose `<html>` and `<body>` tags carry more
  than 400 attributes between them, is refused as a body past Gumbo's limits is: the message renders `messages/_unrenderable`, its
  plain text is empty and the refusal is logged; Rails renders it. AngleSharp's tree builder is quadratic on such bodies; no body
  the composer produces comes near them. Tested by `Html5Tests` (L2, L3).
- **Depth and attribute limits** (400 deep, 400 attributes, 10,000 elements) are counted on the markup's body parse, and depth on
  the elements AngleSharp creates, not on Gumbo's stack: 400 `<b>` then `</p>` (the implied `<p>` is element 401) is refused where
  Rails renders it, and formatting elements reopened past the limit then a comment render where Rails refuses them. A body that ends
  inside a tag that has reached the attribute limit renders its remaining content where Rails renders nothing. Tested by `Html5Tests`
  (L1).
- **Markup AngleSharp's tree builder throws on** is refused as a body past a limit is: the message renders `messages/_unrenderable`,
  its plain text is empty and logged, and posting it stores it as it came. AngleSharp 1.8.4 and 1.8.5 throw a
  `NullReferenceException` on `<p><select><xmp></xmp><select>x`; Rails renders it. Tested by `HardeningTests`
  "markup_the_parser_throws_on_is_refused_not_raised" and `MessagesTests` "a body the parser throws on fails neither the post nor
  the pages that show it".
- **Opengraph URLs** are read by `System.Uri` behind an RFC 3986 character check, not Ruby's parser: `mailto:nobody`, a host with
  empty labels, `[v1.x]` and a port past 65535 give an embed without a link, where Rails raises or links. Tested by
  `OpengraphTests` "System.Uri reads the old URI probes".
- The sibling-walk cost, the tokenizer's raw-text lowercasing and the select-mode exception were reported to AngleSharp
  ([#1392](https://github.com/AngleSharp/AngleSharp/issues/1392), [#1393](https://github.com/AngleSharp/AngleSharp/issues/1393),
  [#1394](https://github.com/AngleSharp/AngleSharp/issues/1394)); this repository does not depend on a fix.

### Integrations (`Integrations/`: jobs, webhooks, unfurls, Web Push)

Every connection out goes through one of three named `HttpClient`s (`Integrations/Clients.fs`: webhook, opengraph, push), none of
which follows redirects, uses a proxy or keeps cookies, and all of which connect through the private-network guard. Tested by
`ClientsTests` (CL1-CL5, GD1-GD4).
- **Webhooks** go out on .NET's `HttpClient`, not the `Net::HTTP` replica. The method, request target, `Content-Type:
  application/json`, `Accept: */*`, `User-Agent: Ruby` and the body are Rails'; the header list's order, the `Host` spelling,
  `Connection` and `Accept-Encoding: gzip, deflate` (Net::HTTP sends `gzip;q=1.0,deflate;q=0.6,identity;q=0.3`) are .NET's. A reply
  that trickles is cut at the 60 s delivery deadline; the 7 s timeout applies up to the response head, not between reads. A POST is
  sent once on a 500, a reset and a timeout. Tested by `WebhookTests` "delivers like the reference" (19 recorded cases) and "gives
  up on a trickling reply", and `ClientsTests` (CL1, CL4).
- **Link unfurls** follow Rails' redirect loop by hand (10 responses, each target re-parsed, required to be http(s) and resolved
  through the guard again, pinned at connect). `Accept-Encoding` is .NET's (`gzip, deflate`). At most 16 unfurls run at once, body
  reads included; each request has 5 s to its response head, and the unfurl as a whole 10 s. A connection is closed under a read
  in progress when a deadline passes (the answer is the same: "Failed to respond within N seconds"). Tested by `OpengraphTests`
  "unfurls like the reference" (90 recorded cases), "gives up on a trickling page" and OG4.
- **Malformed pages** unfurl as an HTML5 parser reads them, not libxml2's legacy parser. They agree on all 55 recorded page bodies
  and differ on 14 probes: the references `&AMP;`, `&#128;`, `&#150;`, `&#xD800;`, `&#1114112;`, `&#;` and `&#x;`, and a `meta`
  inside `textarea`, `title` or `template`, `<meta/property=.../content=...>`, an unterminated quoted value, a NUL, and CR LF
  (normalized to LF). Tested by `OpengraphTests` OG3 ("decodes references like libxml2 but for the class B probes" and "tokenizes
  like libxml2 but for the class B probes", each failing if a listed probe stops differing or another starts).
- **Caps.** A page nested deeper than 256 elements (libxml2's maximum depth) or with more than 100,000 elements stops the parse and
  the metas created before the stop are kept, so a page's head still unfurls. Rails' parser would spend minutes of CPU on a 5 MB page
  of nested elements. Tested by `OpengraphTests` (OG1, OG2).
- **`mailto:`** unfurls answer 204 and look nothing up, where Rails raised `URI::InvalidComponentError` (a 500). Tested by
  `OpengraphTests` "a mailto url unfurls nothing".
- **Tweet hosts.** `System.Uri` lower-cases a URL's host, so `https://Twitter.com/x/status/1` is a tweet here (read through
  fxtwitter.com), where Ruby keeps the host's case and it is not. Tested by `OpengraphTests` "a tweet url's host is read
  lower-cased".
- **Web Push** goes out on `Lib.Net.Http.WebPush` 3.3.1 over the push client (no redirect, no proxy, the guard at connect, a 10 s
  pipeline timeout and a 30 s delivery deadline, `AutoRetryAfter` off so a 429 is sent once). The library writes the request, not
  the web-push gem; the push service and the browser decrypt both forms. The gem's 400 "UnauthorizedRegistration" is logged as a
  plain `ResponseError` and, like the gem's, never destroys the subscription. The VAPID signature is ECDSA with a random nonce, where
  Rust's is deterministic (RFC 6979); both verify under the public key. TLS verifies against the operating system's CA store with
  no revocation check. The JWT's `exp` follows the library's clock. Invalid VAPID keys turn Web Push off at boot with a log line. Tested by `WebPushTests` (WP1-WP7),
  `OptionsTests` (OP6) and `ClientsTests` (CL1).

### Carried over from the Rust port (these still apply, and Rails does not do them)

The Rust port listed these deliberate changes from Rails; each holds here unchanged unless it says otherwise.
- **Jobs.** Redis and Resque are replaced by in-process queues with `JOB_CONCURRENCY` workers per job kind. Queued pushes and webhooks
  are lost on a crash; slow webhooks do not block pushes. The queues are hosted `BackgroundService`s and drain on shutdown. Tested by
  `JobsTests` (JB1-JB5).
- **Push.** Subscriptions survive TLS and configuration failures and are deleted only on 404 or 410 or an invalid subscription P-256
  key; notification bodies are truncated with an ellipsis at 3 KB and titles at 256 bytes; at most 50 deliveries run and 10,000
  wait, beyond that they are dropped and logged. Tested by `WebPushTests` (WP3, WP8) and `PushTests` "truncates by json escaped
  length".
- **Cookies.** Sessions are written only on change and deleted when empty; `last_room` only on change; `session_token` is re-signed
  on the hourly activity refresh, keeping its rolling 20-year expiry. Tested by `AuthenticationTests` (A3, A5) and
  `RoomControllerTests` (D3).
- **Caching.** Room, messages and search `ETag`s hash cached page parts rather than the body; copy-link buttons carry paths and
  resolve them against the page URL. Tested by `RoomsTests` "a room page has the same etag cold and warm" and `MessagesViewsTests`
  "message partial is the same on every host".
- **Media.** libvips 8.16.1 and ffmpeg 7.1.5 use the Rails image's Debian sources, with byte-identical thumbnails, posters and
  metadata for supported formats; libvips omits loaders Rails already blocks; ffmpeg omits external-library-only formats (tracker
  modules, game-console music, JPEG XL and SVG frames, codec2, teletext, DASH and IMF), so tracker and game-console uploads lack
  duration and bit rate. Message uploads are copied and checksummed before their rows are saved, and deleted if saving fails; the
  redundant MD5 reread is skipped. Tested by `VectorTests` "posters are made from the reference frame as the reference made them"
  and `bin/storage-media-vectors`.
- **Request limits.** Non-file-upload bodies and direct uploads are capped at 16 MiB (413); non-numeric direct-upload sizes and
  oversized QR codes return 422; page numbers cap at a billion. Tested by `PresenterAccountsTests` "direct uploads are refused past
  the body limit", `LookupControllerTests` (K2) and `PaginationTests` "huge page numbers are capped".
- **Webhook and unfurl limits.** Replies over 100 MB after decompression fail a webhook delivery without posting a response, and
  only the first 256 attributes of a `meta` tag are read. Tested by `WebhookTests` "rejects replies over the limit" and
  `OpengraphTests` "stops reading a gzip bomb at the limit". (The Cable limits are listed under Cable.)
- **Search.** Words are literal full-text terms, including `NOT`, `AND`, `OR` and `NEAR`. Tested by `MessageTests` "search words are
  never query syntax".
- **Routes and UI.** `/rooms/directs/:id` redirects to the room; infinite `Accept` q-values sort first or last by sign;
  EdgeHTML install instructions include the missing image. Tested by `RoomsTests` "undeclared actions" (the redirect),
  `FormatsTests` "quality ordering" and `PresenterAccountsTests` "edge gets its install instructions".
- **Rich text.** Autolinking escapes `<` and `>` in attributes to prevent stored XSS; sanitization drops `name` attributes to prevent
  DOM clobbering; styles keep only `color` and `background-color` with plain keyword, hex, RGB, HSL values or CSS variables in bot
  and webhook HTML, and message pages drop styles; content attachments nest at most eight levels; deleted-user mentions render ☒
  and are omitted in the editor; plain-text extraction failures are logged and use empty text or an attachment filename, and the
  message is still indexed, pushed, broadcast and sent to bots. Tested by `HardeningTests`
  "a_url_after_a_greater_than_sign_in_an_attribute_cannot_break_out_of_it", "name_attributes_cant_clobber_the_pages_globals",
  "content_attachments_render_eight_levels_deep" and "a_mention_of_a_deleted_user_leaves_the_editor", `SanitizerTests`
  "keeps_only_lexxys_highlight_colors_in_style" and `MiscTests` "a body whose plain text raises has none".
- **Not ported.** Active Storage streaming's duplicate `session_token` cookie and legacy AES-CBC cookies; Campfire uses AES-GCM.
  Tested by `AuthenticationTests` (A1: a Rails-minted cookie signs in).
- **Not carried.** The Rust port's entries on WebSocket compression and CSRF are replaced by the Cable and Application entries
  above; its bcrypt-off-the-writer note and the per-base-URL cache of bot JSON describe Rust internals that do not exist here
  (there is no writer thread to keep bcrypt from, and bot JSON is not cached).

## How this was built

theDotNetWay is a fork of the published F# port that replaced it layer by layer, one phase each, with every phase gated by the
pixel-parity harness (screenshot, accessibility tree and loose DOM equal for every captured state, on every seed), the cookie
round trip, the database differential, the full test suite, and a speed check against the 5x-Rails floor. The KLOC column is
`bin/kloc`'s corrected figure: upstream's counting rules, with the F# comment-marker miscount (cloc 2.10 reads `(*` as a comment)
fixed.

| Phase | What it replaced | With | KLOC at close |
|---|---|---|---:|
| 0 | the port as published | baseline, tooling, the first full gate | 39.4 |
| 1 | the hand-written data layer and its reader and writer topology | EF Core over Microsoft.Data.Sqlite on the Rails schema, no migrations; converters and interceptors | 38.4 |
| 2 | the kit: request and response types, cookie and session code, params, formats, compression | ASP.NET Core: an authentication handler on Rails' ticket format, `ICsrfProtection`, session and flash, `UseForwardedHeaders`, `UseResponseCompression`, controllers on `HttpContext` | 35.3 |
| hybrid reads | EF's statement cost on the hottest reads | Dapper for the per-request read paths, in one file (`Ef/FastReads.fs`), parameters only, compared with EF's answers | 35.4 |
| 3 | the hand-written WebSocket stack | the framework's WebSocket middleware and a bounded `Channel<T>` per connection | 34.8 |
| 4 | the UTF-8 view writer | Falco.Markup templates, HybridCache fragments, a markup lint | 33.7 |
| 5 | a port of the html5ever tree builder and of Loofah | AngleSharp's HTML5 parser with Loofah's rules on top, and `System.Uri` | 30.2 |
| 6 | a custom asset pipeline | `MapStaticAssets`, staged at build time, a generated import map | 30.0 |
| 7 | the Thruster port (front server, cache, ACME, TLS) | Kestrel listeners, OutputCache, HTTP logging, Certes | 27.9 |
| 8 | the `Net::HTTP` replica, hand-written web-push encryption, the job runner, hand-rolled config and clocks | named `HttpClient`s with a connect guard, `Lib.Net.Http.WebPush`, `BackgroundService` queues, `IOptions`, `TimeProvider`, redaction | 27.1 |
| 9 | the Rack range code, three small crates, scattered test helpers | the framework's file result, one `Campfire.Rails` project, `UseWhen` around the request log, `ReadAsync` readers, consolidated tests, this README | 26.2 |
| 10 | the per-statement connection opens, a reflection-heavy read, no page cache | one SQLite connection per request, `DOTNET_TC_CallCountingDelayMs=0`, a Dapper read for `rooms.original`, a page cache in the four GET actions on HybridCache with fresh authorization and a commit epoch | 26.5 |

The result is seven projects: `Campfire.App` (host, controllers, jobs, integrations), `Campfire.Views`, `Campfire.Db`,
`Campfire.RichText`, `Campfire.Storage`, `Campfire.Cable` and `Campfire.Rails` (cookie and crypto compatibility, the Ruby helpers
Rails' behavior needs, and the route table). The plan, each phase's design note, the gate evidence and the verifiers' findings are in
[`plans/dotnetway.md`](plans/dotnetway.md) (the Phase log) and `plans/design-*.md`.

## License

MIT; see [`MIT-LICENSE`](MIT-LICENSE).
