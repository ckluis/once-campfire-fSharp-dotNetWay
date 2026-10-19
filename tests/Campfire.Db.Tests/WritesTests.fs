/// Design: plans/design-data-ef.md section 5, tests W1-W7: `Writes.run`, the `WriteGate` and the shared gate of the migration rule.
module Campfire.Db.Tests.WritesTests

open System
open System.Collections.Generic
open System.Linq
open System.Threading
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Xunit
open Campfire.Db
open Campfire.Db.Tests.Support
open Campfire.Db.Ef

/// An event sink that records into the same log the hooks write to, so tests can read the order they ran in.
type LogSink(log: List<string>, onEmit: Event -> unit) =
    interface EventSink with
        member _.Emit(event: Event) =
            lock log (fun () -> log.Add(sprintf "event %A" event))
            onEmit event

/// A task on the thread pool, with its own execution context's view of `WriteGate.InWrite`.
let private spawn (f: unit -> Task<unit>) : Task = Task.Run(Func<Task>(fun () -> f () :> Task))

type Rig(?onEmit: Event -> unit) =
    let dir = new TempDir()
    let path = dir.File "writes.sqlite3"
    do Schema.prepareFile path "production" TimeProvider.System |> ignore
    let log = List<string>()
    let time = TimeProvider.System
    let factory = Data.factory (Data.options path time ignore)
    let env: Env =
        { Time = time
          Sink = LogSink(log, defaultArg onEmit ignore)
          RichText = BasicRichText()
          BcryptCost = 4 }

    member _.Path = path
    member _.Log = log
    member _.Env = env
    member _.Gate = WriteGate()
    member _.Factory = factory
    member _.Context() : CampfireDb = factory.CreateDbContext()
    member _.Dir = dir

    /// A row of `action_text_rich_texts` (no foreign keys; unique on record type, id and name).
    member _.Row(n: int64) : RichTextRecord =
        { Id = 0L; RecordType = "T"; RecordId = n; Name = "body"; Body = null; CreatedAt = DateTime.MinValue; UpdatedAt = DateTime.MinValue }

    member _.Count() : int =
        use db = factory.CreateDbContext()
        db.RichTexts.Count()

    interface IDisposable with
        member _.Dispose() = (dir :> IDisposable).Dispose()

// W1 -------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``W1 Writes.run holds BEGIN IMMEDIATE: a second connection's BEGIN IMMEDIATE is busy inside the body`` () =
    task {
        use rig = new Rig()
        use db = rig.Context()
        let! result =
            Writes.run rig.Gate rig.Env db (fun _ ->
                // Nothing has been written yet: only an immediate transaction already holds the write lock.
                use other = new SqliteConnection($"Data Source={rig.Path};Pooling=False")
                other.Open()
                use command = other.CreateCommand()
                command.CommandTimeout <- 1
                command.CommandText <- "BEGIN IMMEDIATE"
                let error = Assert.Throws<SqliteException>(fun () -> command.ExecuteNonQuery() |> ignore)
                error.SqliteErrorCode)
        Assert.Equal(5, unwrap result) // SQLITE_BUSY
        // After the commit the lock is free.
        use other = new SqliteConnection($"Data Source={rig.Path};Pooling=False")
        other.Open()
        use command = other.CreateCommand()
        command.CommandText <- "BEGIN IMMEDIATE; ROLLBACK"
        command.ExecuteNonQuery() |> ignore
    }

// W2 -------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``W2 hooks and events run in queue order after the commit, outside the transaction, and a nested hook runs at once`` () =
    task {
        use rig = new Rig()
        use db = rig.Context()
        let say (text: string) = lock rig.Log (fun () -> rig.Log.Add text)
        let! result =
            Writes.run rig.Gate rig.Env db (fun tx ->
                tx.Db.RichTexts.Add(rig.Row 1L) |> ignore
                say "body"
                tx.AfterCommit(fun after ->
                    Assert.False after.InTransaction
                    Assert.Null db.Database.CurrentTransaction
                    say "hook 1 start"
                    after.AfterCommit(fun _ -> say "nested")
                    say "hook 1 end")
                tx.EmitAfterCommit(DeliverWebhook(1L, 2L))
                tx.AfterCommit(fun _ -> say "hook 2")
                tx.EmitNow(PushMessage(3L, 4L))
                Assert.True tx.InTransaction)
        unwrap result
        Assert.Equal<string list>(
            [ "body"; "event PushMessage (3L, 4L)"; "hook 1 start"; "nested"; "hook 1 end"; "event DeliverWebhook (1L, 2L)"; "hook 2" ],
            List.ofSeq rig.Log)
        Assert.Equal(1, rig.Count())
    }

// W3 -------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``W3 an exception in the body rolls back, drops the queue and clears the tracker`` () =
    task {
        use rig = new Rig()
        use db = rig.Context()
        let ran = ref false
        let! thrown =
            Assert.ThrowsAsync<InvalidOperationException>(fun () ->
                Writes.run rig.Gate rig.Env db (fun tx ->
                    tx.Db.RichTexts.Add(rig.Row 1L) |> ignore
                    tx.Db.SaveChanges() |> ignore
                    tx.AfterCommit(fun _ -> ran.Value <- true)
                    tx.EmitAfterCommit(PurgeBlob 1L)
                    tx.Db.RichTexts.Add(rig.Row 2L) |> ignore
                    invalidOp "boom")
                :> Task)
        Assert.Equal("boom", thrown.Message)
        Assert.Equal(0, rig.Count())
        Assert.False ran.Value
        Assert.Empty rig.Log
        Assert.Empty(db.ChangeTracker.Entries())
        // The gate was left: the next write goes through, and the context is usable.
        let! next = Writes.run rig.Gate rig.Env db (fun tx -> tx.Db.RichTexts.Add(rig.Row 3L) |> ignore)
        unwrap next
        Assert.Equal(1, rig.Count())
    }

[<Fact>]
let ``W3 a DbException is an Error, and a unique index violation maps to isRecordNotUnique`` () =
    task {
        use rig = new Rig()
        use db = rig.Context()
        let! failed = Writes.run rig.Gate rig.Env db (fun _ -> Err.fail (RecordNotFound "Room"))
        match failed with
        | Error(RecordNotFound "Room") -> ()
        | other -> failwithf "%A" other
        let! first = Writes.run rig.Gate rig.Env db (fun tx -> tx.Db.RichTexts.Add(rig.Row 7L) |> ignore)
        unwrap first
        let! second = Writes.run rig.Gate rig.Env db (fun tx -> tx.Db.RichTexts.Add(rig.Row 7L) |> ignore)
        match second with
        | Error error -> Assert.True(DbError.isRecordNotUnique error, DbError.display error)
        | Ok() -> failwith "the duplicate was written"
        Assert.Equal(1, rig.Count())
        Assert.Empty(db.ChangeTracker.Entries())
    }

// W4 -------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``W4 a failing hook is logged, the later hooks run and the first error is returned`` () =
    task {
        use rig = new Rig()
        use db = rig.Context()
        let say (text: string) = lock rig.Log (fun () -> rig.Log.Add text)
        let! result =
            Writes.run rig.Gate rig.Env db (fun tx ->
                tx.Db.RichTexts.Add(rig.Row 1L) |> ignore
                tx.AfterCommit(fun _ -> failwith "first")
                tx.AfterCommit(fun _ -> say "second")
                tx.AfterCommit(fun _ -> failwith "third")
                tx.EmitAfterCommit(PurgeBlob 9L))
        match result with
        | Error(Other e) -> Assert.Equal("first", e.Message)
        | other -> failwithf "%A" other
        Assert.Equal<string list>([ "second"; "event PurgeBlob 9L" ] |> List.sort, rig.Log |> List.ofSeq |> List.sort)
        // The write itself committed, and the gate is free.
        Assert.Equal(1, rig.Count())
        let! next = Writes.run rig.Gate rig.Env db (fun _ -> 1)
        Assert.Equal(1, unwrap next)
    }

// W5 -------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``W5 InWrite is true across the body and the commit, false in the hooks, the sink, after run and in a concurrent read`` () =
    task {
        let seen = List<string * bool>()
        use rig = new Rig(fun _ -> lock seen (fun () -> seen.Add("sink", WriteGate.InWrite)))
        use db = rig.Context()
        Assert.False WriteGate.InWrite
        let signal = TaskCompletionSource()
        let reader = Task.Run(fun () -> signal.Task.Wait(); WriteGate.InWrite)
        let! result =
            Writes.run rig.Gate rig.Env db (fun tx ->
                seen.Add("body", WriteGate.InWrite)
                tx.Db.RichTexts.Add(rig.Row 1L) |> ignore
                tx.Db.SaveChanges() |> ignore
                seen.Add("after save", WriteGate.InWrite)
                signal.SetResult()
                seen.Add("concurrent read", reader.Result)
                tx.AfterCommit(fun _ -> seen.Add("hook", WriteGate.InWrite))
                tx.EmitAfterCommit(DisconnectUser(1L, true)))
        unwrap result
        seen.Add("after run", WriteGate.InWrite)
        Assert.Equal<(string * bool) list>(
            [ "body", true; "after save", true; "concurrent read", false; "sink", false; "hook", false; "after run", false ] |> List.sortBy fst,
            seen |> List.ofSeq |> List.sortBy fst)
    }

// W6 -------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``W6 16 concurrent Writes.run posts all commit, never two bodies at once, no SQLITE_BUSY`` () =
    task {
        use rig = new Rig()
        let gate = rig.Gate
        let inside = ref 0
        let overlapped = ref false
        let errors = List<string>()
        let one (n: int64) =
            spawn (fun () ->
                task {
                    use db = rig.Context()
                    let! result =
                        Writes.run gate rig.Env db (fun tx ->
                            if Interlocked.Increment(&inside.contents) > 1 then overlapped.Value <- true
                            tx.Db.RichTexts.Add(rig.Row n) |> ignore
                            Thread.Sleep 3
                            Interlocked.Decrement(&inside.contents) |> ignore)
                    match result with
                    | Ok() -> ()
                    | Error e -> lock errors (fun () -> errors.Add(DbError.display e))
                })
        let tasks = [ for n in 1L .. 16L do for round in 0L .. 3L -> one (n + round * 100L) ]
        do! Task.WhenAll tasks
        Assert.Empty errors
        Assert.False overlapped.Value
        Assert.Equal(64, rig.Count())
    }
