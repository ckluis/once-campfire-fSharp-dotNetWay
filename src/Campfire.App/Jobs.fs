// Port of rust/crates/campfire/src/jobs.rs
//
// The in-process job runner that replaces Resque (see plans/rust-conversion.md, "Jobs").
//
// Models emit `Event`s at the point Rails would `perform_later` (the database's `EventSink`); `Jobs`
// puts each on its kind's **bounded** queue without blocking the writer thread, and each kind has its
// own workers, so a kind that's slow (webhooks to a slow bot) or full can't hold up the others (pushes,
// purges). Nothing retries (`retry_on` is commented out in `reference/app/jobs/application_job.rb`); a
// failure or exception is logged. Queued work is lost if the process crashes, which the plan accepts. On
// shutdown the runner stops taking new work, performs what's queued and waits for it up to the host's grace
// (`HostOptions.ShutdownTimeout`).
//
// Handlers are looked up in a `Registry`. Core registers `RemoveBannedContent` and `PurgeBlob`
// (`CoreJobs`, which needs the app); integrations register `PushMessage` and `DeliverWebhook`.
// `DisconnectUser` is not a job in Rails (it's a synchronous Action Cable broadcast), so it goes
// straight to the cable server.
//
// Rust's handlers are `async` functions returning `anyhow::Result<()>`; here a handler returns a
// `Task<Result<unit, string>>`, where the `Error` is the failure's message with its causes, as
// `{error:#}` prints it. An exception a handler raises is logged as a panic is.
namespace Campfire.App

open System
open System.Collections.Generic
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Campfire.Db
open Campfire.App.Channels

/// How many jobs of each kind may wait before new ones of that kind are dropped (and logged).
module JobLimits =
    [<Literal>]
    let QueueCapacity = 1024

type JobKind =
    | PushMessage
    | DeliverWebhook
    | RemoveBannedContent
    | PurgeBlob
    /// Work enqueued with `Jobs.PerformLater`.
    | AdHoc

module JobKind =
    let all: JobKind list = [ PushMessage; DeliverWebhook; RemoveBannedContent; PurgeBlob; AdHoc ]

    /// An event's case name and nothing of its fields (a log line must not print a message body: J11).
    let eventName (event: Event) : string =
        let case, _ = Microsoft.FSharp.Reflection.FSharpValue.GetUnionFields(event, typeof<Event>)
        case.Name

    /// `None` for an event that isn't a job.
    let ofEvent (event: Event) : JobKind option =
        match event with
        | Event.PushMessage _ -> Some PushMessage
        | Event.DeliverWebhook _ -> Some DeliverWebhook
        | Event.RemoveBannedContent _ -> Some RemoveBannedContent
        | Event.PurgeBlob _ -> Some PurgeBlob
        | Event.DisconnectUser _ -> None

    /// The Rails job class, for logs.
    let name (kind: JobKind) : string =
        match kind with
        | PushMessage -> "Room::PushMessageJob"
        | DeliverWebhook -> "Bot::WebhookJob"
        | RemoveBannedContent -> "RemoveBannedContentJob"
        | PurgeBlob -> "ActiveStorage::PurgeJob"
        | AdHoc -> "AdHoc"

/// Performs one kind of job.
type JobHandler<'App> = 'App -> Event -> Task<Result<unit, string>>

/// Which handler performs which kind of job.
[<Sealed>]
type Registry<'App>() =
    let handlers = Dictionary<JobKind, JobHandler<'App>>()

    member _.Handle(kind: JobKind, handler: JobHandler<'App>) : unit = handlers[kind] <- handler

    member _.TryGet(kind: JobKind) : JobHandler<'App> voption =
        match handlers.TryGetValue kind with
        | true, handler -> ValueSome handler
        | _ -> ValueNone

type internal Work =
    | EventWork of JobKind * Event
    | AdHocWork of name: string * run: (unit -> Task<Result<unit, string>>)

module internal Work =
    let kind (work: Work) : JobKind =
        match work with
        | EventWork(kind, _) -> kind
        | AdHocWork _ -> AdHoc

    let name (work: Work) : string =
        match work with
        | EventWork(kind, _) -> JobKind.name kind
        | AdHocWork(name, _) -> name

/// The enqueueing side: the database's event sink, and `PerformLater` for everything else.
[<Sealed>]
type Jobs(capacity: int, logger: ILogger, ?cable: CableHandle) =
    let queues =
        JobKind.all
        |> List.map (fun kind ->
            let options = BoundedChannelOptions(max capacity 1)
            options.FullMode <- BoundedChannelFullMode.Wait
            options.SingleWriter <- false
            options.SingleReader <- false
            kind, Channel.CreateBounded<Work> options)
        |> dict
    let mutable stopped = false

    member internal _.Queue(kind: JobKind) : Channel<Work> = queues[kind]

    member internal _.Queued(kind: JobKind) : int = queues[kind].Reader.Count

    member internal _.Logger = logger

    /// Closes every queue to new work (the runner's `StopAsync`): what is already queued stays for the
    /// workers to drain.
    member internal _.Close() : unit =
        stopped <- true
        for queue in queues.Values do
            queue.Writer.TryComplete() |> ignore

    member internal _.Enqueue(work: Work) : unit =
        let name = Work.name work
        if queues[Work.kind work].Writer.TryWrite work then
            if logger.IsEnabled LogLevel.Debug then logger.LogDebug("enqueued job={Job}", name)
        elif stopped then
            logger.LogWarning("job runner stopped, dropping job job={Job}", name)
        else
            logger.LogError("job queue is full, dropping job job={Job}", name)

    /// Enqueues best-effort work (`SomeJob.perform_later`). Dropped with an error log when its queue
    /// is full. `work` runs on a worker, once the job's turn comes.
    member this.PerformLater(name: string, work: unit -> Task<Result<unit, string>>) : unit = this.Enqueue(AdHocWork(name, work))

    interface EventSink with
        member this.Emit(event: Event) : unit =
            match JobKind.ofEvent event, event with
            | Some kind, event -> this.Enqueue(EventWork(kind, event))
            // `ActionCable.server.remote_connections.where(current_user: user).disconnect`: a pub/sub
            // broadcast in Rails, done right away. Before the host has started there are no connections to
            // disconnect.
            | None, Event.DisconnectUser(userId, reconnect) ->
                match cable |> Option.bind (fun handle -> handle.TryResolve<Cable>() |> ValueOption.toOption) with
                | None -> ()
                | Some cable -> Revocation.disconnectUser cable userId reconnect |> ignore
            | None, event -> logger.LogWarning("not a job, dropping event {Event}", JobKind.eventName event)

module internal JobWorkers =
    /// `Error` carrying the exception's message and its causes, as `{error:#}` prints them.
    let private chain (error: exn) : string =
        let messages = ResizeArray<string>()
        let mutable current: exn | null = error
        while not (isNull current) do
            let e = nonNull current
            messages.Add e.Message
            current <- e.InnerException
        String.Join(": ", messages)

    let private perform (app: 'App) (registry: Registry<'App>) (logger: ILogger) (work: Work) : Task =
        task {
            let name = Work.name work
            try
                let! result =
                    match work with
                    | AdHocWork(_, run) -> run ()
                    | EventWork(kind, event) ->
                        match registry.TryGet kind with
                        | ValueSome handler -> handler app event
                        | ValueNone -> Task.FromResult(Error $"no handler registered for {JobKind.eventName event}")
                match result with
                | Ok() -> logger.LogInformation("performed job={Job}", name)
                | Error error -> logger.LogError("job failed job={Job} error={Error}", name, error)
            with e ->
                logger.LogError("job panicked job={Job} panic={Panic}", name, chain e)
        }

    /// One of a kind's workers: performs that kind's jobs, one at a time, until its queue has closed
    /// and drained.
    let work (queue: Channel<Work>) (app: 'App) (registry: Registry<'App>) (logger: ILogger) : Task =
        task {
            let reader = queue.Reader
            try
                while true do
                    // `ReadAsync` wakes one worker per job; it throws once the queue is closed and drained.
                    let! item = reader.ReadAsync()
                    do! perform app registry logger item
            with :? ChannelClosedException ->
                ()
        }

/// Performs queued jobs: `concurrency` workers for each kind of job. `StopAsync` stops taking new work, performs what is
/// already queued, and waits for running jobs until the host's grace ends, after which they are abandoned (logged). The
/// workers read without the stopping token on purpose: what is queued drains first.
[<Sealed>]
type JobRunner<'App>(jobs: Jobs, app: 'App, registry: Registry<'App>, concurrency: int) =
    inherit BackgroundService()

    override _.ExecuteAsync(_stoppingToken: CancellationToken) : Task =
        let workers =
            [ for kind in JobKind.all do
                  for _ in 1 .. max concurrency 1 do
                      // Each worker runs on the pool; the queue hands every job to exactly one.
                      Task.Run(fun () -> JobWorkers.work (jobs.Queue kind) app registry jobs.Logger) ]
        Task.WhenAll workers

    member private _.StopBase(token: CancellationToken) : Task = base.StopAsync token

    override this.StopAsync(token: CancellationToken) : Task =
        task {
            jobs.Close()
            do! this.StopBase token
            match this.ExecuteTask with
            | null -> ()
            | running -> if not running.IsCompleted then jobs.Logger.LogWarning "jobs still running at shutdown were abandoned"
        }
