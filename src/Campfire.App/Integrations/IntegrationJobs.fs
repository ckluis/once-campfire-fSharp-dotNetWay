// Port of rust/crates/campfire/src/integrations/jobs.rs
//
// The jobs integrations perform for the app's job runner: `Room::PushMessageJob`
// (reference/app/jobs/room/push_message_job.rb) and `Bot::WebhookJob` (reference/app/jobs/bot/webhook_job.rb),
// including what `Webhook#deliver` does with a reply (create the bot's message, process an attachment,
// `broadcast_create`).
namespace Campfire.App.Integrations

open System
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.Logging
open Campfire.App
open Campfire.App.Controllers
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Views

module IntegrationJobs =
    let private failure (error: DbError) : string = DbError.display error

    let private kitFailure (error: Campfire.App.Web.Error) : string = sprintf "%A" error

    /// `Reads.run` on a context leased for the call (an integration has no request), the failure left as a `DbError`.
    let private readEf (app: AppState) (f: Ef.CampfireDb -> 'T) : Result<'T, DbError> =
        use context = app.DbFactory.CreateDbContext()
        Ef.Reads.run context f

    /// `readEf` for a read that awaits (a message's fragment is fetched while the context is in use).
    let private readEfAsync (app: AppState) (f: Ef.CampfireDb -> Task<'T>) : Task<Result<'T, DbError>> =
        task {
            use context = app.DbFactory.CreateDbContext()
            return! Ef.Reads.runAsync context f
        }

    /// `Writes.run` on a context leased for the write, the failure left as a `DbError`.
    let private writeEf (app: AppState) (f: Ef.Tx -> 'T) : Task<Result<'T, DbError>> =
        task {
            use context = app.DbFactory.CreateDbContext()
            return! Ef.Writes.run app.Gate app.Env context f
        }

    /// `Room::PushMessageJob#perform(room, message)`: `Room::MessagePusher.new(room:, message:).push`, unless Web
    /// Push is off.
    let private pushMessage (app: AppState) (event: Event) : Task<Result<unit, string>> =
        task {
            match event, app.WebPush with
            | Event.PushMessage(_, messageId), Some pool ->
                // It counts a badge for, and queues a notification to, every subscriber; the job already runs on a worker.
                match
                    readEf app (fun db ->
                        let message = Message.find db messageId
                        PushDeliveries.pushMessage pool db app.Env.RichText message (RailsTime.now app.Env.Time) |> ignore)
                with
                | Ok() -> return Ok()
                | Error error -> return Error(failure error)
            | _ -> return Ok()
        }

    /// The VAPID key pair, or `None` (logged) when Web Push is off: missing or invalid keys.
    let vapidOrOff (config: AppConfig) (logger: ILogger) : VapidConfig option =
        match VapidConfig.FromConfig config with
        | Error Missing ->
            logger.LogWarning("Web Push is off: {Reason}", VapidError.message Missing)
            None
        | Error error ->
            logger.LogError("Web Push is off: {Reason}", VapidError.message error)
            None
        | Ok vapid -> Some vapid

    /// The deliveries service, whose invalid subscription handler destroys the subscription
    /// (`Push::Subscription.find_by(id:)&.destroy`).
    let pushDeliveries
        (vapid: VapidConfig)
        (factory: IDbContextFactory<Ef.CampfireDb>)
        (gate: WriteGate)
        (env: Ef.Env)
        (push: PushEnv)
        (logger: ILogger)
        : PushDeliveries =
        let invalidSubscriptionHandler (id: int64) : Task<Result<unit, string>> =
            task {
                use context = factory.CreateDbContext()
                match! Ef.Writes.run gate env context (fun tx -> PushSubscription.destroy tx (PushSubscription.find tx.Db id)) with
                | Ok()
                | Error(RecordNotFound _) -> return Ok()
                | Error error -> return Error(failure error)
            }
        new PushDeliveries(push, vapid, invalidSubscriptionHandler, logger)

    /// config/initializers/web_push.rb (`config.x.web_push_pool`): `None`, and Web Push is off, when the VAPID keys are missing
    /// or invalid.
    let webPushPool
        (config: AppConfig)
        (factory: IDbContextFactory<Ef.CampfireDb>)
        (gate: WriteGate)
        (env: Ef.Env)
        (push: PushEnv)
        (logger: ILogger)
        : PushDeliveries option =
        vapidOrOff config logger |> Option.map (fun vapid -> pushDeliveries vapid factory gate env push logger)

    /// `room.messages.create!(body: text, creator: user)`: the text is assigned to the rich text body, which stores
    /// it canonicalized (as `MessagesController` does; there's no request host in a job).
    let private createTextReply (app: AppState) (room: Room) (bot: User) (text: string) : Task<Result<Message, string>> =
        task {
            match! MessageStaging.canonicalizeBody app text None with
            | Error error -> return Error(kitFailure error)
            | Ok body ->
                let newMessage: NewMessage =
                    { RoomId = room.Id
                      CreatorId = bot.Id
                      ClientMessageId = None
                      Body = Some body
                      AttachmentBlobId = None }
                match! writeEf app (fun tx -> Message.create tx newMessage) with
                | Ok message -> return Ok message
                | Error error -> return Error(failure error)
        }

    /// `ActiveStorage::Blob.create_and_upload!` (its own save), then
    /// `room.messages.create_with_attachment!(attachment:, creator: user)`, which processes the attachment.
    let private createAttachmentReply (app: AppState) (room: Room) (bot: User) (attachment: WebhookAttachment) : Task<Result<Message, string>> =
        task {
            let! staged = Task.Run(fun () -> WebhookClient.stageBlob app.Storage attachment)
            match staged with
            | Error error -> return Error(sprintf "%A" error)
            | Ok staged ->
                let newMessage (blob: Campfire.Storage.Blob) : NewMessage =
                    { RoomId = room.Id
                      CreatorId = bot.Id
                      ClientMessageId = None
                      Body = None
                      AttachmentBlobId = Some blob.Id }
                // The blob, its attachment and the message are one write.
                match!
                    writeEf app (fun tx ->
                        let blob = MessageStaging.saveStaged tx staged
                        Message.create tx (newMessage blob), blob)
                with
                | Error error -> return Error(failure error)
                | Ok(message, blob) ->
                    match! MessageStaging.processAttachment app blob with
                    | Error error -> return Error(kitFailure error)
                    | Ok() ->
                        match readEf app (fun db -> Message.find db message.Id) with
                        | Ok message -> return Ok message
                        | Error error -> return Error(failure error)
        }

    /// `message.broadcast_create`, rendered without a request (`ApplicationController.renderer`).
    let private broadcastCreate (app: AppState) (room: Room) (message: Message) : Task<Result<unit, string>> =
        task {
            let! result =
                readEfAsync app (fun db ->
                    task {
                        let presenter = Presenter(db, app, None)
                        let! html = presenter.MessageFragment message
                        let partials = { Rendered.empty with Message = Some html }
                        app.Broadcasts.MessageCreate(db, room, message, Rendered.partials partials)
                    })
            return result |> Result.mapError failure
        }

    /// `Bot::WebhookJob#perform(bot, message)`: `bot.deliver_webhook(message)`, i.e. `webhook.deliver(message)`, then
    /// the reply.
    let private deliverWebhook (app: AppState) (event: Event) : Task<Result<unit, string>> =
        task {
            match event with
            | Event.DeliverWebhook(botId, messageId) ->
                match
                    readEf app (fun db ->
                        let bot = User.find db botId
                        let message = Message.find db messageId
                        let room = Room.find db message.RoomId
                        match User.webhook db bot with
                        | None -> None
                        | Some webhook ->
                            let payload =
                                Webhook.payload
                                    db
                                    app.Env.RichText
                                    webhook
                                    message
                                    (Routes.roomBotMessages room.Id (User.botKey bot))
                                    (Routes.roomAtMessage room.Id message.Id)
                            Some(bot, room, Option.ofObj webhook.Url, payload))
                with
                | Error error -> return Error(failure error)
                | Ok None -> return Error "undefined method 'deliver' for nil (the bot has no webhook)"
                | Ok(Some(bot, room, url, payload)) ->
                    match! WebhookClient.deliver app.Http app.Time (defaultArg url "") payload with
                    // J11: a bad URL's error quotes the URL, which often holds the secret in its path; the failure line logs its keyed hash.
                    | Error error ->
                        return Error(Logging.redactUrl (Logging.forSecrets app.Secrets) (defaultArg url "") (WebhookError.message error))
                    | Ok delivery ->
                        let! message =
                            task {
                                match delivery.Reply with
                                | ReplyNone -> return Ok None
                                | ReplyText text ->
                                    let! created = createTextReply app room bot text
                                    return created |> Result.map Some
                                | ReplyAttachment attachment ->
                                    let! created = createAttachmentReply app room bot attachment
                                    return created |> Result.map Some
                            }
                        match message with
                        | Error error -> return Error error
                        | Ok None -> return Ok()
                        | Ok(Some message) -> return! broadcastCreate app room message
            | _ -> return Ok()
        }

    /// Registers the handlers for `Event.PushMessage` and `Event.DeliverWebhook`.
    let registerJobs (registry: Registry<AppState>) : unit =
        registry.Handle(JobKind.PushMessage, pushMessage)
        registry.Handle(JobKind.DeliverWebhook, deliverWebhook)
