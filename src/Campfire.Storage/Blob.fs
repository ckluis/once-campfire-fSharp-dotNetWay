// Port of rust/crates/storage/src/blob.rs
/// `active_storage_blobs`, `active_storage_attachments` and `active_storage_variant_records` rows.
///
/// On EF Core (design-data-ef.md, unit 1.7): reads take the `CampfireDb`, writes take it too and run inside the caller's
/// `Writes.run` (so callers decide the transaction boundaries the way the Rails models' callbacks would). `created_at` is
/// stamped by `RailsTimestamps`. LINQ with every captured value in `EF.Parameter`; no raw SQL.
namespace Campfire.Storage

open FsToolkit.ErrorHandling
open System
open System.IO
open System.Linq
open Microsoft.EntityFrameworkCore
open Campfire.Db
open Campfire.Rails

type Blob =
    { Id: int64
      Key: string
      Filename: Filename
      ContentType: string option
      /// The `metadata` column, an ordered JSON object (`store :metadata, coder: JSON`).
      Metadata: Value
      ServiceName: string
      ByteSize: int64
      Checksum: string option
      CreatedAt: string }

/// A blob built from uploaded bytes, not yet saved (`Blob.build_after_unfurling`).
type NewBlob =
    { Key: string
      Filename: Filename
      ContentType: string option
      Metadata: Value
      ServiceName: string
      ByteSize: int64
      Checksum: string }

module Blob =
    /// The row as the storage code sees it.
    let ofEntity (b: Ef.Blob) : Blob =
        { Id = b.Id
          Key = b.Key
          Filename = Filename.create b.Filename
          ContentType = Option.ofObj b.ContentType
          Metadata = Option.ofObj b.Metadata |> Option.bind Json.parseInt64Only |> Option.defaultValue (Value.Object [])
          ServiceName = b.ServiceName
          ByteSize = b.ByteSize
          Checksum = Option.ofObj b.Checksum
          CreatedAt = RailsTime.toDb b.CreatedAt }

    let find (db: Ef.CampfireDb) (id: int64) : Blob option =
        db.Blobs.Where(fun b -> b.Id = EF.Parameter id) |> Ef.Data.tryFirst |> Option.map ofEntity

    /// The blob attached to `record` under `name` (`has_one_attached`): the first attachment's, by id.
    let attached (db: Ef.CampfireDb) (recordType: string) (recordId: int64) (name: string) : Blob option =
        Ef.FastReads.attachedBlob db recordType recordId name |> Option.map ofEntity

    let contentType (blob: Blob) : string = defaultArg blob.ContentType ""

    let isImage (blob: Blob) : bool = (contentType blob).StartsWith("image", StringComparison.Ordinal)

    let isVideo (blob: Blob) : bool = (contentType blob).StartsWith("video", StringComparison.Ordinal)

    /// `variable?`
    let isVariable (blob: Blob) : bool = ContentTypes.isVariable (contentType blob)

    /// `previewable?`: only the video previewer can accept in Campfire's image (no poppler or
    /// mupdf binaries are installed, so the PDF previewers never accept).
    let isPreviewable (blob: Blob) : bool = isVideo blob && Process.ffmpegExists ()

    /// `representable?`
    let isRepresentable (blob: Blob) : bool = isVariable blob || isPreviewable blob

    let isAnalyzed (blob: Blob) : bool =
        match blob.Metadata.TryGet "analyzed" with
        | Some Value.Null
        | Some(Value.Bool false)
        | None -> false
        | Some _ -> true

    /// `metadata[:width]`/`[:height]` as the views read them.
    let dimension (name: string) (blob: Blob) : float option =
        match blob.Metadata.TryGet name with
        | Some(Value.Int i) -> Some(float i)
        | Some(Value.Float f) -> Some f
        | _ -> None

    /// `update!(metadata:)`: the blob as saved.
    let updateMetadata (db: Ef.CampfireDb) (metadata: Value) (blob: Blob) : Blob =
        let json: string | null = Json.encode metadata
        let id = blob.Id
        db.Blobs.Where(fun b -> b.Id = EF.Parameter id).ExecuteUpdate(fun u -> u.SetProperty((fun b -> b.Metadata), json) |> ignore)
        |> ignore
        { blob with Metadata = metadata }

    /// `Representable#format`: the filename's extension when Marcel agrees it names the
    /// content type, otherwise the content type's first registered extension.
    let private format (blob: Blob) : string option =
        let extension = Filename.extension blob.Filename
        if extension <> "" && Marcel.forExtension extension = contentType blob then
            Some extension
        else
            Marcel.extensionsOf (contentType blob) |> Array.tryHead

    /// `default_variant_format`: web images keep their format, everything else becomes PNG.
    let defaultVariantFormat (blob: Blob) : string =
        if ContentTypes.isWebImage (contentType blob) then format blob |> Option.defaultValue "png" else "png"

    /// `delete`: the file, plus any legacy untracked variants under `variants/<key>/`.
    let deleteFiles (service: DiskService) (blob: Blob) : StorageResult<unit> =
        result {
            do! DiskService.delete service blob.Key
            if isImage blob then
                do! DiskService.deletePrefixed service $"variants/{blob.Key}/"
        }

    /// Inserts an `active_storage_attachments` row. Rails then touches the record (and whatever it
    /// touches in turn, e.g. `Message belongs_to :room, touch: true`); that's up to the caller.
    let insertAttachment (db: Ef.CampfireDb) (name: string) (recordType: string) (recordId: int64) (blobId: int64) : int64 =
        let attachment: Ef.Attachment =
            Ef.Data.insert
                db
                { Id = 0L
                  Name = name
                  RecordType = recordType
                  RecordId = recordId
                  BlobId = blobId
                  CreatedAt = DateTime.MinValue }
        attachment.Id

    /// The `(record_type, record_id)` of every attachment of a blob: the records `Blob#touch_attachments`
    /// touches after the blob is updated (e.g. by analysis).
    let attachmentRecords (db: Ef.CampfireDb) (blobId: int64) : (string * int64) list =
        db.Attachments.Where(fun a -> a.BlobId = EF.Parameter blobId).OrderBy(fun a -> a.Id).ToList()
        |> Seq.map (fun a -> a.RecordType, a.RecordId)
        |> List.ofSeq

    /// `blob.variant_records.find_by(variation_digest:)`.
    let findVariantRecord (db: Ef.CampfireDb) (blobId: int64) (variationDigest: string) : int64 option =
        db.Variants.Where(fun v -> v.BlobId = EF.Parameter blobId && v.VariationDigest = EF.Parameter variationDigest)
        |> Ef.Data.tryFirst
        |> Option.map (fun v -> v.Id)

    /// `INSERT` half of `create_or_find_by!`: `None` when the record exists already (writes are serialized by the write
    /// gate, so looking first can't race another writer).
    let insertVariantRecord (db: Ef.CampfireDb) (blobId: int64) (variationDigest: string) : int64 option =
        match findVariantRecord db blobId variationDigest with
        | Some _ -> None
        | None ->
            let record: Ef.VariantRecord = Ef.Data.insert db { Id = 0L; BlobId = blobId; VariationDigest = variationDigest }
            Some record.Id

module NewBlob =
    let private contentType (head: byte[]) (filename: Filename) (declaredType: string option) (identify: bool) : string option =
        if declaredType.IsNone || identify then
            Some(Marcel.identify head (Some(Filename.sanitized filename)) declaredType)
        else
            declaredType

    let private build (filename: Filename) (contentType: string option) (serviceName: string) (byteSize: int64) (checksum: string) : NewBlob =
        { Key = Key.generateKey ()
          Filename = filename
          ContentType = contentType
          Metadata = Value.Object [ "identified", Value.Bool true ]
          ServiceName = serviceName
          ByteSize = byteSize
          Checksum = checksum }

    /// `build_after_unfurling(io:, filename:, content_type:, identify:)`: generates the key,
    /// computes the checksum, identifies the content type with Marcel (unless a declared type
    /// is given with `identify: false`) and marks the blob `identified`.
    let unfurl (data: byte[]) (filename: Filename) (declaredType: string option) (serviceName: string) (identify: bool) : NewBlob =
        let contentType = contentType data filename declaredType identify
        build filename contentType serviceName (int64 data.Length) (Key.checksum data)

    /// [`unfurl`] for a file, reading only as much of it as identification needs, and streaming
    /// it through the checksum.
    let unfurlFile
        (path: string)
        (filename: Filename)
        (declaredType: string option)
        (serviceName: string)
        (identify: bool)
        : StorageResult<NewBlob> =
        Io.attempt (fun () ->
            let head =
                use file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ||| FileShare.Delete)
                let buffer = Array.zeroCreate<byte> (Marcel.magicPrefixLen ())
                let mutable filled = 0
                let mutable finished = false
                while not finished && filled < buffer.Length do
                    let read = file.Read(buffer, filled, buffer.Length - filled)
                    if read = 0 then finished <- true else filled <- filled + read
                Array.sub buffer 0 filled
            let contentType = contentType head filename declaredType identify
            let byteSize = FileInfo(path).Length
            build filename contentType serviceName byteSize (Key.checksumFile path))

    let insert (db: Ef.CampfireDb) (blob: NewBlob) : Blob =
        let row: Ef.Blob =
            Ef.Data.insert
                db
                { Id = 0L
                  Key = blob.Key
                  Filename = Filename.raw blob.Filename
                  ContentType = Option.toObj blob.ContentType
                  Metadata = Json.encode blob.Metadata
                  ServiceName = blob.ServiceName
                  ByteSize = blob.ByteSize
                  Checksum = blob.Checksum
                  CreatedAt = DateTime.MinValue }
        Blob.ofEntity row
