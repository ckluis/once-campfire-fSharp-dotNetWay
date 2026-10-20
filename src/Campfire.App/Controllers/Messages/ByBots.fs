// Port of rust/crates/campfire/src/controllers/messages/by_bots.rs
//
// `Messages::ByBotsController` (reference/app/controllers/messages/by_bots_controller.rb): the
// bot API under `/rooms/:room_id/:bot_key/messages` (JSON by route default). Bodies are the raw
// request body (`RawRequestBody`), or a top-level multipart `attachment`.
namespace Campfire.App.Controllers

open System
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby
open Campfire.Views
open Campfire.App.Web

module ByBots =
    let private before: Before = Before.allowBotAccess Before.Default

    /// `set_room`: `Current.user.rooms.find_by(id: params[:room_id])`, else `head :not_found`.
    let private setRoom (http: HttpContext) : Result<Room, Error> =
        result {
            let! (user: User) = Current.requireUser http
            let! room =
                match (match Params.value http "room_id" with null -> None | id -> Ruby.integerCast id) with
                | Some id -> Data.read http (fun db -> Room.findForUser db user.Id id)
                | None -> Ok None
            match room with
            | Some room -> return room
            | None -> return! Action.halt (Before.head 404)
        }

    /// `String#blank?`: empty or only whitespace.
    let isBlank (value: string) : bool = value |> Seq.forall Char.IsWhiteSpace

    /// `params[:attachment]`: an uploaded file, else the value the key holds.
    let private attachmentParam (http: HttpContext) : bool * ParamValue =
        let file = if http.Request.HasFormContentType then http.Request.Form.Files.GetFile "attachment" else null
        not (isNull file), Params.lookup http "attachment"

    /// `head :unprocessable_content if params[:attachment].blank? && raw_request_body.blank?`
    let private ensureBodyOrAttachmentPresent (http: HttpContext) : Result<unit, Error> =
        let isFile, value = attachmentParam http
        let attachmentBlank =
            not isFile
            && (match value with
                | Absent -> true
                | Str value -> isBlank value
                | Structured -> false)
        if attachmentBlank && isBlank (Params.rawBody http) then
            Action.halt (Before.head 422)
        else
            Ok()

    /// `params[:attachment] ? params.permit(:attachment) : { body: raw_request_body }`
    let private messageParams (http: HttpContext) : MessageParams =
        let isFile, value = attachmentParam http
        if isFile || value <> Absent then
            let assignment, _ = Uploads.assignmentNamed http "attachment" value
            { MessagesController.MessageParamsNone with
                Attachment = (match assignment with Unchanged -> None | assignment -> Some assignment) }
        else
            { MessagesController.MessageParamsNone with Body = Some(Params.rawBody http) }

    /// `X-Total-Count`, and a `Link` to the next page when there is one.
    let private setPaginationHeaders (http: HttpContext) (room: Room) (messages: Message list) : Result<unit, Error> =
        result {
            let after = Params.isPresent (Params.lookup http "after")
            let roomId = room.Id
            let first, last = List.tryHead messages, List.tryLast messages
            let! count, nextPage =
                Data.read http (fun db ->
                    let count = Message.countInRoom db roomId
                    let nextPage =
                        match first, last with
                        | Some _, Some last when after ->
                            if Message.existsAfter db roomId last then Some("after", last.Id) else None
                        | Some first, Some _ -> if Message.existsBefore db roomId first then Some("before", first.Id) else None
                        | _ -> None
                    count, nextPage)
            http.Response.Headers["x-total-count"] <- string count
            match nextPage with
            | Some(key, id) ->
                let botKey =
                    match Params.value http "bot_key" with
                    | null -> ""
                    | key -> key
                let url = Respond.urlFor http $"{Routes.roomBotMessages roomId botKey}?{key}={id}"
                http.Response.Headers["link"] <- $"<{url}>; rel=\"next\""
            | None -> ()
        }

    /// `render :show` (`messages/by_bots/show.json.jbuilder`).
    let private renderShow (http: HttpContext) (message: Message) : Result<Falco.HttpHandler, Error> =
        result {
            let baseUrl = Respond.urlFor http ""
            let! (body: string) =
                Data.present http (fun presenter -> MessagesJson.byBotsShow (presenter.MessageJson(message, baseUrl)))
            return Pages.renderText http 200 Campfire.Kit.Response.JsonUtf8 body
        }

    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                let! (room: Room) = setRoom http
                let! (messages: Message list) = MessagesController.findPagedMessages http room
                do! setPaginationHeaders http room messages
                do! Formats.respondTo http [ Format.Json ] |> Result.map ignore
                let baseUrl = Respond.urlFor http ""
                let! (body: string) =
                    Data.present http (fun presenter ->
                        let jsons = messages |> List.map (fun m -> presenter.MessageJson(m, baseUrl))
                        MessagesJson.byBotsIndex jsons)
                return Pages.renderText http 200 Campfire.Kit.Response.JsonUtf8 body
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                let! (room: Room) = setRoom http
                do! ensureBodyOrAttachmentPresent http
                // MessagesController#create
                let attributes = messageParams http
                let! (message: Message) = MessagesController.createMessage http room attributes
                do! MessagesController.broadcastCreate http room message
                do! MessagesController.deliverWebhooksToBots http room message

                return! Respond.headWithLocation http 201 (Respond.urlFor http (Routes.message message.Id))
            }

    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                let! (room: Room) = setRoom http
                let! (message: Message) = MessagesController.setMessage http room
                do! MessagesController.ensureCanAdminister http message
                // MessagesController#update
                let attributes = messageParams http
                let! (message: Message) = MessagesController.updateMessage http message attributes
                do! MessagesController.broadcastReplace http room message
                let! format = Formats.respondTo http [ Format.Html; Format.Json ]
                if format = Format.Json then
                    return! renderShow http message
                else
                    return! Respond.redirectTo http (Respond.urlFor http (Routes.roomMessage room.Id message.Id))
            }

    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                let! (room: Room) = setRoom http
                let! (message: Message) = MessagesController.setMessage http room
                do! MessagesController.ensureCanAdminister http message
                do! MessagesController.destroyMessage http room message
                return Respond.head http 204
            }
