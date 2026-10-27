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
| `Campfire.Assets` (+ `Assets.Build`, a build tool, not counted; both deleted in Phase 6.7, now `Web/StaticAssets.fs`, `Web/Importmap.fs` and the App's staging target) | Propshaft digests and CSS compilers, importmap, embedded asset blob, `ActionDispatch::Static` | 0.43 | | 14 | reference digests (`ReferenceTests`) | every pixel cell (CSS, JS, images), the network layer (asset paths) |
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

**Phase 5's design note, `plans/design-richtext.md` (2026-10-22), wins where it and this section disagree** (no HtmlSanitizer: Loofah's
scrubber as a walker over AngleSharp; bodies parsed as AngleSharp documents after `<body>`; three caps besides Gumbo's limits;
autolink links inside `pre` and `code` as rails_autolink does; `RubyUri` moves to the App until Phase 8; about 1.75 KLOC).

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

**Phase 4's design note, `plans/design-views.md` (2026-10-21), wins where it and this section disagree** (a small page-in-parts
type stays for the 5x floor, attributes use `Text.enc`'s encoder, the Jbuilder caches go, the differential's last answers are
frozen as test data).

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
- **The front server (Thruster's job).** **Phase 7's design note, `plans/design-front.md` (2026-10-26), wins where it and this
  bullet or the "Response caching and compression" bullet disagree** (TLS through Kestrel's handshake callback, not
  `ServerCertificateSelector`, which sends the leaf without its intermediate; OutputCache as one policy per Rails `public` route family,
  outside compression, varying by `Accept-Encoding`, no tag eviction; certificates ordered at boot by the background service; the
  HTTP logging line with `remote_addr` and `user_agent`; zstd through .NET 11's provider; units 7.1-7.6; about -2.35 KLOC). Kestrel only:
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

**Phase 6's design note, `plans/design-assets.md` (2026-10-22), wins where it and this section disagree** (the web root is a staged
copy in `src/Campfire.App/wwwroot/`, because files linked from elsewhere are served empty outside a publish; `reference/public` is
served by `MapStaticAssets` too, not `UseStaticFiles`, and keeps the framework's cache headers; `pin_all_from` is listed from the
descriptor table; the harness learns the SDK's fingerprint form first; units 6.1-6.7; about 0.15 KLOC).

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

**Phase 8's design note, `plans/design-jobs.md` (2026-10-27), wins where it and sections 2.7 and 2.8 disagree** (`Lib.Net.Http.WebPush` instead of
WebPush 1.0.13, which speaks only the `aesgcm` draft; the useragent-gem and rqrcode ports stay, MyCSharp and QRCoder failing parity; Rails' redirect loop
kept beside the `ConnectCallback` guard; retry-free resilience pipelines; AngleSharp opengraph with depth and element caps; about -1.7 KLOC).

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

See `plans/design-jobs.md` (J10, J13, J14): the harness clock is a `GetUtcNow`-only `TimeProvider` subclass, not `FakeTimeProvider` (which stops
timers); Boot becomes one `WebApplication` builder in this phase.

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

### Phase 1, unit 1.0: .NET 11 SDK move (2026-10-14, `27c98c8`) - PARTIAL (pixel run blocked on disk)

`global.json` -> `11.0.100-rc.2.26504.105` (installed beside 10.0.401 in `~/.dotnet`), `net11.0` in `Directory.Build.props` and the bench projects,
`DOTNET_VERSION` in `Dockerfile` and `Dockerfile.toolchain`, AGENTS.md toolchain line. Net11's BCL annotates `Path.GetFileName`/`GetExtension` as
non-null, so five `nonNull`/`| null` arms (Disk.fs, Fixtures.fs, Config.fs, three test files) went (FS3261/FS3262 under warnings-as-errors); no other change.

- `bin/verify` (Release, warnaserror) on net11.0: 0 warnings, 1038 tests, 0 failed.
- Image rebuilt from `27c98c8` (label checked; runtimeconfig `net11.0`, runtime 11.0.0-rc.2.26504.105); `bin/clean-docker` run. Old image kept as `campfire-dotnetway:before-net11` (digest 247e17441067, Phase 0).
- `bench/quick` (1 rep, five workloads, c=1/16; evidence under `bench/results/dotnetway-p1-0/`), before then after, same session, host probe ratio 1.05-1.10 both:
  full runs, rps c=1 / c=16: room 7711/34626 -> 7742/35116; messages 7969/35998 -> 8402/37688; post 2837/5202 -> 2894/4549 (both runs hit the 90 s warm-up cap; post c=16 is the noisiest);
  sidebar and search dipped in the after run (host load average 14 when it began; probes read 1.07-1.10 but a second Mac session was busy), so both images were
  re-measured back to back on sidebar+search: before sidebar 5966/34294, search 6073/32658; after sidebar 7924/35315, search 6391/30319. Net: no measurable change from the SDK move
  (CPU per request unchanged within noise: room 213 -> 220 us at c=1, 99.8 -> 97.1 at c=16). Cold start 361-418 ms before, 377-412 ms after; idle 27 -> 27-28 MB.
- KLOC (`bin/kloc`): 39,428 corrected (39.4 KLOC), unchanged to within 1 line.
- `bin/pixel-parity --fast --only "auth/**,rooms/**"` on net11.0 (run after clean-docker returned space to 14 GB free): PASSED in 620 s. The harness ran all seeds: default 100/100, crowd 8/8, custom_styles 6/6, first_run 8/8, restricted 2/2 screenshots, aria and loose equal (one auth/sign_in/error capture retried once). `bin/clean-parity` and `bin/clean-docker` run afterwards.

### Phase 1, unit 1.1: EF Core foundation (2026-10-14) - DONE

Packages (`Directory.Packages.props`): `Microsoft.EntityFrameworkCore.Sqlite` 10.0.13, `EFCore.NamingConventions` 10.0.1, `Microsoft.Data.Sqlite` 10.0.12 -> 10.0.13 (Storage and the old
`Database` use it too; their suites pass unchanged). New in `src/Campfire.Db`: `RailsTime.fs` (module `RailsTime` + `RailsTimeConverter`), and under `Ef/`: `Entities.fs` (15 `[<CLIMutable>]` records
and the `Involvement`/`RoomType` enums with their string mappings), `CampfireDb.fs`, `Interceptors.fs` (`SqlitePragmas`, `RailsTimestamps`, `RailsParameterGuard`), `Data.fs` (`options`,
`factory`, `tryFirst`). Nothing outside `Campfire.Db` changed except the package versions and the test project.

Deviations from the design note, both forced by coexisting with the old layer until unit 1.11:
- The new types live in namespace `Campfire.Db.Ef` (the old `User`, `Room`, `Session`, ... and the union forms of `Involvement`/`RoomType` still occupy `Campfire.Db`), and the `Ef/` files compile
  after `Models.fs` (they reuse its `Role` and `Status` enums). `RailsTime` is in `Campfire.Db` beside the old `Timestamp`. Unit 1.11 moves the `Ef` types up when the twins go.
- `ofDb` also reads a `T` separator and a trailing `Z`/` UTC` (the old `Timestamp.ParseDb` did); a bare trailing dot is rejected.

Tests (`tests/Campfire.Db.Tests/EfTests.fs`, one module so D4's process-wide counter is not shared): D1 (twice: `default` and `crowd` seeds), D2 (four), D3 (two), D4, D5, D6 = 11 tests, all pass.
- D1 reads every row of all 15 tables through EF and writes them to a fresh `Schema.prepare`d database; the two are then compared table by table, row by row, column by column with SQLite's
  `quote()` (type-preserving, equal to a `.dump` of the INSERTs; columns by name because migrated and fresh Rails databases order them differently). Identical for both seeds, including the
  timestamps' text. Finding: the seeds were loaded with foreign keys off and hold an orphan (`messages` row whose `creator_id` 933434637 is not a user; `pragma foreign_key_check` reports it),
  so the test's target connection turns `foreign_keys` off for the copy. The orphan round-trips too.
- D4: three concurrently open pooled connections each read `busy_timeout` 5000, `journal_mode` wal, `foreign_keys` 1, `synchronous` 1; `SqlitePragmas.Applied` grew by exactly 3 and did not grow
  over 20 further queries.
- D5: `DateTime` and `DateTimeOffset` holes in `FromSql`/`ExecuteSql` throw (`RailsTime.toDb` in the message); the `toDb` string and a LINQ comparison on a converted column run.

`CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1049 tests, 1042 passed, 0 failed, 7 skipped (the 7 are the same environmental skips as before: Rails-needed
fixtures, Linux-only, export-db). `bin/kloc`: 39,428 corrected (39.4 KLOC); Db 3,307 corrected. The pixel run is not applicable: no behaviour outside `Campfire.Db` changed and no app code uses the new
types yet. No `bin/db-differential` (units 1.3-1.9 write rows through it). Differences recorded: none.

### Phase 1, unit 1.2: Write path and seam (2026-10-14) - DONE

New in `src/Campfire.Db`: `WriteGate.fs` (namespace `Campfire.Db`; it compiles ahead of `Database.fs` because the old writer takes the gate too, so it is not in `Ef/Writes.fs` as the note has it) and
`Ef/Writes.fs` (`ClockTimeProvider`, `Env`, `Tx`, `Writes.run`, `Reads.run`); `Data.addServices` is in `Ef/Data.fs`. In `Campfire.Kit`: `Ctx.Http` (the Adapter passes the request's `HttpContext`; the
three Kit tests that build a `Ctx` pass a `DefaultHttpContext`) and `Front.serveWithLines` gains `configureServices` (`serveWith`/`serve` pass `ignore`). In `Campfire.App`: `AppState` gains
`DbFactory`, `Gate` (= the old `Database.Gate`) and `Env` beside `Db`; `c.Db` (scoped `CampfireDb` from `RequestServices`); `Booted.ConfigureServices`, which `Boot.serve` passes to the front host and
`Support.start` to the test host (`Support.startPipelineWith`; `startPipeline` is `startPipelineWith ignore`).

Decisions the note left open:
- **BEGIN IMMEDIATE.** EF's `BeginTransaction` (and Microsoft.Data.Sqlite's `Serializable` through it) sends a deferred `BEGIN`, which W1 caught: a second connection's `BEGIN IMMEDIATE` succeeded inside the body.
  `Writes.run` opens the connection, starts `SqliteConnection.BeginTransaction(Serializable, deferred = false)` itself and hands it to EF with `UseTransaction`. W1 now sees SQLITE_BUSY (code 5) from the other connection before any
  statement of the body has run.
- **The migration rule is inside the old `Database`.** `Database.Write` takes the `WriteGate` (async) and `WriteBlocking` takes it blocking, around the writer thread's job, so every existing `app.Db.Write` call site is covered (20) without
  touching them; the gate belongs to the `Database` (`db.Gate`) and `AppState.Gate` is that same object, so there is exactly one writer at a time throughout the phase. The old Rust-shaped `DbRun.runWrite` is unchanged.
- `Env.RichText` is still the port's `RichText` interface (over `Conn`); unit 1.5a moves it to `CampfireDb`. `Env`/`Tx` are in `Campfire.Db.Ef` beside the old twins (as in 1.1).
- A failing after-commit hook that is not a `DbException`/`SqliteException` comes back as `Error (Other exn)` (the write has committed); a hook queued from inside a hook runs at once and a failure there is logged only.
- W5's "`DisconnectUser` reaches `Revocation` without tripping C16's guard" is not testable here: Cable's guard arrives with the Cable phase. The test checks what it rests on: `InWrite` is false in the sink call for a `DisconnectUser`
  emitted after commit.

Tests: `tests/Campfire.Db.Tests/WritesTests.fs` W1-W7 (8 tests: W3 has two), all pass; they use `action_text_rich_texts` rows (no foreign keys, unique on type/id/name, which W3 uses for `isRecordNotUnique`). W6 runs 64
concurrent `Writes.run` writes on 16 tasks at once (4 rounds each): all 64 rows present, no error, a counter never above 1 inside the body. W7 interleaves 20 old `Database.Write` and 20 `Writes.run` writes: 40 rows, no error. App
test `a request resolves the scoped CampfireDb and the TimeProvider from its RequestServices` (in `AppTests.fs`) boots the seed, resolves `CampfireDb` (same instance twice in one request), `TimeProvider` (`ClockTimeProvider`) and the gate
(same object as `AppState.Gate` and `Db.Gate`) from `RequestServices`, and compares the user count with the old layer's.

Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1058 tests, 1051 passed, 0 failed, 7 skipped (the same environmental skips). The App suite is unchanged apart from the one added test.
`bin/pixel-parity --fast --only "rooms/**,interactions/**"` (image rebuilt from the tree): PASSED in 781 s, default 153/153, crowd 8/8, custom_styles 4/4, first_run 0/0, restricted 2/2 screenshots, aria and loose equal
(`bench/results/dotnetway-p1-2/pixel.log`). `bin/db-differential` and the bench are not for this unit. `bin/kloc`: 39,859 corrected (39.9 KLOC; 1.1 closed at 39,428); Db 3,714. Differences recorded: none (Class A: no visible change).
Host disk: 11.6 GiB free before the image build, 5.8 GiB after it (colima's data disk grew; `bin/clean-docker` reclaimed 0 B: the 13 images are all tagged, 10.9 GB of them reclaimable but not this unit's to remove).


### Phase 1, unit 1.3: Users, sessions, accounts (2026-10-19) - DONE

`User`, `Session` and `Account` are now the EF entities and the only records of those tables (`src/Campfire.Db/Identity.fs`, namespace `Campfire.Db`, with `Role`, `Status`, `PasswordDigest`, `NewUser`, `UserChanges`; the
three records left `Ef/Entities.fs`; the `Ef/` files now compile before `Models.fs`). Their modules (`User` in `Models.fs`, `Session.fs`, `Account.fs`, `FirstRun.fs`) take `CampfireDb` for reads and `Ef.Tx` for writes. Nullable columns are
`string | null` and times are UTC `DateTime`s in the records; the presenters convert at the edge (`Option.ofObj`, `DateTimeOffset`). New in `Ef/Data.fs`: `found` (RecordNotFound), `insert`, `update` (attach, mark only the
properties that differ from the old record, `SaveChanges`, detach: nothing at all when nothing changed, `RailsTimestamps` stamps `updated_at`), `remove`. In `Campfire.App`: `c.Read`, `c.Write` and `c.TryWrite` (`Writes.run` over the request's
context; `TryWrite` keeps the `DbError` for the `RecordNotUnique` rescue); `Concerns` (sign-in, session resume and start, bot key), the sessions, accounts, users, first-run, bots, bans, avatars, logos, join codes, custom styles, transfers and PWA
controllers, `Presenters/Accounts` (`helpContact`, `noUsers`, `accountUsers`) and the cable connection's session lookup (`SessionAuthenticator` takes the `IDbContextFactory`; `Deps.DbFactory`).

**Spike L1 (the 20 minutes the revision gave this unit): `EF.Parameter(x)` around a captured value keeps EF's query cache hitting.** `LinqTests.fs` counts `CoreEventId.QueryCompilationStarting` over 39 calls with a new value each time after a
warm call: a captured local compiled 39 queries (F# turns it into a constant in the tree), `EF.Parameter id` 0, a record field 0. `EF.Parameter` replaces the record-field idiom of E12 rule (a); every lookup in this unit uses it
(the sign-in lookups included: `Session.findByToken`, `User.findActiveByEmailAddress`, `User.findById`). `EF.Parameter` also parameterises a collection (`Enumerable.Contains(EF.Parameter ids, u.Id)` in `whereIds`) and a `LIKE` pattern.

**LINQ first, and what is not LINQ.** Everything else in the unit is LINQ in method syntax: `Where`, `OrderBy(fun u -> u.Name.ToLower())` (`ORDER BY LOWER(name)`), `EF.Functions.Like`, `Select(fun s -> s.IpAddress)` (one property),
`ExecuteDelete()`, `Find`, `LongCount`. Raw SQL, each because LINQ cannot say it: (1) `grantMembershipToOpenRooms` and `FirstRun`'s grant, an `INSERT ... SELECT` stamped with SQLite's own clock (what Rails' `insert_all` does; the
old code did the same per batch of 1,000 rooms or users, one statement now); (2) `deactivate`'s memberships `DELETE ... WHERE id IN (SELECT ... INNER JOIN rooms ...)`: a subquery over a second table is a lambda inside a lambda, which
fails at run time from F# (E12 rule b). No `*Raw` call, no F# `query { }`.

Decisions and deviations:
- **Old and new coexist until 1.11, so the port's Conn readers stay under new names**: `User.findLegacy`, `findByIdLegacy`, `whereIdsLegacy`, `activeOrderedLegacy`, `fromRow`, `findBySql`, `Select`, and `Account.firstLegacy` (for Room, Membership, Message, the
  sidebar, the presenters and the rooms and messages controllers, which move in 1.4-1.6; same names would have made F# resolve to the wrong module). The new and the old function of one name cannot share a module.
- **Avatar and logo attachments are a second write.** `AttachmentWrites.assign` needs the old `Tx` (unit 1.7 moves the storage rows), and one SQLite transaction cannot span the EF connection and the old writer's. The user, bot, account or first-run
  administrator is written and committed by `Writes.run`, then the attachment by the port's writer. Rails does both in one transaction; the difference is a user saved without its avatar when the second write fails, which a constraint or disk error
  could cause and nothing else. It goes away in 1.7. (Class B of the known-differences policy: no visible difference on the happy path; recorded here, not yet in README.)
- `FirstRun` inserts the first room as an EF row (an open room, creator the administrator) and grants it to every active user after commit, rather than calling `Room.create` and `Room.grantTo` (the old `Tx`): Rails' explicit `grant_to` after the
  room's own `grant_access_to_all_users` was a no-op and is not repeated. Unit 1.4 replaces it with `Room.create`.
- `User.removeBannedContent` and `deliverWebhookLater` stay on the old `Tx` (they call `Message.destroy`, `Webhook.findByUser`); `Membership.destroy` emits `DisconnectUser` itself instead of calling `User.resetRemoteConnections`.
- `User.create`/`createBot` return the saved entity (id and `RailsTimestamps`' times filled in); `Webhook` rows for bots and `Ban` rows for `ban` are EF inserts of the entities (the models themselves move in 1.8).
- `Layout.load` is now one EF read (`Account.first`) and one old read (the logo's attachment, the last room); unit 1.4/1.7 merge them back. It is a per-page cost not measured here.
- The Db test project runs without test parallelism (`CollectionBehavior(DisableTestParallelization = true)` in `Support.fs`): D4 counts physical connections through a process-wide counter and a second module opening connections made it
  flaky (3 expected, 4 seen). The suite is 12 s.

Tests: `UserTests`, `AccountTests`, `FirstRunTests` ported to EF (`t.WriteEf`/`t.ReadEf` in `Support.TestDb`, sharing the old `Database`'s gate, sink and clock); `LinqTests` L1 (three tests). `CallbacksTests`' session tests, `ColumnsTests` (user and account
column order through the legacy readers), `FixturesTests`, `SeedTests`, `DifferentialTests` (the scenario's user, session, ban and account steps go through EF), `RoomTests` and the App tests (`ChannelsSupport.TestApp.WriteEf`, revocation, golden channels,
`AppTests` pipelines register `Booted.ConfigureServices`) adapted; no test was deleted and no assertion weakened.

Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,061 tests, 1,054 passed, 0 failed, 7 skipped (the same environmental skips); Db suite 200 passed + 4 skipped, three runs in a row. Image rebuilt from `f684d38`
(label checked). `bin/cookie-roundtrip`: 20/20, PASSED (Rails -> ours -> Rails both ways). `bin/db-differential`: green (fixtures, scenario, schema identity, Rails boots on the F#-written file; `bench/results/dotnetway-p1-3/db-differential.log`).
`bin/pixel-parity --fast --only "auth/**,account/**,users/**"`: PASSED in 334 s, default 72/72, crowd 4/4, custom_styles 10/10, first_run 8/8, restricted 2/2 screenshots, aria and loose equal (`bench/results/dotnetway-p1-3/pixel.log`).
No bench (not this unit's gate). `bin/kloc`: 39,973 corrected (40.0 KLOC; 1.2 closed at 39,859); Db 3,805 (old and new layers side by side). Differences recorded: the second write for avatars and logos above (until 1.7).
Host disk: 41 GB free before, 39 GB after the image build and the pixel run; `bin/clean-docker` and `bin/clean-parity` run.

### Phase 1, unit 1.4: Rooms, memberships (2026-10-19) - DONE

`Room` and `Membership` are the EF entities and the only records of those tables (`src/Campfire.Db/Rooms.fs`, namespace `Campfire.Db`, with the `Involvement` and `RoomType` enums, now the enum forms the port's unions had as twins; `Ef.Names` keeps the string
mappings). Nullable columns are `string | null` and `Nullable<_>`, times UTC `DateTime`s. Their modules (`Models.fs`) run on `CampfireDb` for reads and `Ef.Tx` for writes: `Room.find/findById/forUser/findForUser/forUserOfType/original/...` are LINQ with `EF.Parameter`
(a user's rooms are `db.Rooms.Join(memberships of the user, ..., fun r _ -> r)`: a join whose result selector is the entity translates, a tuple would not need to), `create/update` are `Data.insert/update` (type change to open queues the grant after commit),
`destroy` is two `ExecuteDelete`s, `revokeFrom` removes each membership through `Membership.destroy` (`DisconnectUser` after commit). The Connectable counters: `Data.update` where `updated_at` is stamped (involvement, read, connections), `ExecuteUpdate`
where it must not be (`connect`: no `updated_at`; `touchConnectedAt`, `disconnected`, `disconnectAll`: explicit). `FirstRun` now calls `Room.create`.

Raw SQL, each because LINQ cannot say it: (1) the `insert_all` grants (`Room.grantTo`: one `INSERT ... VALUES` per batch of 1,000 users, built as a `FormattableString` so every value is a parameter, stamped with SQLite's own clock as `insert_all` is; `grantToActiveUsers`: one
`INSERT ... SELECT` for the new open room); (2) the sidebar: `Membership.visibleWithOrderedRoom`/`withOrderedRoom` are one `SqlQuery<MembershipRoomRow>` (`Rooms.fs`; the times stay text in the row and `RailsTime.ofDb` reads them, so no converter question arises for a keyless
type). `SqlQuery` mapped the snake-case aliases to the record's properties without help (the naming convention reaches keyless types).

Callers on EF now: the rooms controllers (`Rooms`, `Opens`, `Closeds`, `Directs`, `Involvements`, `Refreshes` for the room), `Concerns.setRoom` and `lastRoomVisited`, `Current.lastRoomVisitedIn`, `Layout.load` (the last room joins `Account.first` in one EF read, the logo's
attachment stays on the port's reader until 1.7), the sidebars and profiles controllers with `Presenters/Accounts` (`sidebar`, `profileMemberships`, `bot`, `directPlaceholderUsers` as LINQ), bots index, welcome, searches' return-to-room, autocompletable, `Messages/ByBots`,
`BoostsByBots`, `Boosts`, the test-notification badge, `Membership.disconnectAll` at boot (it moved ahead of `AppState`'s construction, on a leased context), and the channels `Presence`, `RoomChannel` (and so `TypingNotifications`) and `RoomMessages`, which take
`RoomChannel.Access` (the pooled factory, `Db.Gate`, the EF environment; `Deps.Env` is new) instead of the old `Database`. The broadcasts `ClosedRoomCreate/Update` and `DirectRoomCreate` take a `CampfireDb`.

**What stays on the port's `Conn` until the message units** (units 1.5-1.8 delete them; named `*Legacy` as in 1.3): `Room.findByIdLegacy/findLegacy`, `usersLegacy`, `activeBotsLegacy`, `touchLegacy` and `receiveLegacy` (what `Message.create`/`updateBody`/`destroy` call on the old `Tx`), `destroyMessagesLegacy`,
`Membership.forRoomLegacy`, `unreadCountLegacy`, `connectionCutoffLegacy`, with `Room.fromRow` and `Membership.fromRow` over `Columns`. The `Presenter` (messages, rich text), `Broadcasts.UnreadRoom`, `PushSubscription`, `CoreJobs` and `IntegrationJobs` use them.

Decisions and deviations:
- **Room destroy is two writes** (class B of the policy, until 1.5a moves `Message`): the room's messages on the old writer (`Room.destroyMessagesLegacy`), then the memberships and the room on EF's. Rails does both in one transaction; the difference is a room whose messages are gone but
  which still exists if the second write fails, which nothing but a disk error could cause. Recorded here, not yet in README.
- `Room.findDirectFor` looks at the first user's direct rooms and compares each one's member set (the port's walked every direct room joined to its users, repeats included); the result is the same room, as the member sets of direct rooms are unique.
- Counters (`increment!`/`decrement!(touch: true)`) go through `Data.update` (read value plus or minus one, inside `BEGIN IMMEDIATE`) rather than `connections = connections + 1` in SQL: the same row, with the gate making the two equivalent.
- The Presence and room-channel reads lease a context per call (`Access.read`), as the cable connection's session lookup did in 1.3; the test apps' EF options now use the test clock's `TimeProvider` (they used the system's, so `updated_at` of EF writes was real time in channel tests).
- `ColumnsTests` still checks the positional readers for rooms and memberships (`Room.fromRow`, `Membership.fromRowAt`, both column layouts) through the SQL the old module ran, written out in the test; `Room.all`, `forUser` and `withOrderedRoom` no longer exist as `Conn` readers, their EF forms are in `RoomTests`
  (`the sidebar's memberships and rooms are the entities LINQ reads`).

Tests: `RoomTests` and `MembershipTests` ported to EF; new in `RoomTests`: the sidebar statement against LINQ's rows, direct-room lookup in any member order, a grant larger than one batch (1,100 users, then again to see `ON CONFLICT` skip them). `CallbacksTests`, `FirstRunTests`, `UserTests`,
`FixturesTests`, `SeedTests`, `DifferentialTests`, `PushTests` and the App tests (`RoomsTests`, `BroadcastsTests`, `ChannelsTests`, `ChannelsGoldenTests`, `RevocationTests`, `MessagesTests`, `SearchesTests`, `WebPushTests`, `ChannelsSupport`) adapted; no test deleted, no assertion weakened.

Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,064 tests, 1,057 passed, 0 failed, 7 skipped (the same environmental skips); Db suite 203 passed + 4 skipped. Image rebuilt from `bd3ea1b` (label checked). `bin/db-differential`: green (fixtures, scenario, schema identity, Rails boots
on the F#-written file; `bench/results/dotnetway-p1-4/db-differential.log`). `bin/pixel-parity --fast --only "rooms/**"`: PASSED in 312 s, default 74/74, crowd 8/8, custom_styles 4/4, first_run 0/0, restricted 2/2 screenshots, aria and loose equal (`bench/results/dotnetway-p1-4/pixel.log`).
No bench (not this unit's gate). `bin/kloc`: 39,965 corrected (40.0 KLOC; 1.3 closed at 39,973); Campfire.Db 3,768 (old and new layers side by side). Differences recorded: the two-write room destroy above (until 1.5a).
Host disk: 39 GB free before, 36 GB after the image build and the pixel run; `bin/clean-docker` and `bin/clean-parity` run.

### Phase 1, unit 1.5a: Messages, rich text, boosts: model (2026-10-19) - DONE, with the 1.5b call sites it forces

`Message`, `Boost` and `RichTextRecord` are the EF entities and the only records of those tables (`src/Campfire.Db/Messages.fs`, namespace `Campfire.Db`; times UTC `DateTime`, `Body: string | null`). Their modules run on `CampfireDb` for reads and `Ef.Tx` for writes. **LINQ, every
query**: `Message.find/findById/last/count/byCreator/forRoom/findInRoom/countInRoom`, the pages (`lastPage`, `firstPage`, `pageBefore/After/Around`, `pageCreatedSince`, `pageUpdatedSince` with an optional `NOT IN` that is a separate query shape so the cache keeps hitting), `existsBefore/After`,
`paged` (`OrderBy(Id).Skip(40).Any()`), `findReachable` (`db.Messages.Join(db.Memberships.Where(user), ..., fun m _ -> m)`, a result selector that returns the entity), `mentioneesInRoom`, `attachment`, `Boost.*`, `RichTextRecord.findFor`; writes through `Data.insert/update/remove`, `ExecuteUpdate` (the message and room
touches, the unread memberships of `Room.receive`) and `ExecuteDelete` (boosts, body, in `Message.destroy`). Every captured value is `EF.Parameter` (reads) or a plain value in an `ExecuteUpdate` setter (see L2 below). **Raw SQL, each because LINQ cannot say it**: (1) the FTS5 `MATCH` reads
(`searchInRoom`, `searchReachable`: `FromSql` of `SELECT messages.* ... JOIN message_search_index idx ... WHERE ... idx.body MATCH {terms}`, with the order and the limit composed in LINQ on top, so no reliance on a subquery's order); (2) the search-index writes (`insert into/update/delete from message_search_index`, `ExecuteSql` in
the after-commit hooks, E13). No `*Raw` call, no `query { }`. The FTS writes belong to 1.6 by the plan's table but live in `Message.create/touch/destroy`, so they are here; 1.6 keeps `Search.record`/trim and the searches controller.

`Room` gains the EF `touch`, `receive` (`ExecuteUpdate` of the unread memberships: `involvement` not invisible with a `HasValue` guard, because SQL's `!=` leaves a NULL involvement out and EF's C# null semantics would not; not connected or connected before the cutoff; not the creator) and a `destroy` that destroys its
messages (each with its callbacks) in the same transaction. **The room destroy is one write again**: unit 1.4's two-write difference is gone. `User.removeBannedContent`, `deliverWebhookLater` and `Webhook.payload` run on EF (`payload` takes the `Ef.Webhook` of `User.webhook`); `PushSubscription.pushesFor` takes the
context beside the old `Conn` (the subscriptions stay on `Conn` until 1.8).

**The `RichText` interface takes the `CampfireDb`** (`ToPlainText(db, html)`, `MentionedUserIds(db, html)`; the file moved after `Ef/CampfireDb.fs`). `RichText` left the old `Env` and `Tx` (the old `Env` is `{ Clock; Sink; BcryptCost }`) and lives on `Ef.Env` only. `AppRichText`, `DbResolver` (`User.findById db`) and `Testing.BasicRichText` follow.

Call sites (the plan's 1.5b, done here because the record changed under them): `Presenter` and `DbResolver` on a `CampfireDb` (`Presenter(db, app, host)`; its attachment lookup is `Attachments.attachedBlobOn db`, which opens the context's own connection for the storage layer's raw read, until 1.7); the messages,
boosts, by-bots, boosts-by-bots, rooms, directs, refreshes and searches controllers on `c.Read`/`c.Write`; `Broadcasts.MessageCreate/UnreadRoom` take the context; `CoreJobs.removeBannedContent` and `IntegrationJobs` (push, webhook delivery and reply, the broadcast) on a leased context (`AppState.ReadEf/WriteEf` and local `readEf/writeEf`).
`ReadOffloaded` is gone from the messages count in the by-bots index, the refresh and the searches: EF's synchronous read runs on the request's thread (E6); the effect on a search over a large database is for 1.10's bench to show.

Finding **L2** (`LinqTests`): EF 10's `ExecuteUpdate(Action<UpdateSettersBuilder<T>>)` turns the value a setter is given into a parameter: a plain captured value in `s.SetProperty((fun m -> m.UpdatedAt), now)` compiled 0 queries over 39 calls with a new value each time. `EF.Parameter` there throws (it is only for query expressions), and the
expression-lambda overload is not needed. The design's rule "setters use the expression overload over a record" is unnecessary for `ExecuteUpdate`; the rule for `Where` stays.

Differences, none visible in a page: (1) **a post with an attachment is two writes** (class B, until 1.7): the staged blob's row on the port's writer (it uses the raw connection), then the message, its body and its attachment row on EF's; Rails does one transaction, so a failed second write leaves an unattached blob row (the blob purge job
does not know it; nothing but a disk error causes it). The same split: `analyzeAttachment` (blob metadata on the old writer, then the touches of the attached messages on EF's) and `updateMessage` with a new attachment. (2) The room destroy difference of 1.4 is closed.

Tests: `MessageTests` (15; new: P1, reachable messages, destroy) and `CallbacksTests` ported to `WriteEf`/`ReadEf`; P1 checks every timestamp against a frozen clock with microseconds, the rich text, the room's `updated_at`, the FTS row, the unread memberships, the `PushMessage` event, then a boost's touches. `LinqTests` L2.
`UserTests`, `RoomTests`, `PushTests`, `UnitTests`, `FixturesTests`, `SeedTests`, `DifferentialTests` and the App tests (`MessagesTests`, `RoomsTests`, `BroadcastsTests`, `ChannelsTests`, `ChannelsGoldenTests`, `ChannelsSupport`, `IntegrationsSupport`, `WebPushTests`, `MiscTests`) adapted. Retired with the readers they covered: the
`Message`/`Boost`/`RichTextRecord` cases and the row-reading timing of `ColumnsTests` (the entities read by name; D1 and `SeedTests` cover them). Dead `*Legacy` readers removed (`whereIds`, `activeOrdered`, `forRoom`, `users`, `activeBots`).

Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,065 tests, 1,059 passed, 0 failed, 6 skipped (the environmental skips; Db suite 205 passed + 3 skipped, App 200 + 1). Three runs were clean; two others each had one Kit or Cable socket-timing test fail under load while another job ran
(`FrontTests.closes idle and slow connections`; a Cable test), both green on re-run and neither project depends on Db. Image rebuilt from `ba102c0` (label checked). `bin/db-differential`: green (fixtures, scenario, schema identity, Rails boots on the F#-written file; `bench/results/dotnetway-p1-5a/db-differential.log`).
`bin/pixel-parity --fast --only "messages/**,rooms/**,interactions/**,search/**,bot_api/**"`: PASSED in 878 s, default 225/225, crowd 8/8, custom_styles 6/6 (two re-captures of the pixel-flake kind, then equal), first_run 0/0, restricted 2/2 screenshots, aria and loose equal (`bench/results/dotnetway-p1-5a/pixel.log`).
The 13 `realtime/**` states one at a time, default seed: 13/13 equal (`pixel-realtime.log`). No bench (not this unit's gate). `bin/kloc`: 39,927 corrected (39.9 KLOC; 1.4 closed at 39,965); Campfire.Db 3,691, App 9,931.
Host disk: 36 GB free before, 34 GB after; `bin/clean-docker` and `bin/clean-parity` run.

### Phase 1, unit 1.5b: Messages: call sites (2026-10-19) - DONE (delivered inside 1.5a)

Nothing was left to build: unit 1.5a moved every call site of 1.5b's row because the record changed under them (`Controllers/Messages*`, `Boosts`, `ByBots`, `BoostsByBots`, `Presenter`, `DbResolver`, `Broadcasts`, `CoreJobs`; see 1.5a). Audit at `f0fab8d`: none of those files uses `ReadOffloaded`, `app.Db` or `Legacy`; the only `Conn`/`tx.Conn.Raw` left in them is the storage layer's raw blob access in `Controllers/Messages.fs` (`saveStaged`, `touchAttachmentRecords`), which is unit 1.7's. LINQ: all queries EF LINQ, no `*Raw`, no `query { }`.
Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror) re-run on the unchanged tree: 1,065 tests, 1,059 passed, 0 failed, 6 skipped (environmental). The unit's pixel gate (`messages/**`, `rooms/**`, `interactions/**`) was run in 1.5a (default 225/225, realtime 13/13); not repeated, the tree is unchanged. No new differences.

### Phase 1, checkpoint after 1.5b: Opus review (2026-10-19) - GO, with notes for 1.6-1.11

Adversarial review of `6785e51..8206b0d` against `plans/design-data-ef.md` and D1-D9. **Write path**: one writer holds (the old `Database.Write`/`WriteBlocking` and `Writes.run` share `WriteGate`, held through the after-commit queue; `InWrite` is true only from `BEGIN` to `COMMIT`); `ChangeTracker.Clear` and the gate exit are in `finally`; `DisconnectUser` is only ever `EmitAfterCommit` (no `EmitNow` caller left); `Room.receive`, `Message.create/touch/updateBody/destroy`, `Boost.create/destroy` match `reference/app/models` and the old port statement for statement. **SQL injection**: every `FromSql`/`SqlQuery`/`ExecuteSql` is an interpolated `FormattableString`; the one `FormattableStringFactory.Create` (`Room.insertMemberships`) builds its format from constant text and numbered holes only; no `*Raw` call, no `query { }`. **E12(a)**: no LINQ lambda compares against a captured local without `EF.Parameter` (the rest compare against enum constants). **Entities**: no `option` (the `option`s in `Identity.fs` are the `NewUser`/`UserChanges` inputs). **Evidence**: the units' logs (`bench/results/dotnetway-p1-{3,4,5a}`) show what the Phase log claims.

Fixed (small): (1) **`bin/verify` lacked E12's guard**: it now fails on a `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw` outside `Schema.fs`, a `FormattableStringFactory` outside `Models.fs`, or an F# `query { }` anywhere in `src` (tested against planted lines). (2) **`Writes.transact` used `connection.BeginTransaction(Serializable, false)` plus `UseTransaction`**, on the belief that EF's `BeginTransaction` sends a deferred `BEGIN`. A probe against the repo's Microsoft.Data.Sqlite 10.0.13 showed `BeginTransaction()` and `DbConnection.BeginTransaction(Unspecified)` both take the RESERVED lock (a second connection's `BEGIN IMMEDIATE` gets `SQLITE_BUSY`), as E11 says, so it is now `use transaction = db.Database.BeginTransaction()` (W1 guards it). (3) `User.deactivate`'s comment said the disconnect is mid-transaction; it is after commit (design 3.6).

Gates, run here: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `8206b0d` and again with the fixes: both 0 warnings, 1,065 tests, 1,059 passed, 0 failed, 6 skipped (environmental); the Db suite twice more after the fixes (205 passed + 3 skipped each). `bin/pixel-parity --fast --only "messages/**,interactions/**"` on the `ba102c0` image (the reviewed code; `src` unchanged to `8206b0d`): PASSED in 550 s, default 139/139 screenshots, aria and loose equal, the other seeds have no states in those globs (`bench/results/dotnetway-p1-ck1/pixel.log`); `bin/clean-parity` run. Not run: the image was not rebuilt for the three fixes (behaviour-equivalent; the next unit's rebuild covers them), and there is no db-differential or bench at this checkpoint. `bin/kloc`: 39,927 corrected (39.9 KLOC) before the fixes, the same as 1.5a. Disk: 34 GB free before, 32 GB after.

Notes for the remaining builders: see the orchestrator's checkpoint result. The ones that bind: H1 (1.10) is the only guard against a captured local, so run `LinqTests`-style counting on any new hot-path query before committing; `Autocompletable` still reads `users` through the old `Conn` (`autocompletableUsers`) and `Searches` through `ReadOffloaded` (1.6 moves both); the old writer's checkpointer can run `RestartWal` after its gate is released, which can make an EF writer wait on `busy_timeout` once the WAL passes the limit (gone in 1.11); `Writes.run` returns `Error` for a failed after-commit hook although the write committed, so a caller must not retry the body on that error.

### Phase 1, unit 1.6: Search (2026-10-19) - DONE

The FTS5 reads (`Message.searchInRoom`, `searchReachable`) and index writes were already `FromSql`/`ExecuteSql` from 1.5a (E13), so this unit moved what was left of the old connection. `Search` is now an EF entity (the record moved to `Messages.fs`, the module in `Search.fs` runs on `CampfireDb` and `Ef.Tx`): `orderedForUser`, `count`, `countForUser`, `record` (find or insert, trim to the ten most recent with one `ExecuteDelete` over `Contains(EF.Parameter keep)`, then touch with `ExecuteUpdate`) and `destroyAllForUser` are all LINQ, every captured value in `EF.Parameter`, no `*Raw`, no `query { }`. `Controllers/Searches.fs` uses `c.Read`/`c.Write` (no `ReadOffloaded`, no `c.App.Write`). `Controllers/Autocompletable.fs` (the checkpoint's note 4) reads users through `User.autocompletable`, one LINQ query (`Join` with a result selector for the room's users, `EF.Functions.Like`, `OrderBy ToLower`), and its hand-built SQL is gone.
Tests: P2 (`MessageTests`: `searchReachable` equals the port's hand-written FTS5 query for a word, a quoted phrase, a NUL-separated query and `""`, never vacuous); `CallbacksTests`, `UserTests`, `SeedTests`, `DifferentialTests`, `FixturesTests` and `SearchesTests` moved to `WriteEf`/`ReadEf`. No new differences.
Gates: `CAMPFIRE_REQUIRE_SEED=1`-style `bin/verify` (Release, warnaserror): 0 warnings, all suites passed (1,066 tests with P2, 0 failed, 6 environmental skips). Image rebuilt (`bench/results/dotnetway-p1-6/build.log`), `bin/clean-docker` run. `bin/pixel-parity --fast --only "search/**"`: PASSED in 93 s, default 12/12, custom_styles 2/2, screenshots, aria and loose equal (`pixel.log`). `bin/db-differential`: green (rollback ok). `bin/clean-parity` run. `bin/kloc`: 39,886 corrected (39.9 KLOC; 1.5a closed at 39,927). Disk 32 GB free throughout.

### Phase 1, unit 1.7: Storage rows (2026-10-19) - DONE

Code in `02f6db2`: blobs, attachments and variant records are EF entities (LINQ); `Campfire.Db/ActiveStorage.fs` and `Campfire.Storage/Sql.fs` are gone. The class-B two-write splits are removed: blob, attachment and message are one `Writes.run` again (`Controllers/Messages.fs` saveStaged, touchAttachmentRecords, analyzeAttachment; `Presenters/AttachmentWrites.fs` for avatars and logos; the `IntegrationJobs` bot attachment write). The avatar/logo second-write difference recorded under unit 1.4 is closed.
Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,066 tests, 1,060 passed, 0 failed, 6 environmental skips. `bin/storage-media-vectors`: 42 passed, mismatches=0 (the script now builds, then tests with `--no-build`, because the combined `dotnet test` left the container hanging). `bin/db-differential`: ok. `bin/pixel-parity --fast` (default 125/125, restricted 2/2, first_run 0/0, all layers equal): PASSED in 506 s. Evidence in `bench/results/dotnetway-p1-7/`.
`bin/kloc`: 39,661 (39.7 KLOC). Differences recorded: none new.

### Phase 1, unit 1.8: Push, webhooks, bans (2026-10-19) - DONE

Code in `bd6bb98`: `Ban` and `PushSubscription` are the EF entities (`Ef.Ban`, `Ef.PushSubscription`; the port's records and `Row` readers are gone), their modules run on `CampfireDb` and `Ef.Tx`; the legacy `Webhook` record and its `Conn` functions are deleted (the entity and `Webhook.payload` were already EF from 1.5a; `User.webhook` is the lookup); `Membership.unreadCountLegacy`/`connectionCutoffLegacy` are deleted with their last caller. `Ban.banned` is one LINQ `Any` (the unit row said `FromSql`; the 2026-10-19 LINQ-first revision supersedes it: L3 shows 0 compilations with a new address each call, so the cache hits as it would for `FromSql`). The subscriber lookups (`forUsersInvolvedInEverything`, `forMentionedUsers`) are one LINQ join each over `memberships` (the `users` join Rails writes only requires the user to exist, which the foreign key says), `EF.Parameter` on the cutoff, ids and the mentionee array. `IntegrationJobs` has no `app.Db`, `ReadOffloaded`, `Write` or `WriteBlocking` left: the push job reads on a pooled context inside `Task.Run` (the badge counts and notification building are off the job's thread, as `ReadOffloaded` had it); `webPushPool` takes the context factory, the gate and the env, and its invalid-subscription handler runs `Writes.run` on the pool's own invalidation thread and waits there (a dedicated thread, as `WriteBlocking` was). `WebPushPool.Queue`/`pushMessage` and `WebPush.Notification.build` take a `CampfireDb`, no `Conn`. Controllers (`PushSubscriptions`, `TestNotifications`, `Sessions`' unsubscribe, `Concerns.rejectBannedIp`) use `c.Read`/`c.Write`/`c.TryWrite`; the existing-subscription touch is one `ExecuteUpdate`. `PushSubscriptions.findBy` builds its conditions per permitted key (nil is `IS NULL`). Note 7 (`Writes.run` can return Error after commit): nothing here retries a body. Note 9: `PushSubscription.create` returns the same instance `Data.insert` filled.

Tests: `PushTests`, `UserTests`, `SeedTests`, `WebPushTests` and `MessagesTests` ported to the EF readers and the `Ef.PushSubscription` record (null instead of `option`); new L3 (`LinqTests`): `Ban.banned`/`forUser`, `PushSubscription.find`/`forUser`/`forUsersInvolvedInEverything`/`forMentionedUsers` compile 0 queries over 39 calls with new values (and a growing mentionee array). Differences: none new.

Gates: `bin/verify` (Release, warnaserror): 0 warnings, 1,067 tests, 1,061 passed, 0 failed, 6 environmental skips. Image rebuilt from `bd6bb98` (`bench/results/dotnetway-p1-8/build.log`), `bin/clean-docker` run. `bin/pixel-parity --only "users/**,bot_api/**"` (the default lean matrix): PASSED in 243 s, default 72/72, custom_styles 4/4, screenshots, aria and loose equal; `bin/clean-parity` run. `bin/db-differential`: ok (rollback ok). Evidence in `bench/results/dotnetway-p1-8/`.
`bin/kloc`: 39,404 by upstream's rules (39.4 KLOC; 39,578 corrected).

### Phase 1, unit 1.9: Boot, schema, fixtures (2026-10-19) - DONE

Code in `ada23e0`: `Schema.prepare` runs on `CampfireDb` (E15): `ExecuteSqlRaw` only for the constant `schema.sql` (and the constant paging-index addition) inside `BeginTransaction`, the migration versions and `ar_internal_metadata` through `SqlQuery`/`ExecuteSql` holes with Rails' text for the clock; `Schema.prepareFile` is the same on a context of its own (tests). The old `Database` no longer prepares (`Config.Prepare` and `Config.Environment` are gone), so `Boot.boot` builds the factory first and prepares through EF before the port's writer and readers open the file; `configureConnection` and `BusyTimeoutMs` moved to `Sql.fs` (`ConnectionSettings`) for the port's own connections until 1.11. Boot's `disconnectAll` was already EF (1.4). `Fixtures.load` takes a `CampfireDb` and runs on its connection inside the caller's `Writes.run` (`ExecuteSql` with numbered holes; the one runtime-built `FormattableString`, whose table and column names must be listed by `pragma_table_info`, so `bin/verify`'s guard now also allows `Fixtures.fs`). `Testing.fs` (NullSink, RecordingSink, `BasicRichText`, `defaultEnv`) moved to `tests/Shared/Testing.fs` (linked into Db.Tests and App.Tests) with `Testing.prepare` and `Testing.loadFixtures`. `Data.options` now calls `Library.ensureConfigured()`: EF opens the file before the port's `Conn` in boot and in tests, and SQLite's library-wide settings (no memory statistics mutex) must precede the first connection.

Tests: `SchemaTests` on EF (loads then idempotent, paging index, pending migrations refused with the file left free, a failed load rolls back); a new `FixturesTests` case (an unknown table or column is refused and rolls back); `DifferentialTests`, `SeedTests` and `FixturesTests` read the files with plain ADO.NET (`Support.Raw`) and write through EF; `ColumnsTests` and `DatabaseTests` prepare through EF on a file (the tests retire in 1.11); the App test rigs prepare and load fixtures through EF. Differences: none. For 1.11: `Fixtures.Options.Now` is still the port's `Timestamp` inside the loader (the ERB `N.minutes.ago` arithmetic); `Database.Open` and `Testing.defaultEnv` still carry the old `Env`.

Gates: `bin/verify` (Release, warnaserror): 0 warnings, 1,068 tests, 1,061 passed, 0 failed, 6 environmental skips (the first full run had one Cable `ProtocolTests` timeout while the other projects ran at once; `bin/verify Cable` passed twice alone). `bin/db-differential`: ok (fixtures match ruby row for row, scenario matches ruby, export database, schema identity, Rails boots on our file). Image rebuilt (`parity/bin/candidate build`), `bin/clean-docker` run. `bin/pixel-parity --fast --seeds default --only "auth/**,account/**,rooms/**"` (the unit has no UI globs; this is a boot smoke): PASSED, 116/116 screenshots, aria and loose equal (one capture retried, auth/sign_in/error and rate_limited, then equal); `bin/clean-parity` run. `bin/cookie-roundtrip`: 20/20. Evidence in `bench/results/dotnetway-p1-9/`.

`bin/kloc`: 39,252 by upstream's rules (39.3 KLOC; 39,463 corrected).

### Phase 1, unit 1.10: Hot paths and guards (2026-10-19) - DONE

Tests (`tests/Campfire.App.Tests/HotPathTests.fs`, over the app's real pipeline and the `default` seed; `Boot.bootWith` lets a test add `LogTo` to the app's own EF options): **H1** requests the five workloads as two users with their own session tokens, two rooms, two pages, two searches and two post bodies, waits for the queued jobs, then again with other users' sessions, rooms, pages, queries and bodies: zero query compilations in the second round (the first round must show some, so the probe is known to be attached). Sabotaged to prove it is not vacuous: removing `EF.Parameter` from `Session.findByToken` fails H1 with 2 compilations (the two new sessions). The first draft reused the first round's tokens and did NOT catch that sabotage; the round-two sessions are new for that reason. **H2** (revised for LINQ-first): the command log of each workload is written to `bench/results/dotnetway-p1-10/command-log.md` and no statement carries the post body as a literal. **H3**: 16 concurrent posts for 3 s, every 2xx has its row (`before + 2xx = after`), posts/s recorded, not asserted (it runs beside the other suites through the harness's raw sockets: 422 posts/s alone, 55 under the parallel `bin/verify`; `bench/quick` decides the floor).

Statements per request (H2's log, final code): room page 9, messages page 9, sidebar 10, search 8, post a message 21 (5 before the write: ban, session, user, membership, room; the write: 2 inserts, message and room touches, unread memberships, FTS insert; then the render's reads). Every one is LINQ, `FromSql`/`ExecuteSql` or `Find`-shaped; none is `query { }`.

**Finding (the first bench of the phase, `bench/results/dotnetway-p1-10/quick.log`, with the port as same-day control).** H1 was green and the post was still under the floor: c=16 693 posts/s (floor 1,650), pages 3,773-4,612 req/s against the port's 33,600-38,100 on the same host. The cause is not compilation: **every F# lambda handed to a LINQ operator is rebuilt into an expression tree on each call, ~60 us a statement** (in-process: `Where(...)` built and not executed 62 us; the whole `FirstOrDefault` 118 us; the same predicate prebuilt 38 us; `FromSql` 39 us; `Find` 44 us). `EF.Parameter` fixes the compile (L1) but not this build. A 21-statement post pays it a dozen times, inside and outside the write gate; the profile (`profile-post/`) showed 2.6 MB allocated per post, GC pause 9%, and the gate (`WaitSubsystem.Wait`) as the largest wait.

Fixes, smallest first, each measured (all in `bench/results/dotnetway-p1-10/`):
1. Inside the gate, the three `ExecuteUpdate`s of a post (message touch, room touch, unread memberships) are `ExecuteSql` holes with `RailsTime.toDb` text (same SQL EF emitted), and the new message's search-index hook uses the body it just inserted instead of reading it back: post c=16 693 -> 1,364 posts/s (quick-post2).
2. The per-request lookups are `FromSql` with `LIMIT 1` and no composition (`Data.sqlFirst`): `Session.findByToken`, `User.findById`, `Room.findById`/`findForUser`, `Membership.findByRoomAndUser`, `Account.first`, `Ban.banned`, `RichTextRecord.findFor`, `Message.findById`, the message's attachment row. The LINQ-first revision allows this only after LINQ shapes failed the floor, which they did; recorded here as the reason for each. Everything else stays LINQ.

**Gate (`bench/quick`, 1 rep, c=1 and c=16, final code `quick3.log`; Rails medians from phase7-final):**
| workload | c=16 req/s | x Rails | c=1 req/s | CPU us/req (c=16) |
|---|---:|---:|---:|---:|
| room page | 6,343 | 26.5x | 1,789 | 532 |
| messages page | 5,196 | 13.6x | 1,502 | 665 |
| sidebar | 4,075 | 6.8x | 1,180 | 850 |
| search | 5,036 | 12.3x | 1,388 | 692 |
| post a message | 1,807 | 5.5x | 913 | 1,672 |
All five over the 5x floor; the thin ones are post (5.5x) and sidebar (6.8x). Same-day control, the port's image (`quick.log`, run before the fixes): room 35,850, messages 38,138, sidebar 34,488, search 33,616, post 8,541 req/s at c=16, so theDotNetWay is 0.13-0.18x of the port on pages and 0.21x on posts: the EF layer costs 5-8x the port's CPU per request (532-850 vs 94-109 us on pages; 1,672 vs 360 on posts). The 5x-Rails floor is what the phase is held to; the ratio to the port is recorded, not a gate. One rep on a host with ratio 1.05-1.14 (the post c=16 run before the fixes had p99 208 ms and a settled tail 70% above its measured rate): **a 3-rep confirmation is still owed at the phase close** (post and sidebar are within 10-35% of the floor).

Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror) 0 warnings, 1,070 tests, 1,064 passed, 0 failed, 6 environmental skips; one run in four had `SqlTests.the library runs without its memory statistics mutex` fail under the full parallel run (process-wide SQLite setting; passes alone and on the next full run; order-dependent, predates this unit, not investigated). Image rebuilt three times from HEAD (`build*.log`), `bin/clean-docker` after each. Not run: `bin/pixel-parity`, `bin/db-differential`, `bin/cookie-roundtrip` (the SQL changed on six lookups and three updates with the same statements' semantics, covered by the 1,064 seed-backed tests; the pixel and db-differential gates are owed by 1.11, which closes the phase).
`bin/kloc`: 39,257 by upstream's rules (39.3 KLOC; 39,468 corrected). Differences recorded: none visible.
For 1.11 and the phase close: the remaining `Where(lambda)` finders cost ~60 us each on the pages (sidebar 10 statements, room page 9); if a page falls near the floor, port the same `FromSql` shape to its remaining lookups before touching anything else.

### Phase 1, unit 1.11: Delete the topology (2026-10-19) - DONE

Code in `df697be`: `Database.fs` (reader pool, writer thread, checkpointer), `Sql.fs` (`Conn`, `Row`, `SqlArg`, the statement cache), `Columns.fs` and `Time.fs` (the `Timestamp` struct) are deleted, with `AppState.Db`, `AppState.Read`/`ReadOffloaded`/`Write`, `DbError.WriterGone`, `Config.DbReaders` (`RAILS_MAX_THREADS` is no longer read) and every legacy `Conn` reader in `Models.fs` (`fromRow`, `findLegacy`, `Account.firstLegacy`). What survived moved: the random tokens (`alphanumeric`, `base58`, `uuid`) to `Tokens.fs`, the SQLitePCL library settings (no memory-statistics mutex, multi-thread mode) to `Ef/Data.fs` as `Library`, the write gate is built by `Boot` and held by `AppState.Gate` and `Deps.Gate`. `Fixtures` has a private argument type and a `DateTime` base time; `Timestamp` exists only as RailsCompat's own alias of `DateTimeOffset`. Shutdown (`Boot.closeDatabase`) runs `PRAGMA wal_checkpoint(TRUNCATE)` on a pooled context and clears Microsoft.Data.Sqlite's pools; with the old checkpointer gone, WAL growth between shutdowns is SQLite's own auto-checkpoint, and no EF writer can wait on a `RESTART` taken after the gate was released (checkpoint note 8).

Tests: `SqlTests`, `ColumnsTests`, `DatabaseTests`, `TimeTests` retired (their Rails-behaviour cases live in W*, D2, D3, D4) and W7 (the old writer and EF sharing the gate). Tests that read the file or put in rows no model makes use plain ADO.NET: `Support.Raw` (Db tests; `?` marks become numbered parameters) and `RawSql` with `Test.Sql`/`Test.Execute` (App tests; `Execute` holds the write gate). `ErrorTests` uses an in-memory ADO.NET connection. Every test that used `findLegacy` or `db.Read` reads through EF.

Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,021 tests, 1,015 passed, 0 failed, 6 environmental skips (1,070 before: 49 tests retired, none added). Image rebuilt from `df697be` (`bench/results/dotnetway-p1-11/build.log`), `bin/clean-docker` run. `bin/db-differential`: ok (fixtures and scenario match ruby, export database, schema identity, Rails boots on our file; `db-differential.log`). `bin/pixel-parity --fast` on every state: PASSED in 1,671 s, default 361/361, crowd 12/12, custom_styles 16/16, first_run 8/8, restricted 4/4, screenshots, aria and loose equal (one default cell, `auth/sign_in/rate_limited`, was captured again and matched; `pixel-parity.log`); `bin/clean-parity` run. `bin/cookie-roundtrip`: 20/20 checks passed (`cookie-roundtrip.log`). `bin/verify`'s guards unchanged (no `*Raw` outside `Schema.fs`; `FormattableStringFactory` only in `Models.fs` and `Fixtures.fs`; no `query { }`). No `Conn` of the database layer is left in `src` (Kit's `Front/Conn.fs` is the HTTP connection).
`bin/kloc`: 38,132 by upstream's rules (38.1 KLOC; 38,343 corrected), down from 39,257 at 1.10. Differences recorded: none.
Owed at the phase close, not in this unit: `bench/quick` against a same-day control with 3 reps (1.10's figures are one rep each), `bin/pixel-parity` on the lean default matrix (this unit ran `--fast`), the phase review.

### Phase 1: gate (2026-10-19) - DONE

Evidence: `bench/results/dotnetway-p1/` (gate run on `aa1cc73`, resumed after a session limit) and `bench/results/dotnetway-p1-fix/` (the sidebar fix and its re-run, on `6ef24da`).

**Pixel gate (lean, `--fast` off, slices with `--only`, realtime one state at a time), on `aa1cc73`:** every cell equal on screenshots, aria and loose (the loose layer re-counted over all 940 cells after Phase 0's `loose.ts` fix): default 860/860, crowd 24/24, custom_styles 32/32, first_run 16/16, restricted 8/8 = 940/940 on each layer (logs in `dotnetway-p1/pixels/`). After the sidebar fix (image rebuilt from `6ef24da`): rooms/** (default 170, crowd 16, custom_styles 8, restricted 4), realtime/unread_in_sidebar 8, realtime/new_room_in_sidebar 8, users/** (default 72, custom_styles 4): all equal on the three layers (`dotnetway-p1-fix/pixels/`).
**Other gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` 1,021 tests, 1,015 passed, 0 failed, 6 environmental skips (before and after the fix); `bin/db-differential` ok; `bin/cookie-roundtrip` 20/20.

**Finding and fix.** The first 3-rep bench had every workload over the 5x-Rails floor except sidebar c=1 (4.118x Rails, 1,180 req/s vs 287). The sidebar's three remaining `Where(lambda)` statements (direct rooms of the user, member ids of those rooms, placeholder users) each paid the ~60 us expression-tree rebuild (unit 1.10). LINQ first was not available for them: prebuilt expressions cannot capture the per-request user id without a lambda, and the one statement that could be merged was (the direct rooms' member ids are now one `FromSql` over a subselect, so the sidebar runs 9 statements, not 10). Per the design's floor rule, they are now `FromSql` (`Room.forUserOfType`, `Room.userIdsOfDirectRoomsOf`, `User.activeExcluding`; same SQL semantics, same order). Reason recorded: sidebar c=1 under the floor, 60 us x 3 per request.

**Speed (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps, final code; Rails medians from phase7-final; same-session control = the F# port's image, 3 reps, in `dotnetway-p1-fix/control.md`; host load 4-14 during the run, probes 1.0-1.13, `room_show c=16` flagged noisy):**
| workload | c=16 req/s | x Rails | c=1 req/s | x Rails | vs port, same session (c=16 / c=1) |
|---|---:|---:|---:|---:|---|
| room page | 6,013 | 25.1x | 1,250 | 11.7x | 0.16 / 0.17 |
| messages page | 5,178 | 13.6x | 1,501 | 6.9x | 0.13 / 0.19 |
| sidebar | 5,896 | 9.9x | 1,703 | 5.9x | 0.16 / 0.22 |
| search | 5,071 | 12.4x | 1,480 | 7.7x | 0.15 / 0.26 |
| post a message | 1,857 | 5.6x | 930 | 5.6x | 0.50 / 0.41 |
All ten cells are over the 5x floor; none is a blocker. Thinnest: post (5.57-5.63x) and sidebar c=1 (5.94x). All responses 2xx, 0 connection errors in 30 runs; post rows written equal the 2xx answers. The ratio to the port is recorded, not a gate.

`bin/kloc`: 38,141 by upstream's rules (38.1 KLOC; 38,352 corrected). Differences recorded: none visible. `bin/clean-docker` and `bin/clean-parity` run.

### Phase 1: independent verification (2026-10-19) - PHASE 1 DONE; Phase 2 GO, with conditions

Opus verifier, on `e28f816` plus this entry's fixes. Checked against code, logs and re-runs, not the unit reports.
- **Topology gone:** `Database.fs`, `Sql.fs`, `Columns.fs`, `Time.fs` are deleted. No database `Conn`, `ReadOffloaded`, `WriteBlocking`, `DbReaders` or `findLegacy` is left in `src` (Kit's `Front/Conn.fs` is HTTP). `bench/db-micro` was left behind: it still used the deleted `Conn` and no longer built (it is outside the solution, so `bin/verify` never saw it). Deleted here, as design section 6 says.
- **SQL by construction:** `*Raw` appears only in `Schema.fs`, `FormattableStringFactory` only in `Models.fs` (insert_all: constant text and numbered holes) and `Fixtures.fs` (names checked against `pragma_table_info`), and there is no `query { }`. The only ADO.NET command texts are the constant pragmas (`Interceptors.fs`) and the backup's `busy_timeout` int (`Boot.fs`). `bin/verify` now also guards `CommandText`/`CreateCommand(` outside those two files (clean on the tree, fails on a planted line). The sidebar fix's three `FromSql` (6ef24da) are interpolated holes; `activeExcluding` passes the id list as one JSON parameter.
- **One writer:** every `SaveChanges`/`ExecuteSql`/`ExecuteUpdate`/`ExecuteDelete`/`Data.insert|update|remove` in Db, App and Storage was traced to a `Tx` (`Writes.run`/`c.Write`/`app.WriteEf`). The only exceptions are `Schema.prepare` at boot, the shutdown checkpoint and fixtures inside the caller's write. Nothing enforces this at run time (see open items).
- **Callbacks and timestamps:** `Message.create`/`touch`/`destroy`, `Room.touch`/`receive` and the unread marks after 1.10's `ExecuteSql` rewrite match the checkpoint-reviewed semantics: times are `RailsTime.toDb` text, and the search index and `room.receive` run after commit.
- **H1 is live:** on the final code, removing `EF.Parameter` from `Boost.forMessage` (post-a-message path) fails H1 with 2 compilations. Restored.
- **Re-run here:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` twice: 0 warnings, 1,021 tests, 1,015 passed, 0 failed, 6 skipped. The 3 Db skips are the differential tests `bin/db-differential` runs. `bin/db-differential`: ok (fixtures and scenario match Ruby, schema identity, rollback ok). `bin/cookie-roundtrip`: 20/20 on the `6ef24da` image (label checked; `src` differs since only in comments). Logs: `bench/results/dotnetway-p1-verify/`.
- **Evidence checked, not re-run:** the pixel slices sum to 940/940 on each layer. Every one of the 13 top-level state groups and all 13 `realtime/*` states is there. The bench figures in `dotnetway-p1-fix/quick-compare.txt` match the gate entry. The image under the bench is `6ef24da`.
- **KLOC:** `bin/kloc` 38,141 by upstream's rules, **38,352 corrected (38.4 KLOC; the column the plan's budgets use)**, from 39,428 corrected at 1.0 (-1.08). The design projected about 38.0 (-1.4) and the plan 37.4. `Campfire.Db` is 2,280 corrected against the projected ~2.0. Over the projection by 0.35, which is recorded, not a gate.
- **Speed:** all ten cells are over 5x Rails. The floor is thin on post (5.6x, 1,857 posts/s against 1,650) and sidebar c=1 (5.9x). The Rails side is the stored phase7-final medians, not a same-session run: in the same session the port's post ran 3,704/s against its stored 8,905, so the host was slow.
- **Differences recorded:** none visible. The WAL checkpoint is SQLite's auto-checkpoint again (Rails' behaviour). This README had no checkpointer note to remove. The Rust README's paging-index difference stands.
- **Fixed here:** `bench/db-micro` deleted, AGENTS.md's tier-1 line and query rule brought to LINQ-first, three stale comments (`Timestamp`, `Conn`), and the new `bin/verify` guard.
- **Open, for later phases:** (1) single-writer has no run-time check: the old readers' `query_only = ON` went with the reader pool, and `Data.insert`/`ExecuteSql` take any `CampfireDb`. A command interceptor refusing non-SELECT statements outside the gate would restore it. (2) `Writes.run` can return Error after a commit, when an after-commit hook fails (checkpoint note 7). (3) Post and sidebar have ~10% headroom over the floor: Phase 2's antiforgery and auth handler sit on every request, so bench post early in Phase 2, not only at its close.

### Phase 2, unit 2.0: Phase 1 carry-overs (2026-10-19)

Closes open items (1) and (2) of the Phase 1 verification entry. Code in `8d5cd20`.
- **Single-writer, checked at run time:** `SingleWriterGuard` (a `DbCommandInterceptor` in `Ef/Interceptors.fs`, registered in `Data.options`) refuses INSERT/UPDATE/DELETE/REPLACE/CREATE/DROP/ALTER/VACUUM/REINDEX (and a `WITH` that contains one) unless the flow holds the write gate or is inside `WriteGate.Exempt()`. SELECT and PRAGMA pass (the shutdown checkpoint needs no exemption). `Writes.run` marks the flow as holding the gate from `Enter` to `Exit`, so the body, the commit and the after-commit hooks (their own autocommits) may write; `InWrite` keeps its narrower meaning (BEGIN to COMMIT). `Schema.prepare` is the one production exemption (boot). Test setup that writes straight through a context exempts its own flow (`EfTests.contextOver`, `LinqTests.counting`, Storage `TestDb.Open`). Test W8: a stray `ExecuteSql`, `Data.insert` and `ExecuteDelete` fail (EF wraps the `SaveChanges` one in a `DbUpdateException`) and write nothing; reads and a pragma pass; `Exempt` allows and ends; the body and an after-commit hook of `Writes.run` write; refused again after the run. A refused `SaveChanges` leaves its entity tracked, so a caller that catches it must clear the tracker (the test does).
- **After-commit failure:** `Writes.run` logs a failing hook and still returns `Ok value`: the data committed, so the type no longer says the write failed (W4 now asserts `Ok`, the later hooks run, the row is there). The first-failure bookkeeping is gone.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: 0 warnings, 1,022 tests, 1,016 passed, 0 failed, 6 skipped (the same 6). H1 (zero compilations) is in that run and green. One earlier run failed `Cable GoldenTests.replays reference frames` ("expected confirm_subscription, got []", a 10 s timeout) and passed on the immediate re-run with no change; not caused by this unit (no Db write path in it), noted as a flake to watch.
- **Speed (post a message only, 3 reps, `bench/results/dotnetway-p2-0/compare.txt`):** the host drifts between sessions (one control run came out at 1,442/s, 4.4x), so only back-to-back pairs count. Pair 2, tier 2 mounted builds of the new tree against `063b940`: new 1,875/s c=16 and 963/s c=1 (5.68x, 5.77x Rails) against control 1,848/s and 946/s (5.60x, 5.67x); CPU 1,653 against 1,676 us/req. The guard costs nothing measurable. Pair 1 (new mounted against the stored image) read 3% lower (1,826 against 1,882) with non-overlapping ranges, which pair 2 does not reproduce; the floor held in every non-drifted run.
- **KLOC:** `bin/kloc` 38,183 by upstream's rules, **38,395 corrected (38.4 KLOC)**, from 38,352 at the Phase 1 verification (+0.04).
- **Differences recorded:** none; the guard is internal and Rails has no equivalent surface.

### Phase 2, unit 2.1: Seam (2026-10-19)

Code in `54254bb`. No behaviour change.
- **Built:** `src/Campfire.App/Web/` (`Errors`, `Action`, `Current`, `Legacy`, `Services`), compiled before `Concerns.fs`; `FsToolkit.ErrorHandling` 5.2.0. Every `RouteTable` row's `Action` is a `Web.Action` (`Legacy.wrap` of the old `Ctx` action, which also sets `MatchedRoute` and the fragment cache scope). `RouteTable.dispatch` is now an `HttpContext` action: it recognizes the request, puts the path parameters in `Request.RouteValues` (which `Adapter.dispatch` already merged into the `Ctx`) and runs the row through `taskResult`. Recognition failures go through `Errors.respond`, which renders through the kit's `Ctx.Finish` like any legacy error, so the pages, headers and log lines are the same. `Boot.pipeline` became `Web.Services.pipeline` (Boot keeps `publicFiles`; `rawPath` moved to `Services`). `Ctx.Http` already existed (Phase 1).
- **Services (`Services.add`, run from `Booted.ConfigureServices`, so the real host and the test hosts get the same):** `Kit`, `Secrets`, `AppState` singletons; ephemeral data protection; `AddDistributedMemoryCache` (64 MiB); `AddSession` (`_campfire_flash`, HttpOnly, Lax, essential, 2 h idle); `AddAuthentication()` with no scheme yet (units 2.2 and 2.3 add them). Nothing runs them (no `UseSession`, no `UseAuthentication`), so requests are unchanged. The `SuppressAutoDefaultScheme` switch is set twice: as a `RuntimeHostConfigurationOption` in `Campfire.App.fsproj` and by `Services.add` (`AppContext.SetSwitch`), because a test host's runtimeconfig is not the app's.
- **Differences from the design note, on purpose:** `Web.Current` reuses `Campfire.App.AuthenticatedBy` (`Anonymous | BySession | BotKey`) rather than a second type with other case names, so legacy and Web concerns share one until 2.13. `Web.Error` has a `Legacy of Campfire.Kit.Error` case (deleted with the kit's `Ctx` in 2.13). `Services.add` takes the built `Kit` and `AppState`; `Services.pipeline` takes `publicFiles`, the cable handler and the dispatch action, because Controllers and Boot compile after it.
- **Tests (`tests/Campfire.App.Tests/Web/SeamTests.fs`, 6):** `ICsrfProtection`, `IAuthenticationSchemeProvider` and `ISessionStore` resolve from a request's `RequestServices` on net11.0 (risk 3 of the design note is clear: the slim builder registers `ICsrfProtection`); no default authenticate or challenge scheme; `Action.run` (Ok, `Halt`, 404, 503, 422, a throw is 500); `Legacy.wrap` reads `RouteValues` as path params; `Current` is one object per request; every row wrapped and `/up`, an unknown path (404) and an invalid-UTF-8 path parameter (400) answer as before.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: 0 warnings, 1,028 tests, 1,022 passed, 0 failed, 6 skipped (the same 6); H1 is in that run and green. `bin/pixel-parity --fast --only 'auth/**,errors/**,bot_api/**'` on the `54254bb` image: default 36/36, custom_styles 2/2, first_run 8/8 equal on screenshots, aria and loose DOM (crowd and restricted have no such states), PASSED. Reports under `parity/out/p2-1` (ignored).
- **Speed (`bench/quick`, post a message and sidebar, 1 rep on the `54254bb` image, `bench/results/dotnetway-p2-1/new`; host 1.04-1.08):** post 1,853/s c=16 (5.61x Rails) and 935/s c=1 (5.60x), against unit 2.0's same-day control 5.60x and 5.67x; sidebar 5,800/s (9.69x) and 1,762/s c=1 (6.15x). All over the floor; the seam adds one singleton lookup per request. All 2xx, 0 errors, rows written equal answers.
- **KLOC:** `bin/kloc` 38,169 by upstream's rules, **38,381 corrected (38.4 KLOC)**, from 38,395 (-0.01: Boot lost 40 lines, `Web/` added 0.22 and the route table's wrapper 0.01, all of it scaffolding the later units fill or delete).
- **Differences recorded:** none visible. `bin/clean-docker` and `bin/clean-parity` run.

### Phase 2, unit 2.2: Cookie auth (2026-10-20)

Code in `2038298`; this entry and the evidence close it (a first attempt was cut off by a session limit after the code commit).
- **Built:** `Web/TicketFormat.fs` and `Web/Authentication.fs` (`restore`, `require`, `session`, `startNewSessionFor`, `terminateCurrentSession`), a cookie scheme whose ticket is Rails' signed-cookie bytes, and `OnValidatePrincipal` as the session lookup. The legacy `Concerns` call these through `c.Http`; the Kit jar's `session_token` paths are gone. Cable's own cookie check is untouched (Phase 3).
- **Differences from the design note, on purpose:** the hourly resume write is `restore`'s, not the event's (it needs the kit's remote IP until unit 2.6), and the re-issue is a `SignInAsync`, not `ShouldRenew`.
- **Tests:** A1-A8 in `tests/Campfire.App.Tests/Web/AuthenticationTests.fs` (9 tests, A3 has two).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` re-run on 2026-10-20: 0 warnings, 1,037 tests, 1,031 passed, 0 failed, 6 skipped (the same 6); H1 in that run and green. `bin/cookie-roundtrip` 20/20 both ways. `rails_compat_verify_rust.rb` on a cookie our app minted: 51 checks, 0 failures. `bin/pixel-parity` auth/**: PASSED; a realtime state: PASSED.
- **Speed (`bench/quick`, 3 reps, `bench/results/dotnetway-p2-2/bench`):** room page 27.3x (c=16) and 19.0x (c=1) Rails; post a message 5.70x (c=16) and 5.69x (c=1) Rails. All over the 5x floor, about 14% headroom on post; no speed fix needed.
- **KLOC:** `bin/kloc` 38,513 by upstream's rules, **38,725 corrected (38.7 KLOC)**.
- **Differences recorded:** none visible.

### Phase 2, unit 2.3: Bot keys (2026-10-20)

Code in `100e381` (the scheme) and the sign-out fix after it. Evidence in `bench/results/dotnetway-p2-3/`.
- **Built:** `BotKeyAuthenticationHandler` (`Web/Authentication.fs`), registered as the `BotKey` scheme beside `Session` (no policy scheme, no default). It reads `params[:bot_key]` (path, query or body) from the request's `Ctx`, which `Adapter.dispatch` now leaves in `HttpContext.Items` under `Adapter.CtxItemKey`, so the key means what Rails' `params` means. A blank or unknown key is "no result"; a key that is not a string (a hash or array) fails, which `require` answers as a 500 like Rails' `NoMethodError` on `strip`. `Authentication.require` is the session, then the bot key (`botAuthenticate`), then the challenge (still the legacy `request_authentication`, which unit 2.5 moves). `Authentication.denyBots` is `deny_bots`; `authenticated_by` is `Web.Current.AuthenticatedBy` (`BotKey`). The legacy `Concerns.botAuthentication` and `denyBots` call these through `c.Http`.
- **Found and fixed by the replay (a unit 2.2 slip):** `controllers_a/replay.py` showed `DELETE /session` deleting `session_token` with `expires` and `httponly` where Rails writes `max-age=0` and no `httponly` (`SignOutAsync` cannot write `max-age`). Sign-out now appends the kit's `CookieJar.deleteCookieHeader`; A6 asserts the attributes. Second replay: 152 matched, 3 differed by design, 0 differed.
- **Tests (`Web/BotKeyTests.fs`):** B1 (valid key creates a message as the bot; the same key is `authenticated_by` bot key, so `deny_bots` refuses it elsewhere), B2 (wrong, blank and malformed keys: 302 to `/session/new`), B3 (a valid key on three non-bot pages: 403; a signed-in session wins over a key), B4 (cross-site `Origin` and `Sec-Fetch-Site` with a bot key: 201; the same without one: 422), B5 (a hash `bot_key`: 500), B6 (`/up` and a public file with a key in the query: 200, nothing authenticates).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: 0 warnings, 1,043 tests, 1,037 passed, 0 failed, 6 skipped (the same 6); H1 in that run and green. `bin/pixel-parity --fast --only 'bot_api/**,auth/**,errors/**'`: default 36/36, custom_styles 2/2, first_run 8/8 equal on screenshots, aria and loose DOM (crowd and restricted have no such states), PASSED. `controllers_a/replay.py` (default seed): above. `bin/cookie-roundtrip`: 20/20 both ways, on the unit's image. One earlier run of `bin/verify` failed `Kit FrontTests.closes idle and slow connections` (a timing test, passed on the immediate re-run with no change).
- **Speed (`bench/quick`, post a message and sidebar; `bench/results/dotnetway-p2-3`):** the host was slow all session (probe 1.10-1.11, DisplayLink and wallpaper helpers using the Mac's CPU), so the numbers are under the 5x floor for both the new tree and the control. Same-session control, two back-to-back pairs of tier 2 mounted builds, 2 reps each (new `100e381`+fix against `6476beb`): post c=16 1,418 and 1,438 against control 1,408 and 1,376 (4.30x, 4.36x against 4.27x, 4.17x); post c=1 806 and 819 against 819 and 795; sidebar c=16 4,426 and 4,430 against 4,432 and 4,283; sidebar c=1 1,496 and 1,486 against 1,492 and 1,427 (5.2x against 5.2x, 5.0x). CPU per request is equal (post 2,187 and 2,182 us against 2,224 and 2,312). The unit costs nothing measurable: a request with a session never reaches the bot scheme, and one without a bot key adds one `Items` lookup and one `Params.Get`. The Rails side is the stored phase7-final medians. The 5x floor was last measured over (5.70x post) on an unloaded host in unit 2.2; it needs a re-measure when the host is quiet, and the first run of the new image (3 reps, `bench/results/dotnetway-p2-3/bench`) read post 4.31x/4.83x and sidebar c=1 5.16x for the same reason.
- **KLOC:** `bin/kloc` 38,565 by upstream's rules, **38,777 corrected (38.8 KLOC)**, from 38,725 (+0.05).
- **Differences recorded:** none visible. `bin/clean-docker` and `bin/clean-parity` run.

### Unit 2.3 addendum (2026-10-20): speed floor re-measured on a quiet host
`bench/quick --routes post_message,sidebar` (3 reps, image `campfire-dotnetway:app`, host probe ratio 1.06-1.07) against stored Rails medians:
sidebar c=16 10.11x, sidebar c=1 6.12x, post c=16 5.68x, post c=1 5.59x. All at or above the 5x floor; every response 2xx, bytes equal Rust's.
The earlier 4.3x reading was host load (DisplayLink and wallpaper helpers); the same-session control had already shown the unit adds no cost. No speed fix was needed.
Evidence: bench/results/dotnetway-p2-3/quiet/.

### Phase 2, unit 2.4: CSRF (2026-10-20)

Code in `80e2042`. Evidence in `bench/results/dotnetway-p2-4/`.
- **K5 holds on .NET 11:** `ICsrfProtection` is registered by the slim builder (unit 2.1's test) and `DefaultCsrfProtection` behaves as section 1 says: `Sec-Fetch-Site` `same-origin` and `none` pass, `cross-site`, `same-site` and an unknown value are refused, then `Origin` against scheme and host, and a request with neither header passes. No hand-rolled check.
- **Built:** `Web/Csrf.fs` (`verifyAuthenticityToken`: GET/HEAD pass, every other method is `ICsrfProtection.ValidateAsync`, a denial is `InvalidAuthenticityToken`, so 422 and the public page through the existing error path). `Concerns.verifyAuthenticityToken` is what the before-action chain, the Active Storage blob actions and `Mailbox.conductor` call, in Rails' order (after authentication and `deny_bots`, skipped for bot-key requests). `Ctx.VerifyAuthenticityToken`, `ValidRequestOrigin` and the `ForgeryProtectionOriginCheck` setting are deleted from the kit; only `Ctx.MarkForSameOriginVerification` stays, for `verify_same_origin_request`. README "Known differences" has the rewritten CSRF entry (class B).
- **Deliberate difference from the design note:** the check runs on `Request.Scheme`, and the `assume_ssl` and forwarded-headers middleware are unit 2.6's. Until then `Csrf.verifyAuthenticityToken` borrows `https` for the call when the kit says the request is HTTPS (`assume_ssl` or `X-Forwarded-Proto`), so C12 and design risk 4 hold now (production would otherwise refuse every browser `Origin: https://...` that lacks Fetch Metadata). `Host` is not borrowed: `X-Forwarded-Host` arrives with 2.6. Unit 2.6 deletes the borrowing.
- **Tests:** C1-C12 in `tests/Campfire.App.Tests/Web/CsrfTests.fs` (a form post, a `DELETE` through `_method` and a JSON `PATCH`, each per case; the 422 body is `public/422.html` byte for byte, JSON for the JSON write; `allowed` also rejects a redirect to sign-in, since authentication runs first and would pass vacuously). The Kit's forgery tests (`HttpTests` x3, `RailsVectorTests` x2 plus the cross-site asserts) tested the deleted method and are removed. `bin/kit-differential http` compares the Kit app with Rust's and so no longer applies to forgery; not run (needs mise).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: 0 warnings, 1,050 tests, 1,044 passed, 0 failed, 6 skipped (the same 6); H1 in that run and green. `bin/pixel-parity --fast --only 'interactions/**'` on the `80e2042` image: default 79/79 equal on screenshots, aria and loose DOM (the other seeds have no such states), PASSED. `bin/cookie-roundtrip`: 20/20.
- **Speed (`bench/quick`, 3 reps, post a message and sidebar, host probe 1.06-1.07):** post 1,813/s c=16 (5.49x Rails) and 910/s c=1 (5.45x); sidebar 5,835/s (9.75x) and 1,727/s c=1 (6.03x). All over the 5x floor; post is down about 2% from unit 2.3's quiet-host 5.68x and 5.59x (one `ICsrfProtection` lookup and a header read per write). Headroom on post is now about 9%. All 2xx, 0 errors, rows written equal.
- **KLOC:** `bin/kloc` 38,569 by upstream's rules, **38,781 corrected (38.8 KLOC)**, from 38,777 (+0.00: the kit lost its check, `Web/Csrf.fs` and the tests' share is in the test projects).
- **Differences recorded:** CSRF (README, class B): `same-site` is refused, a header-less write over HTTPS passes. `bin/clean-docker` and `bin/clean-parity` run.

### Phase 2, unit 2.5: Session and flash (2026-10-20)

Code in `72ed71d`. Evidence in `bench/results/dotnetway-p2-5/`.
- **Built:** `Campfire.Kit/Flash.fs` replaces `Session.fs` (deleted, with `SessionConfig`, the encrypted `_campfire_session` path and the sid): `Flash` is the unchanged `FlashHash` semantics, now stored as JSON under the `flash` key of ASP.NET Core's `ISession` (cookie `_campfire_flash`, K6); `Flash.load`/`Flash.commit`, `ReturnTo.set`/`take` and `SessionReset` are the helpers over `HttpContext.Session`. `Ctx.Flash()` delegates to them, `Ctx.Session()` is gone (its two callers, `request_authentication` and `post_authenticating_url`, use `ReturnTo`), `Ctx.ResetSession()` clears the session and deletes the cookie. `Services.pipeline` adds `UseCookiePolicy` (SameAsRequest secure, Lax minimum) and `UseSession` after public files, so assets never pay for them. `last_room` is `Response.Cookies.Append` with `Path=/`, `SameSite=Lax` and an `Expires` 20 calendar years out on the harness clock.
- **Deviation from the design note:** the flash module lives in the Kit (`Campfire.Kit/Flash.fs`), not `Web/Flash.fs`: `Ctx` (Kit) calls it in `RedirectToWith`, `CombineEtags` and `Commit`, and Kit cannot reference App. It moves with `Ctx` in unit 2.13. The legacy challenge (`request_authentication`) still builds its redirect through the kit's `Ctx` (the `Response` helpers are unit 2.8's); only its session write moved.
- **Speed seams:** a request without the `_campfire_flash` cookie never loads the session (`Flash.load` and `ReturnTo.take` return empty without touching the store), and writes are committed with `CommitAsync` before the response starts (the middleware would commit after it, when a quick client may already have asked again). F8 counts the store: zero reads or writes for `/session/new`, `/up` and `/account/edit` with no session cookie. The session middleware itself still issues a `RefreshAsync` of its fresh key per request (a no-op lookup in the memory cache); not counted, and the bench shows no cost.
- **Tests:** F1-F7 and F8 (the store is untouched) in `tests/Campfire.App.Tests/Web/FlashTests.fs`. F5 uses the test's own capturing `ILoggerFactory` instead of `FakeLogger` (no new package). Kit tests moved to the session: `Harness.startProtocols` registers `AddSession`/`UseSession`; the Kit `HttpTests` session and flash tests use `_campfire_flash`; the Rails-session carry-over test is deleted (F5 holds the behaviour); `AppTests`' whoami pipeline gained `UseSession`.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,056 tests, 1,050 passed, 0 failed, 6 skipped (the same 6; the first run had one Cable `GoldenTests` miss under load that passed alone and in the rerun). H1 green. `bin/pixel-parity --fast --only 'account/**,users/profile/**,auth/sign_in/**'` on the `72ed71d` image: default 30/30, crowd 4/4, custom_styles 10/10, restricted 2/2 equal on screenshots, aria and loose DOM, PASSED in 207 s. `bin/cookie-roundtrip`: 20/20. `bin/clean-parity` and `bin/clean-docker` run.
- **Speed (`bench/quick`, all workloads, 3 reps, host 1.04-1.10):** room page 5,393/s c=16 (22.5x Rails), 1,612 c=1 (15.1x); messages page 5,130 (13.5x), 1,466 c=1 (6.7x); sidebar 5,734 (9.6x), 1,641 c=1 (5.73x); search 4,955 (12.1x), 1,382 c=1 (7.2x); post a message 1,822 c=16 (5.52x), 891 c=1 (5.34x). All over the 5x floor, all 2xx, 0 errors. Against unit 2.4 (quiet host) post is -0.5% at c=16 and -2% at c=1, sidebar c=1 -5% (6.03x to 5.73x) at a host probe of 1.07 against 1.06: within the run-to-run swing, so no speed fix was needed. Post c=1 headroom is 7%.
- **KLOC:** `bin/kloc` 38,488 by upstream's rules, **38,700 corrected (38.7 KLOC)**, from 38,781 (-0.08).
- **Differences recorded:** the Rails session (README, class B): flash and return-to in server memory under `_campfire_flash`, Rails' cookie untouched; `last_room` attribute order (class A).

### Phase 2, unit 2.6: Requests (2026-10-20)

Code in `ce87a52`. Evidence in `bench/results/dotnetway-p2-6/`.
- **Built:** the pipeline is now `UseExceptionHandler(Errors.exceptionHandler)`, `UseForwardedHeaders`, the `assume_ssl` middleware, the
  default headers (request id, `X-Runtime`, HSTS, secure cookies, `force_ssl`'s redirect), public files, a 16 MiB body cap,
  `UseHttpMethodOverride(_method)` and the upper-casing step, then cookie policy, session and routing. `Adapter.configureForwardedHeaders`
  fills `ForwardedHeadersOptions` from `TRUSTED_PROXIES` (`KnownProxies` cleared, no forward limit); `Request.fs` lost the hand-written
  `Forwarded`/`X-Forwarded-*` parsing, `calculateRemoteIp` and the `Client-Ip` spoofing check (`Request.RemoteIp` is the peer the
  framework left, `Host` the header it left); `RequestState.Ssl` is `Request.IsHttps`, and `Csrf` no longer borrows `https` for its call
  (unit 2.4's stopgap). `Errors.exceptionHandler` renders an escaped exception as Rails' 500 page, a `BadHttpRequestException` with its status
  (413 for the body cap) and an unreadable form as 400; it marks the body abandoned so the renderer does not parse it again.
  `Adapter.app` (the Kit's own test pipeline) uses the same pieces.
- **Differences from the design note, on purpose:** the override reads the form through a buffered body (`EnableBuffering`, rewound after) because the
  kit's body parser still needs the bytes until unit 2.7, so a form POST is parsed twice for now (small: the bench shows no cost on post). `FormOptions`
  carries Rack's `params_limit` (4096 pairs) and the 16 MiB cap. An empty `KnownIPNetworks` list would make the framework trust every peer; `TRUSTED_PROXIES`
  is never empty. The kit's own `Body.fs`/`Multipart.fs` map Kestrel's 413 to their `TooLarge` instead of a 400.
- **Tests:** R1-R6 in `tests/Campfire.App.Tests/Web/RequestsTests.fs` (8 tests: R1 trusted and untrusted peers, R2 forwarded proto/host with secure cookies and
  `assume_ssl` with HSTS, R3 `_method` delete/DELETE/patch/multipart/bogus/header/JSON, R4 urlencoded, multipart, raw and chunked bodies over 16 MiB,
  R5 request id sanitized/generated and default headers on pages not on `/account/logo`, R6 the exception handler and a throwing action). A sabotage run
  (no upper-casing, no forwarded headers, no `assume_ssl`) failed R1, R2 and R3. Kit tests moved with the behaviour: `RequestTests` (the proxy cases are now
  the framework's), `HttpTests` (`X-HTTP-Method-Override` and `Client-Ip` are not read, a public peer's `X-Forwarded-For` is ignored, `_method=bogus` is a 404),
  and the differential's `request` cases that depend on proxy headers are no longer compared with Rust's hand-written answers.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,063 tests, 1,057 passed, 0 failed, 6 skipped (the same 6); H1 in that run and
  green. `bin/pixel-parity --fast --only 'errors/**,auth/**,interactions/**'` on the `ce87a52` image: default 115/115, custom_styles 2/2, first_run 8/8 equal on
  screenshots, aria and loose DOM (crowd and restricted have no such states), PASSED in 519 s. `bin/cookie-roundtrip`: 20/20. `bin/clean-parity` and
  `bin/clean-docker` run.
- **Speed (`bench/quick`, all workloads, 3 reps, host 1.04-1.07):** post a message 1,836/s c=16 (5.56x Rails) and 909/s c=1 (5.45x); sidebar 5,850/s (9.77x) and 1,666/s c=1
  (5.81x); search 5,015 (12.3x) and 1,400 c=1 (7.27x); messages page 5,167 (13.6x) and 1,495 c=1 (6.86x); room page 5,511 c=16 and 1,649 c=1. All over the 5x floor, all 2xx, 0 errors,
  rows written equal answers. Against unit 2.5: post +1% (5.52x to 5.56x) and +2% c=1, sidebar c=1 +1.5%: no speed fix needed; post c=1 headroom is 9%.
- **KLOC:** `bin/kloc` 38,390 by upstream's rules, **38,602 corrected (38.6 KLOC)**, from 38,700 (-0.10: mostly `Request.fs`, which lost the proxy parsing).
- **Differences recorded (README, class B):** proxy headers (trusted ranges only, a public peer's `X-Forwarded-For` ignored, no `Forwarded`/`X-Forwarded-Ssl`/`X-Forwarded-Scheme`/`Client-Ip`), the
  method override (`_method` only, a non-verb `_method` is that verb), the 16 MiB body cap.


### Phase 2, checkpoint after 2.6: Opus review (2026-10-20) - GO, with notes for 2.7-2.13

Adversarial review of `063b940..40b674e` against `plans/design-kit-auth.md` and D1-D9. Evidence in `bench/results/dotnetway-p2-ck/`.
- **Re-run here:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (after the fix below): 0 warnings, 1,063 tests, 1,057 passed, 0 failed, 6 skipped (the same 6);
  H1 green. `bin/cookie-roundtrip` on the `ce87a52` image: 20/20 both ways. `bin/pixel-parity --fast --only 'users/**,search/**,welcome/**'` (a slice no
  unit had run) on the `ce87a52` image: default 44/44, custom_styles 4/4 equal on screenshots, aria and loose DOM (the other seeds have no such states),
  PASSED in 148 s. `bin/clean-parity` run. The units' own evidence (status files, pixel and cookie logs, `env.txt` image revisions) matches their entries;
  2.2 and 2.3 reused an earlier attempt's colima evidence, as their entries say.
- **Checked and holding:** the ticket is Rails' signed bytes (A1-A4, round trip); no default scheme (A7, B6, the switch set twice); the lookup in
  `OnValidatePrincipal` rejects an unknown token or a missing user without deleting the cookie (A2, A8); the before-action order is the one
  `reference-tools/campfire/callbacks.rb` dumped (Ruby includes right to left), CSRF after `deny_bots` and skipped for bot keys; `ICsrfProtection`
  compares `Origin` with the scheme and host the forwarded-headers and `assume_ssl` middleware left; one session store (`ISession`, `_campfire_flash`),
  the encrypted Rails session gone; `KnownProxies`/`KnownIPNetworks` cleared before `TRUSTED_PROXIES` is added; return-to is the request's own URL and
  the redirect keeps the kit's host checks.
- **Fixed:** `Concerns.requireAuthentication`'s challenge tried the bot key a second time after `Web.Authentication.require` had (harmless, the
  handler's result is cached per request, but dead code); it is now only the redirect, and the unused legacy `Concerns.botAuthentication` is deleted.
- **KLOC:** `bin/kloc` 38,374 by upstream's rules, **38,586 corrected (38.6 KLOC)**, from 38,602.
- **Notes for 2.7-2.13:** in the checkpoint's StructuredOutput (orchestrator) and summarized: the bot-key handler and the session resume still read the
  legacy `Ctx` (`HttpContext.Items`, `c.Request.RemoteIp`), so 2.7 must move `bot_key` to `Web.Params` and the client facts to
  `Connection.RemoteIpAddress`; the `_method` override buffers and parses every form POST (multipart included) a second time until 2.7 reads params from
  `Request.Form`; `Flash` and `LookupError` sit in or on the Kit and move in 2.13.


### Phase 2, unit 2.7: Params and formats (2026-10-20)

Code in `1d8f41c` and the R4 fix after it. Evidence in `bench/results/dotnetway-p2-7/` (image `campfire-dotnetway:app` built from `8339b4d`).
- **Built:** `Web/Params.fs`: `params[:k]` over route values, then the JSON or form body, then the query (path over body over query, the last repeated key
  wins), `lookup` (absent, string, or structured: a hash, array, number or boolean, which Rails' `String` methods fail on), `value`, `nested`, `values`, `require`,
  `requireString`, `permit`, `wrap` (`wrap_parameters`), `validate` (malformed JSON is a 400), and `load`, which `Action.run` calls before every action: a form is read
  (the framework caches it) and a JSON body is read into memory once. `Web/Formats.fs`: `requested` (`request.formats`), `respondTo`, `negotiate`,
  `isTurboFrame`, over the framework's `MediaTypeHeaderValue.TryParseList` with Rails' rules around it. Both are new code beside the kit's until 2.13.
- **Opus notes applied:** (1) `BotKeyAuthenticationHandler` reads `bot_key` through `Params.lookup`, not the legacy `Ctx` in `HttpContext.Items`; B1-B6 green, B5 (a hash
  key) still a 500. (2) The method override no longer calls `EnableBuffering` or rewinds: `Adapter.parseBody` takes a form from the framework's cached `Request.Form`
  (text pairs into the kit's nested builder, `IFormFile`s copied to temp files the request deletes), so a form is parsed once, multipart included; a JSON body read by
  `Params.load` is parsed by the kit from `HttpContext.Items`. R3 and R4 green. `Adapter.bodyLimit` now honours the kit's `MaxBodyBytes` (the Kit tests' small limits).
  (3) `Authentication.restore`, `require` and `startNewSessionFor` lost their client callback: the user agent is the header, the IP is `Connection.RemoteIpAddress`
  (IPv4-mapped unmapped). The hourly resume stays in `restore`; the change from K3 is recorded in `plans/design-kit-auth.md` ("Revisions made while building").
  (7) C13 (`Origin: null` refused on the form, `_method` delete and JSON writes) is added and passes. A5's wall-clock timestamps are not touched.
  Notes 4, 5 and 6 are for 2.8, 2.9 and 2.13.
- **Tests:** P1-P4 in `tests/Campfire.App.Tests/Web/ParamsTests.fs` (precedence on a bare `HttpContext` and a bot key in the path over a wrong one in the query end to end;
  `message[body]` from a form, a query string and JSON, and a message posted both ways; `wrap_parameters` incl. the model's attribute list, forms, and a body that has the key;
  `ParameterMissing`, a blank string, malformed JSON as 400 end to end). `FormatsTests.fs` re-points the kit's `FormatTests` golden cases at `Web.Formats`.
  Existing Kit tests that pinned Rack's form reader moved with the behaviour (raw form bytes are gone, a stray `%` in a form is literal and a 200); R4's client now retries
  a connection reset and otherwise requires the app's own 413 log line (the baseline test already failed 2 runs in 6: the host closes while the client still sends).
- **Golden cases:** every `FormatTests` case that is Rails behaviour and that the framework parser reproduces passes (browser-like lists, Turbo's type first,
  quality order, single types, `text/*` expansion, path extension and `params[:format]`, XHR, negotiation). **Recorded class B (README):** Ruby's `to_f` reading of
  odd `q` values (22 cases of `q values read like rails`), `text/xml` folding, and Rack's params parser edge cases (`ParamsTests`' type conflicts, depth limit,
  `a[]]` shapes, the 2,755 `params_vectors.json` replays): the nested-hash parser is not rebuilt on `Web.Params`; the kit's `Params.fs` keeps serving the legacy
  controllers' nested `Param` trees until they move (2.9-2.12) and goes in 2.13, and those Kit tests go with it.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,080 tests, 1,074 passed, 0 failed, 6 skipped (the same 6); H1 in that run and green.
  `bin/pixel-parity --fast --only 'errors/**,auth/**,interactions/**'`: default 115/115, custom_styles 2/2, first_run 8/8 equal on screenshots, aria and loose DOM
  (crowd and restricted have none), PASSED in 503 s. `bin/cookie-roundtrip`: 20/20. `bin/clean-parity` and `bin/clean-docker` run.
- **Speed (`bench/quick`, 3 reps, host 1.07-1.08):** post a message 1,774/s c=16 (**5.37x** Rails) and 852/s c=1 (**5.11x**); sidebar 5,078 (8.48x) and 1,585 c=1 (5.53x);
  search 4,767 (11.7x) and 1,298 c=1 (6.73x); messages page 4,798 (12.6x) and 1,407 c=1 (6.46x); room page 5,206 c=16 and 1,575 c=1. All over the 5x floor, all 2xx, 0 errors,
  rows written equal answers. Against unit 2.6 every workload is 3-13% lower, GET pages included, which this unit does not touch; the same-day control (the F# port's image,
  `control/`, sidebar and post only) ran 5% (c=16) and 12% (c=1) under its stored medians on the sidebar, so the host explains the GET numbers. The port's post rows in the
  control swung to 47% under stored (warm-up did not settle), so they say nothing about post. **Post at c=1 has 2% headroom (5.11x), c=16 7%:** the next cost (compression in 2.8)
  must come with a same-day control for post, and a shortfall is fixed by skipping work, not by moving the floor.
- **KLOC:** `bin/kloc` 38,764 by upstream's rules, **38,983 corrected (39.0 KLOC)**, from 38,586 (+0.40: `Params.fs`, `Formats.fs` and the form copy in `Adapter.fs`, beside the kit's own params, which go in 2.13).
- **Differences recorded (README, class B):** params from the framework's form reader and the `Accept` parser (above).

### Phase 2, unit 2.8: Responses and compression (2026-10-20)

Code in `55b4e67` (Respond, compression, deletions) and the page-compression commit after it. Evidence in `bench/results/dotnetway-p2-8/` (image `campfire-dotnetway:app`
built from the second commit; `first/` is the bench of the first commit, kept because it is the shortfall).
- **Built:** `Web/Respond.fs`: `redirectTo`/`redirectToWith`/`redirectBackOrTo` (Rails' unsafe-redirect rules: another host, a protocol-relative URL or a path-relative one is
  `UnsafeRedirect`, a 500; `AllowOtherHost`, `Notice`, `Alert`, status), `freshWhen`/`stale` (typed `If-None-Match` / `If-Modified-Since`, strict freshness, the
  validator + Turbo Frame + template + flash digest, weak/strong ETag, `Cache-Control` `max-age=0, private|public, must-revalidate`), `expiresIn`, `sendFile` (`Results.File`
  with range processing, Rails' `Content-Disposition`), and the request's flash (`flash`, committed with the session by `commit`, which `Action.run` calls before the
  handler writes). `Web/PublicExceptions.fs`: the public pages straight to the response (Opus note 4's `Errors.respond` through `Adapter.dispatch` and a whole `Ctx` is gone).
  Compression is `UseResponseCompression` in `Services.pipeline` behind the public files (gzip and Brotli at their fastest, HTTPS included, MIME list plus turbo-stream,
  `text/javascript`, SVG and the manifest), with a `SkipSmall` decorator of the framework's provider: nothing under 256 bytes (Opus note 6).
  **Deleted:** `Adapter.useDeflater`, the writer's gzip path, `Rack::Deflater`'s selection/406/`shouldDeflate`, the gzip member cache and `GzipWriter` (`Deflater.fs` keeps `hasWord` and
  the CRC-32 that Front shares), `BodyDigest` on `Response`, and Splice's gzip pieces (`Crc`, `Piece`, dictionary compression, the text-piece cache; it keeps the part digests and the
  ETag). Kit tests that pinned them went: `DeflaterTests` (selection, `q` values, round trips), eight `SpliceTests`, six `AdapterTests` (the deflater's, with the static-file mtime and the 406).
- **The shortfall and what fixed it:** the first bench of plain `UseResponseCompression` put the **messages page at 4.47x Rails at c=1 (floor 5x)** and the room page 35-40% down
  (room c=1 990/s from 1,575; c=16 3,633 from 5,206; `first/bench/`). The same day's identity run of that build (`first/identity/`, `--gzip 0`) was still 24% down on the room page at c=1
  (1,195/s): even with nothing to compress, the middleware's body wrapper copies a 416 KB page through a `StreamPipeWriter` instead of Kestrel's pipe, and with gzip the page is compressed afresh
  each request (decoded 416,122 bytes, 36,812 on the wire). Not a change of floor: skipped work. `Adapter.write` now asks the framework's `IResponseCompressionProvider` for the encoding,
  compresses a `Body.Parts` page once with that provider's own stream, keeps the bytes in a 16 MiB `Generations` map by (encoding, the page's ETag digest) and writes them already
  encoded (`Content-Encoding`, `Vary`, `Content-Length`), so the middleware leaves them alone. Sidebar, search and post bodies are small enough that the middleware's own work is within noise.
- **Tests:** X1-X5 in `tests/Campfire.App.Tests/Web/RespondTests.fs` (redirect matrix incl. the flash notice across a redirect; 304 for a matching ETag, `*`, a list, `If-Modified-Since`;
  the strict-freshness case; `expires_in`'s `Cache-Control` and `Date`; gzip, Brotli, `gzip, br`, `identity`, a turbo-stream write, `Vary`, one `Content-Encoding`, small bodies and a
  redirect uncompressed, a room page's second request the same bytes; `send_file` 200/206/416, `Content-Disposition`, a missing file's 500). `RoomsTests` "pages of messages go out in
  parts" now expects the gzipped page's `Content-Length`. Opus note 5 (F8: no session store reads or writes without `_campfire_flash`) stays green; the rate limiter is 2.9.
- **Gates (second commit's image):** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,064 tests, 1,058 passed, 0 failed, 6 skipped (the same 6; 16 fewer than 2.7's
  1,080: the deleted deflater/splice/adapter tests, plus 5 new X tests); H1 green. `bin/pixel-parity --fast --only 'errors/**,auth/**,interactions/**'` on the final image: default 115/115,
  custom_styles 2/2, first_run 8/8 equal on screenshots, aria and loose DOM (crowd and restricted have none), PASSED in 508 s (`pixel.log`; `pixel-before-cache.log` is the same slice, also 115/115, on the image
  of the first commit). `bin/cookie-roundtrip`: 20/20 (`cookie-roundtrip.log`). `bin/clean-parity` and `bin/clean-docker` run. Network layer Network layer on the real image through Front (`curl` against `candidate up`): `/session/new` with `Accept-Encoding: gzip` -> one
  `Content-Encoding: gzip`, `br` -> `br`, `gzip, br` -> `br`, `identity` -> none; `Vary: Accept-Encoding` on all; a precompressed asset -> one `Content-Encoding: gzip`, no re-encoding.
  A first full pixel run failed only `auth/sign_in/error` ("candidate not ready: 7 in flight") while two parity containers of an earlier session (`campfire-candidate-4312`,
  `campfire-reference-4311`, up 5 hours) were still running in the 6 GB VM; with them removed that state alone passed 2/2 and the whole slice passed 115/115.
- **Speed (`bench/quick`, 3 reps, host 1.06-1.09, second commit):** room page 5,492/s c=16 (23.0x Rails), 1,654 c=1 (15.4x); messages page 5,160 (13.6x), 1,495 c=1 (**6.86x**);
  sidebar 5,544 (9.26x), 1,564 c=1 (**5.46x**); search 4,802 (11.8x), 1,361 c=1 (7.06x); post a message 1,832 c=16 (**5.55x**), 913 c=1 (**5.47x**). Every workload over the 5x floor, all 2xx,
  0 errors, rows written equal answers. Against 2.7 (this morning's `dotnetway-p2-7`) post c=1 is +7% (852 -> 913) and nothing is below it: no regression from sessions, CSRF, auth or compression
  on the five workloads. Same-day control (the F# port's image, `control/`, sidebar and post, 1 rep): sidebar 7,204 c=1 (90% of its stored median) and 36,480 c=16 (97%); post c=1 2,895 (95%); post
  c=16 5,348 (60%, warm-up reached its maximum, as in 2.7), so today's host sits 3-10% under the stored session and the ratios above are conservative. The thinnest are sidebar c=1 (9% headroom)
  and post c=1/c=16 (9-11%).
- **KLOC:** `bin/kloc` 38,548 by upstream's rules, **38,767 corrected (38.8 KLOC)**, from 38,983 (-0.22: the deflater, the gzip cache and Splice's pieces and CRC out, `Respond.fs` and
  `PublicExceptions.fs` in, and the page-compression map).
- **Differences recorded (README, class B):** compression types/size/Brotli and no 406 (`Rack::Deflater`); `send_file` answers ranges; `fresh_when`'s ETag text is not Rails'. No
  `Rack::ETag` body hash from Web actions (K14), and the default `Cache-Control` of a 200 that set none stays with the legacy writer: **2.13 must move it** (an `OnStarting` hook) when `Ctx.Finish` goes.
- **For later units:** note 4's `Flash`/`ReturnTo`/`SessionReset` in `Kit/Flash.fs`, `Current.LookupError` as a `Campfire.Kit.Error` and `CookieJar.deleteCookieHeader` are 2.13's
  (Kit's `Ctx` is still there). A5's wall-clock timestamps are untouched. Opus note 1's `Origin: null` test (C13) was 2.7's.

### Phase 2, unit 2.9: Controllers A (2026-10-20)

Code in `1b97abc` (the controllers and the Web pieces) and the commit after it (what the replay found). Evidence in `bench/results/dotnetway-p2-9/` (`replay.log`,
`cookie-roundtrip.log`, `pixel.log`, `verify.log`, `bench/`, `control/`; image `campfire-dotnetway:app` built from the second commit).
- **Built:** sessions (`new`, `create`, `destroy`), first runs, welcome, PWA (manifest, service worker), session transfers, `Rails::HealthController`, Turbo Native
  navigation and the Action Mailbox ingress and conductor rows are Web actions (`HttpContext -> Task<Result<HttpHandler, Error>>`, `taskResult`), 29 route rows. The pieces they needed
  and earlier units had left behind `Legacy.wrap`: `Web/Before.fs` (the seven-step chain on `HttpContext`, same order and skips as `Concerns.beforeActions`; its record is
  `Before` with an `Auth` field because `Authentication` is a module here), `Web/Pages.fs` (the layout's data, `ViewContext`, the application-layout and Turbo-Frame writers over the
  kit's `Adapter.write`, the incompatible-browser page), `Web/Data.fs` (reads and writes on the request's `CampfireDb`), `Web/Native.fs` (what `Ctx.Finish` gave a response, less the
  digest: Rails' default headers, a default `Content-Type` and `Cache-Control`, as the response starts), `Controllers/Rails.fs` (health, Turbo Native, mailbox), and
  `Respond.head`/`urlFor`(host_with_port rules)/`resetSession`. The route table has `getN`/`postN`/... for Web rows; they set the matched endpoint and scope the fragment cache as `make` does.
  **K15:** `Web/LoginThrottle.fs`, a `PartitionedRateLimiter<string>` singleton (fixed window per remote IP, 10 per 3 minutes), acquired in `Sessions.create` after the chain; the
  `Sessions.increment` counter and its test are gone. Opus note 5: it never touches the session store, and F8 stays green.
  `FirstRuns.create`'s avatar comes from `Request.Form.Files` (copied to a temp file, deleted after staging), since the kit's param map is not on a Web action.
- **Found by the replay and fixed (not specific to a controller):** (1) a response the session middleware sets its cookie on got `Cache-Control: no-cache,no-store`, `Pragma` and `Expires` over the
  action's (unit 2.5's session; no replay had run since); the kit's first-registered `OnStarting` callback runs last and puts the status's default back (also fixes the legacy rows).
  (2) `PublicExceptions` (unit 2.8) added `Cache-Control: no-cache` to an error page, which Rails' middleware does not send, and left the chain's `X-Version`/`X-Rev` on it. Both gone; N9.
  `replay.py` now masks the `_campfire_flash` cookie (K6), accepts a missing ETag where Rails sends one (K14) and ignores `Accept-Encoding` in `Vary` (K11, class A: the framework adds it
  to a compressible response whatever the request asked; recorded in the README).
- **Tests:** N1-N9 in `tests/Campfire.App.Tests/Web/ControllersATests.fs` (default and version headers, cache control and no digest; redirect and `head`; health HTML/JSON/406; Turbo Native;
  conductor CSRF order; PWA; root redirects and `last_room`; the per-IP throttle; error pages). Sabotage: a limit of 11 and a changed default `Cache-Control` made N1, N8, F7 and
  `PresenterAccountsTests` "rejects bad passwords and rate limits sign ins" fail.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,072 tests, 1,066 passed, 0 failed, 6 skipped (the same 6); H1 green. A first run failed `Kit FrontAcmeTests
  cached certificates load and are forgotten by obtaining` once and passed on the immediate re-run with no change in Kit's Front (a timing flake, as the earlier `FrontTests` one). `bin/pixel-parity --fast --only
  'auth/**,pwa/**,welcome/**'`: default 54/54, custom_styles 2/2, first_run 8/8 equal on screenshots, aria and loose DOM (crowd and restricted have no such states), PASSED in 254 s (`pwa/**` and
  `welcome/**` match no state: the PWA files and the welcome page are not captured pages; their answers are in `replay.log` and N6/N7). `controllers_a/replay.py`, three seeds on the final image:
  default 152 matched, 3 differed by design, 0 differed; crowd 15/0/0; first_run 11/0/0 (the first run, on the first commit's image: 138 matched, 14 differed, all the items above). `bin/cookie-roundtrip`:
  20/20 both ways. `bin/clean-docker` and `bin/clean-parity` run.
- **Speed (`bench/quick`, 3 reps, host 1.05-1.08):** room page 5,413/s c=16 (22.6x Rails), 1,647 c=1 (15.4x); messages page 5,099 (13.4x), 1,489 c=1 (6.83x); sidebar 5,469 (9.14x), 1,556 c=1
  (**5.43x**); search 4,917 (12.1x), 1,392 c=1 (7.22x); post a message 1,854 c=16 (**5.62x**), 916 c=1 (**5.49x**). Every workload over the 5x floor, all 2xx, 0 errors, rows written equal answers.
  Against 2.8 (post 5.55x/5.47x, sidebar c=1 5.46x) nothing moved beyond noise: the hot paths are still legacy rows, and the unit adds to them only the kit's cache-header check at response start.
  Same-day control (the port's image, sidebar and post, 1 rep): 97-99% of its stored medians (sidebar 37,363 and 7,792; post 8,805 and 3,034), so the ratios are not flattered by the host.
  The thinnest are sidebar c=1 (8.6% headroom), post c=1 (9.8%) and post c=16 (12%).
- **KLOC:** `bin/kloc` 39,030 by upstream's rules, **39,249 corrected (39.2 KLOC)**, from 38,767 (+0.48): the Web pieces (+~0.75) and the native controllers are in while the `Ctx`-based `Layout`,
  `Page`, `Concerns` and the legacy rows stay until their units move (2.13 deletes them); the controllers themselves shrank slightly.
- **Differences recorded (README, class B):** the sign-in throttle's wall-clock windows in server memory (K15), and the headers of a Web action's response (default `Cache-Control`, no body ETag).
  Class A, in the 2.8 compression entry's spirit: `Vary: Accept-Encoding` on a compressible response to a client that sent no `Accept-Encoding`.
- **For later units:** `Concerns`, `Presenters/ViewContext.fs`'s `Layout` module and `Page.fs` are now duplicated by `Web/Before.fs` and `Web/Pages.fs` until 2.10-2.12 move their callers; each unit
  that moves a controller should delete the `Ctx` helper it stops using (2.13 deletes what is left). `Web.Native.wrap` must stay the way a Web row is finished until `Ctx.Finish` goes, when the legacy default
  `Cache-Control` and the version headers it set move into the same hook. Opus note 4 (`Flash`/`ReturnTo`/`SessionReset` in `Kit/Flash.fs`, `Current.LookupError` as a Kit error,
  `CookieJar.deleteCookieHeader`) is still 2.13's. A5's wall-clock timestamps are untouched.

### Phase 2, unit 2.10: Controllers B (2026-10-20)

Code in `d0fae99` (the controllers and the Web pieces) and `47aac9a` (what the replay found), then the dead-code and docs commit. Evidence in `bench/results/dotnetway-p2-10/` (`replay.log`,
`cookie-roundtrip.log`, `pixel.log`, `verify.log`, `bench/`, `control/`; image `campfire-dotnetway:app` built from `47aac9a`).
- **Built:** accounts (`edit`, `update`), `accounts/users`, `accounts/bots` and bot keys, join codes, custom styles, logos, `users` (join `new`/`create`, `show`) and `users/*` (avatars, bans, profiles, sidebars,
  push subscriptions, test notifications) are Web actions: 37 route rows off `Legacy.wrap`'s sibling `make`. Pieces added: `Params.hashMembers` (`permit(settings: {})` from the form, query or JSON),
  `Presenters.Uploads` (the framework's `Request.Form.Files` as an avatar or logo `Assignment`, the temp copy deleted by `Response.OnCompleted`; first runs use it too, so the try/finally there is gone),
  `Before.ensureCanAdminister`, `Pages.turboStream` and `applyPageHeaders` (the paginated JSON headers), `Data.passwordDigest`, and `Native.useLiveResponse` (avatars and logos are `ActionController::Live`:
  no default headers, default `Cache-Control` `no-cache`; the old `AppTests` and `RequestsTests` R5 caught this). `Params` counts an uploaded file's name, so a form with only `user[avatar]` still has `params[:user]`.
  Dead code deleted: `Concerns.passwordDigest`/`authenticateBy`/`startNewSessionFor`/`terminateCurrentSession`/`postAuthenticatingUrl`/`ensureCanAdminister`, `Accounts.paramToS`/`stringAttribute` (-0.1 KLOC).
- **Found by the replay and fixed:** `Respond.sendFile` passed a relative path to `Results.File`, which reads it under the web root; the container's storage directory is relative (`storage/files/..`), so any
  uploaded avatar answered 500 in the image while every macOS test (absolute temp paths) passed. The path is absolute now; X5 covers a relative path (sabotage: without the fix X5 fails).
- **Tests:** J1-J10 in `tests/Campfire.App.Tests/Web/ControllersBTests.fs` (administrator guard and 403 bodies; account form with the settings hash, form and JSON; the Turbo Stream people list and its 406; role
  change, removal, 400/404 cases; bots end to end including the NOT NULL 500 and a human id; the join flow, wrong code, signed-in redirect, taken email; avatars: signed path, weak ETag, 304, forged token, WebP
  variant; profile and sidebar; push subscription params, wrapped JSON, 400 on `{}`, foreign subscription 404/untouched; the logo's public caching and 304). `AttachmentTests` (avatar and logo uploads and removals) run
  through the Web actions unchanged.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,082 tests, 1,076 passed, 0 failed, 6 skipped (the same 6); H1 green. `bin/pixel-parity --fast --only
  'account/**,users/**,autocompletable/**'`: default 46/46, crowd 4/4, custom_styles 8/8, restricted 2/2 equal on screenshots, aria and loose DOM (first_run has no such states), PASSED in 201 s.
  `controllers_a/replay.py`, three seeds on the final image: default 152 matched, 3 differed by design, 0 differed; crowd 15/0/0; first_run 11/0/0 (the first run, on `d0fae99`'s image: 149/3/3, the avatar 500 above).
  `bin/cookie-roundtrip`: 20/20 both ways. `bin/clean-docker` and `bin/clean-parity` run.
- **Speed (`bench/quick`, 3 reps, host 1.05-1.07):** room page 5,387/s c=16 (22.5x Rails), 1,642 c=1 (15.3x); messages page 5,107 (13.4x), 1,491 c=1 (6.84x); sidebar 5,579 (9.32x), 1,588 c=1
  (**5.54x**); search 4,920 (12.1x), 1,401 c=1 (7.27x); post a message 1,838 c=16 (**5.57x**), 905 c=1 (**5.42x**). Every workload over the 5x floor, all 2xx, 0 errors, rows written equal answers.
  Against 2.9 (post 5.62x/5.49x, sidebar c=1 5.43x) nothing moved beyond noise: the hot paths are still legacy rows. Same-day control (the port's image, sidebar and post, 1 rep): 94.5-99.2% of its
  stored medians (sidebar 37,247 and 7,559; post 8,667 and 2,897), so the ratios are not flattered by the host. The thinnest are post c=1 (8.4% headroom), sidebar c=1 (10.8%) and post c=16 (11%).
- **KLOC:** `bin/kloc` 39,077 by upstream's rules, **39,296 corrected (39.3 KLOC)**, from 39,030 / 39,249 (+0.05): the new Web pieces and `Uploads` against the shrunk controllers and the deleted `Ctx` helpers.
  The `Ctx`-based `Layout`, `Page`, `Concerns` and the rooms/messages/search/autocompletable/QR/unfurl rows stay until 2.11-2.12 move them; 2.13 deletes what is left.
- **Differences recorded (README, class B):** a JSON `null` param is read as a missing key (Rails keeps nil); no page can send it. The Live controllers' headers (already Rails' behaviour, now stated).
- **For later units:** 2.11 moves autocompletable (the only `ApplyHeaders` and `Layout.load` caller left besides `Page`), QR code, unfurl links, searches and the Active Storage endpoints; `Page.framedPage` and
  `Layout` can then go with the rooms/messages move in 2.12. `Native.useLiveResponse` is how a Live controller (Active Storage streaming in 2.11) is marked.


### Phase 2, unit 2.11: Controllers C (2026-10-20)

Code in `af92395` (the controllers and the Web pieces) and `14c1a93` (the Live proxies), then the docs and evidence commit. Evidence in `bench/results/dotnetway-p2-11/` (`verify.log`, `pixel.log`, `replay.log`,
`bench/`, `control/`; image `campfire-dotnetway:app` built from `af92395`, so the pixel, replay and bench runs predate `14c1a93`, which changes only the headers of the two proxy routes).
- **Built:** autocompletable users, the QR code, unfurl links, searches (`index`, `create`, `clear`) and the Active Storage controllers (blob and representation redirect and proxy, disk `show` and `update`, direct
  uploads) are Web actions: 15 route rows off `Legacy.wrap`'s sibling `make`. `Controllers/ActiveStorage.fs` is the new `ActiveStorageControllers`; `ActiveStorage.fs` keeps the media work, `partsBody`/`partsLen` and the purge.
  `require_active_storage_authentication` is `Authentication.session` (found, not resumed: test K5 checks `sessions` is not written) or a before-action `head 401`; the blob endpoints keep their CSRF check, the disk `PUT` has none (`skip_forgery_protection`).
  New pieces: `Data.present`, `Params.nestedText` (a JSON number as text) and `Params.hashJson` (a hash member as a `Value`), and a 400 in `Params.load` for a query whose percent-escapes are not UTF-8 (Rails'
  `check_param_encoding`; the first `bin/verify` run found the loss: `AppTests` "proxied blobs write odd dispositions" expects 400 for `?disposition=%FF` and got 200, since the framework's parser passes U+FFFD).
- **Found and fixed on the way:** a first version sent the proxied file with `Results.File`, which refuses a `Content-Disposition` with a line break (`?disposition=x%0Ay` was a 500); the proxied file goes through the kit response writer
  again, as before. Set-blob's bad signature and the missing session are before-action `head`s (`text/html`, no format from the `.jpg` of the path): a `Respond.head` there answered `image/jpeg`. The two proxy controllers include
  `ActiveStorage::Streaming` (`ActionController::Live`); Rails answers them without `default_headers` (verified in the reference gem), which the legacy rows never did, so they are marked `Native.useLiveResponse` now.
- **Tests:** K1-K6 in `tests/Campfire.App.Tests/Web/ControllersCTests.fs` (mention list: sign-in redirect, HTML and JSON, `X-Total-Count`, blank/unknown/structured `room_id`, 406; QR code: cache headers, bad base64 500, too much 422;
  searches: record/show/clear, hash `q` 500, missing `q` 500; unfurl: missing/blank/hash `url`, cross-site 422; Active Storage writes: 401, cross-site 422, bad/missing/huge `byte_size` 422/400/413, `sessions` untouched; a direct upload
  with a JSON number and a metadata hash, the PUT's 401/422/422/204, redirect, disk file and range, proxy whole/304/single range/multipart ranges/416/forged id/attachment, default headers on the redirect and none on the proxy).
  The C13 `Origin: null` case Opus note 7 asked for was already in `CsrfTests`. A5's wall clock is untouched.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,088 tests, 1,082 passed, 0 failed, 6 skipped (the same 6); H1 green. `bin/pixel-parity --fast --only
  'search/**,interactions/composer/attachment*,interactions/lightbox'`: default 18/18, custom_styles 2/2 equal on screenshots, aria and loose DOM (crowd, first_run, restricted have no such states), PASSED in 98 s.
  `controllers_a/replay.py`: default 152 matched, 3 differed by design, 0 differed; crowd 15/0/0; first_run 11/0/0 (it covers none of this unit's rows but the avatar and logo files, and the new query check). `bin/cookie-roundtrip`
  not rerun: the unit touches no cookie. `bin/clean-docker` and `bin/clean-parity` run.
- **Speed (`bench/quick`, 3 reps, host 1.07-1.08):** room page 5,231/s c=16 (21.9x Rails), 1,545 c=1 (14.4x); messages page 4,947 (13.0x), 1,426 c=1 (6.54x); sidebar 5,358 (8.95x), 1,528 c=1 (**5.33x**); search 4,757 (11.7x), 1,351 c=1
  (7.01x; it is this unit's native action); post a message 1,734 c=16 (**5.25x**), 875 c=1 (**5.24x**). Every workload over the 5x floor, all 2xx, 0 errors, rows written equal answers. Against 2.10 everything is 3-5% lower, post and
  sidebar included, though their rows are still legacy: the same-day control (the port's image, 1 rep: sidebar 36,010 and 7,192, post c=1 2,802) ran at 95-97% of its stored medians (2.10's control ran at 94.5-99.2%), so the host is
  slower today and the ratios are not worse by code. Thinnest: post c=1 (4.8% headroom raw; about 8% adjusted by the control), post c=16 (5.0%), sidebar c=1 (6.6%). 2.12 moves those rows to Web actions and has to find the headroom.
- **KLOC:** `bin/kloc` 39,158 by upstream's rules, **39,377 corrected (39.4 KLOC)**, from 39,077 / 39,296 (+0.08): `Controllers/ActiveStorage.fs` and the Params/Data pieces against the shrunk controllers.
  The `Ctx`-based `Layout`, `Page`, `Concerns` stay for the rooms and messages rows until 2.12; 2.13 deletes what is left.
- **Differences recorded (README, class B):** the mention list reads a hash or array `filter`/`query` as an empty string.
- **For later units:** `Data.present` replaces `MessagesController.present` for 2.12's rows (the messages controllers still call the `Ctx` one). `Page.framedPage` and `Layout.load` have no caller left but the rooms and messages
  rows. `Native.useLiveResponse` is done for Active Storage; the `Ctx.UseLiveResponse` it replaces goes in 2.13.

### Phase 2, unit 2.12: Controllers D (hot paths) (2026-10-20)

Code in `eaeba8e` (the controllers and the Web pieces) and the fix commit after it (a halting before-action commits its flash), then the docs and evidence commit. Evidence in `bench/results/dotnetway-p2-12/` (`verify.log`,
`pixel.log`, `realtime.log`, `replay.log`, `bench/`, `control/`; image `campfire-dotnetway:app` built from the fix commit, which is the code of the unit).
- **Built:** rooms and `rooms/*` (opens, closeds, directs, involvements, refreshes), messages (`/rooms/:id/messages` and `/messages`), boosts, boosts by bots and messages by bots are Web actions: 55 route rows off `Legacy.wrap`'s sibling `make`;
  what is left there are the 41 `actionNotFound`/`missingController` fallback rows. New: `Before.setRoom`, `Pages.content`/`contentInApplicationLayout`/`bare`/`rememberLastRoomVisited`/`rendererBaseUrl`, `Respond.headWithLocation`,
  `Uploads.assignmentNamed`, `Params.rawBody` (kept by `Services.keepRawBody` for bot API paths only, ahead of the method override, so a `curl -d 'Hello'` form post keeps its raw body and no other route buffers), and `Data.read`/`write` map a missing record to
  `Web.NotFound`. The Ctx-free staging helpers are `MessageStaging` (jobs call them). Dead legacy code deleted: `Concerns.setRoom`/`rememberLastRoomVisited`/`lastRoomVisited`, `Page.framedPage`/`content`/`contentInApplicationLayout`/`bare`/`rendererBaseUrl`.
- **Found and fixed on the way:** (1) the first `bin/pixel-parity` found `rooms/show/non_member` different (2 cells): `set_room` redirects with an alert, and a `Halt` answered through `PublicExceptions.respond` never committed the flash.
  `respond` now commits for `Halt`; test D2 follows the redirect and reads the alert. (2) `AppTests`/`RoomsTests` expected a body ETag on a room page; K14 says pages carry none, so the two tests now assert none on the page and a weak one on `messages#index` (`fresh_when`).
  (3) `Params.lookup` does not see a top-level multipart file by name (the bot API's `attachment`), so `Uploads.assignmentNamed` takes the file name apart from the value.
- **Tests:** D1-D5 in `tests/Campfire.App.Tests/Web/ControllersDTests.fs` (a bot's form and JSON raw bodies, blank and empty bodies 422; `set_room`'s alert and the scope of opens; `last_room` set once; refresh `since`; involvement blank, name, bad name and hash).
  The existing `MessagesTests`, `RoomsTests` and the bot API test cover the rest.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 1,093 tests, 1,087 passed, 0 failed, 6 skipped (the same 6); H1 green. `bin/pixel-parity --fast --only 'rooms/**,messages/**'`: default 134/134, crowd 8/8,
  custom_styles 4/4, restricted 2/2 equal on screenshots, aria and loose DOM (first_run has none), PASSED. `realtime/**` one state at a time (13 states, `--seeds default`): 2/2 cells equal each, all 13 pass.
  `controllers_a/replay.py`: default 152 matched, 3 differed by design, 0 differed; crowd 15/0/0; first_run 11/0/0. `bin/cookie-roundtrip` not rerun: the unit touches no cookie but `last_room`, which Rails reads as before.
  `bin/clean-docker` and `bin/clean-parity` run.
- **Speed (`bench/quick`, 3 reps, host 1.08-1.09):** room page 5,210/s c=16 (21.8x Rails), 1,553 c=1 (14.5x); messages page 4,872 (12.8x), 1,412 c=1 (6.48x); sidebar 5,234 (8.74x), 1,493 c=1 (**5.21x**); search 4,784 (11.7x), 1,332 c=1 (6.91x);
  post a message 1,820 c=16 (**5.51x**), 897 c=1 (**5.38x**). Every workload over the 5x floor, all 2xx, 0 errors, rows written equal answers. Post a message gained against 2.11 (5.25x/5.24x), the sidebar c=1 lost a little (5.33x to 5.21x, 4.2% headroom).
  The same-day control (the port's image, 1 rep) ran at 88-93% of its stored medians for sidebar and post c=1 (0.879x, 0.930x), so the host is slower today than in 2.11 (0.95-0.97); adjusted by the control the sidebar c=1 is about 5.9x. Thinnest now: sidebar c=1 (4.2% raw headroom), then post c=1 (7.5%); the sidebar c=16 warm-up did not settle.
- **KLOC:** `bin/kloc` 39,204 by upstream's rules, **39,423 corrected (39.4 KLOC)**, from 39,158 / 39,377 (+0.05): the new Web pieces and tests against the shrunk controllers; 2.13 deletes the `Ctx` side.
- **Differences recorded (README, class B):** a bot API request keeps its raw body for bot API paths only; a JSON `null` attachment is "not given"; a non-empty hash/array `involvement` is a 500.
- **For later units (2.13):** Opus note 3 (remote IP) is already true: nothing in the app calls the kit's `RemoteIp()`. Notes 4 and 5 remain for 2.13 and the limiter (`Flash`/`ReturnTo`/`SessionReset` and `Current.LookupError` in Kit,
  `Errors.respond` building a `Ctx`). The Ctx-based `Layout`, `Page` (`renderDetached*`, `Rendered`), `Concerns` (the before-chain the unit tests still call) are what is left of the legacy side besides the fallback rows.

### Phase 2, unit 2.13: Delete (2026-10-20)

Code in `3a7e9cb` (the deletion and the rehoming) and `09bf227` (HEAD-from-POST, the moved tests, README and design note). Evidence in `bench/results/dotnetway-p2-13/` (`verify.log`, `pixel.log`, `realtime.log`, `replay.log`,
`cookie-roundtrip.log`, `bench/`, `control/`). The pixel, realtime and replay runs used the image built from `3a7e9cb` (`build.log`); `09bf227` adds one HEAD rule and tests, so `bench/` and `control/` ran on an image built from it
(`build-final.log`).
- **Deleted:** `Web/Legacy.fs`, `Concerns.fs`, the `Ctx`-based `Layout`/`CtxApp`/`Current` slots, and in `Campfire.Kit`: `Ctx`, `Actions`, `Adapter`, `Params`, `Body`, `Multipart`, `Cookies`, `Format`, `Request`, `Response`, `Error`,
  `Exceptions`, `KitClock`, `App`, `Generations`, `Deflater`, `Splice`, `Flash`. `grep -rnw Ctx src` is empty. Every route row is a Web action (the 41 fallback rows too). `Campfire.Kit` is `Front/` and `Server.fs`, with no
  reference to RailsCompat, Ruby or Falco. Tests retired with their code: `CtxTests`, `AdapterTests`, `BodyTests`, `ParamsTests`, `ParamsVectorTests`, `CookiesTests`, `SessionTests`, `FormatTests`, `ClockTests`, `RequestTests`,
  `ResponseTests`, `DeflaterTests`, `SpliceTests`, `HttpTests`/`HttpApp`, `LoggingTests`, `PerfTests`, `RailsVectorTests`, `DifferentialTests` and `Harness`/`Helpers`; also `Views.Tests/KitIntegrationTests`, `bin/kit-differential`,
  `reference-tools/kit` and `bench/kit` (a bench of the old kit's `Ctx`).
- **Rehomed (not rewritten), in `Campfire.App/Web`:** `Errors.fs` (one error type; `AppState.ReadEf`/`WriteEf`, the staging helpers and `IntegrationJobs` use it; `Errors.status`/`display`), `Flash.fs` (`Flash`, `ReturnTo`,
  `SessionReset`: Opus note 4), `Edge.fs` (`EdgeConfig` replaces `Kit`/`KitConfig`; forwarded headers, `assume_ssl`, request id, `X-Runtime`, HSTS, method override, body cap), `Outgoing.fs` (Puma's header writing and the
  file/range/stream body, for Active Storage and the public files), `PageParts.fs` (`Splice` and `Generations`, for the compressed page cache), and `CacheControl`/`ExpiresIn`/`SendOptions` in `Respond.fs`. `Pages.send` writes a rendered
  page. `Front.configureKestrel`, `FrontCache.hasWord` and `FrontCrc` took what Front used from the kit. The sign-out cookie is a literal in `Authentication.fs` (bytes unchanged); the Cable connection reads `session_token`
  with the framework's cookie parser. Decisions and why `Splice`/`Generations` stayed: `plans/design-kit-auth.md`, unit 2.13.
- **Tests:** the Kit suite is Front's, Server's and the request log's (65 passed, 1 skipped); Splice and Generations tests are `Web/PagePartsTests.fs`; the CRC and word tests are Front's; R3b (a POST that `_method=head` turned into a
  HEAD announces no length), R7 (request heads as large as Puma and hyper accept, 431 and 414 past them) and R8 (Kestrel's three request-line rules) replace the `AdapterTests` they came from. The AppTests that drove the old chain
  (`whoami`, the platform check) run the Web chain through `Services.pipeline`. C13 (`Origin: null`) was already in `CsrfTests`.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 0 warnings, 969 tests, 963 passed, 0 failed, 6 skipped (the same environmental 6); H1 green. `bin/pixel-parity --fast` on every state: PASSED in 1,449 s, default
  361/361, crowd 12/12, custom_styles 16/16, first_run 8/8, restricted 4/4 equal on screenshots, aria and loose DOM. `realtime/**` one state at a time (13 states): all pass. `controllers_a/replay.py`: default 152 matched, 3 differed
  by design, 0 differed; crowd 15/0/0; first_run 11/0/0. `bin/cookie-roundtrip`: 20/20 (the Cable cookie parse and the sign-out cookie changed). `bin/clean-docker` and `bin/clean-parity` run; disk 21-29 GB free during the bench.
- **Speed (`bench/quick`, 3 reps, host 1.04-1.06):** room page 5,545/s c=16 (23.2x Rails), 1,706 c=1 (15.9x); messages page 5,243 (13.8x), 1,538 c=1 (7.06x); sidebar 5,712 (9.54x), 1,645 c=1 (**5.74x**); search 5,082 (12.5x),
  1,455 c=1 (7.55x); post a message 1,902 c=16 (**5.76x**), 952 c=1 (**5.70x**). Every workload over the 5x floor, all 2xx, 0 errors, decoded bytes equal Rust's, rows written equal answers. Post a message and the sidebar c=1 gained
  against 2.12 (5.51x/5.38x, 5.21x). The thinnest workload now has 14% headroom. The same-day control (the port's image, 1 rep) ran at
  0.99-1.00 of its stored medians for the sidebar and post c=1, so the host is as fast as the stored session and the ratios stand unadjusted (the control's post c=16 warm-up did not settle). Opus note 6 (compression) needed no fix.
- **KLOC:** `bin/kloc` 35,064 by upstream's rules, **35,283 corrected (35.3 KLOC)**, from 39,423 (-4.1): Kit 2,838 (Front only), App 12,277. The plan projected 32.7-33.3 for the whole phase: `Edge`, `Outgoing`, `PageParts`,
  `Flash` and `Errors` are 1.0 KLOC the plan did not list as kept, and the kept `Splice`/`Generations` are 0.5 of it.
- **Differences recorded:** none new in behaviour. README's request-handling entries were rewritten for the Web layer (the kit's route-case and `Allow` entries went with `Adapter.route`; the route table is exact); the
  HEAD-from-POST rule is stated there. Not carried over: a request header with a byte outside visible ASCII is no longer treated as absent in general (the framework hands the app what it was sent; only `Host` and
  `X-Request-Id` are read through the visible-ASCII rule).
- **For Phase 3 and later:** A5 still compares timestamps with the wall clock rather than the frozen harness clock (design risk 11). `Campfire.Kit` keeps `Front/` and `Server.fs` until Phase 7. The phase gates (plan section 4): the
  matrix, `db-differential` (sessions are written; not rerun in this unit, which touches no schema or model) and the same-day-control bench were run per unit; `bin/db-differential` remains for the phase close.

### Phase 2: gate (2026-10-20) - DONE, no blocker

Evidence: `bench/results/dotnetway-p2/` (all of it on an image built fresh from `898f154`; label `org.opencontainers.image.revision` checked equal to HEAD; the tree was clean). No code changed in this unit.
- **Cookie round trip:** `bin/cookie-roundtrip` 20/20 both ways (Rails -> ours -> Rails and ours -> Rails -> ours). `rails_compat_verify_rust.rb` pointed at our output (`mint.sh` signs David in on our image, takes the `session_token` it set, puts it into the Rust vectors file as the `session` entry): 51 checks, 0 failures. The `campfire_sessions.json` vectors are test A1 in `bin/verify` (green).
- **`controllers_a/replay.py`** (restart of both servers per seed, frozen clock): default 152 matched, 3 differed by design, 0 differed; crowd 15/0/0; first_run 11/0/0 (`replay.log`).
- **Lean pixel gate** (`--fast` off; slices with `--only`, the 13 `realtime/**` states one at a time; `pixels/`): every one of the 940 cells equal on screenshots, aria and loose DOM. default 860/860, crowd 24/24, custom_styles 32/32, first_run 16/16, restricted 8/8, on each of the three layers; 25 slices, all rc 0.
- **Other gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnaserror): 969 tests, 963 passed, 0 failed, 6 skipped (the same environmental 6), H1 green. `bin/db-differential`: ok (fixtures and scenario, Rails boots on the database we wrote, rollback ok).
- **Speed (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps; same-session control = the Phase 1 image rebuilt from `063b940` as `campfire-dotnetway:p1ctl`, same 3 reps, alternating order; host probe 1.04-1.08 on 12 probes, load 2.3-3.2, 0 non-2xx, 0 connection errors, post rows equal answers):**
| workload | c=16 req/s | x Rails | c=1 req/s | x Rails | vs Phase 1, same session (c=16 / c=1) |
|---|---:|---:|---:|---:|---|
| room page | 5,498 | 22.97x | 1,689 | 15.77x | 0.83 / 0.83 |
| messages page | 5,191 | 13.64x | 1,541 | 7.07x | 0.97 / 0.98 |
| sidebar | 5,582 | 9.33x | 1,620 | 5.65x | 0.93 / 0.91 |
| search | 5,062 | 12.41x | 1,505 | 7.81x | 0.97 / 0.99 |
| post a message | 1,885 | 5.71x | 945 | 5.66x | 1.00 / 0.99 |
  All ten cells are over the 5x floor; no workload is a blocker, so no speed fix was made. Thinnest: sidebar c=1 (5.65x, 13% headroom), post c=1 (5.66x) and c=16 (5.71x). The control ran at its Phase 1 numbers (post 1,880 against 1,857; sidebar c=1 1,772 against 1,703), so the host is as quiet as then. Phase 2 cost against Phase 1: the room page lost 17% (still 23x Rails), the sidebar 7-9%, post and search nothing; recorded, not a gate.
- **KLOC:** `bin/kloc` 35,064 by upstream's rules, **35,283 corrected (35.3 KLOC)**, from 38,352 at the Phase 1 close (-3.1); App 12,277, Kit 2,838. The plan projected 31.7 for the phase: over by 3.6, as the 2.13 entry recorded (`Edge`, `Outgoing`, `PageParts`, `Flash`, `Errors`, kept `Splice`/`Generations`).
- **Differences recorded:** none new. `bin/clean-docker` and `bin/clean-parity` run; disk 16-28 GB free during the run (the control image was removed after the bench).

### Phase 2: independent verification (2026-10-20) - PHASE 2 DONE; Phase 3 GO

Opus verifier, on `cfb08ec` plus this entry's fix (`6f95368`). Checked against code, logs and re-runs, not the unit reports. Evidence: `bench/results/dotnetway-p2-verify/`.
- **Kit gone:** `grep -rn "\bCtx\b" src` and `grep -rnw Ctx src` are empty; `Campfire.Kit` compiles `Server.fs` and `Front/` only, and nothing in `src` or `tests` names a deleted Kit module.
- **Re-run here:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `cfb08ec`: 0 warnings, 969 tests, 963 passed, 0 failed, 6 skipped; after the fix: 970, 964 passed, 0 failed, the same 6 skipped (H1 green in both). `bin/cookie-roundtrip` on an image built from `6f95368` (label checked): 20/20 both ways.
- **CSRF, probed on the `898f154` image with a signed-in cookie:** every write row of the route table (91 rows; `csrf_sweep.py` in the evidence folder, run against a candidate on port 4412 with `token.txt` holding a signed-in `session_token`; its output was read in the session, not saved) with `Sec-Fetch-Site: cross-site`, `same-site`, a cross `Origin` JSON body, a cross `Origin` body-less request, and the `_method` override: every row with an action is 422; the `actionNotFound` rows, the not-configured ingresses and `disk#update` with a bad token are 404, as in Rails (their checks run after the action lookup or they skip forgery protection). Bot keys skip it (C11, B4). `POST _method=get` is a GET and skips it, as in Rack.
- **Before-action order:** `Before.chain` is the reference's (`include` right to left: version headers, banned IP, authentication, `deny_bots`, forgery, `allow_browser`, then `require_unauthenticated_access`'s steps). Checked against the reference on the same seed: an old browser signed out is redirected to sign in on both (authentication runs before `allow_browser`); a bad bot key without a cookie is the sign-in redirect on both.
- **Cookie, flash, return-to:** sign-in writes Rails' signed `session_token` (`_rails` envelope, purpose `cookie.session_token`, `exp` 20 years on the frozen clock, `path=/; samesite=lax; httponly`); a flipped digit or an unsigned value is the sign-in redirect; `return_to` comes back as the absolute URL with its query; a failed sign-in is 401 with the `flash.now` alert.
- **Security:** `redirect_to` strips CR/LF/NUL and refuses control characters, other hosts and path-relative URLs; a CRLF in the path does not reach a header; `X-Forwarded-*` is trusted from `TRUSTED_PROXIES` only (Rails reads `X-Forwarded-Host` from anyone). A `Host` header is reflected into absolute URLs as Rails does (no host authorization in either). The `_campfire_flash` cookie is data-protected (ephemeral keys), so it cannot be forged or chosen.
- **Compression once:** the room page through Front: `Content-Encoding: gzip`, `br`, `br` for `gzip, br`, none for identity; a gzip body decompresses once to HTML.
- **Fixed here (`6f95368`):** a multipart body cut short before its closing boundary was a bare, empty 500: the method override's form read threw an `IOException`, the exception handler called it a 500, and its renderer read `Request.Form` again (`Formats.requested`), threw inside the handler, and the framework gave up. Rails answers an empty 400 (Rack's `EmptyContentError`; there is no `public/400.html`). `Params.form` treats an unreadable form as empty, `Params.load` maps a refused form read to its status and an unfinished one to 400, and the exception handler maps an `IOException` on a form request to 400. Test R9 (POST through the override, PATCH through `Params.load`) fails without either half; on the rebuilt image both answer 400, `Content-Length: 0`, `text/html; charset=UTF-8`, byte for byte the reference's headers. README: the malformed-multipart difference of design section 7 was missing and is recorded; three entries still cited the Kit's deleted `HttpTests` (now the App tests that hold those cases); `CsrfTests` is C1-C13. AGENTS.md: controllers are Web actions, `Web/PageParts.fs` instead of `Splice.fs`.
- **Checked, not re-run:** the gate's pixel slices (25 logs, all PASSED, `realtime/**` one state at a time), the replay log and `db-differential.status`, and the bench table (`quick-compare.txt` and `control.md` match the gate entry; the control image is `063b940`). The fix touches error paths only, so the bench was not repeated.
- **Speed:** all ten cells over 5x Rails at the gate (thinnest sidebar c=1 5.65x, post c=1 5.66x, post c=16 5.71x); against the same-session Phase 1 control the room page is 0.83x and the sidebar 0.91-0.93x, post and search unchanged.
- **KLOC:** `bin/kloc` 35,068 by upstream's rules, **35,287 corrected (35.3 KLOC)**; the design projected about 33.3 (32.7 from the plan's Phase 1 figure) and the plan 31.7: over by 2.0 against the design, for the rehomed `Edge`, `Outgoing`, `PageParts`, `Flash` and `Errors` (unit 2.13).
- **Open, for later phases (none blocks Phase 3):** (1) `Respond.hostAllowed` takes the host after the last `@` without treating `\` as a separator, so `http://evil.com\@host/` counts as this host, where Ruby's `URI` refuses it; only `redirect_back_or_to` (Referer) could reach it and nothing calls it, but fix it before anything does. (2) The flash store is server memory capped at 64 MiB; a flood of signed-out GETs can evict real users' flash and return-to (checkpoint risk, class B). (3) A5 still uses the wall clock. (4) One App-test run during this check's sabotage runs failed six unrelated tests at once (`SearchesTests` x2, `ParamsTests` P2, `RespondTests` X4, `HotPathTests` H3 with 1,157 query compilations, and R9); the identical tree passed on the next run and four other runs passed. Not reproduced; if H3 ever counts compilations again, investigate before trusting H1.
- **Phase 3 (Cable):** GO. The session lookup Cable must share is `SessionCookie.validatePrincipal` (token, row, user); Cable reads `session_token` with the framework's cookie parser and RailsCompat's verifier today, and the `realtime/**` states pass one at a time.

### Unit H.0: Phase 2 carry-overs

- `Respond.hostAllowed` now answers false for any URL containing a backslash, as Rails' `_url_host_allowed?` does (Ruby's `URI` raises on `\`, the check rescues and answers false; browsers also read `\` as `/`). New test `X1b` drives `redirect_back_or_to` with Referers `http://evil.com\@host/`, `http://host\@evil.com/`, `/\evil.com`, `\\evil.com`, `//evil.com`, userinfo and plain other-host forms (all fall back), and same-host, userinfo-on-our-host and path forms (followed). Sabotaged (check disabled): X1b fails; restored: passes.
- A5 runs on a frozen harness clock: `Boot.bootWithClock` takes an optional `SharedClock` (`bootWith` is unchanged, env-driven), `Support.bootSeededFrozen` boots on `TestClock.FrozenAt`, and A5 asserts exact equality with 2024-06-01T12:00:00Z, with no 2-second wall-clock slack. This is the harness's own frozen clock (the same `TestClock` that `CAMPFIRE_FROZEN_TIME` builds), not the `FakeTimeProvider` package; the App's `TimeProvider` is an adapter over it, so the effect is the same without a new dependency.
- H3 (1,157 query compilations, one-off in the verifier's run): App tests run three more times, then once inside the full `bin/verify`: all green every time, H3 did not recur (host load average 5 to 10 during the runs). Still not reproduced; the standing rule holds.
- Gates: `CAMPFIRE_REQUIRE_SEED=1 bin/verify` Release, warnings as errors: 971 total, 965 passed, 6 skipped (Linux-only), 0 failed. `bin/kloc`: 35.1 KLOC (35.3 corrected). No speed-relevant path touched, so no bench run.

### Unit H.1: Hybrid reads, the four hottest lookups

- **Built:** `src/Campfire.Db/Ef/FastReads.fs` (101 lines) is the only place Dapper is called: `sessionByToken`, `userById`, `accountFirst`, `roomForUser` (the room via the membership join). `Session.findByToken`, `User.findById`, `Account.first` and `Room.findForUser` call it directly (no runtime switch). Reads run on the context's own connection opened through EF (the pragma interceptor runs) and pass `CurrentTransaction` explicitly inside writes. RoomType is read through a private row record (Dapper ignores enum TypeHandlers); the snake-case and DateTime handlers are set once, from `Data.options`. Explicit column lists, parameters only. The EF single-writer guard is untouched (reads only).
- **Guards:** `FastReadsTests` (App.Tests): every user, every (user, room) pair against the memberships, every session, the account, each against EF's answer over the default seed, plus a read inside a write sees the write's own uncommitted row. `bin/verify` greps that Dapper appears only in FastReads.fs and that no SQL there is interpolated or concatenated. Log hook: `FastReads.logTo dataSource sink`; H2's probe registers it per database, so H2's command log lists Dapper's statements (H1 and H2 pass).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` Release, warnings as errors: 973 total, 967 passed, 6 skipped, 0 failed. (The first run failed one test, WebhookTests "gives up on a trickling reply", at host load average ~10; three reruns of App.Tests and a second full verify were green.) `bin/cookie-roundtrip` 20/20 PASSED. `bin/pixel-parity --fast` auth/** and rooms/**: 100/100 pixel, aria and loose DOM equal.
- **Speed (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps, interleaved new/control/control/new/new/control; control = `h1-control` tag, `0cf9739`, rebuilt as `campfire-dotnetway:h1ctl`; host probe ratio 1.045-1.071 on 12 probes, load 1.2-1.5 before each run, 0 non-2xx, 0 connection errors, post rows equal answers):**

  | workload | c | control req/s | hybrid req/s | gain | CPU us/req control -> hybrid | x Rails |
  |---|---|---:|---:|---:|---|---:|
  | room page | 1 | 1,704 | 1,814 | +6.4% | 685 -> 652 | 16.9 |
  | room page | 16 | 5,579 | 5,958 | +6.8% | 622 -> 589 | 24.9 |
  | messages page | 1 | 1,543 | 1,593 | +3.2% | 745 -> 726 | 7.3 |
  | messages page | 16 | 5,227 | 5,340 | +2.2% | 670 -> 651 | 14.0 |
  | sidebar | 1 | 1,631 | 1,686 | +3.4% | 714 -> 693 | 5.88 |
  | sidebar | 16 | 5,712 | 5,965 | +4.4% | 610 -> 589 | 10.0 |
  | search | 1 | 1,515 | 1,573 | +3.8% | 755 -> 731 | 8.2 |
  | search | 16 | 5,101 | 5,244 | +2.8% | 694 -> 671 | 12.9 |
  | post | 1 | 954 | 973 | +2.0% | 1,839 -> 1,783 | 5.83 |
  | post | 16 | 1,901 | 1,928 | +1.4% | 1,625 -> 1,587 | 5.84 |

  All ten cells are over the 5x floor (thinnest: sidebar c=1 5.88x, up from 5.69x in the same session's control). The messages page c=16 warm-up did not settle on the hybrid image (noted by the runner).
- **Do four lookups capture most of the spike's gain? No, but the comparison is soft.** The spike's end-to-end figure (1.5-1.8x on the room and messages pages) was derived from an in-process read-set microbenchmark, never measured on this rig. Measured here, the room page gains 6-7% (about 33 us of CPU per request) and the other workloads 1-4%. The four lookups are 6 of the room page's 9 statements, so a flat 15 us a statement would have saved about 150 us; the bench shows about a fifth of that. The spike's 41 us per EF statement does not appear in the container's per-request CPU, so the remaining Dapper moves (message page reads, membership, original room, logo blob) should be expected to add little; the per-request cost sits elsewhere. Not a blocker; recorded, not a gate.
- **Lines added:** `src/` +109 / -8 (FastReads.fs 101, call sites ~8), tests +64, bin/verify +6.
- **KLOC:** `bin/kloc` 35,146 by upstream's rules, **35,365 corrected (35.4 KLOC)**, from 35.3.
- **Differences recorded:** none. Evidence: `bench/results/dotnetway-hybrid-1/` (`new/`, `ctl/`, logs, `bench.sh`, `cookie-roundtrip.log`, `pixel.log`). `bin/clean-docker` and `bin/clean-parity` run, control image and scratch worktree removed, disk 29 GB free.

### Unit H.2: Hybrid reads, the rest of the hot read sets

- **Built:** `FastReads.fs` (now about 240 lines, still the only Dapper caller) gained `roomById`, `originalRoom`, `membershipByRoomAndUser`, `messageById`, `lastPage`, `pageBefore`, `attachedBlob` (the logo; `Blob.attached` calls it), then, after the in-process measurement below, `visibleMembershipRoomRows` (the sidebar statement), `roomsOfType`, `memberUserIdsOfRoomsOfType`, `activeUsersExcluding`, `searchesOrdered` and `searchReachable`. The model call sites (`Room.findById`/`original`/`forUserOfType`/`userIdsOfDirectRoomsOf`, `Membership.findByRoomAndUser`/`visibleWithOrderedRoom`, `Message.findById`/`lastPage`/`pageBefore`/`searchReachable`, `User.activeExcluding`, `Search.orderedForUser`) call it directly. `Membership`'s involvement goes through a private row record like RoomType. Still no runtime switch, parameters only, the single-writer guard untouched.
- **Guards:** `FastReadsTests` grew: every room, the original room, every (room, user) membership, every message, `lastPage` and `pageBefore` of every message of every room, every attachment's blob, plus a second test that keeps the EF statement each sidebar/search read replaced as the oracle (every user's sidebar rows, rooms of each type, direct-room member ids, active-users-excluding over several id sets and limits, searches, reachable FTS results over four queries). `bin/verify`'s Dapper guard now also catches `.Query<`.
- **Difference found, no behavior change:** `userIdsOfDirectRoomsOf` was an unordered `SELECT *`; the Dapper version selects one column and orders by membership id, so the order differs from EF's planner order (Rails' is unspecified too). Its one caller builds a set, so the test compares sorted.
- **Sidebar and search measured in process first** (`bench/results/dotnetway-hybrid-2/measure-d-sidebar-search-in-process.txt`, code kept as `FastReadsMeasure.fs.txt`; 10,000 pages per cell, 6 rounds alternating order, default seed, load 3.9-5.1): the full read set of the page, with the shared lookups already Dapper in both arms. sidebar: EF 172 us/page (170-173) vs Dapper 143 us (143-144), 1.21x, 166 KB vs 95 KB per page; search: EF 182 us (181-182) vs Dapper 101 us (101-102), 1.8x, 166 KB vs 67 KB. Search is clearly faster; sidebar is 30 us over its four reads, consistent across rounds. Both were moved (the end-to-end bench below shows the sidebar gaining as much as the others).
- **Speed (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps, interleaved new/ctl/ctl/new/new/ctl via `bench/results/dotnetway-hybrid-2/bench.sh`; control = H.1 head code fc1c10e, retagged as `campfire-dotnetway:h2ctl`; hybrid image labeled with HEAD 580b1e8; host probe ratio 1.039-1.073 on 12 probes, load 1.3-1.9 before each run, 0 non-2xx, 0 connection errors in 30 runs per side, decoded bytes equal Rust's on every probed route, post rows equal the 2xx answers):**

| workload | c | control req/s | hybrid-2 req/s | gain | CPU us/req ctl -> new | x Rails (new) |
|---|---|---:|---:|---:|---|---:|
| room page | 16 | 5,823 | 8,305 | +42.6% | 589 -> 396 | 34.71 |
| room page | 1 | 1,819 | 2,770 | +52.3% | 647 -> 457 | 25.87 |
| messages page | 16 | 5,305 | 7,657 | +44.3% | 651 -> 436 | 20.12 |
| messages page | 1 | 1,592 | 2,402 | +50.9% | 725 -> 512 | 11.03 |
| sidebar | 16 | 5,824 | 7,793 | +33.8% | 592 -> 433 | 13.02 |
| sidebar | 1 | 1,706 | 2,337 | +37.0% | 686 -> 528 | 8.15 |
| search | 16 | 5,224 | 7,580 | +45.1% | 673 -> 451 | 18.58 |
| search | 1 | 1,556 | 2,341 | +50.4% | 739 -> 521 | 12.15 |
| post a message | 16 | 1,907 | 2,053 | +7.7% | 1,609 -> 1,448 | 6.22 |
| post a message | 1 | 974 | 1,142 | +17.2% | 1,799 -> 1,621 | 6.84 |

  Every cell is over the floor (thinnest: sidebar c=1 8.15x, post 6.22x/6.84x). The gain is far larger than H.1's 1-7% and than the in-process figures predict (about 30-80 us of a 590-740 us request, yet CPU per request dropped 160-220 us). The control reproduces H.1's own numbers (room page c=1 1,819 vs 1,814 then), so the host is not what moved. I did not isolate why; my hypothesis is that a request with no EF query at all skips fixed per-request EF work (query pipeline entry, change-tracker/state setup on the leased context) that H.1's remaining EF statements still paid, so the last EF read of a page costs far more than its statement time. Recorded, not proven; the post path (still has EF writes) gains +8-17%, which fits.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` Release, warnings as errors: 974 total, 968 passed, 6 skipped, 0 failed (H1, H2, H3 included). `bin/pixel-parity --fast --only 'rooms/**,messages/**,search/**,users/sidebar*'`: default 146/146, crowd 8/8, custom_styles 6/6, restricted 2/2, first_run 0/0 (no matching states), pixel, aria and loose DOM equal in every layer (`bench/results/dotnetway-hybrid-2/pixel.log`). `bin/db-differential` rc 0 (`bench/results/dotnetway-hybrid-2/db-differential.log`).
- **KLOC:** `bin/kloc` 35,189 by upstream's rules, **35,408 corrected (35.4 KLOC)**, unchanged at one decimal from H.1 (35.4): the Dapper reads replace roughly the same number of lines of LINQ and `FromSql`.
- **Differences recorded:** none against Rails (the order note above is internal).
- Evidence: `bench/results/dotnetway-hybrid-2/` (`new/`, `ctl/`, logs, `bench.sh`, `comparison.md`, `pixel.log`). `bin/clean-docker` and `bin/clean-parity` run; control image removed.

### Phase 3, unit 3.1: Decoded golden comparison (2026-10-21)

- **Built (test-only, no source change):** `tests/Shared/JsonFrames.fs` (linked into `Campfire.Cable.Tests` and `Campfire.App.Tests`): `canonical` (frames that are JSON objects or arrays go through `JsonNode.Parse(...).ToJsonString(UnsafeRelaxedJsonEscaping)`, which keeps key order, string contents and number text and drops escaping; harness lines such as `upgrade 101 ...`, `http 404 ...`, `close Some(...)` and `end` pass through unchanged), `assertSameFrames` (same count, same order), `assertSameFrame`, `assertSameFramesUnordered`. `GoldenTests` (Cable) compares each exchange's frames in order through `assertSameFrames` (step names also checked; the 26 exchanges / 25 frames counts are still asserted). `ChannelsGoldenTests` keeps its per-socket, per-step sorted comparison, now decoded, and also asserts which sockets heard frames. `BroadcastsTests`: the unread delivery frame and the escaping test (renamed ``broadcast frames decode to active support json``) go through `assertSameFrame`; the Turbo-stream assertions were already on the decoded message.
- **Tests:** G1 in `tests/Campfire.Cable.Tests/JsonFramesTests.fs` (Rails and relaxed escaping equal, including non-BMP surrogate escapes; changed value, swapped keys, `1` vs `1.0`, identifier inner key order, frame count and order all unequal; harness lines pass through). G2 and G3 are the existing replays through the new comparator.
- **Judged against the untouched transport:** every golden test passes on the current server (`replays reference frames` in both projects, `BroadcastsTests`).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): 977 total, 971 passed, 6 skipped, 0 failed. No bench or pixel run: test-only unit.
- **KLOC:** `bin/kloc` 35,189 upstream rules, 35,408 corrected (35.4), unchanged (tests are not counted).
- **Differences recorded:** none. Note for 3.3-3.5: `ChannelsTests` still compares many `delivery ...` frames byte for byte (`Assert.Equal(delivery ...)`); they were not in this unit's list and will need `assertSameFrame` when `Payload` changes the escaping.

### Phase 3, unit 3.2: Frames and registry (2026-10-21)

- **Built (nothing uses it yet, beside the old files):** `src/Campfire.Cable/Options.fs` (`CableOptions`), `Frames.fs` (`CableJson`, `Payload`, `Frames`: welcome, ping, disconnect, identifierJson, confirmation, rejection, message; relaxed escaping, one `byte[]` per frame), `Streams.fs` (`IOutbound`, `StreamSubscriber`, `StreamRegistry` with a sorted `ImmutableArray` per broadcasting updated by a TryUpdate/TryRemove(KeyValuePair) retry loop and one frame per distinct identifier, `ConnectionIndex`). Compile order: Protocol, Options, Frames, Naming, ..., Channel, Streams, Server (Streams needs `DisconnectReason`).
- **Names that differ from the design until 3.4:** `DisconnectReason` is still defined in Protocol.fs (identical cases; Frames reuses it, 3.4 moves it into Frames.fs when Protocol.fs goes). The design's `Subscriber` record is `StreamSubscriber` because Pubsub.fs owns `Subscriber` (a class); 3.4 renames it back. `StreamRegistry.FramesBuilt` is an addition (R5 needs "no frame built" to be observable). `Payload.toBytes` is an addition for tests and the old transport in 3.3.
- **Tests:** F1-F4 in `tests/Campfire.Cable.Tests/FramesTests.fs` (F1 also rebuilds every recorded welcome, confirm, reject and disconnect frame of `reference.json` and compares under `canonical`), R1-R6 in `StreamsTests.fs` over a fake `IOutbound` (R2 concurrent: 8 threads x 625 add/broadcast/remove cycles; R6 is best of 5 because a single run took 10.6 ms once while the suite ran in parallel, a frame is built per broadcast).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): 990 total, 984 passed, 6 skipped, 0 failed (the first run had R6 at 10.6 ms, fixed as above). No bench or pixel run: no runtime path changed.
- **KLOC:** `bin/kloc` 35,189 upstream rules, 35,408 corrected (35.4); unchanged in the reported figure (the new files are about 230 lines, offset by rounding; the old transport files are deleted in 3.4).
- **Differences recorded:** none user-visible.

### Phase 3, unit 3.3: Channel API (2026-10-21)

- **Built (on the old transport):** `Channel.fs` to the design's section 3 shape: `ICableBroadcaster` (`Broadcast(broadcasting, Payload) -> int`, which `Server` implements), `Subscription<'U>` with `Params : JsonElement` (parsed on first use and `Clone`d off the parser's buffer), `Param : string -> JsonElement voption`, `Services`, `Broadcaster`, `BroadcastTo(list, Payload)`, `Channel<'U>` with `Subscribed`/`Unsubscribed : ... -> Task` and `Perform : string -> JsonElement -> Subscription -> Task<bool>` (an exception is Rails' rescued error: `Connection.safely` logs it and sends nothing), and `ChannelRegistry` (`FrozenDictionary`, `create`, `resolve` with the `::` rule). `ChannelResult`, `ChannelError`, `Params` and `IServer` are gone. `Connection.PerformAction` parses `data` with `JsonDocument` for the one call (object only; `action` null, missing or blank is `receive`, a non-string action is "invalid action"). `Server` takes a `ChannelRegistry` and a services provider (`ServerBuilder.withServices`; default an empty provider), broadcasts `Payload`s, and `Disconnect` writes its internal-channel message as literal JSON. `Turbo.fs` is on strings (`actionTag` builds a `StringBuilder`; `Template`, `TagSink` and `HtmlUtf8` are gone; `broadcast*To` take an `ICableBroadcaster` and send `Payload.ofString`); `verifiedStreamNameFromParams` raises for a non-string name. `JsonText.fs` (and `JsonStringWriter`) is deleted with `JsonTextTests`. The nine App channels read `JsonElement` params (`castId` over `ValueKind`), raise instead of returning `Error`, and broadcast `Payload.ofValue` (typing `{action,user:{id,name}}`, read-room `{room_id}`, unread `{roomId}` encoded once for every member). `Broadcasts` takes an `ICableBroadcaster`, decodes a fragment's UTF-8 to a string until Phase 4 (`ToUtf8`), and `BroadcastNames.readRoom` takes one too. `bench/cable/fsharp/Program.fs` compiles against the new API.
- **Tests:** C1-C5 in `tests/Campfire.Cable.Tests/ChannelTests.fs` (params and `Param`, `Services`/`Broadcaster` are the server's, an exception in `subscribed` or `perform` is logged and the connection carries on, `perform` action/data rules, registry resolution). Moved to the new API with their meaning unchanged: Cable `Support`, `ProtocolTests`, `GoldenTests`, `TurboTests`, `FanOutTests`; App `ChannelsTests` (frames now through `assertSameFrame`; `castId` over `JsonElement`) and `RevocationTests`. `BroadcastsTests` and `ChannelsGoldenTests` pass unchanged from 3.1.
- **Differences from the design, and recorded differences:** (1) `Subscription.Transmit(Payload)` stays (not in the design's list): the Cable test channels use it and nothing in the app does; 3.4 decides. (2) `sub.Services` is the server's provider but the App passes none yet (the default is empty), and the channels still take their `Access` (context factory, write gate, write env) as closures instead of resolving `CampfireDb` from `sub.Services`: a root provider cannot lend a scoped `DbContext` without leaking it, so that moves to 3.5 with the per-command scope (C12). (3) Broadcast frames now escape as `System.Text.Json` with `UnsafeRelaxedJsonEscaping` does (`<`, `>`, `&` raw) where Active Support wrote `<`; the frames decode equal and are compared that way (S6, C15). Confirmation, rejection and `transmit` frames still use the old `Protocol` writer (3.4). (4) `Perform`'s `data` is a `JsonElement` valid only during the call.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): 992 total, 986 passed, 0 failed, 6 skipped (H1 included). `bin/pixel-parity --fast --seeds default --only "realtime/<state>"`, one state per run, on the image built from `a17753e`: `message_sent`, `typing/one`, `presence_read`, `removed_from_room` each 2/2 screenshots, aria and loose equal (`bench/results/dotnetway-3-3/pixel-*.log`); `bin/clean-parity` between runs, `bin/clean-docker` after the build.
- **Speed (`bench/quick`, 3 reps, alternating order, same-session control = the previous image `580b1e8`, whose runtime paths 3.1 and 3.2 did not change; host load 1.9-2.4; `bench/results/dotnetway-3-3/comparison.md`):** every cell within +-1.4% of the control (the broadcast on the post path: c=1 1,142 -> 1,137, c=16 2,077 -> 2,047, CPU 1,620 -> 1,619 and 1,453 -> 1,466 us/req). Against stored Rails: room page 34.6x/26.0x, messages 19.9x/11.1x, sidebar 12.9x/8.2x, search 18.6x/12.1x, post 6.20x/6.81x; none under the 5x floor. No `bench/cable/run` (the transport is unchanged; 3.6 runs it).
- **KLOC:** `bin/kloc` 35,199 upstream rules, 35,418 corrected (35.4); Cable 1,673, App 12,229.

### Phase 3, unit 3.4: Transport on ASP.NET Core WebSockets (2026-10-21)

- **Built:** `/cable` is `app.UseWebSockets()` (after `UseSession`, before routing in `Web/Services.fs`) and `Endpoint.handle`: a request that is not `IsWebSocketRequest` or whose `Origin` is not allowed gets the 404 `Page not found` page; otherwise the connection is authenticated, the upgrade is accepted with the first supported subprotocol, `DangerousEnableCompression = false` and `KeepAliveInterval = Zero`, and `CableConnection.RunAsync` runs. `Connection.fs`: one bounded `Channel<ReadOnlyMemory<byte>>` (256) per connection drained by one writer loop into `SendAsync` (welcome, confirmations, broadcasts, pings, disconnects and the close frame all go through it); broadcasters only `TryWrite` (a full queue aborts the connection, a send over `SendTimeout` is cancelled and aborts it); the read loop does a zero-byte receive per message, reads up to `MaxMessageBytes` (1 MiB, then close 1009) into a pooled buffer and dispatches commands in arrival order; a client Close is echoed through the writer; after a server close the client gets `CloseTimeout` to answer, then `ctx.Abort()`. Streams a callback starts are registered after its transmissions and the confirmation (C13). `Server.fs`: `CableServer<'U>` (`Broadcast`, `BroadcastTo`, `Disconnect`, `Restart`, `Beat`, `ConnectionCount`, `StreamCount`, the origin check from the request's scheme and host) and the trimmed `ServerBuilder`. `Heartbeat.fs`: `CableHeartbeat` (a `BackgroundService` on a `PeriodicTimer(BeatInterval, TimeProvider)`, started at boot) and `CableShutdown` (`ApplicationStopping` -> `Restart`). App: `Channels/CableServices.fs` (`add`: the server, heartbeat and shutdown in DI), `SessionAuthenticator.Connect` now takes the upgrade's `HttpContext`, `Registry.server` passes `CableOptions` and the app clock as the `TimeProvider`, `Boot` registers `CableServices.add` and no longer calls `Cable.Restart()` itself.
- **Deleted:** `Socket.fs` (554 lines), `Pubsub.fs`, `Config.fs`, `Protocol.fs` (`DisconnectReason` moved to `Frames.fs`; `Subscriber` was already `StreamSubscriber`, kept), and the tests `SocketTests`, `PubsubTests`, `ServerTests`, `FanOutTests`, the deflate cases and `cable over TLS` of `ProtocolTests`, the "101 as websocket-driver does" case.
- **Tests:** the rewritten `ProtocolTests` (raw TCP client, still used for the clients that never read or send what a browser would not) and `GoldenTests`/`ChannelTests` run on the new server through `Support.hostWith` (Kestrel on loopback with `UseWebSockets`, the heartbeat and shutdown services); every `TestServer` disposal now checks `ConnectionCount` and `StreamCount` are back to 0. New in `TransportTests.fs` over `ClientWebSocket`: P1 (subprotocol order, none, no `Sec-WebSocket-Extensions` for a deflate offer), P8 (a channel transmits in `subscribed` while another thread broadcasts 1,000 numbered messages: transmission, confirmation, then increasing numbers with no gap), P11 (client close 1001 answered; a client that never answers the server's close is cut within `CloseTimeout`), P12 (stopping the host: three connections each get `server_restart`, close 1000, stop under 2 s). `CableHostTests.fs` (App) runs P12 against the app's own pipeline and host (`bootSeeded`, David's Rails cookie): `UseWebSockets` works behind the app's edge middleware, `ApplicationStopping` reaches `Restart`, and the stop completes. P2, P3 (a 50 ms `BeatInterval`: welcome first, then pings), P4-P7, P9, P10 are the rewritten `ProtocolTests` cases. L1 in light form (`a subscriber that cannot keep up is cut, not waited for`: capacity 2, 10,000 broadcasts under 5 s, connection and stream counts back to 0). `FakeTimeProvider` and `FakeLogger` are not in the repo's package set (no `Microsoft.Extensions.*.Testing` in `Directory.Packages.props`), so P3 uses a short real interval and the tests' own `ILogger` collects log lines.
- **Differences from the design note:** (1) `CableServer` is constructed at boot by `Registry.server`/`ServerBuilder` (not by a DI factory) and takes `CableOptions`, the root `IServiceProvider` and a `TimeProvider`, not `IOptions`, `IServiceScopeFactory` and `IHostApplicationLifetime`; `AppState.Cable` is unchanged. The shutdown hook is a separate hosted service, and `CableHeartbeat` takes only the server. 3.5 moves construction into DI and adds the scopes. (2) The explicit `Cable.Restart()` in `Boot.serve` is gone: P12 on the app's host shows `ApplicationStopping` precedes the drain (risk 8 did not occur). (3) A stream a channel names twice is registered twice and delivered twice, as Rails does (the golden `unread fanout` step records two frames); `StopStreamFrom` removes all of them. (4) Broadcasts that reach a connection while one of its own commands runs are held and queued after the command's frames (bounded by the queue capacity), because the in-process pub/sub is synchronous and Rails' is not: Presence's `subscribed` broadcasts a `ReadRoomsChannel` frame to the same user's open `ReadRoomsChannel` before its confirmation otherwise (the old transport read streams after the command, which gave the same order). (5) The re-check after registration (C10) runs after `welcome` is queued, so a session that disappears in that window sees `welcome` then the unauthorized `disconnect`, where the old transport sent the disconnect alone; the first lookup failing is unchanged (101, disconnect, close 1000, no welcome). (6) The authenticator is still the App's `SessionAuthenticator` reading the `Cookie` header (Phase 2 moved pages to cookie authentication but left Cable on its own reader); 3.5 replaces it with `AuthenticateAsync Schemes.Session`. (7) A zero-byte read first, then a pooled buffer per message; the Binary case reads and discards the message.
- **Known differences this unit lands (README in 3.6):** WebSocket compression none (as Rails, the Rust/port entry goes); slow clients are cut at 256 queued frames or a 30-s stalled send, without a `disconnect` frame; the origin is compared with the request's scheme and host after the forwarded headers and `assume_ssl`; escaping inside frames and the 101 headers differ byte-wise (class A).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): 976 total, 970 passed, 6 skipped, 0 failed (H1 included; the Cable golden replay still 26 exchanges and 25 frames, the App channels replay green). `bench/cable/fsharp` builds against the new API. `bin/pixel-parity --fast --seeds default --only "realtime/<state>"`, one state per run on the image built from `51f5cb7`: `message_sent` 36 s, `typing/one` 34 s, `presence_read` 35 s, `removed_from_room` 77 s, each 2/2 screenshots, aria and loose equal (`bench/results/dotnetway-3-4/pixel-*.log`). An earlier run of the App tests showed `PresenterAccountsTests.autocompletes users` failing with a 302 once (passes alone and in three later full runs; the sign-in rate limit is per IP and the suite shares addresses; not caused by Cable).
- **Speed (`bench/quick`, 3 reps, alternating order, same-session control = the 3.3 image `a17753e`, host probe ratio 1.02-1.06 on all six runs, load 0-1.5 at each start but 3-4 on the Mac between runs from other sessions; `bench/results/dotnetway-3-4/comparison.md`):** every cell within -2.4% to +1.2% of the control (post c=1 1,142 -> 1,115 req/s at the same CPU, 1,625 -> 1,621 us/req; post c=16 2,060 -> 2,050), nothing outside run-to-run noise; against stored Rails: room 34.8x/25.5x, messages 20.1x/11.0x, sidebar 12.9x/8.0x, search 18.5x/12.0x, post 6.21x/6.68x (c=16/c=1), all over the 5x floor (thinnest: post c=16 6.21x, sidebar c=1 8.04x). The first attempt at this bench was discarded and rerun because a build I started on the Mac during rep 1 cut room page c=1 to 2,107 req/s; no further work ran on the Mac during the rerun. Cable fan-out (`bench/cable/run`) is unit 3.6.
- **KLOC:** `bin/kloc` 34,464 upstream rules, **34,683 corrected (34.7 KLOC)**, from 35.4. `Campfire.Cable` 956 lines (from 1,673; the design projected 0.70 KLOC: `Connection.fs` is 0.45, with the held-broadcast rule, the dispatch logging and the pooled read loop), App `Channels/` +17 (`CableServices.fs`).
- Evidence: `bench/results/dotnetway-3-4/` (`new/`, `ctl/`, logs, `bench.sh`, `realtime.sh`, `comparison.md`, `pixel-*.log`). `bin/clean-docker` and `bin/clean-parity` run, control image removed, 30 GB free.

### Phase 3, checkpoint after 3.4: Opus review (2026-10-21) - GO for 3.5, with notes

Adversarial review of `0243446..7bcaef4` (H.0-H.2, 3.1-3.4) against `plans/design-cable.md`, the hybrid-reads decision and D1-D9. Evidence in
`bench/results/dotnetway-3-4-ck/`.
- **Re-run here:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` before any change: 976 total, 970 passed, 6 skipped, 0 failed (the 3.4 numbers). After the
  fixes: 979 total, 973 passed, 6 skipped, 0 failed, 0 warnings (H1 included; Cable golden replay 26 exchanges / 25 frames). `bin/pixel-parity --fast
  --seeds default --only "realtime/<state>"`, one state per run, on the image built from `48cc061` (revision label checked): `message_sent`,
  `typing/one`, `presence_read`, `removed_from_room` each 2/2 screenshots, aria and loose equal. The units' own evidence (3.4's `pixel-*.log`, H.2's
  `comparison.md`) matches their entries. No bench: the fixes are off the request path (the connection's flush, cleanup and read loop).
- **Checked and holding:** protocol frames (welcome, ping with the app clock's seconds, confirm/reject, message, disconnect reasons, close 1000 `""`)
  all go through the one writer loop, so there is never a second outstanding `SendAsync`; the 256-frame bounded queue is only `TryWrite`n, a full
  queue aborts, a stalled send is cancelled after `SendTimeout`; streams register after the confirmation; the origin check and the 404 page; the
  subprotocol pick; golden frames compared decoded (`JsonFrames.canonical`, order-sensitive). Hybrid reads: every statement is a constant with
  `@` parameters (the JSON id list is built from `int64`s), the only-in-FastReads guard fires on a probe file, reads inside a write take
  `CurrentTransaction` (tested), the equivalence test covers every row of the default seed for the by-id reads and the sidebar/search reads.
- **Fixed (`48cc061`):** (1) `InCommand` dropped its flag before flushing the held broadcasts, so a broadcaster that read it down could put a later
  frame of a stream ahead of an earlier held one; the flag now drops after the flush, inside the lock. (2) A `TryWrite`/`Close` from a registry
  snapshot taken before a connection left could, after the request ended, count as a slow consumer and call `Abort` on a finished `HttpContext`
  (an `ObjectDisposedException` thrown into the broadcasting request or the heartbeat); cleanup now marks the connection closing before completing
  its queue, and `Abort` tolerates a disposed context. (3) Commands sent after the server began closing ran their callbacks (Rails' 
  `dispatch_websocket_message` checks `websocket.alive?`); they are logged and ignored, which also stops an unauthorized connection from reaching
  `user.Value`. (4) An empty text message ends at its zero-byte announcement; the loop read on and took the next frame (a close) as its body, so the
  close was answered 1000 instead of the client's code; a close between fragments is now echoed as well. (5) A throwing `Beat` is logged instead of
  faulting `CableHeartbeat` (a faulted `BackgroundService` stops the host). Tests: (3) and (4) fail on the old `Connection.fs` (2 vs 1 subscribed;
  `NormalClosure` vs `EndpointUnavailable`) and pass now; the design's risk-4 test (a ping under the frozen harness clock, carrying the frozen
  seconds, within 4 s) is added to `CableHostTests`.
- **KLOC:** `bin/kloc` 34,517 by upstream's rules, **34,736 corrected (34.7 KLOC)**.
- **Notes for 3.5 and 3.6** (also in the checkpoint's StructuredOutput): see the orchestrator's notes; in short, C10's re-check must not be a second
  `AuthenticateAsync`, scopes per command and per close, new upgrades after `ApplicationStopping` are not closed by `Restart`, L2/L4/L5/L7 and the
  risk-3 test are still owed, P8 can starve on a fast host, `connected_at` non-null is not in the equivalence test's seed.
- `bin/clean-docker` after the build and `bin/clean-parity` after each state; 31 GB free.

### Phase 3, unit 3.5: Auth, scopes, revocation, after-commit (2026-10-21)

- **Built:** (1) `Channels/Auth.fs` `CableAuth.authenticate` is `AuthenticateAsync Schemes.Session` (user from `Current`, kept as `CableUser`); `SessionAuthenticator` and its second reader of the signed cookie are deleted. The session lookup now lives once, in `SessionCookie.lookup db token` (`sessions` row, then user), and `validatePrincipal` and the Cable re-check both call it. (2) C10: `CableAuth.recheck` takes the token from the first result's ticket (`Schemes.TokenClaim`; a repeat `AuthenticateAsync` would return the cached first answer) and runs `SessionCookie.lookup` on the `CampfireDb` of a fresh scope; `CableConnection.RunAsync` calls it after the connection is in the index, behind a test hook (`CableServer.AfterRegistered`, A3). (3) C12: each command (`subscribe`, `unsubscribe`, `message`) runs in `IServiceScopeFactory.CreateAsyncScope()`, and so does each `unsubscribed` on close (one scope per subscription); `Subscription.Services` is that scope. The room channels no longer take an `Access` record: `RoomChannel.Access.read/write sub` use the scope's `CampfireDb`, `WriteGate` and `Env`. (4) `CableServer` is a DI singleton built in `CableServices.add` from `IOptions<CableOptions>`, `IServiceScopeFactory`, `TimeProvider`, `IHostApplicationLifetime`, `ILogger`; it hooks `ApplicationStopping` itself (`CableShutdown` is deleted) and sets a stopping flag, so a connection that registers after `Restart` has walked the index is sent `server_restart` right after `welcome`. (5) C16: `Publish.broadcast` / `Publish.disconnectUser` throw `InvalidOperationException` while `WriteGate.InWrite`; `Broadcasts` wraps whatever it is given in `Publish.guarded`, `BroadcastNames.readRoom` and `Revocation.disconnectUser` call `Publish` directly, so every Broadcasts or Revocation site is behind the guard. (6) Deleted: `SessionAuthenticator`, `Channels/Registry.fs`, `Jobs.SetCable`, `Deps`, `CableShutdown`.
- **Differences from the design note:** (1) `SessionCookie.validatePrincipal` needed no WebSocket condition: `Session#resume` and the cookie re-issue are `Authentication.restore`'s, never run by the Cable endpoint, and sliding expiration is off, so an upgrade already writes nothing and sets no cookie (A2 shows it for a session two hours stale). `bin/cookie-roundtrip` was therefore not run (`validatePrincipal` changed only by extracting the lookup). (2) `CableServer` takes one more constructor parameter than the design lists, `recheck: HttpContext -> IServiceProvider -> Task<bool>`, because the token claim is the App's (`Campfire.Cable` does not know `Schemes`). (3) `AppState` and `Jobs` are built before the host exists, and the server needs the host's scope factory and lifetime, so they reach it through `CableHandle` (`Channels/Identity.fs`): `CableServices.add` registers a hosted `CableLink` that hands it the host's provider at start, and `AppState.Cable` / `AppState.Broadcasts` / the `Jobs` sink resolve through it (`Jobs` before the host has started drops `DisconnectUser`, as before boot). `JobRunner.start` lost its `cable` argument. (4) `Web.Services.addAuthentication` was extracted from `Services.add` so the Cable test rig registers the same scheme; `Env` is a DI singleton now (Boot and the rig register it). (5) `Cable` tests that build a server outside a host keep `ServerBuilder` (`withRecheck`, `withLifetime`; default lifetime never stops) and the test host calls `server.Restart()` from `ApplicationStopping`. (6) `CableOptions` lost `[<AllowNullLiteral>]` (`IOptions<T>` wants a non-null class). (7) A6 (closing runs `absent`, in its own scope) is the existing presence case of `ChannelsTests`, now through the per-unsubscribe scope; the ban, deactivation and membership revocations are `RevocationTests`, unchanged and green over the new scheme. A4 is in `Campfire.Cable.Tests` (a scoped `Marker` service: every command and the close-time `unsubscribed` get a distinct instance, disposed when the command ends), not on `CampfireDb` itself.
- **Tests:** `CableAuthTests.fs` (App, over `bootSeeded` and the real pipeline): A1 (David's Rails cookie gets `welcome`; no cookie, the forged vector and a flipped digit get the unauthorized frame and close 1000), A2 (a session 2 h stale: `sessions` row unchanged, no `Set-Cookie` on the 101), A3 (a session deleted in the hook between registration and re-check: `welcome`, then unauthorized, close; the same cookie is unauthorized on the next connect), A5 (`Publish.broadcast` inside `Writes.run` is refused; a bot post reaches the room subscriber as `append` and the unread frame with the row already committed when each frame arrives). `TransportTests.fs`: A3 at the transport level (the re-check runs in a scope and its answer closes the connection), A4, and P12's late-accept case (`Restart()` then a new upgrade: `welcome`, `server_restart`, close 1000). `ChannelTests` C2 now asserts `Services` is a scope of the server's provider, not the provider itself.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): 986 total, 980 passed, 6 skipped (the same environmental 6), 0 failed, 0 warnings; H1 green; Cable golden replay still 26 exchanges / 25 frames; `bench/cable/fsharp` builds. `bin/pixel-parity --fast --seeds default --only "realtime/<state>"`, one state per run on the image built from `43df8fb` (revision label checked): `removed_from_room` 77 s, `room_deleted_then_send` 39 s, `message_sent` 35 s, `typing/one` 34 s, `presence_read` 34 s, each 2/2 screenshots, aria and loose equal (`bench/results/dotnetway-3-5/pixel-*.log`).
- **Speed (`bench/quick --reps 3`, new image only; host probe ratio 1.04-1.05, load 1.0 at the start; no same-session control image was built, so the comparison is with 3.4's recorded numbers, one session apart; `bench/results/dotnetway-3-5/bench-new.log`):** room page 34.1x / 25.6x Rails (c=16 / c=1), messages 19.7x / 11.0x, sidebar 12.6x / 8.08x, search 18.4x / 12.0x, post 6.20x / 6.59x. Every cell is within 2.5% of 3.4's (the largest: messages c=16 7,660 -> 7,483 and sidebar c=16 7,708 -> 7,545 req/s, about -2%, in the run-to-run noise 3.4 saw across sessions; post c=16 2,050 -> 2,046, c=1 1,115 -> 1,100), all over the 5x floor; nothing blocks. `AppState.Broadcasts` is a singleton lookup per call now, below the noise.
- **KLOC:** `bin/kloc` 34,603 upstream rules, **34,822 corrected (34.8 KLOC)** (3.4: 34,683 corrected). `Campfire.Cable` 1,036 lines (3.4: 956; the re-check, scopes and the stopping flag in `Connection.fs`/`Server.fs`). The corrected total rose because the Cable test rig and `Auth`/`Publish`/`CableServices` now carry what `Registry.fs` and `Connection.fs` did, with the DI wiring.
- Evidence: `bench/results/dotnetway-3-5/` (`build.sh`, `bench.sh`, logs, `new/`). `bin/clean-docker` and `bin/clean-parity` run; 31 GB free.
- **Still owed to 3.6** (from the checkpoint notes, untouched here): L2, L4, L5, L7 and the risk-3 test; the P8 starvation fix; collecting overflow and aborting outside the `held` lock; the `bench/cable/run fsharp --reps 2` record and the README Known differences; `FakeTimeProvider`/`FakeLogger` need Chris's yes before they enter `Directory.Packages.props`; FastReadsTests with a non-null `connected_at`.

### Phase 3, unit 3.6: Slow consumers, limits, bench, docs (2026-10-21)

- **Built:** (1) `Connection.fs`: a refused write (full queue, over-capacity held backlog) no longer aborts while the `held` lock is taken. `InCommand`'s flush and `TryWrite`'s slow path record the refusal and call `Refused()`/`Abort()` after leaving the lock, so a continuation of `ctx.Abort()` that runs inline cannot re-enter and modify `held` mid-iteration. (2) Tests, all over Kestrel on loopback: `LimitTests.fs` with L1 (the real one: 256-frame queue, a client that never reads and whose socket receive buffer is fixed at 4 KB, another client on the stream gets every numbered frame in order, `unsubscribed` ran, registries back to the survivor's), L2 (`SendTimeout` 1 s, 40 frames of 64 KB, cut 0.5-5 s after, cleaned up as L1), L4 (exactly 1 MiB processed whole and in four fragments; 1 MiB + 1 closes 1009 whole and in four fragments), L5 binary (logged, ignored, connection open); `MemoryTests.fs` L7 (below); in `TransportTests.fs` the risk-3 test (P8 with `BeatInterval` = 10 ms and a recording logger at Debug: no "outstanding" in any entry or exception, no Error entry). The existing light L1 (capacity 2), L3, L5 (invalid UTF-8 closes 1007) and L6 cases are renamed to their ids. `Support.fs` gets `RecordingLogger` (an `ILogger` that keeps entries; no package) on every `TestServer`, and `connectNonReading`. (3) P8 no longer starves: the broadcaster runs until the confirmation and 200 more frames have been read (the dead `stop` ref is now the real stop flag). (4) `FastReadsTests` connects six memberships with `Membership.present` before the Dapper-vs-EF comparisons, so a non-null `connected_at` is compared (the default seed has none). (5) `bench/cable/run --plain`; README "Known differences" for Cable (section 8 of the design, plus the register-after-confirmation order and the memory figure).
- **Tests found, not fixed in code:** a broadcast made right after a client reads `confirm_subscription` can precede the stream's registration (C13: streams register after the confirmation is queued) and reach nobody. My first L1 and a probe test waited for a frame that was never sent; the raw client's 5-s read limit restarts on every `ping`, so the wait ran to the 60-s test limit instead of failing. L1 and L2 now broadcast until the expected subscribers are reached (`broadcastUntilReached`) before counting, and every read in them has a `WaitAsync(5 s)`. The README records the order. The raw client's pings-reset-the-limit behaviour is unchanged (note for the other raw-client tests).
- **L7 and the design's 16 KB:** `GC.GetTotalMemory` before and after 1,000 idle connections (one warm-up connection first, parallelization off for the test) is 17.6 KB per connection with the test's own raw client, 16.2-16.3 KB on the server side once a loopback socket pair (1.3 KB, measured the same way) is subtracted. A bare accepted WebSocket on the same Kestrel with no Cable (accept, zero-byte read) is 13.6-13.7 KB; Cable's share is 2.6 KB. **The design's "under 16 KB" is missed by 0.3 KB, and 13.6 KB of the 16 is the host's** (Kestrel's connection and HttpContext, the middleware, `ManagedWebSocket`; `WebSocketOptions.ReceiveBufferSize` is deprecated and does nothing). The test therefore asserts what Cable controls (its share under 5 KB, measured against the larger of two bare-host runs, since a measurement this size moves 1-2 KB with the GC) and the whole under 20 KB as a regression guard, and the figures go to `bench/results/dotnetway-3-6/l7.txt` when `CABLE_L7_REPORT` is set. Not blocking; decision for Chris if 16 KB is wanted as a hard bound.
- **Not added:** `FakeTimeProvider`/`FakeLogger` (`Microsoft.Extensions.TimeProvider.Testing` and `Diagnostics.Testing` 10.10.0 are not in `Directory.Packages.props`; AGENTS says ask before adding a package). The heartbeat tests keep real 10 ms / 3 s intervals and the frozen-clock provider; log assertions use the 40-line `RecordingLogger`. If Chris says yes, P3's `Advance(3 s)` and the log assertions can move to them.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors, 0 warnings): 994 total, 988 passed, 6 skipped (the same environmental 6), 0 failed (3.5: 986; +8 = L1, L2, L4 x3, L5 binary, L7, risk-3); H1 green; Cable golden replay 26 exchanges / 25 frames. L1 and L2 passed 8 runs in a row in their final form (running them together under CPU contention is what exposed the registration race above), and the Cable suite passed three runs in a row before `bin/verify`. `bin/pixel-parity --fast --seeds default --only "realtime/<state>"`, one state per run, on the image built from `ca7911f` (revision label from the build): `message_sent` 36 s, `typing/one` 34 s, `presence_read` 34 s, `removed_from_room` 76 s, each 2/2 screenshots, aria and loose equal (`bench/results/dotnetway-3-6/pixel-*.log`).
- **Speed:** `bench/cable/run fsharp --reps 2 --plain`, recorded with the same-session control (the bench built at `a17753e`, the old transport) in `bench/results/cable-fanout-dotnetway.md`: burst 0.75-1.85 us/delivery against the control's 1.6-1.8 and the stored 1.6-1.75, lone broadcast 43-55 against 53-55 (stored 41-48), resident set 33-44 kB per client against the control's 44 (stored 30), host load 2-6.7 across the runs. Equal within noise, at best less CPU per delivery; no regression. `bench/quick` was not run: the unit changes the connection's refusal path and tests only, nothing on the HTTP request path (the phase gate runs it).
- **KLOC:** `bin/kloc` 34,618 upstream rules, **34,837 corrected (34.8 KLOC)** (3.5: 34,822). `Campfire.Cable` 1,051 lines (3.5: 1,036; the design projected 0.70).
- **Phase 3 state:** units 3.1-3.6 are built. Left for the phase gates (design section 4): `realtime/*` all 13 states on every engine of the lean matrix, one state per run; `bench/quick` post a message and room page against a same-day control; `bin/kloc`. Open questions: the two test packages above, and the 16 KB.
- Evidence: `bench/results/dotnetway-3-6/` (`build.sh`, `build.log`, `pixel-*.log`, `cable-run.log`, `cable-ctl.log`, `cable-run2.log`, `l7.txt`). `bin/clean-docker` and `bin/clean-parity` run; 30 GB free.

### Phase 3: gate (2026-10-21) - DONE, no blocker

Evidence: `bench/results/dotnetway-p3/` (slim: logs, `report.json` per pixel slice, bench JSON). Everything ran on an image built fresh from `c2742ad` (label `org.opencontainers.image.revision` checked equal to HEAD; clean tree). No code changed in this unit.
- **`CAMPFIRE_REQUIRE_SEED=1 bin/verify`** (Release, warnings as errors): 994 total, 988 passed, 6 skipped (the same environmental 6), 0 failed; H1 green; the golden frames compare decoded (G1-G3, Cable golden replay 26 exchanges / 25 frames) green (`verify.log`).
- **Lean pixel gate** (`pixel-driver.sh`: slices with `--only`, all seeds per slice, the 13 `realtime/**` states one at a time, each on every engine of the lean matrix, so the smoke states on Chromium, Firefox and WebKit, desktop and phone, light and dark; 25 slices, every one rc 0 / PASSED): every one of the 940 cells equal on all three layers.

| seed | screenshots | aria | loose DOM |
|---|---:|---:|---:|
| default | 860/860 | 860/860 | 860/860 |
| crowd | 24/24 | 24/24 | 24/24 |
| custom_styles | 32/32 | 32/32 | 32/32 |
| first_run | 16/16 | 16/16 | 16/16 |
| restricted | 8/8 | 8/8 | 8/8 |

  One cell (`auth/sign_in/rate_limited`) was captured again by the harness's pixel-flake retry and matched. The `realtime/**` states are in the totals above (slices `rt-*`): message_sent, attachment_sent, message_edited, message_deleted, boosted, boost_removed, typing/one, typing/many, unread_in_sidebar, new_room_in_sidebar, removed_from_room, room_deleted_then_send, presence_read, all PASSED.
- **`bin/cookie-roundtrip`** 20/20 both ways (the session lookup is Dapper now). **`bin/db-differential`** rc 0 (fixtures and scenario, schemas agree, Rails boots on the database we wrote, rollback ok).
- **Cable** (`bench/cable/run fsharp --reps 2 --plain`, load 2.4): burst 1.60 and 1.85 us/delivery (stored F# 1.6-1.75; 3.6: 0.75-1.85), lone broadcast 59.5 and 53.3 us (stored 41-48; 3.6: 43-55), latency p50 17.0 ms, resident set 44.0 kB/client (3.6 loaded run 44.3). Equal to the stored row within host noise; recorded beside it in `bench/results/cable-fanout-dotnetway.md`. The script exited 1 after printing both reps (cleanup `wait` on the killed server), no rep failed.
- **Speed** (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps, interleaved new/ctl/ctl/new/new/ctl via `bench.sh`; control = the Phase 2 verifier `0243446` rebuilt as `campfire-dotnetway:p2ctl`, label checked, then removed; host probe ratio 1.036-1.088 (new) and 1.040-1.077 (ctl) on 36 probes each, load average 1.5-2.8 at each rep boundary, 0 non-2xx and 0 connection errors in 30 runs per side, decoded bytes equal Rust's, post rows equal answers). The control reproduces Phase 2's own figures (room page c=16 5,565 against 5,498; post 1,880 against 1,885), so the host is as then.

| workload | c | Phase 2 control req/s | HEAD req/s | gain | x Rails (HEAD) | x Rails (control) | CPU us/req (HEAD) |
|---|---|---:|---:|---:|---:|---:|---:|
| room page | 16 | 5,565 | 8,082 | 1.45 | 33.77 | 23.26 | 405 |
| room page | 1 | 1,703 | 2,690 | 1.58 | 25.12 | 15.90 | 469 |
| messages page | 16 | 5,189 | 7,526 | 1.45 | 19.77 | 13.63 | 441 |
| messages page | 1 | 1,527 | 2,361 | 1.55 | 10.84 | 7.01 | 519 |
| sidebar | 16 | 5,623 | 7,634 | 1.36 | 12.75 | 9.39 | 441 |
| sidebar | 1 | 1,620 | 2,289 | 1.41 | 7.99 | 5.65 | 537 |
| search | 16 | 5,098 | 7,545 | 1.48 | 18.50 | 12.50 | 455 |
| search | 1 | 1,498 | 2,315 | 1.55 | 12.01 | 7.77 | 528 |
| post a message | 16 | 1,880 | 2,046 | 1.09 | 6.20 | 5.70 | 1,476 |
| post a message | 1 | 950 | 1,116 | 1.17 | 6.69 | 5.69 | 1,643 |

  All ten cells are over the 5x floor; no workload is a blocker, so no speed fix was made. Thinnest: sidebar c=1 7.99x, post c=16 6.20x, post c=1 6.69x. The gain against Phase 2 is the hybrid reads (H.1, H.2) and is the same size as H.2 measured (+36-58% on the pages, +9-17% on post); Phase 3's Cable work added no visible cost to the request path (HEAD equals H.2's figures within 1-3%: room page c=16 8,082 against 8,305, post c=16 2,046 against 2,053). Some c=16 page cells have a low first rep (ranges in `quick/compare-new.txt`, e.g. room page 5,607-8,332: JIT warm-up settling on the first rep of a part); medians are reported.
- **KLOC:** `bin/kloc` 34,618 by upstream's rules, **34,837 corrected (34.8 KLOC)**, from 35.4 at the hybrid reads and 35.3 at the Phase 2 close (-0.5 for Phase 3; the plan projected 30.8). `Campfire.Cable` 1,051 (design projected 0.70), App 12,270, Kit 2,838.
- **Differences recorded:** none new. The 16 KB per-connection bound of L7 (16.2 KB measured, 13.6 KB of it Kestrel's) and the two test packages (`FakeTimeProvider`, `FakeLogger`) from 3.6 remain open questions for Chris. `bin/clean-docker` and `bin/clean-parity` run; the control image and its scratch worktree are removed; 30 GB free.

### Phase 3: independent verification (2026-10-21) - PHASE 3 DONE; Phase 4 GO

Adversarial review of `0243446..e9ad93e` (H.0-H.2, 3.1-3.6, the gate) against `plans/design-cable.md`, the hybrid-reads decision and
D1-D9, with the cheap checks re-run. Evidence in `bench/results/dotnetway-p3-verify/`. Fixes in `9a947b2`.
- **Checked and holding:** the old transport is gone (no `Socket.fs`, `Pubsub.fs`, `Protocol.fs`, `Config.fs`, `JsonText.fs`, framing,
  masking, ring or deflate in `src/`; the Cable project is ten files on `Microsoft.AspNetCore.App`). Every frame goes through the one
  writer loop; welcome, ping (app clock seconds), confirm/reject, message and disconnect bytes match the recorded Rails frames decoded.
  `/cable` is any-method, 404 "Page not found" for a non-upgrade or a refused origin, before authentication. Authentication is
  `AuthenticateAsync Schemes.Session` with the page scheme's `SessionCookie.lookup`; the re-check reads the first ticket's token in a
  fresh scope; unauthorized and re-check failures close before the read loop starts, and commands after a close are ignored.
  Revocation: membership destroy and sign-out emit `DisconnectUser(reconnect: true)` after commit, ban and deactivate `false`, through
  `Publish` (refused inside `Writes.run`). Channels: `RoomChannel`, Presence and Typing go through `current_user.rooms.find_by`
  (`FastReads.roomForUser`), `RoomMessagesChannel` derives the room from the verified stream name and checks membership, and
  `Turbo::StreamsChannel` turns `:messages` names away (17 rejection assertions in the App tests). Slow consumers: 256-frame queue,
  `TryWrite` only, aborts outside the `held` lock, 30-s send timeout, 1 MiB message limit (L1-L7). Hybrid reads: 17 Dapper calls, all
  in `FastReads.fs`, every SQL a constant with `@` parameters (the id list is JSON built from `int64`s); the equivalence tests compare
  F# records (structural equality over every field) for every row of the default seed, with connected memberships; reads inside a write
  take `CurrentTransaction` (tested). Typing's `BroadcastTo` reaches the server unguarded by `Publish`, but runs in no write.
- **Fixed (`9a947b2`):** (1) **C13 window.** `SubscribeToChannel` queued `confirm_subscription` and only then registered the streams, so
  a client that had read its confirmation could miss a broadcast made in the microseconds between (3.6 had to make L1/L2 broadcast until
  reached, and the README recorded it as a difference). The streams now register first; the command already holds every broadcast until
  its own frames are queued, so the order on the socket is unchanged (transmissions, confirmation, the stream) and nothing is lost after
  the confirmation, which is Rails' guarantee (it confirms once the adapter acknowledges). L1/L2 broadcast once and assert the count; a
  new stress test (C13, 300 subscribe-confirm-broadcast rounds) asserts the confirmed client is reached every time. It is not
  deterministic: it passed 5 runs against the old order (the window is microseconds). README entry removed; design C13 amended.
  (2) **Cross-site WebSocket hijacking on the real pipeline:** `CableAuthTests` now opens upgrades carrying David's valid cookie from
  another host, another port, another scheme, `null` and no Origin, on the app's default `CableOptions`: each gets 404 and no socket,
  then the same cookie from the app's origin is welcome. With the origin check forced open the test fails (sabotage run). (3) **SQL
  guard:** `bin/verify` now requires every Dapper call in `FastReads.fs` to take a named `...Sql` value and every `...Sql` value to be one
  plain literal (no `$`, no `+`); four sabotaged copies (`+` inside, a public value with a prefix, an inline literal at the call, an
  interpolated value) were each caught, and the old grep caught none of the first three. (4) A stale comment in `Streams.fs`.
- **Re-run here:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: 996 total, 990 passed, 6 skipped (the same environmental 6), 0 failed, 0 warnings
  (the gate's 994 plus the two new tests; H1 green; golden replay 26 exchanges / 25 frames). Cable tests alone 80 total, 79 passed, 1
  skipped. On the image built from `9a947b2` (revision label checked equal), `bin/pixel-parity --fast --seeds default --only
  "realtime/<state>"`, one state per run: `message_sent` 35 s, `typing/one` 34 s, `presence_read` 34 s, `removed_from_room` 76 s,
  `room_deleted_then_send` 39 s, `unread_in_sidebar` 39 s, `new_room_in_sidebar` 38 s, each 2/2 screenshots, aria and loose DOM equal.
  `bench/cable/run fsharp --reps 2 --plain` (load 3.6): burst 1.55 and 2.00 us/delivery, lone broadcast 54.0 and 48.7 us, p50 15.9 and
  16.3 ms, 44.3 kB/client, equal to the gate (row in `bench/results/cable-fanout-dotnetway.md`). No `bench/quick`: nothing on the HTTP
  request path changed; the gate's table (all ten cells over 5x Rails, thinnest sidebar c=1 7.99x, post c=16 6.20x, post c=1 6.69x;
  +36-58% on the pages and +9-17% on post against the Phase 2 control, from the hybrid reads) was checked against `quick/compare-*.txt`.
- **KLOC:** `bin/kloc` 34,618 by upstream's rules, **34,837 corrected (34.8 KLOC)**, unchanged (the fix moves a line and adds comments).
  Phase 3 took the corrected count from 35.4 to 34.8; the plan projected 30.8 and `Campfire.Cable` is 1,051 against the design's 0.70 KLOC.
- **Still open for Chris (not blocking):** the L7 bound (16.2 KB per idle connection measured, 13.6 KB of it Kestrel's, against the
  design's under 16 KB); whether to add `FakeTimeProvider`/`FakeLogger`. Known, not fixed: the `PresenterAccountsTests` 302 flake of 3.4
  (did not recur) and the raw test client's read limit that restarts on every ping.
- **Phase 4 (Views on Falco.Markup): GO.** Every Phase 3 gate holds, no workload is near the floor (post c=16 6.20x is the thinnest
  cell, and Phase 4 is where the string-render cost lands, so the bench against a same-session control in each Phase 4 unit matters).
- `bin/clean-docker` after the build and `bin/clean-parity` after each state; 31 GB free.

### Phase 4, unit 4.1: Foundation and oracles (2026-10-21)

- **Built (`dc41436`, `96eb43e`, `ff1771e`; no template moved):** `Falco.Markup` 1.4.0 and `AngleSharp` 1.8.3 (the design said 1.8.5; 1.8.3 is
  the version in the local package cache and does the job) in `Directory.Packages.props`; `Core/Html.fs` (module
  `Campfire.Views.Markup.Html`: `attr`, `attrInt`, `text`, `int`, `trusted`, `sp`, `nl`, and `Trusted` with a private constructor and the
  makers `ofSanitized`, `ofAssetTags`, `ofAccountStyles`, plus an internal `ofLegacy` the design did not list, for the seam; the lint
  allows it in `Core/Legacy.fs` only). `Html.fragment` is not here: it needs `CachedFragment`, which is unit 4.2's. The writer's byte
  type `Html` is renamed `SafeBytes` (7 files, nothing else used it). `Core/Legacy.fs` has `Legacy.node`; `write` and `inLayout` come
  with 4.2. `bin/lint-markup` (python3, a token scan, so strings and comments cannot fool it) with `--self-test` over 18 planted files in
  `tests/lint-markup/planted/` (each must give exactly one finding of the rule its first line names; the 12 of the design's S2 plus
  `Elem.createSelfClosing "turbo-frame"`, `Elem.create "img"`, a variable tag, `Trusted.ofSanitized` and `GetOrCreateAsync` outside their
  files, `TextNode`) and `tests/lint-markup/clean.fs` (literals, the encoding module, the forbidden spellings in comments and strings);
  `bin/verify` runs the self-test and the lint over `src/Campfire.Views` and `src/Campfire.App` (222 files, clean). The 52 zero-argument
  `Attr` values in the lint were read from the package's dll by reflection, not guessed.
- **LooseDom (`tests/Shared/LooseDom.fs`, 450 lines):** `loose.ts` and the masks of `normalize.ts` over AngleSharp (scripting on, as parse5
  and the browser have it), with the JavaScript semantics that matter kept by hand: `trim` (U+0085 is not whitespace, U+FEFF is), `\d`
  and `\b` as ASCII, `$` as end of input, `JSON.stringify` string escaping, and Node's lenient base64 (`Convert.FromBase64String` refuses
  the leftover bits that the Rails tokens with a path prefix have). `parity/bin/loose-golden` runs `loose.ts` itself in the pinned
  Playwright image over the 84 Rails `golden/a` pages and 30 oracle answers (the first markup answer of each of 30 operations) and stores
  the output in `tests/Campfire.Views.Tests/loose/` (2.0 MB); **W2** requires the port to equal it line for line (with the harness's seed
  clock, so the timestamp masks run too). Sabotaged once (one stored line indented): W2 failed naming the page.
- **Oracle:** `golden.py` has a hostile quota (6 extra cases per operation whose input holds one of 13 marker strings: a script tag,
  quotes, an apostrophe, an ampersand reference, a lone angle bracket, U+E000, a closing style or textarea; 3 for whole pages; an operation
  with no marker case takes any case with a markup character in an argument) and prints the cases per operation with the hostile ones
  counted. `bin/views-differential golden` ran **once** (seed 1, 22,374 cases): **1,139 cases, 546 hostile, every operation that has a
  template has at least 3 hostile cases** (`welcome/show` 3, `layouts/application_wrapper` 4, `sessions/incompatible_browser` 5, the rest
  5-15); the operations without any are the ones with no string input (keys, epoch and ISO times, Ruby numbers, the service worker,
  `messages/_unrenderable`, `rooms/mention_prompt_src`, `messages/room_not_found`). Frozen as `tests/Campfire.Views.Tests/oracle.jsonl`
  (7.2 MB; was `differential.jsonl`, 886 cases, 5.3 MB); the script now writes it to `oracle.jsonl` through a temporary file and prints
  the table. A rerun changes the oracle: do it only to add an operation, and run `parity/bin/loose-golden` after.
- **Comparators:** `DifferentialTests` (O1) compares each answer loose (exact for the operations whose answer is text: URLs, JSON, numbers,
  keys, scripts, the SVG avatar), the recorded-fragment offsets still exactly (the writer is untouched; 4.2 re-points them);
  `ParityATests` and `GoldenB` (O2) go through `LooseDom`, GoldenB rebuilding HTML from `canonical.rb`'s token streams (text re-escaped,
  `script`/`style` text raw) and comparing a page's regions (title, head block, nav, main, footer, sidebar) as before; `Support/Dom.fs`
  and `DomTests.fs` (the html5ever tokenizer) are retired. All of them green on the untouched writer on the first run. Sabotage that
  shows they bite: removing the newline between the `<img>` and the `<span>` of `messages/_actions` failed 10 tests (the messages, rooms
  and search goldens and the oracle), restored. `HtmlTests`: Falco writes `title="a"b<c>"` and `Html.attr` writes
  `a&quot;b&lt;c&gt;&amp;d&#39;e &#233; &#128512;`, the same rule as `Html.text`; `Legacy.node` places a writer partial.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: lint self-test ok (18 planted files), lint clean (222 files), Release build with
  warnings as errors, **1006 total, 1000 passed, 6 skipped (the same environmental 6), 0 failed** (Phase 3 closed at 996/990/6/0: +10 =
  W2 x2, the loose rules x5, `HtmlTests` x5 and the differential/golden tests re-pointed, less `DomTests` x2); H1 green. Image rebuilt from
  `ff1771e` (revision label checked equal): `bin/pixel-parity --fast --seeds default --only "rooms/show/**,auth/sign_in"`: **38/38
  screenshots pixel-equal, 38/38 aria equal, 38/38 loose DOM equal** (141 s). `bin/clean-parity` and `bin/clean-docker` after; 30 GB free.
  No `bench/quick`: no template and nothing on the request path moved (the rename is a type name); the speed gates begin with 4.2.
- **Differences recorded:** none for the README (nothing visible changed). Internal: the oracle's comparison is loose, not byte for byte
  (the point of V11); the lint's allowlist has a fourth maker (`ofLegacy`) that dies with the seam in 4.11.
- **KLOC:** `bin/kloc` 34,645 by upstream's rules, **34,864 corrected (34.9 KLOC)**, from 34,837 (+27: `Html.fs` and `Legacy.fs`; the tests,
  the lint and the planted files are not counted). The drop starts when templates move.

### Phase 4, unit 4.2: Pages in parts (2026-10-21)

- **Built (`e6b2999`, `09b424e`):** `CachedFragment` (`Core/Fragment.fs`: bytes, SHA-256 made with it, a process-unique `Id`) replaces `Fragment`
  in the current `FragmentCache`, and `IFragmentAttachment` goes (the cache counts a fragment's bytes, as Rust does; the SHA and the compressed
  pieces are not the cache's any more). `Html.fragment` writes the slot (`<` U+E000 id `>`). `Core/HtmlOutput.fs`: `HtmlOutput` (parts,
  fragments, `Length`, `TextLength`, `Digest()` = SHA-256 over each part with its length and each fragment's digest, `WriteTo`, `ToArray`,
  `ToString`) and `ofNode`/`ofDocument`/`ofNodes`/`text`: Falco's string split at the slots, each fragment found by `Id`; a slot whose fragment
  was not passed, a fragment placed twice, and one passed and never placed all raise. `Out.Fragment` takes a `CachedFragment` (recorded at
  1 KB and over, copied below, as before), `Out.ToOutput()` cuts the text at the recorded offsets, `Out.Page` takes an `HtmlOutput`,
  `Render.page` and `RenderSize.Render` return one. `Layouts.fs` has `Regions` and `ViewPage` (V8); `Core/Legacy.fs` moved after it in the
  project (it calls the writer's layouts) with `write` and `inLayout` (each region rendered by Falco, written with `write`, so the fragments stay
  fragments in the application layout or turbo-rails' frame layout). `Core/Fragments.fs`: `Fragments.add` (a memory cache with `SizeLimit` =
  `FragmentCacheBytes` and `TrackStatistics`, `AddHybridCache` with `MaximumPayloadBytes` 4 MiB, 7-day expirations and
  `DisableDistributedCache` as the default flag, because the session's `IDistributedCache` would otherwise become an L2) and
  `Fragments.compressedPage` (key `page/<encoding>/<digest hex>`, an immutable `CompressedPage`, a serializer for it and for `CachedFragment`);
  `Microsoft.Extensions.Caching.Hybrid` 10.11.0 in `Directory.Packages.props`. `Pages.send` writes an `HtmlOutput`: `Content-Length` from
  `Length`, a page with at least one fragment and a compressing request goes out as the kept `CompressedPage` (already encoded, `Vary`), a page
  in parts otherwise through `HtmlOutput.WriteTo` (part, fragment, part), and a page of one part through `Body.WriteAsync` (see below).
  **Deleted:** `RecordedPage`, `Web/PageParts.fs` (`Generations`, `BodyDigest`, the splice and the text-digest caches, 527 lines),
  `FragmentGlue.fs`, the `PageCompression` map, `recordedAs`, `RecordedTests`, `FragmentGlueTests`, `PagePartsTests`.
- **Tests:** O4 (`HtmlOutputTests`: a page is its parts and fragments in order; U+E000 and a typed slot in text, an attribute value or trusted
  text leave the part count unchanged; placed-not-passed, passed-not-placed and placed-twice throw; the digest changes with one byte of one
  part, one fragment, or where a part boundary falls; `Legacy.inLayout` keeps the fragments in both layouts and checks them), P1
  (`Web/PagesTests`: over the app, with a counting compression provider: `Content-Length` is the plain length, HEAD keeps it with no body,
  the first gzip request makes one compressed stream and the second none with the same bytes, br is a page of its own made once and gzip is
  still kept, a compressing HEAD makes none; and 16 concurrent requests for one page make one), P2 (the warm 40-fragment room page, rendered,
  digested and written in process: **30 KB per page** against the 300 KB bound; one string per page would be 1.6 MB). `RespondTests` X4 now also
  decodes the `gzip, br` answer and says the kept page is HybridCache's; `RoomsTests` "pages of messages go out in parts" is unchanged in what
  it asserts (more than 40 writes, the gzip decoding to the plain page, HEAD) and its comment points at `Fragments.compressedPage`. The
  `FragmentCacheTests` lost the two tests of fragment attachments (the budget no longer counts pieces); `OutTests` has the sized and frame-layout
  tests that were in `RecordedTests`. The differential's F# runner computes the recorded text and offsets from the parts (`Answer.fs`).
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): lint self-test ok, lint clean (222 files), **991 total, 985
  passed, 6 skipped (the same environmental 6), 0 failed**; H1 green. **Network layer on the image** (`bench/results/dotnetway-4-2/network.log`,
  `network.sh`: David signed in over curl): the room page (416,116 bytes) and a page of older messages (383,844) for no `Accept-Encoding`,
  `identity`, `gzip`, `br` and `gzip, br`, twice each: no `Content-Encoding` for none and identity, exactly one header otherwise (`gzip`; `br`;
  `br` for `gzip, br`), `Vary` carries `Accept-Encoding`, `Content-Length` is the encoded length (37,392 gzip and 21,802 br for the room page,
  25,383 and 11,226 for the messages page), every body decodes to the plain length, the second request is the same size. `bin/pixel-parity --fast`
  on the image built from `e6b2999`: `rooms/**,messages/**,search/**,interactions/sidebar*`: default 146/146, crowd 8/8, custom_styles 6/6,
  first_run 0/0, restricted 2/2 screenshots, aria and loose DOM equal (504 s); `realtime/message_sent`, `realtime/boosted`,
  `realtime/message_edited`, one at a time, 2/2 each (34-37 s). After the one-line change of `09b424e`: `rooms/**,interactions/sidebar*` on
  the default seed, 74/74 equal (222 s). `bin/clean-parity` between runs; `bin/clean-docker` after each build.
- **Speed (`bench/quick`, 3 reps, interleaved new/ctl/ctl/new/new/ctl via `bench.sh`; control = the image of `ff1771e`, whose `src/` is
  identical to the base `4b7c03d` (empty `git diff`), tagged `campfire-dotnetway:u42ctl`, label checked, removed after; host probe ratio
  1.04-1.06 on every window, Mac load average 3.2-4.3 from other sessions at each rep boundary (`loads.txt`), 0 non-2xx and 0 connection
  errors in 30 runs per side, decoded bytes equal Rust's, post rows equal answers):** with `e6b2999`:

| workload | c | control req/s | new req/s | x Rails (new) | x Rails (control) | CPU us/req ctl -> new |
|---|---:|---:|---:|---:|---:|---|
| room page | 16 | 8,239 | 8,149 | 34.05 | 34.43 | 399 -> 407 |
| room page | 1 | 2,740 | 2,712 | 25.32 | 25.58 | 463 -> 466 |
| messages page | 16 | 7,579 | 7,634 | 20.06 | 19.91 | 441 -> 437 |
| messages page | 1 | 2,403 | 2,388 | 10.96 | 11.03 | 512 -> 514 |
| sidebar | 16 | 7,683 | 7,670 | 12.81 | 12.84 | 438 -> 440 |
| sidebar | 1 | 2,327 | 2,274 | 7.94 | 8.12 | 529 -> **643** |
| search | 16 | 7,510 | 7,378 | 18.09 | 18.41 | 455 -> 462 |
| search | 1 | 2,318 | 2,294 | 11.90 | 12.03 | 525 -> 528 |
| post a message | 16 | 2,049 | 2,030 | 6.15 | 6.21 | 1,468 -> 1,468 |
| post a message | 1 | 1,122 | 1,113 | 6.67 | 6.72 | 1,625 -> 1,625 |

  Every cell is within -2.3% to +0.7% of the control's throughput and over the 5x floor (thinnest: post c=16 6.15x, post c=1 6.67x, sidebar
  c=1 7.94x). **One real regression, found and fixed:** sidebar c=1 burned 643 us of CPU per request against 529 (all three reps, 613-649)
  though its throughput moved only -2.3%. The sidebar is a page with no fragment, which `Pages` had written with `Body.WriteAsync` and
  my first `send` wrote through `BodyWriter`, which the response compression middleware backs with a stream-pipe copy. A page of one part is
  written with `Body.WriteAsync` again (`09b424e`); pages in parts still use the writer, as before. Re-measured, sidebar only (the one
  workload the change touches; rooms, messages, search and post all have fragments, so their path is unchanged), 3 reps interleaved with the
  same control, load 3.2-4.3, `bench/results/dotnetway-4-2/new2`, `ctl2`: **c=1 2,401 against 2,422 req/s (8.38x against 8.45x Rails), CPU
  505 against 510 us/req; c=16 7,748 against 7,767 (12.94x against 12.98x), 436 against 435**. The first run's S4 prediction (sidebar c=1
  7.99x -> about 7.8x) was pessimistic: the layout is not rebuilt by Falco until 4.10. Allocation per page on the room page: 30 KB with the
  writer (the design's 150-200 KB arrives with the Falco templates).
- **Not done as asked, and why:** the full five-workload bench ran on `e6b2999`, not on the final `09b424e` (a 50-minute run); the sidebar,
  the only workload whose code path changed in `09b424e`, was re-run on it. `Fragments.add` is the first user of HybridCache: its memory cache
  has the same 32 MiB (`FragmentCacheBytes`) as the old `FragmentCache`, so until 4.3 deletes the old one the process may hold both (the
  kept compressed pages are 20-40 KB each).
- **Differences recorded:** none for the README (the bytes of every page are the writer's). Internal: a kept compressed page is HybridCache's,
  not a two-generation map (evicted by `MemoryCache` compaction, 7-day expiry, the key is the SHA-256 of the parts and the fragments' digests,
  so a page that changes in one byte is another page).
- **KLOC:** `bin/kloc` 34,428 by upstream's rules, **34,647 corrected (34.6 KLOC)**, from 34,864 (-217: `PageParts.fs` 384 and
  `FragmentGlue.fs` 21 out; `HtmlOutput`, `Fragments` and the bigger `Legacy` in, about 180). `Campfire.Views` 6,017 corrected, App 11,854.
- Evidence: `bench/results/dotnetway-4-2/` (`new/`, `ctl/`, `new2/`, `ctl2/`, `bench.sh`, `bench-sidebar.sh`, `pixel.sh`, `network.sh`, logs,
  `loads.txt`, `loads2.txt`). `bin/clean-docker` and `bin/clean-parity` run; the control image removed; 28 GB free.

### Phase 4, unit 4.3: Fragments on HybridCache (2026-10-21)

- **What is in** (`a2fa00e`): `Core/Fragments.fs` has the keys (`Fragments.key`: Rails' `cache_key_with_version` with the partial and variant, no
  template digest: `views/messages/_message/messages/<id>-<usec>/presentation-v3`, `views/messages/boosts/_boost/boosts/...`,
  `views/users/sidebars/rooms/_direct/memberships/...`; `cacheVersion`, `cacheKeyWithVersion`, built in a per-thread scratch buffer, one string
  per key) and `Fragments.fetch` over HybridCache (the 7-day options and the 32 MiB `SizeLimit` of 4.2). `MessagesCached`, `BoostsCached` and
  `UsersCached` are async fetchers (`ValueTask`): `MessagesCached.fetch cache ctx id updatedAt view` builds the `MessageView` **inside the
  factory**, so a hit loads nothing (as Rails' `cache` block), and the factory awaits the message's boosts first; `BoostsCached.boosts`,
  `UsersCached.directRooms` and `MessagesCached.messages`/`item` are the list forms. `MessageItem` is `{ ClientMessageId; RoomId; Html }` and
  `SidebarDirectItem` is `{ Html }` (no `View` case); the templates take fragments as arguments (`_Message.render ... boosts`, `_Boosts`,
  `Boosts.Index`, `Show.render w fragment`), so no template reaches the cache and the lint's rule (no cache call outside `Fragments.fs`) holds.
  The App awaits up front: `Presenter.Messages`/`MessageItem`/`MessageFragment`, `Accounts.sidebar` (takes the cache and a context),
  `Data.readAsync`/`presentAsync` over the new `Reads.runAsync` (the same `DbError` boundary; the request's context is used one step at a time, so
  fetches are in order, never `WhenAll`), the room page, refreshes, messages index/create/show, searches, the sidebar, direct-room and boost
  broadcasts, and the webhook-reply broadcast in `IntegrationJobs`. `AppState.Fragments` is the one HybridCache: built in `Boot` (jobs and
  broadcasts render with no host) and registered as the host's `HybridCache`, so the kept compressed pages and the fragments share one 32 MiB
  budget. The fragments render with `Page.fragmentContext` (no request, no `Current.user`; the only field a fragment reads is the asset path).
- **Deleted:** `Core/FragmentCache.fs` (the byte-bounded store, `KeyBuf`, the template digests, `scoped`/`withCache`), `AppState.FragmentCache`
  and every `scoped`/`withCache` call (`Controllers.fs`, `Data.fs`, `Page.fs` and eight controllers), the three Jbuilder caches in `Presenter.fs`
  (`jbuilderKey`, `cachedUserJson`, `tryFetchValue` calls) and `MessagesJson`'s cache sizes, `FragmentCacheTests` (261 lines), and the 32
  `fragment_cache/keys|script` cases of `oracle.jsonl` (they compared the deleted store's keys and eviction with Rust's; `parity/bin/loose-golden`
  re-run for the new line numbers of the 30 loose answers). The differential's runner (`reference-tools/views/differential/fsharp`) now builds
  fragments through the fetchers (a fresh cache per operation that checks reuse, `mode: mixed` fetches every second message through it) and the
  test project references `Microsoft.AspNetCore.App` for the DI container. Its `Program.fs` (the bench mode of `bin/views-differential`) was
  already stale since 4.2 (`RecordedPage`) and is left; `bin/views-differential bench` does not build.
- **Tests:** F1 (`FragmentsTests`: a hit is the same instance; 64 concurrent misses render once; 401 fragments of 9.4 KB into a 2,000,000-byte
  limit keep the estimated size at or under it and fewer than all; the default entry options are 7 days for both expirations and local-only), F2
  (the keys match the framework's formatting byte for byte over 15 edge timestamps and 11 ids; Rails' key vectors; a message's key changes with its
  `updated_at`; a changed boost gets a new fragment and, touching its message, the message does too, while the other boost is the same instance;
  the message fragment holds its boosts'), F3 (`MessagesTests` "F3 a text message is answered and broadcast as it was stored": the stream the
  broadcast wrote and the response equal, the page finds the same fragment with a factory that fails if run, no `campfire.test` host in it).
  `PresentersTests`' Jbuilder key test is gone with the cache; the Views tests that used `FragmentCache.withCache` render fragments directly.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on the final tree (Release, warnings as errors): lint self-test ok, lint clean (221 files),
  **978 total, 972 passed, 6 skipped (the same environmental 6), 0 failed**; H1 (zero query compilations in steady state) green, P2 unchanged.
  Network layer on the image built from `a2fa00e` (`network.log`): the room page 416,116 bytes and the messages page 383,844 (4.2: 416,116 and
  383,844), `Content-Encoding` once for gzip (37,392 and 25,383), br (21,802 and 11,226) and `gzip, br` (br), none for identity, every body
  decodes to the plain length, the second request the same size. `bin/pixel-parity --fast`: `rooms/**,messages/**,search/**,interactions/sidebar*`:
  default 146/146, crowd 8/8, custom_styles 6/6, first_run 0/0, restricted 2/2 screenshots, aria and loose DOM equal (508 s);
  `realtime/message_sent`, `realtime/boosted`, `realtime/message_edited`, one at a time, 2/2 each (35-37 s). `bin/clean-parity` between runs,
  `bin/clean-docker` after the builds.
- **Speed (`bench/quick`, 3 reps, interleaved new/ctl/ctl/new/new/ctl via `bench.sh`; control = `campfire-dotnetway:u43ctl` built from the base
  `6f69c4b` (the head of 4.2) from its tracked files by `build-ctl.sh`, labels checked, image removed after; host probe ratio 1.033-1.089 over
  all runs, Mac load average 2.2-3.9 from other sessions at each rep boundary (`loads.txt`), 0 non-2xx and 0 connection errors in 30 runs per
  side, decoded bytes equal Rust's, post rows equal answers):**

| workload | c | control req/s | new req/s | x Rails (new) | x Rails (control) | CPU us/req ctl -> new |
|---|---:|---:|---:|---:|---:|---|
| room page | 16 | 8,044 | 7,960 | 33.26 | 33.62 | 412 -> 416 |
| room page | 1 | 2,650 | 2,638 | 24.63 | 24.75 | 475 -> 475 |
| messages page | 16 | 7,422 | 7,437 | 19.54 | 19.50 | 448 -> 448 |
| messages page | 1 | 2,362 | 2,355 | 10.81 | 10.84 | 519 -> 521 |
| sidebar | 16 | 7,601 | 7,727 | 12.91 | 12.70 | 445 -> 437 |
| sidebar | 1 | 2,298 | 2,311 | 8.06 | 8.02 | 534 -> 533 |
| search | 16 | 6,955 | 7,321 | 17.95 | 17.05 | 496 -> 466 |
| search | 1 | 2,220 | 2,231 | 11.58 | 11.52 | 545 -> 544 |
| post a message | 16 | 1,974 | 2,063 | 6.25 | 5.98 | 1,547 -> 1,449 |
| post a message | 1 | 1,095 | 1,156 | 6.92 | 6.56 | 1,693 -> 1,633 |

  No cell is under 5x (thinnest: post c=16 6.25x, post c=1 6.92x, sidebar c=1 8.06x). Every cell is within -1.0% to +5.6% of the control's
  throughput and within +0.9% to -6.3% of its CPU per request (the room and messages pages and both sidebar cells move by under 2%), so the
  fetchers cost nothing measurable: a hit is the key's one string, a closure and a completed `ValueTask`. Search c=16 and both post cells are
  a few percent faster, which is the range of this session's noise rather than a claim (the control's own post c=16 is 5.98x here against
  6.21x in 4.2's session: the stored-Rails ratio carries about 5% between sessions); what the removed Jbuilder caches saved is on the bot API,
  which no workload measures.
- **Not done as asked, and why:** nothing deferred. The six `--fast` slices were run on the image of the final code (`a2fa00e`; the commit after
  it changes only AGENTS.md, README and this log).
- **Differences recorded:** none for the README beyond its rewritten fragment-cache bullet (the cache is HybridCache's: `MemoryCache` compaction
  eviction, seven-day expiry, no template digest in a key, JSON not cached; the design lists this as internal). Bytes of every page are the writer's,
  as before.
- **KLOC:** `bin/kloc` 34,232 by upstream's rules, **34,451 corrected (34.5 KLOC)**, from 34,647 (-196): `Campfire.Views` 5,769 corrected from 6,017
  (`FragmentCache.fs` 485 lines out, the fetchers and `Fragments` keys and fetch in), App 11,888 from 11,854 (the async plumbing and `Reads.runAsync`
  against the cache scoping out), Db +38.
- Evidence: `bench/results/dotnetway-4-3/` (`new/`, `ctl/`, `bench.sh`, `build.sh`, `build-ctl.sh`, `pixel.sh`, `network.sh`, logs, `loads.txt`,
  `verify.log`). 27 GB free after `bin/clean-docker`.

### Phase 4, unit 4.4: Messages (2026-10-21)

- **What is in** (`7b72d08`, `ab76f64`): the 15 `messages/**` templates are Falco.Markup functions: `_message`, `_actions`, `_presentation`, `_unrenderable`,
  `_template`, `boosts/_boost` (two roots: the boost and its screen-reader label), `_boosts`, `boosts/index`, `boosts/new`, `index`, `show`, `edit`, the
  create and destroy turbo streams and `room_not_found`. `MessagesPresentation` is `Markup.Messages.presentation` (`Markup/Messages.fs`, compiled after the
  view-models) and `MessageContent.Text` holds `Html.Trusted` (`Trusted.ofSanitized` in `Presenter.fs`, its one allowlisted call). First parts of
  `Markup/Assets.fs` (`image`, `imageSized`), `Links.fs` (`linkTo`) and `Forms.fs` (`formWith`, `hiddenFieldTag`, `buttonTag`, `buttonTo`; no form token).
  A message's fragment is its node rendered with its boosts' fragments slotted (`Html.fragment`) and flattened by the new `HtmlOutput.toFragment`; the
  unrenderable message passes no boosts (they are not placed). The templates that place fragments (`Index`, `Show`, `CreateTurboStream`,
  `DestroyTurboStream`, `Boosts.Index`) also have `output` (an `HtmlOutput`); the controllers send the messages index and the create/destroy streams
  as parts (`Pages.bare`, `Pages.renderOutput`) and hand `show`, `edit`, `boosts/index|new` and `room_not_found` to the writer's layout through
  `Legacy.write`. `rooms/show` places `_template` with `Legacy.write` too. `_template` is script text with `$placeholders$`: written as the ERB's own text
  around `Html.int`/`Html.text` values (whitespace and the ERB's stray `</div class=...>` kept: the loose layer compares a script's text).
- **Whitespace (V4) found by the tests and the browser:** the oracle and goldens were loose-equal on the first build (every `Html.sp` was placed from the ERB: the
  `h3` heading's three inline pieces, `img` + label in every button and link, the actions grid's buttons, the boost form's label/button/link, the two edit
  buttons). `bin/pixel-parity` found one the tests cannot: `lexxy-editor`'s script puts its toolbar in front of the ERB's whitespace text, so the live DOM
  has a space between the toolbar and `lexxy-prompt`; the Falco edit form lost it (`interactions/edit_form`, `edit_form/with_mention` loose DOM, 4 cells,
  `-1` line each). Fixed with an `Html.sp` before the prompt (`ab76f64`); the slice was re-run on the new image, 94/94 loose equal.
- **Tests:** T1 (`MessagesViewsTests`: a value starting with a newline and holding `</textarea>`, `&lt;`, quotes and `<p>` survives the message editor's
  `value` attribute and a `textarea` written with `Html.nl`, no `script` in the page). The oracle (`DifferentialTests`, hostile text included) and the
  `messages_*` goldens (`MessagesViewsTests`) are re-pointed to the Falco templates and loose-equal. Two assertions that checked the writer's bytes changed:
  `DifferentialTests` compares the fragments' **offsets** in a page's text and no longer their lengths (a message's fragment is Falco's, 8.5 KB against Rust's
  9.4 KB: class A), and `RoomsTests` "pages of messages go out in parts" asks for at least 40 writes (the page of older messages is 40 fragments with no text
  between them, and an empty part is not a write; it was more than 40 when the writer put a newline between messages). The differential's runner
  (`reference-tools/views/differential/fsharp`) builds the messages cases through the Falco templates (`nodeAnswer`: cold and warm, the same bytes).
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on the final tree (Release, warnings as errors): lint self-test ok (18 planted), lint clean (224 files),
  **979 total, 973 passed, 6 skipped (the same environmental 6), 0 failed**; H1 green. One run before the last fixes failed `FrontAcmeTests` "cached certificates
  load and are forgotten by obtaining" (a Kit socket test, nothing in this unit touches it; it passed twice alone and in the final full run).
  `bin/pixel-parity --fast` on the image of `ab76f64`: `messages/**`, `interactions/actions_menu**`, `boost_**`, `quick_boost`, `edit_**`, `reply`, `copy_link`,
  `message_deleted`: default 94/94 screenshots pixel-equal, 94/94 aria, 94/94 loose (317 s; the other four seeds have no such states, 0/0); the first run on
  `7b72d08` was 94/94, 94/94 and 90/94 loose (the four edit_form cells above). `realtime/message_sent`, `message_edited`, `message_deleted`, `boosted`,
  `boost_removed`, `attachment_sent` one at a time on the image of `7b72d08` (the later commit changes only the edit form, which no realtime state shows):
  2/2 equal on all three layers each (34-39 s). `bin/clean-parity` between runs, `bin/clean-docker` after the builds.
- **Speed (`bench/quick`, messages page and post a message, 3 reps, interleaved new/ctl/ctl/new/new/ctl via `bench.sh`; control = `campfire-dotnetway:u44ctl`
  built from the base `4756d98` by `build-ctl.sh`, labels checked, image removed after; host probe ratio 1.035-1.066 over the 12 runs, Mac load average
  3.2-3.6 from other sessions, 0 non-2xx and 0 connection errors in 12 runs per side, post rows equal answers):**

| workload | c | control req/s | new req/s | x Rails (new) | x Rails (control) | CPU us/req ctl -> new |
|---|---:|---:|---:|---:|---:|---|
| messages page | 16 | 7,283 | 7,325 | 19.25 | 19.14 | 457 -> 453 |
| messages page | 1 | 2,411 | 2,397 | 11.00 | 11.07 | 508 -> 513 |
| post a message | 16 | 2,098 | 2,076 | 6.29 | 6.36 | 1,421 -> 1,433 |
| post a message | 1 | 1,183 | 1,185 | 7.10 | 7.09 | 1,600 -> 1,599 |

  No cell is under 5x (thinnest: post c=16 6.29x). Every cell is within -1.1% to +0.6% of the control's throughput and within +0.9% to -0.9% of its CPU per
  request: the Falco message partials cost nothing measurable because a message renders once per version (the fragment is cached) and a page places it.
  Post c=16 is 1.0% under its control (the range of the session's noise, the host probe 1.04-1.07); the design predicted 6.15x.
- **Differences recorded:** none for the README (the design's one class A entry is written in 4.11). Visible: the messages page decodes to **345,844 bytes against
  Rust's 383,844 (-9.9%)**, which `bench/quick`'s "decoded bytes equal Rust's" check reports as a FAIL for `messages_page` (the control and every other route
  pass); the check is the writer's byte equality and is meant to change with the phase (4.11 / the gate decides what replaces it: a stored expected size per route).
  `bench/run`'s exit status is 1 in the meantime, which the bench scripts tolerate.
- **KLOC:** `bin/kloc` 34,263 by upstream's rules, **34,482 corrected (34.5 KLOC)**, from 34,451 (+31): `Campfire.Views` 5,800 corrected from 5,769, App unchanged.
  The messages templates and `MessagesPresentation` were 613 code lines as literal tables and are 641 as Falco templates plus the four `Markup/` files (146);
  the design's 0.6 ratio does not hold for a directory whose writer templates were already one line per literal; the shrink comes from deleting `Out.fs`,
  `Tag.fs` and the writer halves of the helpers in 4.11, and every later template that uses these helpers.
- Evidence: `bench/results/dotnetway-4-4/` (`new/`, `ctl/`, `bench.sh`, `build.sh`, `build-ctl.sh`, `pixel.sh`, `pixel-rerun.sh`, pixel and bench logs, `first-run/`, `loads.txt`,
  `verify.log`). 27 GB free after `bin/clean-docker`.

### Phase 4, unit 4.5: Rooms (2026-10-21)

- **Built:** all 20 `rooms/**` templates are Falco.Markup functions (`Templates/Rooms/`): `show` (`ShowPage`) with `_nav`, `_composer` and `_invitation`; `involvements/_bell` and
  `show`; `layouts/_form`, `_edit` and `_new`; `opens/*`, `closeds/*` (each `_form`, `_user`, `edit`, `new`); `directs/edit` and `new`; the `refreshes/show` turbo stream. New
  `Markup/Turbo.fs` (`frameTag`, `streamFrom`, `stream`), `Rooms.fs` (`buttonToDeleteRoom`, `buttonToChangeInvolvement`, `userRow`), `Application.fs` (`screenReader`, `linkBackTo`,
  `linkBackToLastRoomVisited`) and `Users.fs` (`accountLogoTag`, `userFilterMenu`, `userFilterSearch`, `sidebarFrame`; 4.6 and 4.7 add to them). A page module has
  `rendered ctx model : Layouts.ViewPage` (its `Regions` and the fragments it places); `Pages.viewPage` (new, in `Web/Pages.fs`) sends it through `Legacy.inLayout`, the writer's
  application layout or the frame layout, until 4.10. The involvement frame goes out through `Legacy.write`, the refresh as an `HtmlOutput` (an empty refresh is an empty body,
  as the design says). The writer partials a room page still holds (the translation button, the join link, the three notification-help partials, the mention template) are
  placed with `Legacy.node`; `UsersHelper.sidebarTurboFrameTag`, `Rooms.buttonToDeleteRoom` (writer) and the old `ShowPage.head/content/footer` functions are gone. The
  writer's `RoomsHelper.buttonToChangeInvolvement` stays for `users/profiles/_membership` (4.7).
- **Whitespace (V4):** a space (`Html.sp`) between every two inline siblings the ERB separates (the nav's name, settings link and bell; each icon and its label; the label's input
  and spans; the stream source and the jump button; the composer's search link and frame; consecutive turbo streams of a refresh), and before `lexxy-prompt` (the editor's script
  puts its toolbar in front of it, as in 4.4). Boolean attributes the ERB writes bare (`contents`, `multiple`, `hidden` on the autocomplete select) are `Attr.create "x" ""`.
- **Tests:** `RoomsViewsTests` (goldens `rooms_*`) and the `rooms/*` cases of the frozen oracle, hostile text included, are re-pointed and loose-equal; the oracle answers the Falco
  pages by their text without fragment offsets (the text before the first message is Falco's, not the writer's: class A). `RoomsViewsTests` "show keeps every message as a
  fragment" checks the fragments in both layouts. P2 now builds forty distinct fragments (a fragment is placed once per page; repeating one is an O4 error) and stays under
  300 KB. `RoomsTests` "show renders the room" reads `<meta ... />` (void elements end ` />`, class A).
- **Gates run:** lint self-test ok (18 planted), lint clean (228 files); `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on the final tree (Release, warnings as errors): **979 total, 973
  passed, 6 skipped (the same environmental 6), 0 failed**; H1 green. `bin/pixel-parity --fast` on the image of `1ff63d5`: `rooms/**`, `interactions/composer**`,
  `interactions/mention_autocomplete**`: default 100/100 screenshots pixel-equal, 100/100 aria, 100/100 loose; crowd 8/8, custom_styles 4/4, restricted 2/2, first_run 0/0 on all
  three layers (441 s, first run, no fix needed). `realtime/removed_from_room`, `room_deleted_then_send`, `typing/one` one at a time: 2/2 equal on all three layers each (36-78 s).
  `bin/clean-parity` between runs, `bin/clean-docker` after the builds.
- **Speed (`bench/quick`, room page and post a message, 3 reps, interleaved new/ctl/ctl/new/new/ctl via `bench.sh`; control = `campfire-dotnetway:u45ctl` built from the base `2552c6f`
  by `build-ctl.sh`, labels checked, image removed after; host probe ratio 1.046-1.091 over 12 probes, Mac load average 3.2-3.8 from other sessions (`loads.txt`), 0 non-2xx and
  0 connection errors in 12 runs per side):**

| workload | c | control req/s | new req/s | x Rails (new) | x Rails (control) | CPU us/req ctl -> new |
|---|---:|---:|---:|---:|---:|---|
| room page | 16 | 7,590 | 7,287 | 30.45 | 31.72 | 435 -> 446 |
| room page | 1 | 2,399 | 2,347 | 21.91 | 22.40 | 513 -> 522 |
| post a message | 16 | 1,982 | 2,013 | 6.10 | 6.00 | 1,515 -> 1,473 |
| post a message | 1 | 1,119 | 1,140 | 6.83 | 6.71 | 1,659 -> 1,643 |

  No cell is under 5x (thinnest: post c=16 6.10x, the control's own 6.00x in this session). The room page costs **4.0% of throughput at c=16 and 2.2% at c=1** (+11 and +9 us of
  CPU per request): the nav, composer, bell, invitation and message-area chrome are now Falco nodes rendered to a string and split at the slots on every request (the
  layout regions, the writer partials for the notification help as `Legacy.node` strings) where the writer copied literals; the design predicted "no change beyond noise" for the
  room page, and the control's c=16 range (6,383-7,642) shows how noisy it is, but the median moved the same way at both concurrencies. Post a message is within +1.6% to +1.9%
  of the control (noise: it renders no room chrome). The room page is at 30x, six times the floor, so nothing was optimised; the next cheap win, if one is wanted, is to cache the
  static composer and bell nodes per room. Decoded bytes: the room page is **376,936 bytes against Rust's 416,122 (-9.4%)**, and `bench/quick`'s "decoded bytes equal Rust's" check
  prints FAIL for `room_show` (the control, 378,036, fails it too, since 4.4); it is the writer's byte equality and changes with the phase (see 4.4).
- **Differences recorded:** none for the README (the design's one class A entry is written in 4.11). Visible, class A: the empty `rooms/refreshes/show` is an empty body (Erubi
  wrote a newline), void elements end ` />`, the room page is 1,100 bytes shorter than the control's.
- **KLOC:** `bin/kloc` 34,418 by upstream's rules, **34,637 corrected (34.6 KLOC)**, from 34,482 (+155): `Campfire.Views` 5,997 corrected from 5,800 (+197), App 11,846 (-42: the
  rooms controllers no longer carry a `RenderSize` each, and `Rooms.fs` lost the writer's delete button). The 20 templates were 656 non-blank, non-comment lines as literal tables and are 738 as Falco, plus 132 lines of new `Markup/` helpers
  (counted with grep, not cloc); as in 4.4 the shrink comes in 4.11 (deleting `Out.fs`, `Tag.fs` and the writer halves of the helpers).
- Evidence: `bench/results/dotnetway-4-5/` (`new/`, `ctl/`, `bench.sh`, `build-ctl.sh`, `pixel.sh`, pixel and bench logs, `loads.txt`, `verify.log`). 26 GB free after `bin/clean-docker`.

### Phase 4, unit 4.6: Sidebar, search, mentions (2026-10-21)

- **Built:** the 10 templates of the row are Falco.Markup functions: `users/sidebars/show` (`Show.rendered ctx sidebar : Layouts.ViewPage`) with `rooms/_direct`, `_shared` and
  `_direct_placeholder` (`Templates/Users/Sidebars/Rooms/`); `searches/index` (`Index.rendered`) and `_recents`; `autocompletable/users/index` and `_prompt_item`; `users/_mention`
  and `users/autocompletables/_template`. New helpers: `Markup.Users.avatarTag` and `sidebarFrameWith` (`sidebarFrame` is the empty one), `Markup.Rooms.linkToRoom` (the caller's
  attributes before its first `data-*`, Rails' four defaults, its `data-*`, the rest, then `href`). The sidebar and the search page go out through `Pages.viewPage` (the writer's
  layouts until 4.10); the direct room fragment is `HtmlOutput.toFragment (HtmlOutput.ofNode ...)` inside `UsersCached`; the shared-room broadcast partial is the node's text;
  the mentions prompt is `HtmlOutput.ofNodes`. The direct room fragments (and the shared rooms) are separated by `Html.sp` in the page, where Rails' collection leaves whitespace
  between inline `a`s; the fragment itself has none. `RenderSize` fields of the two controllers are gone.
- **Found by `bin/pixel-parity`, not by the oracle or goldens:** a search result's `.message__room` is a block with a `::before` arrow (`sidebar.css`), and the ERB's line break
  between the span and its link renders after the arrow: Rails' heading reads "JZ 3:30 PM → All Talk", ours read "→All Talk" (6 cells, 256-14,000 pixels, aria +1/-1 each, loose
  DOM equal because it drops whitespace at an edge with no sibling before it). The message partial (4.4) had never met a search slice. Fixed with one `Html.sp` before the link
  (`_Message.fs`, commit after the template one); the slice was then 19/19 on the default seed and 2/2 on custom_styles.
- **Tests and oracle:** `ParityATests` (sidebar_david, sidebar_kevin, sidebar_frame, shared rooms, mention, autocompletables template, autocompletable users), `SearchesViewsTests`
  (goldens `searches_index`, `searches_index_empty`) and the oracle runner (`Operations.fs`, `Pages.fs`) are re-pointed; the sidebar, search, autocompletable and mention cases of
  the frozen oracle (hostile text included: 41, 17, 33, 16 and 8 cases) are loose-equal. The `users/direct_room` oracle case (one render per membership version) is unchanged.
- **Gates run:** lint self-test ok, lint clean; `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on the template commit: **979 total, 973 passed, 6 skipped (the same environmental 6), 0 failed**,
  H1 green; after the `_Message.fs` line `Campfire.Views.Tests` 133/133 and `bin/verify` again (below). `bin/pixel-parity --fast` on the image of the fix commit:
  `interactions/sidebar**`, `interactions/phone_sidebar`, `search/**`, `autocompletable/**`: default 19/19 screenshots, 19/19 aria, 19/19 loose; custom_styles 2/2 on all three layers
  (crowd, first_run, restricted: 0 cells); `realtime/unread_in_sidebar`, `new_room_in_sidebar`, `presence_read` one at a time: 2/2 on all three layers each (36-40 s). The first run
  (before the fix, image of the template commit) is kept in `first-run/`. `bin/clean-parity` between runs, `bin/clean-docker` after the builds.
- **Speed (`bench/quick`, sidebar and search, 3 reps, interleaved new/ctl/ctl/new/new/ctl via `bench.sh`; control = `campfire-dotnetway:u46ctl` built from the base `18aad84` by
  `build-ctl.sh`; host probe ratio 1.038-1.079 over 12 probes, Mac load average 2.6-4.7 from other sessions (`loads.txt`), every response 2xx, 0 connection errors):**

| workload | c | control req/s | new req/s | x Rails (new) | x Rails (control) | CPU us/req ctl -> new |
|---|---|---:|---:|---:|---:|---|
| sidebar | 16 | 7,698 | 7,270 | 12.14 | 12.86 | 435 -> 455 |
| sidebar | 1 | 2,328 | 2,373 | 8.28 | 8.12 | 524 -> 516 |
| search | 16 | 7,252 | 7,135 | 17.49 | 17.78 | 471 -> 478 |
| search | 1 | 2,155 | 2,140 | 11.11 | 11.18 | 558 -> 564 |

  No cell is under 5x; the thinnest is sidebar c=1 at 8.28x (the control's 8.12x; the design predicted -2.4%, and the c=1 median is +1.9%, within the control's own range
  2,318-2,392 against 2,254-2,492). Sidebar c=16 is -5.5% of the control (+20 us of CPU per request), but the ranges overlap (control 7,571-7,952, new 6,853-7,686) and the cell is at 12x.
  Search is within 1-2% of the control at both concurrencies. Nothing was optimised. Decoded bytes: sidebar 29,950 against Rust's 30,763 (-2.6%), search 136,873 against
  149,623 (-8.5%; the control's 137,286); `bench/quick`'s "decoded bytes equal Rust's" check prints FAIL for both (the control fails it for search since 4.4) and changes with the phase (see 4.4).
- **Differences recorded:** none for the README (the design's one class A entry is written in 4.11). Visible, class A: the direct room fragment carries no leading or trailing
  whitespace (Rails' "\n  <a>...</a>\n"), `contents` is `contents=""`, void elements end ` />`, the empty-search page and the prompt items have no trailing newline.
- **KLOC:** `bin/kloc` 34,447 by upstream's rules, **34,666 corrected (34.7 KLOC)**, from 34,637 (+29): `Campfire.Views` 6,042 corrected from 5,997 (+45), App 11,830 (-16: two `RenderSize`
  fields, three page calls shorter). The 10 templates were literal tables and are Falco nodes plus 37 lines of new `Markup/` helpers; as in 4.4 and 4.5 the shrink comes in 4.11.
- Evidence: `bench/results/dotnetway-4-6/` (`new/`, `ctl/`, `bench.sh`, `build-ctl.sh`, `pixel.sh`, `first-run/`, pixel and bench logs, `loads.txt`, `verify.log`). 23 GB free after `bin/clean-docker`.
- **Next:** the Opus checkpoint after this unit (design-views.md section 4): review of 4.1-4.6, then 4.7 Users.

### Phase 4, checkpoint after 4.6: Opus review (2026-10-21) - GO for 4.7, with notes

- **Reviewed** against `plans/design-views.md` and D1-D9: the diff `b632a7c..36ac7e2` (19 commits), the lint and its planted files, `Html`, `HtmlOutput`
  and the slot rule, `Fragments` (keys, the 7-day options, `SizeLimit`, `DisableDistributedCache`, immutability, only `Fragments.fs` calling
  HybridCache), `Legacy`, the moved sidebar/search/mention templates against their ERB, the evidence directories of 4.2-4.6 (pixel logs, the bench
  `vs-stored.md` and controls, `verify.log`), and the unit entries above. The gates each unit claims are in its evidence directory with the counts it
  reports. No template fetches a fragment (`renderNode`/`renderHtml` are called only in `HtmlOutput.fs` and `Legacy.fs`; no `.Result` in Views). Every
  bench cell of 4.2-4.6 is over 5x against a same-session control; the thinnest are post c=16 6.10x (4.5) and sidebar c=1 8.28x (4.6).
- **Fixed (`3195579`):** (1) **three lint bypasses**, each confirmed by rendering in `dotnet fsi`: Falco.Markup 1.4.0's `Falco.Markup.Html` module is
  `AutoOpen`, so `_a [ _href_ v ] [ _text v ]` wrote a raw attribute and raw text with no `Attr.`/`Text.` for the rules to see; `open Falco.Markup.Attr`
  then `create "href" v`, and `module T = Falco.Markup.Text` then `T.raw v`, hid the qualified name (Falco's modules are not `RequireQualifiedAccess`);
  `ParentNode`/`SelfClosingNode` take a raw tag. New rules `falco-dsl` (the `_x`, `_x_`, `_x'` spellings, `Templates.html5`/`svg`) and `falco-alias`
  (opening or abbreviating Falco's `Attr`, `Text`, `Elem`, `Html`, `Templates`, `Svg`), the two cases added to `node-ctor`; 9 planted files (27 now),
  the clean file opens `Falco.Markup`, `System.Text` and the project's `Templates` namespace. No source file used any of them (228 files clean).
  (2) **The slot marker inside trusted HTML:** a `<` U+E000 digits `>` in `Html.trusted` text is read as a slot, and `HtmlOutput` raises on a slot it was
  not given, so every page holding it would answer 500. Rich text is safe today only because the sanitizer's serializer escapes `<` in attribute values
  (`sanitizeWithEscapedAttributeBrackets`); the account's custom CSS (raw in a `style` element, admin-written) meets the split when the layout moves in
  4.10 and would have taken down every page including the custom-styles form. `Trusted.ofSanitized` now writes U+E000 as `&#57344;` and
  `ofAccountStyles` as the CSS escape `\E000 ` (the same character in their contexts); new O4 test.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `36ac7e2` (before the fixes): lint self-test ok (18), lint clean (228), 979 total, 973 passed,
  6 skipped, 0 failed; on `3195579`: lint self-test ok (27), lint clean (228), **980 total, 974 passed, 6 skipped (the same environmental 6), 0 failed**,
  H1 green. Image rebuilt from `3195579` (revision label checked), `bin/pixel-parity --fast --only 'rooms/show/**,search/**,interactions/sidebar**'`:
  default 54/54, custom_styles 6/6 (crowd, first_run, restricted 0 cells) screenshots, aria and loose DOM equal (236 s); `realtime/message_sent` alone
  2/2 on all three layers (35 s). `bin/clean-parity` after each, `bin/clean-docker` after the build; 25 GB free. No bench: the fix is a scan for one
  character in two makers (rich text renders once per message version; the styles are not on the request path until 4.10).
- **Notes for 4.7-4.11:** see the checkpoint's return (also below in short). 4.10 is the unit most likely to exceed 90 minutes (the layout, the page-module
  collapse, `--fast` on every state, the five-workload bench): split it as the design's risk 14 says (layout first, the collapse second). Write the
  asset tags as one `Trusted.ofAssetTags` string (S4: rebuilding 30 stylesheet links as nodes costs 16.8 us per page). `Legacy.inLayout` renders and
  splits the nav, footer and sidebar regions for a Turbo-Frame request and then drops them; `Layouts.page` should render only the head and content for a
  frame. The KLOC drop is all in 4.11 (templates grew 613 -> 641, 656 -> 738 lines): record the result against the design's 3.40 honestly.
- **KLOC:** `bin/kloc` 34,449 by upstream's rules, **34,668 corrected (34.7 KLOC)** (+2: `Html.fs`).
- Evidence: `bench/results/dotnetway-4-6/checkpoint/` (`build.sh`, `pixel.sh`, logs, `STATUS-*`).

### Phase 4, unit 4.7: Users (2026-10-21)

- **Built:** the 9 templates of the row are Falco.Markup functions: `users/show` (`Show.rendered ctx user transferId : Layouts.ViewPage`), `users/new` (`New.rendered`), `users/_ban_button`,
  `users/profiles/show` (`Show.rendered`), `_membership`, `_transfer`, `users/push_subscriptions/index` (`Index.rendered`) and `_push_subscription`, and the initials avatar
  `users/avatars/show.svg` (`Show.render userId initials : XmlNode` made of `Elem.create "svg"` elements, initials through `Html.text`; the avatars controller writes it with
  `HtmlOutput.ofNode`). The four pages go out through `Pages.viewPage` (the writer's layout until 4.10); `RenderSize` fields of the four controllers are gone. New helpers:
  `Markup.Forms` fields (`textField`, `emailField`, `passwordField`, `fileField`, `textArea`: options, then `type`, `value`, `name`, `id` as `Tags::TextField` does; the multipart
  `enctype` is a form option the template passes, a `maxlength` is written with its own `size`; `textArea` opens with `Html.nl`), `Markup.Translations` (`translationButton`,
  `translationsFor`), `Markup.Application.linkBack`, `linkToZoomQrCode`, `buttonToCopyToClipboard`, `webShareSessionButton`, `Markup.Links.mailTo`, `Markup.Users.buttonToDirectRoomWith`
  and `profileFormSubmitButton`. Per the checkpoint note: the writer's `RoomsHelper.buttonToChangeInvolvement` is deleted, the profile's `_membership` and the differential's
  `button_to_change_involvement` op use `Markup.Rooms.buttonToChangeInvolvement`. The Falco translation button also replaced the `Legacy.node` calls in `rooms/layouts/_form` and
  `rooms/show/_invitation` (4.5), which opening `Markup` after `Helpers` forced anyway (`Translations.x` resolves to the later module). The profile still places `pwa/_install_instructions`
  and the join page `accounts/_help_contact` with `Legacy.node` (4.9 and 4.8 own them).
- **Whitespace (V4) cases worth knowing:** `_transfer`'s three action buttons write their screen-reader label before the icon (the ERB block starts with the span), so the
  space sits between label and icon; the label before the input needs a space only in the branch where it is a `label` (the other branch is a block `div`); the push
  subscription's two heading spans are separated by a space. All three were found by the golden or oracle comparison, none needed the browser.
- **Tests and oracle:** `ParityATests` (users new, show x6, profile x10, push subscriptions, avatars x2, ban button), the oracle runner (`Rest.fs`, `Pages.fs`, `Operations.fs`) are re-pointed
  and the avatar goldens and the oracle's `users/avatars/show` cases are compared loose, not as text (Falco writes no indentation: class A); the users, profile, push-subscription,
  ban-button and avatar oracle cases (hostile text included) and the `users_show_*`, `profile_*`, `users_new`, `ban_button_*`, `push_subscriptions` and `avatar_*` goldens are loose-equal.
  `PresenterAccountsTests` looks for `>D</text>` in the SVG instead of the ERB's indentation. New `UsersViewsTests.fs` (3 tests): T1 on the profile's bio `textarea` (newline,
  `</textarea>`, entity, U+E000), the profile forms' field names and ids (both file fields keep Rails' shared `id="file"`, in multipart forms, the user's name encoded as data), and the
  SVG well-formed as XML for hostile initials (`<`, quotes, `&`, non-ASCII, three characters).
- **Gates run:** lint self-test ok (27), lint clean (229 files); `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **983 total, 977 passed, 6 skipped (the same environmental 6), 0 failed**, H1 green
  (the first full run had `FrontAcmeTests.cached certificates load and are forgotten by obtaining` fail once under load; it passed alone and in both later full runs).
  Image rebuilt from the template commit (`4b2dc6f`, revision label), `bin/pixel-parity --fast`: `users/**`, `interactions/pwa_install**`, `interactions/notifications_help`: default 34/34,
  custom_styles 2/2 (crowd, first_run, restricted 0 cells) screenshots, aria and loose DOM equal (127 s); beyond the row, because the new helpers and the translation button reach them:
  `auth/join**`, `pwa/install/**`, `interactions/translation_menu`, `rooms/opens/**`, `rooms/closeds/**`: default 40/40, crowd 8/8, restricted 2/2 on all three layers (163 s). No `realtime/**`
  state renders these pages. `bin/clean-parity` after each run, `bin/clean-docker` after the build; 24 GB free.
- **Speed:** no `bench/quick`: the unit moves no page of the five workloads (the profile, person, join and push pages and the avatar SVG, which the avatar cache keeps on disk per user).
  The five workloads' templates are unchanged since 4.6.
- **Differences recorded:** none new for the README (class A as in 4.4-4.6: no whitespace where it does not render, numeric references for non-ASCII and `'`, ` />` on voids); the SVG
  avatar is no longer byte-identical to Rails' (no indentation or line breaks), the same picture.
- **KLOC:** `bin/kloc` 34,458 by upstream's rules, **34,677 corrected (34.7 KLOC)**, from 34,668 (+9): `Campfire.Views` 6,081 corrected from 6,042 (+39: 710 -> 643 lines of templates,
  against about 140 lines of new `Markup/` helpers: the form fields, translations, QR/copy/share buttons), App 11,802 (-28 lines: four `RenderSize` fields, four calls shorter). As in 4.4-4.6 the
  shrink comes in 4.11.
- Evidence: `bench/results/dotnetway-4-7/` (`build.sh`, `pixel.sh`, `build.log`, `pixel-users.log`, `pixel-extra.log`, `verify.log`, `STATUS-*`).
- **Next:** 4.8 Accounts (`accounts/edit`, `_help_contact`, `_invite`, `users/_user`, `_next_page_container`, `index.turbo_stream`, `bots/*`, `custom_styles/edit`).

### Phase 4, unit 4.8: Accounts (2026-10-21)

- **What moved (`7a5bfb5`):** all 11 templates of the row are Falco.Markup functions: `accounts/edit` (nav, content, footer regions), `_help_contact`, `_invite`, `users/_user`,
  `_next_page_container`, the `index.turbo_stream` (an `output` for `Pages.render`; the two streams are custom elements, so the ERB's blank line between them is an `Html.sp`), the five
  `bots/*` (`_bot`, `_form`, `index`, `new`, `edit`) and `custom_styles/edit`. The five pages return `ViewPage` through `Pages.viewPage` (four `RenderSize` fields and four `framedPage`
  calls gone from the controllers); the writer's layout stays until 4.10. `_help_contact` and `_invite` are nodes now, so the writer's sign-in page places the first with
  `Legacy.write`, and the join page and the room invitation lose their `Legacy.node` calls.
- **Helpers:** `Markup.Forms.urlField`, `hiddenField` and `checkBox` (the hidden unchecked field, then the checkbox; `disabled` goes to both as Rails does), `Markup.Application.versionBadge`.
  A model-less `form_with` names a field after its method alone, so `nameAndId` takes an empty object name that way; the oracle's `accounts/bots/_form` cases include model-less ones
  and stay in force (`_Form.render ctx objectName bot`: the pages pass "user"). `Forms.buttonTag` is `button_tag` (`name="button"`): the account name and custom styles submit buttons
  need it, the bots' `profileFormSubmitButton` does not.
- **Bare attributes:** `readonly` and `checked` are bare where the ERB writes them bare (`Attr.readonly`, `Attr.checked'`); the loose layer compares `checked` and `checked="checked"` as different.
  Both were found by the oracle, not the browser.
- **Whitespace (V4):** the oracle and goldens found no slip beyond the two buttons above; the rule used throughout: a space between two inline siblings (links, buttons, labels, inputs,
  images, spans, custom elements) and none beside a block, a hidden input or at an element's edge.
- **Security:** every attribute from data goes through `Html.attr`; no `Text.raw` takes a non-literal. URLs from data in attributes, for the gate's XSS review: the bot form's avatar
  `src` (`AvatarAttachmentUrl`, a storage route built by `Accounts.botForm`), the account logo `src` and the logo delete form's `action` (`ctx.Account.LogoUrl`, a route), the bot's
  webhook `value` (a user-supplied URL, but an input's value, not a link), the invite and curl URLs (`ctx.Url` of routes, as `value` and `data-*-url-value`), and the help contact's
  `mailto:` href (the owner's name and address, encoded by `Html.attr`).
- **Tests:** `ParityATests` (account edit x7, bots, custom styles x2, users turbo stream), the oracle runner (`Rest.fs`, `Operations.fs`) re-pointed; the account, bots, custom styles,
  help contact, invite, `_user`, `_next_page_container` and turbo-stream oracle cases (hostile text included) and the `account_*`, `bots_*`, `custom_styles_*` goldens are loose-equal.
  New `AccountsViewsTests.fs` (3 tests): **T1** on the custom styles textarea (leading newline, `</textarea>`, an entity, U+E000), the bot form's name and webhook encoded as data with the
  multipart form and file field names, and a person's name as text in `_user` (no `img[onerror]`).
- **Gates run:** lint self-test ok, lint clean (229 files); `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **986 total, 980 passed, 6 skipped (the same environmental 6), 0 failed**, H1 green.
  Image rebuilt from `7a5bfb5`: `bin/pixel-parity --fast --only "account/**,auth/join*"`: default 18/18, crowd 4/4, custom_styles 6/6, restricted 2/2 (first_run 0 cells) screenshots, aria and loose
  DOM equal (132 s). Beyond the row, because the unit's partials reach them: `auth/sign_in*`, `welcome/**` (the help contact): default 4/4 (36 s); `rooms/show/original_with_invitation*`
  (`_invite`): default 2/2 (35 s). `bin/clean-parity` after each, `bin/clean-docker` after the build; 24 GB free.
- **Speed:** no `bench/quick`: the unit moves no template of the five workloads (the account, bot and custom styles pages, the people stream and the help contact; `_invite` only appears on
  the first room's invitation, which the room-page workload does not render). The five workloads' templates are unchanged since 4.6.
- **Differences recorded:** none new for the README (class A as before).
- **KLOC:** `bin/kloc` 34,352 by upstream's rules, **34,571 corrected (34.6 KLOC)**, from 34,677 (-106): `Campfire.Views` 6,011 corrected from 6,081 (-70: 785 -> 668 lines of templates, plus about 40
  lines of new `Markup/` helpers), App 11,766 from 11,802 (-36). The drop is larger here than in 4.4-4.7 because these templates carry no writer fragments to keep.
- Evidence: `bench/results/dotnetway-4-8/` (`verify.log`, `build.log`, `pixel-accounts.log`, `pixel-extra.log`).
- **Next:** 4.9 Sessions, first run, PWA, welcome (`sessions/new`, `transfers/show`, `incompatible_browser`, `first_runs/show`, `pwa/*`, `welcome/show`).

### Phase 4, unit 4.9: sessions, first run, PWA, welcome

Built on `dotnetway` from `d8575fc`, one commit (`beba312`) plus this entry.

- **Templates:** `sessions/new`, `sessions/transfers/show`, `sessions/incompatible_browser`, `first_runs/show`, `welcome/show` and `pwa/_install_instructions`, `_browser_settings`,
  `_system_settings` are Falco.Markup functions; the five pages return `ViewPage` through `Pages.viewPage` (four `RenderSize` fields and four `framedPage` calls gone from the
  controllers; `Pages.incompatibleBrowser` loses its own size and uses the new `Pages.viewPageInAnyFormat`); the writer's layout stays until 4.10. `pwa/manifest.json` is
  `Manifest.render : byte[]` on `Utf8JsonWriter` (indented, `UnsafeRelaxedJsonEscaping`, the port's key order: the controller writes the bytes, `Render.plain` is gone from it);
  `service_worker.js` stays a string constant (its bytes now by `Encoding.UTF8`). The profile page and the room bell place the PWA partials as nodes (their `Legacy.node` calls are gone).
- **`Html.unclosed`** (`Core/Html.fs`, the lint's one exempt file): the transfer page is a block-less `form_with`, and the reference's DOM has the layout's footer inside the open form. A closed
  Falco form puts the footer beside it, which the loose layer reports, so the helper renders the node and drops its end tag (it refuses anything that is not that element).
- **Whitespace and glyph slips the oracle and goldens caught** (all fixed, none by the browser): the empty `<em aria-label="the Apple menu">` of the macOS steps holds U+F8FF (the Apple
  logo) in the ERB, so without it the loose DOM lacks an empty text node (3 places); a space between `strong` and `span` in each browser of the incompatible-browser list (a flex column, but
  two inline siblings for the loose rule).
- **Helpers:** none new in `Markup/` (the sign-in and first-run forms use `Forms.emailField`/`passwordField`/`textField`/`fileField` with an empty object name for the model-less sign-in
  form, `Translations.translationButton`, `Users.accountLogoTag`, `Application.screenReader`, `Users.sidebarFrame`).
- **Security:** every attribute from data goes through `Html.attr`; no `Text.raw` takes a non-literal (the lint is clean over 229 files). URLs from data in attributes, for the gate's XSS review:
  the sign-in form's `action` (`ctx.Url` of a route), the first-run form's `action` (a route), the transfer form's `action` (the request's own path, `Respond.rawPath`, encoded by `Html.attr`),
  the sign-in email as an input's `value` (user-supplied, an input's value, not a link), the help contact's `mailto:` (owner data, 4.8), the account logo `src` (a route), and `Html.text
  ctx.Url "/"` in the browser-settings steps (text, not an attribute). No user-supplied URL becomes a link.
- **Tests:** `ParityATests` (pwa partials x30 cases (3 partials x 10 user agents), welcome, layout wrapper, flash, sessions x4 + 2 + 1, first run, manifest), the oracle runner (`Rest.fs`, `Operations.fs`, `Pages.nodeBytesAnswer`)
  re-pointed; the `sessions_*`, `first_run`, `incompatible_browser*`, `install_instructions_*`, `browser_settings_*`, `system_settings_*`, `welcome` goldens and every `sessions/`, `first_runs/`,
  `pwa/` and `welcome/` oracle case (hostile text included) are loose-equal; the manifest is JSON-equal (golden and the 13 oracle cases: `Facts.jsonEqual`, key order included; `pwa/manifest`
  left the oracle's exact-text list). New `SessionsViewsTests.fs` (8 tests): the sign-in email as attribute data (and the `shake` class), the transfer form left open with its action encoded and
  the footer inside it, `unclosed`'s refusal, the welcome name as text, the incompatible-browser title per platform, the first-run form (multipart, `user[...]` names), the PWA partials per
  platform (empty on iOS Safari/Chrome and for Chrome's install steps; a browser name with markup is text), and the manifest's key order and escaping.
- **Gates run:** lint self-test ok, lint clean; `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **994 total, 988 passed, 6 skipped (the same environmental 6), 0 failed**, H1 green.
  Image rebuilt from `beba312`: `bin/pixel-parity --fast --only "auth/**,welcome/**,pwa/**,errors/**,interactions/translation_menu"`: default 66/66, custom_styles 2/2, first_run 8/8
  (crowd and restricted 0 cells), screenshots, aria and loose DOM all equal (270 s). Beyond the row, because the partials reach them: `interactions/notifications_help`, `users/**`,
  `rooms/show/original_with_invitation*`, default 34/34 equal (96 s). `bin/clean-parity` after each, `bin/clean-docker` after the build; 21-24 GB free throughout.
- **Speed (`bench/quick --reps 2 --routes room_show`, `bench/results/dotnetway-4-9`):** this unit's one hot-path touch is the room page's bell (`rooms/involvements/_Bell` in the nav), whose
  three PWA partials were writer text through `Legacy.node` and are Falco nodes now. Micro-measured (3 UAs): 7.4 us (iOS Safari), 14.8 us (Chrome Mac), 21.1 us (Safari Mac) and 22-31 KB per
  room page, against 494 us of CPU per request at c=1. Measured on the new image (host probe 1.05-1.07 before and after each run, 0/4 windows above 1.10, load 0.4-2.4 before): room page c=16
  7,507 req/s (436 us/req) = **31.4x Rails**, c=1 2,523 req/s (494 us/req) = **23.6x**, against 33.8x/25.1x at the Phase 3 gate. **No same-session control**: the difference (-7%, -6%) is
  the sum of units 4.4 to 4.9 (no unit since the Phase 3 gate measured the room page), of which this unit's bell is an estimated 3-4% by the micro-measure, not separable by a
  control inside run-to-run noise. Both cells are far above the 5x floor; the thin cells (post, sidebar) are untouched by this unit (no template of theirs changed). The bell's three
  partials depend only on the platform and the base URL, so a per-platform fragment would remove the 7-21 us if the room page ever needs it; not done, the floor does not ask for it.
  `bench/quick`'s "decoded bytes equal Rust's" check prints FAIL for the room page (376,810 against 416,122) on purpose and exits 1; 4.11 replaces it with a stored size per route.
- **Differences recorded:** none new for the README. Class A as before: the manifest's indentation and its escapes (JSON-equal to Rails'; the values are JSON strings, not ERB-escaped,
  as before this unit); an `<em>` Apple glyph and the PWA steps are loose-equal, not byte-equal.
- **KLOC:** `bin/kloc` 34,446 by upstream's rules, **34,665 corrected (34.7 KLOC)**, from 34,571 (+94): `Campfire.Views` 6,131 corrected from 6,011 (+120), App 11,740 from 11,766 (-26). These
  templates grew: the writer's templates were a few `Utf8.lit` strings and a call per hole (the PWA partials were 300 lines of literals), the Falco ones spell out every element. The KLOC drop
  is 4.11's (the writer, `Out.fs`, `Tag.fs` and the `Helpers/` halves go there); the design's 3.40 KLOC target is judged then.
- Evidence: `bench/results/dotnetway-4-9/` (`verify.log`, `build.log`, `pixel-unit.log`, `pixel-extra.log`, `bench-room.log`, `vs-stored.md`, `report.md`).
- **Next:** 4.10 Layouts (layout first, then collapse the page modules; the checkpoint notes: `Trusted.ofAssetTags` as one string, a Turbo-Frame request renders only head and content,
  custom styles only through `Trusted.ofAccountStyles` with the hostile-CSS test, `--fast` on every state, the five-workload bench).


### Phase 4, unit 4.10: layouts

Built on `dotnetway` from `77838a9`, two code commits (`87157d4` the layouts, `7fadcde` one template the gate found) plus this entry. Done in one pass: the layout and the
collapse of the page modules are both in, because the page modules already returned `ViewPage` (`rendered`) after 4.4-4.9, so the "collapse" was the deletion of what stood
behind it, not a rewrite.

- **Layouts** (`Templates/Layouts/Application.fs`, `_Lightbox.fs`, `TurboRails/Frame.fs`, `Layouts.fs`): `Layouts.application ctx regions` is the application layout (`HtmlOutput.ofDocument`, so
  `<!DOCTYPE html>`), `Layouts.frame regions` turbo-rails' frame layout (`ofNode`, no doctype, **only `Head` and `Content`**: nothing else is built or rendered for a Turbo-Frame request,
  where `Legacy.inLayout` rendered every region and dropped three), `Layouts.page ctx framed viewPage` picks one; `Layouts.ViewPage.content nodes fragments` is a content-only template.
  The head's tags are Falco nodes; the stylesheet and import map tags are one `Html.Trusted` each (`ViewContext.StylesheetTags`/`ImportmapTags` are `Html.Trusted` now, made once per
  process by `ViewContextBuilder.stylesheetTrusted`/`importmapTrusted`, the only file the lint allows `Trusted.ofAssetTags` in), so a page writes two strings, not 30 link nodes (S4).
  New in `Markup.Application`: `pageTitleTag`, `currentUserMetaTags`, `scriptAwareActionCableMetaTag`, `customStylesTag`, `bodyClasses`.
- **Deleted:** `Core/Legacy.fs` (`inLayout`, `write`, `node`: the seam had no writer template left on either side, so it goes now rather than in 4.11) and `Trusted.ofLegacy`,
  `Templates/Layouts/ApplicationWrapper.fs`, the writer's `Layouts.Application`/`Layouts.frame`, `RenderSize`, the writer's layout helpers in `Helpers/Application.fs`
  (`pageTitleTag`, `currentUserMetaTags`, `scriptAwareActionCableMetaTag`, `customStylesTag`, `writeBodyClasses`, `bodyClasses`), `Pages.framedPage`/`content`/
  `contentInApplicationLayout`. `Pages`: `page` (lookup, frame choice), `pageInAnyFormat` (no lookup), `pageInApplicationLayout` and `applicationPageInAnyFormat`
  (`MessagesController`'s own layout, and the incompatible-browser filter there), `bare`. The six controllers that placed a content-only template through the writer now return
  `Layouts.ViewPage.content`.
- **Custom styles:** only `Trusted.ofAccountStyles`, called only in `Markup/Application.fs`. **Known difference (new, for the README in 4.11):** it writes a `</style` (any case) as `<\/style`.
  Rails writes the account's CSS `html_safe`, so a `</style>` typed into the custom styles ends the element and the rest is markup; CSS has no use for the sequence and the account's
  own pages are the only place it would show, but the 4.10 checkpoint asks that hostile CSS lands inside the element, so it does. U+E000 stays a CSS escape (`\E000 `, 4.1).
- **Whitespace** (pixel-parity's and the oracle's loose layers found each; the browser did not): a space at both edges of each region the ERB puts on a line of its own (nav, content,
  footer, sidebar, and the head's end, the frame body's both edges), because the oracle's cases put text there; three spaces in the lightbox (between the image, the form and the two
  links and the button), the last found by `sessions_transfer`, whose open form makes the parser drop the lightbox's own form; the editor `template` of the mention prompt item has a space on
  each side of the mention (`autocompletable/users/_prompt_item`, a 4.5 template: the live DOM after a mention is inserted lacked the trailing one, found by
  `interactions/mention_autocomplete/inserted`, which no unit since 4.5 had run).
- **Tests:** `LayoutsTests.fs` (5): O3 (the frame layout holds exactly `Head` and `Content` as a DOM, no doctype, no nav/footer/aside/dialog; the application layout starts `<!DOCTYPE html>`
  and holds every region, the lightbox and the logo), fragments stay fragments and are checked in both layouts, the asset tags are written as given in order, **hostile custom styles**
  (`</style><script>alert(1)</script><STYLE>`, `<`, U+E000 and a forged `<` U+E000 `0>` slot: one `style` element holds all of it, no script, no throw), and a flash is text in the live region.
  `HelpersTests` body classes, `HtmlOutputTests` O4 (both layouts), `HtmlTests` re-pointed; the oracle runner (`Operations.fs`, `Pages.fs`, `Inputs.fs`) answers the layout ops through the Falco
  layouts (`layouts/application_wrapper` as regions of trusted parts; `recorded/page` as fragments in a page, offsets no longer compared: they were the writer's). Deleted tests: the
  writer's pooled-buffer and `RenderSize` tests, the frame-layout writer test, the wrapper test. Every page golden (`golden/a`, `golden/b`) and all 1,106 oracle cases are loose-equal in full,
  head included.
- **Gates run:** `lint-markup` self-test ok (27 planted files), lint clean (227 files; the `ofAssetTags` allowlist is now `Presenters/ViewContext.fs`, `ofLegacy` is gone);
  `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `7fadcde`: **994 total, 988 passed, 6 skipped (the same environmental 6), 0 failed**, H1 green (`verify.log`).
  **Lean `--fast` pixel gate on every state** (`pixel-driver.sh`: the 25 slices of the Phase 3 gate, all seeds per slice, `realtime/**` one state at a time, Chromium, light, no breakpoint
  sweep): **401 cells, every one equal on screenshots, aria and loose DOM** (default 361, crowd 12, custom_styles 16, first_run 8, restricted 4). The first run, on the image of `87157d4`,
  had one slice red: `interactions` 77/79 loose (`interactions/mention_autocomplete/inserted`, both viewports, one missing `" "`); fixed in `7fadcde`, the image rebuilt, and `interactions`
  re-run on it, 79/79 on all three layers, `autocompletable` (fragment states, 0 cells) again. The other 23 slices ran on the image of `87157d4`; `7fadcde` changes one template that only the
  mention prompt reaches. The full lean matrix (all engines, dark, breakpoints) is the Phase 4 gate's, not this unit's. `bin/clean-parity` after each slice, `bin/clean-docker` after the builds;
  20-24 GB free throughout.
- **Speed (`bench/quick`, 3 reps each, new image `7fadcde` against a same-session control, the 4.9 code `beba312` as `campfire-dotnetway:p410ctl`, interleaved new/ctl/ctl/new/new/ctl; host probe
  1.04-1.08 before and after, 0/10 windows above 1.10 in every rep of both, load 1.15-1.46 before):**

| workload | c | new req/s | control req/s | x Rails (new) | x Rails (control) | CPU us/req new -> control |
|---|---:|---:|---:|---:|---:|---|
| room page | 16 | 7,354 | 7,808 | 30.7x | 32.6x | 440.8 -> 429.2 |
| room page | 1 | 2,515 | 2,506 | 23.5x | 23.4x | 493.5 -> 495.9 |
| messages page | 16 | 7,331 | 7,534 | 19.3x | 19.8x | 455.3 -> 450.6 |
| messages page | 1 | 2,308 | 2,318 | 10.6x | 10.6x | 529.9 -> 527.4 |
| sidebar | 16 | 7,599 | 7,999 | 12.7x | 13.4x | 429.4 -> 423.0 |
| sidebar | 1 | 2,447 | 2,445 | 8.54x | 8.53x | 503.1 -> 503.3 |
| search | 16 | 6,993 | 7,207 | 17.1x | 17.7x | 482.6 -> 474.2 |
| search | 1 | 2,204 | 2,205 | 11.4x | 11.4x | 548.8 -> 546.4 |
| post a message | 16 | 2,041 | 2,060 | 6.18x | 6.24x | 1,458.5 -> 1,455.6 |
| post a message | 1 | 1,145 | 1,160 | 6.86x | 6.95x | 1,637.5 -> 1,634.2 |

  **No cell under 5x** (thinnest: post c=16 6.18x, post c=1 6.86x, sidebar c=1 8.54x), all 2xx with 0 connection errors in 30 runs. The c=1 cells are within -1.3% to +0.1% of the control; the c=16
  cells are 1% to 6% lower (room page -5.8%, sidebar -5.0%, search -3.0%, messages -2.7%, post -0.9%) at +0.7% to +2.7% CPU per request: the new layout is built and rendered as
  nodes on every page (about 15 us of nodes and attributes, S4's 16.8 us less the link nodes), where the writer's was literal text, and a Turbo-Frame request now renders less. At c=16 the
  control's own reps spread 3-4% (room page 7,507-7,843), so the c=16 differences are about as large as the noise; the CPU column, which is steadier, says +1% to +3%. Nothing needed fixing against the
  floor. `bench/quick`'s "decoded bytes equal Rust's" check prints FAIL for the room page (376,570), messages page (345,884), sidebar (29,710) and search (136,633) on purpose and exits 1; 4.11 replaces it.
- **Differences recorded:** one new class-A/B item for the README in 4.11: custom CSS with a `</style` is written `<\/style` (above). Class A as before: attribute and text escaping of non-ASCII is Falco's
  (`&#233;`), the layout has no indentation, and the frame layout's `Content-Length` changes with it.
- **KLOC:** `bin/kloc` 34,317 by upstream's rules, **34,536 corrected (34.5 KLOC)**, from 34,665 (-129): `Campfire.Views` 6,032 corrected from 6,131 (-99: the writer's layout and lightbox as literal
  tables, `Legacy.fs`, `RenderSize`, the writer's layout helpers, against the Falco layout), App 11,710 from 11,740 (-30: `Pages.fs`' three writer entry points). The rest of the drop is 4.11's
  (the writer, `Out.fs`, `Tag.fs`, `Filters.fs` and the writer halves of the `Helpers/`); the design's 3.40 KLOC for the view layer is judged there.
- Evidence: `bench/results/dotnetway-4-10/` (`verify.log`, `build.sh`, `build.log`, `pixel-driver.sh`, `pixels/` (a log and `report.json` per slice, `interactions-rerun/`), `bench.sh`, `quick/`
  (`compare-new.txt`, `compare-ctl.txt`, `bench-*.log`, one JSON per rep)).
- **Next:** 4.11 Delete (`Core/Out.fs`, `Helpers/Tag.fs`, `Filters.fs`, the writer halves, `bin/views-differential` and the tools, `OutTests`/`TagTests`/`FormsTests`, AGENTS.md's view rules and README
  "Known differences" including the `</style` item; replace `bench/quick`'s decoded-bytes check with a stored size per route; `bin/views-differential bench` goes with the tool; KLOC against the design's
  3.40; the `Phase 5` note: whatever replaces the sanitizer keeps escaping `<` in attribute values or keeps the U+E000 rewrite in `ofSanitized`).

### Phase 4, unit 4.11: delete

- **Deleted:** `Core/Out.fs` (`Out`, `Utf8`, `SafeBytes`, `Render`; `RenderSize` and `Core/Legacy.fs` went in 4.10), `Helpers/Tag.fs`, `Filters.fs`, `Links.fs`, `Translations.fs`, and the writer halves of `Helpers/Forms.fs`
  (left: `sanitizeObjectName`, `sanitizeToId`), `Assets.fs` (`assetPath`), `Application.fs` (the text functions), `UsersHelper.fs` (words, colours, initials), `RoomsHelper.fs`, `Turbo.fs` (`domId`) and `Messages.fs`'
  `writeRoomDomId`. `bin/views-differential` (with its `bench` mode, which had not built since 4.2), `bin/views-test-parity`, `reference-tools/views/differential/` (Rust side, `generate.py`, `compare.py`,
  `golden.py`, the F# `Program.fs`/`Differential.fsproj`) and `transcribe.py`. **Kept:** `reference-tools/views/a` and `b` (the Rails golden generators the tests' comments cite). `Helpers/UnicodeTables.fs` was generated by the
  deleted Rust tool; its header says where to get it back from git.
- **Moved:** the F# case runners (`Inputs`, `Answer`, `Pages`, `Rest`, `Operations`) are `tests/Campfire.Views.Tests/Cases/`, the oracle's runner, re-pointed at `Markup.*` (`Answer.rendered`, `fragmentAnswer` and
  `recordedFragmentAnswer` are gone; `Inputs.attrs` makes Falco attributes). `Answer.Out` is `Body`. `OutTests`, `TagTests` and the writer cases of `FormsTests` are deleted (the two sanitizing tests stay).
- **The oracle lost 70 of its 1,139 cases** (`oracle.jsonl`): the ones that exercised only the writer's generic tag builder, which has no Falco counterpart to point at: `helpers/tag` 13, `image_tag` 8, `form` 13, `button` 13,
  `link` (`link_to`, `link_to_text`, `link_to_if`) 6 of 11, `turbo_frame_tag` 6, `link_to_room` 3, `avatar_tag` 8. The Falco helpers those became are covered by the template cases, the goldens and the loose layer (every
  `Markup.*` function is placed by a template the oracle or the goldens render). `helpers/translations` is compared as loose DOM now (Falco encodes the flags as references; it was an exact-text op). The `loose/oracle/`
  pin of loose.ts follows: 24 answers instead of 30 (the six picks that were dropped cases are removed from `index.json` and the stored `.txt` renumbered; `parity/bin/loose-golden` would rewrite it from the file as it is;
  `LooseDomTests` counts the stored files).
- **`bench/quick`'s decoded-bytes check** compares each probed page with `bench/lib/expected-sizes.json` (room page 376,570, messages page 345,884, sidebar 29,710, search 136,633: the Falco pages as 4.10 measured them) and Rust's size for
  the routes that did not change. The run below prints "decoded bytes equal the stored expected size ... on every probed route" with no FAIL.
- **Renamed so the grep is empty:** `Config.FragmentCacheBytes` is `FragmentBytes` (the env var `CAMPFIRE_FRAGMENT_CACHE_MB` stays), `Marshal.Out` is `Buffer`. `grep -rnE "\bOut\b|Utf8\.lit|RecordedPage|RenderSize|FragmentCache" src tests` now finds
  only `Console.Out` in two tests, a `// Out:` comment in `AuthenticationTests`, and comments (`FragmentsTests`, `Config.fs`).
- **AGENTS.md** view rules rewritten (templates, `Markup/`, `Core/Html.fs` and the lint, `HtmlOutput` and fragments, whitespace traps, the oracles); **README** Known differences: the Views entry is one class A item (Falco's
  serialization, encoding, shorter pages, no indentation) holding the `</style` -> `<\/style` difference, plus the cache, Unicode-table and timestamp entries that still hold. `bench/results/views-*.md` marked superseded.
- **Gates:**
  - `bin/verify` (`CAMPFIRE_REQUIRE_SEED=1`, Release, warnings as errors, lint self-test + lint 222 files clean): 969 tests, 963 passed, 0 failed, 6 skipped (Linux-only and Pebble, as before); Views 123/123.
  - `bin/pixel-parity --fast` on every state, one slice at a time and the 13 realtime states one at a time, image `campfire-dotnetway:app` built from 67430ab: **25 of 25 slices PASSED, the UI is pixel-identical** (125 report
    files, 417 state results, evidence in `bench/results/dotnetway-4-11/pixels/`). Nothing needed fixing; every slice ran on every seed the harness has, including the ones that found whitespace in 4.4, 4.6 and 4.10 (search, messages, rooms, autocompletable).
  - `bench/quick` against the 4.10 image (`campfire-dotnetway:p411ctl`, interleaved, 3 reps each, same session; host probe 1.04-1.08 before every window, 0 of 12 above 1.10, load 1.8-5.7 at the start of the parts):
    new vs control req/s: room page c=16 7,386 vs 7,433, c=1 2,502 vs 2,512; messages 7,301 vs 7,402, 2,298 vs 2,306; sidebar 7,574 vs 7,692, 2,421 vs 2,452; search 7,019 vs 7,075, 2,191 vs 2,205; post 2,058 vs 2,048,
    1,154 vs 1,154. x Rails (new): 30.9, 23.4, 19.2, 10.5, 12.7, 8.45, 17.2, 11.4, 6.24, 6.92. **No cell under 5x** (thinnest post c=16 6.24x, post c=1 6.92x, sidebar c=1 8.45x), all 2xx with 0 connection errors in 30 runs.
    Within 0-1.6% of the control, as expected from a unit that deletes code nothing calls. CPU per request equal within 1% (room page 440 vs 436 us).
- **KLOC:** `bin/kloc` 33,550 by upstream's rules, **33,722 corrected (33.7 KLOC)**, from 34,536 (-814). `Campfire.Views` **5,218** corrected from 6,032 (-814: `Out.fs`, `Tag.fs`, `Filters.fs`, `Links.fs`, `Translations.fs`, the
  writer halves), every other project unchanged (App 11,710, Storage 1,646). **Against the design's projection** (Views 3.40, project about 32.0; range 31.6-32.6): Views is **1.8 KLOC over, the project 1.1 KLOC over the range's
  top**. Where (against the 5,792 at b632a7c): templates 3,567 (from 3,504, the design's 2.10: the Falco templates came out the writer's size, not 0.6 of it; messages 613 -> 641 lines, rooms 656 -> 738, as the 4.10 checkpoint
  noted), `Markup/` 456 + `Helpers/` 233 = 0.69 (from 0.94; the design's 0.38), `Core/` 357 (from 614; the design's 0.25: `Fragments.fs` 125 and `HtmlOutput.fs` 110 are larger than projected), the rest 0.60 (0.73 -> 0.67 projected). The App side
  (-0.38 in the design) landed as projected. So Phase 4 net is **-0.57 on Views (5.79 -> 5.22) and -1.1 on the project (34.8 -> 33.7)**, not the design's -2.8. The plan's 31.0 KLOC bar (Rust) is 2.7 above, and the whole gap is
  the templates: closing it means smaller templates (shared partial shapes, repeated markup as data), a decision for the verifier, not this unit.
- **Differences recorded:** the README's one class A entry (above). Nothing new beyond 4.10's `</style` item.
- **Phase 5 note (checkpoint):** whatever replaces the sanitizer must keep escaping `<` in attribute values or keep relying on the U+E000 rewrite in `Trusted.ofSanitized`; do not drop both (AGENTS.md says it too).
- Evidence: `bench/results/dotnetway-4-11/` (`build.sh`, `build.log`, `pixel-driver.sh`, `pixels/` (a log and `report.json` per slice), `bench.sh`, `quick/` (`compare-new.txt`, `compare-ctl.txt`, `bench-*.log`, one JSON per rep)).
- **Next:** Phase 4 independent verification (luminary), then Phase 5.

### Phase 4: gate (2026-10-21/22)

Evidence: `bench/results/dotnetway-p4/` (slim: logs, `report.json` per pixel slice, bench JSON; `lean1/` is the first lean run on the `4a8d095` image, `diag/` the A/B reruns). Gate runner; two code changes: `e7f5b52`/`7be22a7` (avatar SVG) and the harness fix below.
- **`CAMPFIRE_REQUIRE_SEED=1 bin/verify`** on `4a8d095`: 969 total, 963 passed, 6 skipped (the same environmental 6), 0 failed; lint self-test (27 planted files) ok, lint 222 files clean; H1 green (`verify.log`). The Views project alone on `7be22a7`: 123/123 (`views-tests.log`). There is no test named HT1-HT3: the hostile-text render coverage is the frozen oracle (546 hostile cases, `DifferentialTests`), `LayoutsTests` (hostile custom styles inside the `style` element), `UsersViewsTests` (hostile avatar initials as XML), `HtmlTests`, and the lint's planted files; all green.
- **First lean pixel run found a real regression** (image `4a8d095`): 21 of 25 slices red in **WebKit, light scheme only**, 37 to 305 pixels, aria and loose equal. A/B on `rooms/show/designers` against the Phase 3 control: control equal, HEAD 37 pixels. The diff was the `RL` initials of the SVG avatar: the ERB leaves `\n      initials\n    ` inside `<text>` and WebKit centres the text with it; Falco wrote none. Fixed in `e7f5b52` (the test compares the initials trimmed, `7be22a7`). Class C, found only by the engines the `--fast` runs of 4.2-4.11 (Chromium) did not use.
- **Lean pixel gate on `7be22a7`** (25 slices, `realtime/**` one at a time, every engine of the lean matrix): **940 of 940 cells equal on all three layers**.

| seed | screenshots | aria | loose DOM |
|---|---:|---:|---:|
| default | 860/860 | 860/860 | 860/860 |
| crowd | 24/24 | 24/24 | 24/24 |
| custom_styles | 32/32 | 32/32 | 32/32 |
| first_run | 16/16 | 16/16 | 16/16 |
| restricted | 8/8 | 8/8 | 8/8 |

  One cell (`auth/sign_in/error` chromium-desktop-dark) was NOT CAPTURED in its slice (30 s readiness, 8 requests in flight, after three attempts); the state re-run alone (4 cells) was 4/4 equal on all layers, so default counts 859 + 1.
- **Full pixel matrix, once** (`--matrix full`: every state on chromium, firefox, webkit, desktop/laptop/tablet/phone, light and dark, the breakpoint sweep; image `7be22a7`). The harness first died twice (default after 149 of 4,452 cells, restricted after 64 s) from an unhandled rejection in `parity/capture/network.ts` (`bodyOf`'s fallback request on a closed context); fixed in the harness (empty body; the network layer is not reported), and default and restricted re-run in slices with a retry. Result:

| seed | cells with a screenshot | screenshots | aria | loose DOM | not captured |
|---|---:|---:|---:|---:|---:|
| default (4,452 cells incl. fragment states) | 4,438 | 4,436 equal, 2 differ (flake, below) | all equal | all equal | 8 |
| crowd | 144 | 144/144 | 144/144 | 144/144 | 0 |
| custom_styles | 192 | 192/192 | 192/192 | 192/192 | 0 |
| first_run | 96 | 96/96 | 96/96 | 96/96 | 0 |
| restricted | 48 | 40/40 | 40/40 | 40/40 | 8 |

  Default by slice: errors 120, auth 318 (+8 not captured), pwa 276, welcome 38, search 156/158, account 206, users 388, messages 720, interactions 964, rooms 930, the 13 `realtime/**` states 24 each: every cell equal on all three layers except the items below. **The gate is NOT clean on the full matrix:**
  1. **16 Firefox cells not captured, two states** (`auth/join/invalid_code` and `rooms/opens/new/restricted_member`, 8 cells each: Firefox, 4 viewports x light/dark): `candidate: page.goto: NS_ERROR_NET_EMPTY_RESPONSE`. Reproduces on the Phase 3 control image (`b632a7c`), so it is not Phase 4's; the full matrix is the first run that puts these states on Firefox. Cause seen: both are empty-body 404s; with `Accept-Encoding: gzip, br, zstd` Rails answers `Content-Encoding: gzip` with an empty gzip body (chunked), ours answers `Content-Length: 0` plain (`Respond.fs`/`Before.fs`, Phase 2 code). Firefox accepts the first and reports an empty response on the second. A Firefox user opening such a URL on our server gets an error page instead of a blank 404. Not fixed here (Phase 2 layer, outside the unit); needs a fix unit, then those two states on Firefox.
  2. **`search/results/many` WebKit, one or two cells differing by 24-222 pixels** at the right edge of an uploaded WebP avatar (the DOM and avatar bytes are equal). Intermittent: in the slice 2 cells (desktop-dark, laptop-dark), then 1 (desktop-dark, 54 px), isolated reruns of the same cell 3 of 3 equal on HEAD; and the Phase 3 control, run on the same slice, also differed in one cell (`webkit-laptop-light`, 24 px). Judged a WebKit image-decode flake of the harness under load, not Phase 4; recorded, not fixed.
- **`bin/cookie-roundtrip`** 20/20 both ways. **`bin/db-differential`** rc 0 (reference boots on the database we wrote, rollback ok).
- **Speed** (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps, interleaved new/ctl/ctl/new/new/ctl; new = `7be22a7`, control = Phase 3 verifier `b632a7c` as `campfire-dotnetway:p3ctl` (built in a scratch worktree with the submodule copied in, label checked, removed); host probe 1.04-1.07 before and after every rep of both, 0 of 10 windows above 1.10 in any rep, Mac load average 3.3-4.3 throughout (other programs on the Mac; the probe says the pinned cores were not slowed), 0 non-2xx and 0 connection errors in 30 runs per side):

| workload | c | new req/s | control req/s | new vs control | x Rails (new) | x Rails (control) | CPU us/req new / control |
|---|---:|---:|---:|---:|---:|---:|---|
| room page | 16 | 7,433 | 8,442 | -12.0% | 31.06 | 35.28 | 437 / 399 |
| room page | 1 | 2,504 | 2,747 | -8.8% | 23.38 | 25.65 | 496 / 462 |
| messages page | 16 | 7,286 | 7,708 | -5.5% | 19.15 | 20.25 | 458 / 437 |
| messages page | 1 | 2,303 | 2,386 | -3.5% | 10.57 | 10.95 | 530 / 514 |
| sidebar | 16 | 7,605 | 7,870 | -3.4% | 12.71 | 13.15 | 433 / 436 |
| sidebar | 1 | 2,440 | 2,317 | +5.3% | 8.51 | 8.09 | 504 / 532 |
| search | 16 | 6,650 | 7,534 | -11.7% | 16.30 | 18.47 | 511 / 455 |
| search | 1 | 2,181 | 2,295 | -5.0% | 11.32 | 11.91 | 557 / 530 |
| post a message | 16 | 1,995 | 2,056 | -3.0% | 6.04 | 6.23 | 1,523 / 1,465 |
| post a message | 1 | 1,138 | 1,124 | +1.2% | 6.82 | 6.73 | 1,665 / 1,635 |

  **No cell under 5x** (thinnest: post c=16 6.04x, post c=1 6.82x, sidebar c=1 8.51x); no speed fix made. The string-render cost of Phase 4, against the last writer-based code: -3% to -12% throughput at c=16 on the pages (room page and search the most, as they render the most nodes; search's new range 6,627-7,053 is wide), +0.7% to +12% CPU per request, posting -3%/+1%. The control's decoded-bytes check prints FAIL on purpose (writer pages are 8-10% larger than the Falco pages).
- **KLOC:** `bin/kloc` 33,551 by upstream's rules, **33,723 corrected (33.7 KLOC)**, from 34.8 at the Phase 3 close (-1.1); `Campfire.Views` 5,219, App 11,710. Unchanged by this unit (+1 line).
- **Differences recorded:** none new for the README. Open for a fix unit: the Firefox empty-404 pair above; the 16 KB Cable bound and the two test packages (Phase 3 questions) remain open.
- Disk: 19-28 GB free at starts (the Mac's free space fell to 13 GB during the full matrix and was recovered by `fstrim` in the VM); `bin/clean-docker` and `bin/clean-parity` run; the control image is removed.

### Phase 4: security review of the views (2026-10-22)

Opus reviewer, on `306f048`. Findings and method in `plans/design-views.md` section 11 ("Security review").
- **Read:** the 79 templates, 9 `Markup/` helpers, `Core/Html.fs`, `HtmlOutput.fs`, `Fragment.fs`, `ViewContext.fs`, `Layouts.fs` and the five
  helpers that feed attributes (98 files read whole; the remaining 17 `Campfire.Views` files scanned for string-built markup: none); in the
  App the three `Trusted` call sites, the string response writers and their 14 callers, the broadcasts and Cable's tag builder.
- **Templates:** no runtime XSS or HTML injection found. Every data text and attribute goes through `Html.text`/`Html.attr`; no
  user-typed URL reaches `href`/`src` (`link_back`'s `Referer` is Rails' behaviour); style attributes carry integers only; the custom CSS
  can inject CSS (Rails' feature) but not markup (`</style` escaped, the README difference); `messages/_template`'s script template encodes
  its data; no JSON in data attributes.
- **The lint had nine holes, all fixed** (X1-X9): code hidden after a block comment holding `"*)"` and after an interpolation hole holding
  `'"'` (both valid F#, both swallowed by the lint's string scan), double-backtick identifiers, whitespace or a line break around a dot,
  unchecked attribute names (`Markup.Rooms.linkToRoom` took `data-*` names as strings; it now takes attributes), raw makers
  (`HtmlOutput.text`/`ofBytes`, `CachedFragment` from a string), raw responses (`Pages.html`/`renderText`/`renderAs` with an HTML, Turbo
  Stream or SVG type), a scan limited to two projects (now all of `src`), and the exempt encoder file (its definitions are now pinned).
  Each has a planted file the old lint passed: `--self-test` against the old lint fails 11 of the 12 new plants (the twelfth is a
  regression guard), the new lint catches 39 of 39; `bin/lint-markup` 340 files clean.
- **Also fixed:** `PresenterAccountsTests` "serves the account logo and avatars" failed on `306f048` (it looked for `>D</text>`; the gate's
  `e7f5b52` keeps the ERB's indentation around the initials, and `7be22a7` re-pointed only the Views test). Now a whitespace-tolerant match.
- **Gates run:** `bin/verify` green: lint self-test 39/39 and 340 files clean, Release build 0 warnings, tests 969 total, 963 passed, 0 failed, 6 skipped (the usual Linux-only, seed-only and filtered ones; H1-H3 in `HotPathTests` passed). Not run: `bench/quick` and the pixel slices. The only template change is `linkToRoom`'s
  argument type: its two callers build the same `data-sorted-list-*` attribute with the same encoder, so the sidebar renders the same bytes
  (the sidebar oracle and golden tests in `Campfire.Views.Tests` compare it) at the same cost.
- **KLOC:** `bin/kloc` 33,551, corrected 33,723 (33.7), unchanged (`Campfire.Views` 5,219).
- **Differences recorded:** none new. Open, accepted (O3): the lint is a token scan; reflection or unchecked casts that build Falco nodes
  are not seen.

### Phase 4: independent verification (2026-10-22) - PHASE 4 DONE; Phase 5 GO

Opus verifier, adversarial review of `b632a7c..088fbf6` (4.1-4.11, the checkpoint, the gate, the security review) against
`plans/design-views.md` and D1-D9, with the cheap checks re-run. Evidence in `bench/results/dotnetway-p4-verify/`. Fixes in `59207f1`,
`592ed71`, `49e1e09`.
- **Checked and holding:** the writer is gone (no `Out.fs`, `Tag.fs`, `Filters.fs`, `Links.fs`, `Legacy.fs`, `PageParts.fs`,
  `FragmentGlue.fs`, `RecordedPage`, `RenderSize`, `SafeBytes`, `bin/views-differential`, `bin/views-test-parity` in `src`, `tests`
  or `bin`; `Helpers/` keeps only text helpers). Escaping: one encoder (`Core/Html.fs`, `WebUtility.HtmlEncode`), the three `Trusted`
  makers at their one call site each, every `Html.attr` name a literal; data-fed `href`/`src`/`action` values are routes, asset paths and
  blob paths, `style` values numbers; the slot marker is neutralised in sanitized HTML and custom CSS. `bin/lint-markup` self-test 39/39
  and 340 files clean. Fragments: only `Fragments.fs` calls HybridCache; keys are Rails' `cache_key_with_version` (+ partial, variant);
  boosts and body/attachment changes touch the message (`Models.fs`), so a change re-keys it; the cached templates read nothing of the
  viewer (`_Message`, `_Actions`, `_Boost(s)` use `ctx` only for asset paths; `RoomName` is `for_user: nil`); the direct-room fragment
  is keyed by the viewer's own membership. Pixel evidence: the gate's lean `report.json`s give 940 screenshot cells equal on pixels,
  aria and loose, as it said (+16 server-only states whose only red layer is the reported server bytes); the full matrix's only uncaptured cells
  were the 16 Firefox ones below plus the one Chromium readiness timeout the gate re-ran.
- **Fixed:** (1) **The 16 Firefox cells (`auth/join/invalid_code`, `rooms/opens/new/restricted_member`), `49e1e09`.** Not a header
  problem: our 404 was a well-formed `Content-Length: 0` (`curl --raw` on the image). Firefox shows its own NS_ERROR_NET_EMPTY_RESPONSE
  page for an error document of zero bytes; Rails' `Rack::Deflater` gzips the empty body into a few bytes, which Firefox renders blank.
  `Compression.compressEmptyErrors` (a middleware just inside `UseResponseCompression`) starts an unstarted empty response with status
  >= 400, and `SkipSmall` lets that one through, so the framework compresses it when the client takes gzip/Brotli; identity is
  unchanged. `RespondTests` X4b (fails with the middleware removed). On the `49e1e09` image, full matrix (3 engines x 4 viewports x
  light/dark): `auth/join/invalid_code` 24/24 and `rooms/opens/new/restricted_member` (restricted) 24/24 equal on pixels, aria and
  loose. README compression entry updated (and its stale "16 MiB bounded map": kept pages share the fragments' HybridCache). (2) **No
  test rendered a page after a change to a cached record** (F1-F3 test the cache, not freshness): `MessagesTests` "V4 cached fragments
  show a change on the next render and nothing of another viewer" (edit, boost and its removal show on Jason's next room page; David's
  and Jason's pages hold the same message bytes; each member's sidebar names the other), `59207f1`; with the key's version sabotaged it
  fails. (3) Two comments naming the deleted tool, `592ed71`.
- **Re-run here:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `49e1e09`: 971 total, 965 passed, 6 skipped (the same environmental 6), 0
  failed, 0 warnings, H1 green. One earlier run on the same tree failed `FrontAcmeTests` "cached certificates load and are forgotten by
  obtaining" (`ObtainingCount` 1, expected 0); it passed 3/3 alone and in the next full run. Third time in Phase 4 (4.4, 4.7): a race
  in the Phase 7 front server, left for Phase 7 (which deletes `Front/`). Pixels on the `49e1e09` image (label checked): the two states
  above, then `--fast` `interactions/sidebar**,interactions/phone_sidebar,rooms/show/**,search/**` on every seed (the security review's
  `linkToRoom` change had no pixel run): default 55/55, custom_styles 6/6, all three layers.
- **Speed** (`bench/quick --routes post_message,sidebar`, the floor's thinnest cells, 3 reps interleaved new/ctl/ctl/new/new/ctl; control
  the 4.10 image `p411ctl`, removed afterwards; host probe 1.04-1.09 before and after, 0 of 24 windows above 1.10, Mac load 2.4-5.5,
  every response 2xx, 0 connection errors, decoded bytes equal the stored expected sizes): new / control req/s and x Rails (new):
  sidebar c=16 7,619 / 7,593 (12.73x), c=1 2,525 / 2,552 (8.81x); post c=16 2,046 / 2,070 (6.20x), c=1 1,150 / 1,152 (6.89x). Within
  -1.2% to +0.3% of the control; CPU per request within 1%. No cell under 5x. The gate's five-workload table against the Phase 3
  control (room page -12%, search -11.7% at c=16, everything still 6.04x-31x) stands as the phase's string-render cost.
- **KLOC:** `bin/kloc` 33,551 by upstream's rules, **33,723 corrected (33.7)**, unchanged by the fixes (tests and 15 lines of
  middleware and comments). Against the design: Views 5.22 (projected 3.40), project 33.7 (projected 32.0, range top 32.6). The miss
  is the templates (Falco came out at the writer's size, not 0.6 of it). Not a blocker: D7's bar (Rust, 31.0) is the close's (Phase 9)
  and Phase 5's projected -4.8 takes the project to about 28.9. If Phase 9 lands above 31.0, a template-compaction unit (shared partial
  shapes, repeated markup as data) is the lever.
- **Differences recorded:** README compression entry amended (empty errors are compressed, as Rails). No new class B entry.
- **Phase 5 GO.** Conditions: keep `Trusted.ofSanitized` the only way rich text reaches a page and keep escaping `<` in attribute
  values or the U+E000 rewrite (AGENTS.md); re-run the lint's planted files if the sanitizer's output type changes.

### Phase 5, unit 5.1: Html5 (2026-10-22)

`plans/design-richtext.md` section 4, unit 5.1: `src/Campfire.RichText/Next/Html5.fs` (namespace `Campfire.RichText.Next`, module `Html5`, 361 lines), `ParseError`'s three new cases (in `Dom.fs`, where the type still
lives until 5.7), AngleSharp 1.8.4 pinned in `Directory.Packages.props` (the tests move from 1.8.3; `Campfire.RichText.fsproj` references it), `tests/Campfire.RichText.Tests/Html5Tests.fs` (19 tests; the Tests project also references
`Microsoft.Data.Sqlite` to read the seed's 164 bodies). No app change, so no pixel run and no bench. Nothing calls `Next` yet.

- **Built as the row says:** one shared `HtmlParser` over one `BrowsingContext` (scripting off); `parseBody` (a document parse, leading U+FEFF dropped, templates flattened), `parseIn` (`ParseFragment` in the element's
  context, the nodes detached from the parse's context copy from the back, templates flattened), `setInnerHtml`, `replaceWithHtml`, Nokogiri's serializer (`toHtml`, `innerHtml`: no newline after `pre`/`textarea`/`listing`; raw text for
  `style`, `script`, `xmp`, `iframe`, `noembed`, `noframes`, `plaintext`, `noscript`; `&`, U+00A0, `<`, `>` in text, `&`, U+00A0, `"` in attributes; void elements bare), `descendants`, `attr`, `isNamed`, `clone`. The limits and caps are
  counted in the parser's callbacks through a thread-static `ParseState` (the parser is shared; a callback raises `LimitExceeded`, which `parseBody`/`parseIn` turn into the `ParseError`; the state is reset in a `finally`).
- **What the spikes did not tell and the build found (each pinned by a test):**
  1. **A document parse needs a doctype.** Without one AngleSharp parses in quirks mode and `<p><table>` keeps the table inside the `<p>` (Gumbo and html5ever's fragment parses are no-quirks): the first run differed on
     S2's "fuzz 44" and "fuzz 63". Every parse starts `<!DOCTYPE html><body>`; both bodies are equal now.
  2. **AngleSharp's `IAttr.Name` drops the XLink prefix** (`xlink:href` is `href` with the XLink namespace; `xml:` and `xmlns:` keep theirs). `Html5.qualifiedName` applies the serialization algorithm's rule by namespace.
  3. **Comments after `</body>` or `</html>` leave the body** (the document algorithm puts them in `html` and the document; the fragment algorithm, and so the old parser, keeps them in order). `bodyOf` moves them back, after the
     body's other children: exact unless content follows them (`</body><!--c-->tail` gives `tail<!--c-->`; a recorded, pinned class B corner, P2).
  4. **A leading `<frameset>` is not a `ParseFragment` fallback.** The row says to reparse with `ParseFragment`; that is the quadratic path (design S3) and also keeps foreign content inside its element. The body is parsed again
     with one letter in front (a character closes the window for a frameset, which is the fragment algorithm's own rule), and the letter is taken off the first text node: linear, and foreign content breaks out as everywhere else.
     P2 compares six frameset shapes to the old parser's trees.
  5. **`OnCreated` is not called for an `svg` or `math` start tag in HTML content** (its children are). So those two are not counted as elements (only the 40,000-token cap bounds a flat `<svg></svg>` run: 20,000 parse in under
     50 ms) and the depth of an element inside one is still right (it is counted from the tree, not from a counter).
  6. **Depth is checked at the next token**, not only when the next element is created: an element created last is checked when the token after it is read, and at the end of the input. A comment or tag name of 16 MB after 401
     `<div>`s is still read whole (AngleSharp calls no callback inside a token): 37-44 ms, where tags are refused at once (2.4 ms).
  7. **A template's content is its own fragment**: the template's depth is recorded when it is checked and carried into the fragment (10,000 nested templates are refused as too deep in 0.4 ms).
  8. **The attribute count needs distinct names.** AngleSharp's token keeps duplicates until the tag is finished, and `ShouldEmitAttribute` is called for every name, duplicates and end tags included. Gumbo refuses when a tag that
     already holds 400 attributes meets another name, but 1,000 equal ones are one attribute. Below 400 names nothing is counted; from the 400th a set of the tag's names is kept. 200,000 equal attributes parse in 36 ms.
- **Acceptance (Release, `CAMPFIRE_REQUIRE_SEED=1`, CPU time, best of five unless noted):**
  - **P1 (trees with namespaces vs the old `Dom`, the 1,479 inputs = 658 corpus bodies + 164 seed bodies + 657 Rails presentations):** equal on 1,471; **eight corpus bodies differ, 0 seed bodies, 0 presentations**. The named list: `malformed
    select` and fuzz 125, 177, 188, 325, 362, 395 (the 2025 standard's `select` content model: a `<b>`/`<hr>`/`<s>` inside an `<option>` or `<select>` is kept, Gumbo drops it) and `limits attributes on an unfinished tag` (L1). **S2
    named twelve; four are gone for the reasons in 1, 6 and 8** (fuzz 44, fuzz 63, `limits too deep before the adoption agency cuts`; and `limits attributes at the limit and a duplicate` was never different once the count is
    made before the duplicate is dropped). The test fails on an unlisted difference and on a listed case that no longer differs.
  - **L1 (Gumbo's truth table: the 19 cases of the old `DomTests`):** 16 equal, 3 named boundary cases. Beyond `attributes on an unfinished tag` (the input ends inside the 401st name; no callback sees it: renders here, refused by
    Gumbo): `400 x <b> + </p>` (the implied `<p>` of an end tag with none open is an element at depth 401 for AngleSharp, never on Gumbo's stack: refused here, rendered by Gumbo) and `reopened b's + a comment`
    (Gumbo checks again after the formatting elements are reopened past the limit; here the last element's depth is checked and is under it: rendered here, refused by Gumbo). `400 x <b> + <br>`, `1,000 equal attributes`,
    `adoption agency, too deep before` and the rest equal Gumbo. Each of the three also goes into `known-differences.json` in 5.6.
  - **P2:** template contents are children (nested too); BOM dropped at the start only; six frameset shapes equal the old trees; `<html a=1>`/`<body a=1>` add nothing; comments after `</body>`/`</html>` stay in the container (3 above).
  - **P3:** the old `DomTests`' serializer cases (copied), plus `pre`/`textarea`/`listing` losing one newline per round trip, the eight raw-text elements, escaping, void elements, `xlink:href`/`xml:lang`/`xmlns:xlink`.
  - **P4 (`parseIn` in `action-text-attachment`, `div`, `p`, `li`, `figure`, `td` against the old `ParseNodes`):** the plain texts of the corpus and the partial-shaped strings are equal, **except strings holding `<svg`, `<math` or `<select`**
    (set aside, counted: AngleSharp's fragment algorithm keeps foreign content inside its element and has the `select` rule). **Unit 5.4 must therefore hand `parseIn` only markup the pipeline built** (partials, `"@Name"`,
    `"[caption]"`), **never a message's own markup or a `replace(plain text)` of text that can hold `<svg>`**: for those, build nodes (`CreateTextNode`) or go through `parseBody`. A test pins the difference (`<svg><p>y</p></svg>`).
  - **P5 (no quadratic step):** 9,900 `<p>x</p>` 14.0 ms, 9,900 `x<br>` 12.8 ms, 8,000 paragraphs 17.5 ms, a 2,000-row table 14.1 ms, 20,000 chunks of 1 KB text parse (bound 50 ms). **The row's 20,000 paragraphs and 20,000
    `x<br>` are over the 10,000-element cap (R3), so they are refused (17 ms and 13 ms), not parsed**: the test sets the no-quadratic bar at the cap's edge and checks that 20,000 are refused in under 50 ms.
  - **L2 (the port's hostile shapes):** 80,000 `<div>` 0.35 ms, 80,000 `<a><b>` 1.1 ms, 30,000 `<a><div><div>` 0.9 ms (all refused as too deep); 401 `<div>` + 16 MB of tags 2.4 ms; + a 16 MB comment 37 ms, + a 16 MB tag name 44 ms
    (bound 250 ms, not the port's 100: 6 above); a tag with 64,000 attributes 0.17 ms; 550 KB of `<html>` tags with 400 new attributes each is **now refused** (`TooManyRootAttributes`, 0.17 ms for 300 tags; Rails and the port render
    it empty: class B, R3); 200,000 `<br>` after a `<table>` refused at the element cap in 48 ms (bound 1 s).
  - **L3 (the caps):** 9,000 `<br>` after a `<table>` parses in 41 ms (bound 400); 10,000 elements parse, 10,001 are refused in 3.0 ms; 40,000 tokens parse, 40,001 are refused in 5.4 ms; 160,000 ignored end tags refused in 26 ms;
    400 attributes over `<html>` and `<body>` parse, 401 refused (0.17 ms); one tag: 400 names parse, 401 refused; 8,000 paragraphs and a 2,000-row table parse. The other shapes S3 or the build found (10,000 templates, 10,000 nested
    `<svg>`, 9,000 `<table><td>`, formatting elements reopened in a table, 9,000 `<option>`, 12,000 stray `</p>`, text split by `</q>`, 3,000 nested tables) each stay under 400 ms (a test).
  - **L4:** 8 threads x 20,000 parses of 164 seed bodies, the 658 corpus bodies and four refusals/templates give the single-threaded results (innerHtml or the refusal) every time; managed memory after a full collection within 5 MB of the
    start.
- **Known differences found (class B, for `known-differences.json` and the README in 5.6):** (a) the 2025 `select` model (P1's seven bodies); (b) the three limit boundaries (L1); (c) comments after an explicit `</body>` that
  content follows arrive after that content (3); (d) **`<isindex>`**: AngleSharp still expands it as the 2008 standard did (`<form><hr><label>This is a searchable index. Enter search keywords: <input name="isindex"></label><hr></form>`),
  where Gumbo and browsers leave an element of that name. The sanitizer of 5.2 drops `form` and `input` and `label` by the allowlist, but the sentence survives as text and the `hr`s may. AngleSharp offers no switch, and an `OnToken`
  rename does not reach the tree builder; 5.2 should either drop that exact subtree or record the sentence. (e) element-context parses keep foreign content inside (4 under P4); (f) the caps and the root-attribute cap (R3).
- **Not done / left for later units:** the migration differential's other halves (S1, M1 need the sanitizer and the pipeline); the unfinished-tag boundary could close if `StructHtmlToken` exposed the pending attribute at the end
  of input (it does not in 1.8.4).
- **KLOC:** `bin/kloc` 33,929 by upstream's rules, **34,101 corrected (34.1 KLOC)**, from 33,740 (+361: `Next/Html5.fs`; `Campfire.RichText` 6,197 -> 6,558). **Against the design** (`Html5` about 0.14): 0.36, a hundred lines of it
  the thread-local counters, the depth/template/attribute logic and the comment/frameset handling that the spikes did not need. The old parser goes in 5.7; the phase projection (about 29.7) is unchanged for now.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green on the second commit: lint self-test 39/39 and 341 files clean, Dapper and EF guards, Release build with warnings as errors 0 warnings, tests 990 total, 984 passed, 0 failed,
  6 skipped (the same environmental six; was 971 before, +19 here); H1 in `HotPathTests` passed. Not run: pixel slices and `bench/quick` (no app change). Disk 27 GB free at the start and after.
- **Next:** unit 5.2 (Sanitizer: `Loofah.fs` out of `Sanitizer.fs`, the walker on AngleSharp nodes), with the `<isindex>` item above.

### Phase 5, unit 5.2: Sanitizer (2026-10-22)

`plans/design-richtext.md` section 4, unit 5.2: `src/Campfire.RichText/Loofah.fs` (444 lines: `SafeList`, `SafeListData`, `SafeList`, and module `Loofah` with `allowedUri`/`allowedUriUsing`, `cgiUnescapeHtml`, `attrValIsUri`, `escapesAttribute`,
`needsEscaping`, `escapeAttributeValue`, `scrubStyle: string -> string option`) moved out of `Sanitizer.fs` unchanged in substance; the old arena walker (`Sanitizer.fs`, now 117 lines) calls them, so the old pipeline's outputs are unchanged.
`src/Campfire.RichText/Next/Sanitizer.fs` (95 lines): `scrub: INode -> SafeList -> unit` and `sanitize`, Loofah's `PermitScrubber` as a bottom-up walker over AngleSharp nodes (R4).

- **S1:** the new sanitizer equals the old on the 1,479 inputs for all four lists (5,916 comparisons, byte-equal), except 8 inputs, every one on P1's list (`malformed select`, fuzz 125, 177, 188, 325, 362, 395,
  `limits attributes on an unfinished tag`); the test fails on any other name.
- **S2:** `SanitizerTests`' three Rails-vector tests now run on both sanitizers (`S2 next ...`), byte-equal (stricter than loose); all pass.
- **S3:** `Loofah.allowedUri` is the old code (0 verdict differences against `Sanitizer.allowedUri`). **The `WebUtility.HtmlDecode` trial failed and was not kept:** over 2,512 URI attribute values of the corpus bodies, Rails' presentations
  and the seed (237 distinct) plus 466 hardening inputs (695 distinct), HtmlDecode's output differs from `cgiUnescapeHtml`'s on 20 and the **verdict on 3** (`&nbsp;avascript:alert(1)`, `&copy;...`, `&eacute;...`: HtmlDecode decodes
  every named entity, and a decoded U+00A0 is then stripped as a control character, where CGI leaves it and Rails allows the value). The corpus's own values agree; the hardening inputs are what differ. `cgiUnescapeHtml` stays in `Loofah`
  (a test fails the day HtmlDecode would pass).
- **Known difference found (class B):** `<isindex>` (5.1's item d): AngleSharp expands it, so `Next.Sanitizer.sanitize "<isindex>"` with the default list is `<hr>This is a searchable index. Enter search keywords: <hr>`, where the old
  sanitizer (Gumbo) gives "". Not dropped in code (a rename of the subtree would be a heuristic over `form > hr > label`); recorded for `known-differences.json` in 5.6 and pinned by a test.
- **Not done:** `sanitizeWithEscapedAttributeBrackets` has no Next counterpart (R6 removes its reason: the spec serialization writes `<` raw).
- **KLOC:** `bin/kloc` 34,012 by upstream's rules, **34,184 corrected (34.2 KLOC)**, from 33,929 / 34,101 (+83: `Loofah.fs` +444 and `Next/Sanitizer.fs` +95, `Sanitizer.fs` -426). **Against the design** (`Loofah` 0.33, `Sanitizer` about 0.06): 0.44 and 0.095
  (the walker keeps a snapshot of the attributes to model the order-dependent re-escaping). The old walker goes in 5.7.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green: lint, Dapper and EF guards, Release build with warnings as errors, every test (996 total, 990 passed, 0 failed, 6 skipped: the environmental six), run after the last code and test change.
  Not run: pixel slices and `bench/quick` (no app change). Disk 28 GB free.
- **Next:** unit 5.3 (Attachables on attribute functions, PlainText, Content on the AngleSharp DOM).

### Phase 5, unit 5.3: Attachments, load, plain text, mentions (2026-10-22)

`plans/design-richtext.md` section 4, unit 5.3. **Built:** `Attachables.fs` split: `type Attrs = string -> string voption`; `attachmentFrom`, `actionTextAttachableFrom` and `opengraphEmbedFrom` take it (the old `...FromNode` names are gone; the old `Content`,
`ActionText` and `Filters` pass `fun n -> dom.Attr(node, n)`, so the old pipeline's outputs are unchanged). `Next/PlainText.fs` (81 lines), `Next/Content.fs` (174: `attachmentNodes`, Trix conversion, `attachmentGalleryNodes`, `loadRoot`/`load`, `wrap`,
`toHtml`, `toPlainText`; `Content = { Root: IElement }`), `Next/ActionText.fs` (26: `toPlainText`, `mentionedUsers`), and three helpers on `Next/Html5.fs` (`attrsOf`, `createElement`, `textContent`).
- **Tests** (`NextContentTests.fs`): **M1** canonical bodies (bytes), plain text and mentions on the 1,479: 8 canonical bodies differ, 2 plain texts, 1 mention list, **every one on P1's list** (the test fails on any other name). **C2:** the 658 corpus canonical
  bodies byte-equal except P1's 8. **C3:** the 164 seed bodies' plain text and mentions equal (0 differences; with a resolver that makes every SGID a user, 2 seed bodies mention someone). `Attachables` resolves every attachment node of the corpus
  alike through either DOM's attributes (attachment and opengraph embed, structurally equal). The old `AttachablesTests` and `CorpusTests` are unchanged and green.
- **Deviations from the note:** (1) `attrsOf` lives in `Next/Html5.fs`, not `Attachables.fs` (`Attachables.fs` compiles before AngleSharp's helpers; the plan's final order puts `Html5.fs` first, so 5.7 can move it). (2) `Attachables.embedFromContent` (the
  opengraph embed's `content` markup) still parses with the **old `Dom`**, for both pipelines: it is DOM work, not attribute lookup. 5.7 (or earlier) moves it onto `Html5.parseBody` once `Html5.fs` precedes `Attachables.fs`; until then the new pipeline
  depends on the old parser for that one string (M1 shows no difference). (3) `Next/Content.fs` and `Next/ActionText.fs` re-open `Campfire.RichText.Next` after `Campfire.RichText` so `Content` means the new module.
- **Cost (S6, risk 10, seed bodies, 30 rounds, this host under load):** canonical load + `toHtml` old 5.7 us / 6.1 KB, new 13.5 us / 10.7 KB; plain text old 5.9 us / 8.9 KB, new 16.9 us / 13.0 KB. The ratio (2.3-2.9x) is S6's (5.1 against 1.8); a post's added CPU
  is about 8 us (load) + 11 us per plain text, within R12's margin. The floor is judged by `bench/quick` in 5.6.
- **KLOC:** `bin/kloc` 34,250 by upstream's rules, **34,422 corrected (34.4 KLOC)**, from 34,012 / 34,184 (+238: `Next/` +281, `Attachables`/old callers -43 net). `Campfire.RichText` 6,879.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green (lint and self-test, Dapper and EF guards, Release warnings-as-errors build, every test: 1,003 total, 997 passed, 0 failed, 6 skipped: the environmental six; `Campfire.RichText.Tests` 110 passed).
  Not run: pixel slices and `bench/quick` (no app change). Disk 28 GB free.
- **Next:** unit 5.4 (rendering, editor value, filters on `Next/Content.fs`/`Next/ActionText.fs`/`Next/Filters.fs`), then the Opus checkpoint.

### Phase 5, unit 5.4: Rendering, editor value, filters (2026-10-22)

`plans/design-richtext.md` section 4, unit 5.4. **Built:** `Next/Content.fs` second half (`render` with nested content attachments 8 deep and the Action Text sanitize, attachments with full attributes, galleries, `renderAttachmentHtml`, `toRenderedHtmlWithLayout`),
`Next/ActionText.editableValue`, and `Next/Filters.fs` (RemoveSoloUnfurledLinkText, SanitizeTags, SanitizeAttributes; `normalizeTweetUrl` is the old module's, shared). `Html5.parseIn` gained one rule (below).
- **Tests** (`NextContentTests.fs`, old pipeline as the oracle on the 1,479): **M1** `toRenderedHtmlWithLayout` (the bot API's `body.html`): 6 differ, 5 on P1's list plus `corpus:fuzz 398` (recorded below); **M1 filtered content:** 1 differs (P1's `attributes on an unfinished tag`);
  **M1 editor values:** 8 differ, all on P1's list, and byte-equal everywhere else (so exact, not only loose). **C4:** a deleted user's mention leaves the editor and the words around it stay; old and new agree. Nested content attachments (depths 1, 3, 8, 9, 12) render
  byte-equal to the old pipeline and stop at the 8th. The test failure output now shows the window around the first differing byte.
- **Found and fixed (`Html5.parseIn`):** an attachment whose parent is an SVG or MathML element (not an integration point) was replaced by markup parsed *inside* that foreign element, where AngleSharp's fragment algorithm never breaks HTML out (the pre-2017 rule, design S2/Q1) and
  Gumbo does. The mention's `<span>` and its avatar vanished from the rendered output of 4 fuzz bodies (24, 68, 207, 299). `parseIn` now parses in a `div` of the same document when the context is foreign and not an integration point: equal to Gumbo whenever the partial's
  first tag is a breakout tag (a mention's span, an image, a div).
- **Difference recorded (class B, `corpus:fuzz 398`, layout only):** when the replacement's first tag is `action-text-attachment` itself inside a foreign element (Gumbo leaves that element empty and breaks the partial out beside it) the partial stays inside the attachment element.
  Only a hostile body puts an attachment in an svg or math; the Action Text sanitize drops foreign elements with their contents, and the editor value and filters are unaffected (no reparse). Goes into `known-differences.json` in 5.6.
- **Cost (S6, seed bodies, 30 rounds, this host):** canonical load + `toHtml` old 4.6 us / 6.1 KB, new 14.0 us / 10.7 KB; plain text old 6.3 us / 8.9 KB, new 17.9 us / 13.0 KB; **render** (load + `toRenderedHtmlWithLayout`) old 13.7 us / 16.4 KB, new 39.2 us / 25.8 KB (2.9x, S6's range).
  A post's added CPU is about 9 us (load) + 11 us (plain text) + 25 us (render): within R12's margin; the floor is judged by `bench/quick` in 5.6.
- **KLOC:** `bin/kloc` 34,369 by upstream's rules, **34,541 corrected (34.5 KLOC)**, from 34,422 (+119). `Campfire.RichText` 6,998.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green (lint and self-test, Dapper and EF guards, Release warnings-as-errors build, every test: 1,008 total, 1,002 passed, 0 failed, 6 skipped: the environmental six). Not run: pixel slices and `bench/quick` (no app change). Disk 28 GB free.
- **Next:** the Opus checkpoint over 5.1-5.4, then unit 5.5 (the presentation and autolink on AngleSharp).

### Phase 5, checkpoint after 5.4: Opus review (2026-10-22) - GO for 5.5, with notes

Adversarial review of `c88f590..eb87751` (5.1-5.4) against `plans/design-richtext.md` and D1-D9. Fixes in `8abdb6b` and `86943ee`.
- **Gates re-run.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `eb87751` (load average 4-7 from other sessions): 1,008 total, **3 failed**, none in rich text:
  `FrontAcmeTests` "cached certificates load and are forgotten by obtaining" (the known Phase 7 race), Cable `GoldenTests` "replays reference frames"
  (`subscribe base channel`: no confirm frame) and `PresenterAccountsTests` "profile sidebar and user pages" (302 for 200). App, Cable and Kit re-run alone:
  316, 80 and 66 green; the units' green runs stand (the Phase log's 990/996/1,003/1,008 totals match the tests in the tree). After the fixes, on `86943ee`:
  **1,010 total, 1,004 passed, 0 failed, 6 skipped** (the environmental six), 0 warnings, lint self-test 39/39 and 347 files clean, H1 green. **Pixels:** image
  rebuilt at `8abdb6b` (label checked; 5.2 and 5.3 changed code the live pipeline runs: `Loofah.fs`, the `Attachables` split), `bin/pixel-parity --fast --seeds
  default --only 'rooms/show/**,messages/**'`: **96/96 pixels, aria and loose** (309 s). `86943ee` changes only `Next/`, which the app does not call yet.
- **Checked and holding:** the walker against rails-html-sanitizer's `PermitScrubber` and the old arena walker line by line (bottom-up over a children snapshot;
  text skipped; comments and unlisted HTML elements unwrapped, foreign elements dropped with their contents; attributes in order, a removed one not re-escaped,
  `force_correct_attribute_escaping` over the remaining URI attributes after each kept one, a blank `src` removed after its escaping pass, Lexxy's `style` rule
  last): the same. `Loofah.fs` is the old code (the diff is visibility and `allowedUriUsing`). The `Attachables` split is mechanical (`dom.Attr(node, n)` became
  `attrs n`; no rule changed). P1's list is S2's minus four, each explained in 5.1; L1-L4 are in `Html5Tests` with their numbers. The serializer writes no
  raw-text element's text where the sanitizers can keep one (none of the four lists allows `style`, `script`, `xmp`, `iframe`, `noembed`, `noframes`,
  `plaintext`, `noscript`, `svg`, `math`); `<` in attribute values is raw and `Trusted.ofSanitized` still rewrites U+E000 (Phase 4's condition).
- **Fuzz (scratch harness, not committed):** 21,939 inputs (the corpus's 658 bodies and 657 Rails presentations, 53 mutation-XSS shapes: `noscript`,
  `template`, `style`, `xmp`, `iframe`, `noembed`, `noframes`, `plaintext`, `textarea`, `title`, `svg`/`math` with `mglyph`/`malignmark`/`annotation-xml`/
  `foreignObject`, CDATA, comment tricks, `select`, `isindex`; 46 `javascript:`/`vbscript:`/`data:` spellings with entities, controls, tabs, newlines, soft
  hyphens and zero-width spaces, each in `href`/`src`/`poster` and in 11 attachment shapes: images, files, videos, mentions with captions, content
  attachments, opengraph embeds, Trix figures, galleries, attachments inside `svg`/`math`; 20,000 generated hostile bodies). Each went through
  `Next.Sanitizer.sanitize` on the four lists, `load` + `toRenderedHtmlWithLayout`, the stored canonical body loaded and rendered again, and `Filters.apply`:
  **151,087 outputs parsed again as a browser does (AngleSharp, scripting on, inside a div): 0 elements or attributes outside the list, 0 foreign elements,
  0 comments, 0 event attributes, 0 `javascript:`/`vbscript:` or unlisted `data:` URLs after a browser's URL clean-up, 0 URI values `allowedUri` refuses.**
  823 outputs parse back into another structure (unwrapped template contents and tables the body parse rearranges, as Rails' own output does); one of them
  closes the container `div` early (a `table` from an unwrapped `template` inside a `table`): structure only, every node in it sanitized.
- **Against Loofah itself** (the reference image: `SafeListSanitizer` with Campfire's configured lists, `sanitize_action_text_content`, on 4,624 hostile
  inputs x 3 lists = 13,872): the new sanitizer differs from Rails on 679, of which the old equals Rails on 488. Those 488: **403 are AngleSharp lowercasing
  `</Name` inside raw text** (`<textarea></FooBar></textarea>` shows `</foobar>`; text only; new class B), 67 the `select` content model (P1's class), 3
  `isindex` (5.1), 15 foreign-content shapes where AngleSharp drops text Rails keeps (a raw-text name inside `math`) or nests table parts elsewhere. In
  every one the elements and attributes the new output keeps beyond Rails' are allowed ones from those parses (an `img src="x"` from a `select`, `hr`s from
  `isindex`); nothing outside a list.
- **Found and fixed (`8abdb6b`, `86943ee`): quadratic DOM work on wide bodies, under every parse cap.** AngleSharp finds `NextSibling`/`PreviousSibling` and
  the child it removes or inserts before by scanning the parent's list from the front, and `ParseFragment` moves its nodes one at a time. L2/L3 timed the
  parse only; the pipeline was not timed on wide bodies. Before: 9,900 `x<br>` 668 ms layout, 846 ms filters (old 20/27 ms); 19,000 `x<!---->` 2.3 s
  layout, 1.4 s plain text (old 350/5 ms); a caption of 9,500 `<b>`s 1.3 s per plain text, 1.5 s in a `td` (old 10-43 ms); 32,000 nodes in a `td` 4.7 s; a post
  runs plain text two or three times. Now: children by index (`Html5.childrenOf`) in the serializer, `descendants` and template flattening;
  `Html5.detachChildren` (first to last) and `Html5.insertAllBefore` (one fragment) for the unwrap, `replaceWithHtml`, `clear` and the plain-text fragment;
  `parseIn` uses the document parse where the fragment algorithm parses as the body does (HTML contexts other than table parts, `select`, `template`,
  raw-text elements, `html`/`head`/`frameset`, and no `form` above), and elsewhere counts the limits on the body parse and takes the tree from the
  InnerHtml setter of a detached copy of the context (inside a `form` when one is above). After: 6-145 ms for every shape above (the comment case faster
  than the old pipeline's 350 ms). The body-like route also breaks HTML out of foreign content as Gumbo does in those contexts (P4 now sets `svg`/`math`
  aside only in `td`). Tests: `NextContentTests` "wide bodies cost linear time ..." (8 bodies x plain text, layout, filters, 400 ms CPU each in Release;
  every pre-fix figure fails it), `Html5Tests` P5 "an element-context parse of many top-level nodes is linear", P4 rewritten. M1, S1, C2, C3 unchanged.
- **Differences to record in 5.6** (`known-differences.json`, README, class B, all hostile-only): (a) end-tag-shaped text inside `textarea`/`title`/`style`/
  `xmp`/`iframe`/`noembed`/`noframes`/`script`/`plaintext` is lowercased by AngleSharp's tokenizer; (b) raw-text-named elements inside `svg`/`math` and a few
  table-part shapes parse as AngleSharp's tree builder does (text dropped with the foreign element, or tbody/tr placed outside the table); (c) the
  element-context limits are counted on the markup's body parse (depth boundary only). Q1 (report AngleSharp's defects upstream) gains (a) and the
  `NextSibling` cost.
- **Cost:** a seed-sized body is unchanged (the fixes only remove scans); S6's ratios stand for 5.6's bench.
- **KLOC:** `bin/kloc` 34,437 by upstream's rules, **34,609 corrected (34.6)**, +68 over 5.4 (`Html5` helpers and the fragment route). `Campfire.RichText`
  7,066; `Next/Html5.fs` is about 0.4 KLOC against the design's 0.14 (the limit bookkeeping, comments, frameset, and now the parse routes).
- **Remaining units still fit, with one split recommended:** 5.5 (autolink, presentation) fits 90 minutes. **5.6 does not**: split it into 5.6a (`webUrl` and
  `normalizeTweetUrl` on `System.Uri`, `RubyUri.fs` and `UriTests` to the App, `OpengraphMetadata`'s two helpers, `Attachables.embedFromContent` off the old
  `Dom`) and 5.6b (the flip, `CorpusTests` on the new pipeline with `known-differences.json`, README, image, pixel slices, `bench/quick`). 5.7 fits.
- **GO for 5.5.** Conditions: no `NextSibling`/`PreviousSibling` walk, per-child `InsertBefore`, removal from the back or `ParseFragment` call in `Next/`
  (use `Html5.childrenOf`/`detachChildren`/`insertAllBefore`/`parseIn`); 5.5 adds its own wide cases (9,900 URLs, 9,900 e-mail addresses, one text node of
  16,000 URLs) to the linear-time test.

### Phase 5, unit 5.5: Autolink and presentation (2026-10-22)

`plans/design-richtext.md` section 4, unit 5.5. **Built:** `Next/Autolink.fs` (R6) and `Next/ActionText.messagePresentation`, `presentMessage`, `withoutRecipientMentions`. The matchers are the old `Autolink.fs`'s (`urlMatchAt`,
`emailMatchEnd`, `isEmailLocalChar`, `openingBracket` made `internal`; the old file is deleted in 5.7 and they move then). `Next.Autolink.autoLink` is: `Html5.parseBody` (the one parse), `Sanitizer.scrub`, then every text node with no `a`
ancestor is linked in place, then `innerHtml`. A text node is scanned in the form Nokogiri would serialize it (`&amp;`, `&lt;`, `&gt;`, `&nbsp;`), the URL pass runs first, the address pass over the text between links, and the pieces are decoded and built as text nodes
and `<a target="_blank" href>` elements (`Segment` is `Plain | Linked`; the design's `Text`/`Link` collided with the old `Dom`'s names in the tests). No string is spliced into markup, there is no `TagIndex`, and no step scans serialized HTML.
- **Decisions the unit made.** (1) *Adjacent text nodes are one text.* rails_autolink scans the serialized string, where the sanitizer's unwrapped elements leave two text nodes side by side (`www.example.com` + `a b` is one URL, `Rails: www.example.coma&nbsp;b`); a per-node scan
  split 10 corpus presentations. An element's run of text children is joined before it is scanned (M1 went from 10 unlisted differences to 0). (2) *The trailing `>` keeps rails_autolink's order*: stripped punctuation is written first, then the `&gt;` that ended the URL
  (`http://x/&gt;😀` becomes `link😀>`). (3) *An element's children are replaced in one go* (`detachChildren` then `AppendChild` of the rebuilt list): no `InsertBefore`/`RemoveChild` per text node (the checkpoint's rule). (4) *The per-piece `sanitize` of link text and `href` is skipped
  unless a piece holds a NUL or a surrogate*: the escaped form of text contains no markup, so the parse and serialization are the identity otherwise (a parse per link would be the cost of a document each, 9,900 times); the exceptional pieces still go through `Next.Sanitizer`. (5) A bare address holds no
  `&` (a reference ends in `;`, which the local part does not allow), so its display text is the address. (6) Test-only `Html5.parseCount` (a thread-static counter of parses) for A3.
- **Acceptance.** **M1** presentations (loose trees, old pipeline as oracle, 1,479 inputs): 1 differs, `corpus:limits attributes on an unfinished tag` (P1's list); the bytes differ on 164 inputs, every one equal as a tree (the attribute-value escaping of `<` `>`).
  **C1** (`CorpusTests`, Rails' 658 presentations with the port's divergences, loose DOM, through `corpus/known-differences.json`, failing both ways): 573 byte-equal, 84 loose-equal, 1 known difference (the same L1 case, class B). **N1** (every element, attribute name and URI value of ours is in Rails' output
  for the same case; the known difference excluded): 0 survivors over the corpus's presentations; its planted survivors (an element, an event attribute, a `javascript:` URL, a URI Rails lacks, a foreign element) are all caught. **N2** (`securityViolations` on 658 presentations, and on `body.html` with Action Text's list, `embed` aside because Campfire's list allows it):
  0 violations; the planted-defect test is unchanged. **A1** (`NextAutolinkTests`: URLs and addresses, `pre` and `code` linked, nothing inside an `a`, punctuation and brackets, `&nbsp;` inside a URL, the sanitizer first), **A2** (the `title` breakouts: the attribute's value unchanged, no element added; text that looks like markup), **A3** (a presentation's parses are the render's plus the linker's one;
  `autoLink` adds none beyond the sanitizer's) all pass. **`ReferenceTests` and `HardeningTests` now run on both pipelines** (`[<Theory>]` over `next`, a shim `ActionText` choosing the pipeline): 104 test cases pass. Three hardening tests have new-pipeline expectations, argued in the file: the 550 KB of `<html>` tags and the 200,000 `<br>`s in a table are *refused* (R3's root-attribute and
  element caps; Gumbo renders them), and refusing 16 MB of one tag name takes under 300 ms (the old tokenizer: 100 ms; AngleSharp reads the whole name before it reports the token).
- **Linear time (checkpoint note 1).** The linear-time test in `NextContentTests.fs` gained the linker's wide cases and a `presentation` step on every body: 9,900 URLs (one per paragraph, and among 9,900 text nodes), 9,900 addresses (both shapes), one text node of 16,000 URLs and one of 16,000 addresses. Bound 400 ms CPU. Measured warm on this host
  (load 4-8): 246, 295, 352, 244, 61 and 33 ms respectively, most of it the 5.4 render (85-180 ms for 9,900 paragraphs) and the parse, not the linker (the linker pass is 40-65 ms). 352 ms is close to the bound; the test warms each body once, and passes alone 3 of 3 under load 8.
  Under `bin/verify` (every assembly at once, load 9.8) the first run failed the *existing* "19,000 comments" layout case at 493 ms; the test passes alone.
- **Q1 list (checkpoint note 6)** unchanged here; not yet recorded in the README (5.6b). **Notes 4 and 5 not done in this unit:** the class B differences (a)-(c), fuzz 398, `isindex`, the select model and the L1 boundaries go into `known-differences.json` and the README with the flip (5.6b); the corpus shows only the L1 case for presentations, so
  `known-differences.json` has that one entry. The security-review fuzz harness is in the checkpoint session's scratchpad and not in the repo (searched `/private/tmp/claude-501/*/scratchpad`; not found); the oracle re-run on the presentation including autolink belongs to the 5.6 review.
- **KLOC:** `bin/kloc` 34,696 by upstream's rules, **34,868 corrected (34.9 KLOC)**, from 34,437 / 34,609 (+259). `Campfire.RichText` 7,325; `Next/Autolink.fs` is about 0.23 KLOC against the design's 0.02 for the node building (the design counted the matchers as kept; they stay in the old file until 5.7).
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (lint and self-test, Dapper and EF guards, Release warnings-as-errors build, every test): 1,084 total, 6 skipped (the environmental six). Two runs under load 8-10: the first failed the existing "wide bodies" case (19,000 comments, 493 ms), the second the known-flaky
  `FrontAcmeTests` "cached certificates load and are forgotten"; each passed alone (the wide-bodies test 3 of 3; `Campfire.Kit.Tests` 66 total, 0 failed), and every other test passed in both runs (RichText all 191 in the second). Not run: pixel slices and `bench/quick` (no app change; the App does not call `Next/` until 5.6b). Disk 29 GB free.
- **Next:** 5.6a (`webUrl`/`normalizeTweetUrl` on `System.Uri`, `RubyUri.fs` and `UriTests` to the App, `OpengraphMetadata`, `Attachables.embedFromContent`), then 5.6b (the flip, CorpusTests through known-differences.json, README, image, pixel slices, bench).

### Phase 5, unit 5.6a: URIs on System.Uri, RubyUri to the App, OpengraphMetadata on Html5 (2026-10-22)

The first half of the split 5.6 (checkpoint note 3). **Built:** (1) `Attachables.webUrl` and `Filters.normalizeTweetUrl` on `System.Uri` behind the RFC 3986 character check (R8): `Attachables.rfc3986Characters` (printable ASCII, none of `"<>\^`{|}`, one `#`, `%` + two hex digits)
and `Attachables.tryAbsoluteUri`. Both pipelines share the two functions, so the migration differential still compares like with like. (2) `RubyUri.fs` moved unchanged (git mv) to `src/Campfire.App/Integrations/RubyUri.fs` (namespace unchanged; compiled before `Net.fs`) and
`UriTests.fs` to `tests/Campfire.App.Tests/` (two local helpers in place of `Support`). `Campfire.RichText` no longer has a Ruby URI parser. (3) `OpengraphMetadata.stripTags` is `Next.Html5.parseBody` + the text nodes, serialized with `Html5.escapeText`; `sanitizeHtml` calls `Next.Sanitizer.sanitize` (R10). The App no longer builds the old `Dom`.
(4) `Attachables.embedFromContent` parses with `Next.Html5.parseBody` and reads attributes and text through `Html5`. To allow that `Next/Html5.fs` now compiles right after `Error.fs` (before `Attachables.fs`) and the `Attrs` type moved to `Error.fs`.
- **Decision: the query is exempt from the character check.** Ruby's RFC 3986 parser takes `[^#]*` for the query, and a corpus embed has `<img src="https://example.com/i.png?x=<y>">` (Rails links it). R8's check as worded dropped it (`C1` showed it as an unlisted difference); so the check skips the query (first `?` to first `#`),
  and a `%` followed by two non-hex characters inside the query is still no link (`?q=%zz`, Ruby's `query=`), a lone `%z` is a link. This is closer to Ruby than R8's 47 of 54: `web_url` is 48 of 54.
- **Class B, recorded in `corpus/known-differences.json`** (kind `web_url`, six URLs, and the two embeds they blank in presentation, plain text and editor value): `mailto:nobody` (Ruby raises `InvalidComponentError`, the message renders empty; ours renders the embed without a link), `https://.../x` (Ruby: `NoMethodError`),
  `https://example..com/`, `https://.example.com/`, `https://[v1.x]/`, a 20-digit port (Ruby accepts; `System.Uri` refuses: no link). Hostile, hand-made bodies only. `corpus_matches_rails` (the old-pipeline comparison) now reads `known-differences.json` for every kind and fails both ways (a listed case that no longer differs fails; `presentation` stale entries are C1's job on the new pipeline).
- **Tests:** a `web_url` test in `AttachablesTests` (query exempt, `%zz`, the character rules outside the query, own host, numeric host, non-http scheme); `ReferenceTests` webUrl tests, the corpus's 54 `web_url` vectors and every opengraph, webhook and push App test unchanged and green.
- **KLOC:** `bin/kloc` 34,743 by upstream's rules, **34,915 corrected (34.9 KLOC)**, from 34,696 / 34,868 (+47, the character check and `tryAbsoluteUri`, less `embedFromContent`'s `Dom` code).
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (lint and self-test, guards, Release warnings-as-errors build, every test): 1,085 total, 6 skipped (the environmental six), 0 failed on the clean run; two other runs under load 10-23 failed one test each (one in RichText, one in App) that passed alone. No pixel slice or bench: the App does not call `Next` for presentation until 5.6b.
- **Next:** 5.6b (the flip: `Presenter`, `Messages.canonicalBody`, `AppRichText`, `DbResolver` on `Next`; `CorpusTests` on the new pipeline; README "Rich text" entry with class B (a)-(c), fuzz 398, `isindex`, the select model, L1 and Q1; image, pixel slices, `bench/quick`; the security-oracle re-run).

### Phase 5, unit 5.6b: the flip (2026-10-22)

The App's rich text now runs on `Next`. **Built:** (1) `Presenter`, `Messages.canonicalBody`, `AppRichText` open `Campfire.RichText.Next` after `Campfire.RichText`, so `ActionText`, `Content` and the resolver-facing entry points (`presentMessage`, `toPlainText`, `mentionedUsers`, `editableValue`, `Content.load`/`toHtml`/`toRenderedHtmlWithLayout`) are the AngleSharp ones; `DbResolver` is unchanged (it implements the same `IAttachableResolver`). Nothing in the App calls the old `Dom`, `Content`, `ActionText` or `Autolink` now. (2) `corpus_matches_rails` runs plain text, editor values, mentions and `web_url` on `Next` and fails both ways through `corpus/known-differences.json`; presentations are C1/N2 (they need the new parser's loose DOM). Entries added: the L1 boundary for plain text, editor value and mentions; the select model for `editable` "malformed select", fuzz 188, fuzz 395; and S5's `plain_text` fuzz 362. (3) README "Rich text" entry: class B (a) end-tag lowercasing in raw-text/RCDATA, (b) raw-text-named elements in svg/math and table parts (15 cases), (c) limits counted on the body parse, plus L1, the select model, `isindex`, fuzz 398, comments after `</body>`, the URI rules and Q1 (upstream report to AngleSharp: the NextSibling cost and the raw-text lowercasing). fuzz 398, `isindex` and (a)-(c) are not corpus cases, so they live in the README only. (4) New test `S7` (seeded fuzz of 20,000 corpus bodies spliced with 31 hostile fragments through `presentMessage`; security assertions plus a no-comment check): 0 violations.
- **Gates run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: compiles with warnings as errors, App 318, RichText all green. Full-solution runs under load 10-13 failed `Cable GoldenTests replays reference frames` in two runs and `AuthenticationTests A6` in one; each passes alone (Cable 80/80 alone; `bin/verify App` 318/318, 0 failed). Opengraph, webhook and push App tests unchanged and green.
- **Image:** `CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build` at 5d9b6b1; label checked equal to HEAD. `bin/clean-docker` run.
- **Pixel parity** (`--fast`, `rooms/show/**,messages/**,search/**,interactions/edit_form*`, every seed): default 110/110, custom_styles 6/6 equal on screenshots, aria and loose DOM; crowd, first_run and restricted have no cells in the slice (0/0). `realtime/message_sent` 2/2 and `realtime/message_edited` 2/2, one at a time. PASSED. `bin/clean-parity` run.
- **Bench** (`bench/quick`, room page, messages page, search, post a message at c=1 and 16; 3 reps, fsharp-control and dotnetway interleaved, detached and polled; `bench/results/quick-56b`). Control = `campfire-dotnetway:app` as it stood (label 8abdb6b, the old pipeline, tagged `control-8abdb6b`), not a fresh build of 3a50347: between the two only 5.5/5.6a's unused `Next` code and the URI change differ on the request path. Host probe ratio 1.04-1.07 before and after each run, load before runs 1.0-1.5. Medians dotnetway vs control, req/s: room c1 2468/2483, c16 7373/7088; messages c1 2284/2274, c16 7277/7276; search c1 2101/2099, c16 6965/7033; post c1 1100/1131 (-2.7%), c16 2020/2048 (-1.4%). **vs Rails:** room 23.0x/30.8x, messages 10.5x/19.1x, search 10.9x/17.1x, post 6.6x (c1) and 6.1x (c16); no cell under 5x. All 24 runs 2xx, rows written equal 2xx.
- **KLOC:** `bin/kloc` 34,746 by upstream's rules, **34,918 corrected (34.9 KLOC)**.
- **Not done here:** the checkpoint's 151,087-output fuzz harness is out of repo and was not found; S7 is its in-repo, smaller replacement (20,000 outputs) and no Loofah run in the reference image was done for the presentation. 5.7 (delete the old parser) is next.

### Phase 5, unit 5.7: delete the old parser and pipeline (2026-10-22)

`plans/design-richtext.md` section 4, unit 5.7, with the 5.6 carry-over items handled first.
- **5.6 carry-over.** (1) `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on the unchanged 5.6b code at load 1.9: 1,086 total, 1,080 passed, 0 failed, 6 skipped (the environmental six); so Cable `GoldenTests` "replays reference frames" and `AuthenticationTests` A6 did not fail on a quiet host, no cause to chase. (2) Traced by deleting tokens while the old and new trees still differed: **fuzz 188** reduces to `<select><hr>` (Gumbo keeps the `<hr>` in the select, AngleSharp drops the start tag, so the editor value loses it);
  **fuzz 395** to `<template><form><summary></form>text` (`</form>` closes the `<summary>` for Gumbo and not for AngleSharp, so the text stays in the summary); **fuzz 362** to `<select><option><span>` (the 2025 select content model keeps an element inside an option, Gumbo flattens it). The 5.6b descriptions of 188 and 362 were wrong (188 was not a table case, 362 had nothing to do with `]]>`); all three are corrected in
  `corpus/known-differences.json` and in `Html5Tests`' P1 list comment. 125, 177 and 325 are still S2's reading, not traced. (3) Opus checkpoint note 4's items ((a) end-tag lowercasing in raw text, (b) raw-text elements in svg/math and table parts, (c) limits counted on the body parse, fuzz 398, isindex) stay in the README only: `known-differences.json` is keyed by corpus case name and `CorpusTests` fails both ways (a listed case that no longer differs fails), so a non-corpus shape cannot be listed there without making the test fail; the file's header-less layout has no place for them.
- **Frozen trees (first, before anything went).** `corpus/trees.jsonl` (1,489 lines: the 1,479 inputs by label, 658 bodies + 657 Rails presentations + 164 seed bodies, plus 10 P2 strings as `p2:<html>`) and `corpus/context-trees.jsonl` (3,835 element-context trees: P4's six contexts x the corpus's plain texts and ten strings, foreign cases set aside as before), both written once from the old `Dom` by a temporary test fact, then the fact was removed. `Html5Tests` P1, P2 and P4 read them and still pass: P1's difference list is the same eight names. Together 4.7 MB.
- **Deleted.** `Html5ever/` (4 files), `Dom.fs`, `Library.fs`, the old `Content.fs`, `Filters.fs`, `Autolink.fs` (with `TagIndex`, `BracketCounts`, `openAnchorEnd`, `sanitizeWithEscapedAttributeBrackets`), `PlainText.fs`, `ActionText.fs`, the old arena `Sanitizer.fs`; `TokenizerTests`, `DomTests`; the migration differential (M1, C2, C3, S1 and P1's live comparison); `bin/richtext-differential`; `reference-tools/richtext/differential/` (Rust and F# sides); the entity table generator in `script/generate-tables.py` (the word-range generator stays).
- **Moved.** `Next/*` to the root namespace and the project root (the `Next/` folder is gone; the App's `open Campfire.RichText.Next` lines are removed, `OpengraphMetadata` says `Html5` and `Sanitizer`). What the new modules needed from the old ones: `ParseError` (to `Error.fs`; `DomLimits` is gone, `ParseError`'s message text is unchanged), `Presentation` (to `ActionText.fs`), `normalizeTweetUrl` (into `Filters`), and the autolink matchers (`urlMatchAt`, `emailMatchEnd`, `isEmailLocalChar`, `openingBracket`, `UrlMatch`) into `AutolinkMatch.fs` (internal, compiled before `Autolink.fs`). Compile order is now `Unicode`, `RubyExt`, `Loofah`, `Error`, `Html5`, `Attachables`, `Sanitizer`, `PlainText`, `Content`, `Filters`, `AutolinkMatch`, `Autolink`, `ActionText`.
- **Tests.** The old-versus-new tests are gone with their differential; every other test now runs on the one pipeline. `ReferenceTests` and `HardeningTests` lost the `usingNext` theory (a `[<Fact>]` each; the three hardening tests whose expectations differed between pipelines keep the new-pipeline ones, R3). `NextContentTests` is `ContentTests` (C4, nesting to 8 deep, attribute lookups, S6, the linear-time test with its wide cases),
  `NextAutolinkTests` is `AutolinkTests` (A1-A3, segments, wide), `NextSanitizerTests` is `SanitizerVerdictTests` (S3's `HtmlDecode` trial and `isindex`), `SanitizerTests` is the Rails vectors on the one sanitizer. `CorpusTests` and `Support.parsedMarkup` parse with `Html5.parseBody` where they used the old `Dom` as the browser model (the loose DOM, the security assertions, S7): 113 RichText tests, all pass, so the security oracle and the 1,479-input comparisons are unchanged by the switch of model.
  **What the deletion costs in coverage:** the byte-level agreement of canonical bodies (what posting stores), `body.html` layout output and filtered content with the old pipeline is no longer tested (the corpus has Rails' outputs for plain text, editor values, mentions and presentations, which `CorpusTests` compares; it holds a `filtered` field that nothing compares yet). They were recorded at 5.4-5.6b: layout 6 differing inputs, 5 on P1's list and fuzz 398; filtered 1; editor values 8, all on P1's list.
- **Acceptance.** `grep -rIn "Html5ever\|NodeId\|Dom()\|richtext-differential\|TagIndex" src tests bin AGENTS.md`: nothing but `closeTagIndex` in the vendored `lexxy.js` (a JavaScript variable in `src/Campfire.Assets/vendor`, not ours) and the frozen corpus file names. `AGENTS.md`: the layout row, the `bin/richtext-differential` rule (replaced by a "Rich text" paragraph: `Html5` and its limits, the Nokogiri serializer, the walker, autolink, the node rules, the corpus, `known-differences.json`, the frozen trees, the README's non-corpus differences).
- **KLOC.** `bin/kloc` 30,044 by upstream's rules, **30,216 corrected (30.2 KLOC)** (from 34,746 / 34,918 at 5.6b: -4.7 KLOC with the deletion); `Campfire.RichText` **2,264** (the design projected about 1.75 for RichText and about 29.7 for the project, so +0.5 on both). Where it went over: `Html5.fs` about 0.5 (limits bookkeeping, comments, frameset, the parse routes, the node helpers the walker needs), `Attachables.fs` 0.63 (the URI rules on `System.Uri`, `embedFromContent`), `Autolink.fs` + `AutolinkMatch.fs` 0.36 (the matchers are the old ones whole, the design counted them as kept). Every identifier in `src/Campfire.RichText` is referenced outside its definition except `Html5.parseCount` (the test-only parse counter, A3), so there is no dead code to cut; the rest is a design question for the gate.
- **Gates run.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (lint and self-test, the Dapper and EF guards, Release warnings-as-errors build, every test): **clean on the last run: 1,008 total, 1,002 passed, 0 failed, 6 skipped** (the environmental six; the total is 78 lower than at 5.6b: the retired old-pipeline tests). Four earlier full runs on this code failed one load-sensitive test each, a different one each time (the linker's wide-bodies test at 536 ms against 400 under load 8, `FrontAcmeTests` cached certificates twice, Cable `TransportTests` P11 at 551 ms, Cable `ProtocolTests` L5), and every one passed alone (RichText 113/113 twice, Kit 66/66, Cable 80/80 three times).
  Image: `CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build` at 7c68ff9, label equal to HEAD, `bin/clean-docker` run. **Pixel parity**, the same `--fast --only 'rooms/show/**,messages/**,search/**,interactions/edit_form*'` slice on every seed: default **110/110** screenshots, aria and loose DOM equal, custom_styles **6/6**, crowd, first_run and restricted 0/0 (no cells in the slice); `realtime/message_sent` 2/2 and `realtime/message_edited` 2/2, one at a time. PASSED, 380 s. The first attempt hung with 3 of 112 cells outstanding for 46 minutes (the app was serving; the harness never finished the last cells): killed, `candidate down --all`, rerun in full and green; its two `interactions/edit_form` cells passed on their own run too. `bin/clean-parity` run after each. No bench: no change on the request path (only namespaces and the deleted code); the gate re-benches against a fresh control.
- **Remaining for the phase gate / Phase 5 close.** (a) The 5.6 remaining list: the checkpoint's 151,087-output fuzz harness is out of repo (S7's 20,000 outputs stand in) and no Loofah comparison of the presentation was run in the reference image (`docker run` the reference with `bin/rails runner`, about 30 s for 4,600 inputs); (b) the bench control must be a fresh build of the base commit; (c) Q1 (upstream report to AngleSharp: the `NextSibling` cost and the raw-text lowercasing) is in the README, not filed; (d) fuzz 125, 177 and 325 descriptions untraced; (e) `filtered` is in the corpus and not compared.

### Phase 5: gate (2026-10-22)

Evidence: `bench/results/dotnetway-p5/` (slim: logs, `report.json` per pixel slice, bench JSON; 2.6 MB). Gate runner; no code change was needed. Image `campfire-dotnetway:app` built from `4e03f48` (label checked equal to HEAD); control `campfire-dotnetway:p4ctl` built from `b2322f3` (Phase 4's closing commit) in a scratch worktree with the submodule copied in, label `b2322f3`, removed afterwards (the first control attempt failed at the Dockerfile's `COPY reference/hooks` because the script lacked the submodule copy; rerun with it).
- **`CAMPFIRE_REQUIRE_SEED=1 bin/verify`** on `4e03f48`: 1,008 total, 1,002 passed, 0 failed, 6 skipped (the environmental six); lint self-test (39 planted files) ok, lint 337 files clean; the Dapper and EF guards, H1 and the Release warnings-as-errors build green (`verify.log`). The Phase 5 oracle tests are part of that run: RichText 113 (the 1,479-input corpus through `known-differences.json`, the security assertions "same DOM after sanitizing, nothing Loofah strips survives", the hardening inputs, S7's 20,000 fuzzed bodies), App 318. No load-sensitive failure on this run (host load 3-10).
- **Phase-specific pixel slice** (`--fast --only 'rooms/show/**,messages/**,search/**,interactions/edit_form*'`): default **110/110** screenshots, aria and loose equal, custom_styles 6/6, the other seeds 0 cells; PASSED. Then the same four globs inside the lean gate below.
- **Lean pixel gate** (25 slices run with `--only`, `realtime/**` one state at a time, every engine of the lean matrix): **940 of 940 cells equal on screenshot, aria and loose DOM**, no cell NOT CAPTURED, every slice PASSED, so nothing to re-run.

| seed | screenshots | aria | loose DOM |
|---|---:|---:|---:|
| default | 860/860 | 860/860 | 860/860 |
| crowd | 24/24 | 24/24 | 24/24 |
| custom_styles | 32/32 | 32/32 | 32/32 |
| first_run | 16/16 | 16/16 | 16/16 |
| restricted | 8/8 | 8/8 | 8/8 |

  (The full matrix was not asked for at this gate; Phase 4's open items, the Firefox empty-404 pair and the WebKit WebP avatar flake, are untouched by this phase and unchanged.)
- **`bin/cookie-roundtrip`** 20/20 both ways. **`bin/db-differential`** rc 0 (schemas agree, reference boots on the database we wrote, rollback ok).
- **Speed** (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps, interleaved new/ctl/ctl/new/new/ctl, each rep in two parts; new = `4e03f48`, control = `b2322f3`; host probe 1.044-1.081 at the start and end of every rep of both sides (one per-window probe of 1.127 after the new image's sidebar c=1 window, flagged by the report), Mac load average 1.3-2.8 at rep boundaries (3-4.5 at the driver's marks), 0 non-2xx and 0 connection errors in 30 runs per side, post rows equal to the 2xx answers, decoded bytes equal to the stored sizes):

| workload | c | new req/s | control req/s | new vs control | x Rails (new) | x Rails (control) | CPU us/req new / control |
|---|---:|---:|---:|---:|---:|---:|---|
| room page | 16 | 7,299 | 7,472 | -2.3% | 30.50 | 31.22 | 448 / 440 |
| room page | 1 | 2,383 | 2,453 | -2.9% | 22.25 | 22.90 | 515 / 505 |
| messages page | 16 | 7,031 | 7,265 | -3.2% | 18.47 | 19.09 | 472 / 456 |
| messages page | 1 | 2,206 | 2,264 | -2.6% | 10.12 | 10.39 | 550 / 539 |
| sidebar | 16 | 7,500 | 7,554 | -0.7% | 12.53 | 12.62 | 436 / 434 |
| sidebar | 1 | 2,217 | 2,393 | -7.4% | 7.74 | 8.35 | 547 / 513 |
| search | 16 | 6,923 | 7,029 | -1.5% | 16.97 | 17.23 | 488 / 481 |
| search | 1 | 2,125 | 2,162 | -1.7% | 11.03 | 11.22 | 569 / 557 |
| post a message | 16 | 1,994 | 2,027 | -1.6% | 6.04 | 6.14 | 1,509 / 1,465 |
| post a message | 1 | 1,082 | 1,144 | -5.4% | 6.49 | 6.85 | 1,691 / 1,639 |

  **No cell under the 5x floor** (thinnest: post c=16 6.04x, post c=1 6.49x, sidebar c=1 7.74x); no speed fix made. Cost of Phase 5 against Phase 4's close: -0.7% to -3.2% at c=16 and -1.7% to -2.9% at c=1 on the pages, +1% to +7% CPU per request; the two larger drops are sidebar c=1 (-7.4%, the cell the 1.127 probe flagged, its CPU +7%) and post c=1 (-5.4%, CPU +3%, the AngleSharp parse in `canonicalBody`). The sidebar renders no rich text, so its c=1 drop is most likely noise (the control ranges 2,337-2,435 against the new 2,204-2,339).
- **KLOC:** `bin/kloc` 30,044 by upstream's rules, **30,216 corrected (30.2 KLOC)**; `Campfire.RichText` 2,264, App 12,130, Views 5,219. Unchanged by this unit. Phase 4 closed at 33.7, so the phase is -3.5 (the design projected -4.8, about 28.9: RichText runs +0.5 over, as 5.7 recorded).
- **Differences recorded:** none new. Still open from 5.7's remaining list (not gate items): the checkpoint's 151,087-output fuzz harness is out of the repo and no Loofah comparison of the presentation was run in the reference image; Q1 (upstream report to AngleSharp) is in the README, not filed; fuzz 125, 177 and 325 untraced; `filtered` is in the corpus and not compared. From Phase 4: the Firefox empty-404 pair, the 16 KB Cable bound and the two test packages.
- Disk: 20-28 GB free throughout; `bin/clean-docker` and `bin/clean-parity` run; the control image is removed.

### Phase 5: security review of the rich text layer (2026-10-22)

Opus reviewer, on `414caa8`. Findings and method in `plans/design-richtext.md` section 11 ("Security review"); fixes in `d076556`.
- **Read:** the 11 `Campfire.RichText` modules Phase 5 wrote or moved, the App's 5 call sites (presenter, canonicalization, `AppRichText`, opengraph
  metadata, bot replies) and the 5 view files the presentation crosses (`Trusted.ofSanitized`, the slot split, the message markup, the presentation and
  edit templates); the oracle tests.
- **Fuzzed** with a harness now kept at `reference-tools/richtext/review/` (the checkpoint's was lost): the corpus, Rails' presentations, 179 fixed hostile
  cases and grammar-generated bodies through every entry point (presentation, `body.html`, the four sanitizer lists, stored-then-presented, plain text,
  mentions, editor value, unfurl text), each HTML output parsed in a browser model (scripting on, inside the presentation's `div`, sentinel after).
  201,494 inputs / 1,584,965 outputs before the fix, 251,494 / 1,978,388 after. **Against Loofah:** 4,179 hostile cases through the reference image's own
  pipeline (`reference-tools/richtext/review/run.sh`): 4,023 presentations byte-equal, 156 differ (parse differences already class B); 0 browser-model
  violations in ours, 82 in Rails' (the `name` attributes the port drops).
- **F1 (High, fixed):** AngleSharp 1.8.4/1.8.5 throws a `NullReferenceException` on `<p><select><xmp></xmp><select>x`; nothing caught it, so posting it was a 500
  and the same body stored another way (Rails before a switch) failed every page, broadcast and search showing it. `Html5.guarded` now maps any parser
  exception to `ParseError.ParserFailed` (refused as a body past a limit: `messages/_unrenderable`, plain text empty and logged, stored as it came). README
  class B entry. Tests failing before: two `HardeningTests`, `MessagesTests` "a body the parser throws on ...", S7. After: 0 exceptions in 1.98 million outputs.
- **F2 (Medium, fixed):** the security oracle (N2, S7) parsed outputs with `Html5.parseBody` (our parser, scripting off), blind to mutation XSS and container
  escape by construction. Both now also run the browser model; planted defects only it catches.
- **F3 (Low, accepted):** direct sanitizer outputs and `body.html` are not structure-stable (a flattened `template` leaves `table > div > table`; 44 of 1.98
  million outputs close a surrounding `div` when a browser re-parses them); never a presentation (the final auto-link pass re-parses and unwraps only flow
  elements), and those outputs reach no page. **F4, F5, F6 (info):** N1 holds on the corpus but not literally on hostile parse differences (45 of 4,179 keep
  allowlisted extras Rails' output lacks, none unsafe); Rails behaviours kept (unsanitized editor value, `body.html`'s `url`/`content`/`embed`, unsigned-SGID
  mentions, rails_autolink's schemes); the browser model is AngleSharp, not a real browser (a Playwright replay is the open item).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `d076556`: 1,011 total, 1,005 passed, 0 failed, 6 skipped (the environmental six; +3 new tests); lint
  self-test 39/39, 337 files clean; the Dapper and EF guards, H1 and the Release warnings-as-errors build green. No pixel slice and no bench: the change only
  turns an exception into the existing refusal path (no output changes for any body that rendered before, no work added on the request path but a `try`).
- **KLOC:** `bin/kloc` 30,048 by upstream's rules, **30,220 corrected (30.2 KLOC)**, +4 (`ParserFailed`); `Campfire.RichText` 2,268. The harness lives in
  `reference-tools/` (outside the solution and the count).
- **Differences recorded:** one class B (README "Rich text": markup the parser throws on is refused). Q1's upstream list gains the select-mode exception.
  Disk 28 GB free throughout; no image rebuilt.

### Phase 5: independent verification (2026-10-22) - PHASE 5 DONE; Phase 6 GO

Opus verifier, adversarial review of `c88f590..74b4908` (5.1-5.7, the checkpoint, the gate, the security review) against `plans/design-richtext.md`
and D1-D9, with the cheap checks re-run. Evidence in `bench/results/dotnetway-p5-verify/`. Fix in `853604f` (docs and a comment; no behaviour change). **Phase 5 closes at `853604f`** (the last code;
this entry and its evidence are the commit after it).
- **Deleted, checked:** `Html5ever/`, `Dom.fs`, `Library.fs`, the old `Content`/`Filters`/`Autolink`/`PlainText`/`ActionText`, the arena walker,
  `TokenizerTests`, `DomTests`, `bin/richtext-differential` and `reference-tools/richtext/differential/` are gone; `grep` for `Html5ever`, `NodeId`,
  `DomContext`, `DepthLimit`, `TagIndex`, `richtext-differential`, `sanitizeWithEscapedAttributeBrackets` over `src`, `tests`, `bin`, `parity`,
  `AGENTS.md` and `README.md` finds only the vendored `lexxy.js`. `RubyUri.fs` lives in the App until Phase 8 (R9). No HtmlSanitizer package (R4, a
  D9 call: Loofah's rules are parity). AngleSharp pinned at 1.8.4.
- **Pixel evidence, checked and re-run.** The gate's `report.json`s hold 942 lean records with pixels, aria and loose equal: 940 distinct cells plus
  the harness's own retry of `auth/sign_in/error` (two phone cells "not ready after 30000 ms" on the first attempt, equal on the retry; the gate's
  "nothing needed a re-run" means after that automatic retry). Its fast slice is 116/116. **On the closing code** (image rebuilt at `853604f`, label
  equal to HEAD, `bin/clean-docker` run): `--fast --only 'rooms/show/**,messages/**,search/**,interactions/edit_form*'` on every seed: default
  **110/110**, custom_styles **6/6** (the other seeds have no cells) on screenshots, aria and loose DOM; `realtime/message_sent` 2/2 and
  `realtime/message_edited` 2/2, one at a time. PASSED; `bin/clean-parity` after each.
- **Gates re-run.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `74b4908` and again on `853604f`: **1,011 total, 1,005 passed, 0 failed, 6 skipped** (the
  environmental six), lint self-test 39/39 and 337 files clean, the Dapper and EF guards, H1 and the warnings-as-errors build green (load 4-11, no
  flaky failure). `bin/kloc` matches the security review's figures.
- **Security, re-checked independently.** (1) **F1's fix is load-bearing:** with `guarded`'s catch-all narrowed to `ArgumentNullException`, three
  RichText tests fail (the two `HardeningTests` and S7) and `MessagesTests` "a body the parser throws on fails neither the post nor the pages that show
  it" fails; restored (`sabotage-f1.txt`). Every AngleSharp parse in `src` goes through `Html5.guarded` (`parseBody`, `parseIn` both routes,
  `setInnerHtml`); nothing else calls `ParseDocument`/`InnerHtml`. (2) **Fuzz with a new seed** (`reference-tools/richtext/review`, 100,000
  generated + the fixed cases, seed 17): **101,494 inputs, 798,599 outputs, 0 exceptions, 0 mutations, 0 violations in any presentation or stored
  presentation**. Its 39 violations are F3's class (direct `sanitize` outputs and `body.html` leaving a container or refused by our own depth limit
  after the layout's `div`) plus one `body.html` `style="color: expression;"`: a bare keyword that Lexxy's colour rule (`[a-z]+`) lets through,
  inert without parentheses (the harness flags the substring; info, not a finding). (3) **26 hand-written shapes** (`probe.fsx`,
  `probe-output.txt`): F1 and five neighbours (only the F1 shape throws; refused as `ParserFailed`, 3,000 copies too), nested selects, foster
  parenting, formatting elements reopened 300 x 30 times, 3,000 nested `a`, `foreignObject`/`annotation-xml`/`caption` towers, 9,000 `noscript`,
  16,000 URLs, 3,000 attachments, attachments nested 10 deep, `isindex`, a tabbed `javascript:`, `svg a`, a `noscript` breakout: nothing raised
  (presentation, plain text, editor value), every shape under 160 ms; the `javascript:` link lost its `href`, `svg a`, `isindex` and the `noscript`
  breakout presented empty. (4) `Trusted.ofSanitized` is still the presenter's only way to
  place rich text and still rewrites U+E000 (Phase 4's condition).
- **Fixed (`853604f`).** README "Rich text" lacked three things the design's section 8 lists: the caps (10,000 elements, 40,000 tokens, 400 root
  attributes: refused where Rails renders), two of L1's three depth boundaries (400 `<b>` then `</p>` refused, reopened formatting elements then a
  comment rendered), and the class A attribute-value bytes. AGENTS.md now says any AngleSharp exception is `ParserFailed` and how to re-run the review
  harness. `AppRichText.MentionedUserIds`' comment said a parse fails only past the limits; since F1 a short body can fail too (same answer, no
  mentions; only that log line is skipped, `ToPlainText` logs the failure on save).
- **Speed.** The gate's table stands (`4e03f48` against a same-session `b2322f3` control; host probe 1.044-1.081, load 1.3-2.8; 0 non-2xx): **no
  cell under 5x**, thinnest post c=16 6.04x, post c=1 6.49x, sidebar c=1 7.74x; the phase costs -0.7% to -3.2% at c=16 and -1.7% to -7.4% at c=1,
  CPU per request +1% to +7%. Since then only `d076556`'s `try`/`with` around a parse and comments changed: no work on a parse that succeeds, so no
  re-bench.
- **KLOC.** `bin/kloc` **30,048 by upstream's rules, 30,220 corrected (30.2)**; `Campfire.RichText` 2,268. Phase 4 closed at 33.7, so the phase is
  **-3.5** against the design's -4.0 (29.7, range 29.4-30.0): 0.2 over the range, all of it RichText (2.27 against 1.75: `Html5` limits and parse
  routes, the URI rules on `System.Uri`, the matchers kept whole). The project is under Rust's 31.0 for the first time; D7's bar is Phase 9's.
- **Differences recorded:** README "Rich text" amended as above; `known-differences.json` unchanged (5 kinds, 20 entries, each pinned both ways by
  `CorpusTests`).
- **Open, none blocking:** Q1 (report AngleSharp's defects upstream, now with the select-mode `NullReferenceException`) waits on Chris; F6 (replay
  the harness's outputs in real browsers in the parity image); fuzz 125, 177, 325 untraced; the corpus's `filtered` field not compared; the byte
  agreement of canonical bodies with the old pipeline is no longer tested (5.7); Phase 4's carried items (WebKit WebP avatar flake, 16 KB Cable bound,
  the two test packages for Phase 8).
- **Phase 6 GO.** Conditions: keep AngleSharp pinned (bump only with P1's frozen trees green and the review harness at 0 exceptions and 0
  presentation violations); parse only through `Html5`; keep `Trusted.ofSanitized` the one way rich text reaches a page. Phase 6 moves every asset
  path, so run every state `--fast` (any wrong pin breaks interaction states), then the lean matrix on `default`, as the plan's row says.

### Phase 6, unit 6.1 (harness masks), 2026-10-22

- **Built (no app change; `git diff --stat src` empty).** The harness learns the SDK's fingerprint form `/assets/<name>.<10 [0-9a-z]><ext>` beside Propshaft's
  digest, both masked to `/assets/<name>-«digest»<ext>` (design-assets.md A15): `parity/capture/normalize.ts` (`ASSET_DIGEST`; new `describeAssetPath`),
  `network.ts` (`DIGESTED_ASSET` both forms plus a plain `/assets/` URL, described as digested and its body "named by its digest", network layer only),
  `overrides.ts` (`src/Campfire.App/Assets/overrides` tried first; the pattern takes both forms), `tests/Shared/LooseDom.fs` (same alternative),
  `reference-tools/campfire/controllers_a/replay.py` (mask). A plain URL in the DOM layer is untouched, so a page that lost its fingerprints still differs.
- **Tests.** New cases in `loose.test.ts` (Propshaft and fingerprinted URL of one logical path equal; `/assets/highlight.js/core.abcdefghij.js` and
  `/assets/stimulus.min.ubi9oscxp6.js` masked; plain `/assets/languages/javascript.js` unchanged and a plain/fingerprinted pair still differs; an
  import map with both forms equal), `normalize.test.ts` (mask and `describeAssetPath`), `overrides.test.ts`, and an F# twin in `LooseDomTests.fs`.
  `parity/bin/test-loose-dom` 29/29.
- **Goldens.** `parity/bin/loose-golden` rerun: `loose/golden/a` and `oracle/00-18` byte-unchanged (no asset URL in them has either form that the old mask
  missed). It also rewrote `oracle/19-24` and added `25-30`: that is selection drift (the script takes up to 30 answers of the 1,037-line `oracle.jsonl`
  trimmed to 24 in 4.11), unrelated to the masks; the committed files were kept, because W2 compares the port with them and passes.
- **Gates.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: 1,012 total, 1,006 passed, 0 failed, 6 skipped (the environmental six). `bin/pixel-parity --fast
  --only 'auth/sign_in*,rooms/show/designers'` on `default`: 4/4 screenshots, aria and loose equal. `bin/clean-parity` run. Disk 26 GB free.
- **KLOC** `bin/kloc`: 30,048 by upstream's rules, 30,220 corrected (unchanged).

### Phase 6, unit 6.2 (staging), 2026-10-22

- **Built (no served response changes; no `.fs` under `src` touched).** `git mv src/Campfire.Assets/{vendor,overrides,script,OVERRIDES.md}` to
  `src/Campfire.App/Assets/`; `revendor`'s `ROOT`/`CRATE` retargeted (and the path mentions in `export_reference.rb`, `vendor/MANIFEST.md`); the old
  `Campfire.Assets.fsproj` `AssetInput` globs and `--assets` argument point at the new directory, so the old pipeline still serves. `Campfire.App.fsproj`
  gained the `CampfireStageWebRoot` target and the `importmap.rb` `EmbeddedResource` exactly as design-assets.md section 3; `.gitignore` gained
  `src/Campfire.App/wwwroot/`; README's overrides link fixed; `bin/check-assets-incremental` rewritten for the staging target.
- **Acceptance.** `dotnet build` stages 311 files in `wwwroot/assets` + 5 public files (404/422/500/502.html, robots.txt). `campfire.staticwebassets.endpoints.json`
  has a fingerprinted endpoint (label + fingerprint) for each of the 316 (plus the SDK's `.gz` variants: 539 fingerprinted asset files in all). `bin/check-assets-incremental`
  passes (added override staged, in the manifest with a fingerprinted route; deleted, gone from both). New App test S1 (`tests/Campfire.App.Tests/AssetsTests.fs`):
  staged set = Rails' manifest minus the 4 lexxy `.gz`/`.br` plus `install-edge.svg`, bytes equal the sources (overrides first, then LOAD_PATH order).
- **Gates.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): 1,013 total, 1,007 passed, 0 failed, 6 skipped (the environmental six). No pixel run
  (no response changes). Disk 26 GB free.
- **KLOC** `bin/kloc`: 30,048 by upstream's rules, 30,220 corrected (unchanged).

### Phase 6, unit 6.3 (host and table), 2026-10-22

- **Built.** `src/Campfire.App/Web/StaticAssets.fs` (module `StaticAssets`, before `Services.fs`): `ManifestPath`, `hostOptions` (ApplicationName `campfire`,
  WebRootPath `<base>/wwwroot`, content root unchanged), `configureHost` (`UseStaticWebAssets`), `map` (`UseRouting`; the marker middleware that sets
  `SkipRequestHeaders` for `StaticAssetDescriptor` endpoints; `UseEndpoints` + `MapStaticAssets ManifestPath`), the process-wide `FrozenDictionary` table
  (logical path to fingerprinted route: descriptors with no selectors, no `Content-Encoding`, a `fingerprint` and a `label` under `assets/`), the startup
  validation (every descriptor's `AssetPath` must exist in `WebRootFileProvider`, else `InvalidOperationException` naming the web root and `hostOptions` +
  `configureHost`), `path` (raises `MissingAsset`; `InvalidOperationException "static assets are not mapped"` before `map`), `webRootFile`.
  `Front.serveWithLines` takes the `WebApplicationOptions` and a `configureHost` callback (`Front.hostArgs` holds the old `reloadConfigOnChange=false`
  argument; `serveWith`/`serve` and the Kit test harness pass it and `ignore`); `Boot` passes `StaticAssets.hostOptions` and `configureHost`;
  `Support.startPipelineWith` uses them (new `startPipelineOn` takes both explicitly). `Web.Services.pipeline` calls `StaticAssets.map` right after the old
  `publicFiles` middleware.
- **Differences from the design note found while building (recorded, both internal).** (1) `StaticAssetsEndpointDataSource` is not a public type in
  11.0.0-rc.2 and `StaticAssetDescriptor` has `Properties`, not `EndpointProperties`: the descriptors are read from every `EndpointDataSource` in the route
  builder through the public `endpoint.Metadata.GetMetadata<StaticAssetDescriptor>()` (the helper class is still not used). (2) A build output (tests,
  `dotnet run`; not a publish) adds a catch-all fallback route `{**path:file}` to the asset endpoints, and the two `UseRouting` passes share one set of
  endpoints. That fallback matched every request (a method mismatch became a 405 endpoint for POST/DELETE on any path, GET `/cable` a 404: 103 App tests
  failed on the first run). Fix: after the first `UseRouting` the marker middleware drops an endpoint that is not an asset descriptor unless the request
  path is one of the asset routes (so POST to an asset URL is still the framework's 405, H4); `StaticAssets.appEndpointsOnly` runs after the app's own
  `UseRouting` and keeps only the app's own `RouteEndpoint`s (`/cable`). In a publish there is no fallback and both are no-ops (`GetEndpoint` is null).
- **Tests** (`tests/Campfire.App.Tests/AssetsTests.fs`): H1 (all 311 table routes 200 with the staged file's bytes; gzip via `Accept-Encoding` decodes to the
  same bytes; HEAD has no body), H2 (ETag gives 304; `Range: bytes=0-9` on an mp3 gives 206, 10 bytes), H3 (no `x-request-id`/`x-runtime` on an asset, both on
  a page), H4 (`/assets/nope.css` is the app's 404; POST to an asset URL is 405), and a host built without `configureHost` fails at boot with
  "not in the web root ... UseStaticWebAssets". A HEAD response carries no `content-length` through `MapStaticAssets` (not asserted).
- **Gates.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors, lint, Dapper guard, H1 zero compilations): 1,018 total, 1,012 passed, 0 failed,
  6 skipped (the environmental six). Image rebuilt (`parity/bin/candidate build`, label 47eb5b8) and the control `campfire-dotnetway:p5ctl` built once from 853604f
  in a scratch worktree with the submodule copied in (worktree removed; the image stays for 6.5/6.6 and the phase gate); the published image boots (validation passes
  in the publish layout). `bin/pixel-parity --fast --only 'auth/sign_in*,rooms/show/designers'` on `default`: 4/4 screenshots, aria and loose equal.
- **Bench** (`bench/quick --reps 3 --apps fsharp,dotnetway --routes post_message,sidebar`, `FSHARP_IMAGE=campfire-dotnetway:p5ctl` as the control, interleaved,
  detached and polled; `bench/results/quick-63`). Host probe ratio 1.03-1.09 before and after every window, load before runs 1.07-1.38; 12 runs all 2xx, 0
  errors, rows written equal 2xx. Medians new / control (req/s): sidebar c1 2,562 / 2,530, c16 7,734 / 7,727; post c1 1,123 / 1,111, c16 2,011 / 2,028 (new
  1,954-2,047 inside the control's 1,970-2,050). CPU per request, new / control: sidebar c1 479 / 490, post c1 1,639 / 1,664. **vs Rails:** sidebar 8.94x (c1) and
  12.92x (c16), post 6.73x (c1) and 6.09x (c16); no cell under 5x and no drop beyond the control's range (the asset routing pass costs nothing measurable).
- **KLOC** `bin/kloc`: 30,158 by upstream's rules, 30,330 corrected (+110 over 30,220: `StaticAssets.fs` 147 lines written for the asset layer, still before
  the 6.7 deletion of `Campfire.Assets` -429).

### Phase 6, unit 6.4 (tags and import map), 2026-10-22

- **Built.** `src/Campfire.App/Web/Importmap.fs` (`Pin`, `Importmap.pins`, `Importmap.tags`): the `pin` and `pin_all_from` reader with importmap-rails 2.2.2's
  rules (a later pin of a name replaces it in place; the directory's module name drops the extension and a trailing `index`; `to:`, `under:`, `preload:`; an
  unsupported statement, option or value raises); `pin_all_from` lists the table's logical paths under `<under>/` ending `.js`/`.jsm` in path-component order
  (no directory is read); a pin whose file is not in the table is skipped; `tags` writes the `ImportMapDefinition` JSON in its script element, one
  modulepreload per preloaded pin's distinct URL, then the module script. `StaticAssets.fs`: the loaded state is one record (table, stylesheet tags, preload
  header, import map tags) made once in `map`'s load from the descriptors and the embedded `importmap.rb`; `stylesheetTags ()` (every `.css` logical path,
  ordinal order, `data-turbo-track="reload"`), `preloadLinks ()` (Rails' 1,000-byte rule, the comma counted: a link that would pass 1,000 is left out, a later
  shorter one may fit), `importmapTags ()`, and `logicalPaths ()` (the table's keys, for the tests). Not used by any page yet (no response changes).
- **Fixtures.** `git mv tests/Campfire.Assets.Tests/reference tests/Campfire.App.Tests/Assets/reference`; `revendor`'s `FIXTURES`, `export_reference.rb`'s
  comment, the old `ReferenceTests.fs` and S1 point at the new place.
- **Tests** (`AssetsTests.fs`): T1 table = staged set, fingerprint form, `MissingAsset`; T2 bytes equal Rails' compiled files except exactly A12's 11 (310
  compared, 299 equal); T3 31 stylesheets in Rails' order by logical path, equal line for line after the A15 mask; T4 89 pins (names, order, logical targets), 89
  modulepreloads, the module script last, text equal after the mask; T5 preload header 15 links in at most 1,000 bytes, a prefix of Rails' 16 by logical path,
  and no omitted link would fit; T6 the reader on small sources. All green on the first run of the real code (T6's expected order was my slip: `a/b`, `a-b`,
  `index`, `z` is path-component order).
- **Planted failures (run once each, reverted, working tree identical to the commit).** A wrong pin (`initializers/current` pointing at `initializers/highlight.js`)
  fails T4; a dropped `pin_all_from` (`vendor/javascript/languages`) fails T4; a reordered stylesheet list (descending) fails T3 and T5.
- **Gates.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors, lint 339 files clean, Dapper guard, H1): 1,024 total, 1,018 passed, 0 failed, 6
  skipped (the environmental six). No pixel run or bench (no response changes).
- **KLOC** `bin/kloc`: 30,195 by upstream's rules, 30,367 corrected (+37 over 30,330: `Importmap.fs` and the load and accessors in `StaticAssets.fs`, before the 6.7
  deletion of `Campfire.Assets` -429). The design projected 45 lines for `Importmap.fs` alone (105 lines on disk, comments included).
- **Differences recorded.** None new: the 15-link preload header and the import map's text are A5/A6's class A entries.

### Phase 6, checkpoint after 6.4: Opus review (2026-10-22) - GO for 6.5, with notes

Adversarial review of `e58de72..9e31f63` (6.1-6.4) against `plans/design-assets.md` and D1-D9. Fix in `b5d84cb`.
- **Re-run.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `9e31f63`: 1,024 total, 1,018 passed, 0 failed, 6 skipped; on `b5d84cb`: **1,025 total, 1,019
  passed, 0 failed, 6 skipped** (lint self-test 39/39, 339 files clean, the guards, H1). `bin/check-assets-incremental` ok. Image rebuilt at `b5d84cb`
  (label checked; `bin/clean-docker` run); `bin/pixel-parity --fast --seeds default --only 'auth/sign_in*,rooms/show/designers'`: **4/4 screenshots, aria
  and loose equal**; `bin/clean-parity` run. Disk 25-26 GB free throughout. No bench: 6.4 and this fix change only strings computed once at boot (the 6.3
  bench in `bench/results/quick-63` stands: labels 47eb5b8 / 853604f, probe and load recorded, no cell under 5x).
- **Checked and holding.** The masks (A15): over all 315 logical paths the asset mask matches no plain URL, and a Propshaft digest and an SDK fingerprint
  of each path mask equal. The table takes only fingerprinted, unselected, uncompressed descriptors under `assets/`; the startup validation and the
  failing host without `configureHost` are tested. T1-T6 match the Rails fixtures. **Published image probed** (`parity/bin/candidate up`, `default`): no
  `campfire.staticwebassets.runtime.json` in `/opt/campfire` (so no development fallback route), no dotfile in `wwwroot`; a fingerprinted URL gives
  `max-age=31536000, immutable`, ETag, `Vary: Accept-Encoding`, br 1,225 / gzip 1,439 / identity 4,113 bytes, 304 on `If-None-Match`, 206 on a sound's
  `Range`, 405 on POST, no `x-request-id`/`x-runtime`; `/assets/..%2Fcampfire.dll`, `%2e%2e`, `%5C` and raw-backslash forms, `/campfire.dll`, the endpoints
  manifest, `/wwwroot/...`, `/assets/.keep`, `/assets/.DS_Store` and `/..%2f..%2fetc/passwd` are all the app's 404. Until 6.6 the old middleware still
  answers `/404` (200), `/robots.txt` (`public, max-age=2592000`) and the vendored `/assets/lexxy.js.br`; `/assets/lexxy.js.gz` is the SDK's own
  precompressed route (Content-Encoding gzip), not the vendored file. No CSP is configured in the reference (`content_security_policy.rb` is all
  comments), so no nonce; no integrity in the import map (Rails' has none).
- **Fixed (`b5d84cb`).** (1) `Importmap.pins` sorted `pin_all_from` files by path component (the Rust port's `PathBuf` order); importmap-rails sorts
  `Dir[...]` as strings (byte order: `a-b.js` and `a.js` before `a/b.js`). Same output on today's `importmap.rb`; T6 now pins Ruby's order. Also `to:` without
  `under:` (Ruby's `compact`: no prefix) and a repeated `pin_all_from` of a directory keeping its place. (2) The preload `Link` header skipped a link that
  would not fit and counted the comma; actionview's `send_preload_links_header` breaks at the first such link and does not count the comma. Same 15 links
  today; T5 pins Rails' rule. (3) **Security:** the staging target filtered dotfiles only from the load path: a `.DS_Store` in `Assets/overrides` was
  staged and would be served at `/assets/.DS_Store` (shown by a planted file on `9e31f63`'s target). Dotfiles and dot-directories are now left out of the
  overrides and `reference/public` too (planted `.DS_Store` and `.hidden/x.js`: nothing staged). (4) `.dockerignore` keeps the locally staged `wwwroot`
  out of the image's context. (5) New App test: encoded dots and backslashes, the build output and the web root by name are not served.
- **KLOC.** 6.4's entry under-counted: `bin/kloc` at `9e31f63` is **30,278 by upstream's rules, 30,450 corrected** (+120 over 6.3, not +37; `bin/kloc`
  reads `git ls-files`, so an uncommitted `Importmap.fs` is not counted). At `b5d84cb`: **30,280 / 30,452**. The asset layer in the App is about 0.24 KLOC
  (`StaticAssets.fs` and `Importmap.fs`) against the design's 0.15, so the phase lands near 30.2 after 6.7's -429, not 29.9.
- **Differences recorded.** None new.
- **Notes for 6.5-6.7.** (a) 6.5 will not fit 90 minutes with its whole pixel list: commit the flip with `bin/verify` and the `interactions/**` slice
  first, then the remaining slices, `realtime/*` one at a time, and the bench as a second part. (b) Before the flip, check that nothing renders a page,
  a broadcast or a job before `StaticAssets.map` runs (`InvalidOperationException "static assets are not mapped"`): boot warm-ups and the error pages
  read the web root, not the table. (c) In 6.5's live-layer grep, count `-«digest»` in the head against Rails' and also count plain `/assets/` URLs in
  `.loose.norm.html` (must be 0 outside `url()`). (d) 6.6: plain `/assets/<logical>` URLs are already 200 since 6.3 (behind the old middleware); the
  README's Assets entry covers it (N3). Keep this checkpoint's probe list in `bin/asset-check` (encoded dots, `%5C`, dotfiles, the build output's
  files). (e) 6.7: KLOC lands near 30.2, not 29.9; record it, do not chase it in this phase.

### Phase 6, unit 6.5: the flip (2026-10-22)

- **Built.** Pages now read the asset table: `ViewContextBuilder.stylesheetTrusted`/`importmapTrusted` wrap `StaticAssets.stylesheetTags ()`/`importmapTags ()` in
  `Html.Trusted.ofAssetTags` (lazy, first use), `Page.renderDetachedAt` and `Web.Pages` take `AssetPath = StaticAssets.path`, the page `Link` header is
  `StaticAssets.preloadLinks ()`, `Presenter.fs` sound URL and image and `Controllers/Pwa.fs`'s manifest use `StaticAssets.path`. No page, presenter or
  controller opens `Campfire.Assets` any more (`Boot` error pages and `Avatars` still do, until 6.6; the old middleware still serves public files and old URLs).
  The compile order moved: `Importmap`, `Errors`, `Params`, `Formats`, `Flash`, `Respond`, `Edge` and `StaticAssets` now precede `AppState` and the presenters
  (the table must be visible to them; `StaticAssets` needs `Edge`, which needs `Respond`).
- **Check before the flip.** Nothing renders a page at boot: the tags are lazy, boot warm-ups render nothing, and jobs render only once enqueued. A job already
  queued in the database at restart could in principle render before `map` runs (the pipeline is built as the host starts); not seen, not mitigated.
- **Tests.** Two App tests changed for the URL form (J2's "flash is shown once" masks `/assets/flash.`, the Edge install test looks for `/assets/install-edge.`).
  New H7 (the manifest's five asset URLs are all fingerprinted and are the table's route for exactly Rails' five logical paths) and H8 (a `/play yeah` message
  renders `yeah.mp3` and `sounds/yeah.webp` at their fingerprinted routes, no plain URL). `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: 1,027 total, 1,021 passed, 0
  failed, 6 skipped (the environmental six); lint, Dapper guard, H1 green.
- **Harness fix (`e2ca957`).** First `interactions/**` run: 79/79 pixel and aria equal but 73 loose DOM diffs, each one Lexxy's `lexxy-link-url-<id>`.
  `parity/capture/determinism.js` seeds `Math.random` per call site and keys the site with asset digests stripped (`-<hex>.ext`); the SDK form `.<10>.ext` was
  not stripped, so the candidate's sequence was keyed `lexxy.p8cxenlyvx.js` against Rails' `lexxy.js`. It now strips both. Re-run: 79/79 equal in all three layers.
  (The three extra requests per page, `lexxy-content/-editor/-variables.css` fetched twice, are A12: the `@import` loads the plain endpoint.)
- **Pixel (`--fast`, image at fab4bcf, `bin/clean-parity` after).** `interactions/**` (default is the only seed with them): 79/79 screenshots, aria, loose equal.
  `rooms/show/**,auth/**,account/**,pwa/**,users/profile/**` on every seed: default 112/112, crowd 4/4, custom_styles 14/14, first_run 8/8, restricted 2/2, all
  three layers equal. `realtime/message_sent` and `realtime/boosted`, one at a time: 2/2 each, equal. Live-layer grep over the `interactions/**` desktop
  `.loose.norm.html`: `-«digest»` 2,925 in Rails and 2,925 in the candidate; plain `/assets/` URLs not masked: 0.
- **Bench** (`bench/quick --reps 3 --apps fsharp,dotnetway --routes room_show,post_message,static_css`, `FSHARP_IMAGE=campfire-dotnetway:p5ctl` as control,
  interleaved, detached and polled; `bench/results/quick-65`). Host probe ratio 1.03-1.07, load before runs 1.4-2.8 (`uptime.log`); 18 runs, all 2xx, 0 errors.
  Medians new / control (req/s): room_show c1 2,466 / 2,495, c16 7,338 / 7,384; post c1 1,104 / 1,107, c16 2,015 / 2,029 (new 1,950-2,032 vs control
  2,027-2,038); static_css c1 16,776 / 17,247, c16 94,999 / 101,258 (-6%, CPU 30.5 / 27.9 us per request; reported, A10, no gate). **vs Rails:** room page
  30.7x (c16), 23.0x (c1); post 6.10x (c16), 6.61x (c1); no five-workload cell under 5x. The run's own byte check "FAILs" room_show decoded bytes 377,518 against the
  control's 376,570 (+948, 0.25%): longer fingerprint URLs on the page, expected and not a regression; the bench exits 1 because of it, the reports are written.
- **KLOC** `bin/kloc` after commit: 30,273 by upstream's rules, 30,445 corrected (+121 over 30,152 at 6.4's fix: tests H7/H8 and the unit's changes; 6.7 still
  deletes `Campfire.Assets`).
- **Differences recorded.** None new (fingerprint URL form A5, plain `@import` fetches A12, A15 masks).

### Phase 6, unit 6.6: public files and the network check (2026-10-22)

- **Built.** The old `publicFiles` middleware and `Boot.staticResponse` are gone; `Web.Services.pipeline` calls `StaticAssets.map` where Rails' `ActionDispatch::Static`
  ran (it no longer takes a public-files delegate). `ErrorPages` (`Web/Edge.fs`) starts empty and is filled by the pipeline from `404/422/500/502.html` through
  `StaticAssets.webRootFile` once the host's web root is known (`Boot.errorPages ()` returns the empty instance; the tests' `routedPipeline` gets the same fill).
  `Avatars.assetFile` takes the `IWebHostEnvironment` and returns the staged file's `PhysicalPath` (`assets/default-bot-avatar.svg`; `Logos` reads
  `assets/logos/app-icon*.png` the same way); the temporary-directory extraction and its lock are deleted. `Boot` and `Avatars` no longer open `Campfire.Assets`
  (only the project reference remains, for 6.7). Nothing renders before `StaticAssets.map`: the error pages read the web root, not the table.
- **Tests.** AppTests' four `Assets.serve`/`staticResponse` tests (public files before routing, the byte-borrowing corpus, the timing probe) and ControllerTests'
  concurrent-first-write test are gone with the code they covered; `AuthenticationTests`' no-cookie-read test takes its asset path from `StaticAssets.path`.
  New H5 (the five public files carry `reference/public`'s bytes; `/nope`, `/404`, `/422`, `/500` and `/502` are the app's 404 with `404.html`'s bytes) and H6
  (a bot without an avatar gets the staged `default-bot-avatar.svg` as `image/svg+xml`; the stock logo is 200 `image/png`). `CAMPFIRE_REQUIRE_SEED=1 bin/verify`:
  1,025 total, 1,019 passed, 0 failed, 6 skipped (the environmental six); lint, Dapper guard, H1 green.
- **`bin/asset-check`** (bash and python3 inside colima, against `parity/bin/candidate up --seed default`; curl with Brotli decodes `br`): signs in through the form, reads
  the Designers room page, and checks the 144 `/assets/` URLs it carries (142 of text types): N1 (fingerprint form, 200, `max-age=31536000, immutable`, ETag, 304,
  `Vary: Accept-Encoding`, no `x-request-id`/`x-runtime`), N2 (`br`/`gzip`/identity content-encodings, the three decode to equal bytes), N3 (plain `composer.css` 200
  `no-cache`, `nope.css` 404, `/robots.txt` 200, a sound with `Range` 206), N4 (the `Link` header names 15 fingerprinted stylesheets), the five public files, the
  extensionless error paths as the app's 404, and the checkpoint's probe list (21 URLs: encoded dots, `%5C`, a raw backslash, dotfiles, `/campfire.dll`, the endpoint
  manifest, `/wwwroot/...`, `/.env`, `/.git/config`, `/assets/.manifest.json`), each a 404 or 400 that leaks nothing. **Image rebuilt** with
  `CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build` at `a0cb099` (label checked on `campfire-dotnetway:app` and `campfire-candidate-dotnetway`; only
  `bin/asset-check` and this entry were committed after it): **1,794 checks, 0 failed.** A planted `max-age=1` in the expected header failed 144 checks (one per URL), then
  was removed. (The script first `exec`'d python, so its cleanup trap never ran and a candidate container stayed up; fixed, `bin/clean-docker` ran after.)
  `/assets/lexxy.js.gz` answers 200 `Content-Encoding: gzip`: the SDK's own precompressed route of `lexxy.js`, as the README now says.
- **Pixel (`--fast --only 'errors/**,users/**,bot_api/**,pwa/**'`, every seed, `bin/clean-parity` after).** default 66/66 screenshots, aria and loose equal; custom_styles 2/2;
  crowd, first_run and restricted have no such states (0/0); PASSED in 226 s.
- **`bin/cookie-roundtrip` once:** 20/20 checks, both ways (Rails -> ours -> Rails).
- **README.** The "Known differences" **Assets** entry (class A: fingerprint form, `url()`/`@import` not rewritten, ETag and `Last-Modified`, precompressed text, `ImportMapDefinition`,
  15-link header; class B: cache headers, extensionless error paths, no `.manifest.json`/dotfiles/build output, multi-range, 405 on other methods, the vendored `lexxy.js.gz`/`.br`
  names, no `x-request-id` on assets), each with the test that pins it.
- **KLOC** `bin/kloc` after the commits: 30,231 by upstream's rules, 30,403 corrected (-42 against 6.5's 30,273 / 30,445: `Boot`'s static code, `Avatars`' extraction and four retired tests go, H5/H6 and the `ErrorPages` fill come in). 6.7 still deletes `Campfire.Assets` (-429).
- **Differences recorded.** The README Assets entry (design section 8). Beyond it, one that the design's list did not name: `/robots.txt` and the error pages are `no-cache` with an ETag
  (Rails: `public, max-age=2592000`), covered by the cache-header class B line.

#### Phase 6, unit 6.7 (Delete)

- **Built.** `src/Campfire.Assets/`, `src/Campfire.Assets.Build/` and `tests/Campfire.Assets.Tests/` are deleted (with their ignored `bin/`/`obj/`), their `CampfireFs.slnx` rows and the App's project
  reference; the Dockerfile's `Assets.Build` step and comment (the comment now names the staging target), `bin/publish-mounted`'s step and comment, `bin/kloc`'s exclusion, `bin/linux-tests`' comment,
  `Core/Html.fs`' comment, `OVERRIDES.md`' sentence, `parity/capture/overrides.ts`' dead fallback directory; AGENTS.md loses the two layout rows and gains the **Assets** working-rules bullet
  (staging target, `StaticAssets`, startup validation and "nothing before `map`", harness masks, `bin/asset-check`); the layout pointer in section 2's table says where the layer went.
- **Acceptance.** `git grep -n -E 'Campfire\.Assets|Assets\.Build|assets\.bin|EmbeddedData'` over `src tests bin parity bench/quick bench/run Dockerfile AGENTS.md README.md CampfireFs.slnx`: no hits
  (history lives in `plans/`). `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green at `184cae3`: lint (331 files) and self-test, Dapper guard, H1, 1,011 tests, 0 failed, 6 skipped. Image rebuilt with
  `CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build`; `bin/pixel-parity --fast --only 'rooms/show/designers'` PASSED (default equal in screenshots, aria and loose).
- **KLOC** (`bin/kloc` after the commit): 29,802 by upstream's rules, 29,974 corrected (6.6: 30,231 / 30,403; -429 for `Campfire.Assets`, the rest of the phase already counted). Design projected
  about 29.9 corrected with 0.15 in the App; the App's asset layer is nearer 0.24 and the total is 30.0 corrected. Recorded, not chased. Rust's bar is 31.0.
- **Differences recorded.** None new.
- **Phase gates run so far (image `campfire-dotnetway:app` at `184cae3`, label checked).**
  - **Every state `--fast`, every seed**, with 6.5 and 6.6's slices: this unit ran `messages/**,search/**,autocompletable/**,welcome/**,rooms/**` (default 148/148, crowd 8/8, custom_styles 6/6,
    first_run 0/0, restricted 2/2, screenshots, aria and loose all equal; 427 s on default) and the eleven other `realtime/**` states one at a time (each 2/2 equal). With 6.5 (`interactions/**`,
    `rooms/show`, `auth`, `account`, `pwa`, `users/profile`, `realtime/message_sent`, `realtime/boosted`) and 6.6 (`errors`, `users`, `bot_api`, `pwa`) every state has had a `--fast` run since the
    flip; `errors/users/bot_api` ran at `a0cb099`, before the deletion, which changes no served code (the App no longer referenced the deleted project from 6.6).
  - **`bin/asset-check`** against the rebuilt image: 1,794 checks, 0 failed. **`bin/cookie-roundtrip`:** 20/20.
  - **`bench/quick --reps 3 --apps fsharp,dotnetway --routes room_show,messages_page,sidebar,search,post_message,static_css`**, `FSHARP_IMAGE=campfire-dotnetway:p5ctl` as control, interleaved
    (`bench/results/quick-67`). Host probe ratio 1.05-1.07, load before runs 1.3-3.0; 36 runs, all 2xx, 0 errors. Medians new / control (req/s): room_show c1 2,403 / 2,377, c16 6,979 / 7,105;
    messages c1 2,213 / 2,201, c16 6,925 / 7,153; sidebar c1 2,338 / 2,312, c16 7,476 / 7,437; search c1 2,026 / 2,051, c16 6,687 / 6,855; post c1 1,067 / 1,077, c16 1,927 / 1,964;
    static_css c1 16,616 / 17,192, c16 98,434 / 99,303 (reported, A10). No cell beyond the control's range except room_show c16 (-1.8%), messages c16 (-3.2%) and search c16 (-2.4%), with CPU per
    request +4 to +5% at c16 (longer fingerprint URLs and the endpoint table; the same 0.25% byte growth as 6.5). **vs Rails** (stored medians): room page 29.2x (c16), 22.4x (c1); messages 18.2x,
    10.2x; sidebar 12.5x, 8.2x; search 16.4x, 10.5x; post 5.84x (c16), 6.39x (c1); no five-workload cell under 5x. Idle anon memory 45 MB against the control's 25 MB (the staged table and endpoint
    metadata; cold start equal at 516 vs 515 ms). The run's own byte check "FAILs" four routes by +0.2% (+480 to +948 bytes, the longer URLs), as in 6.5; the bench exits 1 for it, reports are written.
- **Still to run (phase close):** the lean matrix on `default`, then the lean pixel matrix on all seeds. The unit itself (6.7) is complete and green.

**Phase 6 close: lean matrix (6.7 follow-up).** Image `campfire-dotnetway:app` at `184cae3`. `bin/pixel-parity` with no `--fast`, default seed by directory slices (realtime one state at a time; typing as `realtime/typing/one` and `/many`), then the other four seeds in one run, `bin/clean-parity` after each. Screenshots, aria and loose DOM all equal everywhere: default 860/860 (auth 62, errors 20, pwa 52, welcome 10, search 30, account 38, users 72, messages 120, interactions 182, rooms 170, eleven realtime states 8 each, typing one and many 8 each; autocompletable, bot_api have no screenshots), crowd 24/24, custom_styles 32/32, first_run 16/16, restricted 8/8. Logs: `bench/results/lean-67/`. This matches the Phase 0 cell counts. Phase 6 gates complete.

### Phase 6: gate (2026-10-22)

Evidence: `bench/results/dotnetway-p6/` (slim: logs, `report.json` per pixel slice, bench JSON; 4 MB). Gate runner; no code change was needed. Image `campfire-dotnetway:app` built from `c29e14e` (label checked equal to HEAD); control built from `853604f` (Phase 5's closing commit, per the Phase 5 verification entry) in a scratch worktree with the submodule copied in, tag `campfire-dotnetway:p5gate`, label `853604f`, removed afterwards with its worktree.
- **`CAMPFIRE_REQUIRE_SEED=1 bin/verify`** on `c29e14e`: 1,011 total, 1,005 passed, 0 failed, 6 skipped (the environmental six); lint self-test (39 planted files) ok, lint 331 files clean; Dapper and EF guards, H1 and the Release warnings-as-errors build green. The first run (`verify-loaded-run.log`) overlapped the image build (host load 7-11) and failed one wall-clock test, `Html5Tests.L3 the caps` (160,000 ignored end tags 50.7 ms against its 50 ms limit); rerun on the quiet host (after the builds), green. Noted, not changed: that limit is load-sensitive.
- **Every state `--fast`, every seed** (24 slices: the twelve directories and the thirteen realtime states one at a time, typing as `/one` and `/many`): every slice PASSED, screenshots, aria and loose equal (`pixels/fast-*.log`).
- **Lean pixel gate** (same 24 slices, no `--fast`, every seed of each slice): **940 of 940 cells equal on screenshot, aria and loose DOM**; no cell NOT CAPTURED, no slice failed, so nothing to re-run.

| seed | screenshots | aria | loose DOM |
|---|---:|---:|---:|
| default | 860/860 | 860/860 | 860/860 |
| crowd | 24/24 | 24/24 | 24/24 |
| custom_styles | 32/32 | 32/32 | 32/32 |
| first_run | 16/16 | 16/16 | 16/16 |
| restricted | 8/8 | 8/8 | 8/8 |

- **`bin/asset-check`** (fingerprinted URLs, cache headers, precompressed variants): 1,794 checks, 0 failed. **`bin/cookie-roundtrip`** 20/20 both ways. **`bin/db-differential`** rc 0 (schemas agree, reference boots on the database we wrote, rollback ok).
- **Speed** (`bench/quick`, 5 workloads, c=1 and c=16, 3 reps, interleaved new/ctl/ctl/new/new/ctl, each rep in two parts; new = `c29e14e`, control = `853604f`). Host probe 1.039-1.076 before runs, windows 1.037-1.095 after (0 of 10 windows above 1.10 in every new rep), Mac load average 2.2-4.2 at rep boundaries; 30 runs per side, all 2xx, 0 connection errors, post rows equal to the 2xx answers.

| workload | c | new req/s | control req/s | new vs control | x Rails (new) | x Rails (control) | CPU us/req new / control |
|---|---:|---:|---:|---:|---:|---:|---|
| room page | 16 | 7,454 | 7,364 | +1.2% | 31.15 | 30.78 | 435 / 439 |
| room page | 1 | 2,496 | 2,513 | -0.7% | 23.31 | 23.47 | 494 / 497 |
| messages page | 16 | 7,398 | 7,292 | +1.5% | 19.44 | 19.16 | 451 / 455 |
| messages page | 1 | 2,344 | 2,315 | +1.3% | 10.76 | 10.63 | 522 / 530 |
| sidebar | 16 | 7,667 | 7,652 | +0.2% | 12.81 | 12.78 | 428 / 429 |
| sidebar | 1 | 2,474 | 2,468 | +0.2% | 8.63 | 8.61 | 499 / 502 |
| search | 16 | 7,004 | 7,049 | -0.6% | 17.17 | 17.28 | 480 / 480 |
| search | 1 | 2,228 | 2,201 | +1.2% | 11.56 | 11.42 | 543 / 550 |
| post a message | 16 | 1,997 | 2,012 | -0.7% | 6.05 | 6.10 | 1,497 / 1,491 |
| post a message | 1 | 1,106 | 1,108 | -0.2% | 6.63 | 6.64 | 1,645 / 1,664 |

  **No cell under the 5x floor** (thinnest: post c=16 6.05x, post c=1 6.63x, sidebar c=1 8.63x); no speed fix made. Cost of Phase 6 against Phase 5's close: within -0.7% to +1.5% on every cell, inside the control's own ranges; CPU per request equal (-1% to +0.4%). (6.7's run against the earlier `p5ctl` saw -1% to -3% at c=16 on three cells; this same-session control does not repeat it.) The bench exits 1 on four routes' decoded-bytes check (+0.2%, +434 to +948 bytes: the fingerprinted URLs are longer), as recorded in 6.5 and 6.7; the control passes it. The stored expected sizes are Phase 4's and would be refreshed at the next re-baseline.
- **KLOC:** `bin/kloc` 29,802 by upstream's rules, **29,974 corrected (30.0 KLOC)**; App 12,309, Views 5,219, Kit 2,842, Db 2,460, RichText 2,268. Phase 5 closed at 30.2, so the phase is -0.2 (design projected about 29.9). Rust's bar is 31.0.
- **Differences recorded:** none new. Open items unchanged from Phases 4 and 5 (Firefox empty-404 pair, WebKit WebP avatar flake, 16 KB Cable bound, the two test packages, the Phase 5 list).
- Disk: 19-30 GB free throughout; `bin/clean-docker` and `bin/clean-parity` run; the control image and worktree are removed.

### Phase 6: security review of the assets (2026-10-22)

Opus reviewer, on `a0b19f2`. Findings and method in `plans/design-assets.md` section 11 ("Security review"); fix in `26c4ea1`.
- **Read:** 17 files whole (`StaticAssets.fs`, `Importmap.fs`, the staging target, the pipeline, `Edge.fs`, the avatar, logo and PWA controllers, the presenters'
  asset call sites, `Pages.fs`' `Link` header, `Front.fs`' host creation, `Respond.sendFile`/`Native.fs`, `AssetsTests.fs`, `bin/asset-check`,
  `bin/check-assets-incremental`, the Dockerfile, `revendor`); inspected the endpoint and runtime manifests, the staged web root and every staged SVG/HTML.
- **F1 (Low, fixed):** the staging target's `Copy` followed symbolic links, so a link among the inputs (file or directory) staged and served a file of the build
  host (shown with two plants on `a0b19f2`). The target now fails the build on any link under the overrides, the `LOAD_PATH` roots and `reference/public`;
  `bin/check-assets-incremental` plants both kinds and failed before the fix. None is tracked today (parity with Propshaft; defence in depth).
- **F2 (info, holding):** no traversal or disclosure: exact manifest routes, files opened by the descriptor's path, dotfiles not staged, the build output's
  fallback route neutralised; six more probes (double encoding, overlong UTF-8, `%00`, `.%2e`, `//..//`, `..;`) in the App test and `bin/asset-check`. A
  trailing slash or another letter case reaches the same public file (recorded). **F3 (guard):** a test fails on script, handlers, `javascript:` or embeds in any
  served SVG/HTML source (none today; a planted `onload` SVG fails it). **F4-F7 (info):** cache headers (public content only, no `Set-Cookie` possible ahead of the
  session), content types (all explicit, equal to Rails'), no `nosniff` on static or `send_file` responses (Rails parity: `Static` and `ActiveStorage::Streaming`),
  import map escaping, no CSP or SRI in the reference, `UseStaticWebAssets` inert in a publish. **F8:** `.dockerignore`'s stale comment.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `26c4ea1`: 1,012 total, 1,006 passed, 0 failed, 6 skipped (the environmental six; +1, F3); lint self-test
  39/39, 331 files clean; Dapper and EF guards, H1. `bin/check-assets-incremental` ok. Image rebuilt at `26c4ea1` (label checked): `bin/asset-check` 1,806 checks,
  0 failed; `bin/pixel-parity --fast --seeds default --only 'rooms/show/designers'` 2/2 equal (screenshots, aria, loose). `bin/clean-parity` and `bin/clean-docker`
  run; disk 26-30 GB free. No bench: nothing on a request path changed (the check runs at build time).
- **KLOC:** `bin/kloc` 29,802 by upstream's rules, 29,974 corrected (30.0), unchanged (MSBuild and tests are not counted).
- **Differences recorded:** none new in the README. `nosniff` absence is Rails parity; a trailing slash or another letter case on an asset route answering the same
  public file is recorded in design-assets.md section 11 only (beside the README's class B line that plain `/assets/` URLs are served).

### Phase 6: independent verification (2026-10-22/26) - PHASE 6 DONE; Phase 7 GO

Opus verifier, adversarial review of `e58de72..d1f9ecf` (design, 6.1-6.7, the checkpoint, the gate, the security review) against `plans/design-assets.md` and
D1-D9, cheap checks re-run. A first verifier session was cut off by a usage limit after its runs; its evidence in `bench/results/dotnetway-p6-verify/` was checked
and kept, and this session finished the rest. Fix in `309ab42` (README, one test, the design note; no app change). **Phase 6 closes at `309ab42`** (this entry and
the evidence are the commit after it).
- **Deleted, checked:** `src/Campfire.Assets/`, `src/Campfire.Assets.Build/`, `tests/Campfire.Assets.Tests/` are gone; `git grep` for `Campfire.Assets`, `Assets.Build`,
  `assets.bin`, `EmbeddedData`, `staticResponse`, `publicFiles`, `assetDirectory`, `assetLock` over `src tests bin parity bench/quick bench/run Dockerfile AGENTS.md
  README.md CampfireFs.slnx .dockerignore` finds nothing. (`StylesheetTags` remains as the Views layout's `Html.Trusted` field, filled from `StaticAssets`, as designed.)
  No package added (Falco 5.2.0, Falco.Markup 1.4.0, AngleSharp 1.8.4 unchanged).
- **Pixel evidence, checked:** the gate's `report.json`s hold **940 distinct lean cells** (default 860, crowd 24, custom_styles 32, first_run 16, restricted 8) and
  401 distinct `--fast` cells, every one equal on screenshots, aria and loose DOM; no cell missing. **Re-run on the closing image** (`campfire-dotnetway:app` and the
  candidate labelled `26c4ea1`; `src` is unchanged since, `d1f9ecf` and `309ab42` touch tests and docs only): `--fast` every seed `interactions/**` (default 79/79),
  `pwa/**,errors/**,auth/**` (default 62/62, custom_styles 2/2, first_run 8/8), `realtime/message_sent` 2/2 and `realtime/attachment_sent` 2/2 one at a time; and
  **lean** (three browsers, both themes, breakpoints) `rooms/show/designers,auth/sign_in*` on default **28/28**. All PASSED; `bin/clean-parity` after each.
- **Ungated layers, looked at.** The network layer differs on 926 of 938 cells shared with Phase 5's gate (Phase 5: 910 already unequal, at smaller counts:
  welcome/no_rooms 63/63 then, 283/265 now). Diffed one cell by request (`network/*.diff`): 129 asset responses differ only in `Cache-Control`, `ETag`,
  `Accept-Ranges` and (one PNG) `Content-Encoding`; three extra requests are the plain `lexxy-*.css` URLs of A12; the page and avatar rows are earlier phases'.
  No asset request is missing or fails. The README's class A line now names `Accept-Ranges` and the uncompressed `campfire-icon.png`.
- **Phase gates re-run.** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on `d1f9ecf` (first session): 1,012 total, 1,006 passed, 0 failed, 6 skipped; on `309ab42`'s tree:
  **1,013 total, 1,007 passed, 0 failed, 6 skipped** (the environmental six; +1 is H9), 0 warnings, lint self-test 39/39 and 331 files clean, Dapper and EF guards,
  H1. `bin/check-assets-incremental` ok (an added override staged and in the manifest, gone when deleted; a planted file link and directory link pointing outside
  the repo fail the build). `bin/asset-check` on the image: **1,806 checks, 0 failed**.
- **Planted failures (6.4's and the checkpoint's, not re-run before):** a wrong pin with the stylesheets reversed fails T3, T4 and T5; a dropped `pin_all_from`
  fails T4; the marker middleware's filter removed (the build output's `{**path:file}` fallback left in) fails 104 App tests (sign-in, Cable 404, 405s). Each
  restored; `git status` clean after (`sabotage-*.txt`).
- **Security, re-checked.** Read `StaticAssets.fs`, the staging target and the link check: only manifest routes are served, the file comes from the descriptor, the
  link check covers every root (a root that is itself a link is reported too). Probed the image and the reference side by side: a trailing slash
  (`/robots.txt/`, `/assets/composer.<fp>.css/`) answers the file in **both** (parity: the security review called it a difference; corrected in design-assets.md
  F2); **letter case** (`/ROBOTS.TXT`, `/ASSETS/COMPOSER.<FP>.CSS`, `/Assets/Composer.css`) answers the file in ours and 404 in Rails: a class B difference the
  README lacked. **Fixed (`309ab42`):** README Assets class B line and App test H9 (case variants and trailing slash answer 200 from the asset endpoint).
- **Speed.** The gate's table stands (`c29e14e` against a same-session `853604f` control; host probe 1.037-1.095, load 2.2-4.2; 30 runs a side, all 2xx): **no cell
  under 5x**, thinnest post c=16 **6.05x**, post c=1 6.63x, sidebar c=1 8.63x; Phase 6 costs -0.7% to +1.5% per cell, CPU per request equal. Since then only a
  build-time check and tests changed, so no re-bench. The bench's decoded-bytes "FAIL" is exactly the fingerprint length: the candidate's sidebar is 30,144 bytes
  with 217 `/assets/` URLs, search 137,229 with 298, so +434 and +596 bytes are 2 per URL (`asset-url-count.txt`). `static_css` (A10, reported): 16,616 / 98,434
  req/s at c=1 / c=16 (6.7) against Rails' stored 15,167 / 98,776: level with Rails at c=16; Phase 7's front-cache design owns it.
- **KLOC.** `bin/kloc` **29,802 by upstream's rules, 29,974 corrected (30.0)**, unchanged by the fix. Phase 5 closed at 30.2, so the phase is -0.2 against the
  design's -0.3 (29.9, range 29.85-29.95): 0.02 over the range's top, the App's asset layer being about 0.24 KLOC against 0.15. Under Rust's 31.0.
- **Differences recorded:** README Assets gains the letter-case line (class B, H9) and the `Accept-Ranges`/uncompressed-image detail (class A).
- **Open, none blocking:** the bench's stored byte sizes (refresh at Phase 7's re-baseline); `Html5Tests` L3's 50 ms limit is load-sensitive (failed once under
  load 7-11 in the gate); front cache for `/assets` (Phase 7); carried from Phases 4-5: Firefox empty-404 pair, WebKit WebP avatar flake, the 16 KB Cable bound,
  the two test packages (Phase 8), Q1 and F6 of Phase 5.
- **Phase 7 GO.** Conditions: keep the startup validation and "nothing renders before `StaticAssets.map`"; keep `StaticAssets.map`'s marker middleware and
  `appEndpointsOnly` in place whenever the pipeline order changes (the planted removal broke 104 tests: watch it if Phase 7 reworks the front server or routing);
  keep the staging target's dotfile and link filters; an OutputCache or front-cache policy for `/assets` must keep `max-age=31536000, immutable` only on
  fingerprinted routes and must not cache plain `/assets/` or error-page responses as immutable (`bin/asset-check` N1/N3 after any change); keep H1, the markup lint
  and the Dapper guard green; refresh the bench's expected byte sizes when Phase 7 re-baselines.

### Phase 7: design (2026-10-26)

Opus designer. `plans/design-front.md` written (decisions F1-F16, spikes S1-S7, units 7.1-7.6 with the checkpoint after 7.3). No code change; `bin/kloc`
29,802 / **29,974 corrected (30.0)** at `8df0b2b`, `Campfire.Kit` 2,842. Spikes in a scratch project under the session scratchpad and the toolchain image in
colima, all removed after (the scratch project, its containers and the Pebble image); a stray `bench/front/fsharp/obj` from a build check was removed.
- **Packages:** Certes 4.1.0 (2026-09-27, MIT, active) with BouncyCastle.Cryptography 2.7.0, no advisory or deprecation on NuGet; .NET 11 RC2 ships
  `ZstandardCompressionProvider`. `bench/front/fsharp` does not build today (it calls the kit `Ctx`/`act` API deleted in 2.13): 7.6 rewrites it.
- **Certes against Pebble** (HTTP-01, Pebble `sha256:ddf23064...`): account 115 ms, order ready after 2 polls, issued and cached in 383 ms; a second run reused
  the account and read the SEC1 key autocert writes; Pebble's leaves now live 6 days (hence renewal at `min(30 days, lifetime / 3)`).
- **TLS on Linux:** `ServerCertificateSelector` sends the leaf alone (with or without `ServerCertificateChain`): `openssl` verify 20, `curl` 60; the TLS handshake
  callback with an `SslStreamCertificateContext` sends the chain (verify 0, HTTP/2 ok). So F3 uses the callback.
- **OutputCache:** a response-driven policy cannot honour `max-age` (expiry and buffering are fixed at the first body write, before `ServeResponseAsync`; a 1 s
  entry lived past 4 s) and default locking doubled the latency of 16 concurrent GETs of one private URL (413 against 207 ms); path-scoped builder policies with
  `Expire`, outside compression with `SetVaryByHeader("Accept-Encoding")`, reproduce Thruster's cache (encodings per client correct, hits uncompressed).
- **Cost on Linux** (15 us endpoint, CPU per request): HTTP logging to the console logger +3.5..+5.9 us at c=16 and +14..+38 us at c=1; path-scoped OutputCache
  on other paths +0.1..+0.8 us. Projected on Phase 6's thinnest cells: post c=16 about 6.0x, post c=1 6.5-6.6x, sidebar c=1 8.0-8.4x; nothing near 5x.
- **Operator step recorded** (design section 8): Let's Encrypt staging once on a real domain, then delete `storage/thruster/` before production.
- **Questions for Chris:** none. Projection: 30.0 -> about 27.6 KLOC.

### Phase 7, unit 7.1: request log and compression (2026-10-26)

Sonnet builder, commits `eba17a2` (code, tests, README) and `c61a029` (asset-check, bench sizes); plus this entry.
- **Built:** `Web/HttpLog.fs` (F8: `UseHttpLogging`, `CombineLogs`, Method/Path/Query/Protocol/Status/Duration, an `IHttpLoggingInterceptor` adding
  `remote_addr` (IPv4-mapped written as IPv4, after the forwarded headers: the interceptor runs on the response) and `user_agent` (control characters
  become a space, so a client cannot forge a line)); `EdgeConfig.LogRequests` (production default false; `Boot.bootEdge` takes it, `Boot.serve` passes
  `LOG_REQUESTS`; `bootWithClock`, so every test host, leaves it off); `Services.add` registers it, `Services.pipeline` puts `UseHttpLogging` first;
  `Compression.configure` adds `ZstandardCompressionProvider` after Gzip; `createLoggerFactory`'s `thruster` filter is `Microsoft.AspNetCore.HttpLogging`
  at Information. `Boot.serve` hands the old front `LogRequests = false` and `GzipCompressionEnabled = false` and no line sink. Deleted:
  `Front/RequestLog.fs` (`RequestLogCapture` moved into `Handler.fs`, the Kit keeps its `ILogger` request line until 7.6; `FrontServices`, `serveWithLines`
  (now `serveHosted`) and the test harness lose the `lines` parameter), `RequestLogTests.fs`, the "line sink" Kit test, `CollectingStream`, `bench/log-micro`.
  `bench/lib/logcount` counts `HttpLoggingMiddleware` lines. README Front-server class B entry (log format, zstd, no jitter, `GZIP_COMPRESSION_*` unread).
- **Tests** (`tests/Campfire.App.Tests/Web/HttpLogTests.fs`, 7): LG1 one entry per request with method, path, query, protocol, status and duration; LG2
  `remote_addr` and `user_agent`, a trusted proxy's `X-Forwarded-For` (an IPv4-mapped one written as IPv4, an IPv6 one as is); LG3 a planted `Cookie`,
  `Authorization` and `Referer` are absent, a tab in the user agent is a space; LG4 the real `Services.pipeline` logs with `LogRequests` and not without;
  CZ1 zstd decompresses (`ZstandardStream`) to the identity body; CZ2 `gzip, deflate, br, zstd` gets `br`, `gzip` gets gzip, none and `identity` get none; CZ3
  a 100-byte response and an image are not compressed and carry no `Vary`. All green first run.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **1,015 total, 1,009 passed, 0 failed, 6 skipped** (the environmental six; 1,013 before), Release, warnings as errors,
  lint, Dapper and EF guards, H1. `bin/kloc`: **29,587 by upstream's rules, 29,759 corrected (29.8)** (29,974 at the design; Kit 2,842 -> 2,585, App +42 lines for `HttpLog.fs` and the edge field). Image rebuilt (`campfire-dotnetway:app`, label `eba17a2`, equal to HEAD's `src`); `bench/lib/logcount` on it: **20,000 requests, 20,001 lines** (the
  one answered readiness probe), SIGTERM, every line counted. `bin/asset-check`: first run **2 failed** (N1's `Vary: Accept-Encoding` on `56k.webp` and
  `campfire-icon.png`: the front server added `Vary` to every response, the framework's endpoints send it on the precompressed text types only: class A, as the
  design said); N1 now requires it on text types and its absence elsewhere; rerun **1,806 checks, 0 failed**. `bin/pixel-parity --fast --seeds default --only
  'auth/sign_in*,rooms/show/designers,errors/**'`: **14/14 screenshots, aria and loose DOM equal**. `bin/clean-docker` and `bin/clean-parity` run; 33 GB free.
- **Bench** (`bench/quick`, five workloads, c=1 and 16, 3 reps, new `eba17a2` against control `campfire-dotnetway:p6ctl` (`309ab42`, built from a scratch worktree
  with the submodule copied, label checked, worktree removed; the image stays for 7.2-7.4), interleaved new/ctl/ctl/new/new/ctl, host probe ratio 1.066-1.085 on every
  window of both sides, Mac load 2.4-4.1 (6.4 at one start); 30 runs a side, all 2xx, 0 errors; `bench/results/dotnetway-7-1/quick/`). **vs Rails** (stored
  medians), new / control: room page 30.4x / 30.7x (c16), 20.8x / 23.2x (c1); messages 18.8x / 19.5x, 9.55x / 10.7x; sidebar 12.3x / 13.0x, **7.57x** / 8.52x; search
  17.0x / 17.1x, 10.2x / 11.2x; post **5.97x** / 6.00x (c16), **6.20x** / 6.57x (c1). **No cell under 5x**; the thinnest is post c=16 at 5.97x (control 6.00x). req/s
  against the control: c16 -0.9% room, -3.2% messages, -5.3% sidebar, -0.4% search, -0.6% post; c1 -10.4% room, -10.5% messages, -11.2% sidebar, -9.2% search, -5.7%
  post. **CPU per request beside S5's projection** (S5: +3.5 to +5.9 us at c16, +14 to +38 us at c1): c16 +14.8 / +20.8 / +29.2 / +13.8 / +40.5 us (room, messages,
  sidebar, search, post), c1 +61.4 / +67.8 / +70.5 / +67.3 / +61.5 us. So the real app pays **about 2-3x S5's c16 cost and about 2x its c1 cost** (the control's own
  batched log was +1.2 us); the c1 loss is the console logger's per-line wake-up plus the interceptor, and it is 5.7-11.2% where the design projected 2.3-7%. Risk 1's
  ladder (`ConsoleLoggerOptions` queue settings, then the interceptor dropped, before anything hand-written) is not triggered, since no cell is near 5x, but the c=1 cells
  are the ones to watch at the 7.3 checkpoint; 7.2's OutputCache and 7.4's host swap (which removes the old front's per-request work) come after.
- **Bench sizes:** `bench/lib/expected-sizes.json` refreshed to room 377,518, messages 346,364, sidebar 30,144, search 137,229 (Phase 6's fingerprinted URLs, 2 bytes
  each; the decoded-bytes check passes on both images now).
- **Differences recorded:** README class B (the HTTP logging line replaces Thruster's `Request path=...` line, no port in `remote_addr`; zstd offered after `br` and `gzip`;
  `GZIP_COMPRESSION_*` unread, no jitter); class A (asset `Vary` on text types only, in the Phase 6 entry's list now by `bin/asset-check`).
- **Open for 7.2:** `Boot.serve` still starts the old front; `bench/front` still does not build (7.6). Evidence: `bench/results/dotnetway-7-1/`.

### Phase 7, unit 7.2: OutputCache (2026-10-26)

Sonnet builder, commit `f050455` (code, tests, README); plus this entry.
- **Built:** `Web/OutputCaching.fs` (F9): five families (avatar, account logo, QR code, Active Storage proxy, disk show), each a path-shape regex
  (case-sensitive, `NonBacktracking`, a prefix guard first) and the controller's lifetime; `configure cacheSize maxItem` sets `UseCaseSensitivePaths`,
  `SizeLimit`, `MaximumBodySize` and one `AddBasePolicy` per family (`With(shape && no Range).Expire(maxAge).SetVaryByHeader("Accept-Encoding")` on the
  builder's default policy). The lifetimes are literals in `OutputCaching`, which `Avatars`, `Logos`, `QrCode` and `ActiveStorageControllers` now read (they are
  compiled after `Services.fs`, so the constants live in the earlier file). `EdgeConfig` gains `CacheSize` and `MaxCacheItemSize` (0 = no cache, the test hosts);
  `Services.add` calls `AddOutputCache` and `Services.pipeline` `UseOutputCache` right before `UseResponseCompression`, both only with a size; `Boot.bootEdge`
  takes the sizes, `Boot.serve` passes `CACHE_SIZE`/`MAX_CACHE_ITEM_SIZE` to the app and `CacheSize = 0` to the old front. README class B entry (no `X-Cache`,
  `Age` on hits, cookies never stored, `Authorization` and `Range` bypass).
- **Tests** (`OutputCachingTests.fs`, 9, over a recording store and a stub app behind the real `Compression.configure`): OC1-OC9 as the design; all green after one
  fix to the test store (it ignored `validFor`, so OC2's expiry failed until the store honoured it).
- **Planted failure:** the avatar predicate widened to `StartsWithSegments "/users"`: **OC3 and OC9 fail** (a `/USERS/...` and `/users/1/profile` match); reverted.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **1,024 total, 1,018 passed, 0 failed, 6 skipped** on the second run. The first run had **1 failure in
  `Campfire.Kit.Tests`** that did not repeat (three isolated reruns of the project and a second full verify were green); the test name was not captured, so it is
  recorded as an unidentified Kit flake under full-suite load. `bin/kloc`: **29,645 by upstream's rules, 29,817 corrected (29.8)** (29,587 / 29,759 after 7.1).
  Image rebuilt (label `f050455`). `bin/asset-check`: **1,806 checks, 0 failed** (no asset route is cached). `bin/pixel-parity --fast --only
  'users/**,account/**,rooms/show/designers'` on all five seeds: **PASSED** (default, crowd, custom_styles 8/8, first_run 0/0, restricted 2/2 equal on pixels, aria and
  loose DOM). `bin/clean-docker` and `bin/clean-parity` run; 32 GB free.
- **Bench** (`bench/quick --routes avatar,room_show,post_message`, 3 reps, new `f050455` against `campfire-dotnetway:p6ctl`, interleaved new/ctl/ctl/new/new/ctl, host probe
  1.05-1.09, Mac load 3.9-5.5; 18 runs a side, all 2xx, 0 errors; `bench/results/dotnetway-7-2/quick/`). **vs Rails:** room page **30.96x** (c16) / 20.45x (c1)
  (control 32.46x / 23.71x), post **6.44x** / 6.18x (control 6.17x / 6.76x): **no cell under 5x**. **Honest reading of "not below the control's range":** room c16
  7,409 [7,292-7,501] against 7,768 [7,753-7,783], room c1 2,190 [2,056-2,211] against 2,539, post c1 1,032 [1,002-1,043] against 1,128 [1,127-1,136] are below the
  p6ctl range; post c16 2,127 [2,107-2,167] against 2,036 is above. That gap is 7.1's request log (p6ctl has the batched log, 7.1's entry measured -9% to -11% at c=1),
  not the cache: against 7.1's own numbers (room 7,281 / 2,224, post 1,971 / 1,034, `dotnetway-7-1/quick`, a different session) 7.2 is level (+1.8%, -1.5%, +7.9%,
  -0.2%), and CPU per request 456.7 / 570.4 / 1,529.6 / 1,753.3 us against 452.9 / 561.0 / 1,545.1 / 1,732.4 us. No same-session control of `eba17a2` was built, so this is a
  cross-session comparison. **Avatar row** (a hit; reported, no stored Rails median): new **12,202 req/s [11,529-12,629] at c=1 (CPU 168 us), 96,936 [93,868-97,238] at
  c=16 (27.4 us)**; control 24,930 [24,650-24,940] (90.5 us) and 127,487 [124,184-129,268] (19.4 us). A hit is about 2x the CPU at c=1 and 1.4x at c=16 until 7.4: the old front still
  sees every request (its cache is off, but its per-request work and the extra hop stay) and the app's hit now crosses `UseHttpLogging` and OutputCache as well as the front.
  The c=16 avatar warm-up reached its maximum in two reps (flat within 1%).
- **Differences recorded:** README class B (OutputCache families by exact path shape, `Age` on hits and no `X-Cache`, cookie responses never stored, `Authorization` and `Range`
  bypass); class A: none new.
- **Open for 7.3:** `Boot.serve` still starts the old front (its cache off); the avatar hit cost is 7.4's to remove; recheck avatar and the c=1 cells against `p6ctl` after the host swap.
  Evidence: `bench/results/dotnetway-7-2/`.

### Phase 7, unit 7.3: ACME on Certes (2026-10-26)

Sonnet builder, commits `c19d90b` (code, tests, fixtures) and `2936a0c` (AGENTS.md recipe, `bin/linux-tests`); plus this entry.
- **Built:** `Certes` 4.1.0 (`Directory.Packages.props`, the App; its one dependency is `BouncyCastle.Cryptography` 2.7.0). `Web/FrontSettings.fs` is the record only (F14; 7.4 adds
  `fromLookup`/`fromEnv`). `Web/Acme.fs` (385 lines with comments): `DirCache` (`read` = `X509Certificate2.CreateFromPem` + `MatchesHostname(domain, true, false)` + the
  currency check; `write` = SEC1 key then the chain's certificates in order, directory 0700, file 0600, temp file then rename; `accountKeyPem`), `Renewal.due`/`retryAfterFailure`,
  `ServerNames.normalize`, `CertificateStore` (per-domain `SslServerAuthenticationOptions` with the chain context, a first-certificate `TaskCompletionSource` per `TLS_DOMAIN` name,
  challenge tokens, `HandshakeOptions`), `AcmeService` (`LoadCached`, `RenewDueAsync(now, ct)` one pass, `ExecuteAsync`: load, wait for `ApplicationStarted`, loop in at most 7-day
  steps; the account is registered lazily once, EAB when both variables are set, the HTTP client is the factory's `"acme"`), `AcmeEndpoints.challenge`. Not wired into `Boot.serve`.
- **Found and fixed in AC11:** the issued file was first read back with the pass's start time; Pebble does not back-date `NotBefore`, so 2 of 6 Mac runs failed with "certificate is
  not yet valid". The service now reads at issue time (`time.GetUtcNow()`), 8/8 Mac runs and 3/3 Linux runs after. Let's Encrypt back-dates by an hour, so production would not
  have seen it.
- **Tests** (`AcmeTests.fs`, 11; fixture `testdata/acme/`: a cache file written the way the deleted port wrote it, a 100-year certificate for `chat.example.com` over the SEC1 key
  of the old `FrontAcmeTests` fixture): AC1-AC10 as the design, over a fixed clock value, with no timer, no wall-clock bound and no counter another thread changes (the only flags are
  read after the awaited call returns). AC11 (Pebble) is skipped without `PEBBLE_DIRECTORY`/`PEBBLE_ROOT`. All green.
- **Planted failure:** `Renewal.due` back to a fixed 30 days: **AC4 and AC9 fail** (the 6-day certificate is due before it existed); reverted.
- **AC11, Pebble `ghcr.io/letsencrypt/pebble@sha256:ddf230642b1a584f519f32e347de1b05a6e4c1f6c35c1863b33effeab5f78199` in colima, host network, three runs in a row
  (`bin/linux-tests App --filter-method "*AC11*"`, Linux, HTTP-01 on 5002):** order time (from `StartAsync` to the certificate in the store) **449 ms, 437 ms, 432 ms**. Each run: the
  certificate issued and cached (`campfire.test`, 0600) with `acme_account+key`; an `SslStream` client with `CustomRootTrust` on the issuing root fetched from Pebble's management
  API, `DisableCertificateDownloads`, no revocation check, got `SslPolicyErrors.None` with a 3-element chain (leaf, Pebble's intermediate sent by the server, root); a second
  host on the same storage loaded the file at start, `RenewDueAsync` made no request (its CA client would have failed the test), and the handshake served the same thumbprint.
  Mac, Pebble with `--add-host campfire.test:192.168.5.2`: 8 of 8 after the fix (about 720 ms for the first order).
- **Toolchain fix:** `bin/linux-tests` hung after the tests finished (the test run completed, but `dotnet test` that builds under the container's PID 1 never returned; idle MSBuild
  nodes outlive it). It now runs `dotnet build ... --disable-build-servers` then `dotnet test --no-build`, passes `LINUX_TESTS_DOCKER_ARGS` and `PEBBLE_*`. Cost me about 25 minutes
  of 20-minute timeouts before I read the process table; the AGENTS.md recipe carries the working command.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **1,035 total, 1,028 passed, 0 failed, 7 skipped** (the environmental six plus AC11) on the second run. The first run had **1 failure,
  `Campfire.Kit.Tests.FrontAcmeTests."cached certificates load and are forgotten by obtaining"`** (`ObtainingCount` expected 0, was 1: the flake this phase deletes in 7.6; it did not repeat). That
  is the 7.2 entry's unidentified Kit flake, now named. `dotnet list package --vulnerable --include-transitive` on the App: **no vulnerable packages**. `bin/kloc`: **29,817
  corrected (29.8 KLOC)**, 29,645 by upstream's rules (the App 12,409 corrected; 29,759 after 7.2). No pixel run (no response changes), no bench (nothing wired), no image rebuilt, so no
  `bin/clean-docker`; 26 GB free after.
- **Differences recorded:** none (nothing is wired). 7.4 records F5's dropped behaviour (cached names that left `TLS_DOMAIN` are no longer served).
- **For the checkpoint:** AC1-AC10 contain no timers or shared counters (`Status.Asked` is a plain flag read after `await`, the clock is the `now` constant); the account on an existing
  `acme_account+key` is AC7 (Certes `KeyFactory.FromPem` reads the SEC1 text) and the second host of AC11 (the same `DirCache` file, no new order). Pebble's default profile issued a
  90-day leaf this run (7,776,000 s), not S2's six days; `lifetime / 3` is covered by AC4 and AC9 alone, not by Pebble. `SslStreamCertificateContext.Create(..., offline: false)` is
  used as the old port did (online OCSP staple fetch on Linux); Pebble has no responder and nothing slowed. 7.4 needs: `FrontSettings.fromLookup`, the listener's
  `TlsHandshakeCallbackOptions(store.HandshakeOptions)`, `MapWhen` for `AcmeEndpoints.challenge`, `AddHttpClient "acme"`; `Acme.fs`'s `LoadedCertificate` shares a name with
  `Campfire.Kit.LoadedCertificate` until 7.6 (qualify if both namespaces are opened).

### Phase 7, checkpoint after 7.3: Opus review (2026-10-26) - GO for 7.4, with notes

Opus checkpoint, adversarial review of `3696f18..d2d01d5` (7.1-7.3) against `plans/design-front.md` and D1-D9. Fixes in `b89974c` and the commit after it (with this entry).
- **Read, against the note.** HTTP logging (`HttpLog.fs`): the six fields, `CombineLogs`, no header or body field; the interceptor adds only `remote_addr` and `user_agent`
  (control characters to spaces). Path and query are logged as Thruster logged them, so a bot key or transfer id in a path reaches the log, as it did (class B, already
  recorded). OutputCache (`OutputCaching.fs`): the five families are exactly Rails' `expires_in ... public` routes (`Users::Avatars`, `Accounts::Logos`, `QrCode`) plus Active
  Storage's `http_cache_forever` proxies and the disk `show`; the shapes are anchored, case-sensitive and disjoint, no page path matches (OC9, the planted widening);
  every policy keeps the builder's default (GET/HEAD, no `Authorization`, 200 only, no `Set-Cookie`), and the session, the cookie jar and the actions are all inside
  `UseOutputCache`, so a hit carries no cookie and a signed-in page is never looked up. None of the cached responses depends on the viewer (the avatar is keyed by its
  signed token); Rails requires a session for the avatar and Thruster served cached ones to anyone with the URL, which the README records. OC6's 2 s bound is loose
  (locking costs about 2x, not 16x), but its real check is the empty `Gets`/`Sets`. ACME (`Acme.fs`): the challenge endpoint answers only a token present in the store
  during an order (a dictionary lookup, no file access, no `/` in the token); keys and certificates go 0700/0600 through a temporary file; names are normalized before
  any lookup or path; TLS options leave protocols and cipher suites at the platform's defaults. AC1-AC10 have no timer and no cross-thread counter (AC6 awaits after `Set`).
- **Fixed:** AC7 now pins an existing `acme_account+key` written as Go and openssl write it (returned unchanged, read by Certes as the same key: Thruster's account is
  reused); `DirCache.put` deletes its temporary file if the write or rename fails (no stray private key); the handshake options set `AllowRenegotiation = false`.
- **Gates re-run:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` twice (after each fix): **1,035 total, 1,028 passed, 0 failed, 7 skipped**, 0 warnings, lint 334 files and
  self-test, Dapper and EF guards, H1. AC11 on the Mac against Pebble (`sha256:ddf23064...`, after the renegotiation change): **3 of 3**, order 723, 670, 667 ms. Image
  rebuilt from `b89974c` (label checked); `bin/pixel-parity --fast --seeds default --only 'auth/sign_in*,rooms/show/designers,users/**'`: **34/34 pixels, aria and loose
  DOM equal**. `bin/clean-parity`, `bin/clean-docker`; 32 GB free. No bench (nothing on a request path changed).
- **KLOC correction:** 7.3's entry repeated 7.2's count: `bin/kloc` reads `git ls-files`, and it ran before `Acme.fs` was committed. Now **29,702 by upstream's rules,
  30,157 corrected (30.2)**, App 12,749 corrected; 7.3 itself was about +340 corrected (`Acme.fs` is about 390 lines against the note's 190). The phase lands nearer 27.8
  than 27.6.
- **Gate readings:** 7.2's "room page and post not below the control's range" failed on three cells against `p6ctl`, and the cause is 7.1's log, not the cache: accepted
  on the cross-session comparison with 7.1's numbers and the equal CPU per request. 7.1's log costs about 2-3x S5's projection (c=1 cells -5.7% to -11.2%); thinnest now
  post c=16 5.97x and post c=1 6.18-6.20x, sidebar c=1 7.57x.
- **Notes for 7.4-7.6 (binding unless the builder records why not):**
  1. **Split 7.4** by the note's rule: it does not fit 90 minutes. 7.4a: `FrontSettings.fromLookup`/`fromEnv`, `Host.fs` on plain HTTP, `Platform/OpenFiles.fs`, the swap in
     `Boot.serve` when `TLS_DOMAIN` is unset (a TLS install stays on the old front), HSTS, `MaxRequestBody`, HS1, HS3, HS5-HS12, image, asset-check, cookie, pixels, bench.
     7.4b: the TLS listener, F6's branch, `AcmeService` and the `"acme"` client wired, HS2 and HS4 (Linux), and the swap for TLS installs. Then 7.5.
  2. **F6, the HTTP port with TLS: keep Thruster's host check.** Answer 421 to a `Host` outside `TLS_DOMAIN` (normalized with `ServerNames.normalize`) and redirect the
     others to `https://<normalized host>[:HTTPS_PORT]<path>?<query>`; answer the challenge only for a `TLS_DOMAIN` host. `UseHttpsRedirection` alone reflects any `Host`
     into a cacheable 301; the old port and Thruster did not. HS2 gains the 421 case; drop the README's "redirects every host" line.
  3. **HSTS on exception pages.** Rails' `ActionDispatch::SSL` is outermost, and Edge's `onStarting` line survived `UseExceptionHandler`'s header clear. `UseHsts` inside
     `UseExceptionHandler` will lose it on a 500. HS5 must include a 500 page; if it loses the header, keep HSTS in Edge's `onStarting` (or run `assumeSsl`/`UseHsts` before
     the exception handler) rather than recording a class A line.
  4. **The first-handshake wait must be bounded** even when `HTTP_READ_TIMEOUT` means none: give the TLS handshake its own finite timeout (Kestrel's 10 s default, or the
     read timeout when finite), so a failing order cannot hold connections open forever.
  5. **Validate `TLS_DOMAIN` at boot:** `CertificateStore` silently drops a name `ServerNames.normalize` refuses; `FrontSettings.fromLookup` (or the host) should log or
     fail on it. Consider `IdnMapping(UseStd3AsciiRules = true)` to match Go's `idna.Lookup`, with a test for an underscore name first.
  6. **Bench:** run 7.4a's bench against `p6ctl` and a same-session control of HEAD before the swap (`b89974c` or later), so the swap's effect is isolated. Recheck the avatar
     hit (168 us at c=1) and the c=1 cells; if any cell nears 5x, apply risk 1's ladder (console queue settings, then the interceptor) before anything hand-written.
  7. Keep `StaticAssets.map`'s call site, the marker middleware and `appEndpointsOnly` where they are (Phase 6's condition); `bin/asset-check` after the swap.
  8. KLOC: run `bin/kloc` after committing (it reads `git ls-files`).

### Phase 7, unit 7.4a: the app's own plain-HTTP host (2026-10-26)

Sonnet builder, commits `414713f` (code, tests) and the README commit after it; plus this entry. The checkpoint's split applies: this is the plain-HTTP half of 7.4; 7.4b is the TLS half.
- **Built:** `Web/FrontSettings.fs` gains `fromLookup`/`fromEnv` (Thruster's variables, the `THRUSTER_` prefix, Go's `ParseBool`/`Atoi`, defaults as Thruster's; a timeout of zero, a negative one or
  49 days or more is `TimeSpan.MaxValue`, the value the old front used and Kestrel accepts as "disabled"; `H2C_ENABLED`, `GZIP_COMPRESSION_*`, `HTTP_WRITE_TIMEOUT`, `DEBUG` not read). `Web/Host.fs`
  (after `Controllers.fs`): `configureKestrel` (the head limits, UTF-8 headers, no `Server` header), `listen` (`HTTP_PORT` any address HTTP/1.1; `TARGET_PORT` on `TARGET_BIND`, skipped with a
  warning when it is the HTTP port; `KeepAliveTimeout` = `HTTP_IDLE_TIMEOUT`, `RequestHeadersTimeout` = `HTTP_READ_TIMEOUT`; Kestrel's default minimum data rates), `configureForwarding`
  (`FORWARD_HEADERS` off turns `UseForwardedHeaders` off), `build` (`CreateSlimBuilder(StaticAssets.hostOptions ...)`, the app's services, `HostOptions.ShutdownTimeout` 10 s under ConsoleLifetime,
  the app's pipeline on the host itself; it refuses `TLS_DOMAIN` with a message naming 7.4b). `Platform/OpenFiles.fs`: `Server.raiseOpenFileLimit`/`openFileLimits` moved unchanged. `Boot.serve`:
  `FrontSettings.fromEnv`; without `TLS_DOMAIN` it is `Host.build` and `app.RunAsync()` (ApplicationStarted logs `Server started`, ApplicationStopping `shutting down`, then the drains and
  `Server stopped`; an `OperationCanceledException` from the stop is the "requests still running at shutdown were abandoned" warning), with `TLS_DOMAIN` it is the old front exactly as before.
  `Boot.bootEdge` takes an `EdgeConfig -> EdgeConfig` (`Boot.servingEdge` applies `LOG_REQUESTS`, the cache sizes and `MAX_REQUEST_BODY`). `Edge`: `force_ssl`'s redirect and `redirectToHttps`
  deleted; `EdgeConfig.MaxRequestBody` (16 MiB, or `MAX_REQUEST_BODY` when lower) read by `bodyLimit` and `configureForms`. `Support.startPipelineOn` uses `Web.Host.configureKestrel`.
- **Differences from the unit row (each argued):** (1) **HSTS stays in `Edge`'s `onStarting`, no `UseHsts`.** I built it as the note said (`AddHsts` 730 days, `IncludeSubDomains`, exclusions cleared,
  `UseHsts` after `assumeSsl`); HS5's 500 case, an exception thrown outside an action so that it reaches `UseExceptionHandler`, **failed: the header was gone** (the framework's middleware sets it
  before the next one runs and the handler clears the response's headers). Checkpoint note 3 says to keep the `onStarting` line in that case, so the unit-7.3 string
  (`max-age=63072000; includeSubDomains`, which is 730 days) stays, and there is no class A line. (2) **`UseHttpLogging` stays first in `Web.Services.pipeline`**, not moved into the host: the host's
  pipeline is that pipeline, so it is already outermost, and moving it would only take the log out of the test hosts (LG1-LG4 run on them). (3) `Host.build` takes `configureServices` and `pipeline`
  rather than `Booted` (the type lives in `Boot.fs`, after `Host.fs`). (4) `Boot.serve` still calls `Front.serveHosted` for `TLS_DOMAIN` installs (the split); `Front.serveHosted` is not yet unused.
  (5) F11's "empty `KnownIPNetworks`" is `ForwardedHeaders.None`: an empty list of known proxies makes the framework trust every peer, which is the trust-all the planted failure plants.
- **Tests** (`tests/Campfire.App.Tests/Web/HostTests.fs`, 12, over `Host.build` on free loopback ports): HS1 `/up` on both ports, `TARGET_PORT` bound to `127.0.0.1` (`0.0.0.0` with `TARGET_BIND`),
  `TARGET_PORT` = `HTTP_PORT` one listener; HS3 the HTTP port serves the app; HS5 (two) HSTS on a page, to `localhost` and another host, a public file, an action's 500, a middleware's 500 (via a
  throwing `/cable`), a 404, and absent with `DISABLE_SSL`; HS6 the head limits through `Host.build` (R7 and R8 already run through `Host.configureKestrel` via `Support`); HS7 `FORWARD_HEADERS` on
  (a forwarded banned address is a 429, also through a chain) and off (the forged header, `X-Forwarded-Proto` and `-Host` ignored); HS8 `HTTP_IDLE_TIMEOUT` closes an idle keep-alive connection after
  1.5-7 s with a 2 s setting, `HTTP_READ_TIMEOUT` cuts unfinished headers (408 or close); HS9 `MAX_REQUEST_BODY` 1,024 gives 413 at 2,000 bytes and not at 100, a larger or zero value leaves 16 MiB;
  HS10 `99999999999` and `0` seconds start the host with `MaxValue` limits; HS11 `StopAsync` sends `server_restart` to two Cable sockets, closes them, and finishes inside the 10 s grace, with
  `ShutdownTimeout` = `Boot.ShutdownGrace`; HS12 `FrontSettings.fromLookup` (defaults, prefix, Go's booleans and integers, `TARGET_BIND`, timeouts, `TLS_DOMAIN`, EAB, ignored variables); and the
  open-file limit test moved from `ServerTests`. All green on the first run except HS5's middleware 500 (the finding above). **P12 and the Cable restart tests green** (in `bin/verify`).
- **Planted failure:** `configureForwarding` made a no-op (`if false && ...`, the equivalent of `KnownIPNetworks` left as Rails' with `FORWARD_HEADERS` off): **HS7 fails** ("a forged X-Forwarded-For was
  honoured: 429"); reverted. HS4's planted `ServerCertificateSelector` belongs to 7.4b.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **1,047 total, 1,040 passed, 0 failed, 7 skipped** (1,035 / 1,028 / 7 before; +12), Release, warnings as errors, lint (336 files) and self-test, Dapper and EF
  guards, H1. `bin/kloc` after committing: **29,885 by upstream's rules, 30,340 corrected (30.3)** (29,702 / 30,157 after 7.3: +183 corrected, all App: `Host.fs` 83, `OpenFiles.fs` 58, `FrontSettings.fs` +99 against
  the Edge and Boot lines removed). Image rebuilt from `414713f` (label checked). In the image (colima): `/up`, `POST /session` (302 with the session cookie) and the room page (200, 377,512 bytes)
  through `HTTP_PORT`, and `/up` and the room page through `TARGET_PORT` (from inside the container, loopback); `docker stop`: exit 0, `shutting down` then `Server stopped` 8 ms later;
  `bench/lib/logcount`: **20,000 requests, 20,001 lines** (the readiness probe), SIGTERM, every line counted. `bin/asset-check`: **1,806 checks, 0 failed**; `git diff 0328698 HEAD` of `StaticAssets.fs` is
  empty (`StaticAssets.map`'s call site, the marker middleware and `appEndpointsOnly` unchanged). `bin/cookie-roundtrip`: **20/20**. `bin/pixel-parity --fast --only 'auth/**,errors/**,bot_api/**,rooms/show/designers'`
  on every seed: **PASSED** (default 38/38, crowd 0/0, custom_styles 2/2, first_run 8/8, restricted 0/0; pixels, aria and loose DOM equal). `bin/clean-parity` and `bin/clean-docker` run; 32 GB free.
  The `errors/**` network layer is not part of this gate's report (reported only), so HSTS on a real exception page is covered by HS5's middleware case.
- **Bench** (`bench/quick`, the five workloads plus avatar, c=1 and 16, 3 reps, interleaved, detached and polled; evidence `bench/results/dotnetway-7-4a/`). **Run A, the swap isolated:** new `414713f` against
  `campfire-dotnetway:p7pre`, a same-session control of `0328698` (HEAD before the swap, built from `git archive` plus the submodule, label checked), host probe 1.04-1.08 before every window (a few
  windows above 1.10 after: avatar c16, post c16, search c1), Mac load 1.3-3.9; 36 runs, all 2xx, 0 errors. req/s new / p7pre: room 7,481 / 7,152 (c16, +4.6%), 2,192 / 2,168 (c1, +1.1%); messages 7,331 /
  6,888 (+6.4%), 2,036 / 2,020 (+0.8%); sidebar 7,583 / 7,276 (+4.2%), 2,133 / 2,095 (+1.8%); search 7,101 / 6,686 (+6.2%), 1,879 / 1,824 (+3.0%); post 2,118 / 2,021 (+4.8%), 1,025 / 1,006 (+1.9%); avatar
  98,255 / 94,251 (c16), 12,907 / 12,468 (c1); CPU per request c1 569 / 603 / 580 / 644 / 1,770 / **157 (avatar)** us against 574 / 606 / 588 / 659 / 1,806 / 164: **the swap is a small gain on every cell**
  (-1% to -7% CPU), as removing the old front's hop should be; idle memory 83 MB against 75 MB (+8 MB), peak 381 against 374. **vs Rails** (stored medians): room 31.3x (c16) / 20.5x (c1), messages 19.3x /
  9.35x, sidebar 12.7x / **7.44x**, search 17.4x / 9.75x, post **6.42x** (c16) / **6.14x** (c1): **no cell under 5x; the thinnest is post c=1 at 6.14x**. **Run B, against `p6ctl`** (`309ab42`, batched log and the old
  front): the session was noisy (another program loaded the Mac: load averages up to 31 at 15:22, probe 1.10-1.27 on several windows, wide rep ranges, warm-ups reaching their maximum), so its medians are
  low and not a number to quote: new / p6ctl room 5,988 / 6,503 (c16), 1,838 / 2,298 (c1), messages 6,140 / 7,062, 1,964 / 2,115, sidebar 6,832 / 7,292, 2,051 / 2,308, search 6,463 / 6,829, 1,715 / 1,985, and vs
  Rails the lowest cell was post at 5.62x (c16) and 5.79x (c1). The **thinnest cell was therefore re-run alone** (`--routes post_message,avatar`, 3 reps, host probe 1.04-1.07 before every window, load falling from
  31 to 4): post c16 **2,139 [1,990-2,140] = 6.48x** (control 2,028), post c1 **1,059 [1,018-1,071] = 6.35x** (control 1,113), CPU 1,515 / 1,750 us (control 1,480 / 1,654). So against `p6ctl` post is +5% at c16 and
  -5% at c1, the c=1 gap being 7.1's request log (measured then at -5.7% to -11.2%).
  **Avatar** (the checkpoint's recheck): a hit costs **157-158 us at c=1** (168 at 7.2, 164 before the swap, 88-95 with `p6ctl`'s cache) and 24.9-26.7 us at c=16 (19.4-19.8 for `p6ctl`). The swap took 7-10 us off
  the c=1 hit; the rest of the gap to `p6ctl` is the HTTP logging line (7.1 measured +61 us at c=1) and OutputCache, which no cell's floor needs; risk 1's ladder (console logger queue settings, then the
  interceptor) is **not triggered** (no cell is near 5x) and is the first thing to try if a later unit needs the c=1 hit back.
- **Differences recorded:** README class B (the host: listeners, no h2c, timeouts and minimum data rates, `FORWARD_HEADERS`, `MAX_REQUEST_BODY`, the 10 s stop, `force_ssl`'s unreachable redirect); no class A line (HSTS
  survives the exception page).
- **Open for 7.4b** (checkpoint notes 1-5, binding): the TLS listener (`UseHttps(TlsHandshakeCallbackOptions)` on `store.HandshakeOptions`, a finite handshake timeout even when `HTTP_READ_TIMEOUT` is none), F6's
  branch on the HTTP port with the **421 for a Host outside `TLS_DOMAIN`**, the redirect to `https://<normalized host>[:HTTPS_PORT]<path>?<query>` and the challenge only for `TLS_DOMAIN` hosts (drop the README's
  "redirects every host" line), `AcmeService` and the `"acme"` `HttpClient` wired into `Host.build`, `TLS_DOMAIN` validated at boot (names `ServerNames.normalize` refuses are logged or fail; test an underscore name
  against `IdnMapping(UseStd3AsciiRules = true)`), HS2 (with the 421 case) and HS4 (also under `bin/linux-tests App`, the planted `ServerCertificateSelector` fails it), and `Boot.serve` on `Host.build` for TLS
  installs too, after which `Front.serveHosted` has no caller. The `p7pre` control image stays for 7.4b's same-session bench (remove it with `p6ctl` at the phase's end). Evidence:
  `bench/results/dotnetway-7-4a/` (`vs-p7pre`, `vs-p6ctl`, `vs-p6ctl-post`).

### Phase 7, unit 7.4b: TLS in the app's host (2026-10-26)

Built on `dotnetway` from `eee2215`; one code commit (`0099ef0`) plus this entry. The unit row's TLS half, per checkpoint notes 1-5.

- **Built:** `Web/Host.fs` gains the HTTPS listener (`ListenAnyIP(HTTPS_PORT)`, HTTP/1.1 and HTTP/2, `UseHttps(TlsHandshakeCallbackOptions)` whose `OnConnection` is `CertificateStore.HandshakeOptions`), `Host.handshakeOptions`
  (`HandshakeTimeout` = `HTTP_READ_TIMEOUT`, **30 s when that means none**, note 4), `Host.httpPortHandler` (F6) behind a terminal `MapWhen` on the HTTP port (with `UseHttpLogging` first on that branch when
  `LOG_REQUESTS` is on), `Host.validateDomains` (note 5), and the `CertificateStore`, `FrontSettings`, `AddHttpClient "acme"` and `AcmeService` registrations when `TLS_DOMAIN` is set. F6 as the note says: a Host outside
  `TLS_DOMAIN` (normalized with `ServerNames.normalize`, port and case dropped) is a **421**, its ACME challenge path a 403 (as Thruster's and the old port's host check answered), a `TLS_DOMAIN` host's challenge is the store's
  token (404 for an unknown one), and everything else is a 301 to `https://<normalized host>[:HTTPS_PORT]<raw target>` (`Connection: close`, Go's `<a href>` body for GET). I wrote the redirect by hand instead of
  `UseHttpsRedirection`, which redirects any Host. `Boot.serve` is `serveHosted` for every install: `serveBehindOldFront` is deleted and `Front.serveHosted` has no caller in the App (the Kit's Front stays until its own unit).
- **`TLS_DOMAIN` validation (note 5):** each name `normalize` refuses is logged at error level; a list with none usable stops the boot (`InvalidOperationException`). `ServerNames.toAscii` now uses
  `IdnMapping(UseStd3AsciiRules = true)`: Go's `idna.Lookup` is an STD3 profile, so an underscore name is refused (it was accepted before; the test pins both that and a punycode name).
- **Tests (`HostTests`, 4 new):** HS2 (redirect with port, normalization, POST, the 421 cases `other`, `localhost`, an IP, a suffix trick and `[::1]`, the challenge 200/404/403, TARGET_PORT plain), HS4 (own certificate per
  SNI over a real `SslStream`, uppercase and trailing-dot names, the handshake refused for another name, no name, a refused name, and the app answering HTTP/1.1 over it), HS4's timeout case (a name with no certificate
  fails within `HTTP_READ_TIMEOUT`, 2 s, in under 12 s; the finite budget when it is none) and the boot validation. **Planted failures:** a `ServerCertificateSelector` returning one fixed certificate fails both HS4 tests
  (the unknown-name handshake succeeds); HS7's trust-all was planted in 7.4a. HS4 and HS2 also pass under `bin/linux-tests App` (colima, 3/3). The checkpoint's other conditions: HSTS stays in `Edge` (7.4a, HS5 covers a 500).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **1,051 total, 1,044 passed, 0 failed, 7 skipped** (the environmental seven, AC11 among them: Pebble was not started this unit; AC11 passed 3/3 in 7.3 and nothing in
  `Acme.fs` but the STD3 name rule changed). Release with warnings as errors, lint, Dapper and EF guards, H1, P12 and the Cable restart tests green inside it. `bin/asset-check`: 1,806 checks, 0 failed
  (`StaticAssets.fs` untouched). `bin/cookie-roundtrip`: 20/20. `bin/pixel-parity --fast --only 'auth/**,errors/**,bot_api/**,rooms/show/designers'` on every seed: **PASSED** (default 38/38, crowd 0/0, custom_styles 2/2,
  first_run 8/8, restricted 0/0). `bin/kloc` after committing: **29,965 by upstream's rules, 30,420 corrected (30.4 KLOC)**, +80 corrected since 7.4a.
- **Image smoke (rebuilt from `0099ef0`, label checked; colima, host network, a self-signed certificate in the ACME cache for `campfire.test`, `HTTP_PORT=8080 HTTPS_PORT=8443 TARGET_PORT=3999`):** the log says
  `Loaded the cached certificate` then `Server started http=:8080 https=:8443 tls_domain=campfire.test`; `https://campfire.test:8443/up` 200 over HTTP/2; `/session/new` 302; `Host: campfire.test` on the HTTP port 301 to
  `https://campfire.test:8443/rooms/1?a=b`; `Host: evil.test` 421; SNI `evil.test` refused; `TARGET_PORT` `/up` 200; `docker stop` logs `shutting down` then `Server stopped`. A real order against a CA is AC11's (Pebble).
- **Bench** (`bench/quick`, five workloads plus avatar, c=1 and 16, 3 reps, interleaved, detached and polled; new `0099ef0` against `campfire-dotnetway:p7pre` (`0328698`, before the swap), host probe 1.06-1.09 before
  every window, a few windows above 1.10 after (avatar c16, post c16, search c16); 36 runs, all 2xx, 0 errors; `bench/results/dotnetway-7-4b/vs-p7pre/`). The session was slower than 7.4a's (the control's room c=1 is
  2,045 against 2,168 then), so compare the pair, not the days. req/s new / p7pre: room 6,896 / 6,768 (c16), 1,877 / 2,045 (c1, a wide control range 1,540-2,132); messages 6,813 / 6,467, 1,933 / 1,869; sidebar 6,433 / 6,954
  (c16, new range 4,872-6,903: its warm-up never settled), 2,055 / 1,906; search 6,748 / 6,192, 1,819 / 1,699; post 1,837 / 1,578 (c16, new range 806-2,055: one rep fell to 806 with the host at 1.10), 1,001 / 866;
  avatar c=1 12,057 / 12,006 (**163 us a hit against 167**), c=16 89,541 / 84,993. The 7.4b change touches only TLS installs, so a plain-HTTP cell moving more than noise would have been a defect: none did. **vs Rails** (stored
  medians): room 28.8x / 17.5x, messages 17.9x / 8.87x, sidebar 10.7x / 7.17x, search 16.5x / 9.44x, post **5.57x** (c16) / **6.00x** (c1): **no cell under 5x; the thinnest is post c=16, whose median is dragged by the one
  806 rep (the other two are 1,837 and 2,055: 5.6x and 6.2x)**. 7.4a measured the same cell at 6.42x on a quiet host; risk 1's ladder is not triggered. `p7pre` and `p6ctl` stay for 7.5.
- **Differences recorded:** README class B (the TLS listener and its handshake budget, F6's 421/403/301 and the challenge, the boot validation, STD3 names); `plans/design-front.md`'s "redirects every host" line is replaced.
  No class A line.
- **Open for 7.5:** nothing from 7.4. Remove `p7pre` and `p6ctl` at the phase's end. A real ACME order through the app's own host (rather than AC11's service-level run) was not exercised end to end; AC11 plus the
  smoke above cover its two halves.

### Phase 7, unit 7.5: TLS end to end (2026-10-26)

Built on `dotnetway` from `cc1e342`; one code commit plus this entry. Harness and scripts only: no `src/` change, so no bench (no response changed).

- **Harness TLS mode (F16):** `PARITY_CANDIDATE_TLS=1` / `bin/pixel-parity --candidate-tls`. `parity/bin/candidate up` makes a self-signed P-256 certificate for `campfire.localhost` with the
  image's own openssl 3.5 (the VM's 3.0 has no `-not_before`; the certificate is valid 2020-2049 because the server clock is frozen at the seed's instant, and one valid from today
  would be "not yet valid" to the app), writes it as a `DirCache` file (`EC PRIVATE KEY` then the certificate, 0600 in a 0700 directory) into a mounted `storage/thruster`, and sets `TLS_DOMAIN`,
  `HTTPS_PORT` = the port (published on 127.0.0.1:N), `HTTP_PORT=80` (published on N+100). `wait_until_up` curls the HTTPS name with `--resolve`. `parity/capture/cli.ts` gained
  `--actual-origin` (the candidate is shown at `https://campfire.localhost:<CAND>`, the reference stays on `http://localhost:3999`; the capture's proxy tunnels the CONNECT to the server's port).
- **Three harness changes that the origin difference forced (none touches a non-TLS run's meaning):** (1) `session.ts`'s sign-in context sets `ignoreHTTPSErrors` (the first run failed with
  `ERR_CERT_AUTHORITY_INVALID` on `/session/new`); (2) `capture.ts` maps the target's own origin and host to the shared ones in every normalized layer (an absolute form `action` read
  `https://campfire.localhost:4112/session` against `http://localhost:3999/session`); (3) `determinism.js` strips the origin from the call site that seeds `Math.random` (Lexxy's
  link-input id differed because the script URL is part of the site). Without (2) and (3) the loose layer failed on both states.
- **Gate:** `bin/pixel-parity --fast --candidate-tls --only 'auth/sign_in,rooms/show/designers'` on `default`: **4/4 screenshots pixel-equal, 4/4 aria equal, 4/4 loose equal**
  (24 s). Non-TLS regression after the harness edits: `--fast --only 'auth/**,rooms/show/designers'` on `default`: 28/28, 28/28, 28/28 (132 s).
- **curl over TLS** (candidate up in colima, `curl --cacert` its own certificate): `https://campfire.localhost:4300/session/new` HTTP/2 200; `Host: campfire.localhost` on the HTTP port
  301 to `https://campfire.localhost:4300/rooms/1?a=b`; the log says `Loaded the cached certificate ... valid until 12/31/2049` (no order) and `Server started http=:80 https=:4300`.
- **`reference-tools/front/pebble-image.sh`** (new; Pebble and the production image `campfire-dotnetway:app` on host networking, `TLS_DOMAIN=campfire.test`, `HTTP_PORT=5002`,
  `HTTPS_PORT=8443`, `ACME_DIRECTORY`, `SSL_CERT_FILE` = Pebble's minica root): **PASSED**. Pebble issued exactly one certificate at boot; `storage/thruster/campfire.test` and `acme_account+key` 0600
  in a 0700 directory; `openssl s_client -showcerts` shows 2 certificates (leaf and intermediate) and HTTP/2 `/up` 200; after `docker restart` the log says `Loaded the cached certificate`,
  Pebble's issued count stays 1, the fingerprint is unchanged and the chain is still 2. (The script's first version checked the whole log for the cached load and passed or failed on
  the earlier boot's lines; it now reads `docker logs --since`.) Containers, root and storage are removed by the script's trap.
- **Operator step:** AGENTS.md has the TLS bullet (harness mode, Pebble script) and the Let's Encrypt staging steps (human-gated; not run).
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: **1,051 total, 1,044 passed, 0 failed, 7 skipped** (the environmental seven; AC11 needs `PEBBLE_DIRECTORY`, and `pebble-image.sh` is the end-to-end
  check this unit). `bin/kloc` after committing: **29,965 by upstream's rules, 30,420 corrected (30.4 KLOC)**, unchanged from 7.4b (harness and scripts are not in the count). `bin/clean-docker`
  run; Pebble image left (10.6 MB); disk 30 GB free.
- **Differences recorded:** none (no class A or B line: the harness mode changes no response).
- **Open for the phase:** 7.6 (delete Kit). `p7pre` and `p6ctl` images are still to be removed at the phase's end. The Pebble image can go with them.

### Phase 7, unit 7.6: Delete (2026-10-26)

Commits `5fbe11b` (the deletion) and the one after it (harness fix and evidence). Evidence: `bench/results/dotnetway-7-6/` (`pixels/`, `quick/`, `kloc.md`, `pixel-driver.sh`, `bench.sh`), `bench/results/front-dotnetway.md`.

- **Deleted:** `src/Campfire.Kit/` (project, `Server.fs`, `Front/*`: 3,263 lines by the design note's count), `tests/Campfire.Kit.Tests/` (13 files, `AcmeStub` and the `ObtainingCount` flake among them), their `CampfireFs.slnx` rows, the App's project reference, `open Campfire.Kit` in `Boot.fs`, `Support.fs` and `SeamTests.fs`, and `ZstdSharp.Port` from `Directory.Packages.props`. `git grep` for `Campfire.Kit`, `ZstdSharp`, `Front.serve`, `RequestLines` and `CertManager` over `src tests bin parity bench/{quick,run,front,lib} Dockerfile AGENTS.md README.md CampfireFs.slnx Directory.Packages.props` finds nothing (`Host.fs`'s `MaxRequestLineSize` is the word `RequestLine` in a Kestrel option name, not `RequestLines`).
- **Rewritten:** `bench/front/fsharp` (Program.fs 179 -> about 85 lines, project references `Campfire.App`; endpoints hello, a 100 KB page, an OutputCache hit on `/users/1/avatar` (identity, gzip, zstd by `Accept-Encoding`), a form post; `LOG_REQUESTS=false`). It had not built since Phase 2 (it used the Kit's deleted `Ctx` types). `bench/front/run` drops the signed-cookie scenario for the F# side. AGENTS.md (the Kit row folded into the App row, the front test bullet, `bin/linux-tests App`), README "Front server" (rewritten for Kestrel: the head limits, `Date`, the three parser rules, Class B for 7.1, 7.2, 7.4 and Certes, a Class A line; the h2c, 49-day clamp and ZstdSharp lines are gone), the Dockerfile and `Config.fs`/`Boot.fs`/`Services.fs` comments, `bin/linux-tests` and `bin/verify` usage lines, the candidate entrypoint comment. `bin/kloc` names no Kit.
- **Front bench** (`bench/results/front-dotnetway.md`, `wrk -t4 -c64`, host load about 20 at the end, so read the CPU column): Web.Host / campfire_kit us per request: tiny 29.6 / 25.1, 100 KB identity 73.8 / 84.0, gzip 61.4 / 73.4, cache hit 37.6 / 21.4, zstd hit 29.8 / 71.7, form post 29.3 / 24.5.
- **A regression of unit 7.5 found and fixed:** the full default-seed lean run failed nearly every cell, and the sliced run showed `realtime/removed_from_room` on WebKit NOT CAPTURED on the **reference** (the Rails side) three attempts in a row. Bisected by reverting the harness files of 7.5 one at a time (`--engines webkit --viewports desktop --schemes light`): the old harness passed, 7.5's `determinism.js` alone failed it, `session.ts`'s `ignoreHTTPSErrors` did not matter. 7.5's call-site change stripped every origin from the seeded `Math.random` site, which changed the seeds of all non-TLS runs. It now maps only `https://campfire.localhost:PORT` to the shared origin (`parity/capture/determinism.js`); the state passes (8/8 cells on the full slice) and the two TLS states still pass 28/28. (The monolithic `bin/pixel-parity --seeds default` run is not the gate's way: slices, `realtime/**` one state at a time. Its mass failure was not diagnosed beyond that.)
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` (Release, warnings as errors): **990 total, 984 passed, 0 failed, 6 skipped** (the Kit's tests went: 1,051 before). Image rebuilt from `5fbe11b` (label checked), `bin/clean-docker` run. `--fast --only 'rooms/show/designers'`: 2/2 equal on all three layers. **AC11 against Pebble** (colima, `bin/linux-tests App`): passed. **`reference-tools/front/pebble-image.sh`: PASSED** (one order at boot, 0600/0700 modes, 2-certificate chain, the restart loads the cache with no second order). **The two TLS states** (`--candidate-tls`, `auth/sign_in`, `rooms/show/designers`): 28/28 screenshots, aria and loose equal (twice, before and after the harness fix). **Lean matrix, default seed** (12 directory slices plus the 13 `realtime/**` states one at a time, `pixel-driver.sh`): every slice rc 0 except `removed_from_room` (the harness regression above), whose rerun after the fix is 8/8; **938 of 938 cells equal on all three layers before the rerun's 8, none NOT CAPTURED after it**; **the other four seeds in one run**: rc 0. `bin/asset-check`: 1,806 checks, 0 failed. `bin/cookie-roundtrip`: 20/20.
- **`bench/quick`** (five workloads plus avatar, `static_css`, `up`, c=1 and 16, 3 reps interleaved with the same-day control `campfire-dotnetway:p6ctl` in four parts each, host probe 1.05-1.09; 96 runs, all 2xx, 0 errors; `quick/{new,ctl}`, `compare-*.txt`). **Against Rails (stored medians), new:** room page 27.7x (c16) / 19.6x (c1), messages 17.2x / 8.99x, sidebar 11.2x / 7.09x, search 14.8x / 9.19x, post **6.03x** (c16) / **5.99x** (c1): **no cell under 5x**. New / `p6ctl` req/s: room 6,638 / 6,943 (c16), 2,104 / 2,356 (c1); messages 6,541 / 6,828, 1,960 / 2,177; sidebar 6,685 / 7,168, 2,031 / 2,381; search 6,055 / 6,570, 1,770 / 2,007; post 1,992 / 1,912, 999 / 1,051; the read workloads cost about 5-9% more than before Phase 7 (logging line, OutputCache, the host; the ladder of risk 1 is not triggered). Static cells new / `p6ctl`: avatar 12,100 / 23,200 (c1, 162 us against 93 us a hit), 98,500 / 120,000 (c16); `static_css` 11,300 / 16,000 and 85,300 / 95,300; `up` 10,900 / 15,400 and 72,700 / 88,100. These carry no Rails floor; they are the cost of one logged, cached request through the host (7.4a measured the avatar hit at 168 us at c=1).
- **KLOC (`bin/kloc` after committing):** **27,834 corrected (27.8 KLOC)**, 27,379 by cloc; Campfire.App 13,011. Phase 7 net from 29.97 (`309ab42`) is -2.14 corrected; the projection was about 27.6 (the host layer came in about 0.2 over: `Host.fs` carries F6 and the handshake options, `Acme.fs` the service). Rust's bar is 31.0.
- **Differences recorded:** README "Front server" now holds the whole of section 8 (class B and the Class A line). No new difference in this unit.
- **Open for the phase:** the verifier. `campfire-dotnetway:p7pre`, `campfire-dotnetway:p6ctl` and `campfire-candidate-p6ctl` are still in colima (remove them when the phase closes); the Pebble image and its directory are removed.

### Phase 7: gate (2026-10-26/27)

Evidence: `bench/results/dotnetway-p7/` (slim: logs, `report.json` per pixel slice, bench JSON, `kloc.md`). Gate runner. Image `campfire-dotnetway:app` built from `607b31c` (label checked equal to HEAD); control built from `309ab42` (Phase 6's closing commit, per its verification entry) in a scratch worktree, tag `campfire-dotnetway:p7gatectl`, label `309ab42`, removed afterwards with its worktree. One tooling fix, no app change (below).
- **`CAMPFIRE_REQUIRE_SEED=1 bin/verify`** on `607b31c`: 990 total, 984 passed, 0 failed, 6 skipped (the environmental six); lint self-test 39/39, lint 327 files clean; Dapper and EF guards, H1 and the Release warnings-as-errors build green (`verify.log`).
- **Phase-specific.** AC11 against Pebble (colima, `bin/linux-tests App`): passed. `reference-tools/front/pebble-image.sh` on the image: **PASSED, 10 of 10 runs after the fix; before it, 4 of 12 runs failed** "the restart did not log a cached load". Cause, a race in the script and not in the app: `since=$(date +%s)` has whole-second resolution, so when boot, order and checks all fit in one second, `docker logs --since` also matched the **first** boot's `Server started`, the wait ended before the restarted container had logged anything, and the grep for `Loaded the cached certificate` ran too early (the failing run's log shows only the first boot; the later checks, one order, same certificate, 2-certificate chain, passed). Fixed with a sub-second RFC 3339 `since`; the failing log is `pebble-image-racefail.log`. Unit 7.6's single pass was luck. TLS states (`--candidate-tls`, `auth/sign_in`, `rooms/show/designers`): **28/28** screenshots, aria and loose equal. `bench/front/run fsharp` (wrk -t4 -c64, 8 s, host load about 3; `front-run.log`), req/s and server CPU us/request: tiny 190,708 / 32.1, 100 KB identity 69,892 / 76.5, gzip 138,577 / 57.0, cache hit 157,735 / 38.7, zstd hit 194,131 / 31.8, form post 192,809 / 32.4.
- **Lean pixel gate, every seed** (12 directory slices on default, the 13 `realtime/**` states one at a time, the other four seeds in one run, three browsers, both themes, breakpoints): **940 of 940 cells equal on screenshot, aria and loose DOM**, no cell NOT CAPTURED; every slice rc 0.

| seed | screenshots | aria | loose DOM |
|---|---:|---:|---:|
| default | 860/860 | 860/860 | 860/860 |
| crowd | 24/24 | 24/24 | 24/24 |
| custom_styles | 32/32 | 32/32 | 32/32 |
| first_run | 16/16 | 16/16 | 16/16 |
| restricted | 8/8 | 8/8 | 8/8 |

  One incident: the first `rooms/**` slice (after 11 slices that passed) stopped writing at cell 137 of 172, on the fresh-server cells (`rooms/directs/new`), and sat 37 minutes with no new file and no CPU; I killed it (the driver, then `bin/pixel-parity` in the VM, then its containers) and ran the slice again: 170/170 equal on all three layers in 488 s, nothing else changed. Not diagnosed beyond that; the hung run's log is `pixels/hung/`. The progress lines of a run print `fail` per cell for the ungated layers (the network layer differs on nearly every cell, Phase 6 verification); the verdict is the summary.
- **`bin/cookie-roundtrip`** 20/20 both ways. **`bin/db-differential`** rc 0 (schemas agree, reference boots on the database we wrote, rollback ok).
- **Speed** (`bench/quick`, five workloads plus `avatar`, `static_css`, `up`, c=1 and c=16, 3 reps interleaved new/ctl/ctl/new/new/ctl, each rep in four parts). Host probe 1.035-1.066 before runs (new), 1.025-1.067 (ctl), windows 1.03-1.09 after; Mac load average 2.6-6.5 at part boundaries (other programs on the Mac; the probe says the pinned cores were not slowed); 48 runs per side, all 2xx, 0 connection errors, post rows equal to the 2xx answers, decoded bytes equal the stored sizes. Medians:

| workload | c | new req/s | control req/s | new vs control | x Rails (new) | x Rails (control) | CPU us/req new / control |
|---|---:|---:|---:|---:|---:|---:|---|
| room page | 16 | 7,234 | 7,399 | -2.2% | 30.23 | 30.92 | 456 / 437 |
| room page | 1 | 2,227 | 2,519 | -11.6% | 20.80 | 23.52 | 563 / 496 |
| messages page | 16 | 7,106 | 7,339 | -3.2% | 18.67 | 19.28 | 476 / 453 |
| messages page | 1 | 2,085 | 2,339 | -10.9% | 9.57 | 10.73 | 594 / 524 |
| sidebar | 16 | 7,357 | 7,696 | -4.4% | 12.29 | 12.86 | 454 / 426 |
| sidebar | 1 | 2,224 | 2,581 | -13.8% | 7.76 | 9.01 | 572 / 479 |
| search | 16 | 6,801 | 7,082 | -4.0% | 16.67 | 17.36 | 505 / 478 |
| search | 1 | 1,913 | 2,157 | -11.3% | 9.93 | 11.19 | 639 / 557 |
| post a message | 16 | 2,113 | 2,013 | +5.0% | 6.40 | 6.10 | 1,513 / 1,485 |
| post a message | 1 | 1,074 | 1,121 | -4.2% | 6.43 | 6.72 | 1,726 / 1,648 |

  **No cell under the 5x floor** (thinnest: post c=16 6.40x, post c=1 6.43x, sidebar c=1 7.76x); no speed fix made. Cost of Phase 7 against Phase 6's close: reads -2% to -4% at c=16 and -11% to -14% at c=1 (CPU per request +4% to +19%; the c=1 cost is the per-request logging line and the host's extra stages, as the design measured: +14 to +38 us at c=1), posting +5% / -4% (inside the control's range, 1,974-2,176 at c=16). Static rows new / control: avatar 12,725 / 24,970 (c=1), 103,863 / 124,992 (c=16); `static_css` 12,051 / 17,188 and 91,955 / 100,076; `up` 11,577 / 16,351 and 78,958 / 90,000: the cost of one logged request through the host, no Rails floor on them. (`bench-new.log` notes "avatar c=16 warm-up did not settle", the avatar row's own warm-up, not a gate cell.)
- **KLOC:** `bin/kloc` 27,379 by upstream's rules, **27,834 corrected (27.8 KLOC)**; App 13,011, Views 5,219, Db 2,460, RichText 2,268, Storage 1,646. From 30.0 at Phase 6's close, -2.14; Rust's bar is 31.0. Unchanged by this unit.
- **Differences recorded:** none new.
- Disk: 17-32 GB free; `bin/clean-docker` and `bin/clean-parity` run, `fstrim` once in the VM; the control image, its candidate image and the Pebble container are removed. Still in colima from earlier units: `campfire-dotnetway:p7pre`, `p6ctl`, `campfire-candidate-p6ctl` (remove when the phase closes).

### Phase 7: security review of the front server (2026-10-27)

Opus reviewer, on `276219c`. Findings and method in `plans/design-front.md` section 11 ("Security review"); fix and tests in `0af3c67`. Evidence:
`bench/results/dotnetway-p7-security/` (`tls-probe.txt`, `query-filter-micro.md`, `vulnerable-packages.txt`, `verify.log`, `kloc.md`).
- **Read:** the phase's code whole (`Web/Host.fs`, `Web/Acme.fs`, `Web/HttpLog.fs`, `Web/OutputCaching.fs`, `Web/FrontSettings.fs`, `Web/Edge.fs`, `Web/Services.fs`' services and pipeline,
  `Boot.fs`' `serve` and logger factory, the controllers' max-age constants), its tests (`HttpLogTests`, `HostTests`, `OutputCachingTests`), the README's Front server entry and the
  harness's TLS mode, against Rails' `filter_parameter_logging.rb`, `active_storage*.rb` and the avatar, logo and QR controllers: 15 files of the phase plus 5 of the reference.
- **R1 (Low, fixed):** the request log wrote the query as sent, so a value whose key Rails' `filter_parameters` names (`token`, `passw`, `email`, `_key`, `secret`, ...) reached it in clear;
  Rails' own line logs `filtered_path`. The logging interceptor now logs Rails' `filtered_query_string` in place of `RequestQuery` when a key matches. SR2 failed before the fix
  (`tok-secret` in the line) and passes after. Cost 28-49 ns a request on the bench's queries (tier 1, `query-filter-micro.md`): no `bench/quick` run, the change is under 0.01% of
  the thinnest cell's CPU per request (post, about 1.5 ms).
- **R2-R9 (info, holding), each pinned by a test:** log forging (path and query percent-encoded, user agent cleaned, the framework's own URL lines held at Warning: SR1, SR3); TLS
  options at the platform's defaults with no renegotiation (SR7) and, in the image, TLS 1.0/1.1 refused, TLS 1.2 ECDHE-ECDSA AEAD and SHA-2 suites only, TLS 1.3 three suites, no SNI
  refused, ALPN `h2` (`tls-probe.txt`); the ACME challenge answers only a pending token for a `TLS_DOMAIN` host on the HTTP port (SR5); key files 0600 through `O_EXCL` and rename in a 0700
  directory (an existing directory is not tightened, as autocert); HSTS on every HTTPS response and never on the HTTP port, the redirect never leaves the `TLS_DOMAIN` name (SR4, the image);
  the output cache stores no cookie, no non-200 and nothing for an `Authorization` request, and touches only the five public families (SR6, SR8); forwarded headers and limits as designed.
  `dotnet list package --vulnerable --include-transitive` on the App: none.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` on the fix: **998 total, 992 passed, 0 failed, 6 skipped** (the environmental six, AC11 among them; 990 before, +8 SR tests), Release with
  warnings as errors, lint self-test 39/39 and 327 files clean, Dapper and EF guards, H1. The image probe ran the image built from `607b31c` (no `src` change since) in the harness TLS mode,
  then again with `DISABLE_SSL` empty for HSTS; both instances stopped. No pixel run (no response a page shows changed; the log line is not in the harness), no image rebuild.
- **KLOC (`bin/kloc` after committing):** 27,406 by upstream's rules, **27,861 corrected (27.9 KLOC)**; App 13,038 (+27, the filter). 
- **Differences recorded:** README class B, the request log: query values Rails filters are `[FILTERED]` (Thruster logged them as sent); paths are logged as sent, as Rails and Thruster log them.

### Phase 7: independent verification (2026-10-27) - PHASE 7 DONE; Phase 8 GO, with conditions

Opus verifier, adversarial review of `3696f18..031a492` (design, 7.1-7.6, the checkpoint, the gate, the security review) against `plans/design-front.md`,
plan sections 2.4 and 3-5 and D1-D9, cheap checks re-run and an image of the closing commit re-gated. Fix in `2fae069` (comments and docs; no behaviour
change). **Phase 7 closes at `2fae069`** (this entry and the evidence are the commit after it). Evidence: `bench/results/dotnetway-p7-verify/`.
- **Deleted, checked:** `src/Campfire.Kit/`, `tests/Campfire.Kit.Tests/`, `bench/log-micro/`, their slnx rows and `ZstdSharp.Port` are gone. `git grep` for `Campfire\.Kit`,
  `ZstdSharp`, `Front\.serve`, `RequestLines`, `CertManager`, `serveWithLines`, `FrontHandler`, `NoLifetime`, `DeadlineStream`, `withDeadlines`, `FrontH2c` over 7.6's acceptance
  paths found **two stale comments 7.6 reported as clean** (`bench/front/rust/src/main.rs` and `bin/splice.rs` naming `Campfire.Kit`) and a leftover zstd comment in
  `Directory.Packages.props`; fixed in `2fae069`, with `Acme.fs`' header ("not yet wired into the production host"), `Host.listen`'s doc comment (it sat on
  `fallbackHandshakeTimeout`), the logger factory's cut-off comment, `Boot.run`'s "behind the front server" and README's logging line ("the request lines come from the
  front server as Thruster's did"). Package added: Certes 4.1.0 (BouncyCastle.Cryptography 2.7.0), nothing vulnerable (`dotnet list package --vulnerable`, the security review).
- **Read, against the note:** `Web/Host.fs`, `Acme.fs`, `HttpLog.fs`, `OutputCaching.fs`, `FrontSettings.fs`, the pipeline of `Services.fs`, `Edge`'s HSTS, `Boot.serve` and the
  logger factory. The note's departures are each recorded and argued in the Phase log: HSTS stays in `Edge.onStarting` (F7; HS5's 500 case), the HTTP port's branch is
  hand-written with Thruster's 421 instead of `UseHttpsRedirection` (F6; checkpoint note 2), the handshake budget is 30 s when `HTTP_READ_TIMEOUT` is none (note 4),
  `TLS_DOMAIN` is validated with STD3 names (note 5), and `UseHttpLogging` stays first in the app's pipeline (7.4a (2)). Tag eviction (D5) is dropped by F9's argument
  (`?v=` keys). `StaticAssets.map`, the marker middleware and `appEndpointsOnly` are where Phase 6 left them (`UseOutputCache` sits after `StaticAssets.map`, so no
  asset is cached). Phase 6's conditions hold.
- **Planted failures (re-run here):** the `Range` exclusion dropped from the cache's policies fails OC4; HSTS sent without `Request.IsHttps` fails nothing, and is
  equivalent in every configuration (`assume_ssl` and `force_ssl` are both `not DISABLE_SSL`, and the HTTP port of a TLS install never reaches `Edge`). Reverted; `git status` clean.
- **Gates re-run on `031a492` and `2fae069`:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` **998 total, 992 passed, 0 failed, 6 skipped** (both), 0 warnings, lint self-test 39/39
  and 327 files clean, Dapper and EF guards, H1. **Image rebuilt from `2fae069`** (label checked; the gate's `607b31c` image kept as `campfire-dotnetway:p7gate` for the
  bench control): `reference-tools/front/pebble-image.sh` **PASSED** (one order at boot, 0600/0700, 2-certificate chain, the restart loads the cache, still one order);
  `bin/asset-check` **1,806 checks, 0 failed**; `bin/cookie-roundtrip` **20/20**; `bin/pixel-parity` (sequential, `bin/clean-parity` after each): **TLS lean**
  `--candidate-tls --only 'auth/sign_in,rooms/show/designers'` default **28/28**; `--fast --only 'users/**,account/**,auth/**,errors/**,rooms/show/designers'` every seed
  **108/108** (default 84, crowd 4, custom_styles 10, first_run 8, restricted 2); `realtime/message_sent` `--fast` **2/2**: screenshots, aria and loose DOM equal on every cell.
- **Pixel evidence of the gate, checked:** its `report.json`s hold **940 distinct lean page cells** (default 860, crowd 24, custom_styles 32, first_run 16, restricted 8)
  and 28 TLS cells, every one equal on screenshot, aria and loose DOM. 16 more entries are non-page responses (JSON, fragments, manifest, service worker) with only
  the server layer; 4 of them differ there (`messages/{index,show}/fragment`, `rooms/refresh/fragment`, `pwa/manifest/custom_styles`) exactly as in Phase 5's and Phase 6's
  evidence (class A, ungated).
- **Speed (`bench/quick`, post, sidebar, search and avatar, c=1 and 16, 3 reps interleaved new/ctl/ctl/new/new/ctl in two parts each; new `2fae069` against `p7gate`
  = the gate's `607b31c`, which isolates the security review's log filter):** host probe 1.027-1.074 before runs, two windows above 1.10 after, Mac load 1.3-5.9;
  48 runs, all 2xx, 0 connection errors, decoded bytes and post rows as stored. Medians new / ctl: post **2,119 / 2,144** (c16, -1.2%, CPU 1,532 / 1,503 us), **1,076 /
  1,090** (c1, -1.3%); sidebar 7,380 / 7,416, 2,215 / 2,236; search 7,130 / 7,161, 1,929 / 1,906; avatar 104,013 / 106,064 (c16), 12,248 / 12,658 (c1). Within noise
  (post does not carry a query, so the filter does not run on it). **vs Rails (stored medians): post 6.42x (c16) / 6.45x (c1), sidebar 12.3x / 7.73x, search 17.5x /
  10.0x: no cell under 5x.** The gate's table stands for the whole phase: reads -2% to -4% at c=16 and -11% to -14% at c=1 against Phase 6's close, post +5% / -4%; the
  c=1 cost is the per-request log line (CPU per request +48 us on a static file, +69 us on an avatar hit at c=1: more than S5's +14..+38 us, as 7.1 found).
- **Static rows against Rails (reported, no floor), from the gate:** avatar 12,725 / 103,863 req/s against Rails' 12,838 / 69,481 (0.99x c=1, 1.49x c=16);
  `static_css` 12,051 / 91,955 against 15,167 / 98,776 (**0.79x and 0.93x: below Rails**); `up` 11,577 / 78,958 against 2,101 / 4,229. Phase 6 left "front cache for
  `/assets`" and `static_css` (level with Rails at c=16) to Phase 7; the design note did not take the item up and the log line made it worse. Not a gate failure (the
  floor is the five workloads), but **not closed**: carried to Phase 8 below.
- **KLOC (`bin/kloc` on `2fae069`):** 27,406 by upstream's rules, **27,861 corrected (27.9 KLOC)**, App 13,038; unchanged by the fix (comments). Phase 7 is -2.11 corrected
  from 29.97 against the note's -2.355 (27.6, range 27.5-27.75): **0.11 over the range's top**. The front layer in the App is about 0.92 KLOC by `wc` (`Acme.fs` 392,
  `Host.fs` 199, `FrontSettings.fs` 123, `HttpLog.fs` 84, `OutputCaching.fs` 66, `OpenFiles.fs` 58) against the note's 0.53: `Acme.fs` is twice its estimate, `Host.fs`
  carries F6 by hand, and R1 added 27. Under Rust's 31.0.
- **Known differences, checked:** README "Front server" holds every class B line of note section 8 plus the departures (421, STD3, handshake budget, HSTS on a 500, the
  filtered query), each naming its test (HS1-HS12, AC4/AC6/AC11, OC1-OC9, LG1-LG4, CZ1-CZ3, SR1-SR8), and the class A line; no class A line for HSTS (it survives).
  The operator step (Let's Encrypt staging) is in AGENTS.md and the note, human-gated, **not run**.
- **Open items of earlier units, checked:** the `ObtainingCount` flake is gone with `Campfire.Kit.Tests`; Phase 6's "refresh the bench's byte sizes" was done in 7.1
  (`c61a029`; the gate's and this bench's byte checks pass); 7.4b's "a real order through the app's own host" is `pebble-image.sh` (passed here); the checkpoint's
  `offline: false` certificate context (OCSP or AIA fetches in production) is still exercised only by the operator's staging run.
- **Open, none blocking:** (1) static files and avatar hits are at or below Rails at c=1 (above); (2) the operator's Let's Encrypt staging run; (3) the gate's one
  undiagnosed hang of the `rooms/**` slice (harness, rerun clean); (4) `bench/lib/layers.py` and `perf_layers.py` still bucket frames by `Campfire.Kit` names (stale
  since Phases 2 and 7; the new front's frames fall under "Kestrel and ASP.NET Core HTTP" and the logger rows); (5) stale images in colima, not removed by this
  verifier (its rules allow only `bin/clean-docker`): `campfire-dotnetway:p7gate` (made here), `p7pre`, `p6ctl`, `campfire-candidate-p6ctl`, `p5ctl`,
  `control-8abdb6b`, `before-net11`, and the Pebble image; (6) carried from Phases 4-6: Firefox empty-404 pair, WebKit WebP avatar flake, the 16 KB Cable bound,
  `Html5Tests` L3's load-sensitive 50 ms bound, Q1 and F6 of Phase 5.
- **Phase 8 GO.** Conditions: keep H1, the markup lint, the Dapper guard and Phase 6's asset conditions green; Phase 8 owns logging and redaction, so (a) it takes up
  the request log's c=1 cost and the static rows (risk 1's ladder: the console logger's queue and formatter settings, then the interceptor; measure `avatar`,
  `static_css` and `up` at c=1 against a same-session control and record whether `static_css` gets back to Rails' level or why not), and (b) if it moves logging to
  `IOptions` or the `Redactor` API, keep the request line's Rails-filtered query (SR2) and the forging guards (SR1, SR3); `FrontSettings` moving to `IOptions` must keep
  Thruster's names, prefix and Go parsing (HS12) and the boot validation of `TLS_DOMAIN`; any change to `Web/Host.fs`, `Acme.fs` or `OutputCaching.fs` re-runs HS2/HS4
  under `bin/linux-tests App`, AC11 and `pebble-image.sh`; the operator's staging run before a release that touches them. Remove the stale control images when the
  orchestrator allows.

### Phase 8: design (2026-10-27)

Opus designer. `plans/design-jobs.md` written: decisions J1-J15, spikes S1-S9, units 8.1-8.9 with the checkpoint after 8.4. No code change. `bin/kloc`
27,406 / **27,861 corrected (27.9)** at `14a334a` (as at `2fae069`). The spikes ran in a scratch project under the session scratchpad on the Mac (.NET 11
RC2), with copies of the repo's opengraph scanner and `RubyUri.fs`. It was removed after. Nothing ran in colima.
- **Packages (NuGet, 2026-10-27):** the dotnet/extensions 10.11.0 line (Http.Resilience with Polly 8.4.2, Compliance.Redaction, TimeProvider.Testing,
  Diagnostics.Testing); `Lib.Net.Http.WebPush` 3.3.1 (MIT, 2.6 M downloads, repo active January 2026). No vulnerability or deprecation reported by
  `dotnet list package --vulnerable/--deprecated --include-transitive`.
- **Web Push:** `WebPush` 1.0.13 (the plan's pick) speaks only the pre-RFC `aesgcm` draft (`Encryption`/`Crypto-Key`/`WebPush` headers), not RFC 8291.
  `Lib.Net.Http.WebPush` 3.3.1 sends `aes128gcm` with `vapid t=,k=`. Its body, decrypted with the gem vector's receiver key, is the message. It takes the
  factory's `HttpClient`. Chosen (J7).
- **User agent:** MyCSharp.HttpUserAgentParser 3.1.6 agrees with the gem on 146 of 385 Rails vectors. It differs on Playwright's Chromium
  (`HeadlessChrome`, Safari to the gem) and on `apple_messages` (blocked to the gem). Both are class C on gate states, so the gem port stays (J9).
- **QR:** QRCoder 1.8.1 matches rqrcode's version on 38 of 39 vectors and its modules on 10 of 39 (other masks). The rqrcode port stays (J8).
- **Resilience:** `AddStandardResilienceHandler` POSTed 4 times to a 500. `DisableForUnsafeHttpMethods` makes that once, but GETs still retry 3 times.
  So: custom pipelines with timeouts only, plus the unfurl limiter; no retry, no breaker (J2).
- **Opengraph on AngleSharp:** the same attributes as the libxml2 scanner on 55 of 55 recorded bodies. 13 malformed-page probes differ (class B).
  Uncapped, a 5 MB nested-`div` page ran over 150 s of CPU, which is class C. Capped at depth 256 and 100,000 elements, every hostile page took
  0.05-0.35 s. A typical page costs 8 ms against 0.8 ms (J5).
- **URIs:** `System.Uri` behind Phase 5's RFC 3986 check gives the same outcome as `RubyUri` on all 172 recorded URLs except `mailto:foo` (Rails 500,
  ours 204: class B) (J6).
- **Clocks:** `FakeTimeProvider` stops `PeriodicTimer` (0 ticks in 300 ms against 15), so the harness clock is a `GetUtcNow`-only subclass and
  `FakeTimeProvider` stays in tests (J10).
- **Redaction:** the HMAC redactor costs 0.33 us. It covers the bot key in request-log paths and webhook URLs in job failures (J11).
- **Carried from Phase 7:** the request log's c=1 cost and the static rows are unit 8.6's ladder (J12). Phase 7 condition (b) is in J11/J13. The
  Host/ACME re-runs (HS2/HS4 on Linux, AC11, `pebble-image.sh`) are in 8.7's acceptance.
- **Projection:** 27.9 -> about 26.2 KLOC (range 26.0-26.5). The plan's -3.0 loses the 1.27 KLOC of the user-agent and QR ports, which stay.
  Five workloads: no per-request change on reads. Posting keeps the same channels. 8.2, 8.5 and 8.7 bench against `p8ctl` (Phase 7's close, `2fae069`).
- **Questions for Chris:** none.

### Phase 8, unit 8.1 Time (2026-10-27)

Sonnet builder, `plans/design-jobs.md` J10. `bin/kloc` 27,379 / **27,833 corrected (27.8)**, from 27,406 / 27,861 at `14a334a` (-28).
- **Code:** `Platform/HarnessClock.fs` (`FrozenTimeProvider`, which overrides `GetUtcNow` only; `HarnessClock.fromValue` / `fromEnvironment` for
  `CAMPFIRE_FROZEN_TIME`). `AppState.Time` replaces `AppState.Clock`. `RailsCompat/Clock.fs` (`Clock`, `SharedClock`, `SystemClock`, `TestClock`) and
  `ClockTimeProvider` are deleted. `Boot`, `AppRichText`, `Presenter`, `Controllers/Messages.fs`, `WebPush` (the VAPID `exp`) and `WebPushPool`'s shutdown
  wait (a timestamp and `Task.Delay(.., time)`) read the provider; `Channels/Identity.fs` only opened the module. `Network` carries a `Time`
  (`Network.system time`), which the opengraph, webhook and push deadlines (`new CancellationTokenSource(deadline, net.Time)`) take; `JobRunner.start`
  takes it for `Runner.Shutdown`'s wait. Two system-clock uses stay: `Respond.fs`'s `Date` header and the `Stopwatch` request duration.
- **Guard:** `bin/verify` refuses `DateTime(Offset).(Utc)Now` in `src` outside `Respond.fs`'s `Date` line. A planted `System.DateTime.UtcNow` in
  `Platform/OpenFiles.fs` failed it (rc 1, naming the file and line); the file was restored.
- **Tests:** `Microsoft.Extensions.TimeProvider.Testing` and `Diagnostics.Testing` 10.11.0. `TestClock` is `FakeTimeProvider` in the Db, App and Storage
  tests (`ChannelsSupport`, `IntegrationsSupport`, `TestDb`, `SeedTests`, `EfTests`'s and Storage's fixed-time subclasses); `Support.bootSeededFrozen` takes
  a provider (`FrozenTimeProvider` for the heartbeat and A5 tests). `CapturingLoggers` is a `FakeLoggerProvider` over a `FakeLogCollector` with a `Text`
  helper over the snapshot. `TimeTests.fs` has TM1-TM4; TM5 is `CableHostTests`' heartbeat under the frozen harness clock (it already existed and passes).
  The two `TestClock` facts in `RailsCompat.Tests` went with the type.
- **Differences recorded (test-only):** `FakeTimeProvider.SetUtcNow` cannot go back and does not follow real time, unlike `TestClock`. So `TestDb` starts at
  the real time (the fixtures date rows relative to now, and `db-differential` compares the shape of the dates), `TestDb(start = ...)` starts a test that
  sets an earlier date there (P1, the timestamps test, and one `SeedTests` fact), the timestamps test's second date is a day later, and the differential
  scenario sets `AutoAdvanceAmount` 1 ms (the real clock moved between its searches, which are kept by recency). No app behavior changed.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green (build with warnings as errors; 1,000 tests, 994 passed, 6 skipped as before: THP, AC11, three Rails-db
  differentials that need `bin/db-differential`, one Cable). Image rebuilt at `6da5ef7` (label checked), `p8ctl` tagged from the `2fae069` image first.
  `bin/pixel-parity --fast --only 'rooms/show/designers,messages/**,users/profile/**,auth/**'`: default 96/96, custom_styles 4/4, first_run 8/8,
  crowd and restricted 0/0 (no matching states), every screenshot, aria and loose layer equal, 368 s. `bin/cookie-roundtrip` 20/20 (dotnetway).
  `bin/db-differential` rc 0 (fixtures, scenario, schema identity, reference on our database). No bench (not asked of this unit). Disk 27 GB free.

### Phase 8, unit 8.2 Jobs (2026-10-27)

Sonnet builder, `plans/design-jobs.md` J1. `bin/kloc` 27,400 / **27,854 corrected (27.9)**, from 27,833 after 8.1 (+21: `PushDeliveries.fs` is 148 lines against
`WebPushPool.fs`'s 128 because it now owns the reader loops and the stop sequence that `Task.Run` and the semaphores hid, and the new tests are not counted;
the design's -70 for this unit did not materialise, the net is made up in 8.7/8.9).
- **Code:** `JobRunner<'App>` is a `BackgroundService` (generic because `Jobs.fs` compiles before `AppState`): `ExecuteAsync` starts `JOB_CONCURRENCY` readers
  per kind on the pool; `StopAsync` closes the queues, then `base.StopAsync` waits for the drain within the host's grace (`HostOptions.ShutdownTimeout`,
  10 s) and logs "jobs still running at shutdown were abandoned" if it ends first. The readers do not take the stopping token on purpose, so what is queued
  drains. `PushDeliveries` (`Integrations/PushDeliveries.fs`, replacing `WebPushPool.fs`) is a `BackgroundService` over a bounded 10,000 channel with 50
  readers (`TryWrite`: a full queue drops and logs, first drop and then the count) and an unbounded channel with 1 reader for invalidations; its handler is
  now `int64 -> Task<Result<unit, string>>` (no `GetAwaiter().GetResult()`). `StopAsync` closes deliveries, waits up to a second on the provider (`wait_for_termination(1)`),
  then closes invalidations (a 410 that comes back in that second is still destroyed), then waits within the grace. `Booted.Jobs`, `Runner`, `JobRunner.start`,
  the `Thread`, `BlockingCollection` and both semaphores are gone; `Boot.drain` is `closeDatabase`; the push job's `Task.Run` is gone. `Registry<AppState>`
  is a DI singleton. `Booted.Workers` registers the services (deliveries first, jobs second, so the stop order is web host, jobs, deliveries, then the
  database after `RunAsync`); only the host that serves the app gets it (`serveHosted` and `Support`'s main host), because stopping a runner closes the
  queues for good and a second host over the same state must not. `Boot.build` (8.7) will register them in the one builder.
- **Tests:** `JobsTests` JB1-JB5 (the old facts ported one for one onto `started`/`stopped`, which do what the host does; JB2's log is also checked at Error
  level on the collector), JB6 in `RevocationTests` (after `ChannelsSupport`): `Event.DisconnectUser` on a `Jobs` with no runner disconnects a connected
  client at once, and no queue holds anything. The pool-limit facts on `PushDeliveries`: 50 deliveries are running (`Running`, polled), then 10,000 queued
  and 5 dropped (`Pending` 10,050), the gate released, drained at stop; a panicking delivery frees its place; invalidations destroy in order
  (the 410 test). The pool facts first fill 50 readers and then the queue because readers drain the channel concurrently, unlike the old semaphore count.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green: 1,002 tests, 996 passed, 6 skipped (the environmental six), 0 warnings; H1, the markup lint, the Dapper guard and
  the clock guard in it. P12 and HS11 pass. `p8ctl` was lost at the end of 8.1; it is `campfire-dotnetway:app` as built at `6da5ef7` (8.1's code, label
  checked, `8e98d39` added only tests and docs) re-tagged before the rebuild, so it is Phase 8's state before 8.2 rather than `2fae069`. Image rebuilt at `b6fac7c`
  (label checked), `bin/clean-docker` run. `bin/pixel-parity --fast --only 'bot_api/**'`: default 4/4 on the server layer (fragment states have no screenshot,
  so the report counts 0/0 screenshots; `report.json` lists the four as pass), the other four seeds have no such states, PASSED. `--only realtime/message_sent`
  alone: default 2/2 screenshots, aria and loose equal, PASSED, `bin/clean-parity` run. `bench/lib/logcount 20000`: sent 20,000 (+1 probe), logged 20,001 lines
  after SIGTERM, OK (the SIGTERM run now goes through the host's hosted-service stop).
- **Bench** (`bench/quick --reps 3 --apps fsharp,dotnetway --routes post_message`, `FSHARP_IMAGE=campfire-dotnetway:p8ctl` as control, interleaved, host probe
  1.045-1.069, `bench/results/quick-82`; 12 runs, all 2xx, 0 errors, post rows equal the 2xx answers): medians new / control req/s: post c=1 1,118 [1,055-1,118]
  / 1,124 [1,054-1,128]; post c=16 2,150 [2,148-2,161] / 2,164 [2,144-2,167]; p50 0.79 / 0.79 ms and 6.00 / 5.92 ms. CPU per request: c=1 1,749 [1,740-1,829]
  against 1,670 [1,660-1,722] (+4.7%, above the control's range, though the throughput is inside it), c=16 1,504 against 1,487 (+1.2%, inside). **vs Rails** (stored
  medians): post c=1 **6.70x**, c=16 **6.51x**; no cell under 5x, no speed fix made. The c=1 CPU gap is the one thing to watch in the phase gate's bench.
- **Differences recorded:** none for users. Internally, a delivery that the old pool would have held in its 10,050 slots is now held in 10,000 plus the 50 the
  readers have in hand, the same count in total once the readers are busy; before they have started, 10,000.

### Phase 8, unit 8.3 Clients, guard, webhook (2026-10-27)

Sonnet builder, `plans/design-jobs.md` J2-J4, J6. `bin/kloc` 27,577 / **28,031 corrected (28.0)**, from 27,854 after 8.2 (+177: `Clients.fs` is 188 lines and the
guard's `connect` adds about 30, while `Http.fs`, `Net.fs` and `RubyUri.fs` (1,223 lines) stay until opengraph and push leave them in 8.4 and 8.5; the webhook grew
234 -> 245 lines (the URL check and the transport mapping replace `Http.exchange`) and `Guard.fs` is 388 -> 408 (`connect` and the key, against the `UInt128` arithmetic that went).

- **Code:** `Integrations/Clients.fs` (after `Http.fs`, before `Webhook.fs`) registers the three named clients: `"opengraph"` (guarded, connect timeout and pipeline
  timeout 5 s, one connection per request, concurrency limiter 16 with a 1,024 queue), `"push"` (guarded, 10 s, pooled) and `"webhook"` (unguarded, 7 s, one connection
  per request). Each is a `SocketsHttpHandler` with `AllowAutoRedirect = false`, `UseProxy = false`, `UseCookies = false`, gzip and deflate on, `ConnectTimeout`, and
  the client's own timeout infinite; `AddResilienceHandler(name, ...)` holds a timeout (and the opengraph limiter) and nothing else (no retry, breaker or hedging).
  `opengraph` and `webhook` carry `Accept: */*` and `User-Agent: Ruby`. `Microsoft.Extensions.Http.Resilience` 10.11.0 (Polly 8.4.2) is in
  `Directory.Packages.props`. Also in `Clients`: `Pinned` (the `campfire.pinned` request option, defined in `Guard.fs`), `pin`, `readLimited` (the body after
  decompression, `ValueNone` just past the limit), `contentType` (the raw header under the port's rule) and `TransportError`/`classify` (J4: Polly's
  `TimeoutRejectedException` is `ReadTimeout`, a connect timeout `OpenTimeout`, `ConnectionError` over a refused socket `Refused`, name resolution `Unresolvable`,
  TLS `Tls`, the rest `Io`). `Guard.fs`: the four address lists are `IPNetwork` values tested with `IPNetwork.Contains`, the embedded IPv4 of NAT64 and
  IPv4-translatable answers is read from the last four bytes; `Guard.connect resolver dial` is the `ConnectCallback` (a pinned address is checked by `blockedAddress`
  again, an unpinned host goes through `resolve`, a refusal is an `HttpRequestException` with the guard's message). `Webhook.fs`: `deliver factory time url payload`
  on the webhook client with `ByteArrayContent` (`Content-Type: application/json`, no charset), `System.Uri` behind `Attachables.tryAbsoluteUri` (Rails' wording
  for a bad URL kept; `invalid port` is gone, `System.Uri` refuses such a URL as a bad one), `WebhookError.Http` is `Transport of TransportError`. `AppState.Http`
  is the factory, built in `Boot` from a provider of its own (`TimeProvider`, logging, `Clients.add`) as the fragment cache is until 8.7. The resolver and the dial are
  DI singletons registered with `TryAdd`, so a test registers its own first; `IDialer` stays an interface for `Http.fs`'s users until 8.4/8.5. Opengraph and push
  still use `Http.fs` and `Network` (unit 8.4 and 8.5); no request of theirs goes through the new clients yet, so the guarded clients are exercised by GD1-GD4 and CL2-CL4.
- **Tests:** `ClientsTests.fs`: CL1 (a webhook POST reaches the server once on a 500, on a reset (`accepted = 1` after 500 ms) and on a timeout (real 7 s, the reply
  "Failed to respond within 7 seconds")); CL2 (a 302 is returned as is by all three clients) and CL3; CL4; CL5 (`readLimited` stops a gigabyte gzip bomb in under 10 s
  and inflates a 3 MB body); a `contentType` fact; GD1 (a host resolving to 127.0.0.1 is refused at connect, the dial never called, the server sees nothing); GD2 (pinned
  10.0.0.1 refused, a pinned public address connects there); GD3 (the lists have 16, 11, 36 and 2 networks; for every network the first and last address by the old
  `UInt128` arithmetic are inside, their neighbours outside, and every disallowed network's ends are blocked); GD4 (see below). `GuardTests` is unchanged and green, one for
  one. `WebhookTests`: the 19 recorded cases (`webhook_cases.json`/`webhook_expected.json`) compare status and reply, and the request on method, target, `Content-Type`,
  `Accept`, `User-Agent` and body (all equal); the other facts moved onto the factory. **CL3 is weak by construction**: .NET never proxies loopback, and a test that
  set the proxy variables and flipped `UseProxy` to true still passed, so CL3 also has a fact on the handlers' settings (`UseProxy`, `AllowAutoRedirect`, `UseCookies`,
  decompression and connect timeout of every handler), which is what pins it.
- **GD4 as written conflicts with the guard tests**: "an IPv4-mapped IPv6 answer is classified as its IPv4 address" cannot hold for `::ffff:8.8.8.8`, which surfguard
  (and the existing `GuardTests`) block outright as the whole of `::ffff:0:0/96`. GD4 asserts what both statements share: a mapped private address is blocked, a mapped
  public one is too (the existing behaviour), and the resolver drops it from an answer list. Record this if the design note is revised.
- **Gates:** `bin/verify` builds with warnings as errors and 0 warnings; the guards, lint, H1 and the clock guard passed in every run. **The tests are green serially,
  not in `bin/verify`'s parallel run on this machine today**: with a second project running on the host (load 6-50) one wall-time test in `Campfire.RichText.Tests`
  failed in each of four `bin/verify` runs, a different one each time (`ContentTests` "wide bodies cost linear time", `Html5Tests` L3 "160,000 ignored end tags took
  64 ms, limit 50 ms", `AutolinkTests` "wide bodies..."), none of them touching code this unit changed (the project does not reference the App); RichText alone passes
  (115 of 115), and `dotnet test --solution CampfireFs.slnx -c Release --no-build --max-parallel-test-modules 1` with `CAMPFIRE_REQUIRE_SEED=1` passed: **1,015 tests,
  1,009 passed, 6 skipped (the environmental six), 0 failed**, 1m 49s. Image rebuilt at `6fb2608` (label checked), `bin/clean-docker`. `bin/pixel-parity --fast --only
  'bot_api/**'`: the default seed's four fragment states (`messages/index`, `index/before`, `update`, `boosts/create`) pass on the server layer, the other four seeds have
  no such state, PASSED (the report counts 0/0 screenshots, as in 8.2); `bin/clean-parity` run. Free disk 20-25 GB throughout.
- **Bench:** none. The webhook and the guard are on no benchmarked path (no workload posts to a bot with a webhook; the push subscriptions the bench uses are refused
  before any request), and the client factory is built once at boot. The post rows of 8.2 stand as the latest numbers; 8.5 and 8.7 bench against `p8ctl`.
- **Differences recorded** (README, Known differences, the webhook bullet; the full Integrations entry is written by 8.9): the request's header order, `Host`,
  `Connection` and `Accept-Encoding` are .NET's (class B); the target is `System.Uri`'s normalization (class A); a trickling reply is cut at the 60 s deadline, the 7 s
  timeout stands to the response head (class B); a relative URL says "not an HTTP URI" as Ruby does, one `System.Uri` refuses says "bad URI".

### Phase 8, unit 8.4 Opengraph (2026-10-27)

Sonnet builder, `plans/design-jobs.md` J3, J5, J6. `bin/kloc` 27,397 / **27,851 corrected (27.9)**, from 28,031 after 8.3 (-180: `OpengraphHtml.fs` (326) and
`OpengraphEntities.fs` (49) are deleted now, not in 8.9, since nothing uses them after this unit; `decode`, `metaEncoding` and the AngleSharp reader (about 110 lines) are
in `OpengraphDocument.fs`; `OpengraphFetch.fs` is 139 -> about 125).

- **Code:** `OpengraphFetch.fs` has `OpengraphEnv` (`IHttpClientFactory`, `IResolver`, `TimeProvider`, replacing `Network` for opengraph), `OpengraphUri` (`System.Uri` behind
  `Attachables.tryAbsoluteUri`; `host` is none for a URI without `//`, so `mailto:` looks nothing up) and Rails' loop by hand on the `"opengraph"` client: every request is pinned
  (`Clients.pin`), 3xx is never followed by the handler, each `Location` is parsed, required to be http(s) and resolved through the guard again (10 responses at most), the
  connect callback checks the pinned address once more. A transport failure is `FetchError.Transport of TransportError`. The body is read through `Clients.readLimited` (5 MB
  after inflation); a HEAD returns the raw `Content-Type`. `OpengraphLocation` and `OpengraphMetadata` take the env and `System.Uri` (`tweetUrl`, the fxtwitter rewrite through
  `UriBuilder`). `Opengraph.fs` no longer has its semaphore: the client's limiter (16, queue 1,024) is the bound, inside the unfurl's 10 s deadline. `OpengraphDocument.read`
  parses with `HtmlParser(IsScripting = false, OnCreated)`; the callback stops the parse (an exception it owns) when an element would be the 100,001st or the previous element,
  now in the tree, is deeper than 256 (void elements are never open); the `meta`s are then read from the partial document (`element.Owner`), so a page's head unfurls.
  `UnfurlLinks` builds the env from `app.Http`, `SystemResolver()` and `app.Time`.
- **Tests (`OpengraphTests`):** the 90 recorded cases one for one: response, lookups, and each request's method, `Host`, target, `Accept` and `User-Agent` (`Accept-Encoding`
  is .NET's and dropped from the comparison). **89 matched on the first run; `mailto raises` is the one change**: 204, no lookup, no request (`classBCases`, and the named fact
  "a mailto url unfurls nothing" for three mailto URLs). The existing facts (https with the test CA verifying the name, gzip bomb stopped in under 2 s, trickling page given up at
  the deadline) run on the client; `IntegrationsSupport.opengraphEnv` builds the env over DI (the fake resolver and dial, and the test CA on the client's handler). OG1: six 5 MB
  hostile pages (nested `div`, `span`, tables, misnested formatting, `<b><p>`, `<i>`) each report `Stopped`, created at most 100,000 elements and finished in under 10 s (the whole
  class ran in 3 s). OG2: a head then 300 nested `div`s unfurls the head. OG4: a `Location` to `10.0.0.1`, to a name resolving to it and to `127.0.0.1` each ends in 204 after one
  request, and nothing but the public address is dialed. OG3: the probes. The 5 MB pathological pages fact now expects the 130,000-meta page to find nothing (its title is last,
  past the element cap).
- **Differences recorded** (README, Known differences, the Integrations entry): the 90 cases and the request fields compared; `Accept-Encoding` is .NET's; **class B, malformed
  pages: the list is 14 probes, not the 13 the unit's row counts** (`&AMP;`, `&#128;`, `&#150;`, `&#xD800;`, `&#1114112;`, `&#;`, `&#x;` and a meta inside `textarea`, `title`,
  `template`, `<meta/property=.../content=...>`, an unterminated quoted value, a NUL, CR LF): S6 counted 6 reference probes, and the test has also had `&#x;`, which differs the
  same way. Both tests fail if a listed probe stops differing or an unlisted one starts. Class B, caps (256 deep, 100,000 elements). Class B, `mailto:` answers 204.
- **Gates:** `bin/verify` (`CAMPFIRE_REQUIRE_SEED=1`, Release, warnings as errors): 0 warnings, lint-markup (326 files, self-test ok), the guards, **1,020 tests, 1,014 passed, 6
  skipped (the environmental six: Pebble, THP and others), 0 failed**. The first run had one failure, `Campfire.RichText.Tests` "the_rest_of_a_body_is_not_read_once_it_is_too_deep"
  (343 ms against a 300 ms CPU bound) with the host at load 19; the project alone passed 115 of 115 and the second full `bin/verify` passed. No pixel run (no state shows a live
  unfurl; `messages/opengraph*` render stored embeds). No image rebuild, no bench: unfurls are on no benchmarked path and the client factory is unchanged.
- **Left for later units:** `Http.fs`, `Net.fs`'s `Network`/`Tls`, `RubyUri.fs` stay for Web Push until 8.5; 8.9 removes them and writes the full Integrations README entry.

### Phase 8, checkpoint after 8.4: Opus review (2026-10-27) - GO for 8.5, with notes

Opus, against `plans/design-jobs.md` J1-J15 and D1-D9, over `4c6e99f..3f14e11`. `bin/kloc` 27,406 / **27,860 corrected (27.9)** (+9 from 8.4: the restored unfurl bound).
- **The design note's checkpoint list holds:** no retry, breaker or hedging in any pipeline (CL1 counts 1 on a 500, a reset and a timeout); the guard is the
  `ConnectCallback` of both guarded clients and re-checks a pinned address (GD2); the opengraph loop is Rails' `fetch.rb` line for line (10 responses, any 3xx, each
  `Location` parsed, http(s) only, resolved and pinned again; OG4 and the recorded redirect cases); AngleSharp's caps bound OG1's six pages, and the 14 class B probes fail
  both ways; `FakeTimeProvider` is in no `src` code, and the frozen provider feeds wall-clock stamps only (timers, `CancellationTokenSource` and `Task.Delay` run on the
  system timer). `GuardTests.fs` is unchanged since the phase began. SSRF probes beyond the tests, by reading: decimal, octal and hex hosts are normalized by `System.Uri` or
  read by `inetAton` and refused; IPv4-mapped and -compatible IPv6 is blocked outright; a zone (`%`) is refused; a redirect to a non-http scheme is `RedirectDenied`.
  Extra hostile pages through `OpengraphDocument.read` (5 MB each): 580,000 attributes on one element 0.48 s CPU, one 5 MB attribute 36 ms, an unterminated comment 62 ms,
  100,000 metas 0.49 s (stopped), 100,000 options in a select 0.12 s (stopped). `dotnet list package --vulnerable --include-transitive` on the App: none. BouncyCastle
  comes only through Certes (Phase 7); Web Push has none.
- **Fixed (3 defects, each with a test that failed first):**
  - `aa2e748` **Unbounded concurrent unfurl bodies.** 8.4 replaced the module semaphore with the client's Polly limiter, which holds a permit only until a response
    head arrives, so nothing bounded the 5 MB body reads (and the decode and parse queued behind them) of concurrent unfurls, an endpoint any signed-in user reaches.
    The semaphore is back around the whole unfurl (16, waiting within the deadline); the client's limiter stays. Test "unfurls past the limit wait while bodies are read":
    20 trickling unfurls, at most 16 connections (20 without the bound).
  - `aa2e748` **Request URLs in logs.** `IHttpClientFactory`'s default handlers log each request URL at Information. Harmless today only because `Boot` builds the clients
    in a side provider with no log providers; 8.7 moves them into the host, where webhook URLs (a secret in the path, often) and push endpoints (capability URLs) would be
    printed. Every client now calls `RemoveAllLoggers()`. Test "no client logs a request's URL" (the path was in the log before). A webhook timeout's resilience event
    was checked by hand and carries no URL. Also: `Webhook.fs`'s header comment said each read has 7 s (it is the response head; README already had the class B line).
  - `4a21a1a` **`PushDeliveries` stop raced `ExecuteAsync` (8.2).** `BackgroundService.StartAsync` runs all of `ExecuteAsync` on the thread pool (.NET 10 on; checked: 0 of
    200 starts ran it on the caller), so `StopAsync` could read `deliveryReaders` before it was assigned: the invalidations closed at once (a 410 never destroyed), and
    readers that started after the stopping token was cancelled exited without delivering. `bin/verify` caught it once under load ("pushes messages and destroys expired
    subscriptions": 4 left, 2 expected). The readers now read without the token until the queue is closed and drained (as the job workers do) and `StopAsync` waits on a
    completion source they set. Test "a stop right after the start still delivers and invalidates" (20 rounds; the old code failed at round 2).
- **Gates I ran:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` at 3f14e11 green (1,020 / 1,014 passed / 6 skipped / 0 failed). With the fixes: run 2 failed only the RichText L3
  wall-clock flake ("160,000 ignored end tags took 74 ms, limit 50 ms", load 17); run 3 failed WebPushTests (the race above) and Cable `MemoryTests` L7 (20.4 KB, load 15);
  run 4, at the final code, **green: 0 warnings, lint 326 files and self-test, the Dapper, EF and clock guards, H1; 1,023 tests, 1,017 passed, 6 skipped, 0 failed**.
  Image rebuilt at `4a21a1a` (label checked), `bin/clean-docker`. `bin/pixel-parity --fast --only 'rooms/show/designers,bot_api/**,users/push_subscriptions/**'` on every seed
  PASSED (default 4/4 screenshots, aria and loose equal; the other seeds have no such states), and `--seeds default --only realtime/message_sent` PASSED (2/2); `bin/clean-parity`.
  No bench: the fixes are off the benchmarked paths (unfurls; the push pool's start and stop), and 8.5 benches post a message against `p8ctl`. Free disk 21-23 GB.
- **The units' gate claims check out** against their logs and this run (8.3's parallel-run flakiness is recorded as it happened; 8.4's verify passed on its second run).
- **8.7 should split now:** 8.7a the production path (`Boot.build`, `DatabaseStartup`, `AppState` from DI, the clients in the host's provider in place of `Boot`'s side
  provider, `Boot.serve`; the HS2/HS4, AC11, `pebble-image.sh` and bench gates), 8.7b `Support` (609 lines) on `Boot.build` and `Booted`/`bootWith*` deleted.

### Phase 8, unit 8.5 Web Push (2026-10-27)

Sonnet builder, `plans/design-jobs.md` J7. `bin/kloc` 27,417 / **27,860 corrected (27.9)**, from 27,851 after 8.4 (+9: the `PinHandler` and `Clients.withPin` (about 25 lines) and the
`PushEnv` record outweigh the 90 lines of header writing and `Http.exchange` plumbing that left `WebPush.fs`; `WebPushEncryption.fs` (154), `Http.fs` (683), `RubyUri.fs` and
`Net.fs`'s `Network` stay compiled until 8.9, which is where the cut shows).

- **Code:** `WebPush.deliver` and `deliverTestNotification` take a `PushEnv` (`IHttpClientFactory`, `IResolver`, `TimeProvider`) and build a `PushServiceClient` over the factory's
  `"push"` client with `AutoRetryAfter = false`, `PushMessage(Urgency = High, TimeToLive = 2419200)` and a per-delivery `VapidAuthentication` (`Subject`, 12 h `Expiration`, scheme
  `Vapid`) under a 30 s deadline on the provider's timer. `DeliveryError` lost `Tls` and `Http of HttpError` for `Transport of TransportError`; `PushServiceClientException`'s status
  maps as `verifyResponse` did (410, 404 destroy; 401, 403, 413, 429, 5xx and the rest log), `CryptographicException` is the key error (invalidates), `ArgumentException`,
  `FormatException` and the library's `InvalidOperationException` for a blank key are `ArgumentError`. `deliver` now answers `Ok true` (sent) or `Ok false` (not a permitted push
  host or a private address: nothing connected) because the library does not return the status. `Vapid.fs` is the pair check only (`VapidConfig.Create`/`FromConfig`, keys kept
  as unpadded Base64 for the library); `WebPushEncryption.fs` is not called (`WebPushEncoding.decode64` and `P256.parsePoint` are used by the pair check; 8.9 moves them).
  `PushDeliveries`, `IntegrationJobs.webPushPool` and `TestNotifications` take the env; `Boot` builds it from its side provider (until 8.7), which now also supplies the resolver.
  `Directory.Packages.props`/`Campfire.App.fsproj`: `Lib.Net.Http.WebPush` 3.3.1.
- **Opus checkpoint note 1, the pin: option 1 taken.** The library builds its own request, so `Clients.pin` cannot be set on it. `Clients.PinHandler` (a `DelegatingHandler`
  registered on the `"push"` client only) copies an `AsyncLocal` address onto `request.Options` and `Clients.withPin` sets and restores it around the call. The "delivers to the
  pinned address" test keeps its meaning (one lookup, one dial to the resolved address, the connect callback re-checks it): without the pin the callback would resolve a second time. The
  `AsyncLocal` flows only into the call's own continuations, and the guard at connect does not depend on it (an unpinned request is resolved and checked there). `MaxResponseContentBufferSize`
  is 16 KB on the push client, since the library reads an error body whole. README: the stale Web Push paragraph (it described `Http.fs`) is rewritten.
- **Tests (`WebPushTests`, 17):** WP1 the vector's receiver key decrypts the payload to the message with the 0x02 delimiter and record size 4096, and the longest payload fits; WP2
  `Content-Encoding: aes128gcm`, `TTL: 2419200`, `Urgency: high`, `Authorization: vapid t=..., k=<VAPID_PUBLIC_KEY>`, the JWT header segment equal to the gem's, claims `aud`
  `https://fcm.googleapis.com`, `sub` (the configured subject, and a second subject), `exp` within a window of now + 12 h (the library reads its own clock, so not the provider's), the
  signature verifying with the public key; WP3 410 and 404 destroy, 401, 403, 400, 413, 429, 500, 503 and 302 are logged and keep, each with one request; WP4 a 429 with `Retry-After: 1`
  reaches the server once (waited 1.5 s); WP5 six endpoints never connect (no dial, no request, lookups only for the two that need one); WP6 missing, malformed or mismatched keys give
  `None` from `webPushPool` and `VapidConfig` errors; WP7 the test notification delivers once and its payload is right (the redirect is the controller's, unchanged); WP8 the three pool
  facts (queue and 50 readers, panic frees a slot, stop right after start) on `PushEnv`. The four tests of `Encryption`/`Vapid.Authorization` went with the code they tested.
  `IntegrationsSupport.pushEnv` builds the env over DI with the test CA on the `"push"` client.
- **Differences recorded** (README, Known differences, the Web Push entry; class A on the wire, all decrypted and verified by the tests): record size 4096 against the gem's ciphertext length
  and the padding, the JWT's bytes, `Authorization: vapid t=..., k=...` with a space after the comma, the library's header spelling (`TTL`) and order, no `Accept` or `User-Agent: Ruby`; class
  B: a 400 "UnauthorizedRegistration" logs as `ResponseError` (the reason phrase is not kept), still not destroying; `exp` follows the library's clock.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green on the first run: 1,020 tests, 1,014 passed, 6 skipped (the environmental six), 0 failed, 0 warnings; H1, the markup lint, the Dapper guard in it.
  `dotnet list package --vulnerable --include-transitive`: none. Image rebuilt at `f181944` (label checked), `bin/clean-docker` run. `bin/pixel-parity --fast --only 'users/push_subscriptions/**'`:
  default 2/2 screenshots pixel-equal, aria and loose equal; crowd, custom_styles, first_run and restricted have no such state (0/0); PASSED in 35 s, `bin/clean-parity` run.
- **Bench** (`bench/quick --reps 3 --apps fsharp,dotnetway --routes post_message` inside colima, `FSHARP_IMAGE=campfire-dotnetway:p8ctl` as control, interleaved, host probe 1.043-1.098,
  `bench/results/quick-85`; 12 runs, all 2xx, 0 errors, post rows equal the 2xx answers): medians new / control req/s: post c=1 1,086 [1,082-1,098] / 1,087 [984-1,095]; post c=16 2,133
  [2,132-2,177] / 2,180 [1,952-2,181] (-2.2%, inside the control's range, whose first rep was the slow one); p50 0.82 / 0.82 ms and 6.01 / 5.89 ms; CPU per request c=1 1,768 / 1,691
  (+4.6%, as in 8.2's note: the same gap, not new), c=16 1,503 / 1,483 (+1.4%). **vs Rails** (stored medians): post c=1 **6.51x**, c=16 **6.46x**; no cell under 5x, no speed fix needed (the bench's
  subscriptions point at a refused host, so the library never encrypts on the benchmarked path). A first attempt was spoiled by a stray run I started on the Mac instead of in colima
  (it shared the container name and removed the real run's container when I stopped it); it was discarded and the run above is the second, clean one.
- **Left for later units:** 8.6 (redaction), 8.7a/b (production Boot, `IResolver` from DI in `UnfurlLinks`, the `StopAsync`-versus-`ExecuteAsync` check of `Heartbeat.fs` and `Acme.fs`), 8.8, 8.9 (delete
  `Http.fs`, `Net.fs`'s `Network`, `RubyUri.fs`, `WebPushEncryption.fs`; the class B tweet-host note). The checkpoint's test-flake item (RichText `CpuTimer`, Cable L7) is still open for before the phase
  gate; it did not fire in this unit's `bin/verify`.

### Phase 8, unit 8.6 Logging (2026-10-27)

Sonnet builder, `plans/design-jobs.md` J11 and J12. `bin/kloc` 27,503 / **27,946 corrected (27.9)**, from 27,860 after 8.5 (+86: `Logging.fs` 100 lines with its comments, the interceptor's bot branch, the tests are not counted).

- **Code:** `Logging.fs` (after `Config.fs`): `Logging.add services secrets` registers `AddRedaction` with the HMAC redactor for `DataClassification("campfire", "secret")` keyed with
  `KeyGenerator.GenerateKey("campfire log redaction", 64)` (base64, key id 1; the erasing redactor is the fallback); `Logging.forSecrets` builds the same redactor once per `Secrets`
  for code with no request services (jobs); `redactBotPath` replaces the key segment of `/rooms/:id/:bot_key/messages...` with its hash and `redactUrl` replaces a URL (as written and in
  the JSON quoting Ruby's URI error uses) in a message. `HttpLog.Interceptor` takes `IRedactorProvider`: on a bot API path it disables `RequestPath` and adds the redacted `Path`
  parameter, as R1 does for the query (a host without a redactor registered gets one with a random key from `HttpLog.add`). `PublicExceptions` logs `path=` through `redactBotPath`
  (`[FILTERED]` when no redactor is registered). `IntegrationJobs.deliverWebhook` redacts the webhook URL in the error it returns, so `job failed ... error=` carries the hash.
  `Jobs.fs` logs a dropped event and a missing handler by `JobKind.eventName` (the union case) and no longer prints the event's fields (`%A` could print a message body).
  `Microsoft.Extensions.Compliance.Redaction` 10.11.0 is in `Directory.Packages.props` and the App.
- **Opus checkpoint notes:** "no client logs a request's URL" stays green (`ClientsTests`, unchanged); the InvalidUrl text is the redaction target (RD3); the `%A` dropped-event line is
  covered by RD3's `eventName` check (no `Event` case is unhandled today, so the branch is not reachable from a test; the helper is what it calls).
- **Tests:** `RedactionTests` RD1-RD3, RD5, RD6 and `WebPushTests` RD4, over the `FakeLogCollector` (`CapturingLoggers`), filtered to what production writes (`Boot.createLoggerFactory`'s
  levels: the capture records every framework category at Trace, whose routing lines carry the raw path and are not shipped). RD1 a bot POST's request line has `Path: /rooms/N/<hash>/messages`
  and no key, the other fields (method, status, `remote_addr`) stay, and a page's path is unchanged; RD2 a throwing handler behind `PublicExceptions` logs `request failed ... path=` with the hash,
  and with no redactor registered with `[FILTERED]`; RD3 a webhook with `http://exa mple.com/hooks/<secret>?k=v` fails with the hash of the URL in the error and in the `job failed` line;
  RD4 a push that the service refuses (500) and one that cannot connect log the host and no part of the endpoint's path; RD5 twelve failed sign-ins (throttled), a join page and a join post
  log no e-mail address, password or join code; RD6 the hash is stable within an app, equal for a second app on the same `secret_key_base`, and different for another. `Support.fs` has
  `bootSeededRequestLog` (the production edge with `LogRequests` on, over the capture). SR1-SR8 and LG1-LG4 are unchanged and green.
- **README:** a class B line (Known differences) for the bot key and webhook URLs in the logs.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify`: the first run had 2 failures in `Campfire.RichText.Tests` (`ContentTests` "wide bodies cost linear time", `Html5Tests` L3), the checkpoint's
  known `CpuTimer` flake (process CPU time, parallel tests) with the host at load 53 from other sessions; the second run was green: 1,026 tests, 1,020 passed, 6 skipped (the environmental six),
  0 failed, 0 warnings; H1, the markup lint and the Dapper guard in it. Image rebuilt at `23eea9b` (label checked), `bin/clean-docker` run. `bench/lib/logcount 20000` (colima): **sent 20,000
  requests (+1 probe), logged 20,001 lines: OK**.
- **The c=1 ladder (J12), measured, nothing kept.** A throwaway build (`/var/tmp/campfire-ladder`, mounted over the image; the switches were environment variables, not committed) ran
  `bench/quick --mounted ... --routes avatar,static_css,up` (2 reps, c=1 and 16; host probe 1.05-1.10, `bench/results/quick-86-e0` ... `quick-86-e4`, `quick-86-q*`). req/s (median [range]) and
  CPU us/request at c=1:

  | Rung | avatar c=1 | static_css c=1 | up c=1 | static_css c=16 | CPU us avatar / static_css c=1 | kept |
  |---|---|---|---|---|---|---|
  | e0 as built (queue 2,500, microsecond timestamp, both fields) | 12,238 | 11,464 | 11,092 | 85,876 | 162 / 188 | - |
  | e1 `MaxQueueLength` 65,536 (`QueueFullMode` stays `Wait`: no line dropped) | 12,534 | 11,865 | 11,510 | 89,919 | 158 / 183 | **no** (below) |
  | e2 no timestamp (a bound: drops a field) | 12,821 | 12,112 | 11,542 | 94,419 | 155 / 182 | no: drops a field |
  | e3 no `remote_addr`/`user_agent` (a bound: drops two fields) | 11,427 | 11,084 | 10,601 | 84,194 | 169 / 192 | no: no gain, drops fields |
  | e4 `LOG_REQUESTS=false` (the whole line, a bound) | 20,226 | 18,340 | 17,415 | 107,924 | 109 / 125 | - |

  The queue rung looked like +3-4% in that run (CPU 162 to 158, 188 to 183) and I re-measured it paired (`static_css` only, 3 reps each, `q1`/`q0`/`q1b`/`q0b` alternating): the host turned noisy
  (load 40-50 from other sessions; probe ratios up to 1.65 in e0b; `q0` ranged 4,035-11,883 at c=1), the best reps were equal (queue 65,536: 11,728 and 12,077; default: 11,883 and 11,946) and the
  CPU medians do not separate (192, 214 against 241, 184). It does not pay reliably, so it is not kept. The timestamp costs about 6-7 us a request (4-5%) and the two fields nothing measurable
  (e3 is slower than e0 inside the noise), so no rung of the ladder changes the line. What the line costs is the framework's: the whole request line is **53-63 us of CPU a request** at c=1
  (avatar 162 to 109, `static_css` 188 to 125) and `static_css` reaches 18,340 req/s without it. A writer of our own is not on the ladder (J12), so the cost stays.
- **`static_css` against Rails (15,167 / 98,776 req/s), final image** (`bench/results/quick-86`, `bench/quick --reps 2 --apps fsharp,dotnetway` with `FSHARP_IMAGE=campfire-dotnetway:p8ctl` as the control, interleaved, all 2xx,
  0 errors, host probe 1.06-1.07): c=1 **11,868** [11,484-12,252] (0.78x Rails), c=16 **85,971** [81,635-90,307] (0.87x Rails). It does not get back to Rails' level: the request line costs
  about 60 us a request at c=1, which alone is the gap (15,167 req/s is 66 us a request, ours is 84 us with the line and 55 us without it; with the line off it is 18,340 and 107,924, above Rails). The static rows carry
  no Rails floor (plan section 4); they are the cost of a logged request through the host.
- **Bench, new / control (`p8ctl`)** req/s medians (c=1, c=16): avatar 12,896 / 11,211 and 100,350 / 94,024; `static_css` 11,868 / 10,682 and 85,971 / 81,564; `up` 11,540 / 10,116 and 76,280 / 75,736; room page 2,085 / 2,026 and 6,945 / 6,615;
  post 1,029 / 1,019 and 2,081 / 2,071 (the control's ranges are wide: the host was noisy). No cell is below the control's median, so the redaction (a path-prefix test per request, a hash only on a bot path or a failure line)
  costs nothing measurable on the room page and the post (risk 11). **vs Rails** (stored medians): room page 29.0x (c16) / 19.5x (c1), post **6.31x** (c16) / **6.17x** (c1): no cell under 5x.
- **Left for later units:** 8.7a/b (production Boot, `IResolver` from DI in `UnfurlLinks`, the `StopAsync`-versus-`ExecuteAsync` check of `Heartbeat.fs` and `Acme.fs`), 8.8, 8.9 (delete `Http.fs`, `Net.fs`'s `Network`,
  `RubyUri.fs`, `WebPushEncryption.fs`; the class B tweet-host note, with the probe count 14). The checkpoint's test-flake item (RichText `CpuTimer`, Cable L7) is still open for before the phase gate and fired twice in this
  unit's first `bin/verify` (L3 and the "wide bodies" timing test): fix it (non-parallel collection or thread CPU time) before 8.9. The Moved-to-Logging console options (`Boot.configureConsole`) stay in `Boot.fs` until 8.7a.

### Phase 8, unit 8.7a Builder, production path (2026-10-27)

Sonnet builder, `plans/design-jobs.md` J14, split per the checkpoint: 8.7a is the production path, 8.7b moves `Support` and deletes `Booted`. `bin/kloc` 27,664 / **28,107 corrected (28.1)**, from
27,860 after 8.5 (the 8.6 entry has no figure; +247 over 8.5 is `Boot.fs` 378 -> about 520 with the shim kept, `BootInputs`, `DatabaseStartup` and `Host.configure`/`mapHttpPort`, which 8.7b and 8.9 take back).

- **Code:** `Boot.build loggers configure overrides` is one `WebApplication.CreateSlimBuilder`: `configure` runs first (an in-memory provider goes here), then `AppConfig`, `FrontSettings` and
  `CAMPFIRE_FROZEN_TIME` are read from `builder.Configuration` (`AppConfig.fromLookup`, `FrontSettings.fromLookup`, `HarnessClock.fromValue`; a bad value throws before any listener exists and
  `Boot.execute` prints `Error: <message>`, exit 1, as before), then `DatabaseStartup` is registered first and `addServices` and `addWorkers` after it, then `overrides`, then `Host.configure`
  (shutdown, ACME, forwarded headers, static assets, Kestrel) and `Host.mapHttpPort`. `addServices` registers `AppConfig`, `Secrets`, `TimeProvider`, the pooled `IDbContextFactory`, the scoped
  `CampfireDb`, `WriteGate`, `Jobs`, `Ef.Env`, `Storage`, `Registry<AppState>`, the one `HybridCache` (`Fragments.add`, on the host's collection: no side `ServiceCollection`; its default entry options
  carry `DisableDistributedCache`, so the session's `IDistributedCache` never becomes an L2), the clients (`Clients.add`: in the host's provider now, disposed with it, where `Boot`'s side provider never
  was), `PushDeliveries` (only with a valid VAPID pair; `IntegrationJobs.vapidOrOff` logs the off case once at build), `AppState` (a singleton whose factory resolves the others), `Web.Services.add` (no longer
  takes `AppState`) and `CableServices.add` (`CableLink` takes the handle from the provider). `DatabaseStartup : IHostedLifecycleService` runs `StartingAsync` = `StoragePaths.createDirs`, `db:prepare`
  through EF and `Membership.disconnectAll`: every hosted service's `StartingAsync` runs before any `StartAsync`, and Kestrel's `GenericWebHostService` is added last, so the database is ready before
  anything starts and before a port binds. `Boot.serve loggers` is `build`, `RunAsync`, then `closeDatabaseAt` (a checkpoint on a connection of its own, since the host and its pooled contexts are
  disposed when `RunAsync` returns), also after a failed start. `Host.build` stays for the HS tests and `bench/front` and is `configure` and `mapHttpPort` around the caller's registrations.
  `UnfurlLinks` takes `IResolver` from the request's services. **`StopAsync` against `ExecuteAsync` (the checkpoint's thread-pool note): checked, nothing to change.** `CableHeartbeat` and `AcmeService` have
  no `StopAsync` override and read no state `ExecuteAsync` assigns (`Acme`'s `context` is used only inside it); `JobRunner` and `PushDeliveries` already use a completion source.
- **The shim (the split rule):** `Booted`, `bootEdge`/`bootWith*` stay for `Support`, now over the same registrations: a private provider over `addServices` (plus `AddLogging`), `DatabaseStartup.Prepare`
  run by hand, and `ConfigureServices` = `addServices` on the test host followed by the root's `AppState`, `CableHandle`, `Jobs`, `WriteGate`, factory, `Storage`, `Env`, `Registry`, `HybridCache` and
  `PushDeliveries` registered as instances (last wins), so `test.App` and the host serve one app. `Booted.Services` is the private provider; `Test` disposes it after `closeDatabase`. 8.7b deletes all of
  it and moves `Support` (609 lines, with `bootSeeded*`, `startPipelineWith` and the dozens of tests that take `test.Booted.ConfigureServices`) onto `Boot.build`'s `configure` and `overrides`.
- **Tests:** `BuildTests.fs` BB1-BB3 build the real host over an in-memory configuration on free ports. BB1: a probe hosted service registered after `DatabaseStartup` sees the database file when it
  starts and nothing listening on `HTTP_PORT`; after the start `/up` answers 200 on `HTTP_PORT` and on `TARGET_PORT`. BB2: `AppState`, the client factory, `HybridCache`, the cable server, the gate and `Jobs`
  are the host's singletons (`Assert.Same`), and Web Push is off without keys. BB3: a missing secret, `JOB_CONCURRENCY=two` and `CAMPFIRE_FROZEN_TIME=yesterday` each fail the build with Rails' and
  the port's messages. HS1-HS12 are unchanged (they run `Host.build`).
- **Test flakes (checkpoint item 4):** `Campfire.RichText.Tests`' `CpuTimer` now reads the calling thread's CPU clock (`clock_gettime(CLOCK_THREAD_CPUTIME_ID)`: 16 on macOS, 3 on Linux; the process's CPU
  time where neither exists), so tiered compilation, the collector's background threads and anything else in the process cannot spend a bound; the assembly was already non-parallel, which is why that
  was not the cause. A new fact proves it (a sleeping thread shows under 100 ms while another burns CPU, a busy loop shows its own). Cable's L7 already sits in a `DisableParallelization` collection
  (`Memory`); it did not fail in this unit, and nothing was changed there: if it fires again under load it is the GC heap measurement, not parallelism.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green: **1,030 tests, 1,024 passed, 6 skipped (the environmental six), 0 failed**, 0 warnings, lint-markup (327 files), the guards, H1.
  **`bin/linux-tests App`: 418 tests, 417 passed, 1 skipped (AC11 without Pebble), including HS2, HS4 and the THP test; AC11 with the Pebble recipe: 1 of 1 passed.** Image rebuilt at `7477bfa` (label
  checked by `asset-check`), `bin/clean-docker` run. `reference-tools/front/pebble-image.sh`: PASSED (one order at boot, cache files 0600/0700, a 2-certificate chain, the restart loads the cache and
  makes no second order). On the image (`campfire-candidate-dotnetway`, seed default): `/up` 200 on `HTTP_PORT` and, from inside the container, on `TARGET_PORT` 3000; `POST /session` 302 to `/`; the room page 200;
  `docker stop` logs `shutting down` then `Server stopped`. `bin/asset-check`: 1,806 checks, 0 failed. `bin/cookie-roundtrip`: 20/20. `bin/pixel-parity --fast --only 'auth/**,errors/**,rooms/show/designers'`:
  default 38/38, first_run 8/8, custom_styles 2/2 screenshots, aria and loose equal on every seed, PASSED; `bin/clean-parity` run. Free disk 21 GB before the bench.
- **Bench** (`bench/quick --reps 3 --apps fsharp,dotnetway`, `FSHARP_IMAGE=campfire-dotnetway:p8ctl` as the control, interleaved, host probe 1.04-1.08 on the measured runs (two control reps started at 1.42-1.48 and
  are the wide ranges in its rows), `bench/results/quick-87`; 30 runs, all 2xx, 0 errors, post rows equal the 2xx answers): medians new / control req/s: room page c=1 2,235 / 2,219, c=16 7,241 / 7,213; messages page 2,046
  / 2,093 and 7,121 / 7,070; sidebar 2,169 / 2,139 and 7,353 / 7,176; search and the post rows in the report. CPU per request equal inside the ranges (room page 559 / 564 us at c=1, post c=1 1,795 us). **vs Rails**
  (stored medians): room page 20.9x (c=1) / 30.3x (c=16), messages 9.4x / 18.7x, sidebar 7.6x / 12.3x, search 9.9x / 16.9x, post **6.39x** (c=1) / **6.48x** (c=16): no cell under 5x, no speed fix made.
- **Differences recorded:** none for users. Internally the database now prepares inside the host (`StartingAsync`) rather than before it is built, so a failed `db:prepare` surfaces as the start's exception
  (`Error: <message>`, exit 1, the same text) and no longer before the TLS-domain check; and `closeDatabaseAt` opens a plain connection for the shutdown checkpoint instead of leasing a pooled context.
- **Left for 8.7b:** `Support` onto `Boot.build` (in-memory configuration, provider and logger overrides, loopback Kestrel), then delete `Booted`, `bootEdge`, `bootWith*`, `Booted.Services` and the share
  list; `Boot.addServices`/`addWorkers` stay public only as long as the shim needs them.

### Phase 8, unit 8.7b Builder, tests on `Boot.build` (2026-10-27)

Sonnet builder, the second half of the 8.7 split. `bin/kloc` 27,594 / **28,037 corrected (28.0)**, from 28,107 after 8.7a (-70: `Booted`, `bootEdge`, `bootWith*`, the shim's share list and `BootInputs.DbOptions` are gone from `Boot.fs`; `Support.fs` grew by about a third of that).
- **What changed:** `Support` boots every `Test` through `Boot.build` over an in-memory configuration on two free ports (`HTTP_PORT`, `TARGET_PORT`; `LOG_REQUESTS` and `CACHE_SIZE` default to 0 in tests, as the test hosts never ran the request log or the output cache), with the capture as the loggers.
  The clock, the EF context options (`LogTo`) and the per-test services replace registrations through `overrides`; a startup filter counts body writes. `Boot.addServices` resolves `TimeProvider` from the provider, so a replacement takes effect everywhere (`Env`, `AppState`, `DatabaseStartup`, the context factory, push).
  `Test.Booted` is now `Support.Wiring` (App, Pipeline, ConfigureServices, Workers): the registrations a second test host needs to serve the primary host's state (`Boot.addServices` over the host's own `EdgeConfig`, then the host's singletons shared in). It is a test helper, not a second app. `Boot.addServices` and `addWorkers` stay public for it.
  LG4 and HS9 now boot the app with `LOG_REQUESTS` / `MAX_REQUEST_BODY` in the configuration instead of calling `bootEdge`. A port picked free and taken before Kestrel bound it (seen once) retries on new ports.
- **Gates:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` 1,030 tests, 1,024 passed, 6 skipped, 0 failed (guards, lint-markup and H1 green). `bin/linux-tests App` 418 tests, 417 passed, 1 skipped (AC11 without Pebble; HS2 and HS4 ran). The production path is untouched apart from the `TimeProvider` resolution above, so the image, bench and pixel gates of 8.7a stand (no re-measure).
- **Differences recorded:** none for users. Test logs now also carry the framework's own lines (the host's `ILoggerFactory` is the capture); the tests that read logs filter by category and passed unchanged.
