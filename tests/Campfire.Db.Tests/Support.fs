/// What the model tests share: a database loaded with the reference fixtures (`fixtures :all`), a
/// recording event sink and a controllable clock (`ActiveSupport::Testing::TimeHelpers`).
module Campfire.Db.Tests.Support

open System
open System.IO
open Campfire.RailsCompat.Clock
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
let at (n: int64) : Timestamp = Timestamp.FromMicrosecond(1_790_000_000_000_000L + n * 1_000_123L)

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
/// app wrote, a table's raw contents). Not the port's `Conn`, and no EF: nothing here knows the entities.
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

/// The environment tests run in: a recording sink, a controllable clock, BCrypt at its minimum cost.
let testEnv (sink: RecordingSink) (clock: TestClock) : Env =
    { Clock = clock
      Sink = sink
      BcryptCost = 4 }

type TestDb(?fixtures: bool) =
    let dir = new TempDir()
    let sink = RecordingSink()
    let clock = TestClock()
    let path = dir.File "test.sqlite3"
    // EF Core beside the port's `Database` until unit 1.11: the same file, the same gate, the same sink and clock.
    let time = Ef.ClockTimeProvider clock
    let factory = Ef.Data.factory (Ef.Data.options path time ignore)
    let efEnv : Ef.Env = { Time = time; Sink = sink; RichText = BasicRichText(); BcryptCost = 4 }
    do Schema.prepareFile path "test" time |> ignore
    let db = Database.Open({ Config.create path with Readers = 2 }, testEnv sink clock)

    do
        if defaultArg fixtures true then
            Testing.loadFixtures db.Gate efEnv factory

    member _.Db : Database = db
    member _.Factory : Microsoft.EntityFrameworkCore.IDbContextFactory<Ef.CampfireDb> = factory
    member _.EfEnv : Ef.Env = efEnv
    member _.Sink : RecordingSink = sink
    member _.Clock : TestClock = clock
    member _.Dir : TempDir = dir

    member _.Write(f: Tx -> 'T) : 'T = db.WriteBlocking f |> unwrap

    member _.TryWrite(f: Tx -> 'T) : Result<'T, DbError> = db.WriteBlocking f

    member _.Read(f: Conn -> 'T) : 'T = db.ReadBlocking f |> unwrap

    /// `Writes.run` on a leased context: the EF writer (unit 1.3 onward), sharing the old writer's gate.
    member _.TryWriteEf(f: Ef.Tx -> 'T) : Result<'T, DbError> =
        use context = factory.CreateDbContext()
        (Ef.Writes.run db.Gate efEnv context f).GetAwaiter().GetResult()

    member this.WriteEf(f: Ef.Tx -> 'T) : 'T = this.TryWriteEf f |> unwrap

    member _.TryReadEf(f: Ef.CampfireDb -> 'T) : Result<'T, DbError> =
        use context = factory.CreateDbContext()
        Ef.Reads.run context f

    member _.ReadEf(f: Ef.CampfireDb -> 'T) : 'T =
        use context = factory.CreateDbContext()
        Ef.Reads.run context f |> unwrap

    member _.Now() : Timestamp = Timestamp.FromDateTimeOffset(clock.Now())

    /// `Now` as the `DateTime` EF's entities carry (UTC, microseconds).
    member _.Utc() : DateTime = RailsTime.now time

    member _.Events() : Event list = sink.Events()

    /// `travel_to Membership::Connectable::CONNECTION_TTL.from_now + 1`
    member _.Travel(seconds: int64) : unit = clock.Travel(TimeSpan.FromSeconds(float seconds))

    interface IDisposable with
        member _.Dispose() =
            (db :> IDisposable).Dispose()
            (dir :> IDisposable).Dispose()
