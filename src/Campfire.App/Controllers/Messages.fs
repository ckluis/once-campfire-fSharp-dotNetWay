// Port of rust/crates/campfire/src/controllers/messages.rs
//
// `MessagesController` (reference/app/controllers/messages_controller.rb), including the multipart
// attachment upload the composer's `FileUploader` posts here, plus what `Messages::ByBotsController`
// reuses: creating a message (with `process_attachment`), delivering webhooks and the broadcasts.
namespace Campfire.App.Controllers

open System
open System.Threading.Tasks
open Campfire.App
open Campfire.App.Channels
open Campfire.Db
open Campfire.App.Web
open Campfire.RichText
open Campfire.Routes
open Campfire.Ruby
open Campfire.Storage
open Campfire.Views
open Campfire.App.Presenters

/// What `create_with_attachment!`/`update!` receive.
type MessageParams =
    { Body: string option
      /// `attachment=`: `None` when the key wasn't given.
      Attachment: Assignment<Upload> option
      ClientMessageId: string option }

/// The request-free pieces of writing a message: the canonical body, a staged blob's row, and `process_attachment`. They report the Web
/// `Error` (`IntegrationJobs` calls them from jobs).
module MessageStaging =
    open FsToolkit.ErrorHandling

    /// Assigning something that isn't an upload, a signed blob id, nil or "".
    let invalidAttachment () : Error = Internal(exn "Could not find or build blob: expected attachable")

    /// Inserts a staged blob's row, keeping its file once the transaction commits.
    let saveStaged (tx: Ef.Tx) (staged: Staged) : Campfire.Storage.Blob =
        let blob = staged.Insert tx.Db
        ActiveStorage.keepAfterCommit tx staged
        blob

    /// Assigning a String to a rich text attribute stores the canonicalized content
    /// (`ActionText::Content.new(body, canonicalize: true).to_html`).
    let canonicalBody (db: Ef.CampfireDb) (app: AppState) (body: string) (requestHost: string option) : string =
        let resolver = DbResolver(db, app.Secrets, app.Time.GetUtcNow())
        let ctx = resolver.RenderContext requestHost
        match Content.load body ctx with
        | Ok content -> Content.toHtml content
        | Error _ -> body

    /// `canonicalBody` on a leased context, ahead of the write that stores it.
    let canonicalizeBody (app: AppState) (body: string) (requestHost: string option) : Task<Result<string, Error>> =
        app.ReadEf(fun db -> canonicalBody db app body requestHost)

    /// `blob.analyze`: its `after_update` touches the attached records (a message also touches its room). The file is
    /// analyzed off the writer; the metadata and the touches are one write.
    let analyzeAttachment (app: AppState) (blob: Campfire.Storage.Blob) : Task<Result<Campfire.Storage.Blob, Error>> =
        taskResult {
            let! metadata = ActiveStorage.analyzedMetadata app blob
            return!
                app.WriteEf(fun tx ->
                    let blob = Campfire.Storage.Blob.updateMetadata tx.Db metadata blob
                    for (recordType, recordId) in Campfire.Storage.Blob.attachmentRecords tx.Db blob.Id do
                        Common.touchRecord tx recordType recordId
                    blob)
        }

    /// `Message#process_attachment`: analyze the blob now (its `after_update` touches the message),
    /// then generate the video preview or the `:thumb` representation.
    let processAttachment (app: AppState) (blob: Campfire.Storage.Blob) : Task<Result<unit, Error>> =
        taskResult {
            let! blob = analyzeAttachment app blob
            if Campfire.Storage.Blob.isVideo blob then
                // attachment.preview(format: :webp).processed
                let! (_: Campfire.Storage.Blob) = ActiveStorage.processedPreview app blob (Variation.formatOnly "webp")
                return ()
            elif Campfire.Storage.Blob.isRepresentable blob then
                // attachment.representation(:thumb).processed
                let thumb = Variation.resizeToLimit 1200L 800L None
                let! (_: Campfire.Storage.Blob) = ActiveStorage.processedRepresentation app blob thumb
                return ()
            else
                return ()
        }

    let private stageUpload (app: AppState) (upload: Upload) : Task<Result<Staged option, Error>> =
        task {
            match! Upload.stage app upload with
            | Ok staged -> return Ok(Some staged)
            | Error e -> return Error e
        }


module MessagesController =
    open FsToolkit.ErrorHandling
    open Microsoft.AspNetCore.Http
    open Campfire.App.Web

    module IndexPage = Campfire.Views.Templates.Messages.Index
    module ShowPage = Campfire.Views.Templates.Messages.Show
    module EditPage = Campfire.Views.Templates.Messages.Edit
    module CreateStream = Campfire.Views.Templates.Messages.CreateTurboStream
    module DestroyStream = Campfire.Views.Templates.Messages.DestroyTurboStream
    module RoomNotFoundPage = Campfire.Views.Templates.Messages.RoomNotFound
    module PresentationPartial = Campfire.Views.Templates.Messages._Presentation

    /// Stands in for the digest `ETagWithTemplateDigest` adds for `messages/index` (only the ETag's
    /// shape has to match the reference).
    [<Literal>]
    let private TemplateDigestIndex = "messages/index"

    let MessageParamsNone: MessageParams =
        { Body = None
          Attachment = None
          ClientMessageId = None }

    // --- Before-actions and params ---------------------------------------------------------------

    /// `@room.messages.find(params[:id])`
    let setMessage (http: HttpContext) (room: Room) : Result<Message, Error> =
        match (match Params.value http "id" with null -> None | id -> Ruby.integerCast id) with
        | None -> Result.Error NotFound
        | Some id ->
            let roomId = room.Id
            Data.read http (fun db -> Message.findInRoom db roomId id)

    /// `head :forbidden unless Current.user.can_administer?(@message)`
    let ensureCanAdminister (http: HttpContext) (message: Message) : Result<unit, Error> =
        match Current.requireUser http with
        | Result.Error e -> Result.Error e
        | Ok user ->
            if User.canAdminister user (Some message.CreatorId) false then
                Ok()
            else
                Action.halt (Before.head 403)

    /// A permitted string attribute as `Param#as_str` reads it.
    let private text (scope: Scope) (key: string) : string option =
        match scope.Get key with
        | Str value -> Some value
        | _ -> None

    /// What assigning the permitted `attachment` does: an upload replaces the attachment, nil or ""
    /// removes it (`Attached::Changes::DeleteOne`), anything else raises.
    let private attachmentAssignment (assignment: Assignment<Upload>, _: bool) : Assignment<Upload> option =
        match assignment with
        | Unchanged -> None
        | assignment -> Some assignment

    /// `params.require(:message).permit(:body, :attachment, :client_message_id)`
    let messageParams (http: HttpContext) : Result<MessageParams, Error> =
        Params.require http "message"
        |> Result.map (fun message ->
            { Body = text message "body"
              Attachment = attachmentAssignment (Uploads.assignment http message "attachment")
              ClientMessageId = text message "client_message_id" })

    /// `@room.messages.find(params[:before])` and friends (`find_paged_messages`).
    let findPagedMessages (http: HttpContext) (room: Room) : Result<Message list, Error> =
        let present (key: string) : int64 option option =
            match Params.lookup http key with
            | Str value when Params.isPresent (Str value) -> Some(Ruby.integerCast value)
            | Structured -> Some None
            | _ -> None
        let before, after = present "before", present "after"
        let roomId = room.Id
        let findInRoom (id: int64 option) (db: Ef.CampfireDb) =
            match id with
            | Some id -> Message.findInRoom db roomId id
            | None -> Err.fail (RecordNotFound "Message")
        Data.read http (fun db ->
            match before, after with
            | Some before, _ -> Message.pageBefore db roomId (findInRoom before db)
            | None, Some after -> Message.pageAfter db roomId (findInRoom after db)
            | None, None -> Message.lastPage db roomId)

    // --- Creating, updating, destroying -----------------------------------------------------------

    let private canonicalize (http: HttpContext) (body: string option) : System.Threading.Tasks.Task<Result<string option, Error>> =
        taskResult {
            match body with
            | Some body ->
                let! canonical = (MessageStaging.canonicalizeBody (Data.app http) body (Some http.Request.Host.Host))
                return Some canonical
            | None -> return None
        }

    /// `@room.messages.create_with_attachment!(attributes)`: the message (with its uploaded blob, in
    /// one transaction), then `process_attachment`. The upload's file is copied into storage and the
    /// body canonicalized before the transaction, so the writer only inserts rows.
    let createMessage (http: HttpContext) (room: Room) (attributes: MessageParams) : System.Threading.Tasks.Task<Result<Message, Error>> =
        taskResult {
            let! (user: User) = Current.requireUser http
            let app = Data.app http
            let roomId = room.Id
            let! (attachment: Staged option) =
                taskResult {
                    match attributes.Attachment with
                    | Some assignment ->
                        match! Uploads.stage http assignment with
                        | Create staged -> return Some staged
                        | Invalid -> return! Result.Error(MessageStaging.invalidAttachment ())
                        | _ -> return None
                    | None -> return None
                }
            let! (body: string option) = canonicalize http attributes.Body
            // The blob, the attachment and the message are one write.
            let! (message: Message, blob: Campfire.Storage.Blob option) =
                Data.write http (fun tx ->
                    let blob = attachment |> Option.map (MessageStaging.saveStaged tx)
                    let message =
                        Message.create
                            tx
                            { RoomId = roomId
                              CreatorId = user.Id
                              ClientMessageId = attributes.ClientMessageId
                              Body = body
                              AttachmentBlobId = blob |> Option.map (fun blob -> blob.Id) }
                    message, blob)
            // Without an attachment, `message` is the row as stored: nothing after the commit writes to it,
            // and Rails answers with the same record (`create!(attributes).tap(&:process_attachment)`,
            // reference/app/models/message/attachment.rb). Analyzing an attachment touches the message, so
            // then it's read back.
            match blob with
            | None -> return message
            | Some blob ->
                let id = message.Id
                do! (MessageStaging.processAttachment app blob)
                return! Data.read http (fun db -> Message.find db id)
        }

    /// `@message.update!(message_params)`. A new attachment replaces the old one (whose blob is purged
    /// later) without `process_attachment`: the blob is only analyzed, by `ActiveStorage::AnalyzeJob`
    /// after commit (verified against the reference with a bot's `PUT` and `attachment`).
    let updateMessage (http: HttpContext) (message: Message) (attributes: MessageParams) : System.Threading.Tasks.Task<Result<Message, Error>> =
        taskResult {
            let app = Data.app http
            // None: no change; Some None: remove; Some(Some staged): replace.
            let! (attachment: Staged option option) =
                taskResult {
                    match attributes.Attachment with
                    | Some assignment ->
                        match! Uploads.stage http assignment with
                        | Invalid -> return! Result.Error(MessageStaging.invalidAttachment ())
                        | Create staged -> return Some(Some staged)
                        | _ -> return Some None
                    | None -> return None
                }
            let! (body: string option) = canonicalize http attributes.Body
            let attachmentGiven = attachment.IsSome
            let! (id: int64, blob: Campfire.Storage.Blob option) =
                Data.write http (fun tx ->
                    let blob = attachment |> Option.flatten |> Option.map (MessageStaging.saveStaged tx)
                    let mutable message = message
                    match body with
                    | Some body -> message <- Message.updateBody tx message body
                    | None -> ()
                    if attachmentGiven then
                        message <- Message.replaceAttachment tx message (blob |> Option.map (fun blob -> blob.Id))
                    message.Id, blob)
            match blob |> Option.filter (fun blob -> not (Campfire.Storage.Blob.isAnalyzed blob)) with
            | Some blob ->
                app.Jobs.PerformLater(
                    "ActiveStorage::AnalyzeJob",
                    fun () ->
                        task {
                            match! MessageStaging.analyzeAttachment app blob with
                            | Ok _ -> return Ok()
                            | Error e -> return Error(sprintf "%A" e)
                        }
                )
            | None -> ()
            return! Data.read http (fun db -> Message.find db id)
        }

    /// `@message.destroy` then `@message.broadcast_remove`.
    let destroyMessage (http: HttpContext) (room: Room) (message: Message) : System.Threading.Tasks.Task<Result<unit, Error>> =
        taskResult {
            do! Data.write http (fun tx -> Message.destroy tx message)
            (Data.app http).Broadcasts.MessageRemove(room, message)
        }

    // --- Broadcasts and webhooks -------------------------------------------------------------------

    /// `@message.broadcast_create`: the message partial appended to the room, then the unread pings.
    let broadcastCreate (http: HttpContext) (room: Room) (message: Message) : Task<Result<unit, Error>> =
        let app = Data.app http
        Data.readAsync http (fun db ->
            task {
                let presenter = Presenter(db, app, None)
                let! html = presenter.MessageFragment message
                let partials = { Rendered.empty with Message = Some html }
                app.Broadcasts.MessageCreate(db, room, message, Rendered.partials partials)
            })

    /// `broadcast_replace_to @room, :messages, target: [ @message, :presentation ], partial:
    /// "messages/presentation", attributes: { maintain_scroll: true }`
    let broadcastReplace (http: HttpContext) (room: Room) (message: Message) : Result<unit, Error> =
        let app = Data.app http
        let baseUrl = Pages.rendererBaseUrl http
        Data.read http (fun db ->
            let presenter = Presenter(db, app, None)
            let view = presenter.Message message
            let account = Account.first db
            let html =
                Page.renderDetachedAt app account baseUrl (fun ctx -> (HtmlOutput.ofNode (PresentationPartial.render ctx view) []).ToString())
            let partials = { Rendered.empty with MessagePresentation = Some html }
            app.Broadcasts.MessageReplace(room, message, Rendered.partials partials))

    /// `deliver_webhooks_to_bots`: every active bot in a direct room, else every mentioned active
    /// bot, except the message's creator.
    let deliverWebhooksToBots (http: HttpContext) (room: Room) (message: Message) : System.Threading.Tasks.Task<Result<unit, Error>> =
        taskResult {
            let app = Data.app http
            let! (bots: User list) =
                Data.read http (fun db ->
                    let candidates =
                        if Room.isDirect room then Room.activeBots db room else Message.mentionees db app.Env.RichText message
                    candidates
                    |> List.filter (fun (user: User) -> user.Role = Role.Bot && user.Status = Campfire.Db.Status.Active && user.Id <> message.CreatorId))
            if not bots.IsEmpty then
                // bot.deliver_webhook_later(@message)
                let messageId = message.Id
                do! Data.write http (fun tx -> bots |> List.iter (fun bot -> User.deliverWebhookLater tx bot messageId))
        }

    // --- Rendering ---------------------------------------------------------------------------------

    /// `render action: :room_not_found` (inside the layout).
    let private renderRoomNotFound (http: HttpContext) : Result<Falco.HttpHandler, Error> =
        result {
            do! Formats.respondTo http [ Web.Format.Html ] |> Result.map ignore
            return! Pages.pageInApplicationLayout http 200 (fun _ -> Layouts.ViewPage.content [ RoomNotFoundPage.render ] [])
        }

    // --- Actions -----------------------------------------------------------------------------------

    /// `index` (`layout false`): the page before/after a message, or the last page; 204 when empty.
    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (_: Membership), (room: Room) = Before.setRoom http
                let! (messages: Message list) = findPagedMessages http room
                if messages.IsEmpty then
                    return Respond.head http 204
                else
                    // fresh_when @messages: the records' cache keys, their latest updated_at, and the template.
                    let etag =
                        messages
                        |> List.map (fun m -> Fragments.cacheKeyWithVersion "messages" m.Id (DateTimeOffset m.UpdatedAt))
                        |> String.concat "/"
                    let freshness =
                        { Freshness.Default with
                            Etag = etag
                            LastModified = ValueSome(DateTimeOffset(messages |> List.map (fun m -> m.UpdatedAt) |> List.max))
                            Template = TemplateDigestIndex }
                    match Respond.freshWhen http freshness with
                    | ValueSome notModified -> return notModified
                    | ValueNone ->
                        do! Formats.respondTo http [ Web.Format.Html ] |> Result.map ignore
                        let! views = Data.presentAsync http (fun presenter -> presenter.Messages messages)
                        return! Pages.bare http 200 Web.Format.Html (fun _ -> IndexPage.output views)
            }

    /// `create`: `set_room` runs inside the action, and a room that's gone renders `room_not_found`.
    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! room =
                    match Before.setRoom http with
                    | Ok(_, room) -> Ok(Some room)
                    | Result.Error NotFound -> Ok None
                    | Result.Error e -> Result.Error e
                match room with
                | None -> return! renderRoomNotFound http
                | Some room ->
                    let! attributes = messageParams http
                    let! (message: Message) = createMessage http room attributes
                    do! broadcastCreate http room message
                    do! deliverWebhooksToBots http room message

                    // The message partial comes out of the fragment cache `broadcastCreate` just filled
                    // (`cache [ message, "presentation-v3" ]`), so it's the request-less rendering: no CSRF
                    // tokens in its forms.
                    do! Formats.respondTo http [ Web.Format.TurboStream ] |> Result.map ignore
                    let kind = PresenterSupport.roomKind room.Type
                    let app = Data.app http
                    let! html =
                        Data.readAsync http (fun db ->
                            task {
                                let presenter = Presenter(db, app, None)
                                let! item = presenter.MessageItem message
                                return CreateStream.output item kind
                            })
                    return Pages.renderOutput http 200 Pages.TurboStreamUtf8 html
            }

    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (_: Membership), (room: Room) = Before.setRoom http
                let! (message: Message) = setMessage http room
                do! Formats.respondTo http [ Web.Format.Html ] |> Result.map ignore
                let! html = Data.presentAsync http (fun presenter -> task {
                    let! html = presenter.MessageFragment message
                    return html
                })
                return! Pages.pageInApplicationLayout http 200 (fun _ -> Layouts.ViewPage.content [ ShowPage.render html ] [ html ])
            }

    let edit: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (_: Membership), (room: Room) = Before.setRoom http
                let! (message: Message) = setMessage http room
                do! ensureCanAdminister http message
                do! Formats.respondTo http [ Web.Format.Html ] |> Result.map ignore
                let! edit =
                    Data.present http (fun presenter ->
                        let editableBodyHtml = presenter.EditableBody message
                        ({ EditableBodyHtml = editableBodyHtml
                           Message = presenter.Message message }: Campfire.Views.Messages.EditView))
                return! Pages.pageInApplicationLayout http 200 (fun ctx -> Layouts.ViewPage.content [ EditPage.render ctx edit ] [])
            }

    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (_: Membership), (room: Room) = Before.setRoom http
                let! (message: Message) = setMessage http room
                do! ensureCanAdminister http message
                let! attributes = messageParams http
                let! (message: Message) = updateMessage http message attributes
                do! broadcastReplace http room message

                // respond_to html: redirect; json: `render :show`, which has no JSON template here.
                let! format = Formats.respondTo http [ Web.Format.Html; Web.Format.Json ]
                if format = Web.Format.Json then
                    return! Result.Error(Internal(exn "Missing template messages/show"))
                else
                    return! Respond.redirectTo http (Respond.urlFor http (Routes.roomMessage room.Id message.Id))
            }

    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (_: Membership), (room: Room) = Before.setRoom http
                let! (message: Message) = setMessage http room
                do! ensureCanAdminister http message
                do! destroyMessage http room message

                do! Formats.respondTo http [ Web.Format.TurboStream ] |> Result.map ignore
                let! view = Data.present http (fun presenter -> presenter.Message message)
                return! Pages.bare http 200 Web.Format.TurboStream (fun _ -> DestroyStream.output view)
            }
