// Port of rust/crates/campfire/src/controllers/users/bans.rs
//
// `Users::BansController` (reference/app/controllers/users/bans_controller.rb). Web actions (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.Db
open Campfire.Routes
open Campfire.App.Web

module Bans =
    /// `redirect_to @user`
    let private redirectToUser (http: HttpContext) (id: int64) : Result<Falco.HttpHandler, Error> =
        Respond.redirectTo http (Respond.urlFor http (Routes.user id))

    /// `before_action :ensure_can_administer, :set_user`; `@user.ban`
    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (user: User) = UsersController.findUser http "user_id"
                let! (_: User) = Data.write http (fun tx -> User.ban tx user)
                return! redirectToUser http user.Id
            }

    /// `@user.unban`
    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (user: User) = UsersController.findUser http "user_id"
                let! (_: User) = Data.write http (fun tx -> User.unban tx user)
                return! redirectToUser http user.Id
            }
