// Port of rust/crates/campfire/src/controllers/accounts/join_codes.rs
//
// `Accounts::JoinCodesController` (reference/app/controllers/accounts/join_codes_controller.rb). A Web action (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.Db
open Campfire.Rails.Routes
open Campfire.App.Web

module JoinCodes =
    /// `Current.account.reset_join_code`
    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (account: Account) = AccountsController.currentAccount http
                let! (_: Account) = Data.write http (fun tx -> Account.resetJoinCode tx account)
                return! Respond.redirectTo http (Respond.urlFor http (Routes.editAccount ()))
            }
