// Port of rust/crates/campfire/src/controllers/users.rs
//
// `UsersController` (reference/app/controllers/users_controller.rb): joining with the account's join code, and a user's page.
// Web actions (unit 2.10). (The module is `UsersController`: `Campfire.Views.Users` is a module too.) Its nested controllers
// (`Controllers/Users/`) are separate modules.
namespace Campfire.App.Controllers

open System
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.Views
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby
open Campfire.App.Web

module UsersController =
    module NewPage = Campfire.Views.Templates.Users.New
    module ShowPage = Campfire.Views.Templates.Users.Show

    /// `User.find(params[key])`: 404 when there's no such user.
    let findUser (http: HttpContext) (key: string) : Result<User, Error> =
        match (match Params.value http key with null -> None | id -> Ruby.integerCast id) with
        | None -> Result.Error NotFound
        | Some id ->
            match Data.read http (fun db -> User.findById db id) with
            | Result.Error e -> Result.Error e
            | Ok(Some user) -> Ok user
            | Ok None -> Result.Error NotFound

    /// `head :not_found if Current.account.join_code != params[:join_code]`
    let private verifyJoinCode (http: HttpContext) : Result<Account, Error> =
        // `Current.account.join_code` on nil raises NoMethodError.
        match Data.read http (fun db -> Account.first db) with
        | Result.Error e -> Result.Error e
        | Ok None -> Result.Error(Internal(exn "undefined method 'join_code' for nil"))
        | Ok(Some account) ->
            if Params.value http "join_code" <> account.JoinCode then Action.halt (Respond.head http 404) else Ok account

    /// `require_unauthenticated_access only: %i[ new create ]`, `before_action :verify_join_code`
    let ``new``: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.requireUnauthenticatedAccess Before.Default) http
                let! account = verifyJoinCode http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let! helpContact = Data.read http (fun db -> Accounts.helpContact db)
                let joinCode = account.JoinCode
                return!
                    Pages.viewPage http 200 (fun ctx -> NewPage.rendered ctx joinCode helpContact)
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.requireUnauthenticatedAccess Before.Default) http
                let! (_: Account) = verifyJoinCode http
                // `params.require(:user).permit(:name, :avatar, :email_address, :password)`
                let! required = Params.require http "user"
                let parameters = Params.permit required [ "name"; "email_address"; "password" ]
                let emailAddress = Map.tryFind "email_address" parameters
                // users.name is NOT NULL: a missing name fails the insert, as in Rails.
                let! name =
                    match Map.tryFind "name" parameters with
                    | Some name -> Ok name
                    | None -> Result.Error(Internal(exn "NOT NULL constraint failed: users.name"))
                let! passwordDigest = Data.passwordDigest http (Map.tryFind "password" parameters |> Option.filter (fun password -> password <> ""))
                let attributes =
                    { NewUser.create name with
                        EmailAddress = emailAddress
                        PasswordDigest = passwordDigest }
                let avatar, _ = Uploads.assignment http required "avatar"
                let! avatar = Uploads.stage http avatar

                // `User.create!(user_params)`, the avatar in the same transaction.
                let! result =
                    Data.tryWrite http (fun tx ->
                        let user = User.create tx attributes
                        user, AttachmentWrites.assign tx (Record.user user.Id) "avatar" avatar)
                    |> Task.map Ok
                match result with
                | Ok(user, pending) ->
                    AttachmentWrites.analyzeLater (Data.app http) pending
                    let! (_: Session) = Authentication.startNewSessionFor http user
                    return! Respond.redirectTo http (Respond.urlFor http (Routes.root ()))
                // rescue ActiveRecord::RecordNotUnique: `redirect_to new_session_url(email_address: user_params[:email_address])`
                | Result.Error error when DbError.isRecordNotUnique error ->
                    let location =
                        match emailAddress with
                        | Some email -> $"{Respond.urlFor http (Routes.newSession ())}?email_address={Ruby.cgiEscape email}"
                        | None -> Respond.urlFor http (Routes.newSession ())
                    return! Respond.redirectTo http location
                | Result.Error error -> return! Result.Error(Internal(Exception(DbError.display error)))
            }

    /// `before_action :set_user, only: :show`
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (user: User) = findUser http "id"
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let secrets = (Data.app http).Secrets
                let transferId = Accounts.transferId secrets user.Id (Data.now http)
                let user = Common.userSummary secrets user
                return!
                    Pages.viewPage http 200 (fun ctx -> ShowPage.rendered ctx user transferId)
            }
