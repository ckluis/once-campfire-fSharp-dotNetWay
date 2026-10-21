# Design note: the view layer on Falco.Markup (Phase 4)

Status: design, 2026-10-21, before Phase 4 builds. Author: the Opus designer (plan section 5). Binding for the Phase 4 builders
together with `plans/dotnetway.md` sections 3-5, D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`),
`plans/design-kit-auth.md` (the `Pages` writer and compression of Phase 2) and `plans/design-cable.md` (the broadcasts that carry
partials). Where this note and plan section 2.3 disagree, this note wins: it is later and rests on the spikes in section 2. The
disagreements, each argued below: a small page-in-parts type stays (the plan deleted "recorded pages" outright, V6); attribute values
use the same encoder as Falco's `Text.enc` rather than `HtmlEncoder.Default` (V2); the Jbuilder caches go (V5); the views differential
retires, but its last sample of Rust's answers is frozen as test data (V11); about 3.4 KLOC instead of 4.0 (section 7).

Scope: `Campfire.Views` (about 80 templates, the view helpers, the UTF-8 writer `Core/Out.fs`, `Helpers/Tag.fs`, the fragment
cache, the recorded pages, the layouts), and the parts of `Campfire.App` that hand pages to the response (`Web/Pages.fs`,
`Web/PageParts.fs`, `FragmentGlue.fs`, the presenters' fragment and Jbuilder caching, the fragment cache in `Boot`). Out of scope:
what the controllers compute (Phase 2's, unchanged), the rich-text pipeline that produces message bodies (Phase 5; this phase only
types its output), Cable's Turbo tag builder (`Campfire.Cable/Turbo.fs`, protocol code; it gets strings from `HtmlOutput.ToString`).

**Preconditions.** Phases 1-3 closed (Phase 3 verifier commit `b632a7c`): controllers are Web actions on `HttpContext`, `Pages`
writes every rendered response (`plans/design-kit-auth.md` K-notes, unit 2.8's kept compressed pages), Cable takes `Payload.ofString`
for Turbo stream tags, the hybrid reads are in `FastReads.fs`. If a piece is missing, the first unit that needs it stops and reports.

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity or the 5x floor forces it), with
the reason.

| # | Decision | Reason |
|---|---|---|
| V1 | **Falco.Markup 1.4.0**, pinned in `Directory.Packages.props` (Falco 5.2.0 already depends on exactly 1.4.0). A partial is a function returning `XmlNode` (or `XmlNode list` when the ERB has several roots); a page is a function returning `Regions` (V8); a turbo-stream template returns `XmlNode list`. Everything is rendered with Falco's `renderNode` / `renderHtml`; there is no serializer of our own. | D2/D9. Spike S1: 1.4.0 is still the newest release (2025-09-10, no commit on `master` since); Falco 6.0.0-beta1 also depends on 1.4.0. Nodes are pure values, so templates are pure functions, which is what lets fragments be fetched up front (V5). |
| V2 | **One encoding module, `Html` (`Core/Html.fs`)**: text through Falco's own `Text.enc` (`WebUtility.HtmlEncode`); attribute values through `Html.attr name value` = `Attr.create name (WebUtility.HtmlEncode value)`, the same encoder; `Html.attrInt`, `Html.text`, `Html.int`, `Html.sp`, `Html.nl`; HTML that is already safe is a `Html.Trusted` value (a struct with a private constructor) written by `Html.trusted`. `Trusted` has three makers, each one call site: `Trusted.ofSanitized` (rich text the sanitizer produced, in the presenter), `Trusted.ofAssetTags` (the import map and stylesheet tags `Campfire.Assets` builds from digests), `Trusted.ofAccountStyles` (the account's custom CSS, which Rails writes `html_safe`). | S1 confirms the plan's finding on 1.4.0 and `master`: `Attr.value "a\"b<c>"` renders `value="a"b<c>"`, and there is no encoding attribute constructor. Using `Text.enc`'s encoder for attributes too keeps one escaping rule in the project (it writes `é` as `&#233;` and emoji as `&#128512;`: class A, same DOM). Rails' exact bytes (`ERB::Util.html_escape`) are not needed for pixels; the plan's `HtmlEncoder.Default` would add a second, different rule (`&#xE9;`). Cached fragments are not `Trusted` text: they are slots (V6). |
| V3 | **The markup lint, `bin/lint-markup`**, run by `bin/verify` over `src/Campfire.Views` and `src/Campfire.App` (outside `Core/Html.fs`): every `Attr.<member>` takes a plain string literal (not `$"..."`, not followed by `+`/`^`), except Falco's 52 zero-argument values (`Attr.methodPost`, `Attr.disabled`, ...) and `Attr.merge`; `Attr.create` takes two literals; every `Text.<member>` other than `enc` and `empty` takes a literal (`Text.rawf` is refused: it formats a value raw; so are the `Text.p`/`Text.strong`-style shortcuts, which wrap `raw`); no `TextNode`, `KeyValueAttr` or `NonValueAttr` built directly; `Elem.create` takes a literal tag; `Elem.createSelfClosing` only with an HTML void element name, and `Elem.create` never with one; the `Trusted` makers only in their three allowlisted files; HybridCache's `GetOrCreateAsync`/`SetAsync`/`RemoveAsync` only in `Core/Fragments.fs` (the fetchers and `Pages` call `Fragments.fetch` and `Fragments.compressedPage`). `bin/lint-markup --self-test` lints a planted file per rule and fails unless each is caught and a clean file passes; `bin/verify` runs both. | The plan's rule ("every non-literal attribute value through the encoding module, every non-literal `Text.raw` already-sanitized or a bug") made mechanical. Spike S2: the prototype caught all 12 planted cases (variable `href`, `Attr.create` with a variable, `"Hi " + v`, `$"Hi {v}"`, `"x" + v` after a literal, `Text.raw body`, `Text.rawf`, `Text.strong name`, `KeyValueAttr`/`TextNode`, pipe-in `v |> Attr.title`, partial application `List.map (Attr.create "data-x")`, a variable tag and a two-line `Attr.create`) and passed a clean file with literals in comments and strings; on the spike's real ports it flagged only the seam's `Trusted` calls. A custom element written self-closing (`<turbo-frame/>`) is not void in HTML and swallows its siblings: that is why `createSelfClosing` is restricted. |
| V4 | **Whitespace follows the loose DOM rule** (`parity/capture/loose.ts`): where the ERB has whitespace between two inline-level siblings (text; `a span img button input label strong time ...`; any custom element: `turbo-frame`, `turbo-cable-stream-source`, `action-text-attachment`), the Falco template writes `Html.sp`; text that the ERB leaves with whitespace at an edge touching an inline sibling keeps one space there (`Html.sp; Html.text name`); between block elements, and between children of the document head, nothing. Inside `textarea` and `pre` the text is exact, and the template writes `Html.nl` right after the opening tag where Rails' `text_area` does (it adds a newline so a leading newline in the value survives the parser). Attribute values Rails spells out stay spelled out (`hidden="hidden"`, not bare `hidden`: the loose layer compares values). | Spike S3, below: with no whitespace at all, the sidebar differed from Rails' golden in 16 places of the loose DOM and the room page in 8 per message (98 on a 13-message page); with 11 `Html.sp` in the sidebar templates and 7 in the message partials (plus 3 conditional) both were equal to Rails on every line. Spaces where the ERB had none are as wrong as missing ones (an inline-block row changes width). The loose layer is a class C gate, so it, not the computed style, is the rule. |
| V5 | **Fragments live in `HybridCache`** (`Microsoft.Extensions.Caching.Hybrid` 10.11.0, local cache only, no L2): the value is `CachedFragment`, a sealed `[<ImmutableObject(true)>]` class holding the fragment's UTF-8 bytes, their SHA-256 (computed once, at creation) and a process-unique `Id`; a custom `IHybridCacheSerializer<CachedFragment>` (six lines) lets the cache measure it. Keys are Rails' `cache_key_with_version` with the partial and variant: `views/messages/_message/messages/<id>-<updated_at usec>/presentation-v3`, `views/messages/boosts/_boost/boosts/...`, `views/users/sidebars/rooms/_direct/memberships/...`, no template digest (an in-process cache empties on deploy). `AddMemoryCache(SizeLimit = FragmentCacheBytes)` (32 MiB default, the config value the port has), `HybridCacheEntryOptions` Expiration and LocalCacheExpiration 7 days (the default is 5 minutes). **Fragments are fetched before rendering, with `await`** (`MessagesCached.messages cache ctx views : ValueTask<CachedFragment[]>` in the presenter or controller), never inside a template; a message's factory awaits its boosts' fragments first, as Rails nests `cache boost` inside `cache [message, "presentation-v3"]`. The Jbuilder caches (`json.cache!` for users, messages, boosts in `Presenter.fs`) go: JSON is rendered each time. | The framework idiom the decisions file names ("HybridCache for page/fragment caching: stampede protection"). Spike S5: a hit returns the **same instance** for the immutable class and for `string`, but a **copy** for `byte[]`, hence the wrapper; a hit completes synchronously; 64 concurrent misses on one key ran the factory once; HybridCache sets each L1 entry's `Size` to the serialized length, so `SizeLimit` bounds it (400 fragments of 9.4 KB into a 2,000,000 limit left 213 entries, 1,990,083 estimated); 40 hits cost 6.5 us and 19.8 KB (the key strings) against the current cache's 2.7-12.3 us. Fetching up front keeps `.Result` out of templates (AGENTS: no sync-over-async) and the templates pure. The JSON caches only save time on the bot API, which no workload measures; dropping them is invisible (keyed by version) and removes their size accounting. |
| V6 | **A page with cached fragments is written in parts, never joined into one string.** A template places a fragment with `Html.fragment f`, which writes the marker `<` U+E000 `<f.Id>` `>`; `HtmlOutput.ofNode node fragments` (and `ofDocument`, `ofNodes`) renders with Falco, splits at the markers, finds each fragment by `Id` among the ones passed, and keeps the text parts as UTF-8 (`HtmlOutput = { Parts: byte[][]; Fragments: CachedFragment[] }`). The response writer copies part, fragment, part into the body pipe. A fragment nested in a fragment (boosts in a message) is flattened into the outer fragment's bytes when that one is created. | Spike S4: the busy room page (40 messages, 430 KB) rendered as one Falco string costs **583 us and 1.64 MB allocated per page** with the workstation GC, **132-184 us and 1.64 MB** with the server GC the app runs (a 780 KB string on the large object heap every request: at 8,000 room pages a second that is 13 GB/s of LOH), against **20-26 us and 150 KB** in parts. The messages page as one string: 157-266 us, 1.5 MB, against 4 us now. That would put the messages page at c=1 within 5-20% of the 5x floor, so the floor, not taste, decides. The marker cannot be forged: both encoders escape `<`, a parser never serializes a `<` that does not open a tag, and `HtmlOutput` skips a U+E000 that is not right after `<` (S6: hostile text and attribute values holding the marker left the page in its two real parts). |
| V7 | **Compressed pages stay kept, in the same HybridCache**, keyed `page/<encoding>/<SHA-256 hex>` where the digest covers the page's text parts and its fragments' stored digests in order (`IncrementalHash`, about 5 us for the room page's 16 KB of text); the value is a sealed immutable `CompressedPage` made with the framework's `IResponseCompressionProvider` stream, written already encoded with `Content-Encoding` and `Vary`, as unit 2.8 does. Only pages holding at least one fragment use it; everything else goes through `UseResponseCompression`. `PageParts.fs`, `Generations`, `FragmentGlue.fs` and the 16 MiB private map go. | Unit 2.8's evidence: compressing the 416 KB room page afresh each request put the messages page at 4.47x Rails at c=1. Gzip at its fastest costs 205-218 us a room page here (S4). SHA-256 over the whole body would be 116 us (S4), over the parts and the fragments' digests about 5. The cache gives stampede protection for sixteen identical concurrent pages and one size budget instead of a hand-made two-generation map. |
| V8 | **Layouts take `content_for` as a record.** `Regions = { Title; BodyClass; Head; Nav; Content; Footer; Sidebar }` (strings optional, regions `XmlNode list`, `Regions.content nodes` for content-only templates); a page module returns `ViewPage = { Regions: Regions; Fragments: CachedFragment list }`. `Layouts.application ctx regions` is the application layout (rendered with `renderHtml`, which writes `<!DOCTYPE html>`); `Layouts.frame regions` is turbo-rails' frame layout (`<html><head>` Head `</head><body>` Content `</body></html>`, `renderNode`, no doctype, as turbo-rails has none). `Pages.page` picks the frame layout for a `Turbo-Frame` request as now; `MessagesController`'s own layout choice stays a parameter. `ApplicationWrapper` and `RenderSize` go. | The plan's 2.3 ("`content_for` regions become `XmlNode list` arguments; the Turbo-Frame layout is a second layout function"). A record instead of seven positional arguments, because 30 page modules build it. Spike S6: the frame layout and a `create.turbo_stream` with a fragment slot rendered loose-equal to the writer's output (212 lines, 0 differences). |
| V9 | **Turbo streams are Falco nodes**: `Elem.create "turbo-stream" [ Attr.create "action" "append"; Html.attr "target" target ] [ Elem.template [] [ Html.fragment f ] ]`, rendered with `HtmlOutput.ofNodes`. A broadcast takes `HtmlOutput.ToString()` into `Payload.ofString`. An empty `rooms/refreshes/show` is an empty body (Erubi wrote `"\n"`). | Same DOM; a turbo-stream response is a list of roots, which is what `ofNodes` is for. |
| V10 | **Not markup, not Falco.Markup**: `pwa/manifest.json` is written with `Utf8JsonWriter` (relaxed escaping, the port's key order); `pwa/service_worker.js` stays a string constant; `users/avatars/show.svg` is Falco.Markup elements (`Elem.create "svg"` / Falco's SVG module) with `Html.text` for the initials (numeric references are valid XML). | Each by the right framework part: JSON by System.Text.Json, the script is static text, SVG is markup. |
| V11 | **Oracles**: (a) `tests/Shared/LooseDom.fs`, an F# port of `loose.ts` over AngleSharp, pinned to `loose.ts` by stored outputs; (b) the views differential's sample (`tests/Campfire.Views.Tests/differential.jsonl`, 886 cases with Rust's answers) refreshed once in 4.1 with every template's hostile-string cases, then **frozen as test data** (`oracle.jsonl`) and compared loose-equal from then on; (c) the Rails goldens (`rust/crates/views/tests/golden/a` HTML, `golden/b` token streams) compared loose-equal on both sides. `bin/views-differential`, `bin/views-test-parity`, `reference-tools/views` (`transcribe.py`, the Rust differential) retire in the last unit. AngleSharp enters the test projects now (plan 2.11; Phase 5 brings it into `src`). | The plan retires the byte-for-byte differentials against Rust; the frozen answers are what Rails renders for inputs nobody would type by hand (every string field hostile), so they stay as vectors do. The loose comparator is the harness's own rule, so a template that passes in `bin/verify` passes the loose layer in the browser. |
| V12 | **Migration seam, `Core/Legacy.fs`** (deleted in 4.11): `Legacy.node (render: Out -> unit) : XmlNode` puts a not-yet-ported partial inside a Falco template (`Trusted` text of `Render.text`; only for partials that hold no fragment), and `Legacy.write (w: Out) (page: HtmlOutput)` puts a Falco partial or page into a writer template, its fragments recorded by `Out.Fragment`. Until 4.10 a Falco page module's `Regions` go through `Legacy.inLayout ctx frame rendered : HtmlOutput`, which calls the writer's layout with each region written by `Legacy.write`. | Phase 2's `Legacy.wrap` pattern: the app stays pixel-identical after every unit, templates move one directory at a time, and the layout (which every page touches) moves once, last. |
| V13 | **Helpers**: Rails' markup helpers become plain Falco functions in a new folder `Markup/` (`Markup.Assets.image`, `Markup.Links.linkTo`/`linkBack`, `Markup.Forms.formWith`/`buttonTo`/fields, `Markup.Turbo.frameTag`/`streamFrom`, `Markup.Application`, `Markup.Users`, `Markup.Rooms`, `Markup.Messages.presentation`), each written the first time a unit needs it. A block is a `XmlNode list` argument. `Helpers/` keeps only what is not markup (`Url`, `Translations`, `UnicodeTables`, the involvement words, avatar colours); `Tag.fs`, `Filters.fs` and the writer halves of `Forms`, `Links`, `Assets`, `Application`, `UsersHelper`, `RoomsHelper`, `Turbo` go in 4.11. No form token is written (Phase 2's token-less CSRF). | Coexistence during the migration without renaming twice; Falco's `Attr`/`Elem` replace `Tag.fs`'s attribute builder (the plan's "most of `Helpers/Forms.fs`"). |
| V14 | **Speed rule for the phase**: no page is rendered as one string when it holds fragments (V6); a unit that moves a hot-path template (the five workloads) runs `bench/quick` on the workloads it touches against a same-session control image of its base commit; a cell under 5x Rails blocks (fix inside Falco's way: fetch fragments once, render a partial once, keep compressed pages, never re-render a cached fragment). | Plan section 3, gate 5, and the speed rule of the phase prompt. |

What stays hand-written, and why (parity or the floor): about 80 template functions (the bulk), Rails' markup helpers as Falco
functions, the `Html` module (40 lines), `HtmlOutput` (about 60), the fragment keys and fetchers (about 60), the compressed-page path in
`Pages` (about 40), the view-models.

## 2. Spikes (scratch project, 2026-10-21)

`/private/tmp/claude-501/-Users-clank-Desktop-projects/fea4ed62-8823-4b82-bed5-fc54b6214d57/scratchpad/viewspike/` (an F# console
project on .NET SDK 11.0.100-rc.2, net11.0, Release, referencing the repo's `Campfire.Views` and linking `tests/Shared/Repo.fs`,
the differential's `Inputs.fs`, `Facts.fs` and `GoldenB.fs`; packages Falco.Markup 1.4.0, Microsoft.Extensions.Caching.Hybrid
10.11.0, System.IO.Hashing 10.0.0; `loose.ts`/`normalize.ts` copied beside a 20-line `cmp.ts` run by node 25 with parse5 8.0.1;
`views_lint.py` and 13 planted files). **Removed after the spikes**; the numbers are here. Apple M-series Mac, one thread, load
average 1.1-1.6; each timing is the middle of five runs of 2,000-20,000 renders after a warm-up.

| # | Question | Result |
|---|---|---|
| S1 | Falco.Markup version; attribute encoding | NuGet: 1.4.0 newest (2025-09-10); GitHub `master` last commit 2025-09-10 ("readme"), `XmlNodeSerializer.serialize` writes `KeyValueAttr` values with `w.Write attrValue`, no encoding; `XmlNodeSerializer` is internal, `renderNode`/`renderHtml` return a `string` built in a `StringBuilder` (cached only under 360 chars). Rendered: `Attr.value "a\"b<c>&d'e"` -> `value="a"b<c>&d'e"`; `Text.enc` -> `a&quot;b&lt;c&gt;&amp;d&#39;e &#233; &#128512;`; `Html.attr` the same in an attribute; voids `<meta name="x" />`, `<br />`, empty `<span></span>`, bare `disabled` |
| S2 | The lint, on planted cases | 12 of 12 planted violations caught (list in V3), the clean file passes, the real spike ports clean except the seam's `Trusted` calls |
| S3 | Whitespace, against the Rails goldens | `users/sidebars/show` (+ `_shared`, `_direct_placeholder` ported, `_direct` through the seam) inside the writer's layout vs `golden/a/sidebar_david.html` (Rails' HTML) under `loose.ts`: the writer 0 differences of 207 lines; Falco with no whitespace **16** (spaces between the two `turbo-cable-stream-source`s, between `avatar` and `direct__author` spans, before the placeholder names " Bender", between the room links, between each icon and its screen-reader label, between the two tool links); with 11 `Html.sp` **0**. `messages/_message` + `_actions` + `_boosts` (`_presentation`'s body through the seam) as fragments in the writer's `rooms/show`, vs the `golden/b` token streams rebuilt as HTML: no whitespace **8** differences on `rooms_show_member` (1 message), **98** on `rooms_show_closed` and `rooms_show_at_message` (13); with 7 unconditional and 3 conditional `Html.sp` **0** beyond one artifact of rebuilding (the `<template>` content, a text token in `canonical.rb`, re-escaped; the rebuild must keep template text raw). The Falco fragments are 375,068 chars against the writer's ~414,000 bytes (no indentation) |
| S4 | Render cost, busy room page (`rooms_show_closed`'s 13 messages repeated to 40, 430 KB) | writer, warm, recorded (today): **37.5 us, 28.7 KB**; joined into one array 43-53 us, 458 KB. Falco, layout and chrome rebuilt as 764 nodes and attributes per render, fragments as strings: **one string then UTF-8: 583 us, 1.64 MB** (the string alone 729 us: the LOH collections decide, not the encoding; under the server GC 184 us to UTF-8 and 132 us for the string alone); **in parts (layout split at markers, fragment bytes as they are): 20.5 us, 150 KB** (server GC 26.0 us); the layout alone 16.8 us, 117 KB (it rebuilds the 30 stylesheet links one by one; the real layout writes the asset tags as one `Trusted` string). Messages page (40 fragments, no layout): writer 4.2 us, 5.2 KB; Falco one string 266 us (server GC 157 us), 1.52 MB. One message, cold: writer 4.0 us, 14 KB; Falco 9.6 us to a string, 10.6 us to bytes, 88-98 KB. Sidebar page: writer 9.9 us, 54 KB; Falco (layout rebuilt + the port) 23.2 us, 202 KB (server GC 28.3); sidebar content alone 6.8 us vs 5.9 us. Keys: SHA-256 of the 430 KB page 116 us, XxHash128 13.7 us, a byte compare 6.9 us; gzip fastest of the page 205-218 us |
| S5 | HybridCache as the fragment cache | sealed `[<ImmutableObject(true)>]` value: a hit returns the same instance (true); `string` same instance (true); `byte[]` a copy (false); a hit's `ValueTask` is already completed (true); 64 concurrent misses: 1 render, one instance; `SizeLimit` 2,000,000 with 400 entries of 9.4 KB: 213 kept, estimated size 1,990,083 (HybridCache sets `Size` to the serialized length; `TrackStatistics` shows it); 40 hits 6.48 us, 19.8 KB (key strings) vs the current `FragmentCache` 2.7-12.3 us, 104 B |
| S6 | Layouts as arguments, the frame layout, turbo streams, slots | a Falco `create.turbo_stream` with the message as a slot: loose-equal to the writer's (212 lines, 0 differences), 2 parts; the frame layout with `Head`/`Content` lists and 40 slots: 2 parts, 40 fragments, first part `<html><head><meta ... /><meta name="current-room-id" ... /></head><body>...`; a `title` attribute and a text both holding `<` U+E000 `0>`: encoded to `&lt;`U+E000`0&gt;`, the page keeps its 2 real parts |

**What S4 predicts against the floor** (bench rig: colima, 4 server cores, server GC; Phase 3 gate figures). The room page in
parts costs about what it does now (20-26 us for the parts, 6.5 us for 40 hits, 5 us for the digest, against 37.5 us): no change
beyond noise at 33.8x/25.1x. The messages page adds about 3 us (hits and digest) to 441-519 us of CPU per request: <1%. The sidebar
adds 8-13 us (the layout and the port) to 537 us at c=1: **7.99x -> about 7.8x**. Post a message renders the new message once
(+6 us and +84 KB against the writer) and its turbo stream from the cache: +8-10 us on 1,476 us at c=16, **6.20x -> about 6.15x**,
and 6.69x -> about 6.65x at c=1. Search is shaped like the room page: no change. Allocation per page rises from 29-54 KB to 150-200
KB (a gen0 collection about every 0.3 s per heap at the 64 MiB budget): within the CPU figures above, watched by the bench's CPU
column. **One string per page would not hold**: +130-180 us and 1.6 MB per room page under the server GC, plus either 116 us of
SHA-256 or 205 us of gzip per request for the compression key; the messages page at c=1 would land at about 5.2-5.9x.

## 3. Types and modules

Names are binding; bodies are the builder's. `Campfire.Views` compile order (new files marked +, deleted in 4.11 marked -):
`Core/Fragment.fs` (+ `CachedFragment`, replacing `Fragment`), `Core/Html.fs` +, `Core/HtmlOutput.fs` +, `Core/Fragments.fs` +,
`Core/Out.fs` -, `Core/ViewContext.fs`, `Core/Legacy.fs` + - (after `Out.fs`), `Core/FragmentCache.fs` - (in 4.3), `Helpers/*`
(the writer halves - in 4.11), `Markup/*.fs` +, the view-models, `Layouts.fs`, `Templates/**`, `*Cached.fs`.

```fsharp
namespace Campfire.Views

// Core/Fragment.fs (about 20 lines)
[<Sealed; System.ComponentModel.ImmutableObject(true)>]
type CachedFragment =
    new: bytes: byte[] -> CachedFragment          // SHA-256 computed here; Id = Interlocked.Increment of a process counter
    member Id: int64
    member Bytes: byte[]
    member Digest: byte[]                         // SHA-256 of Bytes
    member Length: int

// Core/Html.fs (about 40 lines): the only file where a non-literal reaches Attr.create or Text.raw (V2, V3).
// Its full name is Campfire.Views.Markup.Html, beside the Markup/ helpers (`open Campfire.Views.Markup`). The writer's byte type
// `Campfire.Views.Html` is renamed `SafeBytes` in 4.1 (compiler-guided, about 40 sites) so no name means two things.
module Html =
    [<Struct>] type Trusted                       // private constructor
    module Trusted =
        val ofSanitized: string -> Trusted        // allowlisted: src/Campfire.App/Presenters/Presenter.fs (MessageContent.Text)
        val ofAssetTags: string -> Trusted        // allowlisted: the ViewContext builder (importmap, stylesheet tags)
        val ofAccountStyles: string -> Trusted    // allowlisted: Markup/Application.fs (custom_styles_tag, html_safe in Rails)
    val attr: name: string -> value: string -> XmlAttribute          // WebUtility.HtmlEncode
    val attrInt: name: string -> value: int64 -> XmlAttribute
    val text: string -> XmlNode                                      // Text.enc
    val int: int64 -> XmlNode
    val trusted: Trusted -> XmlNode
    val fragment: CachedFragment -> XmlNode                          // the slot: "<" + U+E000 + Id + ">" (V6)
    val sp: XmlNode                                                  // " " (V4)
    val nl: XmlNode                                                  // "\n" after <textarea>/<pre> (V4)

// Core/HtmlOutput.fs (about 60 lines)
[<Sealed>]
type HtmlOutput =
    member Parts: byte[][]                        // UTF-8 text; Parts.Length = Fragments.Length + 1
    member Fragments: CachedFragment[]            // in document order
    member Length: int                            // bytes
    member Digest: unit -> byte[]                 // SHA-256 over parts and fragment digests (V7), computed on demand
    member WriteTo: System.Buffers.IBufferWriter<byte> -> unit
    member ToArray: unit -> byte[]                // flattened (a fragment made of a page; tests)
    override ToString: unit -> string             // Cable payloads (V9)
module HtmlOutput =
    val ofNode: XmlNode -> fragments: CachedFragment seq -> HtmlOutput          // renderNode; throws if a slot's fragment was not passed,
    val ofDocument: XmlNode -> fragments: CachedFragment seq -> HtmlOutput      // renderHtml   or a passed one was placed twice or never
    val ofNodes: XmlNode list -> fragments: CachedFragment seq -> HtmlOutput    // several roots (turbo streams, multi-root partials)
    val text: string -> HtmlOutput                                              // a body with no fragment

// Core/Fragments.fs (about 60 lines): the HybridCache side (V5)
type CachedFragmentSerializer = interface IHybridCacheSerializer<CachedFragment>
type CompressedPage = (sealed, immutable: Encoding: string; Bytes: byte[]) + its serializer
module Fragments =
    val add: IServiceCollection -> sizeLimit: int64 -> unit   // AddMemoryCache(SizeLimit, TrackStatistics), AddHybridCache(options:
                                                              // MaximumPayloadBytes 4 MiB, 7-day expirations), both serializers
    val cacheKeyWithVersion: table: string -> id: int64 -> updatedAt: Timestamp -> string   // also the ETags of logos/avatars/messages
    val key: partial: string -> table: string -> id: int64 -> updatedAt: Timestamp -> variant: string option -> string
    val fetch: HybridCache -> key: string -> render: (CancellationToken -> ValueTask<CachedFragment>) -> ValueTask<CachedFragment>
    val compressedPage: HybridCache -> encoding: string -> HtmlOutput -> compress: (HtmlOutput -> byte[]) -> ValueTask<CompressedPage>

// Core/Legacy.fs (about 30 lines; deleted in 4.11) (V12)
module Legacy =
    val node: (Out -> unit) -> XmlNode                      // a writer partial with no fragment, as Trusted text
    val write: Out -> HtmlOutput -> unit                          // a Falco page or partial into a writer template; fragments recorded
    val inLayout: ViewContext -> frame: bool -> ViewPage -> HtmlOutput   // until 4.10: Falco regions in the writer's layouts

// Layouts.fs + Templates/Layouts/** (V8)
type Regions =
    { Title: string option; BodyClass: string option
      Head: XmlNode list; Nav: XmlNode list; Content: XmlNode list; Footer: XmlNode list; Sidebar: XmlNode list }
module Regions = val content: XmlNode list -> Regions
type ViewPage = { Regions: Regions; Fragments: CachedFragment list }
module Layouts =
    val application: ViewContext -> Regions -> XmlNode      // renderHtml'd by HtmlOutput.ofDocument
    val frame: Regions -> XmlNode                           // turbo-rails' frame layout, HtmlOutput.ofNode
    val page: ViewContext -> frame: bool -> ViewPage -> HtmlOutput

// The fetchers, after the templates (replacing MessagesCached/BoostsCached/UsersCached's bodies)
module MessagesCached =
    val message: HybridCache -> ViewContext -> MessageView -> ValueTask<CachedFragment>   // awaits BoostsCached.boosts first
    val messages: HybridCache -> ViewContext -> MessageView list -> ValueTask<CachedFragment[]>
module BoostsCached = val boosts: HybridCache -> ViewContext -> BoostView list -> ValueTask<CachedFragment[]>
module UsersCached = val directRooms: HybridCache -> ViewContext -> SidebarDirect list -> ValueTask<CachedFragment[]>
```

Template shapes (one module per ERB file at the same relative path under `Templates/`, as now; `rooms/show.html` stays `ShowPage`):

```fsharp
// a partial
module Campfire.Views.Templates.Messages._Message
let render (ctx: ViewContext) (m: MessageView) (boosts: CachedFragment[]) : XmlNode = Elem.div [ Html.attr "id" ... ] [ ... ]
// a page
module Campfire.Views.Templates.Rooms.ShowPage
let regions (ctx: ViewContext) (show: ShowView) : Regions = { Title = Some show.Room.DisplayName; BodyClass = Some "sidebar"; ... }
let rendered (ctx: ViewContext) (show: ShowView) : ViewPage = { Regions = regions ctx show; Fragments = show.Messages }
// a turbo stream
module Campfire.Views.Templates.Messages.CreateTurboStream
let render (target: string) (message: CachedFragment) : XmlNode list = [ Elem.create "turbo-stream" [...] [ Elem.template [] [ Html.fragment message ] ] ]
```

View-models change where fragments enter: `MessageItem` becomes the fetched `CachedFragment` (its `View` case goes; the presenter
fetches), `ShowView.Messages`, `IndexView` results, `RefreshView.NewMessages`/updated, `SidebarShow.DirectMemberships` hold
`CachedFragment`s; `MessageContent.Text` holds a `Html.Trusted`.

App side:

```fsharp
// Web/Pages.fs: what replaces RecordedPage/PageParts/FragmentGlue
module Pages =
    val page: HttpContext -> status: int -> (ViewContext -> ViewPage) -> Result<HttpHandler, Error>     // + the template lookup, the frame choice
    val pageInApplicationLayout: HttpContext -> status: int -> (ViewContext -> ViewPage) -> Result<HttpHandler, Error>
    val bare: HttpContext -> status: int -> Format -> (ViewContext -> HtmlOutput) -> Result<HttpHandler, Error>   // turbo streams, layout false
    // send: Content-Length = page.Length; HEAD keeps it; a page with fragments and a compressing request: HybridCache
    // "page/<enc>/<digest>" -> CompressedPage (V7), else page.WriteTo response.BodyWriter
// Web/Services.fs: Fragments.add services config.FragmentCacheBytes
// Boot.fs, Controllers.fs, Web/Data.fs, Presenters/Page.fs: FragmentCache.scoped / withCache and AppState.FragmentCache go
// Presenters/Presenter.fs: Trusted.ofSanitized at MessageContent.Text; the three tryFetchValue JSON caches go
// Channels/Broadcasts.fs: renders through the fetchers and HtmlOutput.ToString
```

Packages: `Falco.Markup` 1.4.0 (explicit; already transitive), `Microsoft.Extensions.Caching.Hybrid` 10.11.0 (Views; it brings
`Microsoft.Extensions.Caching.Memory`, which the net11.0 shared framework supersedes); tests: `AngleSharp` 1.8.5 (or the version
Phase 5 pins). Not added: System.IO.Hashing (SHA-256 over parts is cheap enough, V7).

## 4. Units, in build order

One Sonnet builder per unit, about 90 minutes, from a clean `git status` on `dotnetway`, one commit per logical step (read `git diff
--cached --stat` first), a Phase log entry per unit (KLOC from `bin/kloc`, gates run, differences recorded). **Common acceptance for
every unit:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green (it runs `bin/lint-markup --self-test`, `bin/lint-markup`, the oracle and golden
tests, and H1: zero query compilations in steady state); the oracle cases (`oracle.jsonl`) and goldens of every template the unit moved loose-equal; `bin/pixel-parity --fast --only
<the unit's globs>` on every seed the globs reach, equal on screenshots, aria and loose DOM, the `realtime/**` states **one at a
time**; `bin/clean-parity` after pixel runs, `bin/clean-docker` after image rebuilds; `df -h /` first (12 GB, 15 GB before a bench).
A unit that moves a hot-path template runs `bench/quick` (detached, polled) on the workloads named, 3 reps, interleaved with a
control image built from the unit's base commit, host probe and load recorded; a cell under 5x Rails blocks. The app is
pixel-identical after every unit: templates not yet moved render through the writer, and the seam (V12) joins the two.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 4.1 Foundation and oracles | `Falco.Markup` in `Directory.Packages.props` and Views; `Core/Html.fs` (module `Campfire.Views.Markup.Html`) and the rename of the writer's `Html` type to `SafeBytes`; `Core/Legacy.fs` with `Legacy.node`; `bin/lint-markup` with `--self-test` and its planted files, wired into `bin/verify`; `tests/Shared/LooseDom.fs` (AngleSharp port of `loose.ts`, with `normalize.ts`'s masks the views need) and `parity/bin/loose-golden` (node, writes `loose.ts`'s output for the goldens into `tests/Campfire.Views.Tests/loose/`); `golden.py` gets a hostile quota (every op, the HOSTILE strings of `generate.py`), `bin/views-differential golden` run once, the result frozen as `oracle.jsonl`; `DifferentialTests`, `GoldenB` and `ParityATests` compare through `LooseDom` (GoldenB rebuilds HTML from tokens, `<template>`/`script`/`style` text raw) | W1, W2, O1, O2 green **on the untouched writer** (so every later unit is judged by the same comparators); no template moved; `--fast` on `rooms/show/**` and `auth/sign_in` as a smoke check |
| 4.2 Pages in parts | `CachedFragment` replaces `Fragment` as what the current `FragmentCache` stores (`IFragmentAttachment` goes); `Core/HtmlOutput.fs`; `Out.Fragment` takes a `CachedFragment` and `Out.ToOutput()` replaces `ToRecordedPage` (text split at the recorded offsets); `Legacy.write` and `Legacy.inLayout` (V12); `Fragments.add` registers the memory cache and HybridCache, and `Pages.send` writes a `HtmlOutput`, keeping compressed pages through `Fragments.compressedPage` (V7); **deleted**: `RecordedPage`, `Web/PageParts.fs` (`Generations`, `BodyDigest`), `FragmentGlue.fs`, the `PageCompression` map, `recordedAs`, `RecordedTests` (replaced by O4, P1, P2) | O4, P1, P2; `RespondTests` X4 and `RoomsTests` "pages of messages go out in parts" re-pointed; network layer on the image: one `Content-Encoding` for gzip, br, `gzip, br`, none for identity, on the room page and messages page; `--fast` `rooms/**`, `messages/**`, `search/**`, `interactions/sidebar*`, `realtime/message_sent`, `realtime/boosted`, `realtime/message_edited` (one at a time); **`bench/quick` all five workloads** vs control |
| 4.3 Fragments on HybridCache | `Core/Fragments.fs`'s keys and `fetch`; the fetchers (`MessagesCached`, `BoostsCached`, `UsersCached`) awaited in the presenters, controllers and broadcasts (rooms, messages, searches, refreshes, sidebar, create/destroy streams); `MessageItem`/`SidebarDirectItem` carry the fetched fragment (no `View` case); **deleted**: `Core/FragmentCache.fs` (`KeyBuf`, the template digests), `FragmentCache.scoped`/`withCache` and every call, `AppState.FragmentCache`, the Jbuilder caches in `Presenter.fs`, `FragmentCacheTests` (replaced by F1-F3) | F1-F3; `--fast` as 4.2; **`bench/quick` all five** vs control |
| 4.4 Messages | the 15 `messages/**` templates (`_message`, `_actions`, `_presentation`, `_unrenderable`, `_template`, `boosts/_boost`, `boosts/_boosts`, `boosts/index`, `boosts/new`, `index`, `show`, `edit`, `create.turbo_stream`, `destroy.turbo_stream`, `room_not_found`) and `MessagesPresentation` as `Markup.Messages.presentation`; the helpers they need (`Markup.Assets`, `Markup.Links`, `Markup.Forms` first part) | the messages oracle cases (hostile included) and `messages_*` goldens loose-equal; T1; `--fast` `messages/**`, `interactions/{actions_menu,boost_picker,boost_created,boost_delete_revealed,quick_boost,edit_form,edit_saved,reply,copy_link,message_deleted}*`, `realtime/{message_sent,message_edited,message_deleted,boosted,boost_removed,attachment_sent}` one at a time; **`bench/quick` post a message and messages page** vs control |
| 4.5 Rooms | the 20 `rooms/**` templates (`show` and `show/_nav`, `_composer`, `_invitation`; `involvements/*`; `layouts/*`; `opens/*`, `closeds/*`, `directs/*`; `refreshes/show.turbo_stream`); `Markup.Rooms`, `Markup.Turbo` | `rooms_*` goldens and oracle cases loose-equal; `--fast` `rooms/**`, `interactions/composer*`, `interactions/mention_autocomplete*`, `realtime/{removed_from_room,room_deleted_then_send,typing/one}` one at a time; **`bench/quick` room page and post a message** vs control |
| 4.6 Sidebar, search, mentions | `users/sidebars/show` and `rooms/_direct`, `_shared`, `_direct_placeholder`; `searches/index`, `_recents`; `autocompletable/users/index`, `_prompt_item`; `users/_mention`, `users/autocompletables/_template`; `Markup.Users` first part | sidebar ParityA cases and the search goldens loose-equal; `--fast` `interactions/sidebar*`, `interactions/phone_sidebar`, `search/**`, `autocompletable/**`, `realtime/{unread_in_sidebar,new_room_in_sidebar,presence_read}` one at a time; **`bench/quick` sidebar and search** vs control. **Opus checkpoint after this unit** (section 4 below the table) |
| 4.7 Users | `users/show`, `new`, `_ban_button`, `profiles/show`, `_membership`, `_transfer`, `push_subscriptions/index`, `_push_subscription`, `avatars/show.svg` (V10) | the users goldens (`users_show_*`, `profile_*`, `users_new`, `ban_button_*`, `push_subscriptions`, `avatar_*` SVGs compared as text after loose) and oracle cases; `--fast` `users/**`, `interactions/{pwa_install,notifications_help}` |
| 4.8 Accounts | `accounts/edit`, `_help_contact`, `_invite`, `users/_user`, `_next_page_container`, `index.turbo_stream`, `bots/*` (5), `custom_styles/edit` (textarea, V4) | `account_*`, `bots_*`, `custom_styles_*` goldens and oracle cases; T1 on the custom styles textarea; `--fast` `account/**`, `auth/join*` |
| 4.9 Sessions, first run, PWA, welcome | `sessions/new`, `transfers/show`, `incompatible_browser`; `first_runs/show`; `pwa/_install_instructions`, `_browser_settings`, `_system_settings`, `manifest.json` (Utf8JsonWriter), `service_worker.js`; `welcome/show` | the `sessions_*`, `first_run`, `incompatible_browser*`, `install_instructions_*`, `browser_settings_*`, `system_settings_*`, `welcome`, `manifest.json` goldens; `--fast` `auth/**`, `welcome/**`, `pwa/**`, `errors/**`, `interactions/translation_menu` |
| 4.10 Layouts | `layouts/application`, `_lightbox`, turbo-rails' `frame` in Falco (`Layouts.application`, `Layouts.frame`, `Layouts.page`); the custom styles and asset tags as `Trusted` (V2); `Pages.page`/`bare` take `ViewPage`/`HtmlOutput`; `Legacy.inLayout` and `ApplicationWrapper` deleted; every page module's `render` collapses to `rendered` | every page golden loose-equal in full (head included, after the masks); `--fast` on **every state** (25 slices as the Phase 3 gate cut them, `realtime/**` one at a time); **`bench/quick` all five** vs control |
| 4.11 Delete | `Core/Out.fs` (`Out`, `Utf8`, `SafeBytes` (the writer's byte HTML), `Render`, `RenderSize`), `Core/Legacy.fs`, `Helpers/Tag.fs`, `Helpers/Filters.fs`, the writer halves of the helpers, `Layouts.fs`'s writer parts; `bin/views-differential`, `bin/views-test-parity`, `reference-tools/views/` (`transcribe.py`, `differential/` Rust and F# sides; the F# `Operations`/`Pages`/`Rest` move into `tests/Campfire.Views.Tests/Cases/` as the oracle's runner); `OutTests`, `TagTests`, `FormsTests` (writer cases); `bench/results/views-*.md` marked superseded; AGENTS.md's view rules rewritten (Falco, `Html`, the lint, `HtmlOutput`, fragments, the oracle); README "Known differences" (section 8) | `grep -rn "\bOut\b\|Utf8.lit\|RecordedPage\|RenderSize\|FragmentCache" src tests` empty (comments aside); `--fast` on every state once more; `bin/kloc` |

Then the **phase gates** (gate runner, plan section 3): `CAMPFIRE_REQUIRE_SEED=1 bin/verify`; the **lean pixel matrix on all seeds**
(slices with `--only`, the 13 `realtime/**` states one at a time on every engine); then the **full pixel matrix once** (`bin/pixel-parity`
without `--fast`, every seed, all engines, viewports, both colour schemes and the breakpoint sweep; long, detached, polled; the plan's
"once after Phase 4"); the **hostile-text render test** (HT1-HT3, run and reported on its own even though `bin/verify` includes it);
`bench/quick` all five workloads against a same-day control (the Phase 3 verifier's `b632a7c` rebuilt as `campfire-dotnetway:p3ctl`);
`bin/cookie-roundtrip` once (the response writer changed); `bin/kloc`. Then the **Opus XSS review of every template** (the verifier):
every `Html.trusted` and `Trusted` maker, every attribute that carries a URL from data (`href`, `src`, `action`, `formaction`,
`data-*-url-value`), JSON in attributes, `<script>`/`<style>` contents (the import map, the custom styles), textarea contents, the SVG
avatar, the slot mechanism, Cable's `Turbo.actionTag`, and the lint's allowlist; findings fixed before the phase closes (class C).

**The checkpoint after 4.6** (Opus): reviews 4.1-4.6 against this note: the lint's rules and allowlist, `LooseDom` against `loose.ts`,
the frozen oracle's coverage of every moved template with hostile strings, `HtmlOutput` and the slot rule, HybridCache options (size limit,
expirations, immutability) and that no template fetches a fragment, the up-front fetching in every hot controller, the bench tables of
4.2-4.6 against their controls, and whether 4.7-4.11 still fit 90 minutes each.

## 5. Tests

In `tests/Campfire.Views.Tests` unless named; App tests boot the seed through `Support.bootSeeded`.

| Id | Test |
|---|---|
| W1 | `bin/lint-markup --self-test`: each planted file (the 12 of S2 plus `Elem.createSelfClosing "turbo-frame"`, `Elem.create "img"`, `Html.Trusted.ofSanitized` outside its file, `GetOrCreateAsync` outside `Fragments.fs`) fails with one finding; the clean file passes |
| W2 | `LooseDom.normalize` of every `golden/a/*.html` and of 30 oracle answers equals the stored `loose.ts` output line for line |
| O1 | every `oracle.jsonl` case rendered through the Falco or writer template (whichever the op is on) is loose-equal to Rust's stored answer; a page case also cold and warm, and framed when `frame` is set |
| O2 | `GoldenB` (`golden/b`) and `ParityATests` (`golden/a`) compare loose on both sides; the regions of a page as now |
| O3 | the frame layout of a page holds exactly the page's `Head` and `Content` (no doctype); `Layouts.application` starts `<!DOCTYPE html>` |
| O4 | `HtmlOutput`: a U+E000 in text, in an attribute value, and `<`U+E000`0>` typed by a user leave the part count unchanged; a fragment placed and not passed, passed and not placed, or placed twice throws; `ToArray` equals parts and fragments in order; `Digest` differs when one byte of one part or one fragment differs |
| F1 | HybridCache: a hit returns the same `CachedFragment` instance; 64 concurrent misses on one key render once; inserting twice `SizeLimit` of fragments keeps the estimated size at or under the limit; the entry options' expirations are 7 days |
| F2 | keys: `cacheKeyWithVersion` against Rails' (`messages/1-20261021120000123456`, the existing vectors); a message's key changes with its `updated_at`; a boost change gives the boost a new key and its message a new key (touch) while the other boosts are hits |
| F3 | a message fragment rendered by a broadcast (no request) and then requested by a page is one render and the same bytes; no fragment holds the request's host |
| P1 | `Pages.send`: `Content-Length` equals the bytes written for a page in parts; HEAD keeps it with no body; the second compressing request of the same room page compresses nothing (a counter on the provider) and is byte-equal; gzip and br kept apart; `Vary: Accept-Encoding` |
| P2 | allocation bound: the warm room page of 40 fragments, rendered and written in process, allocates under 300 KB (S4: 150 KB); one string per page would be 1.6 MB |
| T1 | a `textarea` value (custom styles, a bot's or a message's edit form) starting with `\n`, holding `</textarea>` and `&lt;`: AngleSharp's `value` equals the input |
| T2 | the lint's void-element rule, and the loose DOM of every template with a custom element shows its children inside it |
| HT1 | hostile structure: for every oracle case whose input holds one of `<script>alert(1)</script>`, `"quoted"`, `it's`, `a<b>c`, `&amp; already`, `&lt;`, `a -> b`, U+E000, the same case with each of those strings replaced by `x` renders the same element tree and the same attribute names (AngleSharp), and no `script` element, `on*` attribute or `javascript:` URL appears that the `x` render lacks |
| HT2 | every string field of the oracle's inputs, decoded from the render (text or attribute value, AngleSharp), equals the input where the template prints it |
| HT3 | every `Html.trusted` call site is one of the allowlisted makers' values (the lint, W1) and the custom styles of the hostile cases land only inside the `style` element, as Rails writes them |

Kept, re-pointed: `HelpersTests`, `MessagesTests`, `UsersTests`, `RoomsViewsTests`, `MessagesViewsTests`, `SearchesViewsTests`, the App
tests that check pages (`RoomsTests`, `RespondTests` X4, `ControllersATests`, `BroadcastsTests`). Retired: `OutTests`, `TagTests`,
`RecordedTests`, `FragmentCacheTests`, `FormsTests` (writer), `DifferentialTests` (becomes O1), `DomTests` (becomes `LooseDom` on
AngleSharp; the html5ever tokenizer is Phase 5's to delete).

## 6. What is deleted

`Core/Out.fs` (`Out`, `Utf8`, `SafeBytes` (the writer's byte HTML), `Render`, `RenderSize`; `RecordedPage` already in 4.2), `Core/FragmentCache.fs` (the byte-bounded
store, `KeyBuf`, the template digests), the old `Core/Fragment.fs` (`Fragment`, `IFragmentAttachment`), `Core/Legacy.fs` (after
use), `Helpers/Tag.fs`, `Helpers/Filters.fs`, the writer halves of `Forms`, `Links`, `Assets`, `Application`, `UsersHelper`,
`RoomsHelper`, `Turbo`, `Templates/Layouts/ApplicationWrapper.fs`, `Layouts.fs`'s writer parts; in the App `Web/PageParts.fs`
(`Generations`, `BodyDigest`, `PageParts`, the text-digest caches), `FragmentGlue.fs`, `Pages.fs`'s `PageCompression` map and
`recordedAs`, `AppState.FragmentCache` and every `FragmentCache.scoped`/`withCache`, the Jbuilder caches in `Presenter.fs`;
`bin/views-differential`, `bin/views-test-parity`, `reference-tools/views/` (`transcribe.py`, the Rust differential, `generate.py`,
`golden.py`; the F# case runners move into the tests); the tests listed in section 5.

## 7. KLOC projection

cloc 2.10 code lines (`bin/kloc --files`, corrected) at `b632a7c`: `Campfire.Views` **5,792**: templates 3,504, helpers 942 (Tag
261, Forms 183, Application 162, UsersHelper 109, RoomsHelper 45, Turbo 45, Filters 40, Assets 35, Translations 26, Url 22, Links 14),
core 614 (FragmentCache 344, Out 177, ViewContext 69, Fragment 24), the rest 732 (view-models, presentation, JSON, the cached
modules, layouts). App: `PageParts.fs` 384, `FragmentGlue.fs` 21, `Pages.fs` 267, `Presenter.fs` 277.

| | KLOC |
|---|---:|
| Templates: 3.50 at about 0.6 (S3's ports: the message partials 207 -> 87 lines, the sidebar 98 -> 75; denser markup, no literal tables) | 2.10 (from 3.50) |
| Core: ViewContext 0.07, Html 0.04, HtmlOutput 0.06, Fragments 0.06, CachedFragment 0.02 | 0.25 (from 0.61) |
| Helpers: `Markup/` about 0.30, `Helpers/` non-markup about 0.08 | 0.38 (from 0.94) |
| The rest: presentation as Falco (0.14 -> 0.10), the cached modules as fetchers, layouts as a record | 0.67 (from 0.73) |
| **`Campfire.Views` after** | **3.40 (from 5.79; the plan said 4.00)** |
| App: PageParts, FragmentGlue, the compression map, the JSON caches, the cache scoping out (-0.48); the page writer and up-front fetches in (+0.10) | -0.38 |
| **Phase 4 net** | **about -2.8** |

Project: **34.8 -> about 32.0 KLOC** (range 31.6-32.6: the template ratio is the uncertainty, 0.5-0.7), against the plan's row
30.8 -> 29.0; the base is 4.0 higher than the plan's from the earlier phases' drift, the drop is a little larger than the plan's 1.8.
Rust's bar is 31.0: this phase alone does not reach it; Phase 5 (rich text, -4.8 projected) does.

## 8. Known differences this phase records (README "Known differences", plan section 4)

- **Byte-level (class A), one entry:** whitespace between elements where it does not render is gone (pages about 10% smaller: the
  room page's fragments 375 KB of text against 414 KB); non-ASCII characters and `'` are numeric character references (`&#233;`,
  `&#128512;`, `&#39;`; Rails writes the characters); void elements end ` />` everywhere (Rails' ERB literals write `<meta ...>`);
  attribute order follows each template's list, which keeps Rails' where the builder copied it; an empty `rooms/refreshes/show`
  is an empty body (Erubi wrote a newline); the frame layout has no indentation; `Content-Length` values change with all of this.
- **Internal, not recorded in the README:** the fragment cache is HybridCache's memory cache (eviction by `MemoryCache` compaction,
  not `MemoryStore`'s prune to three quarters; entries expire after 7 days; keys have no template digest); JSON is not cached.
- **No class B entry.** Anything visible (a lost space, a missing `data-*`, an aria name run together such as "Ping withKevin") is
  class C and fixed in the unit that made it.

## 9. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | A space lost or added between inline siblings (pixels, aria names, loose DOM) | O1/O2 in the unit (the loose comparator names the line); `--fast` on the unit's states; S3's two templates as the worked example |
| 2 | An attribute value written raw (XSS) | the lint fails the build (W1 proves it bites); HT1/HT2 on hostile inputs; the XSS review |
| 3 | `Trusted` made from unsanitized text | the lint's allowlist of three files; the review reads each maker's single call site |
| 4 | A page rendered as one string after all (a helper that `renderNode`s fragments into text) | P2's allocation bound; the bench's CPU per request on room and messages pages (S4: +130-180 us) |
| 5 | Slot misuse: a fragment missing, doubled or forged | O4 throws in tests; S6's hostile case in O4 |
| 6 | HybridCache copies fragments on every hit (the immutability attribute dropped, or `byte[]` used as the value) | F1's same-instance assertion; allocation in P2 |
| 7 | L1 unbounded (no `SizeLimit`) or entries expiring every 5 minutes (the default) | F1's size and options assertions; a room page that re-renders its messages every few minutes shows as a CPU step in a long bench run |
| 8 | Sync-over-async: a template or helper calls the cache and blocks on `.Result` (thread-pool starvation at c=16) | the lint's HybridCache rule; the fetchers' only callers are presenters, controllers and broadcasts in `task { }`; templates take fragments as arguments |
| 9 | A fragment that depends on the request (a broadcast's host, the current user), then served to others | F3; the oracle's cold/warm comparison per page case |
| 10 | The leading newline of a `textarea`/`pre` value dropped | T1; `account/custom_styles` and `interactions/edit_form*` states |
| 11 | A custom element written self-closing, swallowing its siblings | the lint (T2); the loose DOM shows the wrong nesting at once |
| 12 | Sidebar c=1 (7.99x) or post (6.20x/6.69x) slipping toward the floor | `bench/quick` in 4.2, 4.3, 4.4 (post), 4.6 (sidebar) against a same-session control; S4 predicts -2.4% on the sidebar and -0.7% on post |
| 13 | More allocation per request (150-200 KB a page) turning into GC time under 16 connections | the CPU-per-request column of every bench; `bench/breakdown alloc` if a page loses more than 10% against its control |
| 14 | The seam (V12) leaking into the final code, or the layout unit too big for 90 minutes | 4.11's grep; 4.10 splits into the layout and the page-module collapse if the first half runs over |
| 15 | The frozen oracle not covering a template (an op the sample skipped) | 4.1 prints cases per op; every op that has a template has hostile cases, the checkpoint checks the table |
| 16 | `LooseDom` drifting from `loose.ts` (the browser layer passing what tests fail, or the reverse) | W2 on stored `loose.ts` outputs; any harness loose-layer failure the tests did not predict is a W2 case to add |
| 17 | Harness flakiness read as a view regression (realtime slices, Chromium OOM in the 6 GB VM) | one compare at a time, `realtime/**` one state per run, compare an error with the Phase 3 gate logs before debugging, re-run with `--only` |
| 18 | Disk during the full matrix (screenshots for every engine and viewport) | `df -h /` before each slice; `bin/clean-parity` between slices; stop at 12 GB |

## 10. Questions for Chris

None. Falco.Markup (D2/D9), the encoding rule (V2: Falco's own encoder, class A bytes), the fragment cache (HybridCache, the
decisions file's idiom), keeping compressed pages and writing pages in parts (the 5x floor, unit 2.8's evidence and S4), dropping the
Jbuilder caches (invisible, least code) and freezing the differential's answers as test data (the plan retires the live tool, not the
evidence) are settled by D9, the floor and the plan.
