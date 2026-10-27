namespace Campfire.Storage.Tests

open System
open System.IO
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Xunit
open Campfire.Db.Ef
open Campfire.Storage

// The timing tests measure the process they run in, so the assembly's tests run one at a time.
[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()

[<AutoOpen>]
module Support =
    type Result<'a, 'e> with
        /// The `Ok` value (Rust's `unwrap`); anything else fails the test with the error.
        member this.Value : 'a =
            match this with
            | Ok value -> value
            | Error error -> failwith $"{error}"

    /// The three Active Storage tables, as the reference's schema.rb makes them.
    let schema =
        """
CREATE TABLE active_storage_attachments (id integer PRIMARY KEY AUTOINCREMENT NOT NULL, blob_id bigint NOT NULL, created_at datetime(6) NOT NULL, name varchar NOT NULL, record_id bigint NOT NULL, record_type varchar NOT NULL);
CREATE UNIQUE INDEX index_active_storage_attachments_uniqueness ON active_storage_attachments (record_type, record_id, name, blob_id);
CREATE TABLE active_storage_blobs (id integer PRIMARY KEY AUTOINCREMENT NOT NULL, byte_size bigint NOT NULL, checksum varchar, content_type varchar, created_at datetime(6) NOT NULL, filename varchar NOT NULL, key varchar NOT NULL, metadata text, service_name varchar NOT NULL);
CREATE UNIQUE INDEX index_active_storage_blobs_on_key ON active_storage_blobs (key);
CREATE TABLE active_storage_variant_records (id integer PRIMARY KEY AUTOINCREMENT NOT NULL, blob_id bigint NOT NULL, variation_digest varchar NOT NULL);
CREATE UNIQUE INDEX index_active_storage_variant_records_uniqueness ON active_storage_variant_records (blob_id, variation_digest);
"""

    /// A clock fixed at 2026-09-26 12:00:00 UTC, as EF's `TimeProvider`.
    let fixedTime () : TimeProvider =
        Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero)) :> TimeProvider

    /// The context over a file holding them, in a directory of its own that goes with the context.
    type TestDb private (options: DbContextOptions<CampfireDb>, dir: TempDir) =
        inherit CampfireDb(options)

        static member Open() : TestDb =
            // Test setup writes straight through the context, outside any Writes.run: exempt this test's flow (never disposed).
            Campfire.Db.WriteGate.Exempt() |> ignore
            let dir = TempDir.Create()
            let path = Path.Combine(dir.Path, "test.sqlite3")
            do
                use conn = new SqliteConnection($"Data Source={path};Pooling=False")
                conn.Open()
                use command = conn.CreateCommand()
                command.CommandText <- schema
                command.ExecuteNonQuery() |> ignore
            new TestDb(Data.options path (fixedTime ()) ignore, dir)

        override this.Dispose() =
            base.Dispose()
            SqliteConnection.ClearAllPools()
            (dir :> IDisposable).Dispose()

    /// A database holding the Active Storage tables, read and written through EF.
    let openDatabase () : TestDb = TestDb.Open()
