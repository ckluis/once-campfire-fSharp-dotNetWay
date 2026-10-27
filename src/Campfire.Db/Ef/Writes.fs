// Design: plans/design-data-ef.md section 3.3 (the write path), section 3.5 (Data.addServices).
namespace Campfire.Db.Ef

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Campfire.Db

/// Everything a model needs besides the context: the clock, where side effects go, the Action Text adapter (over
/// `CampfireDb`) and BCrypt's cost.
type Env =
    { Time: TimeProvider
      Sink: EventSink
      RichText: RichText
      /// `BCrypt::Engine.cost` (12), or `MIN_COST` (4) in the test environment.
      BcryptCost: int }

/// A write in progress: the context inside a `BEGIN IMMEDIATE` transaction (or, while the after-commit queue runs, outside one),
/// the environment and the queued after-commit work.
[<Sealed; NoEquality; NoComparison>]
type Tx internal (db: CampfireDb, env: Env, inTransaction: bool) =
    let queue = ResizeArray<Choice<Tx -> unit, Event>>()

    member _.Db : CampfireDb = db
    member _.Env : Env = env

    /// `Time.current`, at the microseconds a `datetime(6)` column keeps.
    member _.Now() : DateTime = RailsTime.now env.Time

    member _.RichText : RichText = env.RichText

    /// Emits right away, even though the transaction may still roll back: the side effects Rails performs mid-transaction.
    member _.EmitNow(event: Event) : unit = env.Sink.Emit event

    /// Emits once the transaction commits (`after_commit`), or at once when already running after commit.
    member _.EmitAfterCommit(event: Event) : unit =
        if inTransaction then queue.Add(Choice2Of2 event) else env.Sink.Emit event

    /// Queues work to run after commit, each statement in its own autocommit. Queued from inside a hook (no transaction) it runs
    /// at once, and a failure is logged.
    member _.AfterCommit(hook: Tx -> unit) : unit =
        if inTransaction then
            queue.Add(Choice1Of2 hook)
        else
            try
                hook (Tx(db, env, false))
            with e ->
                eprintfn "ERROR campfire_db: after_commit hook failed: %s" e.Message

    member _.InTransaction : bool = inTransaction

    member internal _.TakeQueue() : Choice<Tx -> unit, Event> list =
        let items = List.ofSeq queue
        queue.Clear()
        items

module Writes =

    /// What a read or a write may raise on purpose, as the `DbError` it stands for. Anything else is a bug in the closure.
    let internal toDbError (e: exn) : DbError voption =
        match e with
        | DbException error -> ValueSome error
        | :? SqliteException as sqlite -> ValueSome(Sqlite sqlite)
        | :? DbUpdateException as update ->
            match update.InnerException with
            | :? SqliteException as sqlite -> ValueSome(Sqlite sqlite)
            | _ -> ValueNone
        | _ -> ValueNone

    /// `BEGIN IMMEDIATE` (`default_transaction_mode: immediate`: EF's `BeginTransaction` on Microsoft.Data.Sqlite is
    /// `BEGIN IMMEDIATE` unless asked for `ReadUncommitted`; W1 holds it to that), the body, `SaveChanges`, `COMMIT`. `InWrite`
    /// holds exactly across that span, and any exception rolls back (disposing an uncommitted transaction does it, and closes
    /// the connection the transaction opened).
    let private transact (db: CampfireDb) (tx: Tx) (body: Tx -> 'T) : 'T =
        use transaction = db.Database.BeginTransaction()
        WriteGate.SetInWrite true
        try
            let value = body tx
            db.SaveChanges() |> ignore
            transaction.Commit()
            value
        finally
            WriteGate.SetInWrite false

    let private attempt (db: CampfireDb) (tx: Tx) (body: Tx -> 'T) : Result<'T, DbError> =
        try
            Ok(transact db tx body)
        with e ->
            match toDbError e with
            | ValueSome error -> Error error
            | ValueNone -> reraise ()

    /// Enter the gate; `BEGIN IMMEDIATE`; body; `SaveChanges`; `COMMIT`. An exception (or `DbException`) in the body or the commit
    /// rolls back and drops the queue. Then the queue in order: events to `Env.Sink`, hooks with an after-commit `Tx` (each
    /// statement its own autocommit; a hook that tracks entities calls `SaveChanges`); a failing hook is logged, the rest run,
    /// and the write still returns `Ok` (the data committed). The tracker is cleared and the gate left either way.
    let run (gate: WriteGate) (env: Env) (db: CampfireDb) (body: Tx -> 'T) : Task<Result<'T, DbError>> =
        task {
            do! gate.Enter()
            WriteGate.SetHeld true
            try
                let tx = Tx(db, env, true)
                match attempt db tx body with
                | Error error -> return Error error
                | Ok value ->
                    let after = Tx(db, env, false)
                    for item in tx.TakeQueue() do
                        match item with
                        | Choice2Of2 event -> env.Sink.Emit event
                        | Choice1Of2 hook ->
                            try
                                hook after
                            with e ->
                                eprintfn "ERROR campfire_db: after_commit hook failed: %s" e.Message
                    // The data committed: a failed hook was logged and does not make the write a failure.
                    return Ok value
            finally
                WriteGate.SetHeld false
                db.ChangeTracker.Clear()
                gate.Exit()
        }

module Reads =

    /// The port's `Database.Read` boundary: `DbException` and `SqliteException` become a `DbError`; the context is not tracking,
    /// so nothing to clear.
    let run (db: CampfireDb) (f: CampfireDb -> 'T) : Result<'T, DbError> =
        try
            Ok(f db)
        with e ->
            match Writes.toDbError e with
            | ValueSome error -> Error error
            | ValueNone -> reraise ()

    /// `run` for a read that awaits (a page's fragments are fetched while the context is in use): the same boundary, the context
    /// used one step at a time.
    let runAsync (db: CampfireDb) (f: CampfireDb -> Task<'T>) : Task<Result<'T, DbError>> =
        task {
            let! outcome =
                task {
                    try
                        let! value = f db
                        return Choice1Of2 value
                    with e ->
                        return Choice2Of2 e
                }
            match outcome with
            | Choice1Of2 value -> return Ok value
            | Choice2Of2 e ->
                match Writes.toDbError e with
                | ValueSome error -> return Error error
                | ValueNone ->
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw()
                    return failwith "unreachable"
        }
