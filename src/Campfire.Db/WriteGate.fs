// Design: plans/design-data-ef.md section 3.3.
namespace Campfire.Db

open System.Threading
open System.Threading.Tasks

/// The one writer at a time, as SQLite has it: every write (`Writes.run`) holds the
/// gate from before `BEGIN IMMEDIATE` until its after-commit work is done.
[<Sealed>]
type WriteGate() =
    static let inWrite = new AsyncLocal<bool>()
    let semaphore = new SemaphoreSlim(1, 1)

    member _.Enter() : Task = semaphore.WaitAsync()

    member _.Exit() : unit = semaphore.Release() |> ignore

    /// True from `BEGIN` to `COMMIT` inside `Writes.run`, false in the after-commit queue and everywhere else (design-cable C16).
    static member InWrite : bool = inWrite.Value

    static member internal SetInWrite(value: bool) : unit = inWrite.Value <- value
