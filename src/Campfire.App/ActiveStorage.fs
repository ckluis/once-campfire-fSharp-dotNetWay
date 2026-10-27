// Port of rust/crates/campfire/src/active_storage.rs
//
// The Active Storage endpoints (`activestorage/config/routes.rb`) over `Campfire.Storage`, as the
// engine's controllers serve them, plus `ActiveStorage::Blob#purge` for the purge job.
//
// The controllers are Web actions in `Controllers/ActiveStorage.fs` (unit 2.11); what is here is the media work, the
// attachments' helpers and the purge job.
//
// Downloads stay public behind signed URLs; the disk `PUT` and direct uploads require a Campfire
// session (`reference/config/initializers/active_storage_authentication.rb`). The disk service's
// `show` gets `Cache-Control: max-age=3600, public`
// (`reference/config/initializers/active_storage.rb`). These controllers inherit from
// `ActiveStorage::BaseController` (`protect_from_forgery with: :exception`), not
// `ApplicationController`, so none of Campfire's concerns run.
namespace Campfire.App

open System
open System.Globalization
open System.IO
open System.Threading
open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Campfire.RailsCompat
open Campfire.Ruby
open Campfire.App.Web
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Storage

module ActiveStorage =
    /// The most image and video jobs (variants, previews, analysis) that run at once.
    [<Literal>]
    let private MaxMediaJobs = 4

    /// A storage error inside a database closure (see `Common.storageError`).
    let storageError = Common.storageError

    let private storageToError (error: StorageError) : Error = Internal(Exception(StorageError.message error))

    /// A file as the framework serves it: `TypedResults.PhysicalFile` with range processing (single ranges, 416, HEAD,
    /// `If-Modified-Since`, `If-Range`, `Accept-Ranges`, `Content-Length`). The caller puts the headers the file result does not
    /// write (`Content-Disposition`, `Content-Transfer-Encoding`, `Cache-Control`) on the response first. The framework wants a rooted
    /// path and the storage path may be relative to the working directory (`storage/files`). A client that went away has nothing more
    /// to be told.
    let fileResult (path: string) (contentType: string) (lastModified: DateTimeOffset option) : Falco.HttpHandler =
        fun http ->
            task {
                let result =
                    Microsoft.AspNetCore.Http.TypedResults.PhysicalFile(
                        Path.GetFullPath path,
                        contentType,
                        lastModified = (match lastModified with Some time -> Nullable time | None -> Nullable()),
                        enableRangeProcessing = true
                    )
                try
                    do! result.ExecuteAsync http
                with
                | :? OperationCanceledException
                | :? IOException -> ()
            }

    // --- Media work ------------------------------------------------------------------------------------

    /// Runs libvips, ffmpeg or ffprobe work on the blocking pool, a few jobs at a time: each can take a
    /// lot of memory and CPU (libvips threads its own work), and uploads shouldn't queue behind more of
    /// them than the machine can run at once.
    let private permits = lazy (new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount, 1, MaxMediaJobs)))

    let processMedia (work: unit -> StorageResult<'T>) : Task<Result<'T, Error>> =
        task {
            // The permit goes with the work: a request that gives up (a timeout, a closed connection)
            // doesn't stop the blocking task, so it mustn't free the slot either.
            do! permits.Value.WaitAsync()
            return!
                Task.Run(fun () ->
                    try
                        match work () with
                        | Ok value -> Ok value
                        | Error error -> Error(storageToError error)
                    finally
                        permits.Value.Release() |> ignore)
        }

    /// Keeps a staged file once the write saving its row commits; a rollback drops it instead, which
    /// deletes the file.
    let keepAfterCommit (tx: Ef.Tx) (staged: Staged) : unit =
        tx.AfterCommit(fun _ -> staged.Keep())

    /// `VariantWithRecord#processed` for an already-defaulted variation: the existing variant, or one
    /// transformed (by `transform`) off the writer and then recorded.
    let processedVariantWith
        (app: AppState)
        (blob: Campfire.Storage.Blob)
        (variation: Variation)
        (transform: Storage -> Campfire.Storage.Blob -> Variation -> StorageResult<Staged>)
        : Task<Result<Campfire.Storage.Blob, Error>> =
        task {
            let storage = app.Storage
            match! app.ReadEf(fun db -> Storage.existingVariant db blob variation) with
            | Error e -> return Error e
            | Ok(Some image) -> return Ok image
            | Ok None ->
                match! processMedia (fun () -> transform storage blob variation) with
                | Error e -> return Error e
                | Ok image ->
                    return!
                        app.WriteEf(fun tx ->
                            let db = tx.Db
                            match Storage.recordVariant db blob variation image with
                            | Some recorded ->
                                keepAfterCommit tx image
                                recorded
                            // Another request recorded it first; ours is dropped (and its file deleted).
                            | None ->
                                (image :> IDisposable).Dispose()
                                match Storage.existingVariant db blob variation with
                                | Some existing -> existing
                                | None -> Err.fail (RecordNotFound "ActiveStorage::VariantRecord"))
        }

    let private processedVariant (app: AppState) (blob: Campfire.Storage.Blob) (variation: Variation) =
        processedVariantWith app blob variation (fun storage blob variation -> Storage.transformVariant storage blob variation)

    /// `blob.preview_image`, drawing it with ffmpeg off the writer when it's missing.
    let private previewImage (app: AppState) (blob: Campfire.Storage.Blob) : Task<Result<Campfire.Storage.Blob, Error>> =
        task {
            let storage = app.Storage
            match! app.ReadEf(fun db -> Storage.existingPreviewImage db blob) with
            | Error e -> return Error e
            | Ok(Some image) -> return Ok image
            | Ok None ->
                match! processMedia (fun () -> Storage.drawPreviewImage storage blob) with
                | Error e -> return Error e
                | Ok image ->
                    return!
                        app.WriteEf(fun tx ->
                            let db = tx.Db
                            match Storage.recordPreviewImage db blob image with
                            | Some recorded ->
                                keepAfterCommit tx image
                                recorded
                            | None ->
                                (image :> IDisposable).Dispose()
                                match Storage.existingPreviewImage db blob with
                                | Some existing -> existing
                                | None -> Err.fail (RecordNotFound "ActiveStorage::Blob"))
        }

    /// `blob.preview(transformations).processed`: the preview image itself for empty transformations,
    /// otherwise its processed variant.
    let processedPreview (app: AppState) (blob: Campfire.Storage.Blob) (transformations: Variation) : Task<Result<Campfire.Storage.Blob, Error>> =
        task {
            match! previewImage app blob with
            | Error e -> return Error e
            | Ok image ->
                if Variation.isEmpty transformations then
                    return Ok image
                else
                    match Storage.variationFor image transformations with
                    | Error e -> return Error(storageToError e)
                    | Ok variation -> return! processedVariant app image variation
        }

    /// `blob.representation(variation).processed`, reusing an existing variant or preview.
    let processedRepresentation (app: AppState) (blob: Campfire.Storage.Blob) (variation: Variation) : Task<Result<Campfire.Storage.Blob, Error>> =
        task {
            if Campfire.Storage.Blob.isPreviewable blob then
                return! processedPreview app blob variation
            elif Campfire.Storage.Blob.isVariable blob then
                match Storage.variationFor blob variation with
                | Error e -> return Error(storageToError e)
                | Ok variation -> return! processedVariant app blob variation
            else
                return Error(storageToError (StorageError.Unrepresentable(Campfire.Storage.Blob.contentType blob)))
        }

    /// What `blob.analyze` would save, worked out off the writer.
    let analyzedMetadata (app: AppState) (blob: Campfire.Storage.Blob) : Task<Result<Value, Error>> =
        processMedia (fun () -> Storage.analyzedMetadata app.Storage blob)

    /// Uploads a file to storage for a blob whose row the caller saves next (see `keepAfterCommit`).
    let stageFile (app: AppState) (path: string) (filename: Filename) (contentType: string option) : Task<Result<Staged, Error>> =
        task {
            try
                let! staged = Task.Run(fun () -> Storage.stageFile app.Storage path filename contentType)
                return staged |> Result.mapError storageToError
            with e ->
                return Error(Internal e)
        }

    // --- Direct uploads ----------------------------------------------------------------------------------

    /// A stored `created_at` (`YYYY-MM-DD HH:MM:SS[.ffffff]`, UTC) as `ActiveSupport::JSON` encodes
    /// times: ISO 8601 with milliseconds.
    let jsonTime (dbTime: string) : string =
        let formats = [| "yyyy-MM-dd HH:mm:ss.FFFFFFF"; "yyyy-MM-dd HH:mm:ss" |]
        match DateTime.TryParseExact(dbTime, formats, CultureInfo.InvariantCulture, DateTimeStyles.None) with
        | true, time -> time.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
        | _ -> dbTime

    // --- Purging ---------------------------------------------------------------------------------------

    /// Deletes the attachment row, returning its blob id.
    let private destroyAttachment (db: Ef.CampfireDb) (recordType: string) (recordId: int64) (name: string) : int64 option =
        match
            db.Attachments.Where(fun a -> a.RecordType = EF.Parameter recordType && a.RecordId = EF.Parameter recordId && a.Name = EF.Parameter name)
            |> Ef.Data.tryFirst
        with
        | None -> None
        | Some attachment ->
            let id = attachment.Id
            db.Attachments.Where(fun a -> a.Id = EF.Parameter id).ExecuteDelete() |> ignore
            Some attachment.BlobId

    let private deleteFiles (storage: Storage) (blob: Campfire.Storage.Blob) : Task<Result<unit, string>> =
        task {
            try
                match! Task.Run(fun () -> Storage.deleteFiles storage blob) with
                | Ok() -> return Ok()
                | Error error -> return Error(StorageError.message error)
            with e ->
                return Error e.Message
        }

    /// `ActiveStorage::Blob#purge`: `destroy` (refused while any attachment still points at the blob;
    /// destroys its variant records and preview image attachment, whose blobs are purged later), then
    /// delete the files.
    let purge (app: AppState) (blobId: int64) : Task<Result<unit, string>> =
        task {
            use context = app.DbFactory.CreateDbContext()
            let! destroyed =
                Ef.Writes.run app.Gate app.Env context (fun tx ->
                    let db = tx.Db
                    match Campfire.Storage.Blob.find db blobId with
                    | None -> None
                    | Some blob ->
                        // before_destroy(prepend: true) { raise ActiveRecord::InvalidForeignKey if attachments.exists? }
                        if not (Campfire.Storage.Blob.attachmentRecords db blobId).IsEmpty then
                            None
                        else
                            let dependents = ResizeArray<int64>()
                            // before_destroy { variant_records.destroy_all }: each record's image attachment goes too.
                            let variantRecords = db.Variants.Where(fun v -> v.BlobId = EF.Parameter blobId).Select(fun v -> v.Id).ToList()
                            for recordId in variantRecords do
                                destroyAttachment db "ActiveStorage::VariantRecord" recordId "image" |> Option.iter dependents.Add
                                db.Variants.Where(fun v -> v.Id = EF.Parameter recordId).ExecuteDelete() |> ignore
                            // has_one_attached :preview_image (dependent: :destroy on the attachment)
                            destroyAttachment db "ActiveStorage::Blob" blobId "preview_image" |> Option.iter dependents.Add
                            db.Blobs.Where(fun b -> b.Id = EF.Parameter blobId).ExecuteDelete() |> ignore
                            // after_destroy_commit :purge_dependent_blob_later
                            for dependent in dependents do
                                tx.EmitAfterCommit(Event.PurgeBlob dependent)
                            Some blob)
            match destroyed with
            | Error error -> return Error(DbError.display error)
            | Ok(Some blob) -> return! deleteFiles app.Storage blob
            | Ok None -> return Ok()
        }
