// Port of rust/crates/campfire/src/controllers/presenters/attachments.rs, all but `attached_blob`
// (which the layout needs, so it is in `Attachments.fs`, earlier in the project; this part needs
// `ActiveStorage`, which comes after the concerns).
//
// `has_one_attached` as `User::Avatar` (`:avatar`) and `Account` (`:logo`) use it, over
// `Campfire.Storage` (reference/app/models/user/avatar.rb, account.rb, and Active Storage's
// `Attached::Changes::CreateOne` / `Attachment`).
//
// Assigning an uploaded file and saving the record, in the record's transaction:
// the old attachment is destroyed first (`has_one ... dependent: :destroy` replacing its target;
// its blob is purged after commit, `dependent: :purge_later`), then the new blob and attachment
// rows are inserted, and each attachment change touches the record
// (`belongs_to :record, touch: true`). Since a fresh blob isn't analyzed, `ActiveStorage::AnalyzeJob`
// runs after commit (analysis touches the record again).
//
// Unlike Rails, which uploads after commit, the file is uploaded before the transaction
// (`Assignment.stage`), so the writer never waits on copying and checksumming it; a transaction that
// rolls back deletes it again.
namespace Campfire.App.Presenters

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Campfire.Db
open Campfire.App.Web
open Campfire.Storage
open Campfire.App

/// An uploaded file (`ActionDispatch::Http::UploadedFile`), still in its tempfile (`Path`), which the response deletes when it
/// completes.
type Upload =
    { Path: string
      Filename: string
      ContentType: string option }

module Upload =
    /// Uploads the file to storage for a blob whose row is saved next, off the async threads.
    let stage (app: AppState) (upload: Upload) : Task<Result<Staged, Error>> =
        ActiveStorage.stageFile app upload.Path (Filename upload.Filename) upload.ContentType

/// What `record.avatar = value` does with a permitted param value: `Create` holds the `Upload`,
/// then, once staged, the `Staged` blob.
type Assignment<'U> =
    /// The key wasn't given.
    | Unchanged
    /// `nil` or `""`: `Attached::Changes::DeleteOne` (the attachment is destroyed on save).
    | Delete
    /// An uploaded file: `Attached::Changes::CreateOne`.
    | Create of 'U
    /// Anything else (e.g. a plain string that isn't a signed blob id): Rails raises.
    | Invalid

module Assignment =
    /// Uploads a new file, so the save only has rows to write.
    let stage (app: AppState) (assignment: Assignment<Upload>) : Task<Result<Assignment<Staged>, Error>> =
        task {
            match assignment with
            | Unchanged -> return Ok Unchanged
            | Delete -> return Ok Delete
            | Invalid -> return Ok Invalid
            | Create upload ->
                match! Upload.stage app upload with
                | Ok staged -> return Ok(Create staged)
                | Error e -> return Error e
        }

/// A blob inserted for an attachment, to analyze after commit.
type Pending = { Blob: Campfire.Storage.Blob }

/// The record an attachment belongs to: its polymorphic type and its table.
type Record =
    { RecordType: string
      Table: string
      Id: int64 }

module Record =
    let user (id: int64) : Record = { RecordType = "User"; Table = "users"; Id = id }

    let account (id: int64) : Record = { RecordType = "Account"; Table = "accounts"; Id = id }

module AttachmentWrites =
    /// `record.<name>.destroy` (the attachment): delete it, touch the record, and purge its blob
    /// after commit. Nothing happens without an attachment (`delegate_missing_to :attachment, allow_nil: true`).
    let destroy (tx: Ef.Tx) (record: Record) (name: string) : bool =
        let db = tx.Db
        let recordType, recordId = record.RecordType, record.Id
        let attachment =
            db.Attachments.Where(fun a -> a.RecordType = EF.Parameter recordType && a.RecordId = EF.Parameter recordId && a.Name = EF.Parameter name)
            |> Ef.Data.tryFirst
        match attachment with
        | None -> false
        | Some attachment ->
            let attachmentId = attachment.Id
            db.Attachments.Where(fun a -> a.Id = EF.Parameter attachmentId).ExecuteDelete() |> ignore
            Common.touchRecord tx record.RecordType record.Id
            tx.EmitAfterCommit(Event.PurgeBlob attachment.BlobId)
            true

    /// `record.<name> = uploaded_file; record.save`: replaces any current attachment.
    let attach (tx: Ef.Tx) (record: Record) (name: string) (staged: Staged) : Pending =
        destroy tx record name |> ignore
        let blob = staged.Insert tx.Db
        ActiveStorage.keepAfterCommit tx staged
        Campfire.Storage.Blob.insertAttachment tx.Db name record.RecordType record.Id blob.Id |> ignore
        Common.touchRecord tx record.RecordType record.Id
        { Blob = blob }

    /// Applies an assignment inside the record's save. Returns the blob to analyze after commit.
    let assign (tx: Ef.Tx) (record: Record) (name: string) (assignment: Assignment<Staged>) : Pending option =
        match assignment with
        | Unchanged -> None
        | Delete ->
            destroy tx record name |> ignore
            None
        | Create staged -> Some(attach tx record name staged)
        | Invalid -> Err.fail (DbError.other "Could not find or build blob: expected attachable")

    /// `ActiveStorage::AnalyzeJob`: `blob.analyze`, then `touch_attachment_records`. The file is
    /// analyzed off the writer; only the metadata update and touches run on it.
    let analyze (app: AppState) (blobId: int64) : Task<Result<unit, string>> =
        task {
            use context = app.DbFactory.CreateDbContext()
            match Ef.Reads.run context (fun db -> Campfire.Storage.Blob.find db blobId) with
            | Error error -> return Error(DbError.display error)
            | Ok None -> return Ok()
            | Ok(Some blob) ->
                match! ActiveStorage.analyzedMetadata app blob with
                | Error e -> return Error(sprintf "%A" e)
                | Ok metadata ->
                    let! written =
                        Ef.Writes.run app.Gate app.Env context (fun tx ->
                            Campfire.Storage.Blob.updateMetadata tx.Db metadata blob |> ignore
                            for (recordType, recordId) in Campfire.Storage.Blob.attachmentRecords tx.Db blobId do
                                Common.touchRecord tx recordType recordId)
                    return written |> Result.mapError DbError.display
        }

    /// After commit: `analyze_blob_later` (the file was uploaded before the save).
    let analyzeLater (app: AppState) (pending: Pending option) : unit =
        match pending with
        | None -> ()
        | Some { Blob = blob } -> app.Jobs.PerformLater("ActiveStorage::AnalyzeJob", fun () -> analyze app blob.Id)

    /// `record.<name>.variant(name).processed if record.<name>.variable?`: the processed variant's
    /// blob, or `None` when there's no attachment or it can't be transformed.
    let processedVariant (app: AppState) (record: Record) (name: string) (transformations: Variation) : Task<Result<Campfire.Storage.Blob option, Error>> =
        task {
            match! app.ReadEf(fun db -> Attachments.attachedBlob db record.RecordType record.Id name) with
            | Error e -> return Error e
            | Ok(Some blob) when Campfire.Storage.Blob.isVariable blob ->
                match! ActiveStorage.processedRepresentation app blob transformations with
                | Ok variant -> return Ok(Some variant)
                | Error e -> return Error e
            | Ok _ -> return Ok None
        }

/// Uploads as a Web action reads them (unit 2.10): from the framework's `Request.Form.Files`.
module Uploads =
    /// What `record.avatar = params[:user][:avatar]` gets: an uploaded file is `Create` (copied to a temp file that is deleted when
    /// the response completes), `""` is `Delete`, a key that was not given is `Unchanged`, and any other value is not a file.
    /// The second value is whether the key was given at all (a file, or any non-nil value).
    let assignmentNamed
        (http: Microsoft.AspNetCore.Http.HttpContext)
        (fileName: string)
        (value: Campfire.App.Web.ParamValue)
        : Assignment<Upload> * bool =
        let file = if http.Request.HasFormContentType then http.Request.Form.Files.GetFile fileName else null
        match file with
        | null ->
            match value with
            | Campfire.App.Web.Absent -> Unchanged, false
            | Campfire.App.Web.Str "" -> Delete, true
            | Campfire.App.Web.Str _
            | Campfire.App.Web.Structured -> Invalid, true
        | file ->
            // A new temp file named as Rack names them (`RackMultipart...`).
            let path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RackMultipart" + Guid.NewGuid().ToString("N"))
            use target = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None)
            use source = file.OpenReadStream()
            source.CopyTo target
            target.Dispose()
            http.Response.OnCompleted(fun () ->
                (try
                    System.IO.File.Delete path
                 with
                 | :? System.IO.IOException
                 | :? UnauthorizedAccessException -> ())
                Task.CompletedTask)
            Create { Path = path; Filename = file.FileName; ContentType = Some file.ContentType }, true

    /// `record.avatar = params[:user][:avatar]` for the nested name `scope[key]`.
    let assignment (http: Microsoft.AspNetCore.Http.HttpContext) (scope: Campfire.App.Web.Scope) (key: string) : Assignment<Upload> * bool =
        assignmentNamed http $"{scope.Name}[{key}]" (scope.Get key)

    /// `Assignment.stage` with the failure as a Web error.
    let stage (http: Microsoft.AspNetCore.Http.HttpContext) (assignment: Assignment<Upload>) =
        task {
            let app = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AppState>(http.RequestServices)
            let! staged = Assignment.stage app assignment
            return staged
        }
