/// What the model tests share: a database loaded with the reference fixtures (`fixtures :all`), a
/// recording event sink and a controllable clock (`ActiveSupport::Testing::TimeHelpers`).
module Campfire.Db.Tests.Support

open System
open System.IO
open Microsoft.Extensions.Time.Testing
open Campfire.Db

// D4 counts physical connections through a process-wide counter, so no other test may open one meanwhile; the suite is a few
// seconds, so it runs one test at a time.
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()

/// Unwraps an expected success.
let unwrap (result: Result<'T, DbError>) : 'T =
    match result with
    | Ok value -> value
    | Error e -> failwith (DbError.display e)

/// A fixture's id, by label.
let id (label: string) : int64 = Fixtures.identify label

/// A distinct time for each `n`, with microseconds.
let at (n: int64) : DateTime = DateTime(DateTime.UnixEpoch.Ticks + (1_790_000_000_000_000L + n * 1_000_123L) * 10L, DateTimeKind.Utc)

/// A directory for one test's databases, removed when the test is done.
type TempDir() =
    let dir = Directory.CreateTempSubdirectory "campfire-db-test"
    member _.Path : string = dir.FullName
    member _.File(name: string) : string = Path.Combine(dir.FullName, name)

    interface IDisposable with
        member _.Dispose() =
            try
                dir.Delete true
            with _ ->
                ()

/// Plain ADO.NET over a database file, for the checks that read what is on disk rather than through the models (the rows another
/// app wrote, a table's raw contents). No EF: nothing here knows the entities.
module Raw =
    open Microsoft.Data.Sqlite

    /// A value as SQLite stores it.
    type Cell =
        | Null
        | Int of int64
        | Real of float
        | Text of string
        | Blob of byte[]

    /// A connection of its own (no pool, so the file is free again when it is disposed).
    let openFile (path: string) : SqliteConnection =
        let conn = new SqliteConnection($"Data Source={path};Pooling=False")
        conn.Open()
        conn

    let query (conn: SqliteConnection) (sql: string) (map: SqliteDataReader -> 'T) : 'T list =
        use command = conn.CreateCommand()
        command.CommandText <- sql
        use reader = command.ExecuteReader()
        [ while reader.Read() do
              yield map reader ]

    let scalarInt (conn: SqliteConnection) (sql: string) : int64 =
        use command = conn.CreateCommand()
        command.CommandText <- sql
        Convert.ToInt64(command.ExecuteScalar())

    /// Runs statements and returns the first column of the last row (or 0 when none).
    let exec (conn: SqliteConnection) (sql: string) : unit =
        use command = conn.CreateCommand()
        command.CommandText <- sql
        command.ExecuteNonQuery() |> ignore

    /// A command whose `?` marks are numbered parameters bound to `args` (Microsoft.Data.Sqlite wants names).
    let private bound (conn: SqliteConnection) (sql: string) (args: obj list) : SqliteCommand =
        let command = conn.CreateCommand()
        let parts = sql.Split '?'
        command.CommandText <- String.Join("", parts |> Array.mapi (fun i part -> if i = 0 then part else $"@p{i - 1}{part}"))
        args |> List.iteri (fun i arg -> command.Parameters.AddWithValue($"@p{i}", arg) |> ignore)
        command

    /// A statement with `?` parameters (ids, text), its rows mapped.
    let rows (conn: SqliteConnection) (sql: string) (args: obj list) (map: SqliteDataReader -> 'T) : 'T list =
        use command = bound conn sql args
        use reader = command.ExecuteReader()
        [ while reader.Read() do
              yield map reader ]

    /// `rows`, the first row.
    let row (conn: SqliteConnection) (sql: string) (args: obj list) (map: SqliteDataReader -> 'T) : 'T = rows conn sql args map |> List.exactlyOne

    /// A statement with `?` parameters that returns nothing; the rows it changed.
    let run (conn: SqliteConnection) (sql: string) (args: obj list) : int =
        use command = bound conn sql args
        command.ExecuteNonQuery()

    let names (conn: SqliteConnection) (sql: string) : string list = query conn sql (fun r -> r.GetString 0)

    /// The row's cells, by column name.
    let cells (r: SqliteDataReader) : (string * Cell) list =
        [ for i in 0 .. r.FieldCount - 1 ->
              let cell =
                  match r.GetValue i with
                  | :? DBNull -> Null
                  | :? int64 as v -> Int v
                  | :? float as v -> Real v
                  | :? string as v -> Text v
                  | :? (byte[]) as v -> Blob v
                  | other -> failwith $"unexpected SQLite value {other.GetType()}"
              r.GetName i, cell ]

type TestDb(?fixtures: bool) =
    let dir = new TempDir()
    let sink = RecordingSink()
    // Starts before every date the tests travel to; the fake clock only moves forward.
    let clock = FakeTimeProvider(DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    let path = dir.File "test.sqlite3"
    let time : TimeProvider = clock
    let factory = Ef.Data.factory (Ef.Data.options path time ignore)
    let efEnv : Ef.Env = { Time = time; Sink = sink; RichText = BasicRichText(); BcryptCost = 4 }
    let gate = WriteGate()
    do Schema.prepareFile path "test" time |> ignore

    do
        if defaultArg fixtures true then
            Testing.loadFixtures gate efEnv factory

    member _.Gate : WriteGate = gate

    /// A plain ADO.NET connection on the database file for the length of `f`, to read what is on disk or to put rows in that no
    /// model makes (the tests are single-threaded, so it takes no gate).
    member _.Sql(f: Microsoft.Data.Sqlite.SqliteConnection -> 'T) : 'T =
        use conn = Raw.openFile path
        f conn
    member _.Factory : Microsoft.EntityFrameworkCore.IDbContextFactory<Ef.CampfireDb> = factory
    member _.EfEnv : Ef.Env = efEnv
    member _.Sink : RecordingSink = sink
    member _.Time : FakeTimeProvider = clock
    member _.Dir : TempDir = dir

    /// `Writes.run` on a leased context.
    member _.TryWriteEf(f: Ef.Tx -> 'T) : Result<'T, DbError> =
        use context = factory.CreateDbContext()
        (Ef.Writes.run gate efEnv context f).GetAwaiter().GetResult()

    member this.WriteEf(f: Ef.Tx -> 'T) : 'T = this.TryWriteEf f |> unwrap

    member _.TryReadEf(f: Ef.CampfireDb -> 'T) : Result<'T, DbError> =
        use context = factory.CreateDbContext()
        Ef.Reads.run context f

    member _.ReadEf(f: Ef.CampfireDb -> 'T) : 'T =
        use context = factory.CreateDbContext()
        Ef.Reads.run context f |> unwrap

    /// `Now` as the `DateTime` EF's entities carry (UTC, microseconds).
    member _.Utc() : DateTime = RailsTime.now time

    member _.Events() : Event list = sink.Events()

    /// `travel_to Membership::Connectable::CONNECTION_TTL.from_now + 1`
    member _.Travel(seconds: int64) : unit = clock.Advance(TimeSpan.FromSeconds(float seconds))

    interface IDisposable with
        member _.Dispose() =
            // Pooled connections hold the file (and its WAL) open; the directory is removed next.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
            (dir :> IDisposable).Dispose()
