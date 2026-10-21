// Port of rust/crates/campfire/src/controllers/accounts/custom_styles.rs
//
// `Accounts::CustomStylesController` (reference/app/controllers/accounts/custom_styles_controller.rb). Web actions (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.Views
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.App.Web

module CustomStyles =
    module EditPage = Campfire.Views.Templates.Accounts.CustomStyles.Edit


    /// `before_action :ensure_can_administer, :set_account`
    let edit: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (account: Account) = AccountsController.currentAccount http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let customStyles = Option.ofObj account.CustomStyles
                return!
                    Pages.page http 200 (fun ctx -> EditPage.rendered ctx customStyles)
            }

    /// `@account.update!(params.require(:account).permit(:custom_styles))`
    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (account: Account) = AccountsController.currentAccount http
                let! required = Params.require http "account"
                let parameters = Params.permit required [ "custom_styles" ]
                let customStyles = Map.tryFind "custom_styles" parameters |> Option.map Some
                let! (_: Account) = Data.write http (fun tx -> Account.update tx account None customStyles None)
                return!
                    Respond.redirectToWith
                        http
                        (Respond.urlFor http (Routes.editAccountCustomStyles ()))
                        { RedirectOptions.Default with Notice = "✓" }
            }
