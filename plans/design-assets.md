# Design note: assets on MapStaticAssets (Phase 6)

Status: design, 2026-10-22, before Phase 6 builds. Author: the Opus designer (plan section 5). Binding for the Phase 6 builders
together with `plans/dotnetway.md` sections 3-5 and D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`).
Where this note and plan section 2.6 disagree, this note wins: it is later and rests on the spikes in section 2. The disagreements,
each argued below: the web root is a staged copy in the App's own `wwwroot/`, because linked files are not found outside a publish
(A2); `reference/public` is served by `MapStaticAssets` too, not by `UseStaticFiles` with Rails' header (A9); the import map's pins are
listed from the descriptor table, not from the directories (A6); the parity harness learns the new fingerprint form first (A15);
about 0.15 KLOC instead of 0.12 (section 7).

Scope: `Campfire.Assets` (`Assets.fs`, `Helpers.fs`, `Tags.fs`, `Serve.fs`, `Embedded.fs`: the Propshaft manifest, the asset
helpers, the head tags, `ActionDispatch::Static` over an embedded blob), the build tool `Campfire.Assets.Build` (Propshaft's
digests and compilers, importmap-rails' pin expansion, the blob writer), their inputs (`vendor/`, `overrides/`, `script/`), and
the App's uses of them (`Boot.fs`'s public files and error pages, `Web/Pages.fs`' preload `Link` header, `Presenters/ViewContext.fs`,
`Presenters/Page.fs`, the sound URLs in `Presenter.fs`, `Controllers/Pwa.fs`, `Controllers/Users/Avatars.fs`), plus the harness's
asset masks. Out of scope: the front server (`Campfire.Kit/Front`, Phase 7; this phase only hands it the host options), the Views'
`Markup.Assets` helpers (they take `ctx.AssetPath` and do not change), the rich-text layer.

**Preconditions.** Phases 1-5 closed: Phase 5 closes at `853604f` (its verifier's entry is `5152f85`). Views are Falco.Markup and
the head tags reach the layout as `Html.Trusted.ofAssetTags` strings from `ViewContextBuilder` (Phase 4); the App's pipeline is
`Web.Services.pipeline` on a branch of the front server's `WebApplication`. `Campfire.App` is already an `Sdk.Web` project
(assembly `campfire`; its build writes an empty `campfire.staticwebassets.endpoints.json` today). If a piece is missing, the first
unit that needs it stops and reports. Phase 5's conditions stand: AngleSharp pinned, parse only through `Html5`, `Trusted.ofSanitized`
the one way rich text reaches a page.

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity or the 5x floor forces it), with
the reason.

| # | Decision | Reason |
|---|---|---|
| A1 | **`MapStaticAssets`** (shared framework, `Microsoft.AspNetCore.StaticAssets`) serves every asset and every file of `reference/public`, from the build's endpoint manifest (`campfire.staticwebassets.endpoints.json`). Fingerprints, ETags, `Last-Modified`, content negotiation over the SDK's precompressed `.gz` (build) and `.gz`/`.br` (publish) variants, ranges, HEAD and 304s are the framework's. No package is added. | D9; the plan's 2.6. Spike S3/S4: 311 assets and 5 public files, content types equal to Rails' for all 311 shared assets, br 1,225 / gzip 1,439 / identity 4,113 bytes for `composer.css`, `If-None-Match` 304, `Range` 206, HEAD, `/assets/../campfire.dll` 404. |
| A2 | **The web root is staged by an MSBuild target in `Campfire.App.fsproj`** (`CampfireStageWebRoot`, `BeforeTargets="ResolveProjectStaticWebAssets"`, text in section 3): Propshaft's load path, `Assets/overrides/` first and then `Assets/vendor/LOAD_PATH` in order, is **copied** into the gitignored `src/Campfire.App/wwwroot/assets/<logical path>`, and `reference/public/**` into `wwwroot/`. An override shadows the load-path file of the same logical path (`Remove ... MatchOnMetadata="Link"`); dotfiles (`.keep`) and the vendored lexxy `.gz`/`.br` are left out; a staged file whose source went away is deleted; the staged files replace the evaluated `wwwroot/**` `Content` items. Two files at one path stop the build (the SDK's own "Two assets found targeting the same path"). | The plan's 2.6 ("a build target stages ... into the gitignored web root before discovery"), now proven. Spike S2: `Content` items that only *link* to `reference/` were discovered and published, but from the build output they were served as **200 with an empty body**: the runtime manifest maps every asset under the project's `wwwroot/`, so the files must be there. The input list stays the one `script/revendor` writes (`LOAD_PATH`), so Propshaft's precedence is data, not code. |
| A3 | **The SDK's fingerprint form, `name.<fingerprint>.ext`** (default pattern `#[.{fingerprint}]`, 10 characters of `[0-9a-z]`, e.g. `composer.zroil2fb6p.css`), instead of Propshaft's `name-<8 hex>.ext`. | The framework's naming; a URL is class A (plan section 4). The harness masks both forms to one placeholder (A15). No custom `StaticWebAssetFingerprintPattern`: Propshaft's digest (SHA-1 of the content, the referenced assets and `config.assets.version`) cannot be reproduced by the SDK anyway. |
| A4 | **The asset table** (logical path -> fingerprinted route, a `FrozenDictionary`) is read from the `StaticAssetDescriptor` metadata of the `StaticAssetsEndpointDataSource` that `MapStaticAssets` adds (public types; the `label` and `fingerprint` endpoint properties; descriptors with selectors or a `Content-Encoding` header skipped), once, when the pipeline maps the assets. It is **process-wide** (a module value published once with `Volatile.Write`; reading it before it is loaded throws), because presenters, broadcasts and jobs render outside a request, as the embedded table did. **Startup validation:** every descriptor's `AssetPath` must exist in `IWebHostEnvironment.WebRootFileProvider`, else boot fails naming the cause. `StaticAssetsEndpointDataSourceHelper` (documented "for internal framework use only") is not used. | Spike S4/S8: a host that cannot see the web root serves every asset as 200 with 0 bytes, silently (the test host under its own application name, a missing `UseStaticWebAssets` in the build output); the check found 1,524 missing descriptors there and 0 in a working host. Rails' `asset_path` is process-wide too; `FrozenDictionary` is the decisions file's "build-once tables". |
| A5 | **`stylesheet_link_tag :all, "data-turbo-track": "reload"`** = every logical path in the table ending in `.css`, sorted ordinally (Propshaft's `all_stylesheets_paths`), one `<link rel="stylesheet" href="..." data-turbo-track="reload" />` each (href HTML-escaped), joined by newlines; **plus Rails' preload `Link` header** (`<href>; rel=preload; as=style; nopush`, comma-joined, a link that would pass 1,000 bytes left out), both computed once at load. | The browser must get the same CSS in the same order: S5, 31 of 31 stylesheets in Rails' order, compared by logical path. The `Link` header is Rails' default behaviour on every HTML page and costs about 15 lines; with the longer fingerprints it holds 15 links instead of Rails' 16 (class A). |
| A6 | **`javascript_importmap_tags`** = the import map written by **`ImportMapDefinition`** (`Microsoft.AspNetCore.Components`, shared framework; `ToString()` is the JSON), in a `<script type="importmap" data-turbo-track="reload">`, then one `<link rel="modulepreload" href="...">` per preloaded pin's distinct URL, then `<script type="module">import "application"</script>`. The pins come from `reference/config/importmap.rb` (an embedded resource of the App) through a ~40-line reader of `pin "name"[, to: "path"][, preload: bool]` and `pin_all_from "dir", under: "prefix"[, to:][, preload:]` with importmap-rails 2.2.2's rules (a later pin of a name replaces it in place; a directory's module name drops the extension and a trailing `index`). **`pin_all_from` lists the table's logical paths under `<under>/`** ending in `.js`/`.jsm`, in path-component order: every `pin_all_from` of the reference names a load-path root's subdirectory that equals its `under:` (`app/javascript/controllers` under `controllers`, `vendor/javascript/languages` under `languages`, ...), and a test pins the result to Rails' map. A pin whose path is not in the table is skipped (Propshaft's rescued `MissingAssetError`). | One wrong pin is a dead Stimulus controller. Spike S5: 89 pins with Rails' names in Rails' order, every target the same logical path, 89 modulepreloads equal, and the import map's text **identical to Rails' after digest masking** (`ImportMapDefinition` writes two-space indented JSON with only `"imports"` when scopes and integrity are null). Listing the table instead of the disk means nothing reads `reference/` at run time. `ImportMapDefinition` escapes `<`, `&` and `'` in the JSON, so the map is safe inside its `script` element. |
| A7 | **The asset endpoints get a routing pass of their own at `ActionDispatch::Static`'s place** in `Web.Services.pipeline` (after `Edge.defaultHeaders`, before response compression, the body limit, the method override, session and WebSockets): `UseRouting()`, a two-line middleware that marks a request whose endpoint carries a `StaticAssetDescriptor` as `SkipRequestHeaders` (no `x-request-id`/`x-runtime`, as Rails' `Static` runs before those), `UseEndpoints(MapStaticAssets manifest)`. A request that matches no asset falls through to the second `UseRouting` and Falco, as today. | Rails' middleware order, kept: static responses skip the session, compression and method override as they do now (K11's "public files are served precompressed"). Spike S8: two routing passes in one branch work, the asset endpoint ends the request, other paths reach Falco. S7: a page route with the asset pass in front measured 50,000-50,800 req/s against 49,600-50,300 without (within noise). |
| A8 | **Host options:** the host is created with `WebApplicationOptions(Args = ..., ApplicationName = "campfire", WebRootPath = <AppContext.BaseDirectory>/wwwroot)` and `builder.WebHost.UseStaticWebAssets()` is always called; `StaticAssets.hostOptions` and `StaticAssets.configureHost` make them, and both the front server (`Front.serveWithLines`, which takes them as parameters until Phase 7 deletes it) and the tests' `Support.startPipelineWith` use them. The content root stays the working directory (`/rails` in the image). | Spike S4: the web root and the application name can only be set at creation (`UseWebRoot` afterwards throws `NotSupportedException`); in the image the working directory is `/rails`, so the default web root would be `/rails/wwwroot`; `UseStaticWebAssets` is what makes a *build* output (the tests, `dotnet run`) serve, and a no-op on a publish (no runtime manifest there). A test project gets both manifests copied into its output, but only finds them under the name `campfire` (S4: 200/0 bytes under its own name, 200/4,113 under `campfire`). The content root is left alone so nothing else that resolves against it moves. |
| A9 | **`reference/public` is staged into `wwwroot/` and served by `MapStaticAssets`** (`/404.html`, `/422.html`, `/500.html`, `/502.html`, `/robots.txt`), not by `UseStaticFiles`. The error pages the app renders (`PublicExceptions`, the status-code pages) read their bytes from `WebRootFileProvider` once, when the pipeline is built; `send_file` of `app/assets/images/default-bot-avatar.svg` uses the staged file's `PhysicalPath`. | One mechanism and one web root instead of two (a second static-file middleware would also expose `wwwroot/assets` without fingerprints). The cost is class B: the public files get the framework's cache headers, and the extensionless `/404` (Rails' `Static` tried `404.html`) is now the app's 404 (section 8). The harness's `errors/*/static` states visit `/404.html` etc., which are served as before. |
| A10 | **Cache headers are the framework's**: fingerprinted URLs `max-age=31536000, immutable`; plain URLs `no-cache` (images `max-age=3600, must-revalidate`), with an ETag. Rails sends `public, max-age=2592000` on everything (`production.rb`'s second `public_file_server.headers`). | D9, and better for the browser (a fingerprinted URL never revalidates). Class B. Consequence measured in the gate, not optimised: the front server's response cache keeps only `public` responses (`FrontCache.cacheLifetime`), so assets stop being front-cache hits and every asset request reaches `MapStaticAssets` (the `static_css` row; S7). Phase 7 removes that cache anyway and decides there whether `/assets` needs an OutputCache policy. |
| A11 | **Compression is the SDK's**: gzip variants at build, gzip and Brotli at publish, chosen by `Accept-Encoding` with the framework's quality rules; text types only (`svg`, `css`, `js`, `map`, `html`, `txt`); the vendored `lexxy.js.gz`/`.br` files are not served under their own names. | D9. Rails sends uncompressed files that Thruster gzips (or zstd's) on the fly; the bytes on the wire are class A. |
| A12 | **CSS `url()`/`@import` and JS `sourceMappingURL` are not rewritten.** Relative references resolve beside the fingerprinted file to the plain endpoint of the same logical path (`url(common-file-text.svg)` -> `/assets/common-file-text.svg`; lexxy's `@import url("lexxy-content.css")`; `//# sourceMappingURL=turbo.min.js.map`). | `MapStaticAssets` has no CSS URL rewriting (the SDK 11.0.100-rc.2 tasks only rewrite placeholders in HTML and import maps for WebAssembly apps). Same bytes, other URLs: class A. S6: of Rails' 314 compiled files, 299 are byte-equal to the staged sources; the 11 others are the 3 overrides, the 2 stylesheets with `url()`, the 3 lexxy stylesheets with `@import`, and the 3 scripts with a source map comment. A test pins that exact list. |
| A13 | **Deleted, not replaced:** Propshaft's digester and compilers, importmap-rails' tag writer, the embedded blob and its build step (`Campfire.Assets.Build`), the Rack::Files port (`Serve.fs`: ranges, multipart ranges, `If-Modified-Since` by build time, the extension fallback), the asset helpers' `?query`/`#fragment` and `.js`/`.css` extension handling (no caller passes them; a test-only use goes). | D9; the framework does the job. What the user of a browser sees is unchanged; the edges an operator could notice are section 8's class B entries. |
| A14 | **Inputs move into the App:** `src/Campfire.Assets/{vendor,overrides,script,OVERRIDES.md}` -> `src/Campfire.App/Assets/` (`git mv`; `script/revendor` and `export_reference.rb` retargeted); the golden fixtures `tests/Campfire.Assets.Tests/reference/` -> `tests/Campfire.App.Tests/Assets/reference/`. | The Sdk.Web project owns its web root's inputs; the plan folds Assets into App. Vendored files are still regenerated only by `revendor`. |
| A15 | **The harness learns the fingerprint form before the app changes** (unit 6.1): the DOM and live layers mask `/assets/<name>.<10 [0-9a-z]>.<ext>` to the same `/assets/<name>-«digest».<ext>` as a Propshaft digest (`normalize.ts`, its F# twin `tests/Shared/LooseDom.fs`, `overrides.ts`, `controllers_a/replay.py`); a plain `/assets/` URL stays plain there, so a page that lost its fingerprints still differs. In the network layer only (reported, not gated), a plain `/assets/` URL is described like a digested one, because `url()`, `@import` and source maps fetch plain URLs by design (A12). | The loose DOM is a class C gate; without the mask every page differs on every asset URL. S5: with the mask, the stylesheet and modulepreload tags are equal line for line and the import map text is identical; over all 315 logical paths the new mask matches no plain URL (no false positives). |
| A16 | **Oracles:** Rails' own output, already in the repo (`manifest.json` for the logical paths, `stylesheet_link_tag_all.html`, `javascript_importmap_tags.html`, `link_header.txt`, `compiled_sha256.json`, `static_responses.json`'s content types), compared by **logical path** through the fixture manifest and our table. HTTP-level tests boot the app (`Support.bootSeeded`). The cache headers and Brotli exist only in a publish, so the network check (`bin/asset-check`) runs against the image. | The plan's 2.11 (HTTP-level tests replace internal-unit tests); the reference fixtures are what Rails rendered, so equivalence is shown against Rails itself, not against the deleted port. |

## 2. Spikes (scratch project, 2026-10-22, deleted after)

An F# `Microsoft.NET.Sdk.Web` project (assembly `campfire`, Falco 5.2.0, FSharp.Core 10.1.401) under the session scratchpad, SDK
11.0.100-rc.2.26504.105 / ASP.NET Core 11.0.0-rc.2, the host built as `Front.serve` builds it (slim builder, the app's pipeline
on `host.New()`), reading this repository's `reference/`, `src/Campfire.Assets/{vendor,overrides}` and the golden fixtures read-only.
A plain `Microsoft.NET.Sdk` project referencing it stood in for the test project. Measured on the Mac (M4, 10 cores), wrk 4.2
(`-t2 -c16`), Kestrel on loopback, unpinned, load average 1.5-8.

| # | Question | Result |
|---|---|---|
| S1 | Packages and advisories (NuGet flat container and registration, GitHub advisory database, 2026-10-22) | No package added: `MapStaticAssets`, `StaticAssetDescriptor`, `StaticAssetsEndpointDataSource` and `ImportMapDefinition` are in `Microsoft.AspNetCore.App` 11.0.0-rc.2.26504.105 (the repo's SDK). Falco 5.2.0 is still the newest stable (6.0.0-beta1 2026-07-27), Falco.Markup 1.4.0; NuGet lists no vulnerability for either. The 2026-10-13 ASP.NET Core advisories: CVE-2026-70188 (Kestrel accepts newlines in HTTP/2 trailers, 11.0.0-preview.1 to rc.1) is fixed in the rc.2 we run; the others are for 8.0/9.0/10.0 runtimes. None concerns static assets or static files. |
| S2 | Staging (A2) | `Content` items linked into `wwwroot/assets` from `reference/` and `vendor/`: discovered, fingerprinted and published (`Link` is honoured), but **served as 200 with 0 bytes from the build output** (the runtime manifest maps them under the project's `wwwroot/`). Copied into `wwwroot/`: 4 overrides + 307 load-path files (= Rails' 314 manifest entries, minus the 4 lexxy `.gz`/`.br`, minus the 3 shadowed files, plus `install-edge.svg`), 16 MB; precedence by `Remove ... MatchOnMetadata="Link"` (3 removed); a second build skips unchanged copies; a stray file in `wwwroot/` is deleted and absent from the manifest. Two `.keep` files at one path stopped the build with the SDK's "Two assets found targeting the same path with incompatible asset kinds". Clean build 3.4 s; `dotnet publish -r linux-arm64 --self-contained` 36.7 s; the published `wwwroot` is 20 MB (223 `.br` + 223 `.gz`) against today's 16.3 MB `assets.bin`. Pitfall met: an unqualified `%(RecursiveDir)` in an `Include` inside a target batched over the existing `Content` items and gave the public files the overrides' paths; set `Link` with the metadata-update form on a private item type, as section 3 does. |
| S3 | The manifest | 1,524 endpoints in the build output, 2,416 published. Per asset: the plain route (`no-cache`; images `max-age=3600, must-revalidate`), the fingerprinted route (`max-age=31536000, immutable`, endpoint properties `label` = `assets/composer.css`, `fingerprint` = `zroil2fb6p`, `integrity`), and each compressed variant on both routes with a `Content-Encoding` selector (plus its own `.gz`/`.br` route). `Content-Type` equal to Rails' (`static_responses.json`) for all 311 shared assets. |
| S4 | Serving | Published, run from another directory: identity 4,113 B, `gzip` 1,439, `gzip, deflate, br, zstd` -> br 1,225 for `composer.css`; `ETag` + `If-None-Match` -> 304; `Range: bytes=0-9` on an mp3 -> 206; HEAD 200; POST -> 405; `/assets/../campfire.dll` -> 404; a non-asset path reaches the app. Build output: serves only with `UseStaticWebAssets()` and the application name `campfire` (the referencing project's output holds both manifests); without, 200 with an empty body. In the build output the development runtime handler sends `Cache-Control: no-cache` on fingerprinted URLs and adds a `{**path:file}` fallback route constrained to existing files. `UseWebRoot` or an application name set after the builder is created throws `NotSupportedException`; `WebApplicationOptions(ApplicationName = "campfire", WebRootPath = <base>/wwwroot)` with the content root left at the working directory serves in both layouts. |
| S5 | Equivalence with Rails (by logical path, through `manifest.json`) | Stylesheets: 31 of 31, same order. Import map: 89 pins, same names in the same order, every target the same logical path (pins of `pin_all_from` listed from the table); 89 modulepreloads, same list. After masking both digest forms, the import map's text is identical to Rails' and every `link` line equal (Rails writes `/>` on the stylesheet tags, as we do). The fingerprint mask matched none of the 315 plain logical paths. The preload `Link` header fits 15 links in 959 bytes (Rails 16). |
| S6 | Bytes | 299 of Rails' 314 compiled files byte-equal to the staged sources; differ: `controllers/copy_to_clipboard_controller.js`, `lib/autocomplete/base_autocomplete_handler.js`, `models/file_uploader.js` (overrides), `avatars.css`, `composer.css` (`url()`), `lexxy.css`, `lexxy-content.css`, `lexxy-editor.css` (`@import url()`), `lexxy.js`, `stimulus.min.js`, `turbo.min.js` (`sourceMappingURL`); not staged: `lexxy.js.br`, `lexxy.js.gz`, `lexxy.min.js.br`, `lexxy.min.js.gz`. No relative or dynamic `import` in any served script (two `import("./...")` inside JSDoc comments). |
| S7 | Cost | An asset through `MapStaticAssets` (published, br): **30,400-31,600 req/s, 203-216 us of process CPU per request**; the same br bytes from memory through the same pipeline (the embedded blob's stand-in): 49,200-50,600 req/s, 122-123 us (wrk-bound). A page route with the asset routing pass in front: 50,000-50,800 req/s against 49,600-50,300 without, CPU 120-121 us either way. A table lookup: `FrozenDictionary` 12-20 ns, the port's `ConcurrentDictionary` cache 9-10 ns; a room page makes about 30, under 1 us against its 448 us of CPU (Phase 5 gate). Nothing on the five workloads' path changes measurably; asset requests cost more CPU than the embedded blob (file I/O per request, macOS numbers). |
| S8 | Two routing passes in one branch | `UseRouting(); UseEndpoints(MapStaticAssets)` then `UseRouting(); UseFalco [...]; UseFalcoNotFound` on the same `host.New()` branch: an asset is served and ends the request (the later middleware's marker header absent), `/rooms/1` and an unknown path reach Falco. Startup validation over `WebRootFileProvider`: 1,524 descriptors missing without `UseStaticWebAssets`, 0 with. |

## 3. Types and modules

**`src/Campfire.App/Campfire.App.fsproj`** gains the staging target (proven in S2; paths as they will be after A14):

```xml
<PropertyGroup>
  <CampfireAssetsDir>$(MSBuildProjectDirectory)/Assets/</CampfireAssetsDir>
  <CampfireReferenceRoot>$(MSBuildProjectDirectory)/../../reference/</CampfireReferenceRoot>
</PropertyGroup>
<ItemGroup>
  <EmbeddedResource Include="$(CampfireReferenceRoot)config/importmap.rb" LogicalName="importmap.rb" />
</ItemGroup>
<!-- Propshaft's load path (Assets/overrides first, then Assets/vendor/LOAD_PATH; an override shadows the file of its logical
     path) and reference/public, copied into wwwroot/ (gitignored) before static web assets discovery. -->
<Target Name="CampfireStageWebRoot" BeforeTargets="ResolveProjectStaticWebAssets">
  <Error Condition="!Exists('$(CampfireReferenceRoot)config/importmap.rb')" Text="reference/ submodule is missing (git submodule update --init reference)" />
  <ReadLinesFromFile File="$(CampfireAssetsDir)vendor/LOAD_PATH">
    <Output TaskParameter="Lines" ItemName="_CampfireLoadPathLine" />
  </ReadLinesFromFile>
  <ItemGroup>
    <_CampfireLoadPathGlob Include="$([System.String]::Copy('%(_CampfireLoadPathLine.Identity)').Replace('reference:', '$(CampfireReferenceRoot)').Replace('vendor:', '$(CampfireAssetsDir)vendor/'))/**" />
    <_CampfireOverride Include="$(CampfireAssetsDir)overrides/**" />
    <_CampfireOverride Link="wwwroot/assets/%(RecursiveDir)%(Filename)%(Extension)" />
    <_CampfireAsset Include="@(_CampfireLoadPathGlob)" />
    <_CampfireAsset Remove="@(_CampfireAsset)" Condition="$([System.String]::Copy('%(Filename)%(Extension)').StartsWith('.')) Or '%(Extension)' == '.gz' Or '%(Extension)' == '.br'" />
    <_CampfireAsset Link="wwwroot/assets/%(RecursiveDir)%(Filename)%(Extension)" />
    <_CampfireAsset Remove="@(_CampfireOverride)" MatchOnMetadata="Link" />
    <_CampfirePublic Include="$(CampfireReferenceRoot)public/**" />
    <_CampfirePublic Link="wwwroot/%(RecursiveDir)%(Filename)%(Extension)" />
    <_CampfireStage Include="@(_CampfireOverride);@(_CampfireAsset);@(_CampfirePublic)" />
  </ItemGroup>
  <Copy SourceFiles="@(_CampfireStage)" DestinationFiles="@(_CampfireStage->'%(Link)')" SkipUnchangedFiles="true">
    <Output TaskParameter="DestinationFiles" ItemName="_CampfireStaged" />
  </Copy>
  <ItemGroup>
    <_CampfireStale Include="wwwroot/**" Exclude="@(_CampfireStaged)" />
  </ItemGroup>
  <Delete Files="@(_CampfireStale)" />
  <ItemGroup>
    <Content Remove="@(_CampfireStaged);@(_CampfireStale)" />
    <Content Include="@(_CampfireStaged)" />
  </ItemGroup>
</Target>
```

Destinations stay relative (`wwwroot/...`): an absolute path does not match the evaluated `Content` items and the SDK then fails
with a duplicate key in `DiscoverPrecompressedAssets` (S2). `.gitignore` gains `src/Campfire.App/wwwroot/`. If the static web
assets cache ever reports a duplicate that the sources do not have, delete `src/Campfire.App/obj` once (S2 met it after a bad
`Link`).

**`src/Campfire.App/Web/StaticAssets.fs`** (module `Campfire.App.Web.StaticAssets`, before `Services.fs`), about 100 lines:

```fsharp
/// Propshaft::MissingAssetError: `asset_path` of a logical path the build does not have.
exception MissingAsset of logicalPath: string

/// The endpoint manifest the build writes beside the assembly.
val ManifestPath : string                                   // AppContext.BaseDirectory/campfire.staticwebassets.endpoints.json
val hostOptions : args: string[] -> WebApplicationOptions   // ApplicationName "campfire", WebRootPath <base>/wwwroot (A8)
val configureHost : WebApplicationBuilder -> unit           // builder.WebHost.UseStaticWebAssets() |> ignore
/// The asset routing pass at ActionDispatch::Static's place (A7): UseRouting, the SkipRequestHeaders marker, UseEndpoints with
/// MapStaticAssets ManifestPath; loads and validates the table (A4) the first time.
val map : IApplicationBuilder -> unit
val path : logicalPath: string -> string                    // "/assets/composer.zroil2fb6p.css"; raises MissingAsset
val stylesheetTags : unit -> string                         // A5, made once at load
val preloadLinks : unit -> string                           // the `Link` header value, made once at load
val importmapTags : unit -> string                          // A6, made once at load
/// A file of the web root (error pages, send_file of an image asset): "404.html", "assets/default-bot-avatar.svg".
val webRootFile : IWebHostEnvironment -> relativePath: string -> IFileInfo
```

The loaded state is one immutable record (`Routes: FrozenDictionary<string, string>`, the three strings) behind a module-level
mutable written once with `Volatile.Write`; `map` on a second host in the same process (tests) loads the same manifest and may
replace it with an equal value. Readers before the first `map` get `InvalidOperationException "static assets are not mapped"`.
Nullness: `GetMetadata<StaticAssetDescriptor>()` is `| null -> ...`, as F# 9 nullness requires under warnings-as-errors.

**`src/Campfire.App/Web/Importmap.fs`** (module `Campfire.App.Web.Importmap`, before `StaticAssets.fs`), about 45 lines:
`type Pin = { Name: string; Path: string; Preload: bool }`, `pins (importmapRb: string) (logicalPaths: string seq) : Pin list`
(A6's reader; an unsupported statement or option fails at boot, as the build tool did), `tags (pins: Pin list) (route: string ->
string voption) : string` (the `ImportMapDefinition` JSON in its script element, the modulepreload links, the module script).

**Call sites** (each becomes a one-line change): `ViewContextBuilder` (`stylesheetTrusted`/`importmapTrusted` wrap
`StaticAssets.stylesheetTags ()`/`importmapTags ()` in `Html.Trusted.ofAssetTags`, lazily per process, so the lint's allowlist does
not change); `Page.renderDetachedAt` and `Web.Pages` (`AssetPath = StaticAssets.path`); `Pages`' application-layout response
(`"link", StaticAssets.preloadLinks ()`); `Presenter.fs`' sound URL and image; `Controllers/Pwa.fs` (`Manifest.render ...
StaticAssets.path`); `Controllers/Users/Avatars.fs` (`send_file` of `(StaticAssets.webRootFile env "assets/default-bot-avatar.svg").PhysicalPath`;
the temporary-directory extraction goes); `Boot.errorPages` reads `404/422/500/502.html` through `webRootFile` when the pipeline is
built; `Web.Services.pipeline` calls `StaticAssets.map` where `publicFiles` is now (until 6.6, right after it); `Front.serveWithLines`
takes the `WebApplicationOptions` and a `configureHost` callback (its own tests pass `WebApplicationOptions()` and `ignore`);
`tests/Campfire.App.Tests/Support.fs`' `startPipelineWith` creates its builder with `StaticAssets.hostOptions [||]` and
`configureHost`.

**Tools:** `bin/asset-check` (bash and python3, inside colima against `parity/bin/candidate up --seed default`): signs in, reads a
room page, and checks every stylesheet, modulepreload, import-map and `img` URL under `/assets/` (A16, the network gate; section 5
N1-N4). `bin/check-assets-incremental` is rewritten for the staging target (6.2).

## 4. Units, in build order

One Sonnet builder per unit, about 90 minutes, from a clean `git status` on `dotnetway`, one commit per logical step (read `git diff
--cached --stat` first), a Phase log entry per unit (KLOC from `bin/kloc`, gates run, differences recorded). **Common acceptance for
every unit:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green (it runs the markup lint and its self-test, the Dapper and EF guards and H1:
zero query compilations in steady state); `df -h /` first (12 GB, 15 GB before a bench); `bin/clean-docker` after image rebuilds,
`bin/clean-parity` after pixel runs; pixel slices and benches detached with a status file and polled in the foreground with bounded
loops, one parity compare at a time, `realtime/**` one state per run. Benches: `bench/quick` interleaved against a control image of
Phase 5's close (`853604f`, built once as `campfire-dotnetway:p5ctl` from a scratch worktree with the submodule copied in, removed
after the phase), host probe and load recorded; a five-workload cell under 5x Rails blocks. The app is pixel-identical after every
unit: until the flip (6.5) pages carry the old Propshaft URLs and the old middleware serves them; the new endpoints sit behind it.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 6.1 Harness masks | No app change. `parity/capture/normalize.ts` (`ASSET_DIGEST` gains the `/assets/<name>.<[0-9a-z]{10}><ext>` alternative, same placeholder), `network.ts` (`DIGESTED_ASSET` both forms; `describeRequest` masks both, and a plain `/assets/` URL as if digested, network layer only, A15), `overrides.ts` (directory list gains `src/Campfire.App/Assets/overrides` first; its pattern both forms), `tests/Shared/LooseDom.fs` (the same alternative), `parity/bin/loose-golden` rerun, `reference-tools/campfire/controllers_a/replay.py`'s mask; new cases in `parity/capture/test/normalize.test.ts` and `overrides.test.ts` | `parity/bin/test-loose-dom` green with cases: a Propshaft URL and our URL of one logical path mask equal; `/assets/highlight.js/core.abcdefghij.js` and `/assets/stimulus.min.ubi9oscxp6.js` masked; a plain `/assets/languages/javascript.js` unchanged in the DOM layer; Rails' and S5's import map texts equal after masking; LooseDom tests in `bin/verify` green, `loose/` goldens unchanged or regenerated with a stated reason; `git diff --stat src` empty; `bin/pixel-parity --fast --only 'auth/sign_in*,rooms/show/designers'` on `default` equal (today's app, so the masks change nothing) |
| 6.2 Staging | `git mv src/Campfire.Assets/{vendor,overrides,script,OVERRIDES.md} src/Campfire.App/Assets/` (`revendor`'s `CRATE` retargeted; the old `Campfire.Assets.fsproj` `AssetInput` globs and the `--assets` argument point at the new directory, so the old pipeline still serves); the `CampfireStageWebRoot` target and the importmap embedded resource (section 3); `.gitignore`; `bin/check-assets-incremental` rewritten (an added override is staged and in the endpoint manifest; deleted, it is gone from both); README's overrides link | `dotnet build` stages 311 assets + 5 public files; `campfire.staticwebassets.endpoints.json` has a fingerprinted endpoint for each; `bin/check-assets-incremental` passes; an App test (S1 of section 5) checks the staged set against Rails' manifest and the bytes against the sources; nothing served changes (`git diff` touches no `.fs` but paths in `ReferenceTests`/`OVERRIDES` readers); `bin/kloc` unchanged; no pixel run needed (no response changes) |
| 6.3 Host and table | `Web/StaticAssets.fs` (table, validation, `map`, `path`, `webRootFile`, `hostOptions`, `configureHost`); `Front.serveWithLines` takes the options and the callback; `Support.startPipelineWith` uses them; `Web.Services.pipeline` calls `StaticAssets.map` **right after** the old `publicFiles` (the old middleware still answers every URL a page carries; the new endpoints answer only the new fingerprinted URLs) | H1-H4 of section 5 green; a test host without `configureHost` fails at boot naming the cause; `bench/quick --routes post_message,sidebar` (the two thinnest cells: post c=16 6.04x, sidebar c=1 7.74x) 3 reps against `p5ctl`: no cell under 5x and no drop beyond the control's range (the routing pass is now on every request); `--fast --only 'auth/sign_in*,rooms/show/designers'` equal |
| 6.4 Tags and import map | `Web/Importmap.fs`; `StaticAssets.stylesheetTags`, `preloadLinks`, `importmapTags` computed at load; the fixtures `git mv` to `tests/Campfire.App.Tests/Assets/reference/` (`revendor`'s `FIXTURES` retargeted); `AssetsTests.fs` with T1-T6. Not yet used by pages | T1-T6 green; a planted wrong pin (`controllers/x` pointing at another file), a dropped `pin_all_from`, a reordered stylesheet each fail a test (run once, reverted, recorded in the Phase log); no response changes |
| **checkpoint** | Opus, after 6.4 (section 4 below) | |
| 6.5 The flip | `ViewContextBuilder`, `Page.renderDetachedAt`, `Web.Pages` (asset path and `Link` header), `Presenter.fs` sounds, `Controllers/Pwa.fs` on `StaticAssets`; nothing in the App calls `Campfire.Assets` for a page any more (it still serves public files and old URLs) | `--fast` on every seed for `interactions/**` (every Stimulus-driven state: any wrong pin breaks one), `rooms/show/**`, `auth/**`, `account/**`, `pwa/**`, `users/profile/**`, then `realtime/message_sent` and `realtime/boosted` one at a time: screenshots, aria and loose DOM equal; the page's head in the live layer carries only fingerprinted URLs (a grep of the run's `.loose.norm.html` for `-«digest»` against Rails' count); `bench/quick --routes room_show,post_message,static_css` 3 reps against `p5ctl` (room page: the most asset lookups; post: the thinnest cell; `static_css`: reported, A10) |
| 6.6 Public files and the network check | The old `publicFiles` middleware and `Boot.staticResponse` go; `StaticAssets.map` takes their place; `Boot.errorPages` and `Avatars`' `send_file` read the web root; the App tests that went through `Assets.serve` (`AppTests` "every file Static serves", `AuthenticationTests`' asset path) become HTTP-level (H5-H8); `bin/asset-check` (N1-N4); README "Known differences": the **Assets** entry (section 8) | H5-H8 green; image rebuilt (`CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build`, label checked) and `bin/asset-check` passes against it; `--fast --only 'errors/**,users/**,bot_api/**,pwa/**'` on every seed equal; `bin/cookie-roundtrip` once (the head of the pipeline changed) |
| 6.7 Delete | `src/Campfire.Assets/` (the project, `Assets.fs`, `Helpers.fs`, `Tags.fs`, `Serve.fs`, `Embedded.fs`), `src/Campfire.Assets.Build/`, `tests/Campfire.Assets.Tests/` (its fixtures already moved), their `CampfireFs.slnx` rows and the App's project reference; the Dockerfile's `dotnet build src/Campfire.Assets.Build` step and its comment; `bin/publish-mounted`'s step; `bin/kloc`'s exclusion; `bin/linux-tests`' comment; AGENTS.md (the layout table's two rows, an **Assets** working-rules bullet: the staging target, `StaticAssets`, the startup validation, the harness masks, `bin/asset-check`); `Core/Html.fs`' comment; plan pointers | `git grep -n -e 'Campfire\.Assets' -e 'Assets\.Build' -e 'assets\.bin' -e 'EmbeddedData'` over `src tests bin parity bench/quick bench/run Dockerfile AGENTS.md README.md CampfireFs.slnx` finds only history (Phase log, results); `bin/kloc` recorded (section 7); `--fast --only 'rooms/show/designers'` equal on the rebuilt image |

Then the **phase gates** (gate runner, plan section 3, in this order): `CAMPFIRE_REQUIRE_SEED=1 bin/verify`; **every state `--fast`**
(every seed, slices with `--only`, the `realtime/**` states one at a time); the **lean matrix on `default`**; then the **lean pixel
matrix on all seeds**; **`bin/asset-check`** against the image (fingerprinted URLs, cache headers, precompressed variants);
**`bench/quick`** on the five workloads plus `static_css` (asset hits, reported, not gated) against a same-day `p5ctl` control,
host probe and load recorded; `bin/cookie-roundtrip` once; `bin/kloc`. No `bin/db-differential` (no row is written differently).

**The checkpoint after 6.4** (Opus): reviews 6.1-6.4 against this note before the flip: that the masks (A15) mask only digests and
fingerprints (the false-positive check over every logical path; a plain URL in the DOM still differs), the staging target's
precedence and stale-file handling (`bin/check-assets-incremental`), the table's selection of descriptors and the startup
validation (a host without the web root fails), T1-T6 against the Rails fixtures and the planted failures 6.4 recorded, the 6.3
bench against `p5ctl`, and whether 6.5-6.7 still fit 90 minutes each.

## 5. Tests

In `tests/Campfire.App.Tests/AssetsTests.fs` (new) unless named; HTTP tests boot the seed through `Support.bootSeeded` (build output:
gzip variants only, `no-cache` on fingerprinted URLs from the development runtime handler, so no cache-header assertions here).

| Id | Test |
|---|---|
| S1 | The staged `wwwroot/assets` holds exactly Rails' manifest's logical paths minus the 4 lexxy `.gz`/`.br`, plus `install-edge.svg`; each staged file's bytes equal its source (the override where one exists) |
| T1 | The table's logical paths equal S1's set; every route has the fingerprint form; `path` raises `MissingAsset` for an unknown path |
| T2 | Bytes against Rails' compiled files (`compiled_sha256.json`): equal for every file but exactly A12's 11 (a twelfth or a missing one fails) |
| T3 | `stylesheetTags ()` against `stylesheet_link_tag_all.html`: 31 lines, each mapped to its logical path through the fixture manifest and through the table, same list in the same order, and equal line for line after the A15 mask |
| T4 | `importmapTags ()` against `javascript_importmap_tags.html`: the import map's names in the same order, each target the same logical path; the modulepreload list the same; the last line `<script type="module">import "application"</script>`; the import map's text equal after the A15 mask |
| T5 | `preloadLinks ()`: the first links name the same logical paths as `link_header.txt`'s, the value is at most 1,000 bytes, and one more link would pass 1,000 |
| T6 | `Importmap.pins` on small sources: a later `pin` replaces an earlier one in place; `index.js` gives the directory's name; `preload: false` keeps a pin out of the modulepreloads; a pin to a missing file is skipped; an unsupported statement raises |
| H1 | Every table route answers 200 with the source's bytes (identity) and the gzip variant with `Accept-Encoding: gzip`; HEAD has no body |
| H2 | The ETag of a fingerprinted URL gives 304; `Range: bytes=0-9` on a sound gives 206 with 10 bytes |
| H3 | An asset response has no `x-request-id` and no `x-runtime` (A7); a page response still has both |
| H4 | `/assets/nope.css` is the app's 404 page; `POST` to an asset URL is 405 (class B, pinned) |
| H5 | `/404.html`, `/422.html`, `/500.html`, `/502.html`, `/robots.txt` are served with Rails' bytes; a 404 the app renders has `404.html`'s bytes; `/404` is the app's 404 (class B, pinned) |
| H6 | `GET /users/<bot>/avatar` of a bot without one sends `default-bot-avatar.svg`'s bytes as `image/svg+xml` |
| H7 | The web app manifest's icons and screenshots are fingerprinted URLs of the logical paths Rails names (`Facts.jsonEqual` after the A15 mask) |
| H8 | A message with a sound renders the fingerprinted mp3 and image URLs |
| N1 | `bin/asset-check`: every `/assets/` URL in a room page (stylesheets, modulepreloads, import-map values, `img`) has the fingerprint form and answers 200 with `cache-control: max-age=31536000, immutable`, an ETag (304 on `If-None-Match`) and `vary: Accept-Encoding` |
| N2 | The same URLs, text types: `content-encoding: br` for `Accept-Encoding: br`, `gzip` for `gzip`, none for none, and the three decode to the same bytes |
| N3 | `/assets/composer.css` (plain) 200 `no-cache`; `/assets/nope.css` 404; `/robots.txt` 200; a sound with `Range` 206 |
| N4 | The `Link` header of the room page names fingerprinted stylesheets only |

Kept, re-pointed: `Campfire.Views.Tests` (they take Rails' tags and paths from the goldens and do not change), `ManifestTests`/`PwaTests`
style tests through H7. Retired with `Campfire.Assets.Tests`: `ReferenceTests` (becomes T2-T5 and H5), `HelpersTests` (the URI and
extension rules A13 drops), `ServeTests` (becomes H1-H5).

## 6. What is deleted

`src/Campfire.Assets/` (`Assets.fs`, `Helpers.fs`, `Tags.fs`, `Serve.fs`, `Embedded.fs`, the fsproj and its `GenerateAssets` /
`Stamp*` targets, `obj/assets/` with `assets.bin`, `EmbeddedData.fs`, `importmap-tags.html`); `src/Campfire.Assets.Build/`
(`Propshaft.fs`, `Importmap.fs`, `Program.fs`); `tests/Campfire.Assets.Tests/`; in the App `Boot.staticResponse`, `Boot.publicFiles`,
the `Assets.serve`-based `errorPages`, `Avatars`' temporary-directory extraction (`assetDirectory`, `assetLock`), `ViewContextBuilder`'s
`StylesheetTags` record use; the Dockerfile's Assets.Build step and `bin/publish-mounted`'s. Moved, not deleted: `vendor/`, `overrides/`,
`script/`, `OVERRIDES.md`, the golden fixtures.

## 7. KLOC projection

`bin/kloc` at `5152f85`: **30,220 corrected** (30,048 cloc); `Campfire.Assets` **429**; `Campfire.Assets.Build` is a build tool and
not counted; MSBuild XML is not counted.

| | lines |
|---|---:|
| `Campfire.Assets` deleted | -429 |
| `Web/StaticAssets.fs` (table and validation 30, host and `map` 20, stylesheet tags and `Link` header 25, web-root files 10, the rest 15) | +100 |
| `Web/Importmap.fs` (the reader 30, the tags with `ImportMapDefinition` 15) | +45 |
| App call sites: `Boot`'s static and error-page code (about 40 -> 10), `Avatars`' extraction (about 20 -> 4), the rest one line each | -45 |
| **Phase 6 net** | **about -330** |

Project: **30.2 -> about 29.9 KLOC** (range 29.85-29.95), the plan's row is 24.2 -> 23.9 (the same -0.3 on a base that drifted).
The assets layer after: about 0.15 KLOC in the App (the plan said 0.12; the import map reader and the startup validation are the
difference). Rust's bar is 31.0.

## 8. Known differences this phase records (README "Known differences", plan section 4)

One README entry, **Assets** (`Campfire.App/Web/StaticAssets.fs` and the staging target; reference Propshaft 1.2.1, importmap-rails
2.2.2, `ActionDispatch::Static`, `config/environments/production.rb`), citing the reference files, each class B line with the test
that pins it.

- **Class A (byte-level, with the byte-level class entry):** asset URLs are `name.<fingerprint>.ext` (SDK) instead of
  `name-<digest>.ext` (Propshaft); CSS `url()`/`@import` and JS `sourceMappingURL` are not rewritten and fetch the plain URL of the
  same file (A12); assets carry an ETag and a per-file `Last-Modified`; text assets are precompressed (Brotli or gzip by
  `Accept-Encoding`) instead of gzipped or zstd'd on the fly by the front server; the import map's JSON is written by
  `ImportMapDefinition` (same text after masking today); the preload `Link` header holds 15 stylesheets instead of 16.
- **Class B:** cache headers are the framework's (`max-age=31536000, immutable` on fingerprinted URLs, `no-cache` or
  `max-age=3600, must-revalidate` on plain ones) instead of `public, max-age=2592000` on every public file (A10; N1, N3); an
  extensionless `/404`, `/422`, `/500`, `/502` is the app's 404 instead of the `.html` file with 200 (H5); `/assets/.manifest.json`
  is not served; plain `/assets/<logical path>` URLs are served (Rails serves only digested names; N3); a request with several
  byte ranges gets the whole file with 200 instead of `multipart/byteranges` (S4); a method other than GET or HEAD on an asset URL is
  405 instead of the router's 404 (H4); the vendored `lexxy.js.gz`/`.br` files are not served under their own names.
- **Internal, not in the README:** the front server no longer caches asset responses (no `public`), until Phase 7 removes it.

## 9. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | A wrong or missing pin: a Stimulus controller that never connects (one dead button, no error on screen) | T4 (names, order, targets against Rails); 6.4's planted wrong pin fails it; `interactions/**` `--fast` in 6.5 |
| 2 | Stylesheets in another order or one missing | T3; any `--fast` state's pixels |
| 3 | Assets served as 200 with an empty body (a host that cannot see the web root) | the startup validation (A4) fails the boot; H1 compares bytes; S4/S8 are the worked example |
| 4 | An override shadowed by the reference file (the old frontend behaviour back: a CSRF header, a relative clipboard URL) | S1's byte check on the three overrides; T2's exact list of 11; the SDK's duplicate-path error if the precedence `Remove` breaks |
| 5 | The harness mask hiding a real difference (a page that lost its fingerprints, or a wrong file under a right name) | 6.1's cases (a plain URL in the DOM still differs); the false-positive scan over every logical path; T2 checks the bytes behind the names |
| 6 | Stale or missing files after an input is added or deleted (incremental builds) | `bin/check-assets-incremental`; S1 |
| 7 | The test host under another application name (assets empty in tests only) | the validation fails the test host's boot; `Support.startPipelineWith` uses `hostOptions` |
| 8 | The image's web root not found (`/rails/wwwroot`) or Brotli missing | `bin/asset-check` against the rebuilt image in 6.6 (N1, N2); the validation fails the container's boot |
| 9 | MSBuild metadata batching (an unqualified `%(...)` in a target), or the static web assets cache keeping a bad state | the build fails with a duplicate path or a duplicate key (S2 met both); section 3's target is the spike's working text; delete `obj` once |
| 10 | A test or job rendering before the table is loaded | `InvalidOperationException "static assets are not mapped"` at once; the fragment context and the tags are read lazily after `map` |
| 11 | Asset requests slower than Rails' (Thruster serves them from memory; ours read the file and miss the front cache) | `static_css` in 6.5's and the gate's bench; reported, not gated (A10); if under Rails at c=16, recorded for Phase 7's front-server design (an OutputCache policy for `/assets`) |
| 12 | The asset routing pass costing the thin cells (post c=16 6.04x) | 6.3's bench on `post_message` and `sidebar` against `p5ctl`; S7 predicts no measurable change |
| 13 | Public files: an error page served with other bytes, or the app's rendered 404 without them | H5; `errors/**` `--fast` in 6.6 |
| 14 | The development runtime handler in tests (`no-cache`, a `{**path:file}` fallback route) mistaken for production behaviour | cache headers are asserted only by `bin/asset-check` on the image (N1-N3) |
| 15 | Harness flakiness read as an asset regression (Chromium OOM in the 6 GB VM, a realtime slice) | one compare at a time, `realtime/**` one state per run, compare an error with Phase 5's gate logs before debugging, re-run with `--only` |
| 16 | Disk: the publish's `wwwroot` (+4 MB a layer), the control image, pixel runs | `df -h /` before each slice and build; `bin/clean-docker` after each image; stop at 12 GB (15 before a bench) |

## 10. Questions for Chris

None. MapStaticAssets for assets and public files (D9, the plan's 2.6), the staged web root (proven necessary by S2), the SDK's
fingerprints and cache headers (D9; class A and B by the plan's policy), `ImportMapDefinition` for the import map (shared framework),
listing `pin_all_from` from the table (no run-time read of `reference/`, pinned to Rails' map by T4), and accepting slower asset hits
than the embedded blob (D9 accepts some speed cost; the floor covers the five workloads, and Phase 7 owns the front cache) are settled
by D9, the floor and the plan.
