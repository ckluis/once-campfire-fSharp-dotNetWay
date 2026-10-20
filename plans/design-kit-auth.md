# Design note: the Kit layer on ASP.NET Core (Phase 2)

Status: design, 2026-10-13, before Phase 2 builds. Author: the Opus designer (plan section 5). Binding for the Phase 2 builders
together with `plans/dotnetway.md` sections 3-5 and D1-D9 (`/Users/clank/Desktop/projects/campfire-fsharp-thedotnetway-plan.md`).
Where this note and plan section 2.4 disagree, this note wins: it is later and it rests on the spikes below and on Chris's
decisions of 2026-10-13 (.NET 11, its automatic CSRF protection).

Scope: sessions and the Rails `session_token` cookie, authentication (cookie and bot key), CSRF, the Rails session (flash,
return-to), params, formats, responses (redirects, ETags, errors, default headers), response compression, and the move of every
controller from `Ctx` to `HttpContext`. Out of scope: the front server (`Kit/Front/`, Phase 7), Cable's connection
authentication (Phase 3), views (Phase 4).

**Preconditions.** Phase 1 is closed: the app targets **net11.0** on the .NET 11 SDK (Phase 1's first unit), `CampfireDb` (EF Core)
is registered in DI, and a `TimeProvider` carrying the harness's frozen clock is registered. If Phase 1 left either out, unit 2.1
adds it (the `TimeProvider` adapter over `SharedClock` is about 15 lines).

## 1. Decisions

Each settled by D9 (framework and mainstream packages first, hand-written code only where parity forces it), with the reason.

| # | Decision | Reason |
|---|---|---|
| K1 | **ASP.NET Core cookie authentication** (`AddCookie`) is the `session_token` cookie, with a custom `TicketDataFormat` (`RailsSignedTicketFormat`) that reads and writes Rails' signed cookie. No other cookie format, no DataProtection on this cookie. | The framework does the cookie plumbing (read, `SignInAsync`, `SignOutAsync`, renewal, `Secure`/`SameSite`/`HttpOnly`); only the byte format is Rails'. Spike S1 below: a Rails-minted cookie authenticates, ours verifies under Rails' verifier, renewal and sign-out work. |
| K2 | **No default authenticate scheme.** Authentication runs where Rails runs it: `require_authentication` in the before-action chain calls `ctx.AuthenticateAsync "Session"`, then `"BotKey"`, then `ChallengeAsync "Session"`. The app sets the AppContext switch `Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme`. No `RequireAuthorization`, no `UseAuthorization`. | Rails' order of before-actions decides status codes (an old browser gets the incompatible-browser page before any sign-in redirect; `reject_banned_ip` before authentication). Authorization middleware would run before all of them. Without the switch, .NET picks the single registered scheme as default and authenticates every request, assets included (spike S1.6: one `Unprotect` per asset request; with the switch: zero). |
| K3 | **The session lookup lives in `CookieAuthenticationEvents.OnValidatePrincipal`**: token -> `sessions` row (EF, no tracking) -> user; `RejectPrincipal` if either is gone; the hourly `Session#resume` writes the row and sets `ShouldRenew` so the framework re-issues the cookie. | The documented hook for "is this cookie's session still valid". Re-signing on the hourly refresh is the port's (and Rust's) known difference "cookies written on change"; Rails re-signs on every request. |
| K4 | **Bot keys are a second scheme**, `BotKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>`, tried after the session scheme. No policy scheme. | Rails tries `restore_authentication || bot_authentication || request_authentication` on every controller, not by path; a policy scheme selecting by path would change which requests a bot key can authenticate (and so `deny_bots`' 403s). |
| K5 | **CSRF is .NET 11's `ICsrfProtection`** (`DefaultCsrfProtection`: `Sec-Fetch-Site`, then CORS-trusted origins, then `Origin` vs scheme and host), **called from `verify_authenticity_token` in the before-action chain**, on every non-GET/HEAD request, skipped for bot-key requests; a denial is Rails' `InvalidAuthenticityToken` (422 with `public/422.html`). No antiforgery tokens, no `UseAntiforgery()`, no `IAntiforgeryMetadata` on Falco endpoints. | Chris decided the token-less framework protection (2026-10-13). Read in `dotnet/aspnetcore` `release/11.0`: the auto-injected `CsrfProtectionMiddleware` only evaluates endpoints that carry `IAntiforgeryMetadata` and only *records* a verdict in `IAntiforgeryValidationFeature`; the 400 comes from `FormFeature` when something reads the form (spike S1.8b reproduced the throw). That would leave JSON and body-less writes (Turbo `DELETE`s, `@rails/request.js` JSON) unchecked, check before Rails' chain runs, and turn a cross-site form read into an `InvalidOperationException` (500) instead of 422. Calling the framework's own `ICsrfProtection` (public interface, registered by both `CreateBuilder` and `CreateSlimBuilder`) keeps its algorithm and Rails' coverage and order. |
| K6 | **Rails' session becomes ASP.NET Core session** (`AddSession` over `AddDistributedMemoryCache`), cookie **`_campfire_flash`** (not `_campfire_session`), holding the flash and `return_to_after_authenticating` only. DataProtection is `UseEphemeralDataProtectionProvider()`. Rails' encrypted `_campfire_session` is neither read nor written. | Plan 2.4 settled ASP.NET session; the name is new: with Rails' name, every request from a person who came over from Rails logs a `SessionMiddleware` warning (spike S1.8: "Error unprotecting the session cookie") until something writes the session, and our cookie would overwrite Rails' (and its CSRF token) for a switch back. A separate name leaves Rails' cookie untouched both ways. Ephemeral keys match the in-memory store: both die with the process. |
| K7 | **Flash is a 40-line module over `ISession`** with Rails' `FlashHash` semantics (set for the next request, swept at the end of the request that loads it, `flash.now`, `keep`). | No framework flash outside MVC's TempData (needs MVC). Values are only strings (`notice`, `alert`). |
| K8 | **Controllers become `HttpContext -> Task<Result<HttpHandler, Error>>`** built with `FsToolkit.ErrorHandling`'s `taskResult { }`; `Action.run` turns one into a Falco `HttpHandler`. `Error` keeps Rails' exception kinds (404, 400, 406, 422, 500, ...) plus `Halt of HttpHandler` for a before-action that rendered or redirected. Per-request state (`Current.user`, `Current.session`, `authenticated_by`, the platform) is a typed `HttpContext.Features` entry. | Keeps the 240 call sites' shape (`let!`/`do!` stop at the first halt), so the move is mechanical; `taskResult` is the mainstream F# package for exactly this and deletes the hand-written `act` builder. Features are the framework's per-request typed slots. |
| K9 | **The route table keeps Journey's recognizer**, mounted as Falco's not-found handler as now; path parameters go into `ctx.Request.RouteValues`. Rows are not turned into Falco endpoints in this phase. | Rails picks the first row in table order (`GET /rooms/opens` is `rooms#show`), `:param` excludes dots, globs are lazy and give up `(.:format)` (`*filename(.:format)` splits `moon.jpg` into `moon` + `jpg`), and paths are normalized (`//rooms//1//`); `vectors/campfire_routes.json` pins 111 such recognitions. ASP.NET's catch-all must be a whole last segment and can't be followed by `.{format?}`, so a straight conversion fails the glob rows; per-row `Order` would also be needed. Revisit in Phase 9 with the recognitions vector as the gate. |
| K10 | **Requests: framework middleware.** `UseForwardedHeaders` (X-Forwarded-For/-Proto/-Host, `KnownNetworks` = Rails' `TRUSTED_PROXIES`, no forward limit), a one-line `assume_ssl` middleware (`Request.Scheme <- "https"` unless `DISABLE_SSL`), `UseHttpMethodOverride(FormFieldName = "_method")` with the method upper-cased after it, `UseCookiePolicy` (`SameAsRequest` secure, `Lax` minimum), Kestrel and `FormOptions` limits for bodies. | Each is the shared framework's version of a Rack/ActionDispatch middleware the port re-implemented (`Request.fs`, `Body.fs`, `Multipart.fs`). Spike S1.7: the override reads `_method` before routing and sets the method **as sent** (`delete`), hence the upper-casing. |
| K11 | **`UseResponseCompression`** (gzip and Brotli, `EnableForHttps = true`, the default MIME list plus `text/vnd.turbo-stream.html`, `text/javascript`, `image/svg+xml`, `application/manifest+json`) replaces `Rack::Deflater` (`Deflater.fs`, `Splice.fs`). | Plan 2.4. `Rack::Deflater` gzips HTTPS too (Rails' choice, same BREACH stance). |
| K12 | **OutputCache moves to Phase 7**, with the front server whose cache (`Front/Cache.fs`, Thruster's) it replaces. | In Phase 2 it would sit behind Front's cache: two caches in series with different eviction. The plan's Phase 2 row listed it; this moves it, nothing else changes. |
| K13 | **Errors**: `UseExceptionHandler` with an F# handler serving `ActionDispatch::PublicExceptions` (the HTML pages byte for byte, the JSON/XML bodies for other formats), and the same renderer for `Error` values. No `UseStatusCodePages`, no `ProblemDetails`. | Rails renders a public page only for an exception; `head :forbidden` has an empty body. `UseStatusCodePages` would add bodies to every bare status. |
| K14 | **Rack::ETag is not reproduced.** `fresh_when`/`stale?` (messages page, avatars, logo) keep their weak ETags and 304s; other pages carry no body hash. | Class A (transport): same DOM, pixels and data; hashing every body is cost for no visible effect, and `Splice.fs`'s part hashes go with it. The default `Cache-Control: max-age=0, private, must-revalidate` stays on 200s without one. |
| K15 | **Login throttling** (`rate_limit to: 10, within: 3.minutes` on `sessions#create`) uses `System.Threading.RateLimiting`'s `PartitionedRateLimiter` (fixed window per `remote_ip`), acquired inside the action after the chain, rendering Rails' rejection page. | The framework's limiter; acquiring it in the action keeps Rails' order (CSRF and `allow_browser` first). |

What stays hand-written, and why (parity): the ticket format (Rails' signed-cookie bytes), the bot-key lookup (Rails' `id-token`
key), the flash semantics, Rails' params precedence and bracket names, the format negotiation rules (`BROWSER_LIKE_ACCEPTS`,
`params[:format]`, Turbo's MIME first), `redirect_to`'s host checks, the ETag combination of `fresh_when`, the public-exception
renderer, Journey's recognizer.

**Revisions made while building.** K3 (unit 2.7, Opus checkpoint note 3): the hourly resume stays in `Authentication.restore`, not in
`OnValidatePrincipal`: Rails resumes only in `restore_authentication`, and unit 2.11's Active Storage check calls `AuthenticateAsync`
without resuming. `restore` and `startNewSessionFor` no longer take a client callback into the legacy `Ctx`: the user agent is the request
header and the IP is `Connection.RemoteIpAddress` (which `UseForwardedHeaders` has already set), IPv4-mapped addresses unmapped. K4: the
bot-key handler reads `bot_key` through `Web.Params.lookup` (route, JSON or form body, query) and fails, a 500, for a hash or array. K10: the
method override no longer buffers the body; the form it parses is the framework's cached `Request.Form`, which the kit's body parser and
`Web.Params` both read (`Adapter.parseBody`), and `Adapter.bodyLimit` honours the kit's `MaxBodyBytes`.
K11 (unit 2.8): `UseResponseCompression` sits behind the public files, not around the whole app, because assets are served precompressed;
a `SkipSmall` decorator of the framework's provider leaves a response whose `Content-Length` is under 256 bytes alone (Opus note 6: skip
the work, not the floor). Splice's gzip pieces, the CRC combining and the gzip cache are deleted, the part digests and the ETag stay until 2.13.
The first bench of the plain framework compression left the messages page at 4.5x Rails (c=1) and the room page 35% down (a ~400 KB page
compressed per request, after a copy through the middleware's stream), so `Adapter.write` keeps the compressed form of a `Body.Parts` page by
(encoding, ETag digest) in a bounded `Generations` map, made with the framework provider's own stream, and writes it already encoded.
K13 (Opus note 4): `Web/PublicExceptions.fs` renders the public pages straight to the response (it replaces `Errors.respond`'s
`Adapter.dispatch` through a `Ctx`); the status and message still come from `Campfire.Kit.Error` until 2.13. `Respond.commit` (flash, then
the session) runs in `Action.run` and in the renderer before the response starts. `Respond.fs` has no default `Cache-Control` for a 200
that set none: it stays with the legacy writer, and 2.13 must move it (a middleware's `OnStarting`) when `Ctx.Finish` goes.

K15 and the Web actions (unit 2.9): the limiter is `Web/LoginThrottle.fs`, a `PartitionedRateLimiter<string>` singleton registered in
`Services.add`, one fixed window (10 per 3 minutes) per `SessionCookie.client` IP, acquired in `Sessions.create` after `Before.chain`; it
never touches the session store, so F8 (no store reads or writes for a request without `_campfire_flash`) stays green. The first Web
actions needed the pieces K8 names but earlier units left behind `Legacy.wrap`: `Web/Before.fs` (the chain; its record is `Before` with `Auth`
for the authentication choice, since `Authentication` is already a module here), `Web/Pages.fs` (the layout's data, the page and
Turbo-Frame writers over the kit's `Adapter.write`, so a page of cached fragments still goes out in parts and compressed once),
`Web/Data.fs` (reads and writes on the request's `CampfireDb`) and `Web/Native.fs`. `Native.wrap` is what `Ctx.Finish` did for the
legacy rows, less the digest (K14): at response start, Rails' default headers, a default `Content-Type`, and `Cache-Control`
(`max-age=0, private, must-revalidate` for a 200 with a body, `no-cache` otherwise) where the action set none; an error page is
Rails' middleware and gets only the default headers (no `Cache-Control`, and not the chain's `X-Version`/`X-Rev`: `PublicExceptions`
removes them). `Pages.endpoint` carries the matched row for the layout choice that `MatchedRoute` carried. The session middleware's own
`no-cache,no-store`, `Pragma` and `Expires` (set when it establishes its cookie, over what the action said) are put back by the kit's
first-registered `OnStarting` callback, which runs last: Rails' session cookie leaves the cache headers alone (the replay found this: it
was a unit 2.5 difference that no replay had run since). Opus note 3 (remote IP) stands: the throttle and the ban check take the IP from
`Connection.RemoteIpAddress`.

## 2. Spikes (scratch project, 2026-10-13, .NET SDK 10.0.401, Falco 5.2.0, the repo's `Campfire.RailsCompat`)

`/private/tmp/claude-501/-Users-clank-Desktop-projects/fea4ed62-8823-4b82-bed5-fc54b6214d57/scratchpad/authspike/` (`Program.fs`,
`spike-output.txt`; build output deleted). A `WebApplication` on Kestrel, `SECRET_KEY_BASE` from `parity/.env.reference`, cookies
from `vectors/campfire_sessions.json`.

| # | Question | Result |
|---|---|---|
| S1.1 | Does `AddCookie` with `RailsSignedTicketFormat` accept a Rails-minted `session_token` (vector, `%3D`-escaped on the wire)? | yes: `AuthenticateAsync "Session"` yields the vector's token `AxJs94fteQ5Autv2VrKsH68c` |
| S1.2 | The vector's "forged" cookie | authenticates the signature (it is validly signed for an unknown token); the lookup in `OnValidatePrincipal` is what rejects it, as `Session.find_by(token:)` does |
| S1.3 | `SignInAsync` writes a cookie Rails reads? | yes: `session_token=<value>; expires=...; path=/; samesite=lax; httponly`; `Rack::Utils.escape(raw) = wire` and RailsCompat's verifier (the one `reference-tools/rails_compat_verify_rust.rb` checks against Rails) returns the token. Attribute order differs from Rack's (class A) |
| S1.5 | Renewal: `ShouldRenew` with `IssuedUtc`/`ExpiresUtc` set in `OnValidatePrincipal` | the framework re-issues the cookie through our `Protect` at response start |
| S1.6 | Does an asset request authenticate? | with one scheme registered and no switch: yes (1 `Unprotect`); with `SuppressAutoDefaultScheme`: no (0) |
| S1.7 | `UseHttpMethodOverride(FormFieldName = "_method")` on `POST _method=delete` | routes to the `DELETE` endpoint; `Request.Method` is `delete` (lower case, as sent) |
| S1.8 | Rails' `_campfire_session` beside an ASP.NET session cookie of the same name | ignored, with a `SessionMiddleware` warning per request (hence K6's name) |
| S1.8b | Falco endpoint convention adding `RequireAntiforgeryTokenAttribute(true)` (via `FalcoEndpointDataSource :> IEndpointConventionBuilder`) | the metadata lands on every Falco endpoint; without a middleware that sets the "invoked" key, `EndpointMiddleware` throws ("contains anti-forgery metadata, but a middleware was not found"); with a denied verdict, `Request.ReadFormAsync` throws `InvalidOperationException` ("...invalid anti-forgery token..."). Basis of K5 |
| S1.9 | `SignOutAsync` | `session_token=; expires=Thu, 01 Jan 1970 00:00:00 GMT; path=/; samesite=lax; httponly` |

Read, not run (no .NET 11 SDK on the Mac; disk was 4.7 GB free at the time): `CsrfProtectionMiddleware.cs`,
`DefaultCsrfProtection.cs`, `ICsrfProtection.cs`, `WebApplicationBuilder.cs`, `WebHost.cs`, `FormFeature.cs` on
`dotnet/aspnetcore` `release/11.0`. Unit 2.4 proves K5 on .NET 11 with its matrix test before anything depends on it.

## 3. Types and modules

All new code is in `src/Campfire.App/Web/`, compiled before `Concerns.fs` (later `Before.fs`). Names are binding; bodies are the
builder's.

```fsharp
namespace Campfire.App.Web

// Web/Schemes.fs
module Schemes =
    [<Literal>] let Session = "Session"           // the session_token cookie scheme
    [<Literal>] let BotKey = "BotKey"
    [<Literal>] let SessionCookie = "session_token"
    [<Literal>] let FlashCookie = "_campfire_flash"
    [<Literal>] let TokenClaim = "session_token"  // carried so Protect can re-sign on renewal

// Web/Current.fs: Rails' `Current`, one per request, in HttpContext.Features
type AuthenticatedBy = | Nobody | BySession | ByBotKey
[<Sealed; AllowNullLiteral>]
type Current() =
    member val Session: Session option = None with get, set   // Campfire.Db entity (Phase 1)
    member val User: User option = None with get, set
    member val AuthenticatedBy = Nobody with get, set
    member val Platform: ApplicationPlatform option = None with get, set
module Current =
    val get: HttpContext -> Current            // creates and Features.Set on first use
    val user: HttpContext -> User option
    val requireUser: HttpContext -> Result<User, Error>
    val signedIn: HttpContext -> bool

// Web/TicketFormat.fs (about 50 lines)
type RailsSignedTicketFormat(secrets: Secrets, time: TimeProvider) =
    interface ISecureDataFormat<AuthenticationTicket>
    // Protect: TokenClaim + Properties.ExpiresUtc -> Cookies.sign secrets "session_token" token (Some expires)
    //          (HMAC-SHA1 over the PBKDF2-SHA256 "signed cookie" key, `_rails` envelope, purpose "cookie.session_token")
    // Unprotect: Cookies.verifySigned ... (time.GetUtcNow()) -> ticket with one claim, no ExpiresUtc
    //          (the verifier already enforced `exp`); null on any failure

// Web/Authentication.fs: the Authentication concern and the cookie events
module SessionCookie =
    val configure: CookieAuthenticationOptions -> unit       // name, HttpOnly, Lax, SameAsRequest, SlidingExpiration=false
    val validatePrincipal: CookieValidatePrincipalContext -> Task   // K3
    val redirectToLogin: RedirectContext<CookieAuthenticationOptions> -> Task  // return_to + 302 new_session_url
type BotKeyAuthenticationHandler =                             // K4, about 45 lines
    inherit AuthenticationHandler<AuthenticationSchemeOptions>
module Authentication =
    val restore: HttpContext -> Task<bool>                     // restore_authentication
    val botAuthenticate: HttpContext -> Task<bool>             // bot_authentication
    val require: HttpContext -> Task<Result<unit, Error>>      // restore || bot || Halt(challenge)
    val startNewSessionFor: HttpContext -> User -> Task<Session>   // Sessions.start + SignInAsync
    val terminateCurrentSession: HttpContext -> Task           // destroy, Session.Clear, SignOutAsync, reset_remote_connections
    val postAuthenticatingUrl: HttpContext -> string
    val denyBots: HttpContext -> Result<unit, Error>
    val verifyAuthenticityToken: HttpContext -> Task<Result<unit, Error>>   // K5

// Web/Flash.fs
module Flash =
    val get: HttpContext -> string -> string | null            // loads from ISession once, marks for sweep
    val set: HttpContext -> string -> string -> unit           // next request
    val now: HttpContext -> string -> string -> unit           // this request
    val keep: HttpContext -> unit
    val commit: HttpContext -> unit                            // writes survivors back before the response starts
module ReturnTo = ...                                          // return_to_after_authenticating over ISession

// Web/Params.fs (about 120 lines)
module Params =
    val value: HttpContext -> string -> string | null          // params[:k]: route values > JSON/form body > query
    val nested: HttpContext -> string -> string -> string | null   // params[:a][:b] = form "a[b]" or JSON a.b
    val values: HttpContext -> string -> string list           // "k[]"
    val require: HttpContext -> string -> Result<Scope, Error> // ParameterMissing -> 400
    val permit: Scope -> string list -> Map<string, string>
    val wrap: HttpContext -> string -> string list voption -> unit  // wrap_parameters for JSON

// Web/Formats.fs (about 90 lines)
type Format = Html | TurboStream | Json | Js | Any ...         // the port's Format cases
module Formats =
    val requested: HttpContext -> Format list                  // params[:format], xhr, BROWSER_LIKE_ACCEPTS, q-sorted Accept
    val respondTo: HttpContext -> Format list -> Result<Format, Error>   // UnknownFormat -> 406
    val isTurboFrame: HttpContext -> bool

// Web/Respond.fs (about 150 lines): every response a controller sends
module Respond =
    val html: int -> string -> HttpHandler                     // text/html; charset=utf-8
    val turboStream: string -> HttpHandler
    val json: int -> (Utf8JsonWriter -> unit) -> HttpHandler
    val head: HttpContext -> int -> HttpHandler                // Rails' `head` (content type by format)
    val redirectTo: HttpContext -> string -> Result<HttpHandler, Error>      // 302, UnsafeRedirect checks
    val redirectToWith: HttpContext -> string -> Redirect -> Result<HttpHandler, Error>  // notice/alert/status
    val redirectBackOrTo: HttpContext -> string -> Result<HttpHandler, Error>
    val urlFor: HttpContext -> string -> string                // scheme://host + path
    val sendFile: string -> SendOptions -> HttpHandler         // Results.File(..., enableRangeProcessing = true)
    val freshWhen: HttpContext -> Freshness -> HttpHandler option   // weak ETag, 304
    val expiresIn: HttpContext -> TimeSpan -> ExpiresIn -> unit     // CacheControlHeaderValue
    val setLastRoom: HttpContext -> int64 -> unit              // cookies.permanent[:last_room]

// Web/Errors.fs: Kit's Error and Exceptions, rehomed
type Error =
    | NotFound | BadRequest of string | ParameterMissing of string | UnknownFormat
    | InvalidAuthenticityToken of string | InvalidCrossOriginRequest | UnsafeRedirect of string
    | Status of int | Internal of exn
    | Halt of HttpHandler                                      // a before-action rendered or redirected
module Errors =
    val respond: Error -> HttpHandler                          // PublicExceptions: public/<status>.html or {status, error}
    val exceptionHandler: RequestDelegate                      // UseExceptionHandler's handler, same renderer

// Web/Action.fs
type Action = HttpContext -> Task<Result<HttpHandler, Error>>
module Action =
    val run: Action -> HttpHandler                             // Ok h -> h; Error (Halt h) -> h; Error e -> Errors.respond e
    val halt: HttpHandler -> Result<'a, Error>

// Web/Services.fs: everything Phase 2 registers, and the pipeline
module Services =
    val add: AppConfig -> IServiceCollection -> unit
    val pipeline: AppState -> IApplicationBuilder -> unit      // replaces Boot.pipeline
```

`Concerns.fs` becomes `Web/Before.fs` (`Before` record and `Before.chain : Before -> HttpContext -> Task<Result<unit, Error>>`, the
same seven steps in the same order: version headers, current request, `reject_banned_ip`, `require_authentication`, `deny_bots`,
`verify_authenticity_token` unless bot key, `allow_browser`, then `require_unauthenticated_access`'s restore and redirect).
`RoomScoped`, `TrackedRoomVisit` and `ensure_can_administer` move with it.

A controller after the move:

```fsharp
let show: Action =
    fun ctx ->
        taskResult {
            do! Before.chain Before.Default ctx
            let! room = RoomsController.setRoom ctx Scope.All
            let db = ctx.Plug<CampfireDb>()
            ...
            Respond.setLastRoom ctx room.Id
            return Respond.html 200 (ShowPage.render ...)
        }
```

The pipeline (`Services.pipeline`, on the app's branch of Front's host, as `Boot.pipeline` is now):

```
UseExceptionHandler(Errors.exceptionHandler)
UseForwardedHeaders -> assumeSsl -> defaultHeaders (X-Request-Id, Rails' default_headers, OnStarting)
UseResponseCompression
publicFiles (unchanged until Phase 6)
UseHttpMethodOverride -> upcaseMethod
UseCookiePolicy -> UseSession
UseRouting -> UseFalco [ /cable ] -> UseFalcoNotFound(RouteTable.dispatch)   // dispatch = recognize, RouteValues, Action.run
```

Services (`Services.add`): `AddDataProtection().UseEphemeralDataProtectionProvider()`; `AddDistributedMemoryCache` with
`SizeLimit = 64 MiB`; `AddSession` (`_campfire_flash`, HttpOnly, Lax, `IsEssential`, `IdleTimeout = 2 h`);
`AddAuthentication().AddCookie(Schemes.Session, SessionCookie.configure).AddScheme<AuthenticationSchemeOptions,
BotKeyAuthenticationHandler>(Schemes.BotKey, ignore)`
plus `AddOptions<CookieAuthenticationOptions>(Schemes.Session).Configure<Secrets, TimeProvider>(...)` setting the ticket format,
the events and `TimeProvider`; `AddResponseCompression`; `Configure<FormOptions>`; `Configure<ForwardedHeadersOptions>`; the login
`PartitionedRateLimiter` as a singleton; `Secrets` and `AppState` as singletons. `RuntimeHostConfigurationOption
Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme = true` in `Campfire.App.fsproj`.

**Hosting seam.** Front builds the host (`Front.serveWithLines`, `WebApplication.CreateSlimBuilder`) and the app pipeline is a
branch of it, so the app's services must reach Front's builder: `serveWithLines` gains `configureServices: IServiceCollection ->
unit` (Phase 1 may have added it for `AddDbContextPool`; reuse it), and `tests/Campfire.App.Tests/Support.fs` calls the same
`Services.add`. `ctx.RequestServices` is the host's, so the branch resolves everything. Nothing here depends on the
WebApplication's auto-injected middleware (K2, K5).

Packages: shared framework only (`Microsoft.AspNetCore.App` 11), plus **`FsToolkit.ErrorHandling` 5.2.0** (2026-02-27,
demystifyfp, active: pushed 2026-10-12; netstandard2.0/2.1 and net9.0; FSharp.Core >= 9.0.300 on net9.0, the repo pins
10.1.401). Unchanged: Falco 5.2.0 (6.0.0-beta1 is not taken), `BCrypt.Net-Next` (plan 2.10 moves it). Tests:
`Microsoft.AspNetCore.TestHost` 11.0.0-rc.2.26504.105 only if a builder needs `TestServer`; `Mvc.Testing` waits for Phase 7 (see
section 5). `ZstdSharp.Port` stays until Phase 7 (Front uses it). `FakeLogger` (F5) comes from
`Microsoft.Extensions.Diagnostics.Testing` 10.10.0 (plan Appendix A), test projects only.

## 4. Units, in build order

One Sonnet builder per unit, 90 minutes each, from a clean `git status` on `dotnetway`, one commit per unit. Every unit's common
acceptance: `bin/verify` green (`CAMPFIRE_REQUIRE_SEED=1`), `bin/kloc` recorded in the commit message, and
`bin/pixel-parity --fast --only <the unit's globs>` all equal on screenshots, aria and loose DOM (loose DOM judged against the
Phase 0 residue: the whitespace-beside-hidden-input cells are not this phase's). **Migration rule:** until unit 2.13 the route
table holds both kinds of rows: new `Action`s, and `Legacy.wrap (old: Ctx -> Task<Result<Response, Error>>) : Action` (the
existing `Adapter.dispatch` path: build a `Ctx` from the `HttpContext`, `Adapter.write` its `Response`). `Ctx` gains `Http:
HttpContext`, so the legacy concerns call the new modules; units 2.2-2.5 switch the mechanisms under both kinds of rows at once,
and no request ever sees two session or flash stores.

| Unit | What | Acceptance (beyond the common) |
|---|---|---|
| 2.1 Seam | `Web/` folder; `Services.add`/`pipeline` skeletons; the hosting seam in Front and `Support.fs`; `FsToolkit.ErrorHandling`; the AppContext switch; `Ctx.Http`; `Web/Errors.fs`, `Web/Action.fs`, `Web/Current.fs`; `Legacy.wrap`; every route row wrapped | no behaviour change: the full `auth/**`, `errors/**`, `bot_api/**` `--fast` slices equal; a test resolves `ICsrfProtection`, `IAuthenticationSchemeProvider` and `ISessionStore` from a request's `RequestServices` on net11.0 |
| 2.2 Cookie auth | `TicketFormat.fs`, `SessionCookie` events, `Authentication.restore`/`require`/`startNewSessionFor`/`terminateCurrentSession`; legacy `Concerns` call them through `c.Http`; the Kit jar's `session_token` paths deleted. Cable's own cookie check is untouched (Phase 3) | tests A1-A8 (section 5); **`bin/cookie-roundtrip` 20/20 both ways run by the gate runner right after this unit**, not only at the phase end; `rails_compat_verify_rust.rb` pointed at a cookie our app minted |
| 2.3 Bot keys | `BotKeyAuthenticationHandler`; `require` = session, then bot key, then challenge; `deny_bots`; `authenticated_by` | tests B1-B4; `bot_api/**` slice; `controllers_a/replay.py` bot cases |
| 2.4 CSRF | `verifyAuthenticityToken` over `ICsrfProtection`; `Ctx.VerifyAuthenticityToken` deleted, legacy rows call the new one; `Mailbox.conductor` too; README "Known differences" CSRF entry rewritten (section 7) | the matrix C1-C12 passes on .NET 11 (**stop the phase and report if `ICsrfProtection` is missing or behaves otherwise than section 1 says**); `interactions/**` `--fast` (every form and Turbo write) equal |
| 2.5 Session and flash | `Flash`, `ReturnTo` over `ISession`; `Ctx.Session()`/`Ctx.Flash()` delegate to them; Kit `Session.fs` and the encrypted-cookie path deleted; `last_room` through `Response.Cookies` + `UseCookiePolicy` | tests F1-F7; `account/**`, `users/profile/**`, `auth/sign_in/**` (the notice and alert states) equal |
| 2.6 Requests | forwarded headers, `assume_ssl`, method override + upper-casing, X-Request-Id and default headers, body limits (16 MiB caps of the Rust known differences, 413), `UseExceptionHandler` | tests R1-R6; `errors/**` equal |
| 2.7 Params and formats | `Web/Params.fs`, `Web/Formats.fs`; the Kit golden cases (`tests/Campfire.Kit.Tests` replays of Rust's answers for Accept headers, bodies and strong params) re-pointed at them | P1-P4; every golden case that is Rails behaviour passes; any that fails is either fixed or recorded class B with its README line |
| 2.8 Responses and compression | `Web/Respond.fs` (redirects, ETags, cache control, files); `UseResponseCompression`; `Adapter.useDeflater` and `Splice.fs`'s gzip pieces off (legacy rows write plain bodies) | tests X1-X5; network layer report: every HTML/turbo-stream response `content-encoding: gzip` (or `br`) when asked, no double encoding through Front; `bench/quick` room page and post a message against a same-day control (the compression cost lands here) |
| 2.9 Controllers A | sessions (+ rate limiter K15), first runs, welcome, pwa, transfers, health, turbo native, mailbox | `auth/**`, `pwa/**`, `welcome/**`; `controllers_a/replay.py` (masks: `session_token` value, `_campfire_flash`) |
| 2.10 Controllers B | accounts and `accounts/*` (bots, bot keys, custom styles, join codes, users, logos), users and `users/*` (avatars, bans, profiles, sidebars, push subscriptions, test notifications) | `account/**`, `users/**`, `autocompletable/**` |
| 2.11 Controllers C | autocompletable, QR code, unfurl links, searches, Active Storage endpoints (`require_active_storage_authentication` = `AuthenticateAsync "Session"` or 401) | `search/**`, `interactions/composer/attachment*`, `interactions/lightbox` |
| 2.12 Controllers D (hot paths) | rooms and `rooms/*`, messages, boosts, by-bots | `rooms/**`, `messages/**`, `realtime/**` one state at a time; `bench/quick` all five workloads vs a same-day control |
| 2.13 Delete | `Legacy`, `Ctx.fs`, `Adapter.fs` (its `configureKestrel` moves to `Front/Front.fs`), `Params.fs`, `Body.fs`, `Multipart.fs`, `Cookies.fs`, `Session.fs`, `Format.fs`, `Request.fs`, `Response.fs`, `Deflater.fs` (`crc` and `hasWord` move to `Front/Compression.fs`), `Splice.fs`, `Generations.fs`, `Actions.fs`, `App.fs`, `Error.fs`, `Exceptions.fs`, `KitClock.fs` (callers on `TimeProvider`); `RailsCompat` `MessageEncryptor.fs` and `Cookies.encrypt/decrypt` if nothing else calls them; `bin/kit-differential` and `reference-tools/kit*`; Kit tests of deleted files (moved cases already live in App tests). `Campfire.Kit` keeps `Front/` and `Server.fs` until Phase 7 | `grep -rn "\bCtx\b" src` empty; `bin/verify`; `bin/kloc` |

Then the **phase gates** (gate runner, plan section 3): `bin/cookie-roundtrip` both ways; `controllers_a/replay.py` on `default`,
`crowd`, `first_run`; `bin/pixel-parity` lean matrix on all seeds (every controller changed); `bin/db-differential` (sessions are
written); `bench/quick` against a same-day control; `bin/kloc`.

## 5. Tests

Phase 2 tests run the whole app over a parity seed (`Support.bootSeeded`: the app's real pipeline and the real `Services.add`
on a loopback Kestrel), with an `HttpClient` (`UseCookies = false`, no redirects) or the existing raw `Req`/`Reply` client. That is
the WebApplicationFactory level: the same services, middleware and handlers, over HTTP. `WebApplicationFactory<Program>` itself
needs `Program` to build the `WebApplication`; here Front builds it, so the factory arrives with Phase 7 (when `Boot.serve`
becomes the builder). New test files in `tests/Campfire.App.Tests/Web/`.

| Id | Test |
|---|---|
| A1 | the Rails-minted cookie of `campfire_sessions.json` signs David in (`current-user-id` meta), no `Set-Cookie` |
| A2 | the "forged" vector (valid signature, unknown token) and a cookie with one flipped hex digit: redirect to `/session/new`, `session_token` not deleted (Rails leaves it) |
| A3 | sign in: `Set-Cookie: session_token` verifies with RailsCompat's verifier under purpose `cookie.session_token`; `path=/`, `httponly`, `samesite=lax`, `expires` = frozen now + 20 calendar years; no `secure` over HTTP, `secure` under `assume_ssl` |
| A4 | a token whose signed value ends in `=` padding: `%3D` on the wire both ways (use the forged vector's value) |
| A5 | `last_active_at` 2 h old: the request updates `sessions` (user agent, ip, `last_active_at`, `updated_at`) and re-issues the cookie; 30 min old: no write, no `Set-Cookie` |
| A6 | sign out: the row is gone, `session_token` deleted, `_campfire_flash` cleared; the same user's other session still signs in |
| A7 | `/up`, a digested asset and `/manifest.json` with a valid cookie: the ticket format is not called (a counting `ISecureDataFormat` decorator registered by the test) |
| A8 | a session whose user row is gone: not signed in |
| B1 | `POST /rooms/:id/:bot_key/messages` with a valid key: created, `authenticated_by` bot key |
| B2 | a wrong or blank key: 302 to `/session/new` (Rails' `request_authentication`) |
| B3 | a valid bot key in the query of a non-bot page: 403 (`deny_bots`) |
| B4 | a bot request with a cross-site `Origin` and `Sec-Fetch-Site: cross-site`: not 422 (forgery check skipped for bot keys) |
| C1-C12 | `POST /rooms/:id/messages` (form), `DELETE` via `_method`, and a JSON `PATCH`, each with: `Sec-Fetch-Site: same-origin` (ok); `none` (ok); `cross-site` (422, `public/422.html` bytes); `same-site` (422); no `Sec-Fetch-Site` and `Origin` = the request's scheme and host (ok); another `Origin` (422); neither header (ok); GET with `cross-site` (ok); under `assume_ssl` with `Origin: https://host` (ok) |
| F1 | `redirect_to ..., notice:` then GET: the notice renders once; the next GET has none |
| F2 | failed sign-in: 401 page with the alert (`flash.now`), the next page without it |
| F3 | a turbo-stream response between set and display does not consume the flash (Rails sweeps only a loaded flash) |
| F4 | `return_to`: GET `/rooms/1` signed out -> 302 `/session/new`; sign in -> 302 to the absolute `/rooms/1` URL |
| F5 | a request carrying Rails' `_campfire_session` and no `_campfire_flash`: no warning in the captured log (`FakeLogger`), Rails' cookie not rewritten |
| F6 | `last_room` set as Rails does (`path=/`, `samesite=lax`, 20-year expiry), only when it changes |
| F7 | 11 sign-in attempts from one IP within the window: the 11th is 429 with the rejection page |
| R1 | `X-Forwarded-For` from a trusted proxy sets `remote_ip` (ban check uses it); from an untrusted peer it is ignored |
| R2 | `X-Forwarded-Proto: https` from a trusted proxy: `secure` cookies, `https` URLs |
| R3 | `POST _method=delete` reaches the `DELETE` row; `_method=patch` the `PATCH` row |
| R4 | a 17 MiB urlencoded body: 413; a direct upload over the cap: 413 |
| R5 | `X-Request-Id` echoed (sanitized) or generated; Rails' default headers on HTML, not on live (streamed) responses |
| R6 | an exception in an action: 500 with `public/500.html` bytes; for `Accept: application/json` the JSON body |
| P1-P4 | params precedence (route > body > query), `message[body]` from form and from JSON, `wrap_parameters`, `ParameterMissing` 400 |
| X1-X5 | redirects (relative and absolute, other host -> `UnsafeRedirect`, `redirect_back_or_to`), `fresh_when` 304 with `If-None-Match`, avatar `expires_in ... public, stale_while_revalidate`, gzip/Brotli on HTML and turbo-stream with `Vary: Accept-Encoding`, `send_file` ranges |

Kept as they are: the `vectors/` tests, Db/Storage/Views/Cable tests, the App tests that boot the seed (re-pointed at the new
pipeline by 2.1). Retired with their code in 2.13: `CtxTests`, `AdapterTests`, `BodyTests`, `ParamsTests`, `CookiesTests`,
`DeflaterTests`, `SpliceTests`, `ResponseTests`, `FormatTests`, `HttpTests` (the Kit-vs-Rust HTTP replay); their Rails-behaviour
cases are in P*/X*/R* by then. Front's tests stay with Front.

## 6. KLOC projection

cloc 2.10 code lines at `367c794` for what goes: Kit outside `Front/` and `Server.fs` **5,573** (Params 733, Splice 617, Ctx
599, Response 522, Adapter 492, Multipart 416, Body 390, Request 337, Format 329, Cookies 289, Deflater 260, Session 194, KitClock
110, Error 82, Actions 58, Exceptions 54, App 50, Generations 41), less about 45 lines moved into Front; `MessageEncryptor.fs` and
the encrypted-cookie functions about 85; `Concerns.fs` 269 rewritten as about 200.

| | KLOC |
|---|---:|
| Deleted (Kit 5.53 net of moves, RailsCompat 0.09, Concerns' rewrite 0.07) | -5.69 |
| New `Web/`: ticket format 0.05, auth + events + bot scheme 0.20, Current 0.04, flash/return-to 0.05, params 0.12, formats 0.09, respond 0.15, errors 0.10, action 0.02, services/pipeline 0.10, rate limit 0.02 | +0.94 |
| Controllers' call sites (`Ctx` members to module functions; `act` to `taskResult`) | +0.05 |
| **Phase 2 net** | **-4.7** |

Project: **37.4 -> about 32.7 KLOC** if Phase 1 closes at the plan's 37.4 (**38.0 -> about 33.3** at design-data-ef's projection), against the plan's 31.7: the plan's Phase 2 row counted
almost no new code; the 0.94 above is where the Rails-specific rules (params, formats, public exceptions, flash) go. The
end-of-project projection moves from 17.0 to about 18.0 from this phase alone, about 18.5 with design-data-ef's +0.6 and
design-cable's -0.07: 60% of Rust's 31.0.

## 7. Known differences this phase records (README "Known differences", plan section 4)

- **CSRF (rewritten):** the framework's token-less check (`Sec-Fetch-Site`, then `Origin` against the request's scheme and host).
  Writes from `same-origin` or user-initiated (`none`) requests pass; `cross-site` **and `same-site`** (another subdomain) are
  422; a request with neither header (not a browser) passes, as does a matching `Origin` from a browser without Fetch Metadata.
  Pages carry no CSRF tokens (the `file_uploader.js` override stays), so open tabs keep working across a switch to us; after a
  switch back to Rails, a tab rendered by us needs a reload. (Class B; replaces Rust's entry, which accepted `same-site` and
  rejected header-less HTTPS writes.)
- **Rails session:** flash and return-to live in server memory under `_campfire_flash`; a restart, or a switch either way in
  the middle of a redirect, loses them (sign-in lands on the root). Rails' `_campfire_session` is left alone. (Class B.)
- **Method override:** `_method` only (not `X-HTTP-Method-Override`); a `_method` that is not an HTTP verb makes the request
  that verb (no route, 404) rather than leaving it a POST. (Class B.)
- **Remote IP:** forwarded headers are trusted from Rails' `TRUSTED_PROXIES` ranges only, `X-Forwarded-Host` included; no
  `Client-Ip` spoofing check (`IpSpoofAttackError`). (Class B.)
- **Malformed multipart** bodies may be refused with another status (plan section 2.4). (Class B.)
- **Login throttle** windows run on wall-clock time, not the frozen harness clock. (Class B, test-visible only.)
- **Byte-level (class A):** cookie attribute order and case, no body ETag on pages without `fresh_when`, gzip/Brotli bytes,
  `Cache-Control` directive order.

## 8. Risks and how a builder sees each early

| # | Risk | Early signal |
|---|---|---|
| 1 | Signed out by a switch (cookie bytes, escaping, purpose, expiry) | A1-A4 in unit 2.2, then `bin/cookie-roundtrip` straight after 2.2 (not at the phase end) |
| 2 | Every request authenticates (auto default scheme), costing an HMAC and a query per asset | A7 fails; `bench/quick` sidebar/room numbers drop against the control |
| 3 | .NET 11's `ICsrfProtection` absent from the slim builder or different from `release/11.0` source | 2.1's DI test; C1-C12 in 2.4. Stop and report rather than hand-roll a check |
| 4 | CSRF denies real browser writes (scheme rewritten by `assume_ssl` but `Origin` http, proxies not trusted) | C12 and R2; `interactions/**` `--fast` in 2.4 |
| 5 | Lower-case method after `_method` breaks the recognizer's verb match (`delete` vs `DELETE`) | R3; any `--fast` state that deletes a message or a boost |
| 6 | Flash lost or shown twice (sweep timing, turbo-stream responses, `flash.now`) | F1-F3; `account/edit` "✓" and `auth/sign_in/error` alert states |
| 7 | Two session stores during migration (legacy `Ctx` vs `ISession`) | 2.5 switches `Ctx.Session()` to `ISession` in the same commit; F1 with one legacy and one migrated controller on each side of the redirect |
| 8 | Status codes move because a check now runs in middleware rather than the chain (Rails' order) | `auth/incompatible_browser`, `errors/**`, `bot_api/**` slices in the unit that touches the chain |
| 9 | Double compression or none (Front's compression after `UseResponseCompression`; precompressed static files) | X4 and the network report in 2.8 (`content-encoding` once, `Vary` present) |
| 10 | Speed: `AuthenticateAsync` + claims + EF lookup, `ISession` load per page, compression at Fastest | `bench/quick` after 2.8 and 2.12 against a same-day control (Phase 0 showed the loaded host swings 0.77-1.31x); the floor is 5x Rails at c=16 |
| 11 | The frozen clock missing from cookie auth (`expires` and resume checks on real time) | A3 and A5 assert exact times under `FakeTimeProvider`/the harness clock; `replay.py` masks only the cookie value |
| 12 | Cable signs people in through its own cookie check until Phase 3: a change to the session lookup that Cable does not share | `realtime/**` states in 2.2 and 2.12 |
| 13 | Disk: the gates need 12 GB free (15 GB for `bench/quick`); the Mac had 4.7-14 GB today while another project's `go test` filled `~/Library/Caches/go-build` | `df -h /` before each gate; `go clean -cache` only when `pgrep -f 'go (test|build|run|vet)'` is empty |
