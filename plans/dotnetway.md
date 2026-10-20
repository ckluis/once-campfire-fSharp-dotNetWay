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
