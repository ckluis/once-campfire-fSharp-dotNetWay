// Port of rust/crates/campfire/src/jobs/tests.rs
module Campfire.App.Tests.JobsTests

open System
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Xunit
open Campfire.App
open Campfire.App.Tests.Support
open Campfire.Db

/// A handler that reports each event it performs, after `gate` lets it through (when given).
let private reporting (performed: ChannelWriter<Event>) (gate: SemaphoreSlim option) : JobHandler<AppState> =
    fun _app event ->
        task {
            match gate with
            | Some gate -> do! gate.WaitAsync()
            | None -> ()
            performed.TryWrite event |> ignore
            return Ok()
        }

let private nextPerformed (performed: ChannelReader<Event>) : Task<Event> =
    task {
        use timeout = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)
        return! performed.ReadAsync(timeout.Token).AsTask()
    }

let private webhook (messageId: int64) : Event = Event.DeliverWebhook(1L, messageId)

/// A job runner started as the host starts it.
let private started (jobs: Jobs) (app: AppState) (registry: Registry<AppState>) (concurrency: int) : Task<JobRunner<AppState>> =
    task {
        let runner = new JobRunner<AppState>(jobs, app, registry, concurrency)
        do! (runner :> IHostedService).StartAsync CancellationToken.None
        return runner
    }

/// What the host does at stop: the queues close, what is queued drains, and the host's grace bounds the wait.
let private stopped (runner: JobRunner<AppState>) : Task =
    task {
        use grace = new CancellationTokenSource(TimeSpan.FromSeconds 5.0)
        do! (runner :> IHostedService).StopAsync grace.Token
        runner.Dispose()
    }

[<Fact>]
let ``JB1 a job of each kind runs once on its kind's worker`` () =
    task {
        use! test = bootEmpty ()
        use logs = new CapturingLoggers()
        let seen = Channel.CreateUnbounded<string>()
        let record (name: string) : JobHandler<AppState> =
            fun _app _event ->
                task {
                    seen.Writer.TryWrite name |> ignore
                    return Ok()
                }
        let registry = Registry<AppState>()
        registry.Handle(JobKind.PushMessage, record "push")
        registry.Handle(JobKind.DeliverWebhook, record "webhook")
        registry.Handle(JobKind.RemoveBannedContent, record "remove")
        registry.Handle(JobKind.PurgeBlob, record "purge")
        let jobs = Jobs(JobLimits.QueueCapacity, logs.Logger "jobs")
        let! runner = started jobs test.App registry 1
        let sink = jobs :> EventSink
        sink.Emit(Event.PushMessage(1L, 2L))
        sink.Emit(Event.DeliverWebhook(1L, 2L))
        sink.Emit(Event.RemoveBannedContent 3L)
        sink.Emit(Event.PurgeBlob 4L)
        jobs.PerformLater("Ad hoc", fun () -> task { seen.Writer.TryWrite "adhoc" |> ignore; return Ok() })
        do! stopped runner
        let performed =
            [ let mutable item = Unchecked.defaultof<string>
              while seen.Reader.TryRead(&item) do
                  item ]
            |> List.sort
        Assert.Equal<string list>([ "adhoc"; "purge"; "push"; "remove"; "webhook" ], performed)
    }

[<Fact>]
let ``JB2 JB3 a busy or full kind doesnt hold up the others`` () =
    task {
        use! test = bootEmpty ()
        use logs = new CapturingLoggers()
        let performed = Channel.CreateUnbounded<Event>()
        let slowBot = new SemaphoreSlim(0)
        let registry = Registry<AppState>()
        registry.Handle(JobKind.DeliverWebhook, reporting performed.Writer (Some slowBot))
        registry.Handle(JobKind.PurgeBlob, reporting performed.Writer None)
        let jobs = Jobs(2, logs.Logger "jobs")
        let! runner = started jobs test.App registry 1
        let sink = jobs :> EventSink

        // One webhook runs (stuck on the slow bot), two wait, and the rest are dropped.
        sink.Emit(webhook 1L)
        while jobs.Queued JobKind.DeliverWebhook > 0 do
            do! Task.Yield()
        for messageId in 2L .. 10L do
            sink.Emit(webhook messageId)
        sink.Emit(Event.PurgeBlob 7L)
        let! purged = nextPerformed performed.Reader
        Assert.Equal(Event.PurgeBlob 7L, purged)
        Assert.Contains("job queue is full, dropping job job=Bot::WebhookJob", logs.Text)
        Assert.Contains(logs.Collector.GetSnapshot(), fun record -> record.Level = LogLevel.Error && record.Message.StartsWith "job queue is full")

        for _ in 1..3 do
            slowBot.Release() |> ignore
            match! nextPerformed performed.Reader with
            | Event.DeliverWebhook _ -> ()
            | other -> failwith $"{other}"
        do! stopped runner
        Assert.False(performed.Reader.TryRead() |> fst)
    }

[<Fact>]
let ``JB4 a panicking job is logged and its worker carries on`` () =
    task {
        use! test = bootEmpty ()
        use logs = new CapturingLoggers()
        let jobs = Jobs(JobLimits.QueueCapacity, logs.Logger "jobs")
        let! runner = started jobs test.App (Registry<AppState>()) 1

        let finished = TaskCompletionSource()
        jobs.PerformLater("Exploding", fun () -> failwith "kaboom")
        jobs.PerformLater("After", fun () -> task { finished.SetResult(); return Ok() })
        let! first = Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds 5.0))
        Assert.True(obj.ReferenceEquals(first, finished.Task), "the worker carried on")
        do! stopped runner

        Assert.Contains("job panicked job=Exploding panic=kaboom", logs.Text)
    }

[<Fact>]
let ``a failed job is logged with its cause`` () =
    task {
        use! test = bootEmpty ()
        use logs = new CapturingLoggers()
        let jobs = Jobs(JobLimits.QueueCapacity, logs.Logger "jobs")
        let! runner = started jobs test.App (Registry<AppState>()) 1

        let finished = TaskCompletionSource()
        jobs.PerformLater("Failing", fun () -> Task.FromResult(Error "purging the blob: disk full"))
        jobs.PerformLater("After", fun () -> task { finished.SetResult(); return Ok() })
        let! first = Task.WhenAny(finished.Task, Task.Delay(TimeSpan.FromSeconds 5.0))
        Assert.True(obj.ReferenceEquals(first, finished.Task), "the worker carried on")
        do! stopped runner

        Assert.Contains("job failed job=Failing error=purging the blob: disk full", logs.Text)
    }

[<Fact>]
let ``JB5 shutdown performs what is queued and then takes no more`` () =
    task {
        use! test = bootEmpty ()
        use logs = new CapturingLoggers()
        let performed = Channel.CreateUnbounded<Event>()
        let registry = Registry<AppState>()
        registry.Handle(JobKind.DeliverWebhook, reporting performed.Writer None)
        let jobs = Jobs(JobLimits.QueueCapacity, logs.Logger "jobs")
        let! runner = started jobs test.App registry 2
        let sink = jobs :> EventSink

        for messageId in 1L .. 5L do
            sink.Emit(webhook messageId)
        do! stopped runner
        sink.Emit(webhook 6L)

        let ids =
            [ let mutable event = Unchecked.defaultof<Event>
              while performed.Reader.TryRead(&event) do
                  match event with
                  | Event.DeliverWebhook(_, messageId) -> messageId
                  | other -> failwith $"{other}" ]
            |> List.sort
        Assert.Equal<int64 list>([ 1L; 2L; 3L; 4L; 5L ], ids)
        Assert.Contains("job runner stopped, dropping job", logs.Text)
    }
