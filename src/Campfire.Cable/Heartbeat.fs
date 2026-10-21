// Phase 3 (plans/design-cable.md C8, C11): the heartbeat and the shutdown hook, as hosted services.
namespace Campfire.Cable

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

/// One timer for every connection: a `ping` frame each `BeatInterval`, from boot (Rails starts its timer on the first connection).
[<Sealed>]
type CableHeartbeat<'U>(server: CableServer<'U>) =
    inherit BackgroundService()

    override _.ExecuteAsync(stoppingToken: CancellationToken) : Task =
        task {
            use timer = new PeriodicTimer(server.Options.BeatInterval, server.Time)
            let mutable running = true
            while running do
                let! ticked = timer.WaitForNextTickAsync stoppingToken
                if not ticked then
                    running <- false
                else
                    // A failed beat must not end the service: a faulted `BackgroundService` stops the host by default.
                    try
                        server.Beat()
                    with e ->
                        server.Logger.LogError(e, "Cable heartbeat failed")
        }

/// A stopping host closes its WebSockets (`server_restart`, so clients reconnect) instead of waiting out the shutdown timeout.
[<Sealed>]
type CableShutdown<'U>(server: CableServer<'U>, lifetime: IHostApplicationLifetime) =
    interface IHostedService with
        member _.StartAsync(_: CancellationToken) : Task =
            lifetime.ApplicationStopping.Register(fun () -> server.Restart()) |> ignore
            Task.CompletedTask

        member _.StopAsync(_: CancellationToken) : Task = Task.CompletedTask
