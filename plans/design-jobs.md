# Design note: jobs, integrations, push, config and time (Phase 8)

Status: design, 2026-10-27, before Phase 8 builds. Author: the Opus designer (plan section 5). Binding for the Phase 8 builders
together with `plans/dotnetway.md` sections 2.7, 2.8 and 3-5 and D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`).
Where this note and plan sections 2.7 and 2.8 disagree, this note wins: it is later and rests on the spikes in section 2. The disagreements,
each argued below:
- **Web Push is `Lib.Net.Http.WebPush` 3.3.1, not `WebPush` 1.0.13.** WebPush 1.0.13 sends the pre-RFC `aesgcm` draft coding
  (`Encryption: salt=`, `Crypto-Key: dh=;p256ecdsa=`, `Authorization: WebPush`), not the RFC 8291 `aes128gcm` the plan credits it with
  and the gem sends (J7, S4).
- **The useragent-gem port and the rqrcode port stay.** MyCSharp.HttpUserAgentParser answers differently from the gem for the harness's
  own Chromium user agent and for `apple_messages` (J9, S2). QRCoder picks other masks than rqrcode in 29 of 39 vectors (J8, S3).
  Both are class C, so the spikes decide and no pixel run is needed.
- **The opengraph fetch keeps Rails' redirect loop.** `HttpClient`'s automatic redirects differ from `Net::HTTPRedirection` in
  recorded cases. The SSRF guard still runs in `ConnectCallback` on every connection (J3).
- **The retry-free custom resilience pipelines, not `AddStandardResilienceHandler`.** The standard handler sent a POST 4 times. With
  retries disabled for unsafe methods it still retries a GET on a 5xx 3 times, and its circuit breaker would let one dead bot stop
  every other bot's deliveries (J2, S5).
- **Opengraph on AngleSharp needs two caps** (depth 256, as libxml2 has, and 100,000 elements). Uncapped, a 5 MB page of nested `div`s
  kept a core busy for minutes, a CPU denial of service open to any signed-in user (J5, S6).
- **About -1.7 KLOC instead of -3.0** (section 7): the user-agent and QR ports stay (1.27 KLOC).

Scope: `src/Campfire.App`'s jobs (`Jobs.fs`, `CoreJobs.fs`, `Integrations/IntegrationJobs.fs`), integrations (`Integrations/`: `Net.fs`,
`Guard.fs`, `Http.fs`, `RubyUri.fs`, `Webhook.fs`, `Opengraph*.fs`, `WebPush*.fs`, `Vapid.fs`), the push and unfurl controllers
(`Users/PushSubscriptions.fs`, `Users/TestNotifications.fs`, `UnfurlLinks.fs`), configuration (`Config.fs`, `Web/FrontSettings.fs`), time
(`Campfire.RailsCompat/Clock.fs`, `Campfire.Db.Ef.ClockTimeProvider`, every `Clock` user), logging and redaction (`Boot.createLoggerFactory`,
`Web/HttpLog.fs`, `Web/PublicExceptions.fs`), and the boot (`Boot.fs`, `AppState.fs`, `Web/Host.fs`'s builder), with their tests. The
user-agent parser (`Concerns/UserAgent.fs`) and the QR encoder (`Controllers/QrCode/Rqrcode.fs`) were in scope. The spikes keep both (J8,
J9). Out of scope: Storage, the merge into `Campfire.Rails`, test consolidation (Phase 9); `Campfire.Db`'s own push-endpoint
validator (`PushSubscription.parseEndpoint`, data rules, unchanged); the Cable transport.

**Preconditions.** Phases 1-7 are closed: Phase 7 closes at `2fae069` (its verifier's entry is `14a334a`). Phase 7's conditions bind
this phase:
- Keep H1, the markup lint, the Dapper guard and Phase 6's asset conditions green.
- (a) Phase 8 owns logging, so it takes up the request log's c=1 cost and the static rows (unit 8.6).
- (b) A move of logging to `IOptions` or the `Redactor` API keeps the Rails-filtered query (SR2) and the forging guards (SR1, SR3).
  `FrontSettings` on `IOptions` keeps Thruster's names, prefix and Go parsing (HS12) and the boot validation of `TLS_DOMAIN`.
- Any change to `Web/Host.fs`, `Acme.fs` or `OutputCaching.fs` re-runs HS2/HS4 under `bin/linux-tests App`, AC11 and `pebble-image.sh`.

The jobs already sit on bounded `Channel`s (`Jobs.fs`, from the port). `TimeProvider` is already what EF's interceptors, the cookie
scheme, Cable and ACME read. But it is a `ClockTimeProvider` over the port's `SharedClock`, which `AppState.Clock` still hands out.

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity or the 5x floor forces it), with
the reason.

| # | Decision | Reason |
|---|---|---|
| J1 | **Jobs run in a `BackgroundService`.** `Jobs` (the queues: one bounded `Channel<Work>` per kind, 1,024 deep, `FullMode = Wait` written with `TryWrite` so a full queue drops and logs, the `EventSink`, `PerformLater`) stays a singleton. The task-starting `JobRunner.start`/`Runner.Shutdown` become **`JobRunner : BackgroundService`**: `ExecuteAsync` runs `JOB_CONCURRENCY` reader loops per kind; `StopAsync` completes the writers first (what is queued drains), then waits within `HostOptions.ShutdownTimeout` (the 10 s grace). The `Registry` and the handlers are unchanged. The push job's `Task.Run` goes (it already runs on a worker). Web Push's `WebPushPool` (a `BlockingCollection` thread and two semaphores) becomes **`PushDeliveries : BackgroundService`** over two channels: deliveries (bounded 10,000, `TryWrite`, 50 readers: `Concurrent::ThreadPoolExecutor.new(max_threads: 50, max_queue: 10000)`) and invalidations (unbounded, 1 reader: `FixedThreadPool.new(1)`). | The framework's lifetime owns start and stop instead of hand-held `Task`s; Rails' and Rust's in-process model is unchanged (lost on a crash, no retry: `retry_on` is commented out in `application_job.rb`). Shutdown order comes from registration: the web host (registered last by `Build`) stops first, then jobs, then push deliveries (registered before jobs, so stopped after them, since a job queues deliveries), then `Boot` closes the database after `RunAsync` returns, as `drain` does now. A handler's exception is caught per job as now (a `BackgroundService` that throws stops the host by default). Posting a message emits into the same channel, so the post path is unchanged. |
| J2 | **Three named clients from `IHttpClientFactory`** (`"opengraph"`, `"push"`, `"webhook"`, beside Phase 7's `"acme"`), each with its own `SocketsHttpHandler` (`ConfigurePrimaryHttpMessageHandler`): `AllowAutoRedirect = false` (Net::HTTP never follows; opengraph runs Rails' loop, J3), `UseProxy = false`, `UseCookies = false`, `AutomaticDecompression = GZip ||| Deflate` (Net::HTTP's `Accept-Encoding` and inflation), `ConnectTimeout` = Rails' `open_timeout` (webhook 7 s; opengraph 5 s and push 10 s, the port's), opengraph and webhook `PooledConnectionLifetime = TimeSpan.Zero` (Rails opens one connection per request), push pooled (`Net::HTTP::Persistent`). **Resilience is `AddResilienceHandler(name, ...)` with one timeout strategy each** (the time to the response head: webhook 7 s, Rails' `read_timeout`; push 10 s; opengraph 5 s), **plus a concurrency limiter of 16 on opengraph**. **No retry, no circuit breaker, no hedging, on any client.** A whole delivery or unfurl keeps its deadline as a `CancellationTokenSource` over the body read (webhook 60 s, push 30 s, unfurl 10 s), and the body is read through a length-limited stream (5 MB page, 100 MB reply). Requests on the opengraph and webhook clients carry Net::HTTP's `Accept: */*` and `User-Agent: Ruby`. | The plan's package table and the decisions file ask for the factory and resilience, retries off for POSTs. S5 measured `AddStandardResilienceHandler`: a POST to a 500 was **sent 4 times** (11 s), once with `DisableForUnsafeHttpMethods()`; a GET to a 500 was still retried 3 times (5.8 s). Rails' code retries neither, so the recorded opengraph cases would change, and an unfurl would hold its slot 4 times as long. The standard handler's circuit breaker is per client: one bot's dead endpoint would open it for every bot's webhook, and per-authority pipelines would grow with every host a user unfurls. So the framework's pipeline, holding only what Rails has (timeouts) and what the port added (the unfurl limit). Cost: the pipeline adds 1.6 us a request (S5), off every benchmarked path. Sites and bots see Net::HTTP's `Accept` and `User-Agent` because what a page serves can depend on them (fxtwitter answers crawlers' user agents with the tags). Header order, `Connection`, `Host` spelling and `Accept-Encoding: gzip, deflate` are .NET's (class B). |
| J3 | **The SSRF guard runs in `SocketsHttpHandler.ConnectCallback`** on the opengraph and push clients (`Guard.connect resolver dial`). A request that carries a pinned address (`HttpRequestOptionsKey<IPAddress> "campfire.pinned"`, set where Rails passes `ipaddr:`) connects there **after `Guard.blockedAddress` re-checks it**. A request without one sends its host through `Guard.resolve` and connects to the first public address. So no connection either client opens reaches a blocked address, whoever built the request. **The opengraph fetch keeps Rails' redirect loop** (`Opengraph::Fetch#request`: at most 10 responses, any 3xx is a redirect, each `Location` parsed, required to be http(s), resolved through the guard and pinned): about 25 lines. **`Guard.fs` keeps surfguard's host rules** (LDH names, glibc's `inet_aton` forms, bracketed IPv6, at most 256 answers, IPv4 before IPv6) **and its address lists, held as `System.Net.IPNetwork` values tested with `IPNetwork.Contains`** in place of the `UInt128` arithmetic. `IResolver` stays (the recorded cases' lookups); `IDialer` becomes the `dial` function `Guard.connect` takes (tests map fake public addresses to a local server). The webhook client is unguarded, as Rails' (`webhook.rb`: "operators legitimately point bots at their own internal services"). | Plan 2.7's guard placement, with each hop checked, as the plan asks. The redirect loop stays because `HttpClient`'s own differs from Net::HTTP on cases the recordings hold: a `304`/`300` with a `Location` is a redirect to Rails and a final response to .NET; a relative `Location` is denied by Rails (`URI::Generic` is not `URI::HTTP`) and followed by .NET; an `https` to `http` hop is followed by Rails and refused by .NET; Rails' cap counts responses. Pinning keeps Rails' DNS-rebinding behaviour (the recorded "dns rebinding is pinned" cases: one lookup, then the checked address). Surfguard is Rails' policy, shared by 37signals' apps "so this app, Basecamp, HEY and Fizzy classify internal the same way" (`private_network_guard.rb`). The guard tests check it one for one, so its rules stay hand-written; only the arithmetic moves to the framework. |
| J4 | **`Http.fs` (the Net::HTTP replica, 569 lines) and `Net.fs` go**; requests are `HttpRequestMessage`s on the named clients (J2). What Rails reads from a response is read the same way. The status is `int StatusCode`. `Content-Type` comes from the raw header (`Content.Headers.NonValidated["Content-Type"]`), with the port's `content_type` rule (media type before `;`, `Ruby.strip`, lower case). `Content-Length` comes from `Content.Headers.ContentLength`. The body comes from `ReadAsStreamAsync` through the limit. Failures map to Rails' exceptions by kind. A `TimeoutRejectedException` (the pipeline's timeout) or a connect timeout is `Net::ReadTimeout`/`Net::OpenTimeout` (the webhook's "Failed to respond within 7 seconds"). `HttpRequestError.ConnectionError` is `Errno::ECONNREFUSED`. `NameResolutionError` is `SocketError`. A TLS failure is `OpenSSL::SSL::SSLError` (push destroys nothing for it, as the port decided). | D9: the plan's 2.7 line. The replica existed to write Net::HTTP's header order and spelling, which no recorded case's outcome depends on (class B). |
| J5 | **Opengraph reads `<meta>` with AngleSharp** (pinned 1.8.4, as rich text): `HtmlParser(HtmlParserOptions(IsScripting = false, OnCreated = ...))` over the body decoded as libxml2 reads a UTF-8-tagged buffer (valid UTF-8, else each bad byte as Latin-1: `OpengraphHtml.decode`, kept). The `meta` elements and their attributes become the existing `MetaElement` list, so `OpengraphDocument`'s rules stay as they are: `property` wins over `name`, every `og:` removed, later tags win, non-ASCII dropped without a meta charset, and `metaEncoding`, Nokogiri's `meta_encoding`. **Two caps in `OnCreated`**: depth over 256 (libxml2's default maximum depth, where its HTML parser halts) and more than 100,000 elements (ours) stop the parse. **The `meta`s created before the stop are kept** (libxml2's `xmlHaltParser` keeps the tree so far), so a page's head still unfurls. Parses stay off the async workers, 4 at once (`offTheRuntime`). `OpengraphHtml.fs`'s scanner and `OpengraphEntities.fs` go. | D9 and the plan's 2.7 line. S6: on the 55 recorded page bodies AngleSharp extracts **the same attributes as the libxml2 scanner, 55 of 55**. It differs on 6 of 20 probed character-reference edge cases (`&AMP;`, C1 and invalid numeric references, `&#;`) and 7 of 29 tokenizer edge cases (a meta inside `textarea`, `title` or `template`, `<meta/property=...>`, an unterminated quoted value, NUL, CR LF). Those are malformed pages, the plan's own class B example. **Uncapped, a 5 MB page of nested `div`s did not finish in 2.5 minutes of CPU, nor did misnested formatting tags in 25 s.** The unfurl endpoint is open to any signed-in user, so that is class C. Capped, every hostile page S6 built finished in 0.05-0.35 s (the port's scanner: 0.04-0.17 s). A typical 140 KB page costs 8 ms against 0.8 ms: unfurls are not a benchmarked path, and D9 takes the cost. |
| J6 | **`RubyUri.fs` (410 lines) goes; integration URLs are `System.Uri`** behind rich text's RFC 3986 character check (`Attachables.rfc3986Characters`/`tryAbsoluteUri`, Phase 5's R8, made public for the App), as Phase 5's R9 deferred to this phase. `uri.is_a?(URI::HTTP)` is the scheme test; `request_uri` is `PathAndQuery`; the fxtwitter rewrite is a `UriBuilder` with the host replaced (`AbsoluteUri`). The webhook's "bad URI" messages keep Rails' wording. | S7, on the 172 URLs in the recorded cases, their `Location` headers and meta values, and `UriTests`: the same outcome (http or not; scheme, host, port, request target) for every URL **except `mailto:foo`**: Ruby raises `URI::InvalidComponentError`, so Rails' unfurl answers 500, while `System.Uri` parses it and the unfurl answers 204. That is class B with a test: Rails' 500 is a bug, which AGENTS.md lets us fix. The other 31 non-matches are strings both refuse as URLs (titles, relative paths), and `https:/rooms/1`, `https:rooms/1` and `https://`, which Ruby parses with an empty host the guard then refuses: no fetch either way. Numeric hosts (`2130706433`, `0x7f.1`) are normalized by `System.Uri` and refused by the guard either way, with no DNS lookup either way. What a site sees in the request line is `System.Uri`'s normalization (lower-case host, dot segments removed): class A. |
| J7 | **Web Push is `Lib.Net.Http.WebPush` 3.3.1** (`PushServiceClient(HttpClient)` over the factory's `"push"` client, so the guard, the pin and the timeouts carry it; `AutoRetryAfter = false`). Each delivery carries `PushMessage(Urgency = High, TimeToLive = 2419200)` and `VapidAuthentication(public, private, Subject = VAPID_SUBJECT)` with `VapidAuthenticationScheme.Vapid`. A `PushServiceClientException`'s status maps to the gem's errors as `verifyResponse` does (410, 404: the subscription is destroyed; 401, 403, 413, 429, 5xx: logged). The VAPID pair check at boot (a P-256 import and a sign/verify probe, about 15 lines of `Vapid.fs`) stays and turns push off with a log line, never failing the boot (Rust's behaviour, plan 2.8). `WebPushEncryption.fs` and the rest of `Vapid.fs` go. | S4: Lib.Net.Http.WebPush wrote `Content-Encoding: aes128gcm`, `Authorization: vapid t=<ES256 JWT>,k=<key>` (claims `aud`, `exp` 12 h out, `sub`), `TTL`, `Urgency: high`. **The body, decrypted with the gem vector's receiver key, is the message**, with the `0x02` delimiter. It has no BouncyCastle on net6.0+ (it uses `System.Security.Cryptography`), MIT, 2.6 M downloads, last push 2026-01-04. `dotnet list package --vulnerable --include-transitive`: none. **WebPush 1.0.13**, the plan's choice, speaks only the superseded `aesgcm` draft (its strings: `aesgcm`, `Encryption`, `Crypto-Key`, `p256ecdsa=`, `WebPush `), a different wire format from the gem's; push services are not bound to keep the draft. Differences from the gem: record size 4096 rather than the ciphertext's length, the padding, and the JWT's bytes. All are class A, since the push service and the browser decrypt both. Cost 0.66 ms a delivery (in memory), off every benchmarked path: the bench's subscriptions point at `127.0.0.1:9`, which the permitted-host rule refuses before anything is encrypted. |
| J8 | **QR codes stay on the rqrcode port**; QRCoder is not added. | S3: QRCoder 1.8.1 at ECC level H on rqrcode's 39 vectors: **version equal 38/39, module matrix equal 10/39**. rqrcode scores masks its own way (its floating-point dark-ratio term) and takes the version whose capacity is strictly greater. So `users/profile/qr_code` would show a different code: a pixel difference, class C. The plan made QRCoder conditional on that equality. It would also pull `System.Drawing.Common` 6.0.0. |
| J9 | **The useragent-gem port stays**; MyCSharp.HttpUserAgentParser is not added. | S2, on the 385 vectors Rails made (`vectors/campfire_user_agents.json`: `ApplicationPlatform`'s answers and `allow_browser`'s verdict): MyCSharp 3.1.6 agrees on **146**. It disagrees on **Playwright's Chromium** (`HeadlessChrome/140`): the gem says Safari, so every Chromium cell renders the pwa partials' Safari branches; MyCSharp says Chrome. It also disagrees on the harness's `apple_messages` agent: for the gem that is Safari 9, blocked, and gets the "Campfire" incompatible-browser page; for MyCSharp it is the Facebook robot, not blocked, so the sign-in page would show. Both are class C on the gate's own states (`auth/incompatible_browser/apple_messages`; `pwa/**` and the profile page on Chromium). An adapter that made MyCSharp answer like the gem would be the gem's rules written again. |
| J10 | **`TimeProvider` is the only clock.** `RailsCompat/Clock.fs` (`Clock`, `SharedClock`, `SystemClock`, `TestClock`) and `Campfire.Db.Ef.ClockTimeProvider` go. `AppState.Clock` becomes `AppState.Time : TimeProvider`. Production: `TimeProvider.System`, or with `CAMPFIRE_FROZEN_TIME` a **`FrozenTimeProvider`** (`Platform/HarnessClock.fs`: overrides `GetUtcNow` only). Timeouts in our code take the provider (`new CancellationTokenSource(delay, time)`, `Task.Delay(delay, time, ...)`). Two system-clock uses stay, named in a `bin/verify` guard (no `DateTime(Offset)?.(Utc)?Now` in `src` outside them): the `Date` response header (`Respond.fs`; Thruster's and Puma's real clock, never the harness's frozen one) and the request duration (`X-Runtime`, `Stopwatch`). **Tests use `FakeTimeProvider`** (`TravelTo` is `SetUtcNow`, `Travel` is `Advance`) and **`FakeLogger`/`FakeLogCollector`** in place of `Support.CapturingLoggers` (the decisions file: adopted in this phase with the move to `TimeProvider`). | The decisions file and plan 2.8. S8: a `PeriodicTimer(20 ms)` on `FakeTimeProvider` **ticked 0 times in 300 ms** (15 on the system clock and on a `GetUtcNow`-only subclass). So the harness's clock must not be `FakeTimeProvider`: Cable's heartbeat (`PeriodicTimer(beat, server.Time)`) and ACME's waits would stop in every parity run. The subclass freezes `Time.current` and nothing else, which is what `TestClock.FrozenAt` did. |
| J11 | **Redaction at the logging boundary uses `Microsoft.Extensions.Compliance.Redaction`.** `AddRedaction` with the **HMAC redactor**, keyed from `secret_key_base` through Rails' key generator (`campfire log redaction`, 64 bytes): stable within an install, so an operator can still match two lines, and different between installs. It is called explicitly, because the classification attributes are applied by C# generators. It covers: **the bot key in a bot API path** (the request log's path field through the `HttpLog` interceptor, and `PublicExceptions`' `path=`); **a webhook URL in a job failure** (`InvalidUrl` carries it; such URLs often hold the secret in the path); **a push endpoint's path** (never logged; the host is). No line logs an e-mail address or a session token today; tests pin that (RD). The request line's Rails-filtered query (SR2) and the forging guards (SR1, SR3) are unchanged (Phase 7 condition (b)). | The plan's 2.7/2.8 and the decisions file's package table (bot tokens, push endpoints, e-mails). The bot key is the credential of the bot API: Rails' request line logs it (only the parameters line is filtered), so this is class B, an improvement. S9: 0.33 us per redaction, only on bot API paths and failure lines (no benchmarked workload). |
| J12 | **The request log's cost at c=1** (Phase 7 condition (a)) **is measured down the ladder risk 1 of `design-front.md` named.** The rungs: the console logger's queue (`ConsoleLoggerOptions.MaxQueueLength`, `QueueFullMode`); the formatter (`SimpleConsoleFormatterOptions`: the timestamp format string); the interceptor's two fields. Each runs `bench/quick` against a same-session control on `avatar`, `static_css`, `up` (c=1 and c=16), and the room page and post rows. **A rung is kept only if it pays and keeps the line's fields**, since operators grep `remote_addr` and `user_agent`. The unit records whether `static_css` gets back to Rails' level, or why not. | D9 accepts the framework's logger at its cost. The unit reports what each framework setting buys before anything hand-written is considered: a buffered writer of our own is not on the ladder. |
| J13 | **Configuration is `IOptions` with `ValidateOnStart()`.** `CampfireOptions` (today's `AppConfig`) and `FrontSettings` (Thruster's variables) are `[<CLIMutable>]` records with `mutable` fields, filled by a `Configure<IConfiguration>` delegate per type from the host's configuration (the environment provider: the same variable names, `THRUSTER_` prefix first, Ruby's and Go's parsing kept, HS12), checked by `Validate(predicate, message)` with Rails' messages, and `ValidateOnStart()`. A missing `SECRET_KEY_BASE`, a non-number `JOB_CONCURRENCY` or `CAMPFIRE_FRAGMENT_CACHE_MB`, a bad `CAMPFIRE_FROZEN_TIME`, a `TLS_DOMAIN` with no usable name: each fails `RunAsync` before a listener binds, printed `Error: <message>`, exit 1, as now. Invalid VAPID keys are not a validation failure (J7). `EdgeConfig` is built from the two option types. | Plan 2.8 and the decisions file. Validation moves from a hand-built `Result` chain to the framework's validators, and the host's configuration is where tests set values (an in-memory provider, no process environment). |
| J14 | **Boot is one `WebApplication` builder** (`Boot.build`): configuration, options (J13), `TimeProvider` (J10), logging and redaction (J11), EF's factory and the `WriteGate`, `Env`, storage, the job queues and `JobRunner` (J1), `PushDeliveries`, the HTTP clients (J2), the fragment cache (one `HybridCache` registration, no side `ServiceCollection`), `AppState` as a singleton made from those, Cable, Phase 2's web services, and `Web.Host`'s Kestrel options, listeners and ACME. **`db:prepare` and `Membership.disconnect_all` run in `DatabaseStartup : IHostedLifecycleService.StartingAsync`**, registered first, so they finish before any hosted service starts and before Kestrel binds. `Booted`, `bootEdge`/`bootWith*`, the `ConfigureServices`/`Pipeline` hand-off to `Host.build` and the `drain` of jobs and push go (J1). `Boot.serve` is `build`, `RunAsync`, then the database close. `backup` builds the configuration only. Tests build through `Boot.build` with an in-memory configuration, a `FakeTimeProvider` or `FrozenTimeProvider`, the `FakeLoggerProvider` and Kestrel on a loopback port. | Plan 2.8 keeps Boot in this phase, and J13 needs it: `ValidateOnStart` is only meaningful when the host owns the configuration, and today `bootEdge` reads `AppConfig` and builds `AppState` before any host exists. The `Booted` record was the seam that let the app be built outside the host; with jobs and push as hosted services it has nothing left to carry. |
| J15 | **Packages:** `Lib.Net.Http.WebPush` 3.3.1 (pulls `Lib.Net.Http.EncryptedContentEncoding` 2.1.1), `Microsoft.Extensions.Http.Resilience` 10.11.0 (Polly 8.4.2), `Microsoft.Extensions.Compliance.Redaction` 10.11.0 in the App; `Microsoft.Extensions.TimeProvider.Testing` 10.11.0 and `Microsoft.Extensions.Diagnostics.Testing` 10.11.0 in the test projects. All in `Directory.Packages.props` with a comment naming the decision. Not added: `WebPush`, `Portable.BouncyCastle`, `QRCoder`, `MyCSharp.HttpUserAgentParser`. | S1: current versions (the 10.11.0 line shipped 2026-10-14), no advisory or deprecation on NuGet, none reported by `dotnet list package --vulnerable --include-transitive`. |

What stays hand-written, and why:
- surfguard's host and address rules (J3: the guard tests, one for one) and Rails' redirect loop (J3: recorded cases);
- the `content_type` rule and the error mapping to Rails' exceptions (J4);
- the libxml2 byte decoding and Nokogiri's `meta_encoding` (J5);
- the webhook reply handling and Rails' MIME lookup table (unchanged);
- the VAPID pair check (J7);
- the useragent-gem and rqrcode ports (J8, J9: parity);
- the frozen provider (6 lines);
- the option readers' Ruby and Go parsing (J13: an install's environment).

## 2. Spikes (scratch project under the session scratchpad, 2026-10-27, deleted after)

.NET SDK 11.0.100-rc.2.26504.105 (net11.0), an F# console on `Microsoft.NET.Sdk.Web` on the Mac, with copies of the repo's
`OpengraphHtml.fs`, `OpengraphDocument.fs`, `OpengraphEntities.fs` and `RubyUri.fs`, and data copied from `vectors/` and
`tests/Campfire.App.Tests/testdata/`. Release build. Timings are single-threaded on the Mac under the usual background load: they rank,
they do not gate.

| # | Question | Result |
|---|---|---|
| S1 | Packages, versions, advisories (NuGet registration and flat-container APIs; `dotnet list package --vulnerable/--deprecated --include-transitive`) | Microsoft.Extensions.Http.Resilience, Compliance.Redaction, TimeProvider.Testing, Diagnostics.Testing **10.11.0** (2026-10-14); WebPush 1.0.13 (2026-04-28, needs Portable.BouncyCastle 1.9.0 of 2021-10-19); **Lib.Net.Http.WebPush 3.3.1** (2025-03-09; MIT; repo pushed 2026-01-04, not archived, 1 open issue; 2,615,005 downloads; net6.0 asset depends only on Lib.Net.Http.EncryptedContentEncoding 2.1.1); QRCoder 1.8.1 (2026-10-13; pulls System.Drawing.Common 6.0.0 on net6.0+); MyCSharp.HttpUserAgentParser 3.1.6 (2026-04-01). No vulnerability or deprecation reported for any of them or their transitive packages. |
| S2 | MyCSharp.HttpUserAgentParser against the gem (385 vectors: `ApplicationPlatform`'s ten answers and `blocked`, computed with Campfire's predicates over MyCSharp's name, version, platform and robot type) | **146/385 equal.** Fields that differ: `browser` 208, `operating_system` 82, `safari` 39, `blocked` 30, `chrome` 20, `edge` 8, `firefox` 5. The harness's user agents: 12 of 13 equal; **`apple_messages`**: gem Safari, macOS, blocked; MyCSharp "Facebook", no OS, not blocked. **Playwright Chromium** (`HeadlessChrome/140.0.0.0`): gem `safari` true and browser "Safari"; MyCSharp Chrome. Parse cost 3.15 us uncached, 0.06 us through its cached provider (the bench sends no `User-Agent`, so neither is on its path). |
| S3 | QRCoder 1.8.1 against rqrcode (39 vectors, ECC H, the text's bytes, no ECI, the 4-module quiet zone stripped) | Version equal **38/39**, module matrix equal **10/39** (the sizes agree: the masks differ). |
| S4 | Web Push wire format | **WebPush 1.0.13** (UTF-16 strings of `lib/net10.0/WebPush.dll`): `aesgcm`, `Encryption`, `salt=`, `Crypto-Key`, `p256ecdsa=`, `WebPush ` (the draft-04 coding and the pre-RFC 8292 header), no `aes128gcm`. **Lib.Net.Http.WebPush 3.3.1** through an in-memory handler, the subscription keys of `web_push_expected.json`: `POST`, `TTL: 2419200`, `Urgency: high`, `Content-Type: application/octet-stream`, `Content-Encoding: aes128gcm`, `Authorization: vapid t=...,k=...` (JWT header `{"typ":"JWT","alg":"ES256"}`, claims `aud` `https://fcm.googleapis.com`, `exp` +12 h, `sub`). Decrypted by RFC 8291 with the vector's receiver key: **the message, delimiter 0x02**, record size 4096. Statuses 410, 429 and 500 raise `PushServiceClientException` with the status; with `AutoRetryAfter = false` the 429 was not retried (4 requests for 4 sends). One delivery (encrypt, sign, in-memory send) **664 us** (the port measured 333 us to encrypt and 81 us to sign, `campfire-app-channels-integrations.md`). |
| S5 | Resilience (factory client over a counting handler) | `AddStandardResilienceHandler()`, POST to a 500: **4 attempts**, 11.1 s. With `Retry.DisableForUnsafeHttpMethods()`: POST **1 attempt**; GET to a 500 **4 attempts**, 5.8 s. `AddResilienceHandler` with `AddTimeout(300 ms)`: a 2 s POST failed with `TimeoutRejectedException` after 1 attempt, 312 ms; a fast POST passed. Cost a request (in-memory handler): none 1.20 us, timeout pipeline 2.85 us, standard 4.37 us. The resilience handler refuses the synchronous `HttpClient.Send` (`NotSupportedException`): every call is `SendAsync`. |
| S6 | Opengraph on AngleSharp 1.8.4 (`QuerySelectorAll "meta"` into the port's `MetaElement`, the rest of `OpengraphDocument` unchanged) | Recorded route bodies (`opengraph_cases.json`, every body with one): **55/55 same attributes** as the libxml2 scanner. `OpengraphTests`' probes: character references 14/20 (differ: `&AMP;`, `&#128;`, `&#150;`, `&#xD800;`, `&#1114112;`, `&#;`), tokenizer 22/29 (differ: a meta inside `textarea`, `title`, `template`; `<meta/property=.../content=...>`; an unterminated quoted value; NUL; CR LF normalized to LF). 5 MB pathological pages of `OpengraphTests`: 101-417 ms (scanner 38-174 ms), same attributes. A 140 KB page: **8.08 ms** against 0.79 ms. **Hostile 5 MB pages, uncapped: nested `div` over 150 s of CPU (stopped); misnested formatting over 25 s; `<b><p>` 2.3 s; nested table 0.8 s.** With depth > 256 stopping the parse: nested div, nested table, nested span, formatting 46-52 ms; `<b><p>` still 2.3 s. With both caps (depth 256, 100,000 elements): **every page 0.05-0.35 s**. |
| S7 | `RubyUri.parse` against `System.Uri` behind `Attachables`' RFC 3986 check (172 distinct strings: the recorded cases' URLs, `Location` headers and meta values, `UriTests`' inputs) | **140/172 the same outcome** (http or not; scheme, host, port, request target). Of the 32 others, 28 are non-URLs both refuse (Ruby: a relative `URI::Generic`; .NET: no absolute URI); `https:/rooms/1`, `https:rooms/1`, `https://` (Ruby: http with an empty host, refused by the guard; .NET: no URI); `http://2130706433/`, `http://0x7f.1/` (.NET normalizes the host to `127.0.0.1`; both refused, no lookup); **`mailto:foo`**: Ruby raises `InvalidComponentError`, .NET parses it. |
| S8 | Clocks | `PeriodicTimer(20 ms)` for 300 ms: `TimeProvider.System` 15 ticks, a subclass overriding `GetUtcNow` only 15, **`FakeTimeProvider` 0**. The subclass's `GetTimestamp` moves; `FakeTimeProvider.Advance(4 h)` moves `GetUtcNow` by 4 h. |
| S9 | Redaction and `FakeLogger` | `AddRedaction` + `SetHmacRedactor` (64-byte key, key id 1) for a `DataClassificationSet`, `ErasingRedactor` as fallback: `1:Tf1jMtAxwRm2h+yx+A5VxA==` (26 characters), stable across calls; the fallback erases to `""`; **0.33 us** a redaction. `FakeLogger` captured the record, its level and its formatted message. |

Projection on the five workloads: the post path changes only in where the job readers live (J1, same channels) and in the push job
losing a `Task.Run` (it still counts badges and checks each subscription's host, as now). The reads change nothing per request (no
user agent on the bench; the clock is one virtual call as before). Expect every cell within the control's range. The units that touch
the post path (8.2, 8.5, 8.7) bench it against the control anyway.

## 3. Types and modules

All in `src/Campfire.App` unless named. Compile order: `Platform/HarnessClock.fs` before `Config.fs`; `Integrations/Clients.fs` after
`Guard.fs` and before `Webhook.fs`; `Integrations/PushDeliveries.fs` after `WebPush.fs` (replacing `WebPushPool.fs`); `Logging.fs`
before `Web/HttpLog.fs`; `Boot.fs` last before `Program.fs` (it now also does what `Web/Host.fs`'s `build` did; `Host.fs` keeps Kestrel's
options, the listeners and F6's branch as functions `Boot.build` calls).

**`Platform/HarnessClock.fs`** (about 20 lines; J10)

```fsharp
type FrozenTimeProvider(at: DateTimeOffset) =        // Time.current frozen; timers and timestamps real
    inherit TimeProvider()
    override GetUtcNow: unit -> DateTimeOffset
module HarnessClock =
    val fromValue: string | null -> Result<TimeProvider, string>   // CAMPFIRE_FROZEN_TIME: RFC 3339, or System when unset/blank
```

**`Jobs.fs`** (about 120 lines; J1): `JobKind`, `Registry`, `Work` unchanged; `Jobs` (queues, `EventSink`, `PerformLater`, `Close`);
`type JobRunner(jobs: Jobs, app: AppState, registry: Registry<AppState>, options: IOptions<CampfireOptions>) = inherit BackgroundService`
(`ExecuteAsync`: `Task.WhenAll` of `JOB_CONCURRENCY` reader loops per kind; `StopAsync`: `jobs.Close()` then `base.StopAsync`).
`CoreJobs.withCoreJobs` and `IntegrationJobs.registerJobs` fill the registry, which is registered as a singleton.

**`Integrations/Clients.fs`** (about 80 lines; J2, J3)

```fsharp
module Clients =
    [<Literal>] val Opengraph: string = "opengraph"    // also Push, Webhook
    val Pinned: HttpRequestOptionsKey<IPAddress>       // "campfire.pinned"
    val add: IServiceCollection -> unit                // three AddHttpClient(...).ConfigurePrimaryHttpMessageHandler(...).AddResilienceHandler(...)
    val pin: IPAddress -> HttpRequestMessage -> HttpRequestMessage
    /// The body through `limit` bytes after decompression: ValueSome bytes, or ValueNone when it is longer.
    val readLimited: HttpContent -> int -> CancellationToken -> Task<byte[] voption>
    val contentType: HttpResponseMessage -> string option   // Net::HTTP's content_type over the raw header
```

The resolver (`IResolver`, `SystemResolver`) and the dial function are DI singletons, so tests replace them (`FakeResolver`, the
mapping dial) without touching the clients.

**`Integrations/Guard.fs`** (about 250 lines): the host rules and lists as now (lists as `IPNetwork` values), plus

```fsharp
    val connect: IResolver -> (IPEndPoint -> CancellationToken -> Task<Socket>)
                 -> SocketsHttpConnectionContext -> CancellationToken -> ValueTask<Stream>
```

A pinned address is re-checked with `blockedAddress`; an unpinned host goes through `resolve`. A refusal raises `HttpRequestException`
carrying the `GuardError`'s message (`Attempt to access private IP via <host>`), which the fetch maps back to `FetchError.Guard`.

**`Integrations/Webhook.fs`** (about 140 lines): `WebhookClient.deliver (factory: IHttpClientFactory) url payload` on the `"webhook"`
client (POST, `ByteArrayContent` with `Content-Type: application/json` and no charset, as Rails sends). The reply rules,
`mimeLookup` and `WebhookError` are unchanged; `Http of HttpError` becomes a small `Transport` case over J4's kinds.

**Opengraph** (`OpengraphFetch.fs` about 85, `OpengraphLocation.fs` about 70, `OpengraphDocument.fs` about 110 with the decoding and
`metaEncoding` moved in, `Opengraph.fs` about 35, `OpengraphMetadata.fs` unchanged but for `System.Uri`): `Location` takes the factory
and the resolver, `ResolvedIp` as now, and fetches with `Clients.pin`. `OpengraphFetch.request` is Rails' loop over `SendAsync` with
`HttpCompletionOption.ResponseHeadersRead`. `OpengraphDocument.metaElements: string -> MetaElement list` is the capped AngleSharp reader
(J5); the stop is an exception of its own, caught in `metaElements`, which returns the metas collected so far (each `meta` is added in
`OnCreated` and its attributes read when the next element is created or at the end, as `Html5` checks depth).

**`Integrations/WebPush.fs`** (about 110 lines) and **`Integrations/PushDeliveries.fs`** (about 55; J1, J7):

```fsharp
module WebPush =
    val deliver: PushServiceClient -> VapidAuthentication -> IResolver -> Notification -> Task<Result<int option, DeliveryError>>
    val deliverTestNotification: ...                     // unchanged shape
type PushDeliveries(...) = inherit BackgroundService    // DeliverLater, Queue (in id order), Pending; 50 readers, 1 invalidator
```

`Vapid.fs` keeps `VapidConfig.Create`'s pair check (about 20 lines) and hands out the library's `VapidAuthentication`.

**`Logging.fs`** (about 40 lines; J11): `Logging.add (services) (secrets)` (`AddRedaction` with the HMAC redactor under a
`DataClassification("campfire", "secret")`); `Logging.redact: IRedactorProvider -> string -> string`;
`Logging.redactBotPath: string -> string` (the key segment of `/rooms/:id/:bot_key/messages...`); the console logger's settings from
`createLoggerFactory`, now on the builder's `ILoggingBuilder`. `HttpLog.Interceptor` takes the redactor: on a bot API path it disables
`RequestPath` and adds the redacted path, as R1 does for the query.

**`Config.fs`** (about 110 lines; J13): `[<CLIMutable>] type CampfireOptions = { mutable SecretKeyBase: string; ... }` (today's
`AppConfig` fields), `CampfireOptions.configure: IConfiguration -> CampfireOptions -> unit`, `CampfireOptions.add: IServiceCollection
-> unit` (`AddOptions<CampfireOptions>().Configure<IConfiguration>(...).Validate(...).ValidateOnStart()`). **`Web/FrontSettings.fs`**
the same for Thruster's variables (`THRUSTER_<NAME>` first). `StoragePaths` unchanged.

**`Boot.fs`** (about 200 lines; J14)

```fsharp
module Boot =
    val build: args: string[] -> configure: (WebApplicationBuilder -> unit) -> WebApplication   // tests pass their overrides here
    val serve: string[] -> Task                                                                 // build, RunAsync, closeDatabase
    val backup: CampfireOptions -> ILogger -> unit
    val run: string[] -> int
type DatabaseStartup(...) = interface IHostedLifecycleService   // StartingAsync: Schema.prepare, Membership.disconnectAll
```

`AppState` loses `Clock` (gains `Time`) and is registered with a factory over its services. `AppState.WebPush` becomes
`PushDeliveries option`.

## 4. Units, in build order

One Sonnet builder per unit, about 90 minutes, from a clean `git status` on `dotnetway`, one commit per logical step (read `git diff
--cached --stat` first), a Phase log entry per unit (KLOC from `bin/kloc`, gates run, differences recorded).

**Common acceptance for every unit:**
- `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green: the markup lint and its self-test, the Dapper and EF guards, H1 (zero query compilations
  in steady state), and, from 8.1, the system-clock guard.
- `df -h /` first (12 GB; 15 GB before a bench). `bin/clean-docker` after image rebuilds, `bin/clean-parity` after pixel runs.
- Pixel slices and benches start detached with a status file and are polled in the foreground with bounded loops. One parity compare
  at a time; `realtime/**` one state per run.
- **The control image** is Phase 7's close, `2fae069`. Before the first rebuild, if `campfire-dotnetway:app`'s revision label is `2fae069`
  or `14a334a`, tag it `campfire-dotnetway:p8ctl`; otherwise build it from `2fae069` as Phase 7's gate built its control. It is removed
  when the phase closes.
- Benches: `bench/quick` interleaved against `p8ctl`, host probe and load recorded. A five-workload cell under 5x Rails blocks.
- Rebuild `campfire-dotnetway:app` from HEAD before any image gate (`CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build`,
  label checked).
- The app is pixel-identical after every unit.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 8.1 Time | `Platform/HarnessClock.fs` (J10). `AppState.Time`. `ClockTimeProvider` and `RailsCompat/Clock.fs` deleted, and their users (`Boot`, `RichText.fs`'s `AppRichText`, `Presenter`, `Controllers/Messages.fs`, `Channels/Identity.fs`, `WebPush.fs`'s `unixNow`, `WebPushPool`'s shutdown wait) on the provider. Timeouts in `Opengraph.fs`, `WebPush.fs`, `Webhook.fs`, `Jobs.fs` on the provider. The `bin/verify` guard (`DateTime(Offset)?\.(Utc)?Now` outside `Respond.fs`'s `Date` line). Tests: `Microsoft.Extensions.TimeProvider.Testing` and `Microsoft.Extensions.Diagnostics.Testing` 10.11.0; `TestClock` replaced by `FakeTimeProvider` everywhere (Db, RailsCompat, App tests and `tests/Shared`); `Support.bootSeededFrozen` takes a provider; `CapturingLoggers` replaced by `FakeLoggerProvider` and its collector (a `Text` helper over the snapshot keeps the call sites short). | TM1-TM5 (section 5) green; the guard's planted `DateTime.UtcNow` in a scratch copy fails `bin/verify`; image rebuilt; `bin/pixel-parity --fast --only 'rooms/show/designers,messages/**,users/profile/**,auth/**'` on every seed equal (message times, the QR code's 4 h expiry, the session cookie); `bin/cookie-roundtrip` 20/20; `bin/db-differential` rc 0 (every timestamp comes through the provider) |
| 8.2 Jobs | `JobRunner : BackgroundService` and `PushDeliveries : BackgroundService` (J1), registered as hosted services by `Boot`'s service registration; `Booted.Jobs`, `Runner`, `JobRunner.start` and `WebPushPool`'s thread and semaphores go; `Boot.drain` keeps only the database close; the push job's `Task.Run` goes. The registry is a DI singleton. | JB1-JB6 green; the pool tests (50 running, 10,000 waiting, the rest dropped and logged, invalidations in order) on `PushDeliveries`; P12 and HS11 (shutdown) green; image rebuilt; `bench/lib/logcount` SIGTERM run counts every line; `bin/pixel-parity --fast --only 'bot_api/**'` every seed and `realtime/message_sent` alone, equal; **`bench/quick` post a message c=1 and c=16, 3 reps, against `p8ctl`**: no cell under 5x, CPU per request recorded |
| 8.3 Clients, guard, webhook | `Integrations/Clients.fs` (J2), `Microsoft.Extensions.Http.Resilience` 10.11.0; `Guard.fs`'s lists on `IPNetwork` and `Guard.connect` (J3); the webhook on the `"webhook"` client with `System.Uri` (J4, J6); the resolver and dial as DI singletons; `IntegrationsSupport`'s `MappingDialer` becomes the test dial function. `Http.fs` stays for opengraph and push until 8.4 and 8.5. README class B: the webhook's request headers. | GuardTests **one for one** (every existing fact unchanged and green) plus GD1-GD4; **the 19 recorded webhook cases** (`webhook_cases.json`/`webhook_expected.json`): status and reply equal, the request compared on method, target, `Content-Type`, `Accept`, `User-Agent` and body, the header list's order dropped with its class B line; CL1-CL5 (POST sent once on a 500, on a reset and on a timeout; no redirect followed; proxy variables ignored); `bin/pixel-parity --fast --only 'bot_api/**'` every seed equal |
| 8.4 Opengraph | Fetch and location on the `"opengraph"` client with Rails' loop and pinned addresses (J3); `OpengraphDocument.metaElements` on AngleSharp with both caps (J5); `System.Uri` for every opengraph URL and the fxtwitter rewrite (J6); the module semaphore becomes the client's limiter; `OpengraphHtml.decode` and `metaEncoding` move into `OpengraphDocument.fs`. README class B: malformed pages (the 13 probes of S6), the depth and element caps, `mailto:` answering 204. | **The 90 recorded opengraph cases one for one**: response and lookups equal, requests compared on method, host and path (`Accept`/`User-Agent` too); the `mailto raises` case updated to 204 with its class B line and a test naming it. `OpengraphTests`' libxml2 probes: AngleSharp's answers are the new expectations, and the 13 differing probes are listed as class B in the test (a listed probe that stops differing fails); OG1-OG4 (hostile pages, caps, metas kept before the stop, gzip bomb); `UnfurlLinks` controller tests green; no pixel run (no state shows a live unfurl; `messages/opengraph*` render stored embeds) |
| **checkpoint** | Opus, after 8.4 (below) | |
| 8.5 Web Push | `Lib.Net.Http.WebPush` 3.3.1 on the `"push"` client (J7): `WebPush.deliver`, the test notification, `DeliveryError` from `PushServiceClientException`; `Vapid.fs` trimmed to the pair check; `WebPushEncryption.fs` no longer called. | WP1-WP8 green (the vector's decrypt round trip, headers and JWT claims, 410/404 destroy, 429 not retried, an endpoint outside the permitted hosts or resolving privately never connects, invalid VAPID keys turn push off and the boot continues, the test notification's redirect); image rebuilt; `bin/pixel-parity --fast --only 'users/push_subscriptions/**'` every seed equal; **`bench/quick` post a message c=1 and c=16 against `p8ctl`** (the push job runs on every post of the bench) |
| 8.6 Logging | `Logging.fs` (J11): `Microsoft.Extensions.Compliance.Redaction` 10.11.0, the HMAC redactor keyed from `secret_key_base`; the `HttpLog` interceptor's bot path; `PublicExceptions`' path; webhook URLs in job failures. Then the c=1 ladder (J12): each rung measured, kept only if it pays. README class B: the bot key and webhook URLs no longer appear in logs. | RD1-RD6 with `FakeLogger` green; SR1-SR8 and LG1-LG4 unchanged and green; image rebuilt; `bench/lib/logcount` N lines; **`bench/quick --routes avatar,static_css,up,room_show,post_message` c=1 and c=16 against `p8ctl` per kept rung**, and the Phase log records `static_css` against Rails' 15,167 / 98,776 req/s (back at Rails' level, or why not) |
| 8.7 Builder | `Boot.build` (J14): one builder, `AppState` from DI, `DatabaseStartup`, `Web.Host`'s pieces called from `Boot.build`, `Booted` and the boot variants gone; `Support` boots through `Boot.build` (in-memory configuration, provider and logger overrides, loopback Kestrel). `AppConfig` and `FrontSettings` are still read by today's functions, now from `builder.Configuration` (8.8 moves them to `IOptions`). **If it overruns:** commit the production path (`Boot.serve` on `Boot.build`, image green) with a thin test shim over the same registrations, and move `Support` in a follow-up unit. | HS1-HS12 green and **HS2/HS4 under `bin/linux-tests App`**, **AC11** and **`reference-tools/front/pebble-image.sh`** (Phase 7's condition: `Web/Host.fs` changes); P12; image rebuilt; `/up`, `POST /session` and a room page on `HTTP_PORT` and `TARGET_PORT`; `bin/asset-check` passes (the marker middleware and `appEndpointsOnly` unchanged); `bin/pixel-parity --fast --only 'auth/**,errors/**,rooms/show/designers'` every seed; `bin/cookie-roundtrip` 20/20; **`bench/quick` five workloads 3 reps against `p8ctl`**, no cell under 5x |
| 8.8 Options | `CampfireOptions` and `FrontSettings` on `IOptions` with `Validate` and `ValidateOnStart()` (J13); `EdgeConfig` from them; the VAPID check feeding `PushDeliveries`' registration; `ConfigTests` become OP tests. | OP1-OP7 green; **HS12** (Thruster's names, prefix and Go parsing) green unchanged; the image run with no `SECRET_KEY_BASE` exits 1 printing Rails' message; with `TLS_DOMAIN=10.0.0.1` exits 1 before binding; with a mismatched VAPID pair boots, logs "Web Push is off" and serves `/up`; `bin/pixel-parity --fast --only 'auth/**,account/**'` on `default` equal |
| 8.9 Delete | `Integrations/Http.fs`, `Net.fs` (what is left), `RubyUri.fs`, `OpengraphHtml.fs`, `OpengraphEntities.fs`, `WebPushEncryption.fs`, `WebPushPool.fs` if any remains, `Config.fs`'s `fromLookup` plumbing and `debugString`/`parseNumber` where options replaced them; tests `HttpTests.fs` (its two facts move to `Clients.readLimited`: the gzip bomb stops at 5 MB), `UriTests.fs` (its probes move to the `System.Uri` checks), `WebPushTests`' encryption-vector byte comparison (the round trip replaces it); `IntegrationsSupport`'s raw-socket server kept only if the recorded cases still need it (a `TestServer`-free Kestrel route table is enough). AGENTS.md (the layout table's App row: jobs, integrations, the clock; a bullet for the integrations' tests and the recorded cases), README "Known differences" (section 8), plan pointers, `bin/kloc`. | `git grep -n -e 'Http\.exchange' -e 'RubyUri' -e 'SharedClock' -e 'TestClock' -e 'ClockTimeProvider' -e 'WebPushPool' -e 'OpengraphHtml\.metaElements' -e 'Encryption\.encrypt' -e 'AppConfig\.fromLookup'` over `src tests bin AGENTS.md README.md` finds only history; `bin/kloc` recorded (section 7); image rebuilt, `--fast --only 'rooms/show/designers'` equal |

Then the **phase gates** (gate runner, plan section 3 and the Phase 8 row, in this order):
1. `CAMPFIRE_REQUIRE_SEED=1 bin/verify`.
2. **The recorded opengraph, webhook and push cases** and **the guard tests one for one** (inside `bin/verify`, named in the evidence
   with their counts).
3. `dotnet list package --vulnerable --include-transitive` on the App: none.
4. The slices `users/profile/qr_code`, `auth/incompatible_browser/**`, `users/push_subscriptions/**`, `bot_api/**` and `pwa/**` on every
   seed (lean).
5. Then **the lean pixel matrix on all seeds** (directory slices on `default`, the 13 `realtime/**` states one at a time, the other
   four seeds in one run).
6. `bin/cookie-roundtrip`; `bin/db-differential`.
7. **`bench/quick` five workloads**, c=1 and c=16, 3 reps interleaved, plus `avatar`, `static_css` and `up`, against a same-day `p8ctl`,
   host probe and load recorded (post a message carries the jobs).
8. `bin/kloc`.

**The checkpoint after 8.4** (Opus) reviews 8.1-8.4 against this note before push and the builder:
- No retry and no circuit breaker in any pipeline (CL1's count of 1).
- The guard in `ConnectCallback` on both guarded clients, with a pinned address re-checked: GD2's planted private pin is refused.
- The opengraph loop is Rails' (the recorded redirect cases).
- AngleSharp's caps bound the hostile pages (OG1), and the 13 class B probes are listed and fail both ways.
- `FakeTimeProvider` appears only in tests, and no production timer reads a frozen clock.
- Whether 8.7 (the builder, the largest unit) still fits 90 minutes, or should split now: the production path first, `Support` in 8.7b.

## 5. Tests

In `tests/Campfire.App.Tests` unless named. Hosts for the integration tests: the recorded cases' local server (`IntegrationsSupport`)
behind the test resolver and dial; for the app, `Support`'s boot through `Boot.build` from 8.7.

- **Time (8.1):**
  - TM1: `HarnessClock.fromValue`: unset and blank give the system clock; `2026-03-02T16:00:00Z` gives a provider whose `GetUtcNow`
    is that instant; `nonsense` is an error naming the variable.
  - TM2: a `PeriodicTimer` on the frozen provider ticks (at least 3 ticks of 20 ms within 2 s: a bound in the direction that cannot
    flake).
  - TM3: a message created under `FakeTimeProvider` at T has `created_at` T to the microsecond, and after `Advance(1 s)` an edit's
    `updated_at` is T + 1 s.
  - TM4: the session cookie's expiry and the QR transfer URL's expiry follow the provider (`Advance(4 h + 1 s)` expires the transfer).
  - TM5: Cable's heartbeat still beats under the harness clock (an app booted frozen sends `ping` frames).
  - Every `TestClock` test is ported to `FakeTimeProvider`, one for one.
- **Jobs (8.2):** JB1 a job of each kind runs once on its kind's worker; JB2 a full queue drops the job and logs it at Error
  (`FakeLogger`); JB3 a slow webhook handler does not delay a push job (per-kind workers); JB4 a handler that throws is logged as
  `job panicked` and the next job runs (the hosted service stays up); JB5 at stop, queued jobs drain before `StopAsync` returns, within
  the grace; JB6 `DisconnectUser` is not queued and reaches Cable directly. The existing `JobsTests` facts are ported one for one.
- **Clients and guard (8.3):**
  - CL1: a webhook POST answered 500, a POST whose connection resets and a POST that never answers (the 7 s timeout) each reach the
    server **exactly once**, the last answered "Failed to respond within 7 seconds".
  - CL2: no client follows a 302 by itself.
  - CL3: `HTTP_PROXY`/`HTTPS_PROXY` set in the process do not route a request.
  - CL4: the opengraph and webhook requests carry `Accept: */*` and `User-Agent: Ruby`; a push request carries the library's headers (class A).
  - CL5: `readLimited` stops a gigabyte gzip bomb at the limit (the old `HttpTests`).
  - GD1: an unpinned request through a guarded client to a host resolving to `127.0.0.1` is refused at connect, and the dial is never
    called.
  - GD2: a request pinned to `10.0.0.1` (planted) is refused at connect.
  - GD3: `IPNetwork` lists equal the old lists (every network's first and last address classified as before).
  - GD4: an IPv4-mapped IPv6 answer is classified as its IPv4 address.
- **Opengraph (8.4):**
  - The 90 recorded cases (as above) and the existing `OpengraphTests` facts ported.
  - OG1: S6's hostile 5 MB pages (nested `div`, misnested formatting, `<b><p>`, nested table) each stop or finish. The bound is on
    the caps' effect, not on wall time: the parse reports the stop, and at most 100,000 elements were created.
  - OG2: a head of metas, then 300 nested `div`s, unfurls the head's tags.
  - OG3: the 13 class B probes, listed with their new answers.
  - OG4: a `Location` to `10.0.0.1` after a public first hop is refused (each hop re-resolved and pinned).
- **Web Push (8.5):**
  - WP1: the payload decrypts (RFC 8291, the vector's receiver key) to `encoded_message`, delimiter 0x02.
  - WP2: `Content-Encoding: aes128gcm`, `TTL: 2419200`, `Urgency: high`, `Authorization: vapid t=...,k=<VAPID_PUBLIC_KEY>`, and the
    JWT's `aud` (`https://<host>`), `exp` (now + 12 h on the provider) and `sub` (`VAPID_SUBJECT`).
  - WP3: 410 and 404 destroy the subscription; 401, 403, 413, 429 and 500 are logged and keep it.
  - WP4: a 429 with `Retry-After` is sent once.
  - WP5: an endpoint whose host is not permitted, or resolves privately, is never connected.
  - WP6: invalid or mismatched VAPID keys: push off, a log line, the boot continues, pages carry no `vapid-public-key`.
  - WP7: the test notification redirects to the subscriptions page and delivers once.
  - WP8: the pool limits (as in 8.2).
- **Logging (8.6):** with `FakeLogger` over a booted app:
  - RD1: a bot API request's log line holds the HMAC form of the key and not the key.
  - RD2: a 500 on a bot path logs no key.
  - RD3: a webhook job failing on a bad URL logs no part of the URL.
  - RD4: a push failure logs the host and not the endpoint's path.
  - RD5: a failed sign-in, a throttled one and a join with a bad code log no e-mail address and no token.
  - RD6: the same key gives the same hash within an install, and other `secret_key_base`s give other hashes.
- **Options (8.8):**
  - OP1: every `ConfigTests` fact as an options fact (defaults, Ruby's number parsing and its error text, `SECRET_KEY_BASE_DUMMY`).
  - OP2: a missing `SECRET_KEY_BASE` fails `StartAsync` with Rails' message before a listener binds.
  - OP3: `THRUSTER_HTTP_PORT` wins over `HTTP_PORT`, and Go's `ParseBool`/`Atoi` apply.
  - OP4: `TLS_DOMAIN` with no usable name fails at start.
  - OP5: a bad `CAMPFIRE_FROZEN_TIME` fails at start.
  - OP6: invalid VAPID keys do not fail.
  - OP7: options are read once (a singleton `IOptions<T>`), not per request.

## 6. What is deleted

| What | Lines (corrected `bin/kloc`) | Unit |
|---|---:|---|
| `src/Campfire.RailsCompat/Clock.fs`, `Campfire.Db.Ef.ClockTimeProvider` | 33 + 5 | 8.1 |
| `Support.CapturingLoggers` (tests) | about 40 | 8.1 |
| `Jobs.fs`' `Runner` and `JobRunner.start`, `WebPushPool.fs`' thread and semaphores, `Boot`'s jobs and push drain | about 70 | 8.2 |
| `Guard.fs`' `UInt128` arithmetic (`v4`, `v6`, `inV4`, `inV6`, `ipv4Value`, `ipv6Value`) | about 60 | 8.3 |
| `Integrations/Http.fs` (the Net::HTTP replica), `Net.fs`'s `IDialer`, `TcpDialer`, `TlsTrust`, `Network` | 569 + about 40 | 8.9 (unused after 8.5) |
| `Integrations/OpengraphHtml.fs`' scanner, `OpengraphEntities.fs` | 257 - 40 kept + 42 | 8.9 (unused after 8.4) |
| `Integrations/RubyUri.fs` | 410 | 8.9 (unused after 8.5) |
| `Integrations/WebPushEncryption.fs`, most of `Vapid.fs` | 119 + about 50 | 8.9 (unused after 8.5) |
| `Booted`, `bootEdge`/`bootWithClock`/`bootWith`, `Host.build`'s separate builder | about 85 | 8.7 |
| `Config.fs`' hand-built `Result` chain | about 15 | 8.8 |
| Tests: `HttpTests.fs`, `UriTests.fs`, the encryption byte vector in `WebPushTests` | 51 + 160 + about 60 | 8.9 |
| Not deleted (the plan expected them to go): `Concerns/UserAgent.fs` (813), `Controllers/QrCode/Rqrcode.fs` (452) | | J8, J9 |

## 7. KLOC projection

`bin/kloc` at `2fae069` (and `14a334a`): **27,861 corrected** (27,406 cloc); App 13,038.

| | lines |
|---|---:|
| `Http.fs` -569, `Net.fs` -40, `RubyUri.fs` -410 | -1,019 |
| `OpengraphHtml.fs` + `OpengraphEntities.fs` -299, AngleSharp reader with caps, decoding and `metaEncoding` +70; `OpengraphFetch` -27, `OpengraphLocation` -7, `Opengraph.fs` -6 | -269 |
| `WebPushEncryption.fs` -119, `Vapid.fs` -49, `WebPush.fs` -59, `WebPushPool.fs` -94 + `PushDeliveries.fs` +55 | -266 |
| `Webhook.fs` -40, `Guard.fs` -60, `Clients.fs` +80 | -20 |
| `Jobs.fs` -26, `IntegrationJobs.fs` -14 | -40 |
| `Clock.fs` -33, `ClockTimeProvider` -5, `HarnessClock.fs` +20 | -18 |
| `Boot.fs` -69, `Host.fs` -14, `AppState.fs` -9, `Config.fs` -14, `FrontSettings.fs` -7 | -113 |
| `Logging.fs` +40, `HttpLog.fs` +5, `PublicExceptions.fs` +3 | +48 |
| **Phase 8 net** | **about -1,700** |

Project: **27.9 -> about 26.2 KLOC** (range 26.0-26.5). The plan's row is 21.5 -> 18.5: the same base drifted by 6.4, and the 1.27 KLOC of
the user-agent and QR ports stay (J8, J9). The integrations after: about 1.4 KLOC (guard 0.25, opengraph 0.45, webhook 0.14, push 0.2,
clients 0.08, jobs 0.3). Rust's bar is 31.0.

## 8. Known differences this phase records (README "Known differences", plan section 4)

A new README entry **Integrations** (8.9 writes it; each line names its test):
- **Class B:**
  - Bots and sites see .NET's request headers. `Accept: */*` and `User-Agent: Ruby` are kept; header order, `Connection`, and
    `Accept-Encoding: gzip, deflate` for Net::HTTP's weighted list differ (WH cases, CL4).
  - A webhook reply that trickles is cut at the 60 s delivery deadline, not at 7 s between reads. The 7 s applies up to the response
    head (CL1).
  - Unfurls allow 16 requests at once rather than 16 unfurls (unchanged deadline).
  - Malformed pages unfurl as an HTML5 parser reads them: the 13 probes of S6 (OG3).
  - A page nested deeper than 256 or holding more than 100,000 elements unfurls only the tags before that point (OG1, OG2; libxml2
    halts at 256 too).
  - `mailto:` URLs that Ruby's parser refuses answer 204 instead of Rails' 500 (the recorded case).
  - The bot key in a bot API path and webhook URLs in job failures are logged as HMAC hashes (RD1-RD3). Rails logs the path as sent.
  - A push service's 429 is not retried; it was not in Rails either, and the test pins it (WP4).
- **Class A, once:**
  - The request target as `System.Uri` normalizes it (lower-case host, dot segments).
  - The push payload's record size and padding, and the JWT's bytes (WP1, WP2).
  - Pooled push connections, and push requests without Net::HTTP's `Accept`/`User-Agent` (push services read neither).
- **Kept, no new difference:** the user agent's reading and the QR code (J8, J9), the job model (Rust's "Jobs" difference stands: in
  process, lost on a crash), the push limits (50 running, 10,000 waiting), the unfurl and webhook limits.

## 9. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | A retry or breaker slipping into a pipeline (a template, `AddStandardResilienceHandler` copied from docs), so a bot is posted to twice | CL1's count of exactly 1 on three failure kinds; the checkpoint reads every `AddResilienceHandler` |
| 2 | A guarded client that does not run the guard (a request built without the pin, a client added without the callback) | GD1 and GD2 (planted), OG4; `Clients.add` is the only place a primary handler is built, so the checkpoint greps `SocketsHttpHandler` |
| 3 | AngleSharp's cost on hostile pages beyond the two caps (another quadratic shape) | OG1 at the caps; S6's numbers; the parses stay at 4 permits, so a hostile pattern costs at most 4 cores for its duration; add the pattern to OG1 and lower the element cap if one appears |
| 4 | The recorded cases leaning on Net::HTTP details beyond headers (`Content-Length` after decompression, a `HEAD` body, a `Location` spelled oddly) | 8.3 and 8.4 run the cases first, before deleting anything; each case that changes is either a class B line with its reason or a fix |
| 5 | The harness clock frozen too widely (a timer reading `FakeTimeProvider`, or a `FrozenTimeProvider` timer) | TM2 and TM5; `realtime/**` in the gate (heartbeats); the checkpoint greps `FakeTimeProvider` under `src` |
| 6 | Shutdown order with hosted services (jobs drained after the database closes; a push queued by a draining job after `PushDeliveries` stopped) | JB5, P12, HS11; `bench/lib/logcount`'s SIGTERM run; registration order is commented where it matters |
| 7 | `Lib.Net.Http.WebPush` drift or advisories (single maintainer, 19 months since a release) | pinned in `Directory.Packages.props`; `dotnet list package --vulnerable` in 8.5 and the gate. WP1/WP2 pin the wire format, and the port's encryption is in git history if the package ever has to go |
| 8 | The builder (8.7) overrunning and leaving production and tests booting two ways | the split rule in 8.7; the checkpoint decides before 8.5 starts |
| 9 | `ValidateOnStart` failing after side effects (a database file created before validation) | OP2 and OP5 assert nothing is written to the storage directory on a failed start; `DatabaseStartup` runs in `StartingAsync`, after validation |
| 10 | The request log ladder changing the line operators read | LG1-LG3 and SR1-SR3 unchanged; J12's rule (a rung that drops a field is not kept) |
| 11 | Redaction cost or a redactor on a hot path | only bot API paths and failure lines call it; `bench/quick`'s room page and post rows in 8.6 against `p8ctl` |
| 12 | Disk: image rebuilds in 8.1-8.8, the control image, pixel runs | `df -h /` before each; `bin/clean-docker` after each image; `p8ctl` and Phase 7's leftovers (`p7gate`, `p7pre`, `p6ctl`, `campfire-candidate-p6ctl`) removed when the phase closes, as the orchestrator allows |

## 10. Questions for Chris

None. The following are settled by D9, the parity gate and the spikes, and recorded above:
- The two packages the spikes refused (MyCSharp, QRCoder): class C on the gate's own states.
- WebPush 1.0.13 for Lib.Net.Http.WebPush (wire format).
- Retry-free pipelines (double delivery, recorded cases).
- The guard in `ConnectCallback` with Rails' redirect loop (recorded cases).
- AngleSharp opengraph with caps (a DoS otherwise).
- `System.Uri` (Phase 5's R9).
- The frozen provider over `FakeTimeProvider` in production (timers).
- Redaction of bot keys (the plan's list).
- `IOptions` with `ValidateOnStart` and the builder (plan 2.8).
