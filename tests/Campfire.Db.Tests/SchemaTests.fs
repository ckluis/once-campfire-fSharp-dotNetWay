// Port of the tests in rust/crates/db/src/schema.rs
module Campfire.Db.Tests.SchemaTests

open System
open System.IO
open System.Security.Cryptography
open Xunit
open Campfire.Db
open Campfire.Db.Tests.Support

let private reference = Campfire.Tests.Repo.path "reference/db"

[<Fact>]
let ``schema sha1 matches reference schema rb`` () =
    let contents = File.ReadAllBytes(Path.Combine(reference, "schema.rb"))
    Assert.Equal(Schema.SchemaSha1, Convert.ToHexStringLower(SHA1.HashData contents))

[<Fact>]
let ``migration versions match reference migrations`` () =
    let versions =
        Directory.GetFiles(Path.Combine(reference, "migrate"))
        |> Array.map (fun f -> (Path.GetFileName f).Split('_')[0])
        |> Array.sort
        |> List.ofArray
    Assert.Equal<string list>(versions, Schema.migrationVersions)

/// A prepared-on-demand database file: `Schema.prepare` runs through EF on a context of its own.
let private fresh (dir: ScratchDir) = dir.File "schema.sqlite3"

[<Fact>]
let ``prepare loads then is idempotent`` () =
    use dir = new ScratchDir()
    let path = fresh dir
    Assert.Equal(Schema.Loaded, Schema.prepareFile path "production" (TimeProvider.System))
    Assert.Equal(Schema.UpToDate, Schema.prepareFile path "production" (TimeProvider.System))

    use conn = Raw.openFile path
    Assert.Equal<string list>([ "20251212154340" ], Raw.names conn "SELECT version FROM schema_migrations ORDER BY rowid LIMIT 1")
    Assert.Equal<string list>([ Schema.SchemaSha1 ], Raw.names conn "SELECT value FROM ar_internal_metadata WHERE key = 'schema_sha1'")
    Assert.Equal<string list>([ "production" ], Raw.names conn "SELECT value FROM ar_internal_metadata WHERE key = 'environment'")
    // All 15 migrations, newest recorded first (`assume_migrated_upto_version`).
    Assert.Equal(15L, Raw.scalarInt conn "SELECT COUNT(*) FROM schema_migrations")
    Raw.exec conn "INSERT INTO message_search_index(rowid, body) VALUES (1, 'running dogs')"
    Assert.Equal(1L, Raw.scalarInt conn "SELECT rowid FROM message_search_index WHERE body MATCH 'run'") // porter tokenizer

/// The plan SQLite makes now: each call has its own SQL, since a cached EXPLAIN keeps the plan it was prepared with.
let private queryPlan (conn: Microsoft.Data.Sqlite.SqliteConnection) (sql: string) : string =
    use command = conn.CreateCommand()
    command.CommandText <- $"EXPLAIN QUERY PLAN {sql} /* {Guid.NewGuid()} */"
    use reader = command.ExecuteReader()
    [ while reader.Read() do
          yield reader.GetString 3 ]
    |> String.concat "; "

[<Fact>]
let ``prepare adds the room paging index to new and existing databases`` () =
    let lastPage = """SELECT * FROM "messages" WHERE "room_id" = 1 ORDER BY "created_at" DESC LIMIT 40"""
    use dir = new ScratchDir()
    let path = fresh dir
    Schema.prepareFile path "production" TimeProvider.System |> ignore
    (use conn = Raw.openFile path
     let plan = queryPlan conn lastPage
     Assert.True(plan.Contains "index_messages_on_room_id_and_created_at" && not (plan.Contains "TEMP B-TREE"), plan)

     // A database the Rails app created doesn't have it until the app boots on it.
     Raw.exec conn """DROP INDEX "index_messages_on_room_id_and_created_at" """
     Assert.Contains("TEMP B-TREE", queryPlan conn lastPage))
    Assert.Equal(Schema.UpToDate, Schema.prepareFile path "production" TimeProvider.System)
    use conn = Raw.openFile path
    Assert.DoesNotContain("TEMP B-TREE", queryPlan conn lastPage)
    Assert.Equal(1L, Raw.scalarInt conn "SELECT COUNT(*) FROM sqlite_master WHERE name = 'index_users_on_email_address'")

[<Fact>]
let ``prepare reports pending migrations and leaves the file free`` () =
    use dir = new ScratchDir()
    let path = fresh dir
    Schema.prepareFile path "production" TimeProvider.System |> ignore
    (use conn = Raw.openFile path
     Raw.exec conn "DELETE FROM schema_migrations WHERE version = '20251212154340'")
    let error = Assert.Throws<DbException>(fun () -> Schema.prepareFile path "production" TimeProvider.System |> ignore)
    Assert.Contains("pending migrations: 20251212154340", error.Message)
    // Nothing is left holding the file: it can be deleted and prepared again in its place.
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
    for suffix in [ ""; "-wal"; "-shm" ] do
        File.Delete(path + suffix)
    Assert.Equal(Schema.Loaded, Schema.prepareFile path "production" TimeProvider.System)

[<Fact>]
let ``prepare rolls the schema back when it fails partway`` () =
    use dir = new ScratchDir()
    let path = fresh dir
    // A table the schema also creates: loading it fails, and nothing of the load may stay (one transaction).
    (use conn = Raw.openFile path
     Raw.exec conn "CREATE TABLE accounts (id INTEGER PRIMARY KEY)")
    Assert.ThrowsAny<exn>(fun () -> Schema.prepareFile path "production" TimeProvider.System |> ignore) |> ignore
    use conn = Raw.openFile path
    Assert.Equal(0L, Raw.scalarInt conn "SELECT COUNT(*) FROM sqlite_master WHERE name = 'users'")
    Assert.Equal(0L, Raw.scalarInt conn "SELECT COUNT(*) FROM sqlite_master WHERE name = 'schema_migrations'")
