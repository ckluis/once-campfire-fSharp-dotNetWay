// Port of rust/crates/campfire/src/controllers/accounts/users.rs
//
// `Accounts::UsersController` (reference/app/controllers/accounts/users_controller.rb): the people list's next pages, role
// changes and removal. Web actions (unit 2.10).
namespace Campfire.App.Controllers

open System
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.Views
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Rails.Routes
open Campfire.Rails.Ruby
open Campfire.App.Web

module AccountUsers =
    /// `set_page_and_extract_portion_from User.active.ordered.without_bots, per_page: 500`,
    /// rendered as `index.turbo_stream.erb` (the only template, so other formats are 406).
    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Formats.respondTo http [ Format.TurboStream ] |> Result.map ignore
                let! (users: User list) = Data.read http User.activeOrderedWithoutBots
                let page = Pagination.Page.Create(Params.value http "page", int64 users.Length, [ 500L ])
                let secrets = (Data.app http).Secrets
                let users = page.Records users |> List.map (Common.userSummary secrets)
                let nextPage = if page.IsLast then None else Some(string page.NextParam)

                let! layout = Pages.load http
                let html =
                    Pages.render layout http (fun ctx -> Campfire.Views.Templates.Accounts.Users.IndexTurboStream.output ctx users nextPage)
                Pages.applyPageHeaders http page
                return Pages.renderOutput http 200 Pages.TurboStreamUtf8 html
            }

    /// `User.active.find(params[:user_id] || params[:id])`
    let private setUser (http: HttpContext) : Result<User, Error> =
        let id =
            match Params.value http "user_id" with
            | null -> Params.value http "id"
            | id -> id
        match (match id with null -> None | id -> Ruby.integerCast id) with
        | None -> Result.Error NotFound
        | Some id -> Data.read http (fun db -> User.findActive db id)

    let private redirectToEditAccount (http: HttpContext) : Result<Falco.HttpHandler, Error> =
        Respond.redirectTo http (Respond.urlFor http (Routes.editAccount ()))

    /// `@user.update(role: params.require(:user)[:role].presence_in(%w[ member administrator ]) || "member")`
    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (user: User) = setUser http
                let! required = Params.require http "user"
                let role =
                    match required.Get "role" with
                    | Str "administrator" -> Role.Administrator
                    | _ -> Role.Member
                let! (_: User) = Data.write http (fun tx -> User.update tx user { UserChanges.none with Role = Some role })
                return! redirectToEditAccount http
            }

    /// `@user.deactivate`
    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (user: User) = setUser http
                let! (_: User) = Data.write http (fun tx -> User.deactivate tx user)
                return! redirectToEditAccount http
            }
