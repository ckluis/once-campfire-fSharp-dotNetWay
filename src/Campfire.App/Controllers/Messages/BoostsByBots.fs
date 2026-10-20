// Port of rust/crates/campfire/src/controllers/messages/boosts/by_bots.rs
//
// `Messages::Boosts::ByBotsController` (reference/app/controllers/messages/boosts/by_bots_controller.rb):
// bots boost with the raw request body as the content.
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.Db
open Campfire.Ruby
open Campfire.Views
open Campfire.App.Web

module BoostsByBots =
    let private before: Before = Before.allowBotAccess Before.Default

    /// The room among `Current.user.rooms`, then its message; `head :not_found` without one.
    let private setMessage (http: HttpContext) : Result<Message, Error> =
        result {
            let! (user: User) = Current.requireUser http
            let roomId = match Params.value http "room_id" with null -> None | id -> Ruby.integerCast id
            let messageId = match Params.value http "message_id" with null -> None | id -> Ruby.integerCast id
            let! (room: Room option) = Data.read http (fun db -> roomId |> Option.bind (fun id -> Room.findForUser db user.Id id))
            let! (message: Message option) =
                match room, messageId with
                | Some room, Some id ->
                    Data.read http (fun db -> Message.findById db id |> Option.filter (fun message -> message.RoomId = room.Id))
                | _ -> Ok None
            match message with
            | Some message -> return message
            | None -> return! Action.halt (Before.head 404)
        }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                let! (message: Message) = setMessage http
                // ensure_content_present
                let content = Params.rawBody http
                if ByBots.isBlank content then do! Action.halt (Before.head 422)
                let! (boost: Boost) = BoostsController.createBoost http message (Some content)
                do! BoostsController.broadcastCreate http message boost

                // render :show, status: :created
                do! Formats.respondTo http [ Format.Json ] |> Result.map ignore
                let baseUrl = Respond.urlFor http ""
                let! (body: string) =
                    Data.present http (fun presenter -> MessagesJson.boostsByBotsShow (presenter.BoostJson(boost, message, baseUrl)))
                return Pages.renderText http 201 Campfire.Kit.Response.JsonUtf8 body
            }

    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                let! (message: Message) = setMessage http
                // set_boost, with `rescue ActiveRecord::RecordNotFound` -> head :not_found
                let boost =
                    match BoostsController.setBoost http message with
                    | Ok boost -> Ok(Some boost)
                    | Result.Error NotFound -> Ok None
                    | Result.Error e -> Result.Error e
                let! boost = boost
                match boost with
                | None -> return Before.head 404
                | Some boost ->
                    do! BoostsController.destroyBoost http message boost
                    return Respond.head http 204
            }
