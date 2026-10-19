// Port of rust/crates/db/src/models/rich_text_record.rs
// `ActionText::RichText` rows (`action_text_rich_texts`). Campfire has one: `Message#body`. The record is in `Messages.fs`.
namespace Campfire.Db

open System
open System.Linq
open Microsoft.EntityFrameworkCore

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module RichTextRecord =
    let findFor (db: Ef.CampfireDb) (recordType: string) (recordId: int64) (name: string) : RichTextRecord option =
        db.RichTexts.Where(fun r -> r.RecordId = EF.Parameter recordId && r.RecordType = EF.Parameter recordType && r.Name = EF.Parameter name)
        |> Ef.Data.tryFirst

    let create (tx: Ef.Tx) (recordType: string) (recordId: int64) (name: string) (body: string) : RichTextRecord =
        Ef.Data.insert
            tx.Db
            { Id = 0L
              RecordType = recordType
              RecordId = recordId
              Name = name
              Body = body
              CreatedAt = DateTime.MinValue
              UpdatedAt = DateTime.MinValue }

    let updateBody (tx: Ef.Tx) (record: RichTextRecord) (body: string) : RichTextRecord =
        Ef.Data.update tx.Db record { record with Body = body }

    let delete (tx: Ef.Tx) (record: RichTextRecord) : unit = Ef.Data.remove tx.Db record
