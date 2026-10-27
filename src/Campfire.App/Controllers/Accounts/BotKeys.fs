// Port of rust/crates/campfire/src/controllers/accounts/bots/keys.rs
//
// `Accounts::Bots::KeysController` (reference/app/controllers/accounts/bots/keys_controller.rb). A Web action (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.Db
open Campfire.Rails.Routes
open Campfire.App.Web

module BotKeys =
    /// `User.active_bots.find(params[:bot_id]).reset_bot_key`
    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! bot = Bots.findActiveBot http "bot_id"
                let! (_: User) = Data.write http (fun tx -> User.resetBotKey tx bot)
                return! Respond.redirectTo http (Respond.urlFor http (Routes.accountBots ()))
            }
