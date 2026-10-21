// Port of rust/crates/campfire/src/controllers/messages/boosts.rs
//
// `Messages::BoostsController` (reference/app/controllers/messages/boosts_controller.rb). Its
// `show`/`edit`/`update` routes have no action or template (`action_not_found`).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby
open Campfire.Views
open Campfire.App.Web

module BoostsController =
    module IndexPage = Campfire.Views.Templates.Messages.Boosts.Index
    module NewPage = Campfire.Views.Templates.Messages.Boosts.New

    /// `Current.user.reachable_messages.find(params[:message_id])`
    let private setMessage (http: HttpContext) : Result<Message, Error> =
        result {
            let! (user: User) = Current.requireUser http
            match (match Params.value http "message_id" with null -> None | id -> Ruby.integerCast id) with
            | None -> return! Result.Error NotFound
            | Some id -> return! Data.read http (fun db -> Message.findReachable db user.Id id)
        }

    /// `@message.boosts.find_by!(id: params[:id], booster: Current.user)`
    let setBoost (http: HttpContext) (message: Message) : Result<Boost, Error> =
        result {
            let! (user: User) = Current.requireUser http
            match (match Params.value http "id" with null -> None | id -> Ruby.integerCast id) with
            | None -> return! Result.Error NotFound
            | Some id ->
                let messageId = message.Id
                return! Data.read http (fun db -> Boost.findByMessageAndBooster db messageId id user.Id)
        }

    /// `@message.boosts.create!(content:)`, boosted by `Current.user`.
    let createBoost (http: HttpContext) (message: Message) (content: string option) : System.Threading.Tasks.Task<Result<Boost, Error>> =
        taskResult {
            let! (user: User) = Current.requireUser http
            let messageId = message.Id
            // A nil content violates the column's NOT NULL (ActiveRecord::NotNullViolation, a 500).
            let! content =
                match content with
                | Some content -> Ok content
                | None -> Result.Error(Internal(exn "NOT NULL constraint failed: boosts.content"))
            return! Data.write http (fun tx -> Boost.create tx messageId user.Id content)
        }

    /// `@boost.destroy!` then `broadcast_remove`.
    let destroyBoost (http: HttpContext) (message: Message) (boost: Boost) : System.Threading.Tasks.Task<Result<unit, Error>> =
        taskResult {
            do! Data.write http (fun tx -> Boost.destroy tx boost)
            let roomId = message.RoomId
            let! (room: Room) = Data.read http (fun db -> Room.find db roomId)
            (Data.app http).Broadcasts.BoostRemove(room, boost)
        }

    /// `broadcast_create`: `messages/boosts/_boost` appended to the message's boosts.
    let broadcastCreate (http: HttpContext) (message: Message) (boost: Boost) : System.Threading.Tasks.Task<Result<unit, Error>> =
        let app = Data.app http
        Data.readAsync http (fun db ->
            task {
                let presenter = Presenter(db, app, None)
                let view = presenter.Boost boost
                let! html = BoostsCached.boost app.Fragments (Page.fragmentContext app) view
                let room = Room.find db message.RoomId
                let partials = { Rendered.empty with Boost = Some html }
                app.Broadcasts.BoostCreate(room, message, boost, Rendered.partials partials)
            })

    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (message: Message) = setMessage http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let app = Data.app http
                let! view, boosts =
                    Data.presentAsync http (fun presenter ->
                        task {
                            let view = presenter.Message message
                            let! boosts = BoostsCached.boosts app.Fragments (Page.fragmentContext app) view.Boosts
                            return view, boosts
                        })
                return! Pages.page http 200 (fun ctx -> Layouts.ViewPage.content [ IndexPage.render ctx view boosts ] boosts)
            }

    let ``new``: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (message: Message) = setMessage http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let! (current: User) = Current.requireUser http
                let user = PresenterSupport.userView (Data.app http).Secrets current
                let! view = Data.present http (fun presenter -> presenter.Message message)
                return! Pages.page http 200 (fun ctx -> Layouts.ViewPage.content [ NewPage.render ctx view user ] [])
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (message: Message) = setMessage http
                // params.require(:boost).permit(:content)
                let! boost = Params.require http "boost"
                let content =
                    match boost.Get "content" with
                    | Str content -> Some content
                    | _ -> None
                let! (boost: Boost) = createBoost http message content
                do! broadcastCreate http message boost
                return! Respond.redirectTo http (Respond.urlFor http (Routes.messageBoosts message.Id))
            }

    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (message: Message) = setMessage http
                let! (boost: Boost) = setBoost http message
                do! destroyBoost http message boost
                // No destroy template: `head :no_content`.
                return Respond.head http 204
            }
