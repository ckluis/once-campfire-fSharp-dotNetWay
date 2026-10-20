// Port of rust/crates/campfire/src/controllers/accounts.rs
//
// `AccountsController` (reference/app/controllers/accounts_controller.rb): account settings. Web actions (unit 2.10). Its
// nested controllers (`Controllers/Accounts/`) are separate modules.
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.Views
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.App.Web

module AccountsController =
    module EditPage = Campfire.Views.Templates.Accounts.Edit

    let private editSize = RenderSize()

    /// `set_page_and_extract_portion_from users, per_page: 500`
    let private PerPage = [ 500L ]

    /// `Current.account` where the reference dereferences it (a nil account raises NoMethodError).
    let currentAccount (http: HttpContext) : Result<Account, Error> =
        match Data.read http (fun db -> Account.first db) with
        | Result.Error e -> Result.Error e
        | Ok(Some account) -> Ok account
        | Ok None -> Result.Error(Internal(exn "no account"))

    /// Everyone, administrators first; the page only decides whether a next-page loader follows.
    let edit: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (account: Account) = currentAccount http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let canAdminister = Current.user http |> Option.exists (fun user -> User.canAdminister user None false)
                let! (users: User list) = Data.read http (fun db -> Accounts.accountUsers db canAdminister)
                let page = Pagination.Page.Create(Params.value http "page", int64 users.Length, PerPage)

                let secrets = (Data.app http).Secrets
                let administrators, members =
                    users |> List.map (Common.userSummary secrets) |> List.partition (fun user -> user.Administrator)
                let edit: Campfire.Views.Accounts.EditView =
                    { AccountId = account.Id
                      JoinCode = account.JoinCode
                      RestrictRoomCreationToAdministrators =
                        AccountSettings.restrictRoomCreationToAdministrators (Account.settings account)
                      Administrators = administrators
                      Members = members
                      NextPage = if page.IsLast then None else Some(string page.NextParam) }
                return!
                    Pages.framedPage
                        http
                        200
                        editSize
                        (fun w ctx -> EditPage.render w ctx edit)
                        EditPage.head
                        (fun w ctx -> EditPage.content w ctx edit)
            }

    /// `@account.update!(params.require(:account).permit(:name, :logo, settings: {}))`
    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (account: Account) = currentAccount http

                let! required = Params.require http "account"
                let parameters = Params.permit required [ "name" ]
                let name = Map.tryFind "name" parameters
                let settings = Params.hashMembers http "account" "settings"
                let logo, _ = Uploads.assignment http required "logo"
                let! logo = Uploads.stage http logo

                let! pending =
                    Data.write http (fun tx ->
                        Account.update tx account name None settings |> ignore
                        AttachmentWrites.assign tx (Record.account account.Id) "logo" logo)
                AttachmentWrites.analyzeLater (Data.app http) pending

                return! Respond.redirectToWith http (Respond.urlFor http (Routes.editAccount ())) { RedirectOptions.Default with Notice = "✓" }
            }
