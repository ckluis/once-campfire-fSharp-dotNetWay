# Design note: the front server on Kestrel and framework middleware (Phase 7)

Status: design, 2026-10-26, before Phase 7 builds. Author: the Opus designer (plan section 5). Binding for the Phase 7 builders
together with `plans/dotnetway.md` sections 3-5 and D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`).
Where this note and plan section 2.4 ("The front server", "Response caching and compression") disagree, this note wins: it is later
and rests on the spikes in section 2. The disagreements, each argued below: TLS goes through Kestrel's TLS handshake callback, not
`ServerCertificateSelector`, because the selector sends the leaf without its intermediate (F3, S3); OutputCache is one base policy per
route family that Rails marks `public`, not a response-driven "cache what says public" policy, because the middleware fixes the
expiry and the buffering at the first body write (F9, S4); the cache sits outside response compression, varying by
`Accept-Encoding`, as Thruster's did; certificates are ordered at boot and renewed by the background service rather than on a
handshake (F4); about -2.35 KLOC instead of -2.4 (section 7).

Scope: `src/Campfire.Kit` (all of it: `Server.fs` and `Front/` `Acme.fs`, `Cache.fs`, `Compression.fs`, `Config.fs`, `Conn.fs`,
`Front.fs`, `Handler.fs`, `RequestLog.fs`, `Tls.fs`; 2,842 lines by `bin/kloc`), `tests/Campfire.Kit.Tests`, the `ZstdSharp.Port`
package, and the App's uses (`Boot.serve`, `Boot.createLoggerFactory`, `Web/Edge.fs`'s HSTS and `force_ssl` redirect,
`Web/Services.fs`' pipeline and compression providers, the test hosts' `Support.startPipelineOn`), plus `bench/front`,
`bench/log-micro`, `bench/lib/logcount` and the parity harness's candidate (a TLS mode for the gate). Out of scope: Rails' request
layer (`Web/`, Phase 2's), the IOptions move of the rest of the configuration (Phase 8), `TimeProvider`/`FakeTimeProvider` adoption
(Phase 8, per the decisions file), the Cable transport.

**Preconditions.** Phases 1-6 closed: Phase 6 closes at `309ab42` (its verifier's entry is `8df0b2b`). The App is an Sdk.Web project
whose pipeline (`Web.Services.pipeline`) runs on a branch of the front server's `WebApplication` (`Front.serveWithLines`), with
`StaticAssets.hostOptions`/`configureHost`; the framework's response compression (K11) is behind `StaticAssets.map`; authentication
runs inside the actions (K2: no default scheme, no `UseAuthentication`); `Edge` does `assume_ssl`, `force_ssl`'s HSTS and the 16 MiB
body cap. Phase 6's conditions stand: keep the startup validation and "nothing renders before `StaticAssets.map`"; keep
`StaticAssets.map`'s marker middleware and `appEndpointsOnly` in place whenever the pipeline order changes (`bin/asset-check` N1/N3
after any change to the order); keep H1, the markup lint and the Dapper guard green.

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity or the 5x floor forces it), with
the reason.

| # | Decision | Reason |
|---|---|---|
| F1 | **The App builds its own host** (`Web/Host.fs`): `WebApplication.CreateSlimBuilder(StaticAssets.hostOptions args)`, `configureHost`, the app's services, Kestrel's options and listeners, and the app's pipeline on the host itself (no branch, no `dispatch` by port, no `FrontHandler` body feature). `Front.serveWithLines`, `FrontHandler`/`FrontResponse`, `FrontConn`, `NoLifetime` go. | Thruster's job is now middleware and Kestrel options; a host of the app's own is how an ASP.NET Core app is written. The port picks behaviour in one place only: the HTTP port when TLS is on (F6). |
| F2 | **Listeners:** `HTTP_PORT` (any address, HTTP/1.1); `TARGET_PORT` on `TARGET_BIND` (loopback by default, HTTP/1.1; skipped with a warning when it equals another port, as now); with `TLS_DOMAIN`, `HTTPS_PORT` (any address, `Http1AndHttp2`). HTTP/3 off. **No h2c**: `H2C_ENABLED` is no longer read. | The plan's listener set. h2c needed a reflection hack into Kestrel's internal `HttpProtocolsFeature` (`FrontH2c`); nothing in Campfire's deployment speaks cleartext HTTP/2 (class B, plan section 4 already lists it). |
| F3 | **TLS is `ListenOptions.UseHttps(TlsHandshakeCallbackOptions)`**, the callback answering from the certificate store (F4) a cached `SslServerAuthenticationOptions` per domain: `ServerCertificateContext = SslStreamCertificateContext.Create(leaf, intermediates, offline = false)`, ALPN `h2`, `http/1.1`. The server name is normalised as autocert does (IDN to ASCII, lower case, trailing dot; no name, an IP or a name without a dot fails); a name outside `TLS_DOMAIN` fails the handshake; a `TLS_DOMAIN` name whose first certificate is still being ordered waits for it, bounded by the handshake timeout (`HTTP_READ_TIMEOUT`). **Not** `HttpsConnectionAdapterOptions.ServerCertificateSelector`. | Spike S3 on Linux (the image's platform): with the selector Kestrel sent the leaf alone, with or without `ServerCertificateChain` (that property applies to a static certificate only); `openssl` failed verification (code 20) and `curl` exited 60. Through the callback with a certificate context the intermediate went out, verification passed and curl got HTTP/2. Let's Encrypt leaves are issued by an intermediate, so the selector would break every client that does not fetch intermediates (curl, bots, webhook callers). The callback is the framework's API for per-connection TLS options; it is async, which lets a first handshake wait for the first order as autocert's did. |
| F4 | **ACME is Certes 4.1.0 in `AcmeService : BackgroundService`**, HTTP-01 only (Q2). At start it loads each `TLS_DOMAIN` name's cached certificate (F5) into the store; after `IHostApplicationLifetime.ApplicationStarted` (the HTTP listener must answer the CA) it orders what is missing or due, one domain at a time, and sleeps until the next due time. **Due** = `NotAfter - min(30 days, lifetime / 3) - jitter(0..1 h)`; a failed order is retried 30-60 minutes later (autocert's `renewJitter` rule) and logged as an error. The account (`acme_account+key`) is registered or found lazily, only when an order is needed (`NewAccount(termsOfServiceAgreed = true)`, with `EAB_KID`/`EAB_HMAC_KEY` as external account binding when both are set). The certificate key (ECDSA P-256) and the CSR are made by .NET (`CertificateRequest.CreateSigningRequest`); Certes posts the JWS, polls, finalizes (`IOrderContext.Finalize`) and downloads the chain (`Download` then `ToPem(null)`). Its HTTP goes through `IHttpClientFactory`'s named client `"acme"` (a `User-Agent` naming Campfire; tests give it a primary handler trusting Pebble's root). | The plan's 2.4 and Q2 decided Certes and HTTP-01. Spike S2: Certes 4.1.0 ordered a certificate from Pebble in 383 ms (account 115 ms, order ready after 2 polls) and reused the account on a second run; it read the SEC1 account key autocert writes. Ordering at boot instead of on the first handshake removes the "obtaining" bookkeeping whose race made `FrontAcmeTests` flaky (`ObtainingCount`), and the wait in F3 keeps autocert's visible behaviour for the first visitor. `lifetime / 3` is for CAs that issue short-lived certificates (Pebble's default profile issued 6-day leaves in S2; Let's Encrypt is shortening lifetimes): renewing 30 days before a 6-day certificate expires would order forever. |
| F5 | **The certificate cache stays autocert's `DirCache`** under `STORAGE_PATH` (default `./storage/thruster`): `<domain>` holds the SEC1 `EC PRIVATE KEY` PEM then the chain, `acme_account+key` the account's SEC1 PEM; the directory `0700`, files `0600`, each written to a temporary file and renamed. Read with the framework: `X509Certificate2.CreateFromPem(text, text)` (it refuses a key that does not match, S7), `X509Certificate2Collection.ImportFromPem` for the intermediates, `X509Certificate2.MatchesHostname` for coverage, `NotBefore`/`NotAfter` for currency. Only `TLS_DOMAIN` names are loaded. | Data compatibility: an install that ran Thruster (or the port) keeps its certificates and account, so a switch costs no order and no rate limit, and a rollback finds files Thruster reads. The framework now does what `AcmeFiles.parseCached`, `covers` and `privateKeyPem`'s hand-built DER did (`ECDsa.ExportECPrivateKey` writes SEC1 with the curve named). Serving cached names that left `TLS_DOMAIN` (autocert does) is dropped: an operator who removes a name means it (class B). |
| F6 | **The HTTP port with TLS on** is a terminal branch (`MapWhen` on `Connection.LocalPort = HTTP_PORT`): `GET /.well-known/acme-challenge/{token}` answers the key authorization from the store (`text/plain`, 404 for an unknown token), everything else goes to **`UseHttpsRedirection`** (`RedirectStatusCode = 301`, `HttpsPort = HTTPS_PORT`). Without TLS the HTTP port serves the app, as now. | Thruster's `manager.HTTPHandler(httpRedirectHandler)`, in framework parts. S6: 301 to `https://<host><path>?<query>` for every method, the port omitted when it is 443 and named otherwise, an empty body. Differences from Thruster (class B): a `Host` outside `TLS_DOMAIN` is redirected rather than answered 421; no "Moved Permanently" body; a non-443 `HTTPS_PORT` appears in the `Location`. |
| F7 | **HSTS is `UseHsts`** (`MaxAge` 730 days, `IncludeSubDomains`, `ExcludedHosts` cleared), registered when `force_ssl` is on and placed after the `assume_ssl` middleware; Edge's hand-written `strict-transport-security` line goes. **Edge's `force_ssl` redirect is deleted.** Edge keeps the request id, `X-Runtime`, the session-cache fix and `flagCookiesAsSecure`. | `ActionDispatch::SSL`'s header, the framework's way; Rails sends it to every host, hence the cleared exclusions (`localhost` is excluded by default). The redirect is unreachable: production pairs `force_ssl` with `assume_ssl` (`EdgeConfig.production`), which makes every request HTTPS, and `DISABLE_SSL` turns both off; the only HTTP-to-HTTPS redirect an install ever sent was Thruster's (F6). Check in 7.4 whether an exception page (`UseExceptionHandler` clears the headers) loses HSTS; if so it is a class A line. |
| F8 | **The request log is `UseHttpLogging`**: `CombineLogs = true`, fields `RequestMethod`, `RequestPath`, `RequestQuery`, `RequestProtocol`, `ResponseStatusCode`, `Duration` (no headers, no bodies), plus an `IHttpLoggingInterceptor` that adds `remote_addr` (the connection's address after the forwarded-headers middleware, IPv4-mapped addresses unmapped) and `user_agent`; written by the app's console logger (`Microsoft.AspNetCore.HttpLogging` at Information beside the `Microsoft` = Warning filter). `LOG_REQUESTS=false` leaves the middleware out. `RequestLog.fs` (the batched UTF-8 writer) goes. | The plan's 2.4. Spike S5 (Linux, a 15 us endpoint): the console path costs +3.5 to +5.9 us a request at c=16 and +14 to +38 us at c=1 against no log (the batched writer cost +1.2 us, `phase7-crypto-logging.md`). On the thinnest cells that is at most -0.4% at c=16 and -2.3% (post) to -7% (messages page) at c=1, against Phase 6's 6.05x / 6.63x / 10.76x: nothing near 5x, and D9 accepts the cost. Thruster's line had the client address and user agent; operators grep for them. Log format is class B (plan section 4). |
| F9 | **OutputCache replaces `Front/Cache.fs`**: `AddOutputCache` with `UseCaseSensitivePaths = true`, `SizeLimit = CACHE_SIZE` (64 MiB) and `MaximumBodySize = MAX_CACHE_ITEM_SIZE` (1 MiB), and **one base policy per route family that Rails marks `public`**, built with `OutputCachePolicyBuilder`: `.With(path matches && no Range header).Expire(<the controller's max-age>).SetVaryByHeader("Accept-Encoding")`. The families and lifetimes: `/users/:user_id/avatar` 30 min (`expires_in 30.minutes, public: true`), `/account/logo` 5 min, `/qr_code/:id` 1 year, `/rails/active_storage/{blobs,representations}/proxy/...` 100 years (`http_cache_forever(public: true)`), `/rails/active_storage/disk/...` 1 h. `UseOutputCache()` sits **just outside `UseResponseCompression`** (after `StaticAssets.map`), so an entry is the encoded response for one `Accept-Encoding`, as Thruster's entry was the app's gzipped response under its `Vary`. No tag eviction. | S4. A response-driven policy (cache what says `public` with a positive max-age, Thruster's rule) does not work on OutputCache: the middleware takes the expiry and decides whether to buffer the body at the first body write, before any policy's `ServeResponseAsync` runs, so a `max-age=1` entry was still served after 4 s (the 60 s default applied), and every GET page would be copied into the cache's buffer before being refused. The default locking also made 16 concurrent GETs of one uncacheable URL take 413 ms instead of 207 (the room page at c=16 is exactly that). Path-scoped policies leave every other request untouched: no lookup, no lock, no buffer (+0.1 to +0.8 us a request, S5). The lifetimes come from the controllers' own constants, so they cannot drift. Avatar and logo URLs carry `?v=updated_at`, so a change makes a new key and an old entry is never linked again, as with Thruster: eviction would add code for no visible effect. Outside compression a hit costs no compression (S4: gzip, identity, Chrome's header (`br`) and zstd clients each got their own entry and the right encoding). |
| F10 | **Compression is the app's `UseResponseCompression` alone**, which gains `ZstandardCompressionProvider` (.NET 11) registered **after** Brotli and Gzip. Thruster's `gzhttp` layer (Vary on every response, compression of what the app left alone, BREACH jitter padding) and `ZstdSharp.Port` go; `GZIP_COMPRESSION_ENABLED`, `GZIP_COMPRESSION_DISABLE_ON_AUTH` and `GZIP_COMPRESSION_JITTER` are no longer read. | The framework already compresses what the app renders (K11) and `MapStaticAssets` serves assets precompressed. With zstd registered last, a browser that offers `br` keeps `br` (S4: Chrome's `gzip, deflate, br, zstd` got `br`) and a zstd-only client still gets zstd, as from Thruster, for one line. What `gzhttp` added is invisible on screen: `Vary: Accept-Encoding` only on compressed responses, images (the uncompressed `campfire-icon.png`) no longer gzipped by a front (class A, network layer); no jitter padding (class B; Rails' `Rack::Deflater` has none either). |
| F11 | **Forwarded headers follow `FORWARD_HEADERS`**: when on (Thruster's default without TLS) `ForwardedHeadersOptions.KnownIPNetworks` is Rails' `TRUSTED_PROXIES` (as now); when off (the default with TLS) it is empty, so a client's `X-Forwarded-*` is never honoured. Thruster's request rewriting (`X-Forwarded-For/-Host/-Proto` added for the app, `Forwarded` dropped, `X-Request-Start`, HTTP/2 shown as HTTP/1.1) goes. | With Thruster gone the app sees the client's own connection. Thruster with `FORWARD_HEADERS` off replaced the client's headers with its own, so Rails' `remote_ip` was the peer: trusting no network gives the same. With it on, Thruster appended the peer, which Rails' trusted walk skipped: the middleware's walk from the peer gives the same `remote_ip` for public and private peers alike. The ban check and the login throttle read it, so tests pin it (HS7). `X-Request-Start` fed only Rails' queue-time metrics; the app reads none of these (class A). |
| F12 | **Limits and timeouts are Kestrel's**: the head limits of `Front.configureKestrel` (417,792 bytes of headers, the 65,534-byte target plus method, UTF-8 response headers, no `Server` header) move to `Web.Host.configureKestrel`; `HTTP_IDLE_TIMEOUT` is `KeepAliveTimeout`, `HTTP_READ_TIMEOUT` is `RequestHeadersTimeout` and the TLS handshake timeout (values of 49 days or more mean no timeout); request and response bodies fall under Kestrel's default minimum data rates (240 bytes/s after a 5 s grace). `DeadlineStream` and `withDeadlines` go; `HTTP_WRITE_TIMEOUT` is no longer read. `MAX_REQUEST_BODY` (Thruster's, 0 = none) lowers Edge's 16 MiB cap when set. | D9: the framework's limits instead of Go's deadlines re-built by hand. A slow body or a slow reader is cut by rate rather than by a fixed deadline (class B, operator-visible only). One cap for bodies, the app's, so a 413 looks the same whichever limit hit. |
| F13 | **Shutdown is the generic host's**: `ConsoleLifetime` (SIGTERM, SIGINT) with `HostOptions.ShutdownTimeout = Boot.ShutdownGrace` (10 s); `Boot.serve` awaits `app.RunAsync()`, then drains jobs, Web Push and closes the database as now. Cable's restart on `ApplicationStopping` is unchanged. `Server.shutdownSignal` and `NoLifetime` go; **`Server.raiseOpenFileLimit` moves into the App** (`Platform/OpenFiles.fs`) unchanged. | The framework's lifetime does what the hand-written signal task did. The open-file limit has no framework equivalent and every WebSocket is a descriptor; it stays (about 40 lines). The grace becomes one 10 s budget instead of Thruster's 5 s inside the app's 10 s (class B, operator-visible). |
| F14 | **Thruster's variables keep their names**, `THRUSTER_` prefix and Go parsing: `FrontConfig.fromLookup` moves into the App as `Web/FrontSettings.fs`, trimmed to what is still read (`HTTP_PORT`, `HTTPS_PORT`, `TARGET_PORT`, `TARGET_BIND`, `TLS_DOMAIN`, `ACME_DIRECTORY`, `EAB_KID`, `EAB_HMAC_KEY`, `STORAGE_PATH`, `HTTP_IDLE_TIMEOUT`, `HTTP_READ_TIMEOUT`, `FORWARD_HEADERS`, `MAX_REQUEST_BODY`, `CACHE_SIZE`, `MAX_CACHE_ITEM_SIZE`, `LOG_REQUESTS`). No longer read: `H2C_ENABLED`, `GZIP_COMPRESSION_*`, `HTTP_WRITE_TIMEOUT`, `DEBUG` (class B, one README line). | An install's environment keeps working; Phase 8 moves this record to `IOptions` with the rest of the configuration (plan 2.8), so it moves once more there rather than twice now. |
| F15 | **Tests are HTTP-level in `Campfire.App.Tests`**; `Campfire.Kit.Tests` goes with its stub CA (`AcmeStub`). The CA test is **Pebble** (`PEBBLE_DIRECTORY` and `PEBBLE_ROOT` set it on; skipped otherwise; the phase gate runs it). ACME's other tests need no CA and **no timers**: the schedule is a pure function, one renewal pass (`RenewDueAsync now`) is called directly with the time it is given, the store is inspected after the call returns. Nothing asserts a count another thread changes, and nothing asserts a wall-clock bound. | Pebble is a real RFC 8555 server (the plan's gate); a stub of our own tested our client against our reading of the RFC. The `ObtainingCount` flake came from asserting a dictionary that a task's `finally` empties after the awaited result is observed; with no handshake-driven ordering and no timers in tests, the race is gone by construction. |
| F16 | **The gate's TLS states run through a harness TLS mode** (`PARITY_CANDIDATE_TLS=1`): the candidate gets `TLS_DOMAIN=campfire.localhost`, `HTTPS_PORT` = the candidate's port and a self-signed certificate for that name written into its `STORAGE_PATH` `DirCache` before boot (so no order is made), and the capture's origin is `https://campfire.localhost:<port>` (Chromium resolves `*.localhost` to loopback; the browser context already sets `ignoreHTTPSErrors`). The reference stays on HTTP. | The plan's gate (`auth/sign_in` and `rooms/show/designers` over TLS) needs the harness to reach the candidate over TLS; the cache-file route exercises F3 and F5 exactly as an install with a cached certificate does, without a CA in the harness. Pebble covers ordering (F15) and, in 7.5, the image end to end. |

What stays hand-written, and why: the ACME glue on Certes (the order loop, the schedule, the challenge store: the protocol is
Certes'), the `DirCache` reader and writer (Thruster's file layout is data compatibility), the TLS callback's name normalisation
(autocert's rules), the OutputCache route predicates (Rails' public routes are not ASP.NET endpoints: the route table is Journey's,
K9), the logging interceptor's two fields, the Thruster variable reader (until Phase 8), the open-file limit.

## 2. Spikes (scratch project under the session scratchpad, 2026-10-26, deleted after)

.NET SDK 11.0.100-rc.2.26504.105 (net11.0), F# console on `Microsoft.NET.Sdk.Web`. Behaviour runs on the Mac; TLS, ACME and costs in
the toolchain image (`campfire-fsharp-toolchain`, aarch64, .NET 11 RC2) in colima with host networking, the server pinned to CPUs 0-3
and `bench/loadgen` to 4-7.

| # | Question | Result |
|---|---|---|
| S1 | Packages, versions, advisories | **Certes 4.1.0** (2026-09-27; 4.0.0 2026-09-23; 5.0.0-beta.1 2026-09-29), MIT, `fszlin/certes` not archived, last push 2026-10-19, 3 open issues; assets net10.0, net8.0, netstandard2.0 (net10.0 runs on net11.0); one dependency, **`BouncyCastle.Cryptography` >= 2.7.0** (2.7.0, 2026-07-30). Neither has a vulnerability or deprecation in its NuGet registration. Certes 4.1 also has ARI (`RenewalInfo`) and profiles; not used (F4). `ZstdSharp.Port` is referenced only by `Campfire.Kit` and its tests. The .NET 11 RC2 shared framework has `ZstandardCompressionProvider`, `HstsMiddleware`, `HttpsRedirectionOptions.RedirectStatusCode`. |
| S2 | Certes against Pebble, HTTP-01 | Pebble `ghcr.io/letsencrypt/pebble@sha256:ddf230642b1a584f519f32e347de1b05a6e4c1f6c35c1863b33effeab5f78199` (image of 2026-04-20), host network, `--add-host campfire.test:127.0.0.1`, `PEBBLE_VA_NOSLEEP=1`, `PEBBLE_WFE_NONCEREJECT=0`; directory `https://localhost:14000/dir`, the HTTP-01 answer on port 5002 (Pebble's `httpPort`) from a Kestrel `MapGet`. Run 1: account key made by .NET and written as SEC1, account `Valid` in 115 ms, order `Ready` after 2 polls (246 ms), finalized with a .NET CSR, chain downloaded (2 certificates), cache file written, **383 ms** in all. Run 2: **Certes read the SEC1 account key directly** (`KeyFactory.FromPem`), same account URL, 387 ms. The cache file read back with `CreateFromPem`; `SslStreamCertificateContext.Create(leaf, [intermediate])` built. The leaf: SAN `campfire.test` only (no CN), **valid 6 days** (`notBefore` 2026-10-26, `notAfter` 2026-11-01). |
| S3 | Kestrel TLS: does the certificate selector send the chain? (Linux) | A root, an intermediate and a leaf for `campfire.test` made with `CertificateRequest`, the leaf's cache file in autocert's layout. `ServerCertificateSelector` returning the leaf: **1 certificate sent**, `openssl s_client -verify_return_error` code 20 (unable to get local issuer), `curl --cacert root` exit 60. The same with `ServerCertificateChain` set: identical. `UseHttps(TlsHandshakeCallbackOptions)` with `ServerCertificateContext = SslStreamCertificateContext.Create(leaf, [intermediate])`: **2 certificates**, verify code 0, `curl --http2` answered `hello HTTP/2`. |
| S4 | OutputCache: does it reproduce Thruster's cache? | Response-driven base policy (reads `Cache-Control` in `ServeResponseAsync`): keys honour the query (`?v=2` a miss) and case-sensitive paths (`/Avatar` a miss); `If-None-Match` answered 304 from the entry; HEAD and `Range` miss; a `Set-Cookie` response never stored; **but a `max-age=1` entry was served 0.5, 1.5, 2.5 and 4.5 s later** (`Age` 0-4): `FinalizeCacheHeaders` reads the expiry on the first body write, before `ServeResponseAsync` (`OutputCacheMiddleware.cs`, `main`). **With `AllowLocking` (the default policy's), 16 concurrent GETs of one uncacheable 200 ms URL took 413 ms, without 207 ms.** Builder policies (`With(path && no Range).Expire(t)`): the 1 s entry expired; non-matching paths ran the app every time with no lock (206-209 ms); the default policy's 200-only rule did not store a public 302; a request with `Authorization` bypassed. Placement: inside compression, hits re-compressed per request (gzip 97 B, identity 1,615 B, Chrome's header `br` 81 B, zstd-only `zstd` 75 B); **outside compression with `SetVaryByHeader("Accept-Encoding")`**: each encoding its own entry (first request a miss), the same encodings and sizes, hits without compression. |
| S5 | Hot-path cost (Linux; `GET /x`, 2,400-byte body; CPU per request from the process over 8 s; two rounds) | No log, no cache: **14.9 / 14.7 us** a request at c=16 (157,663 / 147,482 req/s), **78.8 / 88.6 us** at c=1. `UseHttpLogging` (F8's fields, combined) through the simple console logger to a file: **20.6 / 18.2 us** at c=16, **116.7 / 102.7 us** at c=1; one line per request (1,545,822 and 1,728,576 lines). OutputCache with F9's four path policies on a non-matching path: **15.0 / 15.7 us** at c=16, **78.1 / 78.7 us** at c=1. For comparison (`phase7-crypto-logging.md`, tier 1): Thruster's front added about 1-1.5 us and the batched request log 1.2 us. |
| S6 | `UseHttpsRedirection` | `RedirectStatusCode = 301`, `HttpsPort = 443`: `GET /rooms/1?x=1` with `Host: campfire.test` -> 301 `https://campfire.test/rooms/1?x=1`, empty body; `POST` -> 301; `Host: other.example` -> 301 `https://other.example/x`. `HttpsPort = 8443` -> `https://campfire.test:8443/rooms/1?x=1`. |
| S7 | Framework certificate helpers | `X509Certificate2.CreateFromPem(cert, key)` with another certificate's key throws `CryptographicException` ("the key does not match the certificate"); key then chain in one text (autocert's file) reads with the text passed twice; `MatchesHostname` is exact and case-insensitive. |

Projection on the five workloads (Phase 6's gate: post c=16 1,497 us of CPU a request at 6.05x, post c=1 1,645 us at 6.63x, sidebar
c=1 499 us at 8.63x, messages c=1 522 us at 10.76x): front gone (-2.5 us), HTTP logging (+3.5..+5.9 at c=16, +14..+38 at c=1):
post c=16 about 6.0x, post c=1 6.5-6.6x, sidebar c=1 8.0-8.4x, messages c=1 10.0-10.5x. No cell approaches 5x; the unit benches are
what decide.

## 3. Types and modules

All in `src/Campfire.App` (`Campfire.Kit` is deleted in 7.6). Compile order: `Web/FrontSettings.fs` and `Web/HttpLog.fs` before
`Web/Services.fs`; `Web/Acme.fs` before `Web/Services.fs` (the challenge middleware and the store are used by the host only, but
nothing in them needs the controllers); `Web/OutputCaching.fs` and `Web/Host.fs` **after `Controllers.fs`** (they read the controllers'
max-age constants) and before `Boot.fs`; `Platform/OpenFiles.fs` beside `Concerns/Platform.fs`.

**`Web/FrontSettings.fs`** (about 70 lines; F14)

```fsharp
type FrontSettings =
    { HttpPort: int; HttpsPort: int; TargetPort: int; TargetBind: IPAddress
      TlsDomains: string list; AcmeDirectory: string; ExternalAccount: (string * string) voption   // EAB kid, base64url key
      StoragePath: string                                                                            // ./storage/thruster
      IdleTimeout: TimeSpan; ReadTimeout: TimeSpan                                                   // >= 49 days -> Timeout.InfiniteTimeSpan
      ForwardHeaders: bool; MaxRequestBody: int64                                                    // 0 = none
      CacheSize: int64; MaxCacheItemSize: int64; LogRequests: bool }
    member HasTls: bool
module FrontSettings =
    val fromLookup: (string -> string | null) -> FrontSettings   // THRUSTER_<NAME>, then <NAME>; Go's ParseBool/Atoi; defaults as Thruster's
    val fromEnv: unit -> FrontSettings
```

**`Web/HttpLog.fs`** (about 35 lines; F8)

```fsharp
module HttpLog =
    val configure: HttpLoggingOptions -> unit          // CombineLogs, the six fields, no headers, no bodies
    type Interceptor = interface IHttpLoggingInterceptor   // OnResponseAsync adds remote_addr and user_agent; OnRequestAsync nothing
    val add: IServiceCollection -> unit                // AddHttpLogging configure + AddHttpLoggingInterceptor<Interceptor>
```

**`Web/Acme.fs`** (about 190 lines; F3-F6)

```fsharp
/// A certificate ready for handshakes.
type LoadedCertificate = { Options: SslServerAuthenticationOptions; NotBefore: DateTimeOffset; NotAfter: DateTimeOffset }

module DirCache =
    val read: storage: string -> domain: string -> now: DateTimeOffset -> Result<LoadedCertificate, string>  // CreateFromPem, MatchesHostname, current
    val write: storage: string -> domain: string -> key: ECDsa -> chainPem: string -> unit   // SEC1 key then chain; 0700/0600; temp + rename
    val accountKeyPem: storage: string -> string                                             // acme_account+key, created (SEC1) if absent

module Renewal =
    val due: LoadedCertificate -> jitter: TimeSpan -> DateTimeOffset    // NotAfter - min(30 d, lifetime / 3) - jitter
    val retryAfterFailure: jitter01: float -> TimeSpan                  // 30-60 min

module ServerNames =
    val normalize: string | null -> Result<string, string>             // autocert's checks; IdnMapping to ASCII, lower case, no trailing dot

[<Sealed>]
type CertificateStore(domains: string list) =
    member Get: string -> LoadedCertificate voption
    member Set: string * LoadedCertificate -> unit                       // completes the domain's first-certificate task
    member WaitFor: domain: string * CancellationToken -> Task<LoadedCertificate>   // a TLS_DOMAIN name, until its first certificate
    member HandshakeOptions: TlsHandshakeCallbackContext -> ValueTask<SslServerAuthenticationOptions>   // F3; throws for other names
    member AddChallenge: token: string * keyAuthorization: string -> unit
    member RemoveChallenge: token: string -> unit
    member Challenge: token: string -> string | null

[<Sealed>]
type AcmeService(settings: FrontSettings, store: CertificateStore, http: IHttpClientFactory,
                 lifetime: IHostApplicationLifetime, time: TimeProvider, logger: ILogger<AcmeService>) =
    inherit BackgroundService()
    member LoadCached: now: DateTimeOffset -> unit                          // at construction/start: DirCache.read for each domain
    member RenewDueAsync: now: DateTimeOffset * CancellationToken -> Task<DateTimeOffset>   // one pass; returns when to look again
    // ExecuteAsync: LoadCached; await ApplicationStarted; loop { next = RenewDueAsync(time.GetUtcNow()); delay to next (7-day steps) }

module AcmeEndpoints =
    val challenge: CertificateStore -> RequestDelegate                      // /.well-known/acme-challenge/{token}: text/plain or 404
```

The order inside `RenewDueAsync` for one domain: `AcmeContext(Uri directory, IKey account, AcmeHttpClient(Uri directory,
http.CreateClient "acme"))` (made once, lazily) -> `NewAccount([], true, eabKid, eabKey, "HS256")` (once) -> `NewOrder [domain]` ->
for each authorization `Http()`: `store.AddChallenge(token, KeyAuthz)`, `Validate()` -> poll `order.Resource()` until `Ready`/`Valid`
(bounded: 120 s, backoff from 250 ms; `Invalid` fails) -> remove the challenges -> P-256 key + `CertificateRequest` CSR ->
`order.Finalize csr` -> poll until `Valid` -> `order.Download null` -> `ToPem null` -> `DirCache.write` -> `DirCache.read` (the same
checks as a cached file) -> `store.Set`. Exceptions are caught per domain, logged with the domain, and give `retryAfterFailure`.

**`Web/OutputCaching.fs`** (about 40 lines; F9)

```fsharp
module OutputCaching =
    /// Rails' public routes and their max-age, from the controllers' constants (Avatars.MaxAge, Logos.MaxAge, QrCode.MaxAge,
    /// ActiveStorageControllers.HundredYears and DiskMaxAge).
    val families: (string * (PathString -> bool) * TimeSpan) list
    val configure: FrontSettings -> OutputCacheOptions -> unit   // UseCaseSensitivePaths, SizeLimit, MaximumBodySize, one AddBasePolicy each
```

A family's predicate matches the route's path shape exactly (for example `/users/<segment>/avatar` with an optional `.<format>`),
never a prefix that a page shares; every policy is `With(matches && no Range).Expire(maxAge).SetVaryByHeader("Accept-Encoding")` on
the builder's default policy (GET/HEAD, no `Authorization`, 200 only, no `Set-Cookie`, locking on for these routes only).

**`Web/Host.fs`** (about 150 lines; F1-F3, F6, F7, F11-F13)

```fsharp
module Host =
    val configureKestrel: KestrelServerOptions -> unit          // Front.configureKestrel's head limits, UTF-8 headers, no Server header
    val listen: FrontSettings -> CertificateStore | null -> KestrelServerOptions -> unit   // F2, F3, F12's timeouts
    val build: FrontSettings -> Booted -> ILoggerFactory -> args: string[] -> WebApplication
    // services: Booted.ConfigureServices; HttpLog.add (if LogRequests); AddOutputCache (OutputCaching.configure);
    //           AddHttpsRedirection (301, HttpsPort); AddHsts (F7); AddHttpClient "acme"; the store and AcmeService (if HasTls);
    //           HostOptions.ShutdownTimeout = Boot.ShutdownGrace; ForwardedHeadersOptions per F11
    // pipeline: UseHttpLogging (if LogRequests); MapWhen (TLS && LocalPort = HttpPort) [challenge; UseHttpsRedirection];
    //           then Booted.Pipeline (Web.Services.pipeline)
```

**`Web/Services.fs`** changes: `Compression.configure` adds `ZstandardCompressionProvider` after Gzip (F10); the pipeline gains
`UseHsts()` right after `Edge.assumeSsl` when `edge.ForceSsl` (F7) and `UseOutputCache()` right before `UseResponseCompression()`
(F9); `UseHttpLogging()` first in it from 7.1 to 7.4, behind `EdgeConfig.LogRequests` (`LOG_REQUESTS` in production, off in
the test hosts), then in the host's pipeline. `Web/Edge.fs`: `EdgeConfig` gains `LogRequests` (7.1) and `MaxRequestBody` (16 MiB, or `MAX_REQUEST_BODY` when lower) used by `bodyLimit`; the HSTS
line of `onStarting`, `redirectToHttps` and `ForceSsl`'s redirect branch go (`Hsts` becomes the `HstsOptions` values).
`Boot.serve`: `FrontSettings.fromEnv`, `Host.build`, `app.RunAsync()`, then the drains as now; `Boot.createLoggerFactory`: the
`thruster` filter becomes `Microsoft.AspNetCore.HttpLogging` at Information. `Platform/OpenFiles.fs`: `Server.raiseOpenFileLimit`
and `openFileLimits`, moved unchanged. `tests/Campfire.App.Tests/Support.fs`: `Web.Host.configureKestrel`.

**Until the host swap (7.4)** the old front host keeps serving and the new pieces sit in the app's pipeline; `Boot.serve` switches the
front's own work off with its own settings, so nothing is done twice: 7.1 passes `LogRequests = false` and
`GzipCompressionEnabled = false` in the `FrontConfig` it hands `Front.serveWithLines` (and no `RequestLines`); 7.2 passes
`CacheSize = 0` (`MemoryCache.Set` refuses an item larger than the capacity, so nothing is stored; the per-request key is a cost the
7.2 bench includes and 7.4 removes).

## 4. Units, in build order

One Sonnet builder per unit, about 90 minutes, from a clean `git status` on `dotnetway`, one commit per logical step (read `git diff
--cached --stat` first), a Phase log entry per unit (KLOC from `bin/kloc`, gates run, differences recorded). **Common acceptance for
every unit:** `CAMPFIRE_REQUIRE_SEED=1 bin/verify` green (it runs the markup lint and its self-test, the Dapper and EF guards and H1:
zero query compilations in steady state); `df -h /` first (12 GB, 15 GB before a bench); `bin/clean-docker` after image rebuilds,
`bin/clean-parity` after pixel runs; pixel slices and benches detached with a status file and polled in the foreground with bounded
loops, one parity compare at a time, `realtime/**` one state per run. Benches: `bench/quick` interleaved against a control image of
Phase 6's close (`309ab42`, built once as `campfire-dotnetway:p6ctl` from a scratch worktree with the submodule copied in, removed
after the phase; `p5ctl` and other stale control images can go with `bin/clean-docker`), host probe and load recorded; a
five-workload cell under 5x Rails blocks. Rebuild `campfire-dotnetway:app` from HEAD before any image gate
(`CAMPFIRE_REVISION=$(git rev-parse HEAD) parity/bin/candidate build`, label checked). The app is pixel-identical after every unit.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 7.1 Request log and compression | `Web/HttpLog.fs` (F8) registered by `Web.Services.add` and placed first in `Web.Services.pipeline` behind `EdgeConfig.LogRequests` (production: `LOG_REQUESTS`, default on; test hosts off); `Boot.createLoggerFactory`'s filter; `Boot.serve` turns the front's log and compression off (section 3, last paragraph) and passes no `RequestLines`; `RequestLog.fs`, `RequestLogTests.fs` and `bench/log-micro` deleted; `bench/lib/logcount` counts `HttpLoggingMiddleware` lines; `ZstandardCompressionProvider` after Gzip (F10); README class B lines (log format; zstd and jitter) | LG1-LG4, CZ1-CZ3 (section 5) green; `bench/lib/logcount` against the rebuilt image: N requests, N lines; image rebuilt, `bin/asset-check` passes (the front no longer touches asset responses; any N-check that pinned the front's `Vary` or gzip of `campfire-icon.png` is updated with the reason in the Phase log); `bin/pixel-parity --fast --only 'auth/sign_in*,rooms/show/designers,errors/**'` on `default` equal; **`bench/quick` five workloads, 3 reps, against `p6ctl`**: no cell under 5x, CPU per request per cell recorded beside S5's projection; the bench's stored expected byte sizes refreshed (Phase 6's open item: the fingerprint lengths) with the reason |
| 7.2 OutputCache | `Web/OutputCaching.fs` (F9) with the controllers' max-age constants (add `QrCode.MaxAge`, `ActiveStorageControllers.DiskMaxAge` where the number is inline today); `AddOutputCache` in `Web.Services.add`, `UseOutputCache()` right before `UseResponseCompression()`; `Boot.serve` sets the front's `CacheSize` to 0; README class B lines (`X-Cache`, `Age`, cookies, `Authorization`) | OC1-OC9 green; `bench/quick --routes avatar,room_show,post_message` 3 reps against `p6ctl`: the avatar row reported (hits now OutputCache's), room page and post not below the control's range; `--fast --only 'users/**,account/**,rooms/show/designers'` on every seed equal; image rebuilt, `bin/asset-check` passes (no asset route is cached: assets are served before `UseOutputCache`) |
| 7.3 ACME on Certes | `Certes` 4.1.0 in `Directory.Packages.props` and the App; `Web/Acme.fs` (section 3: `DirCache`, `Renewal`, `ServerNames`, `CertificateStore`, `AcmeService`, `AcmeEndpoints`); not yet wired into the production host (the old `CertManager` still serves TLS until 7.4); the Pebble recipe in AGENTS.md rewritten (HTTP-01 on 5002, `PEBBLE_DIRECTORY`/`PEBBLE_ROOT`) | AC1-AC10 green; **AC11 (Pebble) passes three runs in a row** in colima, each run's order time recorded; `dotnet list package --vulnerable --include-transitive` on the App: none; no response changes (no pixel run) |
| **checkpoint** | Opus, after 7.3 (below) | |
| 7.4 The host | `Web/FrontSettings.fs`, `Web/Host.fs`, `Platform/OpenFiles.fs` (F1-F3, F6, F7, F11-F14); `Boot.serve` on `Host.build` and `app.RunAsync()`; `UseHttpLogging` moves from the app's pipeline to the host's (outermost); `UseHsts` in the app's pipeline and Edge's HSTS line and `force_ssl` redirect deleted; `EdgeConfig.MaxRequestBody`; `Support.startPipelineOn` on `Host.configureKestrel`. `Front.serveWithLines` is no longer called (the Kit project still builds, its tests still run, until 7.6) | HS1-HS12 green, HS4 on Linux too (`bin/linux-tests App`: the chain is the platform's); App test P12 (shutdown) and the Cable restart tests green; image rebuilt: `/up`, `POST /session` and a room page through `HTTP_PORT` and `TARGET_PORT`; `bin/asset-check` passes (Phase 6's condition: the marker middleware and `appEndpointsOnly` unchanged, `git diff` shows `StaticAssets.map`'s call site where it was); `--fast` every seed `auth/**,errors/**,bot_api/**,rooms/show/designers`; `bin/cookie-roundtrip` 20/20; `bench/quick` five workloads 3 reps against `p6ctl`, no cell under 5x |
| 7.5 TLS end to end | The harness TLS mode (F16): `parity/bin/candidate` (`PARITY_CANDIDATE_TLS=1`: the certificate made with `openssl req -x509 -newkey ec -pkeyopt ec_paramgen_curve:P-256` for `campfire.localhost`, written as a `DirCache` file into the candidate's storage, `TLS_DOMAIN`, `HTTPS_PORT`, `HTTP_PORT`), the capture's origin, `bin/pixel-parity --candidate-tls`; Pebble against the image (`reference-tools/front/pebble-image.sh`: Pebble and the candidate on host networking, `TLS_DOMAIN=campfire.test`, `HTTP_PORT=5002`, `ACME_DIRECTORY` Pebble's, `SSL_CERT_FILE` = Pebble's root inside the container so the app's client trusts it); the operator step written (section 8, AGENTS.md) | `bin/pixel-parity --fast --candidate-tls --only 'auth/sign_in,rooms/show/designers'` on `default`: screenshots, aria and loose DOM equal to Rails'; `curl --cacert` the made root: HTTP/2 on `HTTPS_PORT`, 301 from `HTTP_PORT` to `https://`; `pebble-image.sh`: the container orders a certificate at boot, writes `storage/thruster/campfire.test` (0600) and `acme_account+key`, serves the 2-certificate chain (`openssl s_client -showcerts`), and after a restart loads it without ordering (Pebble's log shows one order); `bin/clean-docker` after |
| 7.6 Delete | `src/Campfire.Kit/` (project and `Server.fs`, `Front/`), `tests/Campfire.Kit.Tests/` (`AcmeStub`, `FrontAcme*`, `FrontCache*`, `FrontCompression*`, `FrontConfig*`, `FrontHarness`, `FrontTests`, `FrontTls*`, `ServerTests`), their `CampfireFs.slnx` rows and the App's project reference, `ZstdSharp.Port` from `Directory.Packages.props`; `bench/front/fsharp` rewritten onto `Web.Host` (the same endpoints: hello, a 100 KB page identity and gzip, an OutputCache hit and a zstd hit, a form post; `LOG_REQUESTS=false`); AGENTS.md (the layout table's Kit row; the front bullets: Pebble recipe, `bench/front/run`, `bin/linux-tests Kit`), README "Known differences" **Front server** rewritten (section 8), the Dockerfile's `bin/boot` comment, `bin/linux-tests`, `bin/kloc` if it names Kit, plan pointers | `git grep -n -e 'Campfire\.Kit' -e 'ZstdSharp' -e 'Front\.serve' -e 'RequestLines' -e 'CertManager'` over `src tests bin parity bench/quick bench/run bench/front bench/lib Dockerfile AGENTS.md README.md CampfireFs.slnx Directory.Packages.props` finds only history; `bench/front/run fsharp` builds and runs, its table in `bench/results/front-dotnetway.md`; `bin/kloc` recorded (section 7); image rebuilt, `--fast --only 'rooms/show/designers'` equal |

Then the **phase gates** (gate runner, plan section 3 and the Phase 7 row, in this order): `CAMPFIRE_REQUIRE_SEED=1 bin/verify`;
**AC11, the Pebble test**, and `pebble-image.sh`; **`bench/front/run`** reported; **`auth/sign_in` and `rooms/show/designers` over
TLS** (`--fast --candidate-tls`, every seed that has them); the **lean matrix on `default`**; then the **lean pixel matrix on all
seeds**; **`bench/quick`** on the five workloads plus the front-server rows (`--routes
room_show,messages_page,sidebar,search,post_message,avatar,static_css,up`) against a same-day `p6ctl` control, host probe and load
recorded; `bin/asset-check` against the image; `bin/cookie-roundtrip` once; `bin/kloc`. No `bin/db-differential` (no row is written).
**Operator step, not run by any agent:** Let's Encrypt staging once on a real domain (section 8).

**The checkpoint after 7.3** (Opus): reviews 7.1-7.3 against this note before the host swap: the HTTP logging fields (no header
or body beyond `user_agent`), the 7.1 bench against `p6ctl` beside S5's projection, the OutputCache families against Rails' controllers
(every `expires_in ... public` and `http_cache_forever` route present, nothing a page shares a prefix with matched; OC6's lock
check), that the Pebble test is deterministic (AC11's three runs; no timer or shared counter in AC1-AC10), Certes' account handling
on an existing `acme_account+key` (AC7), and whether 7.4-7.6 still fit 90 minutes each (7.4 is the largest: if it overruns, the TLS
listener and F6's branch move to 7.5 and the host swap ships plain HTTP first, TLS installs staying on the old host until then).

## 5. Tests

New files in `tests/Campfire.App.Tests/Web/`: `HttpLogTests.fs`, `OutputCachingTests.fs`, `AcmeTests.fs`, `HostTests.fs`. Hosts are
`Support.startPipelineOn` (a real Kestrel on loopback) unless a test needs TLS or two listeners, which build `Host.build`'s pieces
on free ports.

- **Request log (7.1):** LG1 one combined line per request with method, path, query, protocol, status and duration, through a
  `FakeLogger`-free capture (`Support`'s existing log-line capture); LG2 `remote_addr` and `user_agent` present, an IPv4-mapped peer
  written as IPv4, a trusted proxy's `X-Forwarded-For` reflected; LG3 no request or response header value but the user agent appears
  (a planted `Cookie: session_token=secret` and `Authorization` are absent from the line); LG4 `LOG_REQUESTS=false`: no line.
- **Compression (7.1):** CZ1 `Accept-Encoding: zstd` gets zstd that decompresses (`ZstandardStream`) to the identity body; CZ2
  Chrome's `gzip, deflate, br, zstd` gets `br`, `gzip` gets gzip, none gets identity; CZ3 a response under 256 bytes and an image
  are not compressed, and carry no `Vary`.
- **OutputCache (7.2):** OC1 an avatar is served once by the app and then from the cache (a counting stub of the avatar action, or
  `BodyWrites`), with `Age`; OC2 each family's lifetime equals its controller's constant (a pure check over `OutputCaching.families`), and expiry works:
  a test family with `Expire` 1 s is a hit at once and a miss after a 2.5 s wait (a wait in the direction that cannot flake: a slow host
  only makes the entry older); OC3 `?v=2` and `/USERS/...` are misses; OC4 `Range` bypasses and gets 206 from the app; OC5 `If-None-Match` with the entry's ETag
  gets 304 without the app; OC6 **a room page is never looked up, stored or locked**: 16 concurrent GETs of a deliberately slow page
  complete in about one page's time, and the store holds no entry for it; OC7 a response that sets a cookie (the hourly session resume on
  an avatar) is not stored and the next request reaches the app; OC8 gzip, identity and `br` clients each get their encoding from their own
  entry (S4's table); OC9 every family's predicate against Rails' route shapes: the avatar, logo, QR and Active Storage paths match, a page
  path that shares a prefix (`/users/1/profile`, `/account/edit`, `/rails/active_storage/direct_uploads`) does not.
- **ACME (7.3):** AC1 `DirCache.write` then `read` round-trips (key SEC1 with the curve named, chain order kept), file `0600`, directory
  `0700`, through a temporary file; AC2 `read` refuses an expired, a not-yet-valid, a mismatched-key and a wrong-name file with a
  reason, and accepts a wildcard covering the name; AC3 a cache file written by the deleted port (the fixture from `FrontAcmeTests`'
  "reads sec1 keys as go and openssl write them", copied as test data) reads; AC4 `Renewal.due` for a 90-day certificate is 30 days before
  `NotAfter` minus the jitter, for a 6-day one two days before; AC5 `ServerNames.normalize`: case, trailing dot, IDN, an IP, no dot, a
  slash; AC6 `CertificateStore.HandshakeOptions` gives the stored options for a stored name, fails for a name outside `TLS_DOMAIN`, and for a
  `TLS_DOMAIN` name without a certificate completes when `Set` is called (the task is awaited after `Set` returns, so no race); AC7
  `accountKeyPem` creates a SEC1 key once and returns the same text after; AC8 the challenge middleware answers a stored token with
  `text/plain`, 404 otherwise, and only under `/.well-known/acme-challenge/`; AC9 `RenewDueAsync` with every domain loaded and not due
  makes no HTTP request (a handler that fails the test if called) and returns the earliest due time; AC10 a failing order (the handler
  answers 500) leaves the store unchanged, logs the domain and returns now + 30-60 min. **AC11 (Pebble, skipped without
  `PEBBLE_DIRECTORY`)**: a host with the HTTP listener on 5002 (Pebble's HTTP-01 port; Pebble started with `--add-host
  campfire.test:192.168.5.2`, the VM's name for the Mac, when the test runs on the Mac, or `127.0.0.1` under `bin/linux-tests` on host
  networking) and the service for `campfire.test` orders at start, writes the cache file,
  and an `SslStream` client trusting only Pebble's root (`CustomRootTrust`, no AIA) completes a handshake on the HTTPS listener with a
  2-certificate chain; a second host on the same storage loads the file and makes no order.
- **Host (7.4):** HS1 `/up` on `HTTP_PORT` and on `TARGET_PORT`, `TARGET_PORT` bound to loopback; HS2 with TLS, the HTTP port answers a
  challenge token and 301s everything else to `https://<host><path>?<query>` (443 omitted, 8443 named); HS3 without TLS the HTTP port
  serves the app; HS4 the HTTPS listener negotiates `h2` and `http/1.1`, sends the intermediate (a client with `CustomRootTrust` on the
  test root and no AIA succeeds), refuses an unknown name and an IP; HS5 HSTS on an HTTPS page and an asset, to `localhost` too, absent
  with `DISABLE_SSL`; HS6 the head limits (the old R7 cases through `Host.configureKestrel`); HS7 `remote_ip` with `FORWARD_HEADERS` on and
  off: a public peer, a private peer with a forged `X-Forwarded-For`, a trusted proxy chain (the ban check's view); HS8 `KeepAliveTimeout`
  from `HTTP_IDLE_TIMEOUT` (an idle connection closed, "closes idle and slow connections" re-stated); HS9 `MAX_REQUEST_BODY` below 16 MiB
  gives the app's 413; HS10 a large value of a timeout means none (no `ArgumentOutOfRangeException` at boot); HS11 shutdown: SIGTERM's
  path (`StopAsync`) closes Cable sockets with `server_restart` first, then the host stops within the grace; HS12 `FrontSettings.fromLookup`:
  the `THRUSTER_` prefix, Go's booleans and integers, defaults, ignored variables (`H2C_ENABLED`, `GZIP_COMPRESSION_*`).
- **Kept elsewhere:** the 431/414 cases of `RequestsTests` (R7) and the Kestrel parser rules (R8) run through `Host.configureKestrel`; P12
  and the Cable restart tests; `bin/asset-check`; the harness.

Planted failures, run once each and reverted, recorded in the Phase log: 7.2, a family predicate widened to `/users/` (OC6 or OC9
fails); 7.3, `Renewal.due` back to a fixed 30 days (AC4 fails for the 6-day certificate); 7.4, the TLS listener switched to
`ServerCertificateSelector` (HS4 fails on Linux), and `KnownIPNetworks` left as Rails' with `FORWARD_HEADERS` off (HS7 fails).

## 6. What is deleted

| What | Lines (file) | Unit |
|---|---:|---|
| `src/Campfire.Kit/Front/RequestLog.fs`, `tests/Campfire.Kit.Tests/RequestLogTests.fs`, `bench/log-micro/` | 314 + 150 | 7.1 |
| `src/Campfire.Kit/` (`Server.fs` 73 after `raiseOpenFileLimit` moves; `Front/Acme.fs` 833, `Cache.fs` 313, `Compression.fs` 403, `Config.fs` 166, `Conn.fs` 190, `Front.fs` 197, `Handler.fs` 876, `Tls.fs` 212) and `Campfire.Kit.fsproj` | 3,263 | 7.6 |
| `tests/Campfire.Kit.Tests/` (the rest: 2,146 lines, `AcmeStub` and the `ObtainingCount` flake among them) | 2,146 | 7.6 |
| `ZstdSharp.Port` (package) | | 7.6 |
| `Web/Edge.fs`: the HSTS line, `redirectToHttps`, `force_ssl`'s redirect branch | about 25 | 7.4 |
| `bench/front/fsharp/Program.fs`'s Kit code (rewritten) | 157 -> about 80 | 7.6 |

`bin/kloc` counts `Campfire.Kit` at **2,842** (corrected = cloc).

## 7. KLOC projection

`bin/kloc` at `309ab42`: **29,974 corrected** (29,802 cloc), of which `Campfire.Kit` 2,842.

| | lines |
|---|---:|
| `Campfire.Kit` deleted | -2,842 |
| `Web/Acme.fs` (cache 45, renewal and names 25, store 45, service and order 60, endpoint 15) | +190 |
| `Web/Host.fs` (Kestrel options and listeners 50, services 45, pipeline and the HTTP-port branch 25, run 30) | +150 |
| `Web/FrontSettings.fs` (the reader, trimmed) | +70 |
| `Web/OutputCaching.fs` | +40 |
| `Web/HttpLog.fs` | +35 |
| `Platform/OpenFiles.fs` (moved) | +40 |
| `Edge.fs` -25, `Services.fs` +5, `Boot.fs` -25 (the serve function's front plumbing), controllers' constants +5 | -40 |
| **Phase 7 net** | **about -2,355** |

Project: **30.0 -> about 27.6 KLOC** (range 27.5-27.75); the plan's row is 23.9 -> 21.5 (the same -2.4 on a base that drifted). The
front layer after: about 0.5 KLOC in the App. Rust's bar is 31.0.

## 8. Known differences this phase records (README "Known differences", plan section 4)

The README's **Front server** entry is rewritten for Kestrel (7.6), keeping the Kestrel lines that still hold (the `Date` header on
`TARGET_PORT`, the head limits, the three parser rules) and replacing the rest:

- Class B, each with the test that pins it: TLS-ALPN-01 is not answered, certificates come by HTTP-01 only, so port 80 must be reachable
  (Q2; `reference/docs/self-hosting.md` asks for 80 and 443) (AC11); certificates are ordered at boot and renewed in the background
  `min(30 days, lifetime / 3)` before expiry, a first handshake waits for the first order, and a cached certificate for a name no longer
  in `TLS_DOMAIN` is not served (AC4, AC6); with TLS the HTTP port redirects every host (no 421), with an empty body, and names a non-443
  `HTTPS_PORT` (HS2); no cleartext HTTP/2 (`H2C_ENABLED` ignored); `HTTP_WRITE_TIMEOUT` is not read and slow bodies or readers are cut by
  Kestrel's minimum data rate (HS8); `GZIP_COMPRESSION_*` and `DEBUG` are not read, there is no BREACH jitter padding (CZ3); the request log
  is ASP.NET Core's HTTP logging line (LG1-LG3); `X-Cache` is gone and cache hits carry `Age`, a public response that sets a cookie is not
  cached, a request with `Authorization` bypasses the cache (OC1, OC7); the shutdown grace is one 10 s budget (HS11); a cached avatar, logo
  or QR code is served to anyone who has its URL, as Thruster served it (OC1; parity with the reference install, noted for the reviewer).
- Class A, once: `Vary: Accept-Encoding` only on compressed responses; images no longer compressed by a front; `X-Request-Start` and the
  front's `X-Forwarded-*` no longer reach the app (the remote IP is unchanged, HS7); on a cache hit `X-Request-Id`/`X-Runtime` are the hit's
  own; if 7.4 confirms it, an exception page without HSTS.

**The operator step** (AGENTS.md and the Phase log record it; no agent runs it): on a host with a real domain whose ports 80 and 443
reach the container, run the image once with `TLS_DOMAIN=<domain>`,
`ACME_DIRECTORY=https://acme-staging-v02.api.letsencrypt.org/directory` and a fresh storage volume; expect within a minute a certificate
whose issuer contains "(STAGING)" (`openssl s_client -connect <domain>:443 -servername <domain> -showcerts`: 2 or 3 certificates),
`storage/thruster/<domain>` and `acme_account+key` (mode 0600), a log line for the order and none for a renewal; restart and expect no
second order (staging's account page or the log). Then **delete `storage/thruster/`** before production: a staging certificate in the
cache is current and would be served.

## 9. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | HTTP logging costs more on the real app than S5 projects (the console logger's per-line wake-up at c=1, a pipe instead of a file) | 7.1's `bench/quick` against `p6ctl`, CPU per request per cell beside S5; c=1 cells first. If a cell nears 5x: `ConsoleLoggerOptions` queue settings, then the interceptor dropped, before anything hand-written |
| 2 | An OutputCache policy wider than intended (lookups, locks or buffering on pages) | OC6 and OC9; 7.2's room page and post rows against the control; the planted widened predicate |
| 3 | A TLS chain without the intermediate (the obvious `ServerCertificateSelector`) | HS4 on Linux, AC11's `CustomRootTrust` client, 7.5's `openssl s_client -showcerts` count; the planted selector |
| 4 | A flaky ACME test (the `ObtainingCount` kind) | AC11 three runs in a row in 7.3; the checkpoint looks for timers and cross-thread counters in AC1-AC10 |
| 5 | The CA's validation arriving before the HTTP listener is up | `AcmeService` orders only after `ApplicationStarted`; AC11 and `pebble-image.sh` order at boot and pass |
| 6 | Certes or BouncyCastle drift or advisories | pinned 4.1.0 in `Directory.Packages.props`; `dotnet list package --vulnerable --include-transitive` in 7.3 and the gate |
| 7 | The pipeline order breaking Phase 6's asset conditions (the marker middleware, `appEndpointsOnly`, immutable only on fingerprinted URLs) | `bin/asset-check` after 7.1, 7.2 and 7.4; assets never reach `UseOutputCache` (it sits after `StaticAssets.map`) |
| 8 | `remote_ip` changing for bans and the login throttle | HS7 with `FORWARD_HEADERS` on and off; the planted trust-all |
| 9 | Shutdown order (Cable sockets, jobs, the WAL checkpoint) | HS11 and P12; `bench/lib/logcount`'s SIGTERM run in 7.1 and 7.4 counts every line |
| 10 | HSTS missing where Rails sends it (`localhost` excluded by default, exception pages) | HS5; the network layer of `errors/**` in 7.4's `--fast` run (reported) |
| 11 | The harness's TLS mode (Chromium not resolving `*.localhost`, the origin check, `wss:` for Cable) | 7.5's first `--fast` run of `auth/sign_in`; fall back to `--host-resolver-rules` in the browser launch |
| 12 | A cached public response leaking a cookie or a user's data | OC7; the builder's default policy refuses `Set-Cookie`; the gate's reviewer reads OC1's note (Thruster's parity) |
| 13 | 7.4 overrunning 90 minutes | the checkpoint's split rule (plain HTTP host first, TLS in 7.5) |
| 14 | Disk: image rebuilds in 7.1, 7.2, 7.4, 7.5, 7.6, the control image, pixel runs, the Pebble image | `df -h /` before each; `bin/clean-docker` after each image; remove the Pebble image and containers after 7.5 and the gate |

## 10. Questions for Chris

None. Kestrel's TLS callback over the selector (S3: the selector drops the intermediate), Certes on HTTP-01 (Q2, decided), ordering at
boot (removes the flake's cause, keeps the first visitor's wait), OutputCache by route family outside compression (S4: the only shape
that reproduces Thruster's cache without costing the pages), the framework's HTTP logging at its measured cost (S5, D9 accepts it),
zstd through the .NET 11 provider, `UseHttpsRedirection` and `UseHsts`, Kestrel's limits over Go's deadlines, and the generic host's
lifetime are settled by D9, the spikes, the floor and the plan. The Let's Encrypt staging run is an operator step (section 8), not a
question.
