# Design note: rich text on AngleSharp (Phase 5)

Status: design, 2026-10-22, before Phase 5 builds. Author: the Opus designer (plan section 5). Binding for the Phase 5 builders
together with `plans/dotnetway.md` sections 3-5, D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`) and
`plans/design-views.md` (the `Html.Trusted` rule that carries rich text into a page). Where this note and plan section 2.2 disagree,
this note wins: it is later and rests on the spikes in section 2. The disagreements, each argued below:

1. **No HtmlSanitizer.** Configured to Loofah's rules it needed five handlers of our own and still differed from Rails on 15 more
   corpus presentations and on 78-85 sanitizer outputs per list; Loofah's scrubber written as a 60-line walker over AngleSharp's DOM
   matched the port on every input that parses alike (R4, S4).
2. **A body is parsed as an AngleSharp document after `<body>`, not with `ParseFragment`.** AngleSharp's fragment algorithm keeps an
   older fragment-case rule (foreign content never breaks out in a fragment) and moves the top-level nodes in quadratic time; its
   document parse has neither defect (R2, S2, S3).
3. **Three caps besides Gumbo's limits** (10,000 elements, 40,000 tokens, 400 attributes on `<html>`/`<body>` tags), because
   AngleSharp's tree builder is quadratic on three hostile shapes (R3, S3).
4. **Autolink links text inside `pre` and `code`**, as rails_autolink does; the plan's "outside `a`, `pre`, `code`" was wrong (R6).
5. **`RubyUri.fs` moves to the App unchanged** for the integrations, which Phase 8 replaces under its SSRF and recorded-case gates; rich
   text itself moves to `System.Uri` (R8, R9).
6. **About 1.7 KLOC, not 1.4** (section 7): Loofah's URI rule, the Lexxy style rule and Action Text's attachment code stay.

Scope: `Campfire.RichText` (all of it: `Html5ever/`, `Dom.fs`, `Sanitizer.fs`, `RubyUri.fs`, `RubyExt.fs`, `Content.fs`,
`Attachables.fs`, `PlainText.fs`, `Filters.fs`, `Autolink.fs`, `ActionText.fs`, `Error.fs`), its tests, the App's call sites
(`Presenters/Presenter.fs`, `Controllers/Messages.fs`'s `canonicalBody`, `RichText.fs`, `Presenters/RichText.fs`), and the two
helpers in `Integrations/OpengraphMetadata.fs` that use the old DOM and sanitizer (`stripTags`, `sanitizeHtml`). Retired with it:
`bin/richtext-differential` and `reference-tools/richtext/differential/` (they compare with the Rust crate). Out of scope: the
integrations' URL parsing and the libxml2-style `OpengraphDocument` scanner (Phase 8); the views (Phase 4's `Trusted.ofSanitized`
is unchanged); the Rails corpus generator `reference-tools/richtext/{run.sh,generate.rb}`, which stays as the oracle.

**Preconditions.** Phases 1-4 closed (Phase 4 verifier `b2322f3`): `Trusted.ofSanitized` is the only way rich text reaches a page
and rewrites U+E000 to `&#57344;` (`src/Campfire.Views/Core/Html.fs`); `tests/Shared/LooseDom.fs` exists on AngleSharp (1.8.3 in
the test projects); pages read message bodies from cached fragments (`Presenter.MessageFragment`), so rich text runs only on a
fragment miss and on posting. If a piece is missing, the first unit that needs it stops and reports.

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity or the 5x floor forces it), with
the reason.

| # | Decision | Reason |
|---|---|---|
| R1 | **AngleSharp 1.8.4**, pinned in `Directory.Packages.props` for `src` and the tests (the tests move from 1.8.3). No HtmlSanitizer, no AngleSharp.Css. | S1: 1.8.4 (2026-10-07) is the newest release at least two weeks old (1.8.5 is from 2026-10-15); the only AngleSharp advisory (GHSA-pgww-w46g-26qg, mXSS through `annotation-xml`) is fixed since 1.5.0. HtmlSanitizer 9.2.1039 has no open advisory either, but is not used (R4). |
| R2 | **One parsing module, `Html5`** (`src/Campfire.RichText/Html5.fs`): a single `HtmlParser` built once with a shared `BrowsingContext` (`BrowsingContext.New(Configuration.Default)`) and `HtmlParserOptions(IsScripting = false)` (Gumbo's and the port's setting). **A body-context parse** (Nokogiri's `Document#fragment`, which every load, wrap and sanitize uses) is `parser.ParseDocument("<body>" + html)` with a leading U+FEFF dropped first; the document's `body` element is the container, used in place (no node is moved out). **An element-context parse** (Nokogiri's `node.inner_html=` and `node.replace(html)`, only ever given markup the pipeline built: partials, `"<div>" + inner + "</div>"`, `"@Name"`, `"[caption]"`) is `parser.ParseFragment(html, context)`. After either, a `template`'s `Content` children are moved into the element (Gumbo keeps them as children). A body whose parse turns the body into a `frameset` (a leading `<frameset>`, which the fragment algorithm ignores) is parsed again with `ParseFragment` in a `body` context. **Serialization** is Nokogiri's HTML5 rules over AngleSharp's `HtmlMarkupFormatter.OpenTag`/`CloseTag`: the spec's fragment serialization without the newline AngleSharp adds after `<pre>`, `<textarea>` and `<listing>`, and with the text of `style`, `script`, `xmp`, `iframe`, `noembed`, `noframes`, `plaintext` and `noscript` written raw (about 30 lines). | S2: `ParseFragment` leaves `<svg><p>y</p></svg>` as an SVG `p`, where the document parse, html5ever and Gumbo (the corpus) break out to an HTML `p` (AngleSharp keeps a fragment-case exception they do not have); with it, 79 of the 658 corpus bodies parsed to other trees than the port's; as a document after `<body>`, 12 (one `noscript`, seven `select` fuzz bodies, `malformed select`, three limit boundaries), and none of the 164 seed bodies. S3: `ParseFragment` moves its top-level nodes into the context copy one at a time (quadratic: 20,000 `<p>x</p>` 4.7 s, 10,000 `x<br>` 2.7 s); the document parse of the same takes 4 ms. With AngleSharp's own `ToHtml`, 56 of 1,479 inputs serialized to a different DOM than Nokogiri's; with the 30-line serializer, 9 (S2). S6: the shared context takes a parse from 10.4 us to 3.7 us. |
| R3 | **Limits, enforced while parsing, through `HtmlParserOptions` callbacks.** Gumbo's two: tree depth 400 (`OnCreated`: the element created before this one is in the tree by now; its element ancestors below the container are counted, a void element skipped) and 400 attributes on a tag (`OnToken`, or `ShouldEmitAttribute` if it counts before AngleSharp drops duplicates, as Gumbo does). Three of ours: **10,000 elements** (`OnCreated`), **40,000 tokens** (`OnToken`), **400 attributes summed over `<html>` and `<body>` start tags** (`OnToken`). A callback raises `LimitExceeded`, which aborts the parse and becomes `ParseError` (as the port's `TreeDepthExceeded`/`TooManyAttributes` do): the message renders empty, its plain text is empty and logged. | Gumbo refuses past its limits and Rails renders ""; the port reproduced it inside its tree builder. AngleSharp has no limit, and its tree builder is quadratic on three shapes Gumbo builds in linear time (S3): elements foster-parented out of a table (200,000 `<br>` after `<table>`: 18.6 s), text split by ignored tags (160,000 `x</q>`: 2.9 s) and attributes merged onto the root from `<html>` tags (200 tags of 400 attributes: 6.5-6.9 s). The caps bound the worst parse to about 260 ms (9,000 foster-parented `<br>`) and refuse 16 MB of hostile tags in under 10 ms (16 MB of plain text parses in 32 ms); a long legitimate message (8,000 paragraphs) parses in 12 ms and stays under every cap. Aborting from the callback stops reading the input at once (16 MB after 401 `<div>`s: 4.5 ms), which the port's hardening tests require. |
| R4 | **The sanitizer is Loofah's `PermitScrubber` as a walker over AngleSharp nodes** (`Sanitizer.fs`): children first, then the node; an element outside the tag list is unwrapped (its children moved before it), a comment removed, a foreign (SVG, MathML) element removed with its contents; an allowed element's attributes are visited in order with the port's rules (outside the list or a URI attribute failing `allowed_uri?`: removed; a blank `src`: removed; after each kept attribute, `force_correct_attribute_escaping` over all of the element's URI attributes), then Lexxy's `style` rule. The data and the string rules (`SafeList`, `SafeListData`, `allowed_uri?`, the escaping, the colour rule) move out of the old file unchanged into `Loofah.fs`. **HtmlSanitizer is not used.** | Rails' sanitizer is Loofah, and parity is the bar: the same DOM, and nothing Loofah strips surviving. S4 configured HtmlSanitizer 9.2.1039 with Loofah's tags, attributes, URI attributes and schemes, `KeepChildNodes`, and handlers for what it does differently: Loofah's URL verdict through `FilterUrl` (it overrode HtmlSanitizer's own 83-102 times per list), Loofah's escaping through `PostProcessNode`, foreign elements and template contents in a pre-pass. Its CSS still rewrote Lexxy's `style` values (AngleSharp.Css re-serializes declarations) and its order-free URL check cannot see Loofah's order-dependent escaping (`href=" jav\tascript:x"` passes in Loofah after an earlier attribute re-escaped it): 15 more corpus presentations than the walker differed from the port, and 78-85 sanitizer outputs per style-bearing list. Every rule that decides parity was ours anyway; the walker is the scrubber itself. AngleSharp does the job a library can do here: parsing, the DOM and serialization (the 3.6 KLOC html5ever port and the 0.45 KLOC arena go). |
| R5 | **No DOM type of our own.** `Content = { Root: IElement }` (the container), `NodeId` becomes `INode`/`IElement`, and the helpers the pipeline needs (`descendants`, `attr`, `setInnerHtml`, `replaceWithHtml`, `clone`, `isNamed`) are functions in `Html5`. **The Ruby round trips stay where the port has them** (serialize, then parse in the same context: `replace(html)`, `inner_html=`, a filter returning HTML, each sanitizer pass). | They shape the output (a `<figure>` rendered inside a `<p>` closes it only on the next body parse). With them kept, the AngleSharp pipeline matched the port on 655 of 658 corpus presentations (S5). Speed does not ask for fewer (S6: a presentation without the linking step costs 40.9 us against the port's 52.5); dropping one later is allowed only with the corpus test green. |
| R6 | **Autolink on the DOM, rails_autolink's matchers kept.** Every text node with no `a` ancestor (inside `pre` and `code` too: rails_autolink links there) is escaped as Nokogiri would serialize it (`&amp;`, `&lt;`, `&gt;`, `&nbsp;`); the existing matchers run over that string (`urlMatchAt`, `emailMatchEnd`, the trailing punctuation and bracket rules, the trailing `&gt;`, the `sanitize` of link text and `href` with the default list); the result is a list of text and link segments, each decoded back (the four references plus the `&#39;` and `&quot;` that `Erb.htmlEscape` writes in trailing punctuation), and the text node is replaced by text nodes and `<a target="_blank" href="...">` elements built with `CreateElement`/`CreateTextNode`. `TagIndex` and the escaped-brackets serialization go. | rails_autolink scans the serialized HTML with regular expressions; a match can never cross a tag, so per text node on its serialized form is the same scan. The port's stored-XSS fix (a URL after `>` in an attribute value) holds by construction: no string is spliced into markup. S5 ran the port's linker per text node this way: the presentations matched the port's on every case whose input parses alike, the attribute-value XSS cases of `HardeningTests` included. Inside an unclosed `<a>` (`auto_linked?`'s `rindex`) is "has an `a` ancestor". |
| R7 | **The public surface keeps its shape.** `ActionText.presentMessage`/`messagePresentation`/`toPlainText`/`editableValue`/`mentionedUsers`/`withoutRecipientMentions`, `Content.load`/`toHtml`/`toRenderedHtmlWithLayout`, `Presentation`, `RenderError`, `IAttachableResolver`, `MentionUser`, `GidLookup`, `SignedLookup`, `RenderContext`, `Attachables.webUrl`, `SafeList`, `Sanitizer.sanitize: string -> SafeList -> Result<string, ParseError>`. The presentation stays a `string` handed to `Trusted.ofSanitized`; `<` and `>` in attribute values are written raw (the spec's serialization), and the U+E000 rewrite in `ofSanitized` keeps the slot marker unforgeable. | The App changes by `open`s only, so the flip is one small unit. Phase 4's verifier condition: `Trusted.ofSanitized` stays the only way in, and the sanitizer's output type does not change, so the markup lint's planted files need no change (`bin/verify` runs `bin/lint-markup --self-test` anyway). The port escaped `<` in attributes only so its regex linker could not mistake an attribute for text (R6 removes the reason); the marker defence never relied on it. |
| R8 | **URIs in rich text are `System.Uri`** (`Uri.TryCreate(value, UriKind.Absolute)`, `IdnHost`) behind an RFC 3986 character check (ASCII printable only, no `"<>\^`{|}`, at most one `#`, every `%` followed by two hex digits): `web_url` and the tweet URL normalisation of `RemoveSoloUnfurledLinkText`. | S7: of Rails' 54 `web_url` vectors, `System.Uri` alone agrees on 44, with the 8-line check on 47. The 7 left are URLs only a hand-made body carries (Ruby accepts `https://example..com/`, `https://.example.com/`, `https://[v1.x]/`, a 20-digit port, a lone `%z`; Ruby raises on `mailto:nobody` and `https://.../x`): class B, the plan's "Ruby's URI.parse error classes go". |
| R9 | **`RubyUri.fs` moves, unchanged, to `src/Campfire.App/Integrations/RubyUri.fs`** with its tests, for the webhook, push and opengraph fetch/location/metadata code. Phase 8 replaces it with `System.Uri`. | Its remaining users are integrations, whose URL parsing feeds the SSRF guard (`Location`, `Guard.fs`): changing it belongs with Phase 8's guard tests and recorded cases, not with rich text. `System.Uri` and Ruby disagree on hostile URLs in ways that matter there (backslashes become slashes). |
| R10 | **`OpengraphMetadata.stripTags` and `sanitizeHtml`** (Rails' `strip_tags` and `sanitize` on unfurl titles and descriptions) move onto `Html5` and the new `Sanitizer`. | They are the only App code that builds the old DOM; the scanner that reads the page (`OpengraphDocument`) is Phase 8's. |
| R11 | **Oracles.** Rails' corpus (`tests/Campfire.RichText.Tests/corpus/expected.json`: 658 bodies, 54 `web_url` values, made by the reference image) is compared as the loose DOM (`tests/Shared/LooseDom.fs`) for markup, exactly for plain text, mentions and `web_url`, with the port's deliberate divergences applied to Rails' side as now. Every case allowed to differ is listed in `corpus/known-differences.json` (case, kind, class, reason); the test fails on an unlisted difference and on a listed case that no longer differs. While both pipelines exist, a **migration differential** in `bin/verify` compares the new modules with the old on the 1,479 inputs of S2 (it goes with the old code). | The plan retires the differentials against Rust; Rails' own answers are the better oracle and already cover the seeds' shapes, Lexxy's and Trix's markup, attachments, galleries, XSS attempts, limits and 400 fuzz bodies. S2/S5 showed loose comparison of serializations can hide a namespace difference (an SVG `p` and an HTML `p` serialize alike), so the parse oracle compares trees with namespaces, not serializations. |
| R12 | **Speed rule for the phase.** Rich text runs on a fragment miss and on posting; the room, messages and search pages read cached fragments. The flip unit and the gate run `bench/quick` on post a message and the three pages against a same-day control image; a cell under 5x Rails blocks. The parser and its `BrowsingContext` are built once (R2). | S6 (seed bodies, one thread): a presentation costs 40.9 us before linking against the port's 52.5 us, plain text 6.6 against 2.8, the canonical load 5.1 against 1.8; allocation about 3x the port's. A post runs the canonical load once, plain text two or three times and one presentation: about +10 us of CPU on 1,465-1,523 us per post at c=16 (6.04-6.20x Rails), well inside the floor's margin. A per-request document (no shared context) would add about 40 us. |
| R13 | **Migration seam:** the new modules live in `src/Campfire.RichText/Next/` (namespace `Campfire.RichText.Next`) beside the old until the flip unit points the App at them; the delete unit removes the old ones and moves `Next/` back to the root namespace. DOM-independent code is shared, not copied: `Loofah.fs` (R4) and the attachable types, the resolution rules and the partial renderers (`Attachables.fs` taking an attribute lookup `string -> string voption` instead of a `Dom` and a `NodeId`). | Phase 2's and 4's seam pattern: the app stays pixel-identical after every unit, and the old pipeline is the in-process oracle for the new one until the flip. |

What stays hand-written, and why (parity or the floor): the allowlists (data), Loofah's URI rule and attribute escaping, Lexxy's
colour rule, the scrubber walk, the depth/attribute limits and the three caps, the Nokogiri serializer rules, the Action Text
canonicalisation and attachment rendering, the partials, rails_autolink's patterns, plain-text conversion.

## 2. Spikes (scratch project, 2026-10-22)

`/private/tmp/claude-501/-Users-clank-Desktop-projects/fea4ed62-8823-4b82-bed5-fc54b6214d57/scratchpad/rtspike/`: an F# console
project on .NET SDK 11.0.100-rc.2 (net11.0, Release), referencing the repo's `Campfire.RichText` (the port, as the comparison) and
linking `tests/Shared/LooseDom.fs` and `tests/Campfire.RichText.Tests/CorpusResolver.fs`; AngleSharp 1.8.4 (1.8.5 for one check),
HtmlSanitizer 9.2.1039, AngleSharp.Css 1.1.2. A prototype of the whole pipeline on AngleSharp (`Proto.fs`, about 700 lines: the
parse and limits, both sanitizers, canonical load with Trix conversion and galleries, attachment rendering through the port's
attachable resolution, filters, plain text, linking per text node through the port's linker). **Removed after the spikes**; the numbers
are here. Apple M-series Mac, load average 1.3-1.7, one thread. Inputs ("the 1,479"): the corpus's 658 bodies, the 164 non-empty
bodies of the `default` seed (read-only), and Rails' 657 presentations.

| # | Question | Result |
|---|---|---|
| S1 | Versions and advisories (NuGet registration API, GitHub advisories, 2026-10-22) | AngleSharp 1.8.4 (2026-10-07), 1.8.5 (2026-10-15); HtmlSanitizer 9.2.1039 (2026-08-28, AngleSharp >= 1.7.2, AngleSharp.Css >= 1.0.2), 10.0.1102-beta; AngleSharp.Css 1.1.2 (2026-09-11), 1.1.4 and 1.2.0 (2026-10-20/21). Advisories: AngleSharp GHSA-pgww-w46g-26qg (< 1.5.0, fixed); HtmlSanitizer GHSA-j92c-7v7g-gj3f (template bypass, < 9.0.892), GHSA-43cp-6p3q-2pc4 (foreign content, < 8.0.723), GHSA-8j9v-h2vp-2hhv (< 5.0.372): all fixed; AngleSharp.Css none |
| S2 | Parse and serialize, against the port (html5ever + Nokogiri's serializer) | `ParseFragment` (body context) + AngleSharp's `ToHtml`: 1,381 byte-equal, 40 loose-equal only, 2 both refused, **56 different**. With the Nokogiri serializer (no `<pre>` newline, raw `noscript`) and templates flattened: 1,395 / 44 / 5 / 35, and comparing trees with namespaces **79 of 658 corpus bodies** differ (all but five fuzz bodies; the foreign-content breakout: `<svg><p>y</p></svg>` gives `svg(p)` in `ParseFragment`, `svg(), p` in `ParseDocument`). **As a document after `<body>`: 1,463 byte-equal, 3 loose-equal only, 4 both refused, 9 different; trees differ on 12 of 658 corpus bodies** (`malformed select` and seven fuzz bodies, all `<select>` content parsed by the 2025 standard's rules; fuzz 63, `noscript`; three limit boundaries) **and 0 of 164 seed bodies** |
| S3 | Cost on hostile shapes (parse only) | `ParseFragment`: quadratic in top-level nodes (10,000/20,000 `<p>x</p>` 1.35/5.2 s; 10,000 `x<br>` 2.7 s; 1.8.5 the same; the `InnerHtml` setter, which moves them in one go, 30/64 ms, but takes no options). Tree builder (any mode): `<table>` + 50,000/100,000/200,000 `<br>` 1.4/4.7/18.6 s (port 21-32 ms); 80,000/160,000 `x</q>` 0.65/2.9 s; 200 `<html>` tags x 400 attributes 6.5 s (clearing the token's attributes in `OnToken` does not help). With R3's limits and caps: every hostile shape of the port's `HardeningTests` refused in 1.5-6 ms (80,000 `<div>`, 80,000 `<a><b>`, 30,000 `<a><div><div>`, 401 `<div>` + 16 MB, a tag with 64,000 attributes 36 ms); the worst under the caps 257 ms (9,000 foster-parented `<br>`); 16 MB of plain text 32 ms; 8,000 paragraphs 12 ms |
| S4 | Sanitizer: the port vs A (walker) vs B (HtmlSanitizer as configured in R4), on the 1,479, body parsed as a document | A differs from the port on 11 (default list) or 12 (Action Text, content filter, autolink lists) inputs, every one an input whose tree already differs (S2). B differs on 18 / 97 / 90 / 22: the CSS rewrite of `style` (`color: red` and Lexxy's `var(--highlight-1)` re-serialized or dropped), and order-dependent escaping (fuzz 120, 244, 250: `href="%20jav\tascript:x"` kept by Loofah, removed by B) |
| S5 | The pipeline on AngleSharp against Rails' corpus answers and the port (resolver: `CorpusResolver`) | **A, document parse: presentations loose-equal to the port's 655 of 658** (the three limit boundaries; the port's own 37 deliberate differences from raw Rails are the same 37); **plain text equal to Rails' 653 of 658** (three limit boundaries, fuzz 63, fuzz 362); the 164 seed presentations loose-equal 164 of 164. B: 640 of 658. A with `ParseFragment`: 615 and 627. Every presentation difference is an input whose tree differs (none comes from the pipeline) |
| S6 | Cost per body (164 seed bodies, 30 rounds after warm-up, shared `BrowsingContext`) | port presentation 52.5 us, 39 KB; AngleSharp presentation without the linking step 40.9 us, 119 KB (the spike's linking step re-parsed through the port's linker: 154 us, not the design); filters alone 12.1 us; one Action Text sanitize 4.8 us (port 1.1); plain text 6.6 us, 21 KB (port 2.8, 9 KB); canonical load + `toHtml` 5.1 us, 18 KB (port 1.8, 6 KB); an empty document 1.6 us. Without the shared context: a parse 10.4 us instead of 3.7 |
| S7 | `web_url` on `System.Uri` (Rails' 54 vectors) | 44 of 54 alone; 47 of 54 with the RFC 3986 character check (R8). The 7 left in R8 |

## 3. Types and modules

Names are binding; bodies are the builder's. `Campfire.RichText` compile order at the end of the phase: `Unicode/WordRanges.fs`,
`Unicode/Chars.fs`, `RubyExt.fs`, `Error.fs`, `Html5.fs`, `Loofah.fs`, `Sanitizer.fs`, `Attachables.fs`, `PlainText.fs`,
`Content.fs`, `Filters.fs`, `Autolink.fs`, `ActionText.fs`. During the migration the new files sit under `Next/` (R13) after the old
ones; `Loofah.fs` and the DOM-independent `Attachables.fs` are shared from 5.2 and 5.3 on.

```fsharp
namespace Campfire.RichText

// Error.fs: ParseError keeps its two cases and gains the caps (the port's Dom.fs defined it).
type ParseError =
    | TreeDepthExceeded
    | TooManyAttributes
    | TooManyElements        // > 10,000 (R3)
    | TooManyTokens          // > 40,000
    | TooManyRootAttributes  // > 400 over <html> and <body> start tags
// RenderError, ResultCe: unchanged.

// Html5.fs (about 140 lines)
module Html5 =
    [<Literal>] val MaxTreeDepth: int = 400
    [<Literal>] val MaxAttributes: int = 400
    [<Literal>] val MaxElements: int = 10_000
    [<Literal>] val MaxTokens: int = 40_000
    [<Literal>] val MaxRootAttributes: int = 400
    /// Nokogiri's `Document#fragment(html)`: a document parse after "<body>"; the body element is the container (R2).
    val parseBody: html: string -> Result<IElement, ParseError>
    /// Nokogiri's `Node#fragment`: `html` parsed in `context`'s context; the nodes, detached, owned by context's document.
    val parseIn: context: IElement -> html: string -> Result<INode[], ParseError>
    val setInnerHtml: element: IElement -> html: string -> Result<unit, ParseError>     // inner_html=
    val replaceWithHtml: node: INode -> html: string -> Result<unit, ParseError>        // replace(html), parent's context
    val toHtml: node: INode -> string            // Nokogiri's serialization of the node
    val innerHtml: node: INode -> string         // of its children (a container's markup)
    val descendants: root: INode -> INode list   // document order, root excluded
    val attr: node: INode -> name: string -> string voption
    val isNamed: node: INode -> localName: string -> bool
    val clone: root: IElement -> IElement        // deep, same document (the port's Dom.Clone)

// Loofah.fs (moved from Sanitizer.fs in 5.2, string rules only)
[<Sealed>] type SafeList = (* unchanged: Tags, Attributes, AllowsTag, AllowsAttribute *)
module SafeListData = (* unchanged *)
module SafeList = (* defaults, actionText, contentFilter, autoLink: unchanged *)
module Loofah =
    val allowedUri: uri: string -> bool                      // Loofah::HTML5::Scrub.allowed_uri?
    val needsEscaping: value: string -> bool                 // force_correct_attribute_escaping!'s trigger
    val escapeAttributeValue: value: string -> string
    val scrubStyle: style: string -> string option           // Lexxy's colours; None removes the attribute
    val cgiUnescapeHtml: string -> string                    // kept unless 5.2 proves WebUtility.HtmlDecode gives the same verdicts

// Sanitizer.fs (about 60 lines)
module Sanitizer =
    val scrub: root: INode -> list: SafeList -> unit         // in place, children of root (R4)
    val sanitize: html: string -> list: SafeList -> Result<string, ParseError>   // parseBody, scrub, innerHtml

// Attachables.fs: the types (MentionUser, GidLookup, SignedLookup, IAttachableResolver, RenderContext, OpengraphEmbed, Attachable,
// Attachment, PlainTextRepresentation) and the partials unchanged; every function that read a Dom node takes the node's attributes:
type Attrs = string -> string voption
module Attachables =
    val attachmentFrom: attrs: Attrs -> ctx: RenderContext -> Result<Attachment, RenderError>
    val actionTextAttachableFrom: attrs: Attrs -> ctx: RenderContext -> Attachable
    val opengraphEmbedFrom: attrs: Attrs -> ctx: RenderContext -> Result<OpengraphEmbed option, RenderError>
    val webUrl: value: string voption -> requestHost: string -> Result<string option, RenderError>   // System.Uri (R8) from 5.6
    val attrsOf: element: IElement -> Attrs                  // AngleSharp; the old pipeline passes `fun n -> dom.Attr(node, n)`

// Content.fs
type Content = { Root: IElement }
module Content =
    val attachmentNodes: root: INode -> IElement list
    val load: html: string -> ctx: RenderContext -> Result<Content, RenderError>        // canonicalized
    val wrap: html: string -> Result<Content, RenderError>
    val toHtml: Content -> string
    val toPlainText: Content -> RenderContext -> Result<string, RenderError>
    val render: Content -> RenderContext -> Result<string, RenderError>
    val renderAttachmentHtml: Attachment -> RenderContext -> Result<string, RenderError>
    val toRenderedHtmlWithLayout: Content -> RenderContext -> Result<string, RenderError>

// PlainText.fs: nodeToPlainText: INode -> string. Filters.fs: apply: Content -> RenderContext -> Result<Content, RenderError>
// (normalizeTweetUrl on System.Uri from 5.6).

// Autolink.fs (about 200 lines): the matchers unchanged; TagIndex goes.
type Segment = Text of string | Link of href: string * text: string   // decoded
module Autolink =
    val linkText: text: string -> Segment list               // rails_autolink over one text node's escaped form
    val autoLink: html: string -> list: SafeList -> Result<string, ParseError>   // sanitize, link every text node outside <a>, innerHtml

// ActionText.fs: unchanged signatures (R7).
```

App: `Presenters/Presenter.fs`, `Controllers/Messages.fs` and `RichText.fs` change `open`s only (5.6, then back in 5.7);
`AppRichText.ParseSafeLength` stays 800, its comment re-argued for the new limits (800 UTF-16 units hold at most 266 elements and
no tag with 401 attributes, under every limit and cap); `Integrations/OpengraphMetadata.fs`'s `stripTags` becomes `Html5.parseBody`
and the text of every descendant text node, serialized; `sanitizeHtml` calls `Sanitizer.sanitize`.

## 4. Units, in build order

One Sonnet builder per unit, about 90 minutes, from a clean `git status` on `dotnetway`, one commit per logical step (read `git diff
--cached --stat` first), a Phase log entry per unit (KLOC from `bin/kloc`, gates run, differences recorded). **Common acceptance for
every unit:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green (it runs the markup lint and its self-test, H1, the Dapper guard and every
test, the migration differential included while it exists); `df -h /` first (12 GB free, 15 GB before a bench). Units 5.1-5.5 add
code the app does not call yet, so they run no pixel slice; 5.6 and 5.7 run `bin/pixel-parity --fast --only
'rooms/show/**,messages/**,search/**,interactions/edit_form*'` on every seed the globs reach (equal on screenshots, aria and loose
DOM), then `realtime/message_sent` and `realtime/message_edited` one at a time (they broadcast a rendered message); `bin/clean-parity`
after pixel runs, `bin/clean-docker` after image rebuilds. Long runs detached with a status file, polled in the foreground with
bounded loops. The app is pixel-identical after every unit: the old pipeline serves until 5.6.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 5.1 Html5 | AngleSharp 1.8.4 in `Directory.Packages.props` (tests from 1.8.3) and `Campfire.RichText.fsproj`; `ParseError`'s new cases (the old `Dom` maps nothing to them); `Next/Html5.fs`: the parser and shared context, `parseBody` (document after `<body>`, BOM, frameset fallback, templates flattened), `parseIn`, the limits and caps in the callbacks, the Nokogiri serializer, the DOM helpers | P1 (tree equivalence with the old `Dom` on the 1,479: differences exactly S2's 12 corpus bodies plus the Rails outputs and seed bodies they imply, listed by name in the test, 0 seed bodies), P2-P5, L1-L4; `bin/kloc` |
| 5.2 Sanitizer | `Loofah.fs` (SafeList data and the string rules moved out of `Sanitizer.fs`, which keeps its arena walker and calls them); `Next/Sanitizer.fs` (R4); `WebUtility.HtmlDecode` tried in place of `cgiUnescapeHtml` in `allowedUri` and kept only if every URI attribute value in the corpus and hardening inputs gets the same verdict (record the count) | S1-S3; the old `SanitizerTests` green on both sanitizers; no output change of the old pipeline (the migration differential and `CorpusTests` unchanged) |
| 5.3 Attachments, load, plain text, mentions | `Attachables.fs` split: the DOM-independent part takes `Attrs` (both pipelines call it); `Next/Content.fs` first half (`attachmentNodes`, Trix conversion, galleries, `load`, `wrap`, `toHtml`, `toPlainText`), `Next/PlainText.fs`; `Next/ActionText` `toPlainText` and `mentionedUsers` | M1 for canonical bodies (bytes), plain text and mentions; C2 and C3 on the new modules; `AttachablesTests` green on both |
| 5.4 Rendering, editor value, filters | `Next/Content.fs` second half (`render`: attachments with full attributes, galleries, nested content attachments 8 deep, the Action Text sanitize; `renderAttachmentHtml`; `toRenderedHtmlWithLayout`); `Next/ActionText.editableValue`; `Next/Filters.fs` | M1 for `toRenderedHtmlWithLayout` (the bot API's `body.html`) and editor values (loose, exact where the old bytes were exact), filtered content; C4. **Opus checkpoint after this unit** (below) |
| 5.5 Autolink and presentation | `Next/Autolink.fs` (R6); `Next/ActionText.messagePresentation`/`presentMessage`/`withoutRecipientMentions` | M1 for presentations (loose; differences exactly P1's list); C1, N1, N2, A1-A3; `HardeningTests` and `ReferenceTests` run on the new modules as well as the old |
| 5.6 Flip | The App and `Campfire.Db`'s glue on `Next` (`Presenter`, `Messages.canonicalBody`, `AppRichText`, `DbResolver`); `OpengraphMetadata.stripTags`/`sanitizeHtml` (R10); `webUrl` and `normalizeTweetUrl` on `System.Uri` (R8); `RubyUri.fs` and `UriTests` to the App (R9); `CorpusTests` on the new pipeline through `known-differences.json`; README "Known differences" (section 8) | the image rebuilt and labelled; the common pixel slices on every seed; **`bench/quick` post a message, room page, messages page and search** (detached, 3 reps interleaved) against a control image of the unit's base commit, host probe and load recorded, no cell under 5x; App tests for opengraph, webhooks and push green unchanged |
| 5.7 Delete | `Html5ever/`, `Dom.fs`, the old `Content.fs`, `Filters.fs`, `Autolink.fs` (and `TagIndex`), `PlainText.fs`, `ActionText.fs`, the arena walker in `Sanitizer.fs`, `Library.fs`; `TokenizerTests`, `DomTests` (their Gumbo truth table already lives in L1), the migration differential; `Next/*` back to the root namespace and the App's `open`s; `bin/richtext-differential`, `reference-tools/richtext/differential/`; AGENTS.md (the layout row, the `bin/richtext-differential` rule, a rich text paragraph: `Html5`, the limits, the walker, the corpus and its known differences) | `grep -rn "Html5ever\|NodeId\|Dom()\|richtext-differential\|TagIndex" src tests bin AGENTS.md` empty (comments aside); the common pixel slices once more; `bin/kloc` |

Then the **phase gates** (gate runner, plan section 3, the phase prompt): the corpus and hardening suites reported on their own
(`CorpusTests`, `HardeningTests`, N1: the same DOM after sanitizing except `known-differences.json`, nothing Loofah strips survives);
`--fast` and then the **lean** slices `rooms/show/**`, `messages/**`, `search/**`, `interactions/edit_form*` on every seed;
`CAMPFIRE_REQUIRE_SEED=1 bin/verify`; the **lean pixel matrix on all seeds** (every state, slices with `--only`, the 13
`realtime/**` states one at a time on every engine); `bench/quick` all five workloads against a same-day control (the commit Phase 5 starts from, this note's,
rebuilt as `campfire-dotnetway:p4ctl`, label checked, removed after), host probe and load recorded; `bin/db-differential` (posting stores canonical bodies);
`bin/kloc`. Then the **Opus security review** of the rich text layer (section 9's risks 1, 6, 7 and 9: the sanitizer walk against
Loofah's, the URI rule, the serializer against browsers' parsing of our output (mutation XSS), the linker's node building, the
attachment partials' escaping, every `known-differences.json` entry); findings fixed before the phase closes.

**The checkpoint after 5.4** (Opus): reviews 5.1-5.4 against this note: P1's list (nothing beyond S2's), the limits and caps
(L1-L3's numbers, the shared context's L4), the walker against Loofah's scrubber line by line, the `Attachables` split (no rule
changed), M1's results, the cost of a load, a plain text and a render against S6, and whether 5.5-5.7 still fit 90 minutes each.

## 5. Tests

In `tests/Campfire.RichText.Tests` unless named. "The 1,479" are S2's inputs (the corpus's 658 bodies, the 164 seed bodies read from
`parity/.seed/default` when it is built, Rails' 657 presentations); seed-dependent cases skip without the seed, and fail under
`CAMPFIRE_REQUIRE_SEED=1`.

| Id | Test |
|---|---|
| P1 | Parse equivalence while the old `Dom` exists: the tree of every input of the 1,479 (element names with namespaces, attribute sets, text, in order) equals the old parser's, except a named list (S2's 12 corpus bodies and what follows from them); a refusal on one side is a refusal on the other except the limit boundaries in L1. After 5.7 the list and the comparison are frozen as stored trees for those inputs (`corpus/trees.jsonl`, written once by 5.7 from the old parser before it goes) |
| P2 | `parseBody`: a template's contents are its children; a leading U+FEFF dropped, one later kept; a leading `<frameset>` parses as the fragment algorithm does; `<html a=1>` and `<body a=1>` add nothing; comments after `</body>` and `</html>` stay in the container (or, if the document parse moves them, the canonical body test C2 shows Rails' bytes and the entry goes to section 8) |
| P3 | Serializer: `<pre>\n\nx`, `<textarea>\n\nx`, `<listing>` round-trip as Nokogiri's (one newline lost per round trip); `noscript`, `script`, `style`, `xmp`, `iframe`, `noembed`, `noframes`, `plaintext` text raw; `&`, U+00A0, `<`, `>` in text and `&`, U+00A0, `"` in attributes escaped, `<` and `>` in attributes raw; void elements without a closing tag; `xlink:href` and other foreign attribute names as the old serializer wrote them |
| P4 | `parseIn` contexts the pipeline uses (`action-text-attachment`, `div`, `p`, `li`, `figure`, `td`) give the old `ParseNodes`' trees for the partials and plain-text strings of the corpus |
| P5 | No quadratic step left in `parseBody`: 20,000 `<p>x</p>` and 20,000 `x<br>` parse in under 50 ms CPU each (Release) |
| L1 | Gumbo's truth table (the old `DomTests`: 400/401 `<b>`, `<b>` x 400 + `<p>`/`<br>`/`</p>`, 397 + `<table><td>`, closing after being too deep, the adoption agency cases, the reopened `<b>`s in table text; 400/401 attributes, a duplicate, an end tag, an unfinished tag, inside `<textarea>`): equal to Gumbo except a named list of boundary cases, each also in `known-differences.json` |
| L2 | The port's hostile shapes (`HardeningTests`: 80,000 `<div>`, 80,000 `<a><b>`, 30,000 `<a><div><div>`, 401 `<div>` + 16 MB of tags, a comment or a tag name, 64,000 attributes, 550 KB of `<html>` tags, 800 KB of `<br>` in a table) refused or parsed within the existing bounds (CPU time, Release) |
| L3 | The caps: 9,000 `<br>` after `<table>` parses under 400 ms CPU; 10,001 elements, 40,001 tokens and 401 attributes over `<html>`/`<body>` tags refuse in under 50 ms; a legitimate long message (8,000 paragraphs, 2,000-row table) parses |
| L4 | The shared parser: 8 threads x 20,000 mixed parses (seed bodies, refusals among them) give the single-threaded results; managed memory after a full collection within 5 MB of the start (the shared `BrowsingContext` keeps no document) |
| S1 | Sanitizer equivalence while the old exists: for the four lists, the new `Sanitizer.sanitize` on the 1,479 equals the old except inputs in P1's list |
| S2 | `SanitizerTests`' Rails vectors (rails-html-sanitizer outputs from the reference image) on the new sanitizer, compared loose |
| S3 | `Loofah.allowedUri` verdicts on every URI attribute value of the corpus and hardening inputs equal the port's (it is the same code; the test guards the `HtmlDecode` trial) |
| M1 | Migration differential (5.3-5.6, deleted in 5.7): canonical body bytes, plain text, mentions, `toRenderedHtmlWithLayout`, editor values and presentations of the new modules against the old on the 1,479, differences only on P1's list |
| C1 | `CorpusTests` on the new pipeline: presentations loose-equal to Rails' (with the port's divergences), plain text, mentions and editor values as now, `web_url` exact; every difference listed in `corpus/known-differences.json` and every listed case still different |
| C2 | Canonical bodies (what posting stores): the corpus's bodies through `Content.load` and `toHtml` byte-equal to the old pipeline's outputs except P1's list (class A otherwise, listed) |
| C3 | Mentions and plain text of the seed bodies equal the old pipeline's (search index, push and webhook bodies) |
| C4 | Editor values of the corpus loose-equal to Rails' (`editable`); a body with a deleted user's mention leaves the editor without it (the port's difference) |
| N1 | Nothing Loofah strips survives: for every corpus case, each element name, attribute name and URI attribute value in our presentation (and in the bot API's `body.html`) also occurs in Rails' output with the port's divergences applied; a planted output with an extra element, an event attribute or a `javascript:` URL fails it |
| N2 | The security assertions of `CorpusTests` (`securityViolations`) on every new output and on Rails' own; their planted-defect test |
| A1 | `AutolinkTests` and `ReferenceTests`' autolink cases on the new linker; URLs inside `pre` and `code` are linked; nothing inside an `a` |
| A2 | The attribute-value cases (`a_url_after_a_greater_than_sign_in_an_attribute_cannot_break_out_of_it`, the e-mail twin, a `title` holding `"><img src=x onerror=alert(1)>` and a URL): the attribute's value is unchanged and no element is added |
| A3 | Linking adds no parse: a presentation of a seed body allocates no second document for the linking step (a counter on `Html5`'s parses, test-only) |
| X1 | App (`MessagesTests`): posting a message stores the canonical body Rails would, the search index finds its plain text, the broadcast and the next room page show the same message bytes |

Kept, re-pointed: `ReferenceTests`, `HardeningTests`, `AttachablesTests`, `FiltersTests`, `RubyTests`, `CorpusTests` (C1, N2), the
App tests that render messages (`MessagesTests`, `RoomsTests`, `BroadcastsTests`, the opengraph and webhook tests). Moved: `UriTests`
to `Campfire.App.Tests` with `RubyUri.fs`. Retired: `TokenizerTests`, `DomTests` (into L1 and P1), the migration differential.

## 6. What is deleted

`src/Campfire.RichText/Html5ever/` (`Types.fs`, `NamedEntities.fs`, `Tokenizer.fs`, `TreeBuilder.fs`), `Dom.fs` (the arena, its
serializer, `DepthLimit`, `DomContext`), `Library.fs`, the old `Content.fs`, `Filters.fs`, `Autolink.fs` (`TagIndex`,
`sanitizeWithEscapedAttributeBrackets`), `PlainText.fs`, `ActionText.fs` and the arena walker of `Sanitizer.fs`; `RubyUri.fs` leaves
the project (to the App, R9); `bin/richtext-differential`; `reference-tools/richtext/differential/` (the Rust and F# sides);
`TokenizerTests.fs`, `DomTests.fs`; the migration differential (M1, S1 and P1's live comparison, frozen as stored trees).

## 7. KLOC projection

cloc 2.10 code lines (`bin/kloc --files`) at `b2322f3`: `Campfire.RichText` **6,191** (the same in both columns; `WordRanges.fs` is
static data, out): `Html5ever/` 3,582 (`Tokenizer` 1,602, `TreeBuilder` 1,630, `NamedEntities` 297, `Types` 53), `Attachables` 450,
`Dom` 445, `RubyUri` 410, `Sanitizer` 407, `Autolink` 284, `Content` 204, `RubyExt` 130, `ActionText` 76, `Filters` 76,
`PlainText` 59, `Error` 36, `Chars` 31, `Library` 1. The project: **33,734 corrected (33.7 KLOC)**.

| | KLOC |
|---|---:|
| Gone: `Html5ever/` 3.58, `Dom` 0.45, `Library`, `TagIndex` and the escaped-brackets path (about 0.10), the arena walker (about 0.05) | -4.18 |
| `RubyUri` out of the project (to the App, R9) | -0.41 |
| New: `Html5` about 0.14 (parse, limits, serializer, helpers), `Sanitizer` walker about 0.06, `Autolink`'s node building about 0.02 | +0.22 |
| Kept, ported to AngleSharp with little change in size: `Loofah` 0.33 (from `Sanitizer`; 0.27 if `HtmlDecode` replaces `cgiUnescapeHtml`), `Attachables` 0.43, `Content` 0.19, `Autolink` matchers 0.18, `RubyExt` 0.13, `ActionText` 0.07, `Filters` 0.07, `PlainText` 0.06, `Error` 0.04, `Chars` 0.03 | (about 1.53) |
| **`Campfire.RichText` after** | **about 1.75 (from 6.19; the plan said 1.40)** |
| App: `RubyUri` in (until Phase 8 replaces it with `System.Uri`), `OpengraphMetadata`'s two helpers a little shorter | +0.40 |
| **Phase 5 net** | **about -4.0** |

Project: **33.7 -> about 29.7 KLOC** (range 29.4-30.0), under Rust's 31.0 for the first time; the plan's row said 29.0 -> 24.2 from a
base the earlier phases moved up by 4.7, and the Phase 4 verifier expected about 28.9. The 0.8 between them: `RubyUri` stays in the App
until Phase 8 (0.41, gone there), and Loofah's URI rule, Lexxy's colour rule and Action Text's attachment code are parity code the plan's
1.4 underestimated.

## 8. Known differences this phase records (README "Known differences", plan section 4)

A new **Rich text** entry (`Campfire.RichText`, on AngleSharp; reference `ActionText::Content`, rails-html-sanitizer and Loofah,
rails_autolink, Nokogiri's Gumbo), with `corpus/known-differences.json` as its exact list:

- **Class A, byte-level:** the presentation writes `<` and `>` inside attribute values as themselves (the port wrote `&lt;` and
  `&gt;`; Rails writes them raw too). Serialization is otherwise Nokogiri's (S2: 1,463 of 1,479 inputs byte-equal).
- **Class B, the parser follows the current HTML standard where Gumbo is older:** `<select>` contents (a `<b>` or an `<hr>` inside an
  `<option>`) are kept as the 2025 standard and current browsers keep them: the corpus's `malformed select` and seven fuzz bodies parse
  differently; after the pipeline, the list in `known-differences.json` (S5: no presentation; plain text of fuzz 362). One fuzz body
  with `noscript` (fuzz 63, plain text).
- **Class B, limits:** Gumbo's depth limit counts its stack of open elements as tokens arrive; AngleSharp exposes no stack, so the
  depth is counted on elements as they are created. Bodies within a few elements of 400 deep can be refused where Rails renders
  them, or the reverse (the corpus's `limits too deep before the adoption agency cuts`; L1's list, where the spike also moved
  `400 x <b>` + `</p>` (refused here) and `<b>`, 390 `<span>`, 10 `<div>`, `</b>`, 300 `<div>` (rendered here)). Attributes are counted per tag as Gumbo does if `ShouldEmitAttribute` sees
  duplicates and unfinished tags, else `limits attributes at the limit and a duplicate` and `limits attributes on an unfinished tag`
  go here too (the plan's "400-duplicate-attributes edge").
- **Class B, caps:** a body of more than 10,000 elements or 40,000 tokens, or whose `<html>`/`<body>` tags carry more than 400
  attributes, is refused like a too-deep one (renders empty, no plain text, logged). Rails renders it. AngleSharp's tree builder
  is quadratic on such bodies (S3); no body Campfire's composer produces comes near them.
- **Class B, unfurl URLs:** `web_url` and the tweet URL check parse with `System.Uri` behind an RFC 3986 character check: 7 of Rails'
  54 vectors differ (R8's list), all URLs only a hand-made body carries; where Ruby raises (the message renders empty), the card
  renders without the link.
- **Kept from the port and Rust** (their README entries stand): `name` attributes dropped; no link inserted into an attribute value
  (now by construction); a missing attachable renders ☒ and leaves the editor; content attachments render 8 deep.

## 9. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | A sanitizer bypass: the walker departing from Loofah (an element unwrapped that should go with its contents, an attribute visited out of order, a URI attribute missed) | S1 against the old sanitizer on 4 x 1,479 inputs; N1 against Rails' outputs; N2's planted defects; the security review reads the walker beside rails-html-sanitizer's `PermitScrubber` |
| 2 | Another quadratic path in AngleSharp's tree builder (S3 found three) | L2 and L3's CPU bounds; P5; a builder who sees a parse over 50 ms on a body under 100 KB stops and measures the shape before going on |
| 3 | The document parse after `<body>` differing from the fragment algorithm on a shape the corpus lacks (`</body>` then comments, a leading `<html>`, `<frameset>`, a BOM) | P2's explicit cases; P1 on the 1,479; C2 on canonical bodies (what is stored) |
| 4 | Limits miscounted by one (refusing what Gumbo accepts at 400, or accepting 401) | L1's truth table on the first day of 5.1; the corpus's `limits *` cases in C1 |
| 5 | The shared `BrowsingContext` not thread-safe, or holding every parsed document (a leak under load) | L4; the bench's CPU and memory columns in 5.6 |
| 6 | Mutation XSS: our serialization parsed differently by the browser than by AngleSharp (the classic sanitizer bug) | the output holds no foreign element, no `noscript`, `template`, `style` or `script` (N2); P3's serializer rules; the security review parses each presentation of the corpus with the loose layer's parser (scripting on) and compares trees |
| 7 | The linker building a node from the wrong decoded piece (a link text with `&amp;` shown as `&amp;`, an `href` with `&nbsp;`) | A1 and C1 (the corpus has `&amp;`, U+00A0, brackets and `&gt;` cases); the `search/results*` and `rooms/show/*` slices show link text |
| 8 | Loose comparison hiding a namespace difference (S2 found 40 such inputs) | P1 compares trees with namespaces; N1 counts element names with namespaces |
| 9 | `web_url` on `System.Uri` accepting a URL that reaches the page as `href`/`src` where Ruby refused it | R8's check; C1's `web_url` cases; the review reads every `href`/`src` the attachment partials take from data |
| 10 | Posting slows toward the floor (post c=16 at 6.04-6.20x Rails) | A3; S6's per-body figures re-measured in 5.3-5.5 (a test-only stopwatch is enough); `bench/quick` post a message in 5.6 against a control |
| 11 | An AngleSharp upgrade changing parses (the standard moves) | the version is pinned; P1's frozen trees fail on any change; bump only with them green |
| 12 | The integrations' behaviour moving with `RubyUri` | R9 moves the file unchanged; the App's opengraph, webhook and push tests unchanged and green in 5.6 |
| 13 | The `Attachables` split changing a rule for the old pipeline before the flip | the old `CorpusTests` byte comparison stays green through 5.3-5.5 |

## 10. Questions for Chris

| # | question | options |
|---|---|---|
| Q1 | AngleSharp has four defects this design works around: its fragment parser keeps the pre-2017 rule that foreign content never breaks out in a fragment (79 corpus bodies parse differently from browsers), `ParseFragment` moves its result one node at a time (quadratic), and its tree builder is quadratic on foster-parented elements, on text split by ignored tags and on `<html>` attributes. The workarounds (the document-mode parse and three caps) are in this design and do not wait on anything. May I report them upstream? | 1. Yes, open issues on AngleSharp/AngleSharp with the minimal cases from S2/S3, under your account, and offer a PR for the fragment rule and the quadratic move (Recommended: once fixed and released, a later unit can drop the caps and the document-mode parse) <br> 2. Issues only, no PR <br> 3. No, keep the workarounds and say nothing upstream |

Everything else is settled by D9, the parity bar and the floor, as recorded in section 1: AngleSharp for parsing, the DOM and
serialization; no HtmlSanitizer (R4: it cannot be configured to Loofah's rules, and parity is the bar); the document-mode parse and
the caps (R2, R3); rails_autolink's matchers per text node (R6); `System.Uri` for rich text and `RubyUri` deferred to Phase 8 (R8,
R9); Rails' corpus as the oracle with an explicit known-differences list (R11).

## 11. Security review (Opus, 2026-10-22, at `414caa8`)

Scope: everything Phase 5 added or changed (`c88f590..414caa8`): `src/Campfire.RichText` (`Html5`, `Sanitizer`, `Loofah`, `Content`,
`Attachables`, `Filters`, `Autolink`, `AutolinkMatch`, `PlainText`, `ActionText`, `Error`), the App's call sites (`Presenters/Presenter.fs`,
`Controllers/Messages.fs` canonicalization, `RichText.fs`, `Integrations/OpengraphMetadata.fs`, `IntegrationJobs.fs`'s bot replies) and the
view boundary the presentation crosses (`Core/Html.fs` `Trusted.ofSanitized`, `Core/HtmlOutput.fs` slot splitting,
`Markup/Messages.fs`, `Templates/Messages/_Presentation.fs`, `Templates/Messages/Edit.fs`). 21 source files and the oracle tests
(`CorpusTests`, `HardeningTests`, `Support`, `CorpusResolver`, the Views `HtmlOutputTests` O4) read. Fixes in `d076556`.

**Method.** (1) Read the walker beside rails-html-sanitizer's `PermitScrubber`/Loofah 2.25 and every place a string becomes markup. (2) A
fuzz harness, kept at `reference-tools/richtext/review/` (the checkpoint's was lost): the corpus's 658 bodies and 657 Rails presentations,
179 fixed hostile cases (70 URL spellings of `javascript:`/`vbscript:`/`data:` with entities, `&Tab;`/`&NewLine;`/`&colon;`, C0
controls, NUL, soft hyphen, zero-width space, U+2028, ideographic space, percent forms, each in `href`/`src`/`cite`; 35 raw tag tricks; 47 mutation-XSS
classics: `noscript`, `template`, `style`, `xmp`, `iframe`, `noembed`, `noframes`, `plaintext`, `textarea`, `title`, `svg`/`math` with
`mglyph`/`malignmark`/`annotation-xml`/`foreignObject`/`desc`, CDATA, comment tricks, nested forms, table foster parenting, `select`; 27
attachment shapes: content attachments, remote images and videos with `javascript://`/`data:`/protocol-relative URLs, opengraph
embeds by attributes and by content markup, mentions of a user whose name, title and SGID hold markup, galleries, Trix figures) and
generated bodies from a grammar over 119 tags, 61 attribute names and those values, mutated by insert/delete/duplicate. Each input
went through `presentMessage`, the bot API's `body.html`, `Sanitizer.sanitize` with each of the four lists, the stored canonical body
presented again, plain text, mentions, the editor value and the unfurl's `strip_tags` + `sanitize`. Every HTML output was parsed in
the **browser model**: a page parse with scripting on, the output inside `messages/_presentation`'s `div` with a sentinel after it,
checked for leaving the container, elements and attributes outside the list, foreign elements, comments, event and `data-*`
attributes, `id`/`name`/`form`, URLs by the browser's URL rules (leading and trailing C0 and space stripped, tab and newlines removed,
scheme by its ASCII grammar; `data:` by its MIME type) and by `allowed_uri?` again, and elements or attributes the browser builds that
the pipeline's own parse of the same output lacks (mutation). Two runs: seed 2, 201,494 inputs, 1,584,965 outputs (before the fix);
seed 3, 251,494 inputs, 1,978,388 outputs (after). (3) Against Loofah itself: 4,179 hostile cases (the fixed ones and 4,000
generated) through the reference image's own pipeline (`generate.rb`, `review/run.sh`), compared with our presentations.

| # | Severity | Where | Finding | State |
|---|---|---|---|---|
| F1 | High (stored denial of service) | `src/Campfire.RichText/Html5.fs:296` (`guarded`) | AngleSharp 1.8.4 (and 1.8.5) throws a `NullReferenceException` from its select mode when an `<xmp>` inside a `<select>` closes the paragraph the select is in and another `<select>` follows (`<p><select><xmp></xmp><select>x`, also inside `div`, `b`, `caption`). Nothing caught it: every entry point raised. Posting the body was a 500 (canonicalization raised before the write), and the same body stored any other way, by Rails before a switch to this app (Rails renders it) or by a later path that skips canonicalization, failed every page, broadcast and search result that renders the message, for every member of the room, until the row was edited by hand. Found by the fuzz (4 of 201,494 inputs, 44 raised outputs) and shrunk by delta debugging. | **Fixed** (`d076556`): `guarded` turns any exception the tree builder throws (not `OutOfMemoryException`) into `ParseError.ParserFailed`, refused as a body past a limit is: `messages/_unrenderable`, plain text empty and logged, stored as it came. README class B entry; Q1's upstream list gains it. Tests that failed before: `HardeningTests` "markup the parser throws on is refused, not raised" (and inside an attachment's content and caption), `MessagesTests` "a body the parser throws on fails neither the post nor the pages that show it" (post 200, stored raw, room page and messages page 200), S7's new fragment. After the fix: 0 exceptions in 1,978,388 outputs |
| F2 | Medium (test oracle) | `tests/Campfire.RichText.Tests/CorpusTests.fs:169` | The security assertions (N2, S7, the planted defects) parsed each output with `Html5.parseBody`, the pipeline's own parser, with scripting off and as a body. An output that a browser parses differently from AngleSharp's no-script body parse (the mutation-XSS class: `noscript` text, markup leaving the container) passes such an oracle by construction; risk 6 of section 9 asked for a scripting-on parse and 5.7 had switched it to `parseBody`. | **Fixed** (`d076556`): `browserViolations` parses as a page does (scripting on, inside the presentation's `div`, sentinel after) and runs the same assertions on what it builds, plus container escape, comments and foreign elements; N2 and S7 run both models; planted defects that only the browser model catches (an end tag that closes the container, a `div` inside a table that does, a `noscript` read as text); S7 gains 13 of the review's shapes. All green on the current pipeline |
| F3 | Low | `src/Campfire.RichText/Sanitizer.fs:79` (`sanitize`), `Content.fs:263` (`body.html`) | Sanitizer output is not structure-stable: a flattened `template` unwrapped inside a table leaves `table > div > table` and similar trees that no parser builds, and a browser re-parsing such output inside a `div` closes that `div` early (the inner `</div>` reaches it). 44 of 1,978,388 outputs: direct `sanitize` with each list (35) and `body.html` (5 plus 4 refused by our own depth limit after the layout's `div`). **Never a presentation** (0 of about 500,000 presentation and stored-presentation outputs; neither ours nor Rails' presentations escape on the 4,179 Rails cases): the presentation's last step, `autoLink`, parses the layout's markup afresh (a parser-built tree, every `template` and table part outside the list gone in the Action Text pass) and its scrub unwraps only flow elements (`figure`, `figcaption`, `video`, `audio`, `source`, `embed`, `action-text-attachment`), which a parser never places in table context. The direct outputs reach no page: an attachment's `content` attribute (sanitized again where it renders), the filters (parsed again), the unfurl's title and description (escaped text, no element in 1.98 million checks), `body.html` (JSON for bots and webhooks; Rails' shapes are the same) | **Accepted** (parity with Loofah, no page placement). Guarded: S7 and N2 fail if a presentation leaves its container |
| F4 | Info | N1 (section 5) | "Nothing Loofah strips survives", read literally, does not hold on hostile bodies whose parse differs from Gumbo's (the `select` model, `isindex`, `frameset`, AngleSharp's raw-text and table shapes): of the 4,179 cases, 4,023 presentations are byte-equal to Rails', 156 differ, and 45 of those keep something Rails' output lacks (161 facts: allowed elements such as `a`, `table`, `p`, `img`, `b`, allowed attributes, and URLs `http://x.test/`, `/users/1`, `src="x"`). Every one is inside the auto-link list with a safe URL: **0 browser-model violations in our 4,179 presentations** (Rails' own: 82, all the `name` attributes the port drops on purpose) | Accepted: the README's class B parse entries; the corpus's N1 stays exact |
| F5 | Info | the views, `Attachables`, `AutolinkMatch` | Rails' behaviour kept as it is, noted for the record: the editor value is the stored body, not sanitized (`editable_body`; Lexxy imports it), and goes out through `Html.attr`; `body.html` keeps an attachment's `url` and `content` attributes (`url` is not a URI attribute to Loofah) and Campfire's list allows `embed` there; an unsigned SGID still mentions any user (`attachable_from_possibly_expired_sgid`, Campfire's own extension); rails_autolink links `file://`, `feed://`, `svn://` and the like | No change (parity; none reaches the presentation's allowlist with a script URL) |
| F6 | Info | the review itself | The browser model is AngleSharp with scripting on, not Chromium, Firefox or WebKit; no real browser replayed the outputs. The residual is small: presentations contain no raw-text, RCDATA, foreign, `select`, `template` or `noscript` element, the places where parsers disagree | Open (a Playwright replay of the harness's outputs in the parity image would close it) |

**Checked and holding** (each with the fuzz's numbers above: 0 violations in every presentation, stored presentation and unfurl output):
- *Allowlists and the walker.* Bottom-up over a snapshot of children, as `traverse_conditionally_bottom_up`; text skipped; comments
  and unlisted HTML elements unwrapped; a foreign element removed with its contents, which is stricter than Loofah (it unwraps). An SVG
  or MathML element whose local name is on a list (`svg:a`) is kept by the tag check, but its foreign root (`svg`, `math`) is on no list
  and takes it away. Attributes in order with `force_correct_attribute_escaping` re-run after each kept one, a later URI attribute
  checked in its escaped form; blank `src` removed; Lexxy's colour rule last (`body.html` only: the auto-link list has no `style`). No
  `id`, `name` or `data-*` but `data-language` reaches a page.
- *URIs.* `allowed_uri?` reads the DOM value (entities already decoded once by the parser, as the browser's own value is), drops C0,
  space, backtick, DEL and U+0080-U+0101, decodes entities again and `&Tab;`/`&NewLine;`/`&colon;`, lowercases, and takes the longest
  `[a-z][a-z0-9+.-]*` before a separator. A browser accepts only an ASCII scheme after stripping C0 and space at the ends and tab and
  newlines inside, so every value the browser reads as `javascript:`/`vbscript:` this reads the same or stricter (the second decoding and
  the control stripping only refuse more). `data:` keeps Loofah's five media types, decided on the first `;`/`,` segment; a value whose
  MIME type the browser would read as HTML, XML or SVG never passed (the 70 spellings x 3 attributes x 4 lists, and the fuzz).
- *Mutation XSS.* The serializer is the spec's (text `&`, U+00A0, `<`, `>`; attributes `&`, U+00A0, `"`); raw text is written raw only
  under a raw-text parent, and no list keeps one, so after unwrapping such text is escaped under its new parent. `<` and `>` stay raw in
  attribute values (R7): safe because no presentation holds an element a browser reads as raw text, RCDATA, a comment or foreign
  content, and the page places it in a `div` (no `script`, `template` text or attribute context). The canonical body a post stores can
  turn `noscript` text into markup (Nokogiri does the same), and every reader sanitizes it again.
- *Autolink.* Links are built with the DOM from text-node pieces: `href` is the matched text (scheme from rails_autolink's list or
  `http://` added) or `mailto:` + the escaped address; no string is spliced into markup; a text node with an `a` ancestor is skipped. The
  attribute-value breakout cases (A2) hold.
- *Attachments and mentions.* Every partial value goes through `Erb.htmlEscape`; opengraph `href`/`url` through `webUrl` (http and https
  only, the RFC 3986 check, the query exempt but escaped); remote image and video URLs through `imageTag`'s asset rule and then the
  final `allowed_uri?` (`javascript://`, `data:image/svg+xml` and `data:text/html` removed in the Action Text pass); a hostile user name,
  bio and SGID render as text; content attachments recurse 8 deep and are sanitized at each level.
- *Limits.* Gumbo's two and the three caps are counted in the parser's callbacks; a refusal and now a parser exception are both a
  `ParseError`; nested content attachments can exceed 400 deep only inside a render, where the final parse refuses them. The App's
  `ParseSafeLength` shortcut (800 UTF-16 units, no attachment) stays under every cap.
- *The U+E000 slot marker.* The serializer writes U+E000 as itself in text and attributes; `Trusted.ofSanitized` (the presenter's only way
  to place rich text) rewrites every one to `&#57344;`, so a typed `<` U+E000 digits `>` in a `title` cannot read as a fragment slot (Views
  O4); text `<` is always `&lt;`.
- *Unfurl cards.* `strip_tags` is the text nodes escaped, then `sanitize` with the default list: escaped text, no element in any output.
