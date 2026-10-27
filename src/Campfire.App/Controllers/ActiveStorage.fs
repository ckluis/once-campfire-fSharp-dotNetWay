// Port of rust/crates/campfire/src/active_storage.rs (the controllers)
//
// The Active Storage endpoints (`activestorage/config/routes.rb`) over `Campfire.Storage`, as the engine's controllers serve
// them, now Web actions (unit 2.11). Downloads stay public behind signed URLs; the disk `PUT` and direct uploads require a
// Campfire session (`reference/config/initializers/active_storage_authentication.rb`: `AuthenticateAsync "Session"` or 401, without
// resuming it, as Rails' `find_session_by_cookie` does not). The disk service's `show` gets `Cache-Control: max-age=3600, public`
// (`reference/config/initializers/active_storage.rb`). These controllers inherit from `ActiveStorage::BaseController`
// (`protect_from_forgery with: :exception`), not `ApplicationController`, so none of Campfire's other concerns run.
namespace Campfire.App.Controllers

open System
open System.Globalization
open System.IO
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Falco
open Microsoft.AspNetCore.Http
open Campfire.Rails
open Campfire.Rails.Ruby
open Campfire.App
open Campfire.Db
open Campfire.Storage
open Campfire.App.Web

module ActiveStorageControllers =
    /// `ActiveStorage.service_urls_expire_in`
    [<Literal>]
    let private ServiceUrlsExpireIn = 300L

    /// `http_cache_forever`: `expires_in 100.years`.
    [<Literal>]
    let private HundredYears = OutputCaching.HundredYears

    let private storageToError (error: StorageError) : Web.Error = Internal(Exception(StorageError.message error))

    let private statusError (code: int) : Web.Error = Web.Error.Status code

    let private header (http: HttpContext) (name: string) : string option =
        match http.Request.Headers[name].ToString() with
        | "" -> None
        | value -> Some value

    // --- Blobs -----------------------------------------------------------------------------------------

    /// `ActiveStorage::SetBlob#set_blob`: `Blob.find_signed!(params[:signed_blob_id] || params[:signed_id])`.
    /// A bad signature is `head :not_found`; a valid one for a missing blob is `RecordNotFound`.
    let private setBlob (http: HttpContext) : Result<Campfire.Storage.Blob, Web.Error> =
        let signedId =
            match Params.value http "signed_blob_id" with
            | null ->
                match Params.value http "signed_id" with
                | null -> ""
                | id -> id
            | id -> id
        let storage = (Data.app http).Storage
        match Paths.verifySignedBlobId storage.Verifier signedId (Data.now http) with
        | None -> Action.halt (Before.head 404)
        | Some blobId ->
            match Data.read http (fun db -> Campfire.Storage.Blob.find db blobId) with
            | Ok(Some blob) -> Ok blob
            | Ok None -> Result.Error NotFound
            | Result.Error e -> Result.Error e

    /// `blob.url(disposition:)` on the disk service: a signed `/rails/active_storage/disk/...` URL on
    /// this request's host that expires in `service_urls_expire_in`.
    let private blobUrl (http: HttpContext) (blob: Campfire.Storage.Blob) (disposition: string option) : string =
        let storage = (Data.app http).Storage
        let contentType = ContentTypes.forServing (Campfire.Storage.Blob.contentType blob)
        let disposition =
            ContentTypes.forcedDisposition (Campfire.Storage.Blob.contentType blob)
            |> Option.orElse disposition
            |> Option.defaultValue "inline"
        let expiresAt = (Data.now http).AddSeconds(float ServiceUrlsExpireIn)
        let path = DiskService.urlPath storage.Service storage.Verifier blob.Key (Some expiresAt) blob.Filename (Some contentType) disposition
        Respond.urlFor http path

    let private disposition (http: HttpContext) : string option =
        match Params.value http "disposition" with
        | null -> None
        | value -> Some value

    /// `request.fullpath`
    let private fullpath (http: HttpContext) : string =
        match http.Request.QueryString.Value with
        | null
        | ""
        | "?" -> Respond.rawPath http
        | query -> Respond.rawPath http + query

    /// `http_cache_forever(public: true)`: cache for 100 years, ETag on the full path, and a fixed
    /// Last-Modified. The 304 handler when the client's copy is fresh.
    let private httpCacheForever (http: HttpContext) : HttpHandler voption =
        Respond.expiresIn http HundredYears { ExpiresIn.Default with Public = true; Immutable = true }
        let lastModified = (Timestamps.tryParse "2011-01-01T00:00:00Z").Value
        Respond.freshWhen http { Freshness.Default with Etag = fullpath http; LastModified = ValueSome lastModified; Public = true }

    /// `send_blob_stream(blob, disposition:)` and `send_blob_byte_range_data`: the blob's file as the framework's file result, which
    /// answers the request's `Range` (C3). A ranged answer ignores the query's disposition, as Rails' proxy controller does. The headers it does not write are set first, as Puma writes them: the disposition comes from
    /// the query and may hold a line break.
    let private sendBlob (http: HttpContext) (blob: Campfire.Storage.Blob) (disposition: string option) (ranged: bool) : Result<HttpHandler, Web.Error> =
        let path = Storage.pathFor (Data.app http).Storage blob
        if not (File.Exists path) then
            if ranged then
                Result.Error(storageToError StorageError.FileNotFound)
            else
                // `rescue ActiveStorage::FileNotFoundError`: expires_now, head :not_found.
                http.Response.Headers.CacheControl <- "no-cache"
                Ok(Respond.head http 404)
        else
            let disposition =
                ContentTypes.forcedDisposition (Campfire.Storage.Blob.contentType blob)
                |> Option.orElse disposition
                |> Option.defaultValue "inline"
            let headers = HeaderMap()
            headers.SetPumaValue("content-disposition", ContentDisposition.format disposition (Filename.sanitized blob.Filename))
            headers.SetPumaValue("content-transfer-encoding", "binary")
            Outgoing.applyHeaders http headers
            Ok(ActiveStorage.fileResult path (ContentTypes.forServing (Campfire.Storage.Blob.contentType blob)) None)

    /// `ActiveStorage::Blobs::RedirectController#show`
    let blobsRedirect: Web.Action =
        fun http ->
            taskResult {
                do! Csrf.verifyAuthenticityToken http
                let! blob = setBlob http
                Respond.expiresIn http (uint64 ServiceUrlsExpireIn) ExpiresIn.Default
                let url = blobUrl http blob (disposition http)
                return! Respond.redirectToWith http url { RedirectOptions.Default with AllowOtherHost = true }
            }

    /// `ActiveStorage::Blobs::ProxyController#show`
    let blobsProxy: Web.Action =
        fun http ->
            taskResult {
                Native.useLiveResponse http // `include ActiveStorage::Streaming`
                do! Csrf.verifyAuthenticityToken http
                let! blob = setBlob http
                let disposition = disposition http
                match header http "range" with
                | Some range when not (String.IsNullOrWhiteSpace range) -> return! sendBlob http blob None true
                | _ ->
                    match httpCacheForever http with
                    | ValueSome notModified -> return notModified
                    | ValueNone -> return! sendBlob http blob disposition false
            }

    // --- Representations -------------------------------------------------------------------------------

    /// `set_representation`: `@blob.representation(params[:variation_key]).processed`. A bad variation key
    /// is `head :not_found`. Returns the blob that represents it (the variant's or preview's image).
    let private setRepresentation (http: HttpContext) (blob: Campfire.Storage.Blob) : Task<Result<Campfire.Storage.Blob, Web.Error>> =
        task {
            let app = Data.app http
            let key = match Params.value http "variation_key" with null -> "" | key -> key
            match Variation.decode app.Storage.Verifier key (Data.now http) with
            | Ok variation ->
                let! image = ActiveStorage.processedRepresentation app blob variation
                return image
            | Result.Error StorageError.InvalidSignature -> return Action.halt (Before.head 404)
            | Result.Error error -> return Result.Error(storageToError error)
        }

    /// `ActiveStorage::Representations::RedirectController#show`
    let representationsRedirect: Web.Action =
        fun http ->
            taskResult {
                do! Csrf.verifyAuthenticityToken http
                let! blob = setBlob http
                let! image = setRepresentation http blob
                Respond.expiresIn http (uint64 ServiceUrlsExpireIn) ExpiresIn.Default
                let url = blobUrl http image (disposition http)
                return! Respond.redirectToWith http url { RedirectOptions.Default with AllowOtherHost = true }
            }

    /// `ActiveStorage::Representations::ProxyController#show`
    let representationsProxy: Web.Action =
        fun http ->
            taskResult {
                Native.useLiveResponse http // `include ActiveStorage::Streaming`
                do! Csrf.verifyAuthenticityToken http
                let! blob = setBlob http
                let! image = setRepresentation http blob
                match httpCacheForever http with
                | ValueSome notModified -> return notModified
                | ValueNone ->
                    // Rails streams a representation whole: it never answers a `Range`.
                    http.Request.Headers.Remove "Range" |> ignore
                    return! sendBlob http image (disposition http) false
            }

    // --- Disk service ----------------------------------------------------------------------------------

    let private diskServe (http: HttpContext) : Result<HttpHandler, Web.Error> =
        let storage = (Data.app http).Storage
        let encodedKey = match Params.value http "encoded_key" with null -> "" | key -> key
        match Disk.decodeVerifiedKey storage.Verifier encodedKey (Data.now http) with
        | None -> Ok(Respond.head http 404)
        | Some key ->
            let path = DiskService.pathFor storage.Service key.Key
            if not (File.Exists path) then
                Ok(Respond.head http 404)
            else
                // `serve_file(path, content_type:, disposition:)`: the key's own values, the type `application/octet-stream`
                // and `attachment` when it has none. Disk keys have no extension, so the file result takes the type as it is.
                let headers = HeaderMap()
                headers.SetPumaValue("content-disposition", key.Disposition)
                Outgoing.applyHeaders http headers
                Ok(ActiveStorage.fileResult path (defaultArg key.ContentType "application/octet-stream") (Some(DateTimeOffset(File.GetLastWriteTimeUtc path))))

    /// `ActiveStorage::DiskController#show`, plus the initializer's `after_action` cache header.
    let diskShow: Web.Action =
        fun http ->
            Task.FromResult(
                diskServe http
                |> Result.map (fun handler ->
                    fun (http: HttpContext) ->
                        http.Response.Headers.CacheControl <- $"max-age={OutputCaching.DiskMaxAge}, public"
                        handler http)
            )

    /// `ActiveStorageAuthentication#require_active_storage_authentication`: 401 without a session.
    let private requireActiveStorageAuthentication (http: HttpContext) : Task<Result<unit, Web.Error>> =
        task {
            match! Authentication.session http with
            | Result.Error e -> return Result.Error e
            | Ok None -> return Action.halt (Before.head 401)
            | Ok(Some _) -> return Ok()
        }

    /// `token[:content_type] == request.content_mime_type && token[:content_length] == request.content_length`
    let private acceptableContent (http: HttpContext) (token: DiskToken) : bool =
        let mediaType =
            match http.Request.ContentType with
            | null -> None
            | contentType ->
                let media = (match contentType.IndexOf ';' with -1 -> contentType | i -> contentType.Substring(0, i)).Trim().ToLowerInvariant()
                if media = "" then None else Some media
        let contentLength =
            match header http "content-length" with
            | None -> None
            | Some length ->
                match Int64.TryParse(length.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) with
                | true, n -> Some n
                | _ -> None
        (token.ContentType |> Option.map (fun t -> t.ToLowerInvariant())) = mediaType && Some token.ContentLength = contentLength

    /// `ActiveStorage::DiskController#update` (the direct-upload PUT), behind `require_active_storage_authentication`.
    let diskUpdate: Web.Action =
        fun http ->
            taskResult {
                do! requireActiveStorageAuthentication http
                let storage = (Data.app http).Storage
                let encodedToken = match Params.value http "encoded_token" with null -> "" | token -> token
                match Disk.decodeVerifiedToken storage.Verifier encodedToken (Data.now http) with
                | None -> return Respond.head http 404
                | Some token ->
                    if not (acceptableContent http token) then
                        return Respond.head http 422
                    else
                        use body = new MemoryStream()
                        do! http.Request.Body.CopyToAsync body
                        let bytes = body.ToArray()
                        let! uploaded =
                            Task.Run(fun () ->
                                use stream = new MemoryStream(bytes, false)
                                DiskService.upload storage.Service token.Key stream (Some token.Checksum))
                            |> Task.map Ok
                        match uploaded with
                        | Ok() -> return Respond.head http 204
                        | Result.Error StorageError.Integrity -> return Respond.head http 422
                        | Result.Error error -> return! Result.Error(storageToError error)
            }

    // --- Direct uploads ----------------------------------------------------------------------------------

    /// `blob.as_json(root: false, methods: :signed_id).merge(direct_upload: { url:, headers: })`
    let private directUploadJson (blob: Campfire.Storage.Blob) (signedId: string) (url: string) (contentType: string option) : string =
        let text (value: string option) = match value with Some value -> Value.String value | None -> Value.Null
        Json.encode (
            Value.Object
                [ "id", Value.Int blob.Id
                  "key", Value.String blob.Key
                  "filename", Value.String(Filename.raw blob.Filename)
                  "content_type", text blob.ContentType
                  "metadata", blob.Metadata
                  "service_name", Value.String blob.ServiceName
                  "byte_size", Value.Int blob.ByteSize
                  "checksum", text blob.Checksum
                  "created_at", Value.String(ActiveStorage.jsonTime blob.CreatedAt)
                  "signed_id", Value.String signedId
                  "direct_upload",
                  Value.Object [ "url", Value.String url; "headers", Value.Object [ "Content-Type", text contentType ] ] ]
        )

    /// `ActiveStorage::DirectUploadsController#create`, behind CSRF and `require_active_storage_authentication`.
    let directUploadsCreate: Web.Action =
        fun http ->
            taskResult {
                do! Csrf.verifyAuthenticityToken http
                do! requireActiveStorageAuthentication http
                // `params.expect(blob: [:filename, :byte_size, :checksum, :content_type, metadata: {}])`
                let! (_: Scope) = Params.require http "blob"
                // Strings, and numbers as their text: Active Storage's JavaScript sends `byte_size` as a number.
                let text (key: string) : string option =
                    match Params.nestedText http "blob" key with
                    | null -> None
                    | value -> Some value
                match text "filename" |> Option.filter (fun f -> f <> ""), text "checksum" |> Option.filter (fun c -> c <> "") with
                | Some filename, Some checksum ->
                    // Stricter than Rails, whose attribute cast makes a byte size that isn't a number 0 (an
                    // upload only an empty file could fill): it's refused.
                    match text "byte_size" |> Option.bind Ruby.integerCast with
                    | None -> return! Result.Error(statusError 422)
                    // The upload's PUT body is read into memory, so it's capped like other bodies: don't hand out
                    // a URL for more than it will accept. (Campfire's editor only attaches mentions and embeds;
                    // files go up with the message form.)
                    | Some byteSize when byteSize < 0L || byteSize > Edge.MaxRequestBody -> return! Result.Error(statusError 413)
                    | Some byteSize ->
                        let storage = (Data.app http).Storage
                        let contentType = text "content_type"
                        let metadata = Params.hashJson http "blob" "metadata" |> Option.defaultValue (Value.Object [])
                        let now = Data.now http
                        let newBlob: NewBlob =
                            { Key = Key.generateKey ()
                              Filename = Filename.create filename
                              ContentType = contentType
                              Metadata = metadata
                              ServiceName = storage.Service.Name
                              ByteSize = byteSize
                              Checksum = checksum }
                        let! (blob: Campfire.Storage.Blob) = Data.write http (fun tx -> NewBlob.insert tx.Db newBlob)
                        let expiresAt = now.AddSeconds(float ServiceUrlsExpireIn)
                        let url =
                            Respond.urlFor
                                http
                                (DiskService.urlPathForDirectUpload storage.Service storage.Verifier blob.Key expiresAt contentType byteSize checksum)
                        let signedId = Paths.signedBlobId storage.Verifier blob.Id None
                        let json = directUploadJson blob signedId url contentType
                        return Pages.renderText http 200 Pages.JsonUtf8 json
                | _ -> return! Result.Error(statusError 422)
            }
