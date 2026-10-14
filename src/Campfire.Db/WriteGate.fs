// Design: plans/design-data-ef.md section 3.3 and the section 4 migration rule. Lives ahead of Database.fs because the old
// writer takes the gate too, until unit 1.11 deletes it.
namespace Campfire.Db

open System.Threading
open System.Threading.Tasks

/// The one writer at a time, as SQLite has it: every write (the old `Database` writer thread's job, `Writes.run`) holds the
/// gate from before `BEGIN IMMEDIATE` until its after-commit work is done.
[<Sealed>]
type WriteGate() =
    static let inWrite = new AsyncLocal<bool>()
    let semaphore = new SemaphoreSlim(1, 1)

    member _.Enter() : Task = semaphore.WaitAsync()

    /// For synchronous callers (`Database.WriteBlocking`), never from inside an async task.
    member _.EnterBlocking() : unit = semaphore.Wait()

    member _.Exit() : unit = semaphore.Release() |> ignore

    /// True from `BEGIN` to `COMMIT` inside `Writes.run`, false in the after-commit queue and everywhere else (design-cable C16).
    static member InWrite : bool = inWrite.Value

    static member internal SetInWrite(value: bool) : unit = inWrite.Value <- value
