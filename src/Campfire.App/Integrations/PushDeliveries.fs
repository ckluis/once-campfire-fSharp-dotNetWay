// Port of rust/crates/campfire/src/integrations/web_push/pool.rs
//
// `WebPush::Pool` (reference/lib/web_push/pool.rb) with the invalid-subscription handler from
// reference/config/initializers/web_push.rb, as a hosted service over two channels: deliveries (up to 50 at once, 10,000
// waiting, the rest dropped like `Concurrent::RejectedExecutionError` and logged) and invalidations (one reader that
// destroys expired or unusable subscriptions in order).
namespace Campfire.App.Integrations

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Campfire.Db
open Campfire.RichText

/// `Concurrent::ThreadPoolExecutor.new(max_threads: 50, max_queue: 10000)`
module internal PoolLimits =
    [<Literal>]
    let MaxThreads = 50

    [<Literal>]
    let MaxQueue = 10000

[<Sealed>]
type PushDeliveries(net: Network, vapid: VapidConfig, invalidSubscriptionHandler: int64 -> Task<Result<unit, string>>, logger: ILogger) =
    inherit BackgroundService()

    let deliveries =
        let options = BoundedChannelOptions(PoolLimits.MaxQueue)
        options.FullMode <- BoundedChannelFullMode.Wait
        Channel.CreateBounded<Notification> options
    // `FixedThreadPool.new(1)`
    let invalidations =
        let options = UnboundedChannelOptions()
        options.SingleReader <- true
        Channel.CreateUnbounded<int64> options
    let mutable running = 0
    /// Deliveries dropped since the queue was last accepting them.
    let mutable dropped = 0
    let mutable shutDown = false
    let mutable deliveryReaders: Task = Task.CompletedTask

    member private _.Deliver(notification: Notification) : Task =
        task {
            match! WebPush.deliver net vapid notification with
            | Ok _ -> ()
            | Error error when DeliveryError.invalidatesSubscription error -> invalidations.Writer.TryWrite notification.Subscription.Id |> ignore
            | Error error -> logger.LogError("Error in WebPush::Pool.deliver: {Class} {Error}", DeliveryError.className error, DeliveryError.message error)
        }

    /// One of the 50 readers: delivers one notification at a time until the queue has closed and drained, or the host stops.
    member private this.ReadDeliveries(stopping: CancellationToken) : Task =
        task {
            let reader = deliveries.Reader
            try
                let mutable more = true
                while more do
                    let! available = reader.WaitToReadAsync stopping
                    if not available then
                        more <- false
                    else
                        let mutable item = Unchecked.defaultof<Notification>
                        while reader.TryRead(&item) do
                            Interlocked.Increment(&running) |> ignore
                            try
                                try
                                    do! this.Deliver item
                                with e ->
                                    logger.LogError("WebPush::Pool delivery panicked: {Panic}", e.Message)
                            finally
                                Interlocked.Decrement(&running) |> ignore
            with :? OperationCanceledException ->
                ()
        }

    /// The invalidation worker: destroys each subscription in order.
    member private _.ReadInvalidations() : Task =
        task {
            let mutable more = true
            while more do
                let! available = invalidations.Reader.WaitToReadAsync()
                if not available then
                    more <- false
                else
                    let mutable id = 0L
                    while invalidations.Reader.TryRead(&id) do
                        logger.LogInformation("Destroying push subscription: {Id}", id)
                        try
                            match! invalidSubscriptionHandler id with
                            | Ok() -> ()
                            | Error message -> logger.LogError("Error in WebPush::Pool.invalid_subscription_handler: {Message}", message)
                        with e ->
                            logger.LogError("Error in WebPush::Pool.invalid_subscription_handler: {Panic}", e.Message)
        }

    override this.ExecuteAsync(stoppingToken: CancellationToken) : Task =
        let readers = [ for _ in 1 .. PoolLimits.MaxThreads -> Task.Run(fun () -> this.ReadDeliveries stoppingToken) ]
        deliveryReaders <- Task.WhenAll readers
        Task.WhenAll(deliveryReaders, Task.Run(fun () -> this.ReadInvalidations()))

    member this.DeliverLater(notification: Notification) : unit =
        if Volatile.Read(&shutDown) then
            logger.LogWarning "WebPush::Pool is shut down, dropping a notification"
        elif not (deliveries.Writer.TryWrite notification) then
            if Interlocked.Increment(&dropped) = 1 then logger.LogError "WebPush::Pool is full, dropping notifications"
        else
            match Interlocked.Exchange(&dropped, 0) with
            | 0 -> ()
            | count -> logger.LogError("WebPush::Pool dropped {Count} notifications while it was full", count)

    /// `queue(payload, subscriptions)`: in id order (`find_each`), each subscription's notification is built here
    /// (counting its badge) and delivered on the pool.
    member this.Queue(db: Ef.CampfireDb, payload: PushPayload, subscriptions: Ef.PushSubscription list) : unit =
        for subscription in subscriptions |> List.sortBy (fun s -> s.Id) do
            this.DeliverLater(WebPush.Notification.build db subscription payload)

    member private _.StopBase(token: CancellationToken) : Task = base.StopAsync token

    /// Waits (up to a second, like `wait_for_termination(1)`) for queued deliveries, then closes the invalidations once the
    /// deliveries are done (so a 410 that comes back is still destroyed) and waits for them within the host's grace.
    override this.StopAsync(token: CancellationToken) : Task =
        task {
            Volatile.Write(&shutDown, true)
            deliveries.Writer.TryComplete() |> ignore
            let! _ = Task.WhenAny(deliveryReaders, Task.Delay(TimeSpan.FromSeconds 1.0, net.Time))
            invalidations.Writer.TryComplete() |> ignore
            do! this.StopBase token
        }

    member _.Vapid: VapidConfig = vapid

    /// Queued or running deliveries.
    member _.Pending: int = deliveries.Reader.Count + Volatile.Read(&running)

    /// Deliveries running now (at most 50).
    member _.Running: int = Volatile.Read(&running)

module PushDeliveries =
    /// `Room::PushMessageJob#perform` / `Room::MessagePusher#push`: the payload goes to the subscriptions of
    /// everyone involved in everything, then to mentioned users involved in mentions. Badges are counted here;
    /// delivery happens on the readers.
    let pushMessage (pool: PushDeliveries) (db: Ef.CampfireDb) (richText: Campfire.Db.RichText) (message: Message) (now: DateTime) : PushPayload =
        let payload, everything, mentions = PushSubscription.pushesFor db richText message now
        pool.Queue(db, payload, everything)
        pool.Queue(db, payload, mentions)
        payload
