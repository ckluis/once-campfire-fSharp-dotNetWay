// Port of rust/crates/campfire/src/controllers/accounts/bots.rs
//
// `Accounts::BotsController` (reference/app/controllers/accounts/bots_controller.rb). Web actions (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.Views
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Rails.Routes
open Campfire.Rails.Ruby
open Campfire.App.Web

module Bots =
    module IndexPage = Campfire.Views.Templates.Accounts.Bots.Index
    module NewPage = Campfire.Views.Templates.Accounts.Bots.New
    module EditPage = Campfire.Views.Templates.Accounts.Bots.Edit


    /// ApplicationController's chain, then `ensure_can_administer`.
    let private before (http: HttpContext) =
        taskResult {
            do! Before.chain Before.Default http
            do! Before.ensureCanAdminister http
        }

    let findActiveBot (http: HttpContext) (key: string) : Result<User, Error> =
        match (match Params.value http key with null -> None | id -> Ruby.integerCast id) with
        | None -> Result.Error NotFound
        | Some id -> Data.read http (fun db -> User.findActiveBot db id)

    /// `User.active_bots.find(params[:id])`
    let private setBot (http: HttpContext) : Result<User, Error> = findActiveBot http "id"

    /// `params.require(:user).permit(:name, :avatar, :webhook_url)`: the hash, and its scalar members.
    let private botParams (http: HttpContext) : Result<Scope * Map<string, string>, Error> =
        Params.require http "user" |> Result.map (fun user -> user, Params.permit user [ "name"; "webhook_url" ])

    let private redirectToBots (http: HttpContext) : Result<Falco.HttpHandler, Error> =
        Respond.redirectTo http (Respond.urlFor http (Routes.accountBots ()))

    /// `@bots = User.active_bots.ordered`
    let index: Web.Action =
        fun http ->
            taskResult {
                do! before http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let secrets = (Data.app http).Secrets
                let! users = Data.read http User.activeBotsOrdered
                let! bots = Data.read http (fun db -> users |> List.map (Accounts.bot db secrets))
                return!
                    Pages.page http 200 (fun ctx -> IndexPage.rendered ctx bots)
            }

    let ``new``: Web.Action =
        fun http ->
            taskResult {
                do! before http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let bot = Campfire.Views.Accounts.BotForm.empty
                return!
                    Pages.page http 200 (fun ctx -> NewPage.rendered ctx bot)
            }

    /// `User.create_bot! bot_params`
    let create: Web.Action =
        fun http ->
            taskResult {
                do! before http
                let! user, parameters = botParams http
                // users.name is NOT NULL.
                let! name =
                    match Map.tryFind "name" parameters with
                    | Some name -> Ok name
                    | None -> Result.Error(Internal(exn "NOT NULL constraint failed: users.name"))
                // `create_webhook!(url: webhook_url) if webhook_url`: any non-nil value, "" included.
                let webhookUrl = Map.tryFind "webhook_url" parameters
                let avatar, _ = Uploads.assignment http user "avatar"
                let! avatar = Uploads.stage http avatar
                let! pending =
                    Data.write http (fun tx ->
                        let bot = User.createBot tx name webhookUrl
                        AttachmentWrites.assign tx (Record.user bot.Id) "avatar" avatar)
                AttachmentWrites.analyzeLater (Data.app http) pending
                return! redirectToBots http
            }

    let edit: Web.Action =
        fun http ->
            taskResult {
                do! before http
                let! (bot: User) = setBot http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let storage = (Data.app http).Storage
                let baseUrl = Respond.urlFor http ""
                let botId = bot.Id
                let! webhookUrl = Data.read http (fun db -> User.webhookUrl db bot)
                let! form = Data.read http (fun db -> Accounts.botForm db storage baseUrl bot webhookUrl)
                return!
                    Pages.page http 200 (fun ctx -> EditPage.rendered ctx botId form)
            }

    /// `@bot.update_bot! bot_params`: the webhook first, then the bot, in one transaction.
    let update: Web.Action =
        fun http ->
            taskResult {
                do! before http
                let! (bot: User) = setBot http
                let! user, parameters = botParams http
                let changes = { UserChanges.none with Name = Map.tryFind "name" parameters }
                let webhookUrl = Map.tryFind "webhook_url" parameters
                let avatar, _ = Uploads.assignment http user "avatar"
                let! avatar = Uploads.stage http avatar
                let! pending =
                    Data.write http (fun tx ->
                        User.updateBot tx bot changes webhookUrl |> ignore
                        AttachmentWrites.assign tx (Record.user bot.Id) "avatar" avatar)
                AttachmentWrites.analyzeLater (Data.app http) pending
                return! redirectToBots http
            }

    /// `@bot.deactivate`
    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! before http
                let! (bot: User) = setBot http
                let! (_: User) = Data.write http (fun tx -> User.deactivate tx bot)
                return! redirectToBots http
            }
