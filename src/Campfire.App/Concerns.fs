// Port of rust/crates/campfire/src/concerns.rs
//
// `ApplicationController`'s concerns as before-action functions on `Ctx`.
//
// `ApplicationController` does `include AllowBrowser, Authentication, Authorization,
// BlockBannedRequests, SetCurrentRequest, SetPlatform, TrackedRoomVisit, VersionHeaders`. Ruby
// includes a list right to left, so the `included` hooks (and their `before_action`s) run in
// reverse, and the chain the reference actually runs is (dumped with
// `reference-tools/campfire/callbacks.rb`):
//
// 1. `set_version_headers` (VersionHeaders)
// 2. `Current.request = request` (SetCurrentRequest)
// 3. `reject_banned_ip`, unless GET/HEAD (BlockBannedRequests)
// 4. `require_authentication` (Authentication)
// 5. `deny_bots` (Authentication)
// 6. `verify_authenticity_token`, unless authenticated by bot key (Authentication's
//    `protect_from_forgery with: :exception, unless: -> { authenticated_by.bot_key? }`)
// 7. `allow_browser` (AllowBrowser)
//
// then the controller's own before-actions. `beforeActions` runs 1-7 with a controller's skips
// (`Before`); controllers then call their own (`setRoom`, `ensureCanAdminister`, ...) in
// declaration order. Everything returns `Error(Halt ..)` to stop the chain the way a Rails callback
// that renders or redirects does, so actions just use `let!` in `act { }`:
//
//     let show (c: Ctx) =
//         act {
//             do! Concerns.beforeActions c Before.Default
//             let! membership, room = Concerns.setRoom c   // RoomScoped
//             ...
//         }
//
// The `Current` attributes (`Current.user`, `Current.session`) are in `Current` (`Current.fs`).
namespace Campfire.App

open System
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Campfire.App.Presenters
open Campfire.Kit
open Campfire.Db
open Campfire.Ruby
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Templates.Sessions

/// How a controller's `allow_unauthenticated_access` / `require_unauthenticated_access` /
/// `allow_bot_access` / `skip_forgery_protection` change the chain for one action.
type Authentication =
    /// `require_authentication`
    | Required
    /// `allow_unauthenticated_access`: skip `require_authentication`.
    | Skipped
    /// `require_unauthenticated_access`: skip `require_authentication`, then (after the rest of the
    /// chain, as it's declared in the subclass) `restore_authentication` and
    /// `redirect_signed_in_user_to_root`.
    | RequireUnauthenticated

type Before =
    { Authentication: Authentication
      /// `deny_bots` (skipped by `allow_bot_access`).
      DenyBots: bool
      /// `verify_authenticity_token` (skipped by `skip_forgery_protection`).
      ForgeryProtection: bool }

module Before =
    let Default: Before =
        { Authentication = Required
          DenyBots = true
          ForgeryProtection = true }

    let allowUnauthenticatedAccess (before: Before) : Before = { before with Authentication = Skipped }

    let requireUnauthenticatedAccess (before: Before) : Before = { before with Authentication = RequireUnauthenticated }

    let allowBotAccess (before: Before) : Before = { before with DenyBots = false }

    let skipForgeryProtection (before: Before) : Before = { before with ForgeryProtection = false }

module Concerns =
    // --- Helpers -------------------------------------------------------------------------------

    /// `head status` from a before-action. Rails sets the controller's `formats` only after the
    /// before-callbacks have run (`AbstractController::Callbacks#process_action` wraps
    /// `ActionController::Rendering#process_action`), so a `head` there falls back to `Mime[:html]`
    /// whatever the request format: `text/html`, no charset. (A `head` inside the action itself uses
    /// the request format: that's `c.Head`.)
    let head (status: int) : Response =
        let response = Response(status)
        if Status.hasNoContent status then response else response.ContentType "text/html"

    // --- Current ---------------------------------------------------------------------------------

    let currentUser = Current.currentUser

    let requireCurrentUser = Current.requireCurrentUser

    let currentSession = Current.currentSession

    let signedIn = Current.signedIn

    let authenticatedBy = Current.authenticatedBy

    let platform = Current.platform

    let lastRoomCookie = Current.lastRoomCookie

    let lastRoomVisitedIn = Current.lastRoomVisitedIn

    // --- VersionHeaders ----------------------------------------------------------------------------

    /// `set_version_headers`: `X-Version` and `X-Rev` (`config/initializers/version.rb`).
    let setVersionHeaders (c: Ctx) : unit =
        let config = c.App.Config
        c.SetHeader("x-version", config.AppVersion)
        // Rails drops a header set to nil.
        match config.GitRevision with
        | Some revision -> c.SetHeader("x-rev", revision)
        | None -> ()

    // --- SetCurrentRequest -------------------------------------------------------------------------

    /// `Current.request = request`. `default_url_options` then carry the request's host and
    /// protocol, which is what `Ctx.UrlFor` already does, so there's nothing to store.
    let setCurrentRequest (_: Ctx) : unit = ()

    // --- BlockBannedRequests -----------------------------------------------------------------------

    /// `reject_banned_ip`, unless the request is safe (GET/HEAD): 429 for a banned `remote_ip`.
    let rejectBannedIp (c: Ctx) : Task<Result<unit, Error>> =
        act {
            if not (c.Request.IsGet || c.Request.IsHead) then
                let! ip = c.Request.RemoteIp()
                let! banned = c.Read(fun db -> Ban.banned db ip)
                if banned then return! halt (head 429)
        }

    // --- Authentication ----------------------------------------------------------------------------

    let private userAgentAndIp (c: Ctx) : Result<string option * string, Error> =
        match c.Request.RemoteIp() with
        | Error e -> Error e
        | Ok ip -> Ok((match c.Request.UserAgent with null -> None | ua -> Some ua), ip)

    let private webError (error: Campfire.App.Web.Error) : Error = Campfire.App.Web.Errors.toKit error

    /// The session cookie scheme's answers (`Web.Authentication`), as the `Ctx` slots the legacy actions read.
    let private syncCurrent (c: Ctx) : unit =
        let current = Campfire.App.Web.Current.get c.Http
        match current.Session with
        | Some session -> c.SetCurrent(CurrentSessionSlot session)
        | None -> ()
        match current.User with
        | Some user ->
            c.SetCurrent(CurrentUserSlot user)
            Current.setAuthenticatedBy c current.AuthenticatedBy
        | None -> ()

    /// `Authentication::SessionLookup#find_session_by_cookie`
    let findSessionByCookie (c: Ctx) : Task<Result<Session option, Error>> =
        task {
            let! found = Campfire.App.Web.Authentication.session c.Http
            return found |> Result.mapError webError
        }

    /// `restore_authentication`: resume the session named by the `session_token` cookie (K3: the cookie scheme).
    let restoreAuthentication (c: Ctx) : Task<Result<bool, Error>> =
        task {
            let! restored = Campfire.App.Web.Authentication.restore c.Http (fun () -> userAgentAndIp c)
            match restored with
            | Ok true ->
                syncCurrent c
                return Ok true
            | Ok false -> return Ok false
            | Error e -> return Error(webError e)
        }

    /// `bot_authentication`: `params[:bot_key].present?` and a matching active bot (K4: the bot-key scheme).
    let botAuthentication (c: Ctx) : Task<Result<bool, Error>> =
        task {
            match! Campfire.App.Web.Authentication.botAuthenticate c.Http with
            | Ok true ->
                syncCurrent c
                return Ok true
            | Ok false -> return Ok false
            | Error e -> return Error(webError e)
        }

    /// `request_authentication`: remember where we were, then off to sign in.
    let requestAuthentication (c: Ctx) : Result<unit, Error> =
        let url = c.Request.Url
        c.Session().Insert("return_to_after_authenticating", url)
        let location = c.UrlFor(Routes.newSession ())
        match c.RedirectTo location with
        | Ok response -> halt response
        | Error e -> Error e

    /// `require_authentication`: `restore_authentication || bot_authentication || request_authentication`.
    let requireAuthentication (c: Ctx) : Task<Result<unit, Error>> =
        task {
            let otherwise () : Task<Result<unit, Campfire.App.Web.Error>> =
                task {
                    let! bot = botAuthentication c
                    match bot with
                    | Error e -> return Error(Campfire.App.Web.Error.Legacy e)
                    | Ok true -> return Ok()
                    | Ok false ->
                        match requestAuthentication c with
                        | Error e -> return Error(Campfire.App.Web.Error.Legacy e)
                        | Ok() -> return Ok()
                }
            let! required = Campfire.App.Web.Authentication.require c.Http (fun () -> userAgentAndIp c) otherwise
            syncCurrent c
            return required |> Result.mapError webError
        }

    /// `redirect_signed_in_user_to_root`
    let redirectSignedInUserToRoot (c: Ctx) : Result<unit, Error> =
        if signedIn c then
            match c.RedirectTo(c.UrlFor(Routes.root ())) with
            | Ok response -> halt response
            | Error e -> Error e
        else
            Ok()

    /// `has_secure_password`'s `password=`, hashed on the blocking pool ahead of the write that saves
    /// it, so bcrypt (about 250 ms) holds neither an async thread nor the database writer.
    let passwordDigest (c: Ctx) (password: string option) : Task<Result<PasswordDigest option, Error>> =
        task {
            match password with
            | None -> return Ok None
            | Some password ->
                try
                    let! digest = PasswordDigest.hash password c.App.Env.BcryptCost
                    return Ok(Some digest)
                with e ->
                    return Error(Internal e)
        }

    /// `User.active.authenticate_by(email_address:, password:)`: the user is looked up on a reader,
    /// and the password checked once the reader is released.
    let authenticateBy (c: Ctx) (emailAddress: string) (password: string) : Task<Result<User option, Error>> =
        task {
            // `authenticate_by` returns nil for a blank password before looking anything up.
            if password = "" then
                return Ok None
            else
                match! c.Read(fun db -> User.findActiveByEmailAddress db emailAddress) with
                | Error e -> return Error e
                | Ok candidate ->
                    try
                        let! user = Task.Run(fun () -> User.authenticated candidate password)
                        return Ok user
                    with e ->
                        return Error(Internal e)
        }

    /// `start_new_session_for(user)`
    let startNewSessionFor (c: Ctx) (user: User) : Task<Result<Session, Error>> =
        task {
            let! started = Campfire.App.Web.Authentication.startNewSessionFor c.Http (fun () -> userAgentAndIp c) user
            match started with
            | Ok session ->
                syncCurrent c
                return Ok session
            | Error e -> return Error(webError e)
        }

    /// `terminate_current_session`: destroy the session, drop the cookie, reset the Rails session, and disconnect
    /// the user's sockets (`reset_remote_connections`, errors only logged).
    let terminateCurrentSession (c: Ctx) : Task<Result<unit, Error>> =
        task {
            let current = Campfire.App.Web.Current.get c.Http
            current.Session <- currentSession c
            current.User <- currentUser c
            let! ended = Campfire.App.Web.Authentication.terminateCurrentSession c.Http
            match ended with
            | Ok() ->
                c.ResetSession()
                return Ok()
            | Error e -> return Error(webError e)
        }

    /// `post_authenticating_url`: `session.delete(:return_to_after_authenticating) || root_url`.
    let postAuthenticatingUrl (c: Ctx) : string =
        match c.Session().Remove "return_to_after_authenticating" with
        | ValueSome(Campfire.RailsCompat.Value.String url) -> url
        | ValueSome Campfire.RailsCompat.Value.Null
        | ValueNone -> c.UrlFor(Routes.root ())
        | ValueSome other -> Campfire.RailsCompat.Json.encode other

    /// `verify_authenticity_token`: the framework's `ICsrfProtection` (K5), and the mark `verify_same_origin_request` reads.
    let verifyAuthenticityToken (c: Ctx) : Task<Result<unit, Error>> =
        task {
            c.MarkForSameOriginVerification()
            let! verdict = Campfire.App.Web.Csrf.verifyAuthenticityToken c.Http
            return verdict |> Result.mapError webError
        }

    /// `deny_bots`: 403 for bot-key requests.
    let denyBots (c: Ctx) : Result<unit, Error> =
        match Campfire.App.Web.Authentication.denyBots c.Http with
        | Ok() -> Ok()
        | Error _ -> halt (head Status.Forbidden)

    // --- Authorization -----------------------------------------------------------------------------

    /// `ensure_can_administer`: 403 unless `Current.user.can_administer?` (no record).
    let ensureCanAdminister (c: Ctx) : Result<unit, Error> =
        let allowed = currentUser c |> Option.exists (fun user -> User.canAdminister user None false)
        if allowed then Ok() else halt (head Status.Forbidden)

    // --- AllowBrowser --------------------------------------------------------------------------------

    /// What `IncompatibleBrowser.render` writes with the Turbo-Frame layout's help, sized from its last render.
    let private incompatibleBrowserSize = RenderSize()

    /// `render template: "sessions/incompatible_browser"` (200). Rendered from a before-action, so
    /// it's HTML whatever the request format. The layout is the controller's: turbo-rails' frame
    /// layout for a Turbo-Frame request, except in controllers that declare their own layout
    /// (`MessagesController` and its `Messages::ByBotsController`), which always use the application
    /// layout.
    let private renderIncompatibleBrowser (c: Ctx) : Task<Result<Response, Error>> =
        task {
            let ownLayout =
                match c.Current<MatchedRoute>() with
                | ValueSome route -> route.Endpoint.StartsWith("messages#", StringComparison.Ordinal) || route.Endpoint.StartsWith("messages/by_bots#", StringComparison.Ordinal)
                | ValueNone -> false
            // An explicit `render template:`, so no format lookup: a blocked browser gets this page for
            // /webmanifest.json, /service-worker.js or `Accept: application/json` alike (verified against
            // the reference), never a 406.
            let! response =
                if ownLayout then
                    Layout.pageInAnyFormat c Status.Ok (fun ctx ->
                        incompatibleBrowserSize.Render(fun w -> IncompatibleBrowser.render w ctx))
                else
                    Layout.pageOrFrameInAnyFormat
                        c
                        Status.Ok
                        (fun ctx -> incompatibleBrowserSize.Render(fun w -> IncompatibleBrowser.render w ctx))
                        (fun ctx -> Layouts.frame incompatibleBrowserSize IncompatibleBrowser.head (fun w -> IncompatibleBrowser.content w ctx))
            return response |> Result.map (fun response -> response.ContentType Response.HtmlUtf8)
        }

    /// `allow_browser versions: VERSIONS, block: -> { render template: "sessions/incompatible_browser" }`
    ///
    /// The platform parsed for the check is kept for the layout's `platform`, so a page parses its
    /// User-Agent once. A missing or blank one isn't checked, and is parsed only if a page renders.
    let allowBrowser (c: Ctx) : Task<Result<unit, Error>> =
        task {
            match c.Request.UserAgent with
            | null -> return Ok()
            | header when not (UserAgent.isPresent header) -> return Ok()
            | header ->
                let platform = ApplicationPlatform.create header
                let blocked = ApplicationPlatform.browserBlocked platform
                c.SetCurrent platform
                if blocked then
                    match! renderIncompatibleBrowser c with
                    | Ok response -> return halt response
                    | Error e -> return Error e
                else
                    return Ok()
        }

    // --- The ApplicationController chain ---------------------------------------------------------

    /// `ApplicationController`'s before-actions, in the order the reference runs them.
    let beforeActions (c: Ctx) (before: Before) : Task<Result<unit, Error>> =
        act {
            setVersionHeaders c
            setCurrentRequest c
            do! rejectBannedIp c
            if before.Authentication = Required then do! requireAuthentication c
            if before.DenyBots then do! denyBots c
            if before.ForgeryProtection && authenticatedBy c <> BotKey then do! verifyAuthenticityToken c
            do! allowBrowser c
            if before.Authentication = RequireUnauthenticated then
                let! (restored: bool) = restoreAuthentication c
                ignore restored
                do! redirectSignedInUserToRoot c
        }

    // --- TrackedRoomVisit ----------------------------------------------------------------------------

    /// `remember_last_room_visited`: `cookies.permanent[:last_room] = @room.id`.
    ///
    /// Only when it changes: Rails sets it on every room page.
    let rememberLastRoomVisited (c: Ctx) (roomId: int64) : unit =
        let roomId = string roomId
        if c.Cookies.Get "last_room" <> roomId then c.Cookies.Set("last_room", (Cookie.New roomId).AsPermanent())

    /// `last_room_visited`: the `last_room` cookie's room if the user is in it, else
    /// `Current.user.rooms.original`.
    let lastRoomVisited (c: Ctx) : Task<Result<Room option, Error>> =
        match currentUser c with
        | None -> Task.FromResult(Ok None)
        | Some user ->
            let lastRoom = lastRoomCookie c
            c.Read(fun db -> lastRoomVisitedIn db user.Id lastRoom)

    // --- RoomScoped ----------------------------------------------------------------------------------

    /// `RoomScoped#set_room`: `Current.user.memberships.find_by!(room_id: params[:room_id])`, 404
    /// otherwise. Returns the membership and its room.
    let setRoom (c: Ctx) : Task<Result<Membership * Room, Error>> =
        task {
            match requireCurrentUser c with
            | Error e -> return Error e
            | Ok user ->
                match (match c.ParamStr "room_id" with null -> None | id -> Ruby.integerCast id) with
                | None -> return Error NotFound
                | Some roomId ->
                    return!
                        c.Read(fun db ->
                            let membership =
                                Membership.findByRoomAndUser db roomId user.Id |> Err.orNotFound "Membership"
                            // `@membership.room` is nil when the room is gone (memberships have no foreign key to
                            // rooms), and the action fails on it: a 500, where `Membership#room`'s
                            // `RecordNotFound` would be a 404.
                            let room =
                                match Room.findById db membership.RoomId with
                                | Some room -> room
                                | None -> Err.fail (DbError.other "the membership's room is gone")
                            membership, room)
        }
