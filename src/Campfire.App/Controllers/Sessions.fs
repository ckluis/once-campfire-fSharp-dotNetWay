// Port of rust/crates/campfire/src/controllers/sessions.rs
//
// `SessionsController` (reference/app/controllers/sessions_controller.rb): sign in and out. A Web action (unit 2.9): the
// before-action chain is `Web.Before`, the response is `Web.Pages`/`Web.Respond`, and `rate_limit to: 10, within: 3.minutes`
// is the framework's limiter (K15, `Web.LoginThrottle`), acquired inside the action after the chain.
namespace Campfire.App.Controllers

open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Rails.Routes
open Campfire.Views.Templates.Sessions
open Campfire.App.Web

module Sessions =
    [<Literal>]
    let private Rejection = "Too many requests or unauthorized."

    /// `redirect_to first_run_url if User.none?`
    let private ensureUserExists (http: Microsoft.AspNetCore.Http.HttpContext) : Result<unit, Error> =
        match Data.read http (fun db -> Accounts.noUsers db) with
        | Result.Error e -> Result.Error e
        | Ok false -> Ok()
        | Ok true ->
            match Respond.redirectTo http (Respond.urlFor http (Routes.firstRun ())) with
            | Ok handler -> Action.halt handler
            | Result.Error e -> Result.Error e

    let private renderNew (http: Microsoft.AspNetCore.Http.HttpContext) (status: int) : Result<Falco.HttpHandler, Error> =
        result {
            do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
            let emailAddress = match Params.value http "email_address" with null -> None | email -> Some email
            let! helpContact = Data.read http (fun db -> Accounts.helpContact db)
            return! Pages.page http status (fun ctx -> New.rendered ctx emailAddress helpContact)
        }

    /// `flash.now[:alert] = "Too many requests or unauthorized."; render :new, status:`
    let private renderRejection (http: Microsoft.AspNetCore.Http.HttpContext) (status: int) : Result<Falco.HttpHandler, Error> =
        (Respond.flash http).Now("alert", Rejection)
        renderNew http status

    /// `rate_limiting(to:, within:, by: -> { request.remote_ip }, with: -> { render_rejection :too_many_requests })`
    let private rateLimit (http: Microsoft.AspNetCore.Http.HttpContext) : Result<unit, Error> =
        let _, ip = SessionCookie.client http
        if LoginThrottle.hit http ip then
            Ok()
        else
            match renderRejection http 429 with
            | Ok handler -> Action.halt handler
            | Result.Error e -> Result.Error e

    /// `User.active.authenticate_by(email_address:, password:)`: the user is looked up on a reader, and the password
    /// checked once the reader is released.
    let private authenticateBy (http: Microsoft.AspNetCore.Http.HttpContext) (emailAddress: string) (password: string) : Task<Result<User option, Error>> =
        task {
            // `authenticate_by` returns nil for a blank password before looking anything up.
            if password = "" then
                return Ok None
            else
                match Data.read http (fun db -> User.findActiveByEmailAddress db emailAddress) with
                | Result.Error e -> return Result.Error e
                | Ok candidate ->
                    try
                        let! user = Task.Run(fun () -> User.authenticated candidate password)
                        return Ok user
                    with e ->
                        return Result.Error(Internal e)
        }

    /// `allow_unauthenticated_access only: %i[ new create ]`, `before_action :ensure_user_exists, only: :new`
    let ``new``: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                do! ensureUserExists http
                return! renderNew http 200
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                do! rateLimit http
                let emailAddress = Params.value http "email_address"
                let password = Params.value http "password"
                let! user =
                    match emailAddress, password with
                    | null, _
                    | _, null -> Task.FromResult(Ok None)
                    | email, password -> authenticateBy http email password
                match user with
                | Some user ->
                    let! (_: Session) = Authentication.startNewSessionFor http user
                    // `post_authenticating_url`: `session.delete(:return_to_after_authenticating) || root_url`
                    let location =
                        match ReturnTo.take http with
                        | null -> Respond.urlFor http (Routes.root ())
                        | url -> url
                    return! Respond.redirectTo http location
                | None -> return! renderRejection http 401
            }

    /// `Push::Subscription.destroy_by(endpoint: params[:push_subscription_endpoint], user_id: Current.user.id)`
    let private removePushSubscription (http: Microsoft.AspNetCore.Http.HttpContext) : Task<Result<unit, Error>> =
        match Params.value http "push_subscription_endpoint", Current.user http with
        | null, _
        | _, None -> Task.FromResult(Ok())
        | endpoint, Some user -> Data.write http (fun tx -> PushSubscription.destroyByEndpoint tx user.Id endpoint)

    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! removePushSubscription http
                do! Authentication.terminateCurrentSession http
                Respond.resetSession http
                return! Respond.redirectTo http (Respond.urlFor http (Routes.root ()))
            }
