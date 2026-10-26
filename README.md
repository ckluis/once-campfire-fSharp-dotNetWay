# Campfire in F#

An F# implementation of [ONCE Campfire](https://github.com/basecamp/once-campfire), built on
ASP.NET Core and Falco. It aims to be a drop-in replacement for the Rails app: the same SQLite
database, storage layout and signed or encrypted cookies, with the same pages and behavior.

**Status: complete, and faster than the Rust port under load.** Every screen the parity harness captures renders the
same as the Rust port's, byte for byte (214 states across 5 seeds), and the five benchmarked workloads run 1.24-1.33x the
Rust port's throughput at 16 connections while spending 8-23% less CPU per request. The story, with every measured
version on one chart, is the [project page](https://ckluis.github.io/once-campfire-fSharp/) (`index.html`); the phases
are in [`plans/fsharp-port.md`](plans/fsharp-port.md).

It is translated from the [Rust port](https://github.com/basecamp/once-campfire-rust) (pinned in
`rust/`), which reproduces the Rails app (pinned in `reference/`) and ships the parity harness,
golden vectors and benchmark tooling this repository reuses under the MIT license.

The Rails frontend ships with a few
[port-owned overrides](src/Campfire.App/Assets/OVERRIDES.md): three JavaScript files and one image that
deliberately differ from Rails.

## Performance

Measured with 16 concurrent clients in a Linux VM (colima, 8 vCPUs) on an Apple M4, with four hardware threads
allocated to each app, the reference Rails app and the unmodified Rust image on the same machine and seed, 3 reps each
in alternating order ([`bench/results/phase7-final.md`](bench/results/phase7-final.md)):

| HTTP workload (requests/sec) | Rails | [Rust](https://github.com/basecamp/once-campfire-rust) | F# | F# / Rust |
|---|---:|---:|---:|---:|
| Room page | 239 | 28,623 | 40,422 | 1.41× ¹ |
| Messages page | 381 | 30,063 | 37,836 | 1.26× |
| Sidebar | 599 | 28,238 | 37,555 | 1.33× |
| Search | 408 | 26,606 | 34,070 | 1.28× |
| Post a message | 330 | 7,174 | 8,905 | 1.24× |

Every response was 2xx, every post's row was counted, and decoded bodies are identical to the Rust port's. F# spends
8-23% less CPU per request than Rust at 16 and 64 connections. ¹ The room page settles about 6% lower after 80 s,
when the thread pool grows (about 1.33×). A last tuning unit then turned off the thread pool's hill climbing
([`bench/results/phase7-unit-7.5.md`](bench/results/phase7-unit-7.5.md), F# re-measured against these Rust medians):
p99 on the room, messages and sidebar pages fell to Rust's level (about 1.0-1.1 ms at 16 clients) and F# ran 1.22-1.40×
Rust. Where Rust still leads: CPU per request at a single connection, memory, cold start and Action Cable fan-out (see
the results files). Part of the lead is how the request log is written: F# batches plain lines, the Rust port writes one
coloured line per request. (Since Phase 7's unit 7.1 the request log is the framework's HTTP logging line, below.)

## Running it

With [ONCE](https://github.com/basecamp/once), or Docker directly, as with the Rust port (the image is a drop-in for
the reference's: uid 1000, `/rails`, `/rails/storage/{db,files,backups}`, ports 80 and 443, the ONCE hooks):

```sh
docker build -t campfire-fsharp:app --build-arg APP_VERSION=... --build-arg GIT_REVISION=... .
docker run -d -p 80:80 -p 443:443 \
  -e SECRET_KEY_BASE=... -e VAPID_PUBLIC_KEY=... -e VAPID_PRIVATE_KEY=... \
  -e TLS_DOMAIN=chat.example.com \
  -v campfire:/rails/storage \
  campfire-fsharp:app
```

`TLS_DOMAIN` enables automatic Let's Encrypt certificates; `DISABLE_SSL` enables plain HTTP. The settings are in
[`Config.fs`](src/Campfire.App/Config.fs) (the app's) and [`Front/Config.fs`](src/Campfire.Kit/Front/Config.fs) (Thruster's).
For local development, `dotnet run --project src/Campfire.App -- server` with `SECRET_KEY_BASE` (or
`SECRET_KEY_BASE_DUMMY=1`) set and libvips installed (`HTTP_PORT=3000` avoids port 80).

## Development

```sh
git submodule update --init
bin/verify          # Release build with warnings as errors, then every test
```

See [`AGENTS.md`](AGENTS.md) for the layout and working rules.

## Known differences

The differences the Rust port lists from Rails apply here too (see its README). Differences
specific to this port are listed below, each citing the reference file it departs from.

- **Application** (`Campfire.App`; `rust/crates/campfire` is the port it follows):
  - Forgery protection is .NET 11's token-less `ICsrfProtection` (`Web/Csrf.fs`), called where Rails runs
    `verify_authenticity_token`, so the Rust README's entry on it is replaced: `Sec-Fetch-Site` first (`same-origin` and
    `none` pass; `cross-site` and **`same-site`**, another subdomain, are 422), then `Origin` against the request's scheme and
    host for a browser without Fetch Metadata. A write with neither header (not a browser) passes, over HTTPS too, where
    Rust refused it. Pages carry no tokens, so open tabs keep working across a switch; after a switch back to Rails a tab
    this port rendered needs a reload. Bot-key requests skip the check, as in Rails. Reference:
    `ActionController::RequestForgeryProtection` (the `:header_only` strategy). Tested by `CsrfTests` (C1-C13).
  - The mention list (`autocompletable/users`) reads a `filter` or `query` that is a hash or array as an empty string (Rails would
    search for the hash's `to_s`, which matches nothing either way). Reference: `app/controllers/autocompletable/users_controller.rb`.
  - A bot API request (`/rooms/:room_id/:bot_key/messages...`) keeps the bytes of its body for `raw_request_body` before the method
    override reads a form body, so `curl -d 'Hello'` posts "Hello" as in Rails; no other route keeps raw bytes, and a multipart body's
    `raw_request_body` is empty. A JSON `null` for `message[attachment]` or the bot API's `attachment` is "not given" (Rails' `nil` removes
    the attachment); `""` removes it as in Rails. `involvement` given as a non-empty hash or array is a 500 (an empty one is blank, as
    in Rails). References: `app/controllers/concerns/raw_request_body.rb`, `app/controllers/messages/by_bots_controller.rb`.
  - The flash and `return_to_after_authenticating` live in ASP.NET Core's session (server memory, cookie `_campfire_flash`),
    not in Rails' encrypted `_campfire_session`, which is neither read nor written (it stays in the browser untouched, so a
    switch back to Rails keeps it). A restart, or a switch either way in the middle of a redirect, loses a pending notice
    or return-to URL (sign-in then lands on the root). A request with no session cookie never loads the session. `last_room`
    is written through `Response.Cookies` (attribute order differs from Rails', class A). Reference:
    `config/initializers/session_store.rb`. Tested by `FlashTests` (F1-F8).
  - Proxy headers are the framework's `UseForwardedHeaders` (`Web/Services.fs`, `Edge.configureForwardedHeaders`):
    `X-Forwarded-For`, `-Proto` and `-Host` are honoured from Rails' `TRUSTED_PROXIES` ranges only, walked from the right
    with no forward limit, so a public peer's `X-Forwarded-For` is ignored where Rails (and the Rust port) believe it. Not
    read at all: `Forwarded`, `X-Forwarded-Ssl`, `X-Forwarded-Scheme`, and `Client-Ip` (so there is no
    `IpSpoofAttackError`, a 500 in Rails). `assume_ssl` (unless `DISABLE_SSL`) is a one-line middleware that makes every
    request `https`. Reference: `ActionDispatch::RemoteIp`, `ActionDispatch::AssumeSSL`. Tested by `RequestsTests`
    (R1, R2).
  - The method override is `UseHttpMethodOverride` on the form field `_method` only: `X-HTTP-Method-Override` is not read,
    a POST without a content type is not a form, and a `_method` that is not an HTTP verb makes the request that verb (a
    404 here, Rails and Rust leave it a POST). The method is upper-cased after it (`_method=delete` is the `DELETE` row).
    Reference: `Rack::MethodOverride`. Tested by `RequestsTests` (R3, R3b).
  - Params are the framework's readers (route values, `Request.Form`, the query string, a JSON body), in Rails' order
    (path over body over query, the last repeated key wins). A form is read once, by the framework: a stray `%` in a form is
    taken literally where Rack answers 400, the form's raw bytes are not kept, uploads are the framework's `IFormFile`s copied
    to a temp file, and pairs that interleave other keys are grouped by name (`a[][b]`, `a[][c]` in an unusual order build
    other array elements). Not reproduced: Rack's type-conflict errors (`a=1&a[]=2`) and its nesting depth limit.
    Reference: `ActionDispatch::ParamBuilder`, `Rack::QueryParser`. Tested by `ParamsTests` (P1-P4).
  - A malformed multipart body is judged by the framework's form reader, not Rack's: one cut short before its closing
    boundary, or sent without a boundary, is an empty 400 as in Rails (Rails has no `public/400.html`); other shapes Rack
    refuses may be read, or refused with another status. Reference: `Rack::Multipart::Parser`. Tested by `RequestsTests` (R9).
  - Format negotiation parses the `Accept` header with the framework and keeps Rails' rules around it (`params[:format]`,
    browser-like lists ignored, path extension, `text/*` expansion, Turbo's type first). A `q` Ruby's `to_f` reads and the
    framework does not (`q=abc`, `q=1e17`) leaves the item at 1.0, and `text/xml` is not folded into `application/xml`.
    Reference: `Mime::Type.parse`, `ActionDispatch::Http::MimeNegotiation`. Tested by `FormatsTests`.
  - Responses are compressed by the framework's `UseResponseCompression` (gzip and Brotli, HTTPS included), not `Rack::Deflater`:
    only the types in its list (HTML, JSON, JavaScript, CSS, SVG, XML, plain text and Turbo's streams), nothing under 256 bytes
    except an empty error (`head :not_found`), which goes out as a few compressed bytes as Rails sends it (Firefox shows its own
    error page for an error document of zero bytes; `RespondTests` X4b), nothing the public files already serve precompressed, and Brotli when the client asks for it; `Rack::Deflater` gzipped
    every body whatever its type or size and answered 406 to a client that refused every encoding. A page of cached fragments
    (a room page, a page of messages) is compressed once per encoding and kept, by the digest of its parts, in the HybridCache
    that holds the fragments (one 32 MiB budget by default), with the framework's provider's bytes; it is no longer built from gzip pieces stored per fragment, so a
    changed page costs one compression of the whole page. Reference: `config.ru` (`Rack::Deflater`). Tested by `RespondTests`
    (X4) and `RoomsTests` ("pages of messages go out in parts"). `Vary: Accept-Encoding` is sent on every compressible response, also to a
    client that sent no `Accept-Encoding` (`Rack::Deflater` adds it only for one that did).
  - `send_file` answers a `Range` request with 206 or 416 (the framework's range processing); Rails' `send_file` sends the whole
    file. `redirect_to`, `fresh_when`, `stale?` and `expires_in` keep Rails' rules (unsafe-redirect checks, strict freshness, the
    ETag combination of the validator, a Turbo Frame and the flash), on the framework's typed `If-None-Match` and
    `If-Modified-Since`; a `fresh_when` ETag digests the same parts as Rails (validator, Turbo Frame, template, flash) but writes the flash
    differently, so it is not byte-equal to Rails' or the Rust port's. No
    page gets `Rack::ETag`'s body hash from a Web action (K14). Reference: `ActionController::Redirecting`,
    `ActionController::ConditionalGet`, `ActionController::DataStreaming`. Tested by `RespondTests` (X1-X3, X5).
  - Sign-in throttling (`rate_limit to: 10, within: 3.minutes` on `sessions#create`) is the framework's `PartitionedRateLimiter`, a fixed
    window per remote IP in server memory, acquired inside the action after the before-action chain (as Rails' callback runs).
    Its windows run on wall-clock time, not the frozen harness clock, and a restart empties them (Rails' cache entries are in
    the cache store). Reference: `SessionsController` (`rate_limit`). Tested by `FlashTests` (F7) and `ControllersATests` (N8).
  - The responses of the controllers moved to `HttpContext` (sessions, first run, welcome, PWA, transfers, health, Turbo Native,
    Action Mailbox) carry Rails' default headers, a default `Content-Type` and, where they set none, `Cache-Control`
    `max-age=0, private, must-revalidate` for a 200 with a body and `no-cache` otherwise, set as the response starts; they
    carry no `ETag` (K14: the same DOM, pixels and data). A page rendered by a Web action is written by `Pages`, so a
    page of cached fragments still goes out in parts. Tested by `ControllersATests` (N1, N2).
  - The account and user controllers (accounts, bots, join codes, custom styles, logos, users, avatars, bans, profiles, sidebars,
    push subscriptions) read their params through the framework's form, query and JSON readers (`Web.Params`): a JSON `null` is
    read as a missing key, where Rails keeps a nil value (`{"user":{"name":null}}` updates nothing here; Rails raises on the NOT NULL
    column; a nil `endpoint` in a push subscription is dropped from the lookup instead of matching `IS NULL`). A browser form cannot
    send nil, so no page of the app can reach the difference. The two `ActionController::Live` controllers (avatars and logos, by
    `include ActiveStorage::Streaming`) still answer without Rails' default headers and with `Cache-Control: no-cache` where
    they set none. Reference: `Accounts::*`, `Users::*`. Tested by `ControllersBTests` (J1-J10).
  - A request body is capped at 16 MiB, files included, by Kestrel (413; Rust's front server did the same, Rails reads
    any size). Reference: `config/puma.rb` (no limit). Tested by `RequestsTests` (R4).
  - `VAPID_SUBJECT` without a `TLS_DOMAIN` defaults to this project's URL (`https://github.com/ckluis/once-campfire-fsharp`);
    Rust's names its own. Tested by `ConfigTests` ("vapid subject defaults to the tls domain").
  - A response header whose first line is empty reaches the wire as a line of its own (the first line of a
    `Content-Disposition` that begins with a line break, which Puma writes), where Kestrel's `Append` would drop a
    lone empty value; `Web/Outgoing.fs` hands such a name's values over as one list. Tested by `AppTests` ("proxied blobs
    write odd dispositions as puma does").
  - Route recognition looks a path's routes up by its first segment and tries their regexes in table order (Rust asks a
    `RegexSet` for the first of a verb's patterns that match). The answer is the same for every path of the
    generated corpus. Tested by `RouteTableTests` ("recognizes like a first match scan").
  - The `jemalloc` options of `main.rs` have no counterpart: .NET's GC is configured in the project file (server GC,
    tiered PGO); only the transparent-huge-page opt-out (`prctl`) is kept, on Linux.
  - `GET /rooms/:room_id/refresh?since=` clamps a `since` too large for the clock to the end of the .NET `DateTime` range
    (Rust: the end of jiff's), which only matters to what `created_at >` is compared with: no row is newer than either end,
    and every row is newer than the low one. Reference: `app/controllers/rooms/refreshes_controller.rb`.
  - Logs are `Microsoft.Extensions.Logging`'s console lines (`RAILS_LOG_LEVEL` or `CAMPFIRE_LOG` set the level), not
    `tracing`'s; the request lines come from the front server as Thruster's did.
  - The outbound HTTP clients (opengraph, Web Push, bot webhooks; `Integrations/Http.fs`) write the HTTP/1.1 exchange
    themselves over a stream, as Rust does over hyper's client connection, and verify TLS peers with .NET's `SslStream`
    (the operating system's CA store, no revocation checks) where Rust uses rustls with the system's certificates. The
    request line and headers go out as `Net::HTTP` writes them, in its order and spelling, which `WebhookTests`
    ("delivers like the reference") and `WebPushTests` ("delivers to the pinned address with the gems headers") compare.
    A gzip reply is inflated through .NET's `GZipStream` and `ZLibStream` read in 64 KB steps, so a bomb is stopped
    within 64 KB of the limit (Rust: within the decoder's 32 KB buffer); tested by `HttpTests`.
  - Web Push's VAPID signature is ECDSA with a random nonce (`ECDsa`), where Rust's p256 signs deterministically
    (RFC 6979); both verify under the public key, which `WebPushTests` ("signs the vapid header like the gem") checks.
  - An unfurl, a webhook delivery and a push that run past their deadline have their connection closed under the read
    in progress (Rust drops the future); the answer is the same ("Failed to respond within N seconds", no content, a
    read timeout). Tested by `OpengraphTests` ("gives up on a trickling page"), `WebhookTests` ("gives up on a trickling reply").
  - The opengraph `<meta>` scanner steps through UTF-16 units where Rust's steps through bytes; everything it matches is
    ASCII, so it splits the same places. `FILES_AND_MEDIA_URL_REGEX` is .NET's regex engine (non-backtracking) where
    Rust uses the `regex` crate: `\b` and `\S` can differ from Rust's on characters that are only marks or letter numbers.

- **Web app manifest** (`Campfire.Views/Templates/Pwa/Manifest.fs`; reference `app/views/pwa/manifest.json.erb`):
  the account name and the logo URLs are JSON strings, where Rails HTML-escaped them inside the JSON (a small logo's
  `src` reads `/account/logo?size=small&amp;v=...` there, `&v=` here and in the Rust port, which lists the same
  difference). With an account logo the manifest differs from Rails in that one byte run, so parity's
  `pwa/manifest/custom_styles` cell reports it for this port and for Rust alike: both `allowlist.yml` files list only
  `pwa/manifest`, which doesn't match the sub-state, and ours is kept no larger than Rust's. Tested by `ParityATests`
  ("pwa manifest is valid json whatever the account is called").

- **Assets** (`Campfire.App/Web/StaticAssets.fs`, `Importmap.fs` and the `CampfireStageWebRoot` build target; the references are
  Propshaft 1.2.1, importmap-rails 2.2.2, `ActionDispatch::Static` and `config/environments/production.rb`). Assets and the five
  `public/` files are staged into the web root at build time and served by ASP.NET Core's `MapStaticAssets` where Rails'
  `ActionDispatch::Static` ran; the page's stylesheet tags, import map and `Link` header are made once at start from that table.
  Class A (the bytes differ, the behaviour does not):
  - Asset URLs are `name.<fingerprint>.ext` (the SDK's ten-character form) where Propshaft writes `name-<digest>.ext`.
  - A CSS `url()` or `@import` and a JavaScript `sourceMappingURL` are not rewritten: they fetch the plain URL of the same file
    (pinned by T2's list of 11 files; the three `lexxy-*.css` files are fetched twice per page for it).
  - Assets carry an ETag, a per-file `Last-Modified` and `Accept-Ranges: bytes`, and text assets are precompressed (Brotli or gzip
    by `Accept-Encoding`) where Rails' front server compressed them on the fly; images (`campfire-icon.png` on every page) go
    uncompressed, where Rails' front server compressed them too (the parity harness's network layer shows each of these).
  - The import map's JSON is written by `ImportMapDefinition` (the same text after the digest mask), and the preload `Link` header
    holds 15 stylesheets where Rails' holds 16 (Rails' 1,000-byte rule, the same rule, a different byte count per link).
  Class B (the behaviour differs), each with the test or check that pins it:
  - Cache headers are the framework's: `max-age=31536000, immutable` on a fingerprinted URL and `no-cache` on a plain one
    (`/assets/<logical path>`, which Rails does not serve at all), against `public, max-age=2592000` on every public file
    (`bin/asset-check`, N1 and N3). `/robots.txt` and the error pages are `no-cache` with an ETag too.
  - An extensionless `/404`, `/422`, `/500` or `/502` is the app's own 404 page, not the `.html` file with 200 (H5).
  - `/assets/.manifest.json` is not served, and neither is any dotfile, the build output (`/campfire.dll`, the endpoint manifest) or
    the web root by name (`/wwwroot/...`); encoded dots and backslashes are the app's 404 (an App test, and `bin/asset-check`).
  - A request with several byte ranges gets the whole file with 200, where Rails answers `multipart/byteranges`.
  - A method other than GET or HEAD on an asset URL is 405, where Rails' router answers 404 (H4).
  - Asset and public-file routes ignore letter case: `/ASSETS/COMPOSER.<FINGERPRINT>.CSS`, `/Assets/Composer.css` and `/ROBOTS.TXT`
    answer the file, where Rails' `Static` (a case-sensitive file system) leaves them to the router's 404. A trailing slash
    (`/robots.txt/`) answers the file in both (H9).
  - The vendored `lexxy.js.gz` and `lexxy.js.br` are not served under their own names: `/assets/lexxy.js.gz` is the SDK's own
    precompressed route of `lexxy.js` (it answers `Content-Encoding: gzip`), and `/assets/lexxy.js.br` is the app's 404.
  - Asset responses carry neither `x-request-id` nor `x-runtime`, which Rails' `Static` also answers ahead of (H3).

- **A response header value with a line break** (the `?disposition=` of a proxied blob, the one header the app builds from
  the request) keeps every line, and a line holding another control character is left out. The Rust port keeps only the first
  line; Puma writes them all (puma 7.2.1, `Puma::Request#str_headers`, the reference's server). Tested by `AppTests`
  ("proxied blobs write odd dispositions as puma does").

- **Front server** (`Campfire.Kit`, Thruster's job; Thruster 0.1.23 is the reference, `rust/crates/kit/src/front`
  the port it follows):
  - Idle HTTP/1 connections close at `HTTP_IDLE_TIMEOUT`, as Go's `http.Server` (Thruster) closed them. The
    Rust port closes them at the shorter of that and `HTTP_READ_TIMEOUT`, because hyper's header timer also
    runs while a connection waits; Kestrel's keep-alive timer doesn't. Tested by `FrontTests`
    ("closes idle and slow connections").
  - Kestrel always sends `Date`, so the app's own listener on `TARGET_PORT` sends one, where Puma (and the
    Rust port) send none.
  - Request head limits are hyper's, not Puma's: up to 417,792 bytes of headers (431 past that) and a 65,534-byte
    URI (414 past that; Kestrel limits the whole request line, so the edge moves by the method's length). Kestrel's
    own 8 KB line and 32 KB headers refused cookies and URLs Rails serves. Puma stops at
    112 KB of headers and answers 400 above 12 KB of URI (10 KB of query), where this port and the Rust port serve
    them. Tested by `RequestsTests` (R7).
  - Three request-line rules are fixed in Kestrel's parser and can't be configured, so they differ from Puma's and
    hyper's: a `%00` in the path is 400 (Rails and Rust redirect or route it as any path); an absolute-form target
    whose authority differs from `Host` is 400 (both serve it); an HTTP/1.0 request is answered with an
    `HTTP/1.1` status line (both answer `HTTP/1.0`). Tested by `RequestsTests` (R8).
  - Cleartext HTTP/2 (`H2C_ENABLED`) on a port that also serves HTTP/1.1 reads each connection's first
    bytes for HTTP/2's preface and tells Kestrel which protocol to speak through an internal Kestrel
    feature (`Campfire.Kit/Front/Conn.fs`, `FrontH2c`); if a Kestrel release changes it, the port serves
    HTTP/1.1 only and logs a warning. Tested by `FrontTests` ("speaks h2c only when enabled").
  - Compressed responses carry the same decoded bytes as Rust's, not the same compressed bytes (.NET's
    deflate and ZstdSharp are not flate2 and libzstd); the app's own compression is the framework's, and the Rust test
    that pins the SHA-256 of a spliced gzip member is gone with the spliced gzip (unit 2.8).
  - Class B (Phase 7, unit 7.1; `plans/design-front.md` F8, F10), each with the test that pins it:
    - The request log is ASP.NET Core's HTTP logging line (`Web/HttpLog.fs`: `UseHttpLogging`, one combined entry per request with
      the method, path, query, protocol, status and duration, plus `remote_addr` and `user_agent`) written by the console logger as
      `Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware`, not Thruster's `Request path=... status=...` line. It carries no other
      header and no body; `LOG_REQUESTS=false` leaves the middleware out (`HttpLogTests` LG1-LG4). The client address is the connection's
      after the forwarded headers, an IPv4-mapped address written as IPv4, with no port.
    - Compression is the app's `UseResponseCompression` alone, which now offers zstd after Brotli and Gzip: a browser that offers `br`
      keeps `br`, a client that takes only zstd gets zstd (`HttpLogTests` CZ1-CZ3). `GZIP_COMPRESSION_ENABLED`,
      `GZIP_COMPRESSION_DISABLE_ON_AUTH` and `GZIP_COMPRESSION_JITTER` are not read and there is no BREACH jitter padding (Rails'
      `Rack::Deflater` has none either); `Vary: Accept-Encoding` is on compressed responses only, and an image is not compressed.
  - Class B (Phase 7, unit 7.2; `plans/design-front.md` F9), the response cache, each with the test that pins it
    (`OutputCachingTests` OC1-OC9):
    - It is ASP.NET Core's OutputCache (`Web/OutputCaching.fs`), one base policy per route family that Rails marks `public`, matched
      by the route's exact path shape: `/users/:id/avatar` 30 minutes, `/account/logo` 5 minutes, `/qr_code/:id` 1 year, the Active
      Storage proxy routes 100 years, the disk service's `show` 1 hour (the controllers' own constants). Thruster cached any
      response that said `public` with a positive max-age; nothing else is cached here, so a page is never looked up, stored or
      locked. `CACHE_SIZE` is the cache's capacity and `MAX_CACHE_ITEM_SIZE` the largest body it stores; keys are case-sensitive and
      include the query and the request's `Accept-Encoding` (the entry is the encoded response).
    - A cache hit carries `Age` (seconds in the cache); there is no `X-Cache: hit/miss/bypass` header, which Thruster added to every
      response.
    - A response that sets a cookie is never stored (the hourly session resume on an avatar), and a request with an `Authorization`
      header or a `Range` header bypasses the cache.
  - Class B (Phase 7, unit 7.4a; `plans/design-front.md` F1, F2, F11-F14): an install without `TLS_DOMAIN` is served by the app's own
    host (`Web/Host.fs`: Kestrel's listeners and limits, ConsoleLifetime), not by the front server above (installs with `TLS_DOMAIN` keep
    that front until unit 7.4b, and the bullets above that name it still describe them), each with the test that pins it
    (`HostTests`):
    - `HTTP_PORT` and `TARGET_PORT` (loopback, or `TARGET_BIND`) both serve the app over HTTP/1.1; cleartext HTTP/2 is not offered and
      `H2C_ENABLED` is not read (HS1, HS12). `HTTP_WRITE_TIMEOUT`, `GZIP_COMPRESSION_*` and `DEBUG` are not read either.
    - `HTTP_IDLE_TIMEOUT` is Kestrel's keep-alive timeout and `HTTP_READ_TIMEOUT` its request-header timeout; a body is no longer cut by a
      deadline from the request's start but by Kestrel's minimum data rate (240 bytes a second after 5 seconds), for requests and
      responses alike. A timeout of zero, a negative one or one of 49 days or more means none (HS8, HS10, HS12).
    - `FORWARD_HEADERS` (default on without TLS) decides whether a client's `X-Forwarded-For`, `-Proto` and `-Host` count: on, only from
      Rails' `TRUSTED_PROXIES`; off, never. The client address the ban check and the sign-in throttle see is unchanged (HS7). The front's
      own `X-Forwarded-*`, `Forwarded` and `X-Request-Start` rewriting is gone; the app reads none of them.
    - `MAX_REQUEST_BODY` (0 = none) lowers the app's 16 MiB cap, so the 413 is the app's whichever limit hit (HS9). Stopping is one 10 second
      budget (SIGTERM or SIGINT: Cable sockets get `server_restart`, then connections drain; HS11), not Thruster's 5 seconds inside the app's 10.
    - HSTS is still sent on every response that reaches the edge middleware, including a 500 page (`UseHsts` loses the header there, so it
      stays in `Edge`); `force_ssl`'s own HTTP-to-HTTPS redirect is gone, as production's `assume_ssl` made every request HTTPS before it
      could fire (HS5).
  - `HTTP_IDLE_TIMEOUT`, `HTTP_READ_TIMEOUT` and `HTTP_WRITE_TIMEOUT` above 49 days are clamped to 49 days,
    the longest .NET's timers can wait. Thruster and the Rust port accept any number of seconds; a value
    that large means "never" in all three. Tested by `FrontConfigTests`.
  - Certificates are ordered by a small RFC 8555 client of our own (`Front/Acme.fs`) in place of
    instant-acme, in autocert's cache layout as Rust writes it. It sends `User-Agent`, which RFC 8555
    requires (Pebble refuses requests without it), and renews at most once an hour for CAs whose
    certificates live less than 30 days.

- **Request and response handling** (`Campfire.App`'s `Web/` folder, which unit 2.13 of the .NET way made the whole request
  layer; `rust/crates/kit/src/{adapter,ctx,params,request}.rs` is what it replaced, the reference is Rails 8.1 on Puma 7.2.1):
  - A request for a method a route doesn't have is Rails' 404 page (`ActionController::RoutingError`, as in
    `reference/config/routes.rb`: no route, no `Allow`). Rust's axum router also answers with the 404
    (`method_not_allowed_fallback`) but still adds an `Allow` header; the route table adds none. Tested by `RouteTableTests`
    and `SeamTests` (the table dispatches an unknown path to the 404 page).
  - `Pages.json` writes floats as the json gem does (`1e+15`, `0.00000015`: `Json.generate`), which is what `render json:`
    gives in Rails (json 2.21.2 in the reference). Rust's `Ctx::json` goes through serde_json (`1000000000000000.0`,
    `1.5e-7`). Tested by `RailsCompat.Tests` ("floats match the json gem").
  - A file an upload spooled is deleted when the response completes (`Uploads.assignmentNamed`), as Rails'
    `Rack::TempfileReaper` does. In Rust a `TempPath` goes when the `UploadedFile` drops, so an app that kept the file past
    the request could still read it there.
  - A request header with a byte outside visible ASCII reaches the app as the framework hands it (Rust's `HeaderValue::to_str`
    treated it as absent); only `Host` and `X-Request-Id` are read through that rule, for the redirect and the request id.
  - A POST that `_method=head` turned into a HEAD sends no body and announces no length: the host holds a POST's response
    to its `Content-Length`. A real HEAD keeps the length of the body it did not send (`Rack::Head`). Tested by
    `RequestsTests` (R3b).

- **Cable** (`Campfire.Cable`; `rust/crates/cable` is the port it follows, Action Cable 8.1 on Puma, with websocket-driver, is the
  reference). The transport is ASP.NET Core's WebSocket middleware (`app.UseWebSockets()`), so the handshake, framing, masking,
  control frames and close codes are the framework's. Tested by `ProtocolTests`, `TransportTests`, `LimitTests` and `MemoryTests`.
  - **No WebSocket compression, as Rails.** A client's `permessage-deflate` offer gets no `Sec-WebSocket-Extensions`
    (`DangerousEnableCompression = false`), which removes the Rust port's compression entry. The framework deflates per socket and per
    send, and the Rust saving (one deflate per broadcast shared by every subscriber) is not available to it.
  - **Slow clients are cut, not buffered.** Rails buffers without limit. A client whose outbound queue is 256 frames behind, or whose
    socket has taken nothing for a send of 30 seconds (`SendTimeout`), is disconnected with a TCP abort and no `disconnect` frame (one
    could only queue behind the backlog); its Action Cable client reconnects and resubscribes. `unsubscribed` runs for each of its
    subscriptions. Tested by `LimitTests` (L1, L2).
  - **Limits, as in Rust (Rails has none):** 64 subscriptions and 4 KiB identifiers per connection (beyond them the command is logged
    and ignored), messages of at most 1 MiB (larger closes with 1009, also when sent in fragments), a binary message is logged and
    ignored, invalid UTF-8 in a text message closes with 1007. Tested by `ProtocolTests` (L3, L5, L6) and `LimitTests` (L4, L5).
  - **Origin check:** `Origin` is compared with the request's scheme and host as ASP.NET Core sees them after the trusted-proxy
    forwarded headers and `assume_ssl` (`allow_request_origin?`); behind a proxy that rewrites `Host` and sends `X-Forwarded-Host` from
    a trusted range, the forwarded host is the one compared (Rails compares the raw `Host`).
  - **Authentication is the page cookie scheme** (`AuthenticateAsync` on the `Session` scheme): the same ticket and lookup as every
    page, no `sessions` write and no cookie on the upgrade. The connection looks its session up once more after it is registered
    (a ban or sign-out between the first lookup and the registration would otherwise go unheard; Rails has that gap), in a scope of
    its own. A remote disconnect is sent after the commit that caused it, not inside the transaction as Rails' `deactivate` and
    `ban` do (as in Rust).
  - **Byte level:** JSON inside frames writes `<`, `>` and `&` as themselves where Rails escapes them (`\u003c`; the frames
    decode equal and the golden comparison is on decoded frames); payloads written from anonymous records keep their keys in
    alphabetical order, which is Rails' order for every payload Campfire sends today. The `101 Switching Protocols` is written by
    Kestrel, so `Connection` is `Upgrade` (as websocket-driver writes it; Rust writes `upgrade`) and it carries `Date` and, unless
    the host turns it off, `Server`, which neither websocket-driver nor Rust sends (`ProtocolTests`, "the 101 answers as
    websocket-driver does, plus the host's Date"). The heartbeat (`ping` every 3 seconds) starts at boot, not with the first connection. WebSockets over HTTP/2 are
    the framework's once TLS and HTTP/2 are served (Phase 7).
  - **Memory:** an idle connection holds about 16 KB of managed memory on the server side on the development Mac, 13.6 KB of it the
    host's own (Kestrel, the middleware, a `ManagedWebSocket`) and 2.6 KB Cable's; the Rust port's resident set is about 9 KB per client
    (`bench/results/cable-fanout-dotnetway.md`, `MemoryTests` L7).

- **Views** (`Campfire.Views`; `rust/crates/views` is the port it follows, and its answers are the frozen oracle the tests replay). The templates are
  Falco.Markup, serialized by the library, so the HTML is the same page as Rails' to the loose DOM (the pixel harness's own rule) and not the same
  bytes. One class A entry covers all of it:
  - **Class A (markup bytes):** attribute values and text are encoded by `Core/Html.fs` (every non-ASCII character as a numeric reference, `&#233;`;
    `'` as `&#39;`), there is no indentation or inter-tag whitespace the ERB does not render, empty attributes are written as the ERB writes them
    and the layout is built from nodes. A page is therefore a few percent shorter than Rails' (the room page 376,570 bytes against Rust's 416,122), a
    Turbo-Frame response's `Content-Length` changes with it, and the account's custom CSS is the one place the text itself differs: a `</style` (any
    case) in it is written `<\/style`, so a hostile stylesheet cannot close the element (Rails writes it raw). `bench/lib/expected-sizes.json` stores the
    size of each probed page.
  - The view fragment cache is the framework's HybridCache over a size-limited memory cache (32 MiB by default, `CAMPFIRE_FRAGMENT_CACHE_MB`),
    not Rails' Redis store: entries are evicted by `MemoryCache` compaction rather than `MemoryStore`'s prune to three quarters, they
    expire after seven days, and a key is Rails' `cache_key_with_version` with the partial and variant but no template digest (the
    cache doesn't outlive the process). Fragments are fetched up front and awaited, never inside a template. JSON (the bot API) is
    not cached. Tested by `FragmentsTests` (F1, F2) and the message tests (F3).
  - `capitalize`, `User#initials`' word boundary and the case of a name's first letters use tables generated from
    Rust's standard library (`Helpers/UnicodeTables.fs`), not .NET's own casing, which leaves out special
    casing (`ß` is `SS`), the Turkish dotless i and characters Unicode added since. Rust's `capitalize` is what
    the references' `String#capitalize` is closest to; a name whose first letter has special casing prints as Rust
    prints it. Tested by `HelpersTests` ("capitalizes like rust").
  - `String#downcase` of a user's name (the room forms' `data-value`) is Rust's `str::to_lowercase`, final-sigma rule included
    (`ΑΣ` is `ας`, `ΑΣΑ` is `ασα`), from `Case_Ignorable` and `Cased` ranges generated from Rust's standard library with the
    other Unicode tables. .NET's `ToLowerInvariant` writes `σ` for every sigma and leaves out special casing. Tested by
    `HelpersTests` ("to_lowercase ...").
  - Timestamps in cache keys and `to_fs(:epoch)` are `DateTimeOffset`s, which hold 100 ns where jiff holds 1 ns.
    A key shows microseconds, so no key differs; a record dated before 1970 or outside the years 1 to 9999 is not
    representable (no record is).
- **Rich text** (`Campfire.RichText`; the pipeline runs on AngleSharp's HTML5 parser, where Rails runs Nokogiri/Gumbo and the Rust port html5ever). Every difference below needs a hostile, hand-made body; none appears in the
  658-case corpus's ordinary bodies or the 164 seed bodies, and `tests/Campfire.RichText.Tests/corpus/known-differences.json` lists the corpus cases that differ, checked both ways. Class A: the serializer is Nokogiri's, so `<` and `>` inside attribute values are written as themselves, as Rails writes them (the F# port wrote `&lt;` and `&gt;`; the trees are equal).
  Class B:
  - End-tag-shaped text inside raw-text and RCDATA elements is lowercased by AngleSharp's tokenizer: `<textarea></FooBar></textarea>` shows `</foobar>` (403 of 13,872 fuzz comparisons against Rails).
  - Raw-text-named elements inside `svg` or `math`, and a few table-part shapes, follow AngleSharp's tree builder: the text is dropped with the foreign element, or `tbody`/`tr` land outside the table (15 cases).
  - Caps Gumbo does not have: a body of more than 10,000 elements or 40,000 tokens, or whose `<html>` and `<body>` tags carry more than 400 attributes between them, is refused as a body past Gumbo's
    limits is (the message renders `messages/_unrenderable`, its plain text is empty and logged); Rails renders it. AngleSharp's tree builder is quadratic on such bodies (`plans/design-richtext.md` R3, S3;
    `Html5Tests` L2, L3); no body the composer produces comes near them.
  - Limits (400 deep, 400 attributes, 10,000 elements) are counted on the markup's body parse; in a non-body context that changes only the depth boundary. A body that ends inside a tag that has reached the attribute limit
    renders its remaining content where Rails renders nothing. Depth is counted on the elements AngleSharp creates, not on Gumbo's stack, so two boundary shapes go the other way: 400 `<b>` then `</p>` (the
    implied `<p>` is element 401) is refused where Rails renders it, and formatting elements reopened past the limit then a comment render where Rails refuses them (`Html5Tests` L1).
  - The 2025 `select` model: AngleSharp keeps markup inside `option` and `select` that Gumbo flattens. `<isindex>` is still expanded to a search form as the 2008 standard did; the sanitizer drops the form, the sentence stays as text.
  - Comments after an explicit `</body>` that content follows arrive after that content. Element-context parses keep foreign content inside.
  - A layout-only case (corpus fuzz 398): an `action-text-attachment` whose replacement begins with another `action-text-attachment` inside a foreign element stays inside it (Gumbo breaks out beside it).
  - Opengraph URLs are read by `System.Uri` behind an RFC 3986 character check, not Ruby's parser: `mailto:nobody`, a host with empty labels, `[v1.x]` and a port past 65535 give an embed without a link, where Rails raises or links.
  - Markup AngleSharp's tree builder throws on is refused as a body past a limit is (`ParseError.ParserFailed`): the message renders `messages/_unrenderable`, its plain
    text is empty and logged, and posting it stores it as it came. AngleSharp 1.8.4 and 1.8.5 throw a `NullReferenceException` on `<p><select><xmp></xmp><select>x` (an
    `xmp` in a select closes the paragraph, and the select with it); Rails renders it. Before, one such stored body failed every page showing it (security review F1,
    `plans/design-richtext.md` section 11).
  - Upstream: the sibling-walk cost, the tokenizer's raw-text lowercasing and the select-mode `NullReferenceException` are to be reported to AngleSharp (Q1).

## License

MIT ([`MIT-LICENSE`](MIT-LICENSE)). The F# port is © 2026 Chris Kluis; Campfire, the Rust port, its parity harness and
vectors are © 37signals, LLC.
