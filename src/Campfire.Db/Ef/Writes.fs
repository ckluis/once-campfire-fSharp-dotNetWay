// Design: plans/design-data-ef.md section 3.3 (the write path), E14 (ClockTimeProvider) and section 3.5 (Data.addServices).
namespace Campfire.Db.Ef

open System
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Campfire.Db
open Campfire.RailsCompat.Clock

/// The harness's clock as a `TimeProvider`, so a frozen `CAMPFIRE_FROZEN_TIME` reaches EF's timestamps (E14).
[<Sealed>]
type ClockTimeProvider(clock: SharedClock) =
    inherit TimeProvider()
    override _.GetUtcNow() : DateTimeOffset = clock.Now()

/// Everything a model needs besides the context: the clock, where side effects go, the Action Text adapter (still the port's
/// interface over `Conn`; unit 1.5a moves it onto `CampfireDb`) and BCrypt's cost.
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
    /// and the first failure is returned. The tracker is cleared and the gate left either way.
    let run (gate: WriteGate) (env: Env) (db: CampfireDb) (body: Tx -> 'T) : Task<Result<'T, DbError>> =
        task {
            do! gate.Enter()
            try
                let tx = Tx(db, env, true)
                match attempt db tx body with
                | Error error -> return Error error
                | Ok value ->
                    let after = Tx(db, env, false)
                    let mutable firstFailure: exn | null = null
                    for item in tx.TakeQueue() do
                        match item with
                        | Choice2Of2 event -> env.Sink.Emit event
                        | Choice1Of2 hook ->
                            try
                                hook after
                            with e ->
                                eprintfn "ERROR campfire_db: after_commit hook failed: %s" e.Message
                                if isNull firstFailure then firstFailure <- e
                    match firstFailure with
                    | null -> return Ok value
                    | failure ->
                        return
                            match toDbError failure with
                            | ValueSome error -> Error error
                            | ValueNone -> Error(Other failure)
            finally
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
