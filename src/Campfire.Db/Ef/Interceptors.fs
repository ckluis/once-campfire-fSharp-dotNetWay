// Design: plans/design-data-ef.md section 3.2 (E5, E9, E10).
namespace Campfire.Db.Ef

open System
open System.Data.Common
open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Diagnostics
open Campfire.Db

/// Rails' connection settings (`SQLite3Adapter#configure_connection`), once per physical connection: EF opens and closes a
/// connection per query and Microsoft.Data.Sqlite pools the physical ones, so each handle is remembered (E10).
type SqlitePragmas() =
    inherit DbConnectionInterceptor()

    static let configured = ConditionalWeakTable<SQLitePCL.sqlite3, obj>()
    static let mutable applied = 0

    /// How many physical connections have been configured (tests compare it with the connections they opened).
    static member Applied : int = Volatile.Read(&applied)

    static member Apply(connection: DbConnection) : unit =
        match connection with
        | :? SqliteConnection as c ->
            let handle = nonNull c.Handle
            let mutable seen = Unchecked.defaultof<obj>
            // Racing threads own different physical connections, so the check and the add never contend on one handle.
            if not (configured.TryGetValue(handle, &seen)) then
                use command = c.CreateCommand()
                command.CommandText <-
                    "PRAGMA busy_timeout = 5000; PRAGMA foreign_keys = ON; PRAGMA journal_mode = wal; PRAGMA synchronous = normal; "
                    + "PRAGMA journal_size_limit = 67108864; PRAGMA cache_size = 2000"
                command.ExecuteNonQuery() |> ignore
                configured.Add(handle, obj ())
                Interlocked.Increment(&applied) |> ignore
        | _ -> ()

    override _.ConnectionOpened(connection, _) = SqlitePragmas.Apply connection

    override _.ConnectionOpenedAsync(connection, _, _) =
        SqlitePragmas.Apply connection
        Task.CompletedTask

/// `created_at` / `updated_at` as Active Record sets them: `Added` entities get both when unset, `Modified` ones get
/// `updated_at` unless it was set. One `now` per `SaveChanges`, as Active Record uses one per save (E9).
type RailsTimestamps(time: TimeProvider) =
    inherit SaveChangesInterceptor()

    static let isUnset (value: obj | null) =
        match value with
        | :? DateTime as d -> d = DateTime.MinValue
        | _ -> false

    member _.Stamp(context: DbContext | null) : unit =
        match context with
        | null -> ()
        | context ->
            let now = Campfire.Db.RailsTime.now time
            for entry in context.ChangeTracker.Entries() do
                match entry.State with
                | EntityState.Added ->
                    for name in [ "CreatedAt"; "UpdatedAt" ] do
                        if not (isNull (entry.Metadata.FindProperty name)) then
                            let property = entry.Property name
                            if isUnset property.CurrentValue then property.CurrentValue <- now
                | EntityState.Modified ->
                    if not (isNull (entry.Metadata.FindProperty "UpdatedAt")) then
                        let property = entry.Property "UpdatedAt"
                        if not property.IsModified then property.CurrentValue <- now
                | _ -> ()

    override this.SavingChanges(eventData, result) =
        this.Stamp eventData.Context
        result

    override this.SavingChangesAsync(eventData, result, _) =
        this.Stamp eventData.Context
        ValueTask<InterceptionResult<int>>(result)

/// A `DateTime` bound as a raw SQL parameter reaches SQLite as 7-digit text that Active Record never writes and that compares
/// wrongly against stored text. LINQ parameters go through the converter and are text already, so this only ever fires on
/// `FromSql`/`ExecuteSql` holes: pass `RailsTime.toDb t` (E5).
type RailsParameterGuard() =
    inherit DbCommandInterceptor()

    static member Check(command: DbCommand) : unit =
        for parameter in command.Parameters do
            match parameter.Value with
            | :? DateTime
            | :? DateTimeOffset ->
                invalidOp $"parameter {parameter.ParameterName} is a DateTime/DateTimeOffset: pass RailsTime.toDb (Rails' text) in raw SQL"
            | _ -> ()

    override _.ReaderExecuting(command, _, result) =
        RailsParameterGuard.Check command
        result

    override _.ReaderExecutingAsync(command, _, result, _) =
        RailsParameterGuard.Check command
        ValueTask<InterceptionResult<DbDataReader>>(result)

    override _.NonQueryExecuting(command, _, result) =
        RailsParameterGuard.Check command
        result

    override _.NonQueryExecutingAsync(command, _, result, _) =
        RailsParameterGuard.Check command
        ValueTask<InterceptionResult<int>>(result)

    override _.ScalarExecuting(command, _, result) =
        RailsParameterGuard.Check command
        result

    override _.ScalarExecutingAsync(command, _, result, _) =
        RailsParameterGuard.Check command
        ValueTask<InterceptionResult<obj>>(result)

/// The single writer, checked at run time: a statement that writes (INSERT, UPDATE, DELETE, REPLACE, CREATE, DROP, ALTER, or a CTE
/// leading to one) is refused unless the flow holds the write gate (`Writes.run`) or is inside `WriteGate.Exempt` (boot's
/// `Schema.prepare`). Reads and pragmas pass. Free on the write path (a flag), one prefix test per read.
type SingleWriterGuard() =
    inherit DbCommandInterceptor()

    static let writeWords = [| "INSERT"; "UPDATE"; "DELETE"; "REPLACE"; "CREATE"; "DROP"; "ALTER"; "VACUUM"; "REINDEX" |]

    static let startsWith (text: string) (start: int) (word: string) =
        String.Compare(text, start, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) = 0

    static let isWrite (text: string) : bool =
        let mutable i = 0
        while i < text.Length && Char.IsWhiteSpace text[i] do
            i <- i + 1
        if i >= text.Length then false
        else
            match text[i] with
            | 's' | 'S' | 'p' | 'P' -> false // SELECT, PRAGMA
            | _ ->
                if startsWith text i "WITH" then
                    // A CTE may lead to a write: look for the keyword anywhere in it.
                    writeWords |> Array.exists (fun w -> text.Contains(w, StringComparison.OrdinalIgnoreCase))
                else
                    writeWords |> Array.exists (startsWith text i)

    static member Check(command: DbCommand) : unit =
        if not WriteGate.MayWrite && isWrite command.CommandText then
            invalidOp $"a write statement outside the write gate (Writes.run): {command.CommandText}"

    override _.ReaderExecuting(command, _, result) =
        SingleWriterGuard.Check command
        result

    override _.ReaderExecutingAsync(command, _, result, _) =
        SingleWriterGuard.Check command
        ValueTask<InterceptionResult<DbDataReader>>(result)

    override _.NonQueryExecuting(command, _, result) =
        SingleWriterGuard.Check command
        result

    override _.NonQueryExecutingAsync(command, _, result, _) =
        SingleWriterGuard.Check command
        ValueTask<InterceptionResult<int>>(result)

    override _.ScalarExecuting(command, _, result) =
        SingleWriterGuard.Check command
        result

    override _.ScalarExecutingAsync(command, _, result, _) =
        SingleWriterGuard.Check command
        ValueTask<InterceptionResult<obj>>(result)
