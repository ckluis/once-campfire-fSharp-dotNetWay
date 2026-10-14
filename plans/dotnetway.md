# theDotNetWay: the third Campfire in F#

Status: planned 2026-10-13, nothing built. This file and the D1-D9 decisions
(`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`, copied in short below) win over `AGENTS.md` and
`plans/fsharp-port.md` wherever they conflict.

## Goal

The Campfire a .NET team would write: framework-first, the least and most familiar code, the UI pixel-identical to Rails,
and still several times Rails' speed. It starts as a fork of the published F# port (`ckluis/once-campfire-fSharp`,
`a78972f`) and replaces one layer at a time behind the parity harness (D6).

| # | Decision (Chris, 2026-10-10 and 2026-10-13) |
|---|---|
| D1 | F# on ASP.NET Core and F#-friendly libraries, not C# |
| D2/D9 | Views in Falco.Markup, replacing the hand-written UTF-8 writer. Falco stays for routing |
| D3 | EF Core over Microsoft.Data.Sqlite on the Rails schema, no migrations; no-tracking reads on hot paths; raw SQL through EF only where LINQ can't say it. Compiled queries were asked for: see Q1 |
| D4 | Action Cable protocol on ASP.NET Core's WebSocket middleware; Rails' JS client unchanged |
| D5 | OutputCache, HybridCache/IMemoryCache, ASP.NET Core authentication and antiforgery, plus a shim for Rails' signed and encrypted cookies |
| D6 | Fork the port; one layer at a time, each behind the parity harness |
| D7 | Fewer backend KLOC than Rust by upstream's cloc rules, pixel-identical, several times Rails' speed. Stretch: Rust's speed |
| D9 | The most .NET way possible: a framework or mainstream package wherever one does the job, even at some speed cost; hand-written code only where parity demands it |

What "match Rust's implementation" meant in the port no longer applies. Rust and the port stay useful as the record of
*what* Rails does (their tests, vectors and README notes); *how* is now the framework's.

## Starting state (2026-10-13, `dotnetway` at `a78972f`)

- `dotnet build CampfireFs.slnx -c Release` on the untouched checkout: succeeded, 0 warnings, 25 s.
- `bin/verify` (warnings as errors, then every test) with the parity seeds copied in from onCrack (`parity/.seed`, gitignored):
  **passed, 1,038 tests: 1,031 succeeded, 7 skipped, 0 failed, 24 s wall.** The skips: three Db tests that need the reference
  image (`fixtures match ruby row for row`, `export database for rails`, `scenario matches ruby`), a timing test, Cable's
  `record_reference`, the Linux-only transparent-huge-pages test and the Linux-only TLS ticket test.
- Not run here: the parity harness, `bench/`, the differentials (they need colima, the reference image or mise). Phase 0 runs
  the baselines.
- KLOC: **39.4 by upstream's rules, corrected; 38.2 as cloc 2.10 prints it.** See "Counting" below; `bin/kloc` reproduces both.

### Counting (D7)

Upstream's rule: cloc 2.10 `--by-file --skip-uniqueness`, backend code plus executable template code, tests, vendored code,
generated files, build tools and static data out. onCrack's round 5 method is reused as is (`bin/kloc`; the static-data list
is onCrack's `kloc.py`).

**cloc 2.10 undercounts F#.** It takes `(*` as the start of an F# block comment wherever it appears, so every `COUNT(*)` in a
SQL string, and `(*` inside a string or a `//` comment, hides the lines up to the next `*)`: `Campfire.Db/Models.fs` counts 353
lines of code instead of 1,163, and the whole port 38,180 instead of 39,429. The sources have no real block comments.
`bin/kloc` prints both columns. **This plan budgets and gates on the corrected column**, the honest and higher one. (Rust's
31.0 may carry the mirror image of this artifact: a few non-test Rust files have `/*` inside strings, such as `"*/*"` in
`integrations/net/http.rs`. Not re-counted here; the bar stays 31.0 as onCrack measured it.)

## 1. Layer inventory

KLOC is `bin/kloc`'s corrected column at `a78972f`, static data left out (shown separately). Tests are `[<Fact>]`/`[<Theory>]`
attributes in `tests/Campfire.<P>.Tests`.

| Project | What it does now | KLOC | Static data (out) | Tests | Differentials and vectors | Parity checks that guard it |
|---|---|---:|---:|---:|---|---|
| `Campfire.Ruby` | Ruby string behaviour: ERB escaping, `to_i`/`to_f`, `Float#to_s`, `CGI.escape`, Rack byte ranges | 0.63 | | 21 | `vectors/ruby_core.json` | indirectly every page (escaping, numbers on screen) |
| `Campfire.RailsCompat` | Message verifier and encryptor, key generator, cookie metadata envelope, Active Support JSON, Marshal, GlobalID/SGID, signed ids, content disposition, bcrypt | 1.47 | | 47 | `vectors/rails_compat.json`, `campfire_sessions.json`; `reference-tools/rails_compat_verify_rust.rb` (Rails verifies what we sign) | `auth/*`, every signed-in state (session cookie), Turbo stream names in `realtime/*` |
| `Campfire.Routes` | Path helpers mirroring `config/routes.rb` | 0.08 | | 5 | `vectors/campfire_routes.json` | links on every page (server, live layers) |
| `Campfire.Assets` (+ `Assets.Build`, a build tool, not counted) | Propshaft digests and CSS compilers, importmap, embedded asset blob, `ActionDispatch::Static` | 0.43 | | 14 | reference digests (`ReferenceTests`) | every pixel cell (CSS, JS, images), the network layer (asset paths) |
| `Campfire.Db` | SQLite on the Rails schema: one writer thread, reader pool, WAL checkpointer, hand-written SQL per model, columns by position, Active Record timestamps, callbacks, fixtures | 3.31 | 0.32 (`Fixtures.fs`) | 182 | `bin/db-differential` (fixtures and a long scenario vs Active Record; schemas; Rails boots on our DB) | every state (the seeds' data), `rooms/*`, `messages/*`, `search/*` |
| `Campfire.RichText` | Action Text pipeline: a 3.6 KLOC port of html5ever (tokenizer, tree builder, entities), an arena DOM with Nokogiri's serializer, a Loofah-compatible sanitizer, attachments, rails_autolink, plain text, Ruby URI | 6.19 | 0.20 (`WordRanges.fs`) | 78 | `bin/richtext-differential` (vs the Rust crate, 20 parse contexts), Rust's corpus and hardening tests | message bodies: `rooms/show/*`, `messages/*`, `interactions/edit_form*`, `search/results*`, mentions, unfurls |
| `Campfire.Storage` | Active Storage blobs, disk service, Marcel MIME detection, variants (NetVips), previews and analysis (ffmpeg/ffprobe), the proxy file server | 1.77 | 3.00 (`Tables.fs`, Marcel's) | 41 | `vectors/storage.json`, `bin/storage-media-vectors` | `interactions/composer/attachment*`, `interactions/lightbox`, `users/avatar*`, `realtime/attachment_sent`, `account/edit` (logo) |
| `Campfire.Cable` | RFC 6455 framing by hand over the upgraded stream, shared `permessage-deflate`, a seqlock ring pub/sub, Action Cable protocol, Turbo stream names | 1.60 | | 65 | golden frames recorded from Rails (`GoldenTests`), `bench/cable/run` | the cable layer of every page, `realtime/*` (13 states) |
| `Campfire.Kit` | Rack/ActionDispatch in F#: `Ctx`, params and strong params, multipart (a multer port), cookies, Rails session, CSRF by `Sec-Fetch-Site`, flash, formats, responses, gzip, page splicing; the front server (Thruster: TLS, ACME, HTTP/2, h2c, cache, compression, request log) | 8.34 | | 231 | `bin/kit-differential` (vs the Rust kit), `bench/front/run`, Pebble ACME | `auth/*`, `errors/*`, `bot_api/*`, the network layer of every page, `controllers_a/replay.py` |
| `Campfire.Views` | 80 templates as F# modules writing UTF-8, helpers, the byte-bounded fragment cache, recorded pages, view-models | 5.79 | 0.88 (`UnicodeTables.fs`, `TranslationsTable.fs`) | 137 | `bin/views-differential` (byte for byte vs Rust), `bin/views-test-parity`, `ParityATests`/`GoldenB` DOM tests | every cell: server, live, aria and pixel layers |
| `Campfire.App` | Boot, config, the 177-row route table, the `ApplicationController` chain, useragent gem port, controllers, presenters, channels, jobs, Active Storage endpoints, integrations (Net::HTTP replica, SSRF guard, opengraph, Web Push, VAPID, webhooks, search), QR encoder | 9.82 | 0.20 (`SearchWordRanges.fs`) | 200 | `controllers_a/replay.py`, golden Cable frames, recorded opengraph/webhook/push cases | everything; `auth/incompatible_browser` (user agent), `users/profile/qr_code`, `bot_api/*`, `realtime/*` |
| **Total** | | **39.43** | 4.60 | 1,021 | | 206 states × lean matrix, 5 seeds |

## 2. Replacement map, per layer

Package facts were checked against the NuGet registration API and the GitHub repos on 2026-10-13 (table in the appendix).
Three questions were settled by spikes in a scratch project on this machine (.NET SDK 10.0.401, EF Core 10.0.13, Falco 5.2.0,
Falco.Markup 1.4.0); their results are quoted where they matter and summarised in the appendix.

### 2.1 Data: `Campfire.Db` -> EF Core

**Replaces it:** `Microsoft.EntityFrameworkCore.Sqlite` 10.0.13 (dotnet/efcore, active; pulls `Microsoft.Data.Sqlite` 10.0.13
and `SQLitePCLRaw.bundle_e_sqlite3` 2.1.13). One `CampfireDb : DbContext`, registered with `AddDbContextPool` (pooled,
one per request), mapping the 15 Rails tables by name with the fluent API in `OnModelCreating`. No migrations; the schema is
Rails'. Deleted: `Database.fs` (writer thread, reader pool, checkpointer), `Sql.fs`, `Columns.fs`, `Schema.fs`'s statement
plumbing, `Testing.fs`, most of `Models.fs`'s SQL.

- **Entities from F#.** `[<CLIMutable>]` records, one per table, with Rails' column names mapped explicitly. CLIMutable gives EF the
  parameterless constructor and setters it needs for change tracking; F# code still builds and copies them as records.
  Nullable columns are `Nullable<'T>` for values and `string | null` for text (F# 9 nullness, `<Nullable>enable</Nullable>`),
  **not `option`**: EF has no option mapping, and `EntityFrameworkCore.FSharp` (the package; `EFCore.FSharp` is its repo name)
  is dormant at 6.0.7 for EF 6 (last release 2022-06-26). `Option.ofObj`/`Option.ofNullable` at the edges where F# logic
  wants options.
- **Rails' row format.** One `ValueConverter` for `datetime(6)`: Active Record writes `YYYY-MM-DD HH:MM:SS` plus `.ffffff` only
  when the microseconds are non-zero, truncated to microseconds; EF's default wrote `2026-10-13 12:00:00.123` in the spike.
  The port's `Time.fs` logic moves into the converter (about 30 lines). Enums (`role`, `status`, room `type` STI strings
  `Rooms::Open`/`Closed`/`Direct`) are value conversions too; rooms keep one entity with a `Type` string, not EF TPH, so the
  stored discriminator stays byte-identical.
- **Timestamps and callbacks.** A `SaveChangesInterceptor` sets `created_at`/`updated_at` the way Active Record does (from
  `TimeProvider`, so the harness's frozen clock reaches it). After-commit work (broadcasts, push and webhook jobs, search
  reindex) runs explicitly after `SaveChangesAsync` returns, in the model functions, as `after_commit` does: not in
  interceptors, where ordering would be implicit.
- **Rich-text and Active Storage rows.** `action_text_rich_texts` (`record_type = 'Message'`, `name = 'body'`) and
  `active_storage_blobs`/`attachments`/`variant_records` are plain entities with their polymorphic string columns; blob
  `metadata` stays JSON text. Keys, checksums, `service_name` and `variation_digest` are computed as Rails does (kept code in
  Storage and RailsCompat), so Rails reads our rows and we read Rails'.
- **Writes and the single writer.** EF opens a transaction per `SaveChanges`; Microsoft.Data.Sqlite begins it `IMMEDIATE`
  (checked in the spike; Rails' `default_transaction_mode: immediate`). Writers in different requests then serialise in SQLite itself, as Rails 8's
  do. To avoid busy-wait spins under the post-a-message load, every write goes through one process-wide
  `SemaphoreSlim(1)` (a 20-line `Writes.run`), the moral equivalent of the port's writer thread without the thread. Reads
  never take it. WAL mode, `synchronous=NORMAL` and the other pragmas of `database.yml` go on the connection through a
  `DbConnectionInterceptor` (`ConnectionOpened`). SQLite's own auto-checkpoint does the WAL (Rails' behaviour); the
  checkpointer thread goes.
- **Updates.** `ExecuteUpdateAsync`/`ExecuteDeleteAsync` for Active Record's `update_columns`, `touch` and `delete_all`. EF 10's
  non-expression setter lambda works from F# (spike: one row updated). Tracked entities only where a record is loaded and
  changed in the same request anyway.
- **Search (FTS5).** `message_search_index` is an FTS5 virtual table. Reads: `Messages.FromSql($"... JOIN message_search_index
  ... MATCH {q} ...")` composed with LINQ (spike: works, 1 hit). Writes: `Database.ExecuteSqlAsync($"INSERT INTO
  message_search_index(rowid, body) VALUES ({id}, {plain})")`. The boot check that adds
  `index_messages_on_room_id_and_created_at` if missing (a Rust known difference) stays, as one `ExecuteSql`.
- **Queries and their cost (spike, in-memory SQLite, 1 row, 20,000 runs).** Raw prepared ADO.NET 1.3 us; EF `FromSql` 15 us;
  EF LINQ written with F# lambdas 51 us, of which 30 us is F# turning the lambdas into expression trees on every call; F#'s
  `query { }` builder 4,235 us. So: LINQ with lambdas by default, `FromSql` on the hot paths that need it, **never `query { }`**
  (a lint in `bin/verify`). **Compiled queries do not work from F#**: F# hands a nested lambda inside an expression to EF as a
  call to `QuotationToLambdaExpression(SubstHelper(...))` instead of a quoted lambda, and `EF.CompileAsyncQuery` throws
  `InvalidCastException` (`MethodCallExpression1` to `LambdaExpression`). EF 10's precompiled queries are C# interceptors,
  not usable either. See Q1.
- **Fixtures.** `Fixtures.fs` (static data) stays as the seed loader for tests, written through EF.

Kept hand-written: the datetime converter, `Writes.run`, the pragma interceptor, the FTS5 SQL, the domain logic the port has
in `Models.fs` (memberships, unread marking, bans, bot tokens, room touch), the boot index check.
**After: 1.6 KLOC** (from 3.31).
**Risk: high.** Data compatibility (every row Rails reads back), the per-query cost of F# lambdas (it is the main speed risk
of the project), and write contention on the post-a-message workload. Guarded by `bin/db-differential` and the phase's
speed check before anything else is replaced.

### 2.2 Rich text: `Campfire.RichText` -> AngleSharp + HtmlSanitizer

**Replaces it:** `AngleSharp` 1.8.4 (active, net10.0 target) for parsing and serializing; `HtmlSanitizer` 9.2.1039 (mganss,
active; stable targets netstandard2.0/net8.0, depends on AngleSharp >= 1.7.2 and AngleSharp.Css >= 1.0.2) for the allowlist
layers. Deleted: `Html5ever/` (3.58 KLOC), `Dom.fs`, `Sanitizer.fs`, most of `RubyUri.fs` and `RubyExt.fs`.

- **Equivalence bar.** What shows on screen, not bytes: the sanitized body must produce the same DOM (after the harness's loose
  normalisation, section 4) and the same pixels as Rails for every body in the seeds, the Rust corpus and the hardening
  inputs, and must never let through what Loofah strips. Byte differences in serialization (attribute quoting, `&nbsp;` vs
  U+00A0, `<br>` vs `<br/>`) are class A differences.
- **The sanitizer.** One `HtmlSanitizer` instance per allowlist (Action Text's, the presentation filters'), configured with
  Loofah's tags, attributes and URI schemes. Two Loofah rules HtmlSanitizer does differently get small `RemovingTag`/
  `RemovingAttribute` handlers: HTML elements outside the list are *unwrapped* (children kept), foreign elements (svg, math)
  are removed with their contents. Loofah's attribute re-escaping is a serialization detail (class A).
- **Gumbo's limits.** Nokogiri refuses a tree deeper than 400 or a tag with more than 400 attributes. AngleSharp parses first;
  the limit is checked on the parsed tree (one walk). Inputs over the limits are refused as before; the only difference is an
  element with more than 400 attributes that are mostly duplicates (Gumbo counts them as it reads), recorded as a class B
  difference. Request bodies stay capped at 16 MiB, so the full parse is bounded.
- **Autolink on the DOM.** rails_autolink runs regexes over serialized HTML, which the port had to harden against a stored XSS
  (`<`/`>` in attribute values). Here it walks text nodes (outside `a`, `pre`, `code`) and splits them into text and `<a>`
  nodes, with rails_autolink's URL and e-mail patterns and its trailing-punctuation rules, `target="_blank"` included. The
  XSS class is gone by construction. Same links, same pixels.
- **Attachments, mentions, unfurl cards, plain text.** `Content.fs`, `Attachables.fs`, `PlainText.fs` and `Filters.fs` are
  rewritten over AngleSharp's DOM (`QuerySelectorAll("action-text-attachment")`, `ReplaceWith`). The Ruby round trips the
  port reproduced step by step (serialize, re-parse) are dropped where the DOM gives the same tree.
- **URIs.** `System.Uri` plus the two checks Campfire makes (http/https only, tweet URL normalisation); Ruby's `URI.parse`
  error classes go (class B for malformed URLs in unfurls).

Kept hand-written: the allowlists (data), the two Loofah handlers, the depth/attribute check, autolink's patterns, the
attachment and mention rendering, plain-text conversion.
**After: 1.4 KLOC** (from 6.19).
**Risk: medium-high.** Malformed or hostile markup is where the parsers differ; the hostile inputs move from byte comparison
to "same DOM after sanitizing, and nothing Loofah strips survives". Security review by Opus at the phase close.

### 2.3 Views: `Campfire.Views` -> Falco.Markup

**Replaces it:** `Falco.Markup` 1.4.0 (FalcoFramework, stable, netstandard2.0; last release 2025-09-10; Falco 5.2.0 already
depends on it). Each template becomes a function returning `XmlNode`, rendered with `renderHtml`/`renderNode`. Deleted:
`Core/Out.fs` (the UTF-8 writer), the recorded-page machinery, `Tag.fs`'s attribute builder, most of `Helpers/Forms.fs`,
`bin/views-differential` and `bin/views-test-parity`.

- **Escaping (found in the spike, the main risk of this layer).** Falco.Markup 1.4.0 HTML-encodes text (`Text.enc`, through
  `WebUtility.HtmlEncode`) but **writes attribute values raw**: `Attr.value "a\"b"` rendered `value="a"b"`. There is no
  encoding variant in the API (`Attr.create`, `Attr.createBool` only). So the project gets one small module, `Html`, whose
  attribute helpers encode (`Html.attr name value = Attr.create name (HtmlEncoder.Default.Encode value)`), and `bin/verify` gets a
  lint that fails on `Attr.` calls with a non-literal value outside that module, and on `Text.raw` with a non-literal argument outside
  the modules that emit already-sanitized HTML (rich-text bodies, cached fragments): the other way an unescaped string reaches a page. The hostile-text cases of the views
  differential (quotes, angle brackets, ampersands in every string field) become a test that renders each template and
  parses it back with AngleSharp.
- **Pixel-identical, not byte-identical.** `WebUtility.HtmlEncode` writes `é` as `&#233;` and emoji as numeric references;
  they render the same. **Whitespace is the pixel risk**: ERB leaves newlines and spaces between inline elements, which render
  as a space; Falco.Markup writes none. Whitespace between children of flex and grid containers and between block elements
  doesn't render; between inline elements (an icon and a label in a `span`, links in running text) it does. Templates keep a
  `Text.raw " "` where the ERB had whitespace between inline siblings, and the pixel gate on all 206 states finds the ones
  missed. `pre`, `textarea` and attribute values keep their exact text.
- **Hooks survive.** Every `data-controller`, `data-action`, `data-*-target`, `data-turbo-*`, `id`, `aria-*`, `role`,
  `<template>` and `<turbo-stream>` attribute and element is kept as is; the loose DOM layer (section 4) compares all of them.
- **Fragment caching.** Rails caches three partials (`messages/_message` with `presentation-v3`, `messages/boosts/_boost`,
  `users/sidebars/rooms/_direct`). They become `HybridCache` entries (`Microsoft.Extensions.Caching.Hybrid` 10.10.0, active)
  holding the rendered string, keyed like Rails' (`record.cache_key_with_version` plus the partial and variant), so a record
  change produces a new key and no eviction is needed; the L1 memory cache gets a size limit. The byte-bounded fragment cache,
  `Splice`, recorded pages and their SHA bookkeeping go.
- **Layouts.** `content_for` regions become `XmlNode list` arguments of the layout function; the Turbo-Frame layout is a second
  layout function.

Kept hand-written: 80 template functions (the bulk), the helpers Rails has (`image_tag`, `button_to`, `link_to` variants,
`turbo_stream_from`, `message_presentation`, translations), the `Html` encoding module, view-models.
**After: 4.0 KLOC** (from 5.79; ERB is 1,978 non-blank lines over 80 files and Falco.Markup runs about 1.5 lines per ERB
line).
**Risk: high.** Whitespace pixels, and an XSS hole if one attribute escapes the lint. Speed: string rendering through
`StringBuilder` instead of pooled UTF-8, a few microseconds a page.

### 2.4 Kit: `Campfire.Kit` -> ASP.NET Core

The kit stops existing as a project. Controllers become Falco `HttpHandler`s over `HttpContext` (D9; the port's rule
"controllers take `Ctx`, never `HttpContext`" is withdrawn). What remains lives in `Campfire.App/Web/`.

- **Sessions (`session_token`).** ASP.NET Core cookie authentication (`AddAuthentication().AddCookie`, in the shared framework)
  with:
  - `Cookie.Name = "session_token"`, `HttpOnly`, `SameSite=Lax`, path `/`, `Secure` when the request is HTTPS (Rails'
    `assume_ssl`/`force_ssl` behaviour), expiry 20 years (Rails' `permanent`), `SlidingExpiration = false`;
  - `TicketDataFormat` = `RailsSignedTicketFormat`, an `ISecureDataFormat<AuthenticationTicket>` whose `Protect` writes the
    Rails 7.1+ signed cookie (`MessageVerifier` with the `_rails` metadata envelope, purpose `cookie.session_token`, JSON
    serializer, HMAC-SHA1 over the PBKDF2-SHA256 key derived from `SECRET_KEY_BASE`, as `Campfire.RailsCompat/Cookies.fs` does) around the session's token, and whose `Unprotect`
    verifies one and returns a ticket carrying only that token. So the cookie on the wire is the one Rails writes and reads,
    and a switch either way keeps people signed in;
  - `CookieAuthenticationEvents.OnValidatePrincipal` looks the token up in `sessions` (EF), rejects it if gone, attaches the
    user, and refreshes `last_active_at` hourly with a re-issued cookie (Rails' `Session#resume`); `OnRedirectToLogin` sends
    HTML requests to `/session/new` and others a 401, as Rails' `require_authentication` does.
  - Bot API requests authenticate by bot key through a second, tiny scheme (`AuthenticationHandler`), selected by a policy
    scheme on the path.
  Kept hand-written: `RailsSignedTicketFormat` (about 60 lines) over `RailsCompat`'s verifier. `CookieManager` stays the
  default chunking one: the cookie is ~150 bytes, never chunked.
- **The Rails session (`_campfire_session`), flash and return-to.** ASP.NET Core session (`AddSession` over
  `AddDistributedMemoryCache`), cookie name `_campfire_session`, `HttpOnly`, `SameSite=Lax`. Campfire's session holds only the
  flash and the return-to URL; both may be lost on a restart or on a switch to or from Rails (class B: a sign-in that
  redirects through a version switch lands on the root instead of the page asked for). Flash is a 30-line helper on
  `ISession` (set, read once, `flash.now`). Rails' encrypted cookie is no longer read or written.
- **CSRF.** ASP.NET Core antiforgery (`AddAntiforgery`, shared framework) with Rails' names: `FormFieldName =
  "authenticity_token"`, `HeaderName = "X-CSRF-Token"`, its cookie named `_campfire_csrf` (HttpOnly, Lax),
  `SuppressXFrameOptionsHeader = true`. The layout renders `<meta name="csrf-param">`/`<meta name="csrf-token">` as Rails'
  `csrf_meta_tags` does, and `button_to`/forms carry the hidden field, so Turbo and `@rails/request.js` send the header exactly
  as they do against Rails. Validation is Falco's `Request.validateCsrfToken` (over `IAntiforgery`) in the before-action chain,
  skipped for bot-key requests (Rails' `unless: -> { authenticated_by.bot_key? }`). This brings the pages *back* to Rails'
  markup (the port and Rust omit the tags, a Rust known difference that goes away). The cost, class B: a tab opened before a
  switch to or from Rails fails its first write once with a 422 and needs a reload. No shim for Rails' masked tokens: it would
  need the Rails session cookie and is worth one reload per upgrade. Check in this phase that antiforgery's
  `Cache-Control: no-cache, no-store` on token-issuing responses doesn't defeat `fresh_when` on the messages page (the ETag is
  computed from records, section 2.4 "Responses").
- **Params.** `IFormCollection`, route values and `System.Text.Json` (`JsonDocument`) for JSON bodies. Rails' bracket names
  (`message[body]`) are read as the flat keys they are; `params.require`/`permit` becomes a 60-line helper that answers 400
  for a missing key (Rails' `ParameterMissing`). The multer port goes: Kestrel and `FormOptions` read multipart (class B:
  malformed multipart bodies, such as one without its closing boundary, may be accepted or refused with another status).
  Limits from `MAX_REQUEST_BODY`/Rust's 16 MiB cap on `KestrelServerOptions.Limits` and `FormOptions`.
- **Formats.** `respond_to` with html, turbo_stream and json: a 50-line negotiation on `Microsoft.Net.Http.Headers.MediaTypeHeaderValue`
  that keeps Rails' order rules (Turbo's `text/vnd.turbo-stream.html` first when present).
- **Responses, ETags, errors.** `fresh_when @messages` is a weak ETag computed from the records' cache keys and a 304 when it
  matches; `stale?(etag:)` for avatars and the logo likewise. Error pages serve Rails' `public/404.html`, `422.html`,
  `500.html` byte for byte (`UseStatusCodePages` re-executing to a static file handler, plus an exception handler that serves
  `500.html`; `ProblemDetails` is out, as the decisions say). `/up` keeps its exact body (a health check mapped to Rails'
  response writer).
- **Response caching and compression.** `AddOutputCache` (shared framework) with a base policy that caches only what the app
  marks `Cache-Control: public` (avatars, logos, QR codes: Rails' `expires_in ..., public: true`), tag-evicted on avatar or
  logo change with `IOutputCacheStore.EvictByTagAsync`. `AddResponseCompression` (shared framework) with gzip and Brotli for
  HTML, JSON and Turbo streams; static assets are precompressed at build time by `MapStaticAssets`. The hand-written gzip
  framing, CRC-32 and zstd (`ZstdSharp.Port`) go (class A/B: Thruster also offers zstd; clients that accept zstd also accept
  gzip).
- **The front server (Thruster's job).** Kestrel only:
  - listeners from `HTTP_PORT`/`HTTPS_PORT`/`TARGET_PORT` with `HttpProtocols.Http1AndHttp2` (HTTP/3 off); TLS through
    `ServerCertificateSelector`; HTTP to HTTPS redirect with `UseHttpsRedirection` when `TLS_DOMAIN` is set (Thruster's behaviour);
  - `X-Sendfile`-like serving is `Results.File(path, contentType, enableRangeProcessing = true)` (Kestrel uses sendfile where
    it can); Rack byte-range code goes;
  - request log: `ILogger` message templates through `UseHttpLogging` with a fixed field set, no bodies; the batched console
    writer goes (class B: log format);
  - h2c (cleartext HTTP/2 detection) goes (class B; Thruster offers it, nothing in Campfire's deployment uses it);
  - **ACME**: `Certes` 4.1.0 (fszlin/certes; released 2026-09-27, active again; targets net8.0/net10.0) in a
    `BackgroundService` that orders and renews ECDSA P-256 certificates for `TLS_DOMAIN` 30 days before expiry, answers
    HTTP-01 at `/.well-known/acme-challenge/{token}` on the HTTP port, and keeps the certificate and account key in autocert's
    `DirCache` layout under `STORAGE_PATH` (an install that ran Thruster keeps its certificate and account: no Let's Encrypt
    rate limit on a switch). Evaluated and rejected: `LettuceEncrypt` (archived, net6.0, last release 2024-05),
    `ACMESharpCore` and `FluffySpoon.AspNet.EncryptWeMust` (dormant since 2020), terminating TLS in a separate proxy (not a
    drop-in for Rails' image). TLS-ALPN-01 needs the ClientHello's ALPN list, which Kestrel's TLS callbacks don't expose; the
    port reads it off the connection by hand. See Q2.

Kept hand-written: the Rails ticket format, the bot-key scheme, flash, strong-params helper, format negotiation, ETag helper,
the ACME glue on Certes, the `DirCache` reader/writer, Kestrel listener config, the output-cache policy.
**After: 1.3 KLOC** (from 8.34), in `Campfire.App/Web/`; the project is deleted.
**Risk: high.** Signed-in users across a switch (the round-trip gate), CSRF behaviour on every form and Turbo request,
antiforgery's cache headers, and ACME on a real domain (Pebble in tests, Let's Encrypt staging once).

### 2.5 Cable: `Campfire.Cable` -> WebSocket middleware + `Channel<T>`

**Replaces it:** `UseWebSockets` and `HttpContext.WebSockets.AcceptWebSocketAsync(WebSocketAcceptContext(SubProtocol =
"actioncable-v1-json"))` (shared framework). Deleted: `Socket.fs` (framing, shared deflate), `Pubsub.fs` (seqlock ring,
`Wake`), most of `JsonText.fs`.

- **Compression.** None, as Rails: Action Cable's `websocket-driver` negotiates no extensions. The Rust and port difference
  ("permessage-deflate without context takeover, compressed once per broadcast") goes away.
- **Fan-out.** One bounded `Channel<ReadOnlyMemory<byte>>` per connection (capacity 256, `BoundedChannelFullMode.Wait` with
  `TryWrite`), a writer loop per connection draining it into `SendAsync`, and a `ConcurrentDictionary<stream,
  ImmutableArray<subscription>>` registry. A broadcast serialises its frame once and `TryWrite`s it to each subscriber; a full
  channel means a client that stopped reading, which is closed (Rust's "clients that don't read for 30 seconds disconnect"
  becomes "a client 256 frames behind disconnects", class B). Heartbeat pings every 3 s from one `PeriodicTimer` in a
  `BackgroundService`.
- **Protocol.** `welcome`, `ping`, `confirm_subscription`, `reject_subscription`, `disconnect` (with `reason` and
  `reconnect`), subscribe/unsubscribe/message commands, Rust's limits (64 subscriptions, 4 KiB identifiers, 1 MiB messages).
  JSON through `System.Text.Json` (`Utf8JsonWriter` and `JsonDocument`; no reflection serializer needed). Frames decode to the
  same JSON as Rails'; escaping may differ (STJ escapes non-ASCII as `\uXXXX`, class A).
- **Channels and auth.** The connection is authenticated from the same cookie scheme (`AuthenticateAsync` on the upgrade
  request). Turbo's signed stream names keep `RailsCompat`'s verifier. Revocation on ban/deactivate closes the user's
  connections after commit, as now.

Kept hand-written: the Action Cable protocol (about 300 lines), the registry, the channel classes in `Campfire.App/Channels`.
**After: 0.65 KLOC** (from 1.60).
**Risk: high.** Frame ordering and subscription confirmation timing that `realtime/*` captures depend on; fan-out speed
(per-socket `SendAsync` without the shared frame cache) is reported, not optimised.

### 2.6 Assets: `Campfire.Assets` -> `MapStaticAssets`

**Replaces it:** `MapStaticAssets` (shared framework, .NET 9+). Spike, F# `Microsoft.NET.Sdk.Web` project with Falco: the
static web assets pipeline ran, wrote `web.staticwebassets.endpoints.json`, fingerprinted every file
(`application.ah3pz4gezj.css`, `controllers/hello.z7ea3mj4ir.js`), precompressed `.gz` variants, and exposed each endpoint's
`StaticAssetDescriptor` (with `label` = the logical name and `fingerprint`) as endpoint metadata; `ImportMapDefinition`
(`Microsoft.AspNetCore.Components`, shared framework) rendered an import map from F# without Razor. `ResourceAssetCollection`
is *not* attached to plain endpoints (Razor attaches it), so the logical-name table is built from the descriptors at startup
into a `FrozenDictionary`.

- `Campfire.App` becomes an Sdk.Web project. A build target stages `reference/app/assets` and the vendored gem assets
  (`turbo-rails`, `stimulus-rails`, `actioncable`, `activestorage`, `actiontext`, `lexxy`, Trix) into the gitignored web
  root before discovery, under base path `assets`.
- `image_tag`, `stylesheet_link_tag` and the import map look names up in the table. **The browser has to get the same CSS in the
  same order**: the layout emits the stylesheets in Propshaft's order (`stylesheet_link_tag :app` is the sorted file list),
  and the import map pins the same names as `config/importmap.rb` (`pin`, `pin_all_from` with `under:`, `preload:` ->
  `modulepreload` links), now pointing at fingerprinted URLs.
- Propshaft rewrites `url()` in CSS to digested paths; `MapStaticAssets` doesn't. Relative `url()`s resolve to the
  unfingerprinted endpoint beside them (served with an ETag): same pixels, other URLs (class A).
- `reference/public` (error pages, icons, `robots.txt`) is served by `UseStaticFiles` with the headers of
  `public_file_server.headers`.
- `Assets.Build` (the Propshaft and importmap port, a build tool) and `OVERRIDES.md`'s override mechanism are re-evaluated:
  overrides stay as files in the staged web root.

Kept hand-written: the importmap pin list builder (about 60 lines, reading `config/importmap.rb`'s three verbs), tag helpers.
**After: 0.12 KLOC** (from 0.43), folded into `Campfire.App`.
**Risk: medium.** CSS order and JS module resolution (one wrong pin is a dead Stimulus controller, which the pixel gate on
interaction states catches).

### 2.7 Jobs, integrations, push (in `Campfire.App`)

- **Jobs.** One `BackgroundService` per job kind (`Room::PushMessageJob`, `Bot::WebhookJob`, media analysis) reading a bounded
  `Channel<'Job>`, `JOB_CONCURRENCY` readers each. Same in-process model as Rust's known difference (lost on a crash).
- **HTTP clients.** `IHttpClientFactory` (`Microsoft.Extensions.Http`, shared framework) with three named clients (opengraph,
  push, webhook), each with `AddResilienceHandler`/`AddStandardResilienceHandler` from `Microsoft.Extensions.Http.Resilience`
  10.10.0 (dotnet/extensions, active): Rust's timeouts as attempt and total timeouts, opengraph's 16-at-once as a concurrency
  limiter. **Retries are off for POSTs** (`Retry.DisableForUnsafeHttpMethods()`): a bot webhook must not be delivered twice.
  The `Net::HTTP` replica (`Http.fs`, 569 lines: header order and spelling) goes; bots see .NET's request headers (class B).
- **SSRF guard.** `SocketsHttpHandler.ConnectCallback` resolves the host, refuses private, loopback, link-local and other
  special ranges (`System.Net.IPNetwork.Contains` over the list `Guard.fs` has), and connects to the checked address. It runs
  for every connection, redirects included, so each hop is re-checked without a redirect loop of our own. Response size caps
  through a length-limited read. (About 120 lines instead of 357.)
- **Opengraph.** `AngleSharp`'s parser and `QuerySelectorAll("meta[property^='og:'], meta[name]")` replace the libxml2 meta
  scanner and its entity table (class B for malformed pages, which libxml2's HTML4 parser reads differently).
- **Web Push.** `WebPush` 1.0.13 (web-push-libs/web-push-csharp, active, net10.0 asset) for VAPID and RFC 8291 encryption,
  sent through the factory's push client (its `WebPushClient` takes an `HttpClient`). It depends on `Portable.BouncyCastle`
  1.9.0 (no advisories or deprecation on NuGet today; re-check with `dotnet list package --vulnerable` in the phase). Fallback
  if it fails that check: keep the port's 119-line RFC 8291 encryption on `System.Security.Cryptography`.
  `Lib.Net.Http.WebPush` 3.3.1 is the slower-moving alternative (no net10.0 asset, last release 2025-03).
- **QR code.** `QRCoder` 1.8.1 (Shane32/QRCoder, active) replaces the 452-line rqrcode port if its SVG is pixel-identical on
  `users/profile/qr_code` (same ECC level, mask choice and module size); otherwise the port's encoder stays. Decided by the
  pixel gate in the phase.
- **User agent.** `MyCSharp.HttpUserAgentParser` 3.1.6 (active, net10.0) for browser, version and platform, feeding
  `ApplicationPlatform`'s predicates (the Ruby file is 60 lines of regex checks, kept as is) and Rails' `allow_browser
  versions: :modern` table. The 813-line useragent-gem port goes (class B: exotic or malformed user agents may be classified
  differently; the five engines the harness drives and the `auth/incompatible_browser` cases must not).
  `UAParser` is dormant (NuGet package from 2021); `DeviceDetector.NET` pulls LiteDB and YamlDotNet for far more than needed.
- **Redaction.** `Microsoft.Extensions.Compliance.Redaction` 10.10.0 at the logging boundary for e-mail addresses, session and bot
  tokens, push endpoints (the attribute-driven classification is a C# generator; the `Redactor` API is called explicitly).

**After (integrations and jobs): 0.9 KLOC** (from 2.47 corrected).
**Risk: medium.** Webhook double delivery (retries off for POST, tested), the guard's completeness (port its tests one for one).

### 2.8 Config, time, logging, DI (in `Campfire.App`)

- `IOptions<CampfireOptions>` bound from the environment variables Rails reads (`SECRET_KEY_BASE`, `TLS_DOMAIN`,
  `VAPID_*`, `JOB_CONCURRENCY`, ...), with `ValidateDataAnnotations`-free validation delegates and `ValidateOnStart()`: a bad
  setting fails the boot, not the first request. Invalid VAPID keys keep disabling push at boot (Rust's behaviour) rather than
  failing it.
- `TimeProvider` everywhere (`TimeProvider.System` in production; `FakeTimeProvider` from
  `Microsoft.Extensions.TimeProvider.Testing` 10.10.0 in tests). The parity harness's frozen clock reaches the app through
  the same environment variable as now, read into a fixed-offset `TimeProvider` at boot. `KitClock.fs` and `RailsCompat/Clock.fs` go.
- `ILogger` message templates; `LoggerMessage` source generation is C# only, so templates are plain calls. `FakeLogger`
  (`Microsoft.Extensions.Diagnostics.Testing` 10.10.0) asserts redaction in tests.
- Built-in DI: `DbContext` pool, HTTP clients, options, caches, the Cable registry and job queues as singletons. Keyed
  services where the port matches on a kind (the three HTTP clients).
- `Boot.fs` becomes a `WebApplication` builder (about 150 lines). Falco keeps the routes: the 177 rows of `config/routes.rb` as
  a `HttpEndpoint list` (`get`, `post`, `mapGet` ...) with Rails' path patterns.
- `FrozenDictionary` for the build-once tables (asset names, route recognitions, translations).

**After (App outside integrations, jobs and the Kit remains): 4.6 KLOC** (controllers 2.3, presenters 0.7, channels 0.42,
Active Storage endpoints 0.3, route table 0.2, concerns and platform 0.25, boot and config 0.25, misc 0.2), from 7.35.

### 2.9 Storage: `Campfire.Storage`

NetVips 3.2.0 (active) over libvips 8.16.1 and ffmpeg/ffprobe 7.1.5 stay: there is no framework image or video pipeline,
and the byte-identical thumbnails, posters and metadata are what keep Rails and us agreeing on the files. The system libvips
stays (not `NetVips.Native`), as the port's Dockerfile builds it.

- Marcel's MIME tables stay (static data, outside the count): the detected `content_type` is persisted, and decides whether
  Rails, after a rollback, shows a file as an image, a video or a download. `FileExtensionContentTypeProvider` would answer
  differently for many types.
- The disk service's key layout (`ab/cd/key`), checksums, variation keys (Marshal-encoded, signed) and `variation_digest` stay:
  they are data compatibility.
- `FileServer.fs` becomes `Results.File` with ranges; `TempFile.fs` uses `Path.GetTempFileName`; `JsonValue.fs` uses
  `System.Text.Json.Nodes`; `Process.fs` keeps `System.Diagnostics.Process` (it is the framework).
- `Microsoft.AspNetCore.Http.Features.FormOptions` limits apply to direct uploads (Rust's 16 MiB cap, 422 for nonnumeric
  sizes).

**After: 1.3 KLOC** (from 1.77).
**Risk: low.**

### 2.10 Ruby, RailsCompat, Routes: one `Campfire.Rails` project

The three small projects merge into one (`Campfire.Rails`) once their callers are on the framework.

- **Kept (formats Rails reads or writes):** `MessageVerifier`, `MessageEncryptor` (AES-256-GCM), `KeyGenerator`
  (`Rfc2898DeriveBytes.Pbkdf2`, framework), the `_rails` metadata envelope, `Marshal` (the subset variation keys need),
  GlobalID/SGID, signed ids, `Password` (`BCrypt.Net-Next` 4.2.1, active; the port pins 4.0.3), secrets derivation, path
  helpers, and the Ruby behaviours visible on screen (`Float#to_s` and `number_to_human_size` for sizes and durations,
  `to_i` on params).
- **Replaced:** Active Support JSON (`Json.fs`) by `System.Text.Json` with an encoder that escapes `<`, `>`, `&`, U+2028 and
  U+2029 as Rails does; ERB escaping by `HtmlEncoder`/Falco.Markup; content-disposition approximations by
  `ContentDispositionHeaderValue.SetHttpFileName` (class B: the legacy `filename=` parameter transliterates differently; the
  `filename*=` that browsers use is the same); Rack byte ranges by the framework; `Clock.fs` by `TimeProvider`.

**After: 1.1 KLOC** (from 2.18 for the three).
**Risk: low**, and the cookie round trip guards the part that matters.

### 2.11 Testing

xunit.v3 4.0.2 (the port pins 3.2.0) and `Microsoft.AspNetCore.Mvc.Testing` 10.0.13 (`WebApplicationFactory<Program>`; the
F# entry module gets a `Program` type marker). HTTP-level tests replace the internal-unit tests of the deleted code: kit
tests become requests against the app (status, headers, cookies, body), view tests become rendered pages parsed with
AngleSharp, Cable tests drive a `ClientWebSocket` through the test server. Kept unchanged: the golden vectors of what Rails
writes (cookies, routes, storage, Ruby behaviours), the db and storage tests that check rows and files, and the recorded
reference cases (opengraph, webhook, push). Retired with their layers: `bin/kit-differential`, `bin/richtext-differential`,
`bin/views-differential`, `bin/views-test-parity` (they compared against Rust byte for byte). The parity harness and
`bin/db-differential` are the outside gates.

Test projects follow the source projects; tests of deleted projects move to `Campfire.App.Tests` or go with the code.

### Budget

| Layer | Now (corrected) | After | What goes |
|---|---:|---:|---|
| Db | 3.31 | 1.60 | writer/reader topology, SQL strings, column lists, row readers |
| RichText | 6.19 | 1.40 | html5ever port, arena DOM, sanitizer, Ruby URI |
| Views | 5.79 | 4.00 | UTF-8 writer, recorded pages, fragment cache, tag builder |
| Kit | 8.34 | 1.30 (into App) | Ctx, params, multipart, cookies, session, CSRF, formats, gzip, splice, front server |
| Cable | 1.60 | 0.65 | framing, deflate, ring pub/sub |
| Assets | 0.43 | 0.12 (into App) | Propshaft serving, embedded blob |
| App: integrations and jobs | 2.47 | 0.90 | Net::HTTP replica, libxml2 scanner, push crypto, rqrcode |
| App: the rest | 7.35 | 4.60 | useragent gem port, Ctx plumbing, route-table glue |
| Storage | 1.77 | 1.30 | file server, temp files, JSON value |
| Ruby + RailsCompat + Routes | 2.18 | 1.10 | JSON, escaping, ranges, dispositions, clocks |
| **Total** | **39.43** | **16.97** | |

**Projected: about 17 KLOC against Rust's 31.0 (55% of it), with a 30% overrun still 22.**
Hand-written code that stays is parity-forced: the Rails cookie and message formats, the persisted data formats, the
templates, the Action Cable protocol, Loofah's two sanitizer rules, autolink's patterns, ACME glue.

## 3. Order and phases

Risk first: the layers that can sign people out, corrupt data or break real time go before the large but well-understood
view rewrite. One layer per phase, each a workflow of sequential units with an independent verifier (section 5).

**Gates common to every phase**

1. `bin/verify` green (Release, warnings as errors, every test), plus `CAMPFIRE_REQUIRE_SEED=1`.
2. **Pixel parity on the states the layer can affect**: `bin/pixel-parity --only <globs>` (imported from onCrack in Phase 0;
   the screenshot layer, every seed), every cell equal to Rails'. Plus the `aria` layer equal and the new loose DOM layer
   (section 4) equal on the same cells.
3. **The Rails cookie round trip** (`bin/cookie-roundtrip`, new in Phase 0): sign in on the Rails image, switch the same
   storage to ours, the user is still signed in and can post; sign in on ours, switch back to Rails, same. Plus the
   `campfire_sessions.json` vectors and `rails_compat_verify_rust.rb` pointed at our output.
4. **Data compatibility**: `bin/db-differential` (fixtures and the long scenario written through Active Record and through
   ours compare equal; Rails boots on a database we wrote and serves its pages). Every phase that writes rows.
5. **A quick speed check**: `bench/quick` (F# only, five workloads, c=1 and c=16, 3 reps) against the stored Rails and Rust
   medians (`bench/results/phase7-final/`). Reported, not optimised, **unless a workload falls under 5x Rails at c=16**
   (the floor: "several times"; the port runs 27-159x Rails at c=16 today, from post a message to the room page; Rails' sidebar
   is its fastest page at 599 req/s, so the floor is ~3,000 req/s there and ~1,650 on posting). A workload under the floor blocks the phase until it is back over.
6. `bin/kloc` before and after, recorded in the phase's notes in this file.

The **full pixel gate** (`bin/pixel-parity` with the full matrix, every seed, all engines and viewports) runs once, at the
end (Phase 9), and once after Phase 4 (views), the phase that touches every cell.

| # | Phase (layer) | Units | Phase-specific gates | Deleted | KLOC before -> after (project total) | Speed check |
|---|---|---|---|---|---|---|
| 0 | Baseline and tooling | rename the image to `campfire-dotnetway:app` (`parity/bin/candidate`, `Dockerfile`, `bench/`); import `bin/pixel-parity`(+`-report`) from onCrack and add the `aria` and loose-DOM report; `bin/cookie-roundtrip`; run the full pixel gate on the untouched port (it must be all-equal, else record the residue); `bench/quick` baseline; three Opus design notes (EF mapping, auth and cookies, Cable) appended here | all gates green on the untouched port | nothing | 39.4 -> 39.4 | baseline |
| 1 | Data -> EF Core | `CampfireDb` and mapping for 15 tables; converters and interceptors; model functions one table group at a time (users/sessions, rooms/memberships, messages/rich text/boosts, storage rows, search, push/webhooks/bans); App call sites; delete the topology | `bin/db-differential` all green; all states, `--fast` first then the lean matrix on `default` and `crowd`; db tests ported | `Database.fs`, `Sql.fs`, `Columns.fs`, `Testing.fs`, SQL in `Models.fs` and friends | 39.4 -> 37.4 | five workloads; the EF cost lands here |
| 2 | Kit -> ASP.NET Core | auth handler and Rails ticket format; bot-key scheme; antiforgery; session and flash; params, formats, ETags, error pages; controllers from `Ctx` to `HttpContext` (by controller group); response compression and OutputCache | cookie round trip both ways; `controllers_a/replay.py` (masks for the antiforgery token and the session cookie); `auth/*`, `errors/*`, `bot_api/*`, `account/*`, then the lean matrix on all seeds (every controller changed) | `Ctx.fs`, `Adapter.fs`, `Params.fs`, `Body.fs`, `Multipart.fs`, `Cookies.fs`, `Session.fs`, `Format.fs`, `Request.fs`, `Response.fs`, `Deflater.fs`, `Splice.fs`, `bin/kit-differential` | 37.4 -> 31.7 | five workloads; antiforgery and auth costs |
| 3 | Cable -> WebSocket middleware | protocol over `WebSocket`; per-connection bounded channels; heartbeat service; auth from the cookie scheme; delete framing and ring | golden frames compared decoded; `realtime/*` on every engine (they are smoke states); `bench/cable/run` reported | `Socket.fs`, `Pubsub.fs`, `JsonText.fs`, the deflate path | 31.7 -> 30.8 | Cable fan-out reported |
| 4 | Views -> Falco.Markup | the `Html` module and the attribute lint; layouts; templates by directory (rooms, messages, users/sidebars, accounts, sessions/first run/pwa/welcome); HybridCache fragments; delete the writer | lean matrix on all seeds, then the **full matrix once**; the hostile-text render test; Opus XSS review of every template | `Core/Out.fs`, `FragmentCache.fs`, recorded pages, `Tag.fs`, `bin/views-differential`, `bin/views-test-parity` | 30.8 -> 29.0 | five workloads; the string-render cost lands here |
| 5 | Rich text -> AngleSharp + HtmlSanitizer | sanitizer configuration and Loofah handlers; DOM-based autolink; content, attachments, plain text; depth and attribute limits | corpus and hardening inputs: same DOM after sanitizing, nothing Loofah strips survives; `rooms/show/*`, `messages/*`, `search/*`, `interactions/edit_form*`; Opus security review | `Html5ever/`, `Dom.fs`, `Sanitizer.fs`, `RubyUri.fs`, `bin/richtext-differential` | 29.0 -> 24.2 | room, messages and search pages |
| 6 | Assets -> MapStaticAssets | Sdk.Web; staging target; descriptor table; import map and tag helpers; public files | every state `--fast` (any wrong pin breaks interaction states), then lean on `default` | `Campfire.Assets` project, `Assets.Build` | 24.2 -> 23.9 | asset hits reported |
| 7 | Front server -> Kestrel + middleware | listeners and TLS; Certes ACME service and `DirCache`; HTTPS redirect, HSTS, HTTP logging; delete `Front/` | Pebble ACME test; Let's Encrypt staging once on a real domain (operator); `bench/front/run` reported; `auth/sign_in` and `rooms/show/designers` over TLS | `Front/` (2.7 KLOC), `ZstdSharp.Port` | 23.9 -> 21.5 | front-server page and asset rows |
| 8 | Jobs, integrations, push, config and time | BackgroundService queues; HttpClientFactory clients with resilience and the guard; AngleSharp opengraph; WebPush; QRCoder (pixel decides); user-agent parser; IOptions, TimeProvider, logging, redaction | recorded opengraph, webhook and push cases; guard tests one for one; `users/profile/qr_code`, `auth/incompatible_browser`, `users/push_subscriptions/*`, `bot_api/*` | `Http.fs`, `Opengraph*` scanners, `WebPushEncryption.fs`, `UserAgent.fs`, `Rqrcode.fs` (if QRCoder passes), `KitClock.fs`, `Config` plumbing | 21.5 -> 18.5 | post a message (jobs on the write path) |
| 9 | Storage, Rails helpers, tests; the close | Storage trims; merge Ruby/RailsCompat/Routes into `Campfire.Rails`; test consolidation; README "Known differences" for theDotNetWay; the final bench (`bench/run --apps reference,rust,dotnetway`, all suites) | **full pixel gate**, every seed and the full matrix; cookie round trip; db-differential; `bin/kloc` final | leftovers | 18.5 -> 17.0 | full re-baseline with Rails and Rust re-measured |

Each phase's notes (units done, gate evidence, KLOC, speed table, differences recorded) are appended under "Phase log" below as
it closes, the way `plans/fsharp-port.md` records its phases.

## 4. Known-differences policy

D9 accepts differences from the port, and from Rails, that a user cannot see and an install cannot feel. Three classes:

| Class | What | Examples | How it is recorded |
|---|---|---|---|
| **A: byte-level, invisible** | Serialization and transport details that don't change the DOM tree, pixels, accessibility tree or decoded data | attribute quoting and order, character references vs literal characters, whitespace that doesn't render, `<br>` vs `<br/>`, JSON escaping, header order, gzip bytes, asset URLs and fingerprints, import-map formatting, ETag values | once, as a class, in `README.md` "Known differences" ("Byte-level"); the harness normalises them in the loose DOM layer; the server-HTML and network-body layers are reported, not gated |
| **B: behaviour, visible to an operator, integration or edge user** | Something a person or another system could notice | antiforgery tokens (a tab from before a switch needs a reload), flash and return-to lost across a restart or switch, HTTP-01 only for ACME (if Q2 says so), malformed multipart statuses, exotic user agents, malformed unfurl pages, webhook request headers, log format, no zstd/h2c, slow Cable clients cut at 256 queued frames, the 400-duplicate-attributes edge | one README entry each, citing the reference file it departs from, with a test that pins the new behaviour and a parity mask or allowlist entry only if no mask can express it (`parity/allowlist.yml` keeps its owner field); the phase verifier signs off each |
| **C: never acceptable** | | any pixel difference in a captured cell; any aria-tree difference; a live-DOM difference that survives the loose normalisation (a lost `data-*`, `id`, Turbo or Stimulus hook); Cable frames that decode to different JSON or arrive in another order on a subscription; data Rails can't read or reads differently; a user signed out by a switch either way; status codes on non-hostile requests; a security regression (XSS, CSRF, SSRF, sanitizer bypass) | not recorded: fixed before the phase closes |

The loose DOM layer (Phase 0): the harness's live `body.outerHTML`, parsed and compared as a tree with attributes as sets,
character references decoded, and whitespace-only text nodes dropped except inside `pre`, `textarea` and inline formatting
contexts where the pixel layer would show them. It is what "data attributes and Turbo/Stimulus hooks must survive" is checked
by.

Rust's known differences the port carried: the CSRF and WebSocket-compression ones go away (back to Rails' behaviour); jobs,
push, cookies-written-on-change, caching ETags, SQLite index, media, request limits, Cable limits, unfurling and webhook limits
stay as they are; "front server" ones are re-stated for Kestrel in Phase 7.

## 5. Model and agent split for the build workflow

| Role | Model | Does | Time box |
|---|---|---|---|
| Designer | Opus (`claude-opus-5-5`) | the three design notes of Phase 0 (EF mapping and the write path, cookies/auth/CSRF, Cable), and each phase's unit list and acceptance criteria for Phases 1, 2, 3 and 5 | 45 min per note |
| Builder | Sonnet (`claude-sonnet-5-5`) | one unit at a time: code, tests, `bin/verify`, the phase's `--fast` pixel run, `bin/kloc`; commits per unit | 90 min per unit; a unit that overruns is split, not extended |
| Gate runner | Sonnet | the lean or full pixel gate, `bin/db-differential`, `bin/cookie-roundtrip`, `bench/quick`; writes the evidence into the phase log | as long as the gate; see below |
| Verifier | Opus | independent review of each phase: the diff, the gate evidence, the class B entries, a security pass on Kit, Views and RichText phases | 45 min |
| Fixed steps | Haiku, only as `claude-haiku-5-5` (the `haiku` alias resolved to 4.5 in workflows) | disk check (`df -h /`, stop under 12 GB; 15 GB before a benchmark), `bin/clean-docker`, `bin/clean-rust-builds`, seed copy, `bin/kloc` table into the log, image rebuild | 10 min |

- One agent at a time on the colima VM (6 GB: two parity compares OOM-kill Chromium) and concurrency 1 on any fan-out, so a
  session-limit hit strands at most one unit.
- Gates under ten minutes (`bin/verify`, `bin/kloc`, `--fast` on a few states) run in the foreground, blocking. Long ones (the
  lean or full pixel gate, `bench/quick`, `bench/run`) run as a detached script (`nohup ... > parity/out/<run>/log 2>&1 &`
  writing a status file at the end) started by the orchestrator, which polls the status file with a bounded until-loop. A
  subagent never backgrounds a job and then waits on it.
- Each unit starts from `git status` clean on `dotnetway`, commits with `Co-Authored-By: Claude ...` only, and never pushes.

## 6. Open questions for Chris

Both answered (decisions file, "Decided after the design": Q1 option 1, Q2 option 1). Of the "Settled here" list below, two
lines are superseded by the decisions of 2026-10-13 and the design notes: the target is **.NET 11** (moved to in Phase 1's first
unit), and CSRF is **.NET 11's token-less `ICsrfProtection`**, not antiforgery tokens (`plans/design-kit-auth.md` K5; section
2.4's CSRF bullet is superseded the same way). Where a design note and sections 2.1, 2.4 or 2.5 disagree, the note wins.

| # | question | options |
|---|---|---|
| Q1 | D3 asks for compiled queries, but `EF.CompileAsyncQuery` throws on any F# lambda that nests another (spike), and EF 10's precompiled queries are C#-only. F# LINQ costs ~51 us a query (30 us of it F#'s lambda conversion), `FromSql` ~15 us, raw ADO.NET 1.3 us. What do hot paths use? | 1. EF LINQ by default, `FromSql` on the five workloads' queries, no compiled queries; measure in Phase 1 and stop only if a workload falls under 5x Rails (Recommended) <br> 2. A ~60-line expression rewriter that turns F#'s `QuotationToLambdaExpression` calls back into quoted lambdas so compiled queries work (unproven, ours to maintain) <br> 3. A small C# project holding the compiled queries (fastest; breaks D1) |
| Q2 | ACME: Thruster answers TLS-ALPN-01 on 443 and HTTP-01 on 80. Certes covers the protocol, but TLS-ALPN-01 needs the ClientHello's ALPN list, which Kestrel doesn't expose. | 1. HTTP-01 only, on Certes; an install must have port 80 reachable, which Campfire's self-hosting guide already asks for (`reference/docs/self-hosting.md`: "allow traffic on ports 80 and 443") (Recommended) <br> 2. Keep TLS-ALPN-01 with the port's hand-written ClientHello reader (~150 lines kept) <br> 3. Drop in-process ACME; require a proxy in front for TLS (not a drop-in for Rails' image) |

Settled here, with the reason:

- **"Several times Rails" is a floor of 5x Rails at c=16 on each of the five workloads.** The port is 27-159x, so the floor
  leaves room for EF and string rendering while staying a claim worth making; anything under it blocks a phase.
- **CSRF uses ASP.NET Core antiforgery under Rails' names, no shim for Rails' tokens.** D5 names antiforgery; it puts the
  pages back to Rails' markup; a shim would have to decrypt Rails' session cookie for a one-reload benefit at upgrade time.
- **Flash and return-to live in ASP.NET Core session (memory), not Rails' encrypted cookie.** They are the only things in it,
  and losing them across a restart or a switch costs a redirect to the root.
- **No `option` in entities; no EntityFrameworkCore.FSharp.** The package is dormant at EF 6; `Nullable`/nullness types map
  natively.
- **Marcel's MIME tables stay.** The persisted `content_type` is data compatibility; they are static data outside the count.
- **QRCoder replaces rqrcode only if the pixel gate passes on the QR state**, decided in Phase 8 by measurement.
- **Projects shrink to seven** (`Rails`, `Db`, `RichText`, `Storage`, `Cable`, `Views`, `App`): Kit and Assets fold into App,
  Ruby, RailsCompat and Routes into Rails. The port's rule against edges Rust lacks no longer applies.
- **Target .NET 10 (LTS)**. .NET 11 is due 2026-11-10 (STS); measure its runtime async after Phase 9, as the decisions say,
  before moving.
- **The differentials against Rust retire with their layers**; the harness, the Rails vectors and `bin/db-differential` are the
  gates that compare with Rails itself.
- **Count with the corrected cloc column** (`bin/kloc`); report the raw column beside it so the number is comparable with
  onCrack's table.

## Appendix A: packages (checked 2026-10-13)

| Package | Version (date) | Targets | Repo, status |
|---|---|---|---|
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.13 (2026-10-13) | net10.0 | dotnet/efcore, active (11.0.0-rc.2 also out) |
| Microsoft.Data.Sqlite | 10.0.13 (2026-10-13) | netstandard2.0 | dotnet/efcore, active (the port pins 10.0.12) |
| EntityFrameworkCore.FSharp | 6.0.7 (2022-06-26) | net6.0, EF 6 | efcore/EFCore.FSharp, dormant: not used |
| AngleSharp | 1.8.4 (2026-10-07) | netstandard2.0, net8.0, net10.0 | AngleSharp/AngleSharp, active |
| AngleSharp.Css | 1.1.2 (2026-09-11) | netstandard2.0, net8.0, net10.0 | active (HtmlSanitizer's dependency) |
| HtmlSanitizer | 9.2.1039 (2026-08-28); 10.0.1075-beta | netstandard2.0, net8.0 | mganss/HtmlSanitizer, active |
| Falco | 5.2.0 (2025-12-21); 6.0.0-beta1 (2026-07-27) | net8.0-net10.0 | FalcoFramework/Falco, active |
| Falco.Markup | 1.4.0 (2025-09-10) | netstandard2.0 | FalcoFramework/Falco.Markup, slow (stable) |
| Microsoft.Extensions.Caching.Hybrid | 10.10.0 (2026-09-09) | net8.0-net10.0 | dotnet/extensions, active |
| Microsoft.Extensions.Http.Resilience | 10.10.0 (2026-09-09) | net8.0-net10.0 | dotnet/extensions, active |
| Microsoft.Extensions.TimeProvider.Testing | 10.10.0 | net8.0-net10.0 | dotnet/extensions, active |
| Microsoft.Extensions.Compliance.Redaction | 10.10.0 | net8.0-net10.0 | dotnet/extensions, active |
| Microsoft.Extensions.Diagnostics.Testing | 10.10.0 | net8.0-net10.0 | dotnet/extensions, active |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.13 (2026-10-13) | net10.0 | dotnet/aspnetcore, active |
| xunit.v3 | 4.0.2 (2026-10-09) | net8.0, net472 | xunit/xunit, active (the port pins 3.2.0) |
| Certes | 4.1.0 (2026-09-27); 5.0.0-beta.1 | netstandard2.0, net8.0, net10.0 | fszlin/certes, active |
| LettuceEncrypt | 1.3.3 (2024-05-20) | net6.0 | natemcmaster/LettuceEncrypt, **archived** |
| ACMESharpCore | 2.2.0.148 (2020-08-20) | netstandard2.0 | dormant |
| WebPush | 1.0.13 (2026-04-28); depends on Portable.BouncyCastle 1.9.0 | net8.0-net10.0, netstandard2.1 | web-push-libs/web-push-csharp, active |
| Lib.Net.Http.WebPush | 3.3.1 (2025-03-09) | netstandard2.0, net6.0 | tpeczek, slow |
| QRCoder | 1.8.1 (2026-10-13) | netstandard2.0+ | Shane32/QRCoder, active |
| MyCSharp.HttpUserAgentParser | 3.1.6 (2026-04-01) | net8.0-net10.0 | MyCSharp/HttpUserAgentParser, active |
| UAParser | 3.1.47 (2021-05-26) | netstandard | ua-parser/uap-csharp, package dormant |
| DeviceDetector.NET | 6.5.2 (2026-07-30) | net8.0-net10.0 | active, heavy dependencies |
| NetVips | 3.2.0 (2026-01-01) | net6.0 | kleisauke/net-vips, active |
| BCrypt.Net-Next | 4.2.1 (2026-05-19) | netstandard2.0, net10.0 | BcryptNet/bcrypt.net, active (the port pins 4.0.3) |
| FSharp.SystemTextJson | 1.4.36 (2025-06-13) | netstandard2.0 | Tarmil, slow: not needed (no reflection serialization of unions) |
| FSharp.Core | 10.1.402 (2026-10-13); 11.0.101-rc2 also out | netstandard2.0/2.1 | dotnet/fsharp, active (the port pins 10.1.401; stay on the 10.1 line with SDK 10.0.4xx) |

In the shared framework (no package): cookie authentication, antiforgery, session, OutputCache, ResponseCompression,
HttpLogging, WebSockets, `MapStaticAssets`, `ImportMapDefinition`, `IHttpClientFactory`, `System.Threading.Channels`,
`TimeProvider`, `FrozenDictionary`, `System.Net.IPNetwork`.

Not usable from F# (C# source generators or interceptors): System.Text.Json source generation, `[LoggerMessage]`,
`[GeneratedRegex]` (use `RegexOptions.Compiled` regexes in module-level values), `[LogProperties]`, EF precompiled queries,
`dotnet ef dbcontext optimize` (emits C#). Roslyn analyzers don't analyse F#: `TreatWarningsAsErrors` stays, FSharpLint is
optional.

## Appendix B: spikes (2026-10-13, scratch projects, not in the repo)

| Question | Result |
|---|---|
| EF Core 10 maps `[<CLIMutable>]` F# records onto a Rails-shaped table | yes: insert with `RETURNING`, no-tracking reads, `Nullable<int64>` column |
| `ExecuteUpdate` from F# (EF 10's non-expression setter) | works |
| `EF.CompileAsyncQuery` on an F# lambda with a nested `Where` | fails at first execution: `InvalidCastException`, F# emits `QuotationToLambdaExpression(SubstHelper(...))` where EF expects a quoted lambda; the overloads also need explicit type arguments |
| Cost per query, 1 row, in-memory SQLite, Release | raw prepared ADO.NET 1.3 us; EF `FromSql` 15.2 us; EF LINQ with F# lambdas 50.6 us (building the query alone 29.8 us); F# `query { }` 4,235 us |
| `FromSql` over an FTS5 join composed with LINQ | works |
| Microsoft.Data.Sqlite 10.0.13 `BeginTransaction()` (what `SaveChanges` uses) | `IMMEDIATE`: a second connection's write got `SQLITE_BUSY` while the first held an empty transaction |
| EF's default `DateTime` text | `2026-10-13 12:00:00.123` (Rails writes `.123000`): converter required |
| `MapStaticAssets` in an F# Sdk.Web project with Falco 5.2.0 | works: fingerprinted names, precompressed `.gz`, `StaticAssetDescriptor` metadata with `label` and `fingerprint`; `ResourceAssetCollection` not attached to plain endpoints |
| `ImportMapDefinition` from F#, no Razor | works |
| Falco.Markup 1.4.0 escaping | text encoded (`WebUtility.HtmlEncode`, non-ASCII as numeric references); **attribute values not encoded**; no whitespace between elements |
| Falco 5.2.0 security helpers | `Request.validateCsrfToken`, `Response.ofHtmlCsrf`, `Falco.Security.Xsrf.antiforgeryInput`/`getToken`, `Request.ifAuthenticated` |

## Phase log

(Appended as phases close.)

### Phase 0: baseline (2026-10-13, untouched port code, `dotnetway` at `1a687ea`)

Tooling units: `a43066b` (image rename, `dotnetway` app in `bench/`), `5841a67` (`bin/pixel-parity` with the aria and loose-DOM layers),
`1035553` (`bin/cookie-roundtrip`), `1a687ea` (AGENTS.md). Baseline gates run on `campfire-dotnetway:app` built from `1a687ea` (label checked,
digest `247e17441067`). Evidence (slim, no screenshots) is under `bench/results/dotnetway-p0/`.

**Pixel gate, full lean matrix, run as `bin/pixel-parity --only "<dir>/**"` slices, one at a time** (the first attempt used `<dir>/*`, which does not
cross `/` and matched only the top level of each directory; its logs are kept under `pixels/first-attempt-star-globs/`). Slices: auth, errors, pwa,
welcome, bot_api, search, account, users, autocompletable, messages, interactions, rooms, and `realtime` as 13 single-state runs.

| Seed | Screenshots pixel-equal | aria equal | Loose DOM equal | Other layers (server / live / network / cable), reported not gated |
|---|---:|---:|---:|---|
| default | 860 / 860 | 860 / 860 | 348 / 860 (512 different) | 873/874 / 860/860 / 860/860 / 860/860 |
| crowd | 24 / 24 | 24 / 24 | 24 / 24 | all equal |
| custom_styles | 32 / 32 | 32 / 32 | 20 / 32 (12 different) | server 32/33, rest equal |
| first_run | 16 / 16 | 16 / 16 | 16 / 16 | all equal |
| restricted | 8 / 8 | 8 / 8 | 8 / 8 | all equal |
| **Total** | **940 / 940** | **940 / 940** | **416 / 940 (524 different)** | |

940 is onCrack's round-3 count; its 944 includes 4 `search/results/order` cells that exist only there. 0 pixel differences, 0 not captured in the final
counts. The server-layer differences are `pwa/manifest` (allowlisted, `allowed`) and its custom_styles twin. Fragment states (bot_api 4, autocompletable 2,
and the other response states) are counted in the report as pass on the server layer.

**The finding: the loose DOM layer is not green on the untouched port.** 524 cells in 112 states (messages 120, interactions 166, realtime 96, rooms 88,
pwa 32, search 22) differ, all of one kind: Rails emits a whitespace-only text node (`" "`) that the port does not, 106,440 removed lines over 447 cells in the
full corpus, and in a 60-cell sample 12,776 of 12,778 sit between `<form ...>` and the hidden `<input>` of `button_to` forms (the quick-boost forms of every
message), the rest between `<form>` and a `label`/`a`. No attribute, id, aria, role, Turbo or Stimulus hook differs. Pixels and the aria tree are equal in the
same cells, and the strict live layer (which drops whitespace-only text) is equal in all 860 default cells. So it is either a class A difference the loose
rule is too strict about (whitespace next to a `display:none` hidden input does not render, though `form` is inline), or Rails' ERB whitespace to emit.
Not fixed here. Decision needed before Phase 1's gate: relax the loose rule for whitespace beside `input[type=hidden]`/`script`/`template`, or make the views
emit it. Sample: `pixels/loose-sample/`.

**Harness faults, re-run by themselves, none a pixel difference.** `auth/sign_in/error` was not captured on the first two attempts (the harness retried it twice;
the third attempt captured all cells, equal). `realtime/**` as one slice crashed twice (an unhandled "Timeout 30000ms" / "Target page ... closed" from
`NetworkLog.bodyOf` in `parity/capture/network.ts`, 1,446 s and 523 s, no report), so the 13 realtime states ran one at a time (8 cells each, 104 cells):
12 captured at once; `realtime/removed_from_room` produced no report in its single-state run (the same `network.ts` crash), and its second run needed the harness's retries (`pixels/p0-rt/removed_from_room-rerun/`: a first pass, then two re-captures) and then had all 8 cells equal on pixels and aria. Nothing in `src/` or the harness was changed for them.

**Other gates**

| Gate | Result |
|---|---|
| `CAMPFIRE_REQUIRE_SEED=1 bin/verify` | passed on the second run: 1,038 tests, 1,031 succeeded, 0 failed, 7 skipped (the 7 listed under "Starting state"). The first run failed one test, `Campfire.Kit.Tests.ParamsTests.many keys parse from json and a query string in linear time` (2.49 s against a 2 s limit) with host load average 8-11 from other projects; log in `verify-run1-timing-flake.log`. A timing test, flaky under load. |
| `bin/db-differential` | passed (exit 0): fixtures and the long scenario equal Active Record, `Campfire.Db` tests 3/3, "schema.sql, the reference's db:prepare and Campfire.Db's prepare agree", "reference on the F#-written database: rollback ok". |
| `bin/cookie-roundtrip` | 20 / 20 checks, PASSED, both directions (Rails -> ours -> Rails, ours -> Rails), cookie-less controls included. |
| `parity/bin/test-loose-dom` | 21 / 21 (from the tooling unit). |

**Speed (`bench/quick`, five workloads, c=1 and c=16, 3 reps, in 6 parts, `quick/`; `quick-compare.txt`).** Median req/s, with the stored Rails and Rust
medians of `bench/results/phase7-final`. Host: macOS load average 8-15 from other projects' builds during the whole run (`quick/driver.log` has it per
part), 4 of 8 threads pinned to the server; the harness's host probe read 1.1-1.5x its quiet spin and flagged 30 runs "noisy host" and "warm-up did not settle".
The Mac's free space was 9-16 GiB, below the 15 GiB bench floor part of the time (another project's `go test` refilled `~/Library/Caches/go-build`, cleaned
between parts whenever no go process ran); no run failed for it.

| Workload | c | dotnetway | port stored | vs port | Rust stored | vs Rust | Rails stored | vs Rails |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| room page | 16 | 30,751 | 40,422 | 0.76x | 28,623 | 1.07x | 239 | 128.5x |
| room page | 1 | 5,800 | 7,617 | 0.76x | 6,481 | 0.90x | 107 | 54.2x |
| messages page | 16 | 33,744 | 37,836 | 0.89x | 30,063 | 1.12x | 381 | 88.7x |
| messages page | 1 | 6,608 | 8,312 | 0.80x | 6,822 | 0.97x | 218 | 30.3x |
| sidebar | 16 | 31,306 | 37,555 | 0.83x | 28,238 | 1.11x | 599 | 52.3x |
| sidebar | 1 | 6,144 | 8,000 | 0.77x | 6,196 | 0.99x | 287 | 21.4x |
| search | 16 | 17,752 | 34,070 | 0.52x | 26,606 | 0.67x | 408 | 43.5x |
| search | 1 | 4,494 | 6,055 | 0.74x | 5,646 | 0.80x | 193 | 23.3x |
| post a message | 16 | 4,055 | 8,905 | 0.46x | 7,174 | 0.57x | 330 | 12.3x |
| post a message | 1 | 2,389 | 3,052 | 0.78x | 2,779 | 0.86x | 167 | 14.3x |

All five workloads at c=16 are far over the 5x-Rails floor (lowest: post a message, 12.3x). The numbers are **below** the port's stored phase7-final (0.46x-0.89x),
which is not "within noise" of that table. A **control** in the same session (`quick-control.md`, 1 rep, the port's `campfire-fsharp:app` and `campfire-dotnetway:app`
alternating, same source) read dotnetway/port at 0.77x-1.31x with no pattern (c=16: room 0.91, messages 1.03, sidebar 1.31, search 1.31, post 0.88), and the port
image itself ran at 16-21k req/s on the page routes against its stored 38-40k. So the gap to the stored table is the loaded host, not the rename or the image. The
Phase 1 speed checks must be compared with a control run of this baseline on the same day, or taken on a quiet host. Run-to-run spread in `quick/` is wide (room page
c=16: 21,741-31,424).

**KLOC (`bin/kloc`, `kloc.md`)**: 39,429 corrected (39.4 KLOC), 38,180 as cloc 2.10 prints it; per project (corrected): App 9,820, Kit 8,341, RichText 6,191,
Views 5,792, Db 3,307, Storage 1,767, Cable 1,603, RailsCompat 1,466, Assets 429, Ruby 631, Routes 82. Static data left out 4,598 (corrected). Rust 31.0, Rails 5.5.

**Not done in this unit:** the three Opus design notes (EF mapping, auth and cookies, Cable).

**Design notes** (committed after this unit): `plans/design-data-ef.md` (Phase 1), `plans/design-kit-auth.md` (Phase 2),
`plans/design-cable.md` (Phase 3). Combined KLOC projection: about 18.5 at the end (60% of Rust's 31.0).

**Verifier (2026-10-13).** The loose-DOM residue was the layer's own defect, not the port's: `loose.ts` left Rails' forgery-token
`<input>` out of the output but still counted it as an inline sibling, so the template newline after it was kept as `" "`, and it
counted `input type=hidden` as drawn. Fixed (forgery tokens and box-less elements are looked through, a run of blanks counts once;
`parity/bin/test-loose-dom` 22/22). Re-checked with `bin/pixel-parity --fast` on `default`: `search/results` and
`messages/threaded` (both in the residue) 4/4 pixel, aria and loose equal, plus the second sample recorded in the commit. The
lean-matrix loose count over all 940 cells has not been re-run (disk under the 12 GB floor during verification); the first Phase 1
gate run re-establishes it.
