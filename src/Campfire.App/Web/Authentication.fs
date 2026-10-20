// plans/design-kit-auth.md section 3 (Web/Authentication.fs): Rails' `Authentication` concern on ASP.NET Core's
// cookie authentication (K1-K3).
//
// The scheme is the `session_token` cookie. It is never the default scheme (K2), so nothing authenticates until
// a before-action asks: `restore` is `restore_authentication`, `require` is `require_authentication`'s first
// step. The session lookup is the cookie handler's `OnValidatePrincipal` (token -> `sessions` row -> user; either
// gone rejects the principal). The hourly `Session#resume` write and the cookie's re-issue are `restore`'s, not the
// event's, because they need the request's client address, which the kit still derives (forwarded headers arrive in
// unit 2.6); the re-issue goes through `SignInAsync` so the framework writes the cookie.
namespace Campfire.App.Web

open System
open System.Security.Claims
open System.Text.Encodings.Web
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Campfire.App
open Campfire.Db
open Campfire.Ruby

module SessionCookie =
    /// `cookies.signed.permanent[:session_token] = { httponly: true, same_site: :lax }`.
    let configure (options: CookieAuthenticationOptions) : unit =
        options.Cookie.Name <- Schemes.SessionCookie
        options.Cookie.HttpOnly <- true
        options.Cookie.SameSite <- SameSiteMode.Lax
        options.Cookie.SecurePolicy <- CookieSecurePolicy.SameAsRequest
        options.Cookie.IsEssential <- true
        options.SlidingExpiration <- false

    /// K3: is this cookie's session still valid? `Session.find_by(token:)`, then its user.
    let validatePrincipal (context: CookieValidatePrincipalContext) : Task =
        let http = context.HttpContext
        let current = Current.get http
        match context.Principal with
        | null -> Task.CompletedTask
        | principal ->
            match principal.FindFirst Schemes.TokenClaim with
            | null -> context.RejectPrincipal()
            | claim ->
                let db = http.RequestServices.GetRequiredService<Campfire.Db.Ef.CampfireDb>()
                let found =
                    Campfire.Db.Ef.Reads.run db (fun db ->
                        match Session.findByToken db claim.Value with
                        | None -> None
                        | Some session -> User.findById db session.UserId |> Option.map (fun user -> session, user))
                match found with
                | Ok(Some(session, user)) ->
                    current.Session <- Some session
                    current.User <- Some user
                    current.AuthenticatedBy <- BySession
                | Ok None -> context.RejectPrincipal()
                | Error error ->
                    current.LookupError <- Some(AppErrors.dbError error)
                    context.RejectPrincipal()
            Task.CompletedTask

    /// The client facts a session records: user agent and remote IP (an error when the IP cannot be told).
    type Client = unit -> Result<string option * string, Campfire.Kit.Error>

/// K4: `bot_authentication` as a second scheme, tried after the session scheme and never chosen by path. The key is
/// `params[:bot_key]` (path, query or body, read from the request's `Ctx`); a blank or unknown key is "no result" and
/// a key that is not a string (a hash or array) fails, which `Authentication.require` answers with a 500 as Rails'
/// `NoMethodError` does.
type BotKeyAuthenticationHandler(options: IOptionsMonitor<AuthenticationSchemeOptions>, logger: ILoggerFactory, encoder: UrlEncoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() : Task<AuthenticateResult> =
        let http = this.Context
        match http.Items.TryGetValue Campfire.Kit.Adapter.CtxItemKey with
        | true, (:? Campfire.Kit.Ctx as ctx) ->
            match ctx.Params.Get "bot_key" |> ValueOption.filter (fun p -> p.IsPresent) with
            | ValueNone -> Task.FromResult(AuthenticateResult.NoResult())
            | ValueSome param ->
                match param.AsStr with
                | null -> Task.FromResult(AuthenticateResult.Fail(exn "undefined method 'strip' for bot_key"))
                | key ->
                    let botKey = Ruby.strip key
                    let db = http.RequestServices.GetRequiredService<Campfire.Db.Ef.CampfireDb>()
                    let current = Current.get http
                    match Campfire.Db.Ef.Reads.run db (fun db -> User.authenticateBot db botKey) with
                    | Ok(Some bot) ->
                        current.User <- Some bot
                        current.AuthenticatedBy <- AuthenticatedBy.BotKey
                        let identity = ClaimsIdentity([ Claim(ClaimTypes.NameIdentifier, string bot.Id) ], this.Scheme.Name)
                        let ticket = AuthenticationTicket(ClaimsPrincipal identity, this.Scheme.Name)
                        Task.FromResult(AuthenticateResult.Success ticket)
                    | Ok None -> Task.FromResult(AuthenticateResult.NoResult())
                    | Error error ->
                        current.LookupError <- Some(AppErrors.dbError error)
                        Task.FromResult(AuthenticateResult.NoResult())
        | _ -> Task.FromResult(AuthenticateResult.NoResult())

module Authentication =
    /// The framework's `SignInAsync` writes no-cache headers of its own; Rails' response headers are the kit's, so
    /// they are put back as they were.
    let private keepingHeaders (http: HttpContext) (work: unit -> Task) : Task =
        task {
            let names = [| "Cache-Control"; "Pragma"; "Expires" |]
            let before = names |> Array.map (fun name -> name, http.Response.Headers[name])
            do! work ()
            for name, value in before do
                if value.Count = 0 then http.Response.Headers.Remove name |> ignore else http.Response.Headers[name] <- value
        }
        :> Task

    /// Sign in with a fresh, 20-year cookie (`cookies.signed.permanent`), dated by the harness's clock.
    let private issue (http: HttpContext) (principal: ClaimsPrincipal) : Task =
        let now = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()
        let properties =
            AuthenticationProperties(
                IsPersistent = true,
                IssuedUtc = Nullable now,
                ExpiresUtc = Nullable(Campfire.RailsCompat.Cookies.permanentExpiresAt now)
            )
        keepingHeaders http (fun () -> http.SignInAsync(Schemes.Session, principal, properties))

    let private principalFor (token: string) : ClaimsPrincipal =
        ClaimsPrincipal(ClaimsIdentity([ Claim(Schemes.TokenClaim, token) ], Schemes.Session))

    let private dbFailure (error: DbError) : Error = Legacy(AppErrors.dbError error)

    /// `find_session_by_cookie`: the session the cookie names, without resuming it.
    let session (http: HttpContext) : Task<Result<Session option, Error>> =
        task {
            let! result = http.AuthenticateAsync Schemes.Session
            let current = Current.get http
            match current.LookupError with
            | Some error -> return Result.Error(Legacy error)
            | None -> return Ok(if result.Succeeded then current.Session else None)
        }

    /// `restore_authentication`: resume the session named by the `session_token` cookie. `Session#resume` writes the
    /// row at most hourly and, on that schedule, the cookie is signed again (Rails signs it on every request; the
    /// difference is recorded in the README's known differences).
    let restore (http: HttpContext) (client: SessionCookie.Client) : Task<Result<bool, Error>> =
        task {
            let! found = session http
            match found with
            | Result.Error error -> return Result.Error error
            | Ok None -> return Ok false
            | Ok(Some session) ->
                let current = Current.get http
                let services = http.RequestServices
                let time = services.GetRequiredService<TimeProvider>()
                if Session.needsResume session (RailsTime.now time) then
                    match client () with
                    | Result.Error error -> return Result.Error(Legacy error)
                    | Ok(userAgent, ip) ->
                        let app = services.GetRequiredService<AppState>()
                        let db = services.GetRequiredService<Campfire.Db.Ef.CampfireDb>()
                        let! resumed =
                            Campfire.Db.Ef.Writes.run app.Gate app.Env db (fun tx -> Session.resume tx session userAgent (Some ip))
                        match resumed with
                        | Result.Error error -> return Result.Error(dbFailure error)
                        | Ok resumed ->
                            current.Session <- Some resumed
                            do! issue http (principalFor resumed.Token)
                            return Ok true
                else
                    return Ok true
        }

    /// `bot_authentication`: the `BotKey` scheme. Fails (a 500) for a key that is not a string, or when the lookup
    /// could not be made.
    let botAuthenticate (http: HttpContext) : Task<Result<bool, Error>> =
        task {
            let! result = http.AuthenticateAsync Schemes.BotKey
            let current = Current.get http
            match current.LookupError with
            | Some error when not result.Succeeded -> return Result.Error(Legacy error)
            | _ ->
                if result.Succeeded then return Ok true
                elif isNull result.Failure then return Ok false
                else return Result.Error(Internal(nonNull result.Failure))
        }

    /// `require_authentication`: restore (the session scheme), then the bot key, else whatever `otherwise` does (the
    /// redirect to sign in, K2's challenge).
    let require (http: HttpContext) (client: SessionCookie.Client) (otherwise: unit -> Task<Result<unit, Error>>) : Task<Result<unit, Error>> =
        task {
            match! restore http client with
            | Result.Error error -> return Result.Error error
            | Ok true -> return Ok()
            | Ok false ->
                match! botAuthenticate http with
                | Result.Error error -> return Result.Error error
                | Ok true -> return Ok()
                | Ok false -> return! otherwise ()
        }

    /// `deny_bots`: 403 for a request a bot key authenticated.
    let denyBots (http: HttpContext) : Result<unit, Error> =
        if (Current.get http).AuthenticatedBy = AuthenticatedBy.BotKey then Result.Error(Error.Status 403) else Ok()

    /// `start_new_session_for(user)`: the `sessions` row, then the cookie.
    let startNewSessionFor (http: HttpContext) (client: SessionCookie.Client) (user: User) : Task<Result<Session, Error>> =
        task {
            match client () with
            | Result.Error error -> return Result.Error(Legacy error)
            | Ok(userAgent, ip) ->
                let services = http.RequestServices
                let app = services.GetRequiredService<AppState>()
                let db = services.GetRequiredService<Campfire.Db.Ef.CampfireDb>()
                let! started = Campfire.Db.Ef.Writes.run app.Gate app.Env db (fun tx -> Session.start tx user.Id userAgent (Some ip))
                match started with
                | Result.Error error -> return Result.Error(dbFailure error)
                | Ok session ->
                    do! issue http (principalFor session.Token)
                    let current = Current.get http
                    current.Session <- Some session
                    current.User <- Some user
                    current.AuthenticatedBy <- BySession
                    return Ok session
        }

    /// `terminate_current_session` less the Rails session (unit 2.5): destroy the row, delete the cookie, and
    /// disconnect the user's sockets (`reset_remote_connections`, errors only logged).
    let terminateCurrentSession (http: HttpContext) : Task<Result<unit, Error>> =
        task {
            let services = http.RequestServices
            let app = services.GetRequiredService<AppState>()
            let db = services.GetRequiredService<Campfire.Db.Ef.CampfireDb>()
            let current = Current.get http
            let! destroyed =
                match current.Session with
                | Some session -> Campfire.Db.Ef.Writes.run app.Gate app.Env db (fun tx -> Session.destroy tx session)
                | None -> Task.FromResult(Ok())
            match destroyed with
            | Result.Error error -> return Result.Error(dbFailure error)
            | Ok() ->
                // Rails' `cookies.delete(:session_token)` (`max-age=0`, no `httponly`), which the framework's
                // `SignOutAsync` does not write; the principal is not cached past this request.
                http.Response.Headers.Append("Set-Cookie", Campfire.Kit.CookieJar.deleteCookieHeader Schemes.SessionCookie Campfire.Kit.DeleteOptions.Default)
                match current.User with
                | Some user ->
                    match! Campfire.Db.Ef.Writes.run app.Gate app.Env db (fun tx -> User.resetRemoteConnections tx user) with
                    | Ok() -> ()
                    | Result.Error error ->
                        services.GetRequiredService<Campfire.Kit.Kit>().Logger.LogWarning("Could not disconnect remote connections on sign out: {Error}", DbError.display error)
                | None -> ()
                return Ok()
        }
