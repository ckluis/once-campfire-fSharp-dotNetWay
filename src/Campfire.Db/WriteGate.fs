// Design: plans/design-data-ef.md section 3.3.
namespace Campfire.Db

open System.Threading
open System.Threading.Tasks

/// The one writer at a time, as SQLite has it: every write (`Writes.run`) holds the
/// gate from before `BEGIN IMMEDIATE` until its after-commit work is done.
[<Sealed>]
type WriteGate() =
    static let inWrite = new AsyncLocal<bool>()
    static let held = new AsyncLocal<bool>()
    static let exempt = new AsyncLocal<int>()
    let semaphore = new SemaphoreSlim(1, 1)

    member _.Enter() : Task = semaphore.WaitAsync()

    member _.Exit() : unit = semaphore.Release() |> ignore

    /// True from `BEGIN` to `COMMIT` inside `Writes.run`, false in the after-commit queue and everywhere else (design-cable C16).
    static member InWrite : bool = inWrite.Value

    static member internal SetInWrite(value: bool) : unit = inWrite.Value <- value

    /// True while this flow holds a gate (the body, the commit and the after-commit queue of `Writes.run`) or runs inside `Exempt`:
    /// the span in which `SingleWriterGuard` lets a write statement through.
    static member MayWrite : bool = held.Value || exempt.Value > 0

    static member internal SetHeld(value: bool) : unit = held.Value <- value

    /// The boot, shutdown and test-setup writes that happen outside any `Writes.run` (`Schema.prepare`): dispose to end it.
    static member Exempt() : System.IDisposable =
        exempt.Value <- exempt.Value + 1
        { new System.IDisposable with
            member _.Dispose() = exempt.Value <- exempt.Value - 1 }
