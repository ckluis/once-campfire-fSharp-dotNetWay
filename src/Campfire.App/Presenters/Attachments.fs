// Port of `attached_blob` in rust/crates/campfire/src/controllers/presenters/attachments.rs
//
// `has_one_attached` as `User::Avatar` (`:avatar`) and `Account` (`:logo`) use it, over
// `Campfire.Storage`. The rest of the file (assigning an uploaded file inside a record's save) follows
// with the controllers that need it.
namespace Campfire.App.Presenters

open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Campfire.Db

module Attachments =
    /// `record.<name>.attached?`'s blob: the attachment's blob, if any.
    let attachedBlob (conn: Conn) (recordType: string) (recordId: int64) (name: string) : Campfire.Storage.Blob option =
        Common.raiseStorage (Campfire.Storage.Blob.attached conn.Raw recordType recordId name)

    /// `attachedBlob` on EF's context (unit 1.7 moves the storage rows onto it): the context's own connection, opened for the
    /// call, so the read sees what the context's transaction sees.
    let attachedBlobOn (db: Ef.CampfireDb) (recordType: string) (recordId: int64) (name: string) : Campfire.Storage.Blob option =
        db.Database.OpenConnection()
        try
            match db.Database.GetDbConnection() with
            | :? SqliteConnection as connection ->
                Common.raiseStorage (Campfire.Storage.Blob.attached connection recordType recordId name)
            | other -> invalidOp $"attachedBlobOn needs a SQLite connection, not {other.GetType().Name}"
        finally
            db.Database.CloseConnection()
