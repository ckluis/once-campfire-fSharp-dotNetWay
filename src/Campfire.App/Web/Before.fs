// plans/design-kit-auth.md section 3 (Web/Before.fs): `ApplicationController`'s before-action chain, on `HttpContext`.
//
// `ApplicationController` does `include AllowBrowser, Authentication, Authorization, BlockBannedRequests,
// SetCurrentRequest, SetPlatform, TrackedRoomVisit, VersionHeaders`. Ruby includes a list right to left, so the chain the
// reference runs is (`reference-tools/campfire/callbacks.rb`):
//
// 1. `set_version_headers`
// 2. `Current.request = request` (nothing to store: the request is the `HttpContext`)
// 3. `reject_banned_ip`, unless GET/HEAD
// 4. `require_authentication`
// 5. `deny_bots`
// 6. `verify_authenticity_token`, unless authenticated by bot key
// 7. `allow_browser`
//
// then the controller's own before-actions. `Before.chain` runs 1-7 with a controller's skips; a step that rendered or
// redirected ends the action with `Halt`, so an action reads
//
//     fun http -> taskResult { do! Before.chain Before.Default http; ... }
//
// (`verify_same_origin_request`, the cross-origin `<script>` check on a GET that renders JavaScript, applies to no Web
// action: the one that renders JavaScript, the service worker, skips forgery protection.)
namespace Campfire.App.Web

open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby

/// How a controller's `allow_unauthenticated_access` / `require_unauthenticated_access` change the chain for one action.
type Auth =
    /// `require_authentication`
    | Required
    /// `allow_unauthenticated_access`: skip `require_authentication`.
    | Skipped
    /// `require_unauthenticated_access`: skip `require_authentication`, then (after the rest of the chain) `restore_authentication`
    /// and `redirect_signed_in_user_to_root`.
    | RequireUnauthenticated

type Before =
    { Auth: Auth
      /// `deny_bots` (skipped by `allow_bot_access`).
      DenyBots: bool
      /// `verify_authenticity_token` (skipped by `skip_forgery_protection`).
      ForgeryProtection: bool }

module Before =
    let Default: Before =
        { Auth = Required
          DenyBots = true
          ForgeryProtection = true }

    let allowUnauthenticatedAccess (before: Before) : Before = { before with Auth = Skipped }

    let requireUnauthenticatedAccess (before: Before) : Before = { before with Auth = RequireUnauthenticated }

    let allowBotAccess (before: Before) : Before = { before with DenyBots = false }

    let skipForgeryProtection (before: Before) : Before = { before with ForgeryProtection = false }

    /// `head status` from a before-action. Rails sets the controller's `formats` only after the before-callbacks have run,
    /// so a `head` there falls back to `Mime[:html]` whatever the request format: `text/html`, no charset.
    let head (status: int) : HttpHandler =
        fun http ->
            http.Response.StatusCode <- status
            if not (Respond.noContent status) then http.Response.ContentType <- "text/html"
            if not (Respond.noBody status) then http.Response.ContentLength <- 0L
            Task.CompletedTask

    /// `ensure_can_administer`: `head :forbidden` unless the current user can administer.
    let ensureCanAdminister (http: HttpContext) : Result<unit, Error> =
        let allowed = Current.user http |> Option.exists (fun user -> User.canAdminister user None false)
        if allowed then Ok() else Action.halt (head 403)

    /// `RoomScoped#set_room`: `Current.user.memberships.find_by!(room_id: params[:room_id])`, 404 otherwise. Returns the membership and
    /// its room.
    let setRoom (http: HttpContext) : Result<Membership * Room, Error> =
        match Current.requireUser http with
        | Result.Error e -> Result.Error e
        | Ok user ->
            match (match Params.value http "room_id" with null -> None | id -> Ruby.integerCast id) with
            | None -> Result.Error NotFound
            | Some roomId ->
                Data.read http (fun db ->
                    let membership = Membership.findByRoomAndUser db roomId user.Id |> Err.orNotFound "Membership"
                    // `@membership.room` is nil when the room is gone (memberships have no foreign key to rooms), and the action
                    // fails on it: a 500, where `Membership#room`'s `RecordNotFound` would be a 404.
                    let room =
                        match Room.findById db membership.RoomId with
                        | Some room -> room
                        | None -> Err.fail (DbError.other "the membership's room is gone")
                    membership, room)

    /// `set_version_headers`: `X-Version` and `X-Rev` (`config/initializers/version.rb`); Rails drops a header set to nil.
    let private setVersionHeaders (http: HttpContext) : unit =
        let config = (Data.app http).Config
        http.Response.Headers["x-version"] <- config.AppVersion
        match config.GitRevision with
        | Some revision -> http.Response.Headers["x-rev"] <- revision
        | None -> ()

    /// `reject_banned_ip`, unless the request is safe (GET/HEAD): 429 for a banned `remote_ip`.
    let private rejectBannedIp (http: HttpContext) : Result<unit, Error> =
        if HttpMethods.IsGet http.Request.Method || HttpMethods.IsHead http.Request.Method then
            Ok()
        else
            let _, ip = SessionCookie.client http
            match Data.read http (fun db -> Ban.banned db ip) with
            | Result.Error e -> Result.Error e
            | Ok true -> Action.halt (head 429)
            | Ok false -> Ok()

    /// `request_authentication`: remember where we were, then off to sign in.
    let private requestAuthentication (http: HttpContext) : Result<unit, Error> =
        ReturnTo.set http (Pages.requestUrl http)
        match Respond.redirectTo http (Respond.urlFor http (Routes.newSession ())) with
        | Ok handler -> Action.halt handler
        | Result.Error e -> Result.Error e

    /// `require_authentication`: `restore_authentication || bot_authentication || request_authentication`.
    let private requireAuthentication (http: HttpContext) : Task<Result<unit, Error>> =
        Authentication.require http (fun () -> Task.FromResult(requestAuthentication http))

    /// `redirect_signed_in_user_to_root`
    let private redirectSignedInUserToRoot (http: HttpContext) : Result<unit, Error> =
        if Current.signedIn http then
            match Respond.redirectTo http (Respond.urlFor http (Routes.root ())) with
            | Ok handler -> Action.halt handler
            | Result.Error e -> Result.Error e
        else
            Ok()

    /// `allow_browser versions: VERSIONS, block: -> { render template: "sessions/incompatible_browser" }`
    ///
    /// The platform parsed for the check is kept for the layout's `platform`, so a page parses its User-Agent once. A
    /// missing or blank one isn't checked, and is parsed only if a page renders.
    let private allowBrowser (http: HttpContext) : Result<unit, Error> =
        let agent = http.Request.Headers.UserAgent
        if agent.Count = 0 then
            Ok()
        else
            let header = agent.ToString()
            if not (Campfire.App.UserAgent.isPresent header) then
                Ok()
            else
                let platform = Campfire.App.ApplicationPlatform.create header
                (Current.get http).Platform <- Some platform
                if Campfire.App.ApplicationPlatform.browserBlocked platform then
                    match Pages.incompatibleBrowser http with
                    | Ok handler -> Action.halt handler
                    | Result.Error e -> Result.Error e
                else
                    Ok()

    /// `ApplicationController`'s before-actions, in the order the reference runs them.
    let chain (before: Before) (http: HttpContext) : Task<Result<unit, Error>> =
        task {
            setVersionHeaders http
            match rejectBannedIp http with
            | Result.Error e -> return Result.Error e
            | Ok() ->
                let! authenticated =
                    if before.Auth = Required then requireAuthentication http else Task.FromResult(Ok())
                match authenticated with
                | Result.Error e -> return Result.Error e
                | Ok() ->
                    let denied =
                        if before.DenyBots then
                            Authentication.denyBots http |> Result.mapError (fun _ -> Error.Halt(head 403))
                        else
                            Ok()
                    match denied with
                    | Result.Error e -> return Result.Error e
                    | Ok() ->
                        let! verified =
                            if before.ForgeryProtection && (Current.get http).AuthenticatedBy <> Campfire.App.AuthenticatedBy.BotKey then
                                Csrf.verifyAuthenticityToken http
                            else
                                Task.FromResult(Ok())
                        match verified with
                        | Result.Error e -> return Result.Error e
                        | Ok() ->
                            match allowBrowser http with
                            | Result.Error e -> return Result.Error e
                            | Ok() ->
                                if before.Auth = RequireUnauthenticated then
                                    match! Authentication.restore http with
                                    | Result.Error e -> return Result.Error e
                                    | Ok _ -> return redirectSignedInUserToRoot http
                                else
                                    return Ok()
        }
