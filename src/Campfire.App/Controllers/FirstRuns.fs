// Port of rust/crates/campfire/src/controllers/first_runs.rs
//
// `FirstRunsController` (reference/app/controllers/first_runs_controller.rb): set up the account and its first
// administrator. Web actions (unit 2.9).
namespace Campfire.App.Controllers

open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Views
open Campfire.App.Web

module FirstRuns =
    module ShowPage = Campfire.Views.Templates.FirstRuns.Show

    /// `redirect_to root_url if Account.any?`
    let private preventRepeats (http: HttpContext) : Result<unit, Error> =
        match Data.read http (fun db -> Account.count db > 0L) with
        | Result.Error e -> Result.Error e
        | Ok false -> Ok()
        | Ok true ->
            match Respond.redirectTo http (Respond.urlFor http (Routes.root ())) with
            | Ok handler -> Action.halt handler
            | Result.Error e -> Result.Error e

    /// `allow_unauthenticated_access`, `before_action :prevent_repeats`
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                do! preventRepeats http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                return! Pages.page http 200 ShowPage.rendered
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                do! preventRepeats http

                // params.require(:user).permit(:name, :avatar, :email_address, :password)
                let! required = Params.require http "user"
                let user = Params.permit required [ "name"; "email_address"; "password" ]
                let name = Map.tryFind "name" user
                let emailAddress = defaultArg (Map.tryFind "email_address" user) ""
                let password = defaultArg (Map.tryFind "password" user) ""
                let avatar, _ = Uploads.assignment http required "avatar"
                // users.name is NOT NULL: Rails raises ActiveRecord::NotNullViolation.
                let! name =
                    match name with
                    | Some name -> Ok name
                    | None -> Result.Error(Internal(exn "NOT NULL constraint failed: users.name"))
                let app = Data.app http
                let! avatar = Assignment.stage app avatar
                let! passwordDigest =
                    task {
                        try
                            let! digest = PasswordDigest.hash password app.Env.BcryptCost
                            return Ok digest
                        with e ->
                            return Result.Error(Internal e)
                    }

                let! result =
                    Data.tryWrite http (fun tx ->
                        let administrator = FirstRun.create tx name emailAddress passwordDigest
                        administrator, AttachmentWrites.assign tx (Record.user administrator.Id) "avatar" avatar)
                    |> Task.map Ok

                let root = Respond.urlFor http (Routes.root ())
                match result with
                | Ok(administrator, pending) ->
                    AttachmentWrites.analyzeLater app pending
                    let! (_: Session) = Authentication.startNewSessionFor http administrator
                    return! Respond.redirectTo http root
                // rescue ActiveRecord::RecordNotUnique
                | Result.Error error when DbError.isRecordNotUnique error -> return! Respond.redirectTo http root
                | Result.Error error -> return! Result.Error(Internal(System.Exception(DbError.display error)))
            }
