// Design: plans/design-jobs.md J10 (`TimeProvider` is the only clock).
namespace Campfire.App.Platform

open System
open Campfire.Rails

/// The harness's frozen clock: `Time.current` stands still and nothing else does. It overrides `GetUtcNow` only, so timers
/// (`PeriodicTimer`, `Task.Delay`, `CancellationTokenSource`) and `GetTimestamp` keep running, which Cable's heartbeat and
/// ACME's waits need (a `FakeTimeProvider` never ticks them). `FakeTimeProvider` is for tests.
[<Sealed>]
type FrozenTimeProvider(at: DateTimeOffset) =
    inherit TimeProvider()
    override _.GetUtcNow() : DateTimeOffset = at

module HarnessClock =
    /// The variable the parity runs pin.
    [<Literal>]
    let Variable = "CAMPFIRE_FROZEN_TIME"

    /// `CAMPFIRE_FROZEN_TIME` (an RFC 3339 timestamp such as `2024-06-01T12:00:00Z`): unset or blank is the system clock.
    let fromValue (value: string | null) : Result<TimeProvider, string> =
        match value with
        | null -> Ok TimeProvider.System
        | value when String.IsNullOrWhiteSpace value -> Ok TimeProvider.System
        | value ->
            match Timestamps.tryParse (value.Trim()) with
            | Some now -> Ok(FrozenTimeProvider now :> TimeProvider)
            | None -> Error $"{Variable}={value} is not an RFC 3339 timestamp"

    /// The process's clock, from the environment.
    let fromEnvironment () : Result<TimeProvider, string> = fromValue (Environment.GetEnvironmentVariable Variable)
