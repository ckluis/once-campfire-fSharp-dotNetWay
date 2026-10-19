// Port of rust/crates/db/src/schema.rs; on EF Core per plans/design-data-ef.md E15.
/// The schema exactly as `bin/rails db:prepare` creates it on a fresh database, and the
/// connection settings from `reference/config/database.yml` plus the sqlite3 adapter's
/// `DEFAULT_PRAGMAS`.
///
/// `schema.sql` is the `sqlite_master` of a database the reference app created with
/// `db:prepare` (loading `reference/db/schema.rb`), minus the objects SQLite derives on its
/// own (FTS5 shadow tables, `sqlite_sequence`, autoindexes). A fresh `db:prepare` loads
/// `schema.rb`, so columns come out in alphabetical order; databases that were migrated keep
/// migration order. All queries in this crate name their columns, so both work: every model
/// selects its own list of them (`Columns`) and reads by position in that list, never by
/// position over `*`.
module Campfire.Db.Schema

open System
open System.IO
open System.Linq
open System.Reflection
open Microsoft.EntityFrameworkCore

let schemaSql : string =
    match Assembly.GetExecutingAssembly().GetManifestResourceStream "schema.sql" with
    | null -> failwith "schema.sql is not embedded"
    | stream ->
        use stream = stream
        use reader = new StreamReader(stream)
        reader.ReadToEnd()

/// Every migration in `reference/db/migrate`, oldest first.
let migrationVersions : string list =
    [ "20231215043540"
      "20231220143106"
      "20240110071740"
      "20240115124901"
      "20240130003150"
      "20240130213001"
      "20240131105830"
      "20240209110503"
      "20250825100957"
      "20250825100958"
      "20250825100959"
      "20251126092013"
      "20251126115722"
      "20251126130131"
      "20251212154340" ]

/// SHA1 of `reference/db/schema.rb`, which `db:schema:load` records in `ar_internal_metadata`.
[<Literal>]
let SchemaSha1 = "f75da8dad38bfb179ffd757bd7a7c2b3f818bc29"

/// Indexes this app adds to the Rails schema, created on boot when missing (new and existing
/// databases alike). Additive only, so the database still works with the Rails image.
let additions : string list =
    [
      // A room's messages are paged by `created_at` (`last_page`, `page_before`, `page_after`), and
      // with only `index_messages_on_room_id` every room page sorted the room's whole history:
      // 60 ms at 236k messages, against 0.02 ms with this index.
      """CREATE INDEX IF NOT EXISTS "index_messages_on_room_id_and_created_at" ON "messages" ("room_id", "created_at")""" ]

type Prepared =
    /// The database was empty; the schema was loaded.
    | Loaded
    /// The schema was already current.
    | UpToDate

let private tableExists (db: Ef.CampfireDb) (name: string) : bool =
    db.Database.SqlQuery<int64>($"SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = {name}").ToList()
    |> Seq.exists (fun count -> count > 0L)

let pendingMigrations (db: Ef.CampfireDb) : string list =
    let applied = db.Database.SqlQuery<string>($"SELECT version AS Value FROM schema_migrations").ToList() |> Set.ofSeq
    migrationVersions |> List.filter (fun version -> not (applied.Contains version))

let private setInternalMetadata (db: Ef.CampfireDb) (key: string) (value: string) (now: string) : unit =
    match db.Database.SqlQuery<string | null>($"SELECT \"value\" AS Value FROM \"ar_internal_metadata\" WHERE \"key\" = {key}").ToList() |> List.ofSeq with
    | [] ->
        db.Database.ExecuteSql($"INSERT INTO \"ar_internal_metadata\" (\"key\", \"value\", \"created_at\", \"updated_at\") VALUES ({key}, {value}, {now}, {now})")
        |> ignore
    | current :: _ when current <> value ->
        db.Database.ExecuteSql($"UPDATE \"ar_internal_metadata\" SET \"value\" = {value}, \"updated_at\" = {now} WHERE \"key\" = {key}")
        |> ignore
    | _ -> ()

/// `schema.sql` is a constant, so the one `ExecuteSqlRaw` of the data layer (E12) is here: inside `BEGIN IMMEDIATE`, with the
/// migration versions and the metadata Rails records, committed together or not at all (disposing the transaction rolls back).
let private loadSchema (db: Ef.CampfireDb) (environment: string) (time: TimeProvider) : unit =
    use transaction = db.Database.BeginTransaction()
    db.Database.ExecuteSqlRaw schemaSql |> ignore

    // `assume_migrated_upto_version` inserts the current version, then the rest newest first.
    for version in List.rev migrationVersions do
        db.Database.ExecuteSql($"INSERT INTO \"schema_migrations\" (\"version\") VALUES ({version})") |> ignore

    let now = RailsTime.toDb (RailsTime.now time)
    setInternalMetadata db "environment" environment now
    setInternalMetadata db "schema_sha1" SchemaSha1 now
    transaction.Commit()

/// `bin/rails db:prepare`: loads the schema into an empty database, or verifies an
/// existing one is fully migrated. We don't port migrations, so a database with pending
/// migrations is an error (boot the Rails image once to migrate it). Then adds `additions`. Never `EnsureCreated` or `Migrate`:
/// Rails owns the schema. Not a write of the app's: it runs at boot, before anything else holds the file.
let prepare (db: Ef.CampfireDb) (environment: string) (time: TimeProvider) : Prepared =
    let prepared =
        if tableExists db "schema_migrations" then
            match pendingMigrations db with
            | [] -> UpToDate
            | pending -> Err.fail (DbError.other ("pending migrations: " + String.concat ", " pending))
        else
            loadSchema db environment time
            Loaded
    for addition in additions do
        db.Database.ExecuteSqlRaw addition |> ignore
    prepared

/// `prepare` on a context of its own over the file at `path` (tests, and anything that has no factory yet).
let prepareFile (path: string) (environment: string) (time: TimeProvider) : Prepared =
    use db = new Ef.CampfireDb(Ef.Data.options path time ignore)
    prepare db environment time
