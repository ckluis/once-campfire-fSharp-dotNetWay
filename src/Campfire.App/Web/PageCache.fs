// plans/design-perf.md section 1 P6-P12, section 3: upstream's `cache_read_response`, in the actions, on HybridCache.
//
// A hit runs everything a miss runs before the lookup (the before-actions, `set_room`), then writes the kept bytes. The
// lookup is a read that never calls a factory; the store is `Pages.send`'s `capture`, after a kept compressed page went out.
namespace Campfire.App.Web

open System
open System.Buffers
open System.IO
open System.Security.Cryptography
open System.Text
open System.Threading
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.ResponseCompression
open Microsoft.Extensions.Caching.Hybrid
open Microsoft.Extensions.DependencyInjection
open Campfire.App
open Campfire.Db
open Campfire.Views

/// What a hit writes: the content encoding, the kept bytes, the content type and Rails' cache headers (never `Set-Cookie`), one
/// pair per header value.
[<Sealed; AllowNullLiteral; System.ComponentModel.ImmutableObject(true)>]
type PageEntry(encoding: string, body: byte[], contentType: string, headers: struct (string * string)[]) =
    member _.Encoding: string = encoding
    member _.Body: byte[] = body
    member _.ContentType: string = contentType
    member _.Headers: struct (string * string)[] = headers

/// Lets HybridCache measure an entry (its serialized length is the entry's size against the memory cache's `SizeLimit`); the local
/// cache keeps the instance itself, so this runs for the size and never for a hit.
[<Sealed>]
type PageEntrySerializer() =
    interface IHybridCacheSerializer<PageEntry> with
        member _.Deserialize(source: ReadOnlySequence<byte>) =
            use reader = new BinaryReader(new MemoryStream(source.ToArray()), Encoding.UTF8)
            let encoding = reader.ReadString()
            let contentType = reader.ReadString()
            let headers = Array.init (reader.ReadInt32()) (fun _ -> struct (reader.ReadString(), reader.ReadString()))
            PageEntry(encoding, reader.ReadBytes(reader.ReadInt32()), contentType, headers)

        member _.Serialize(value: PageEntry, target: IBufferWriter<byte>) =
            use buffer = new MemoryStream()
            (use writer = new BinaryWriter(buffer, Encoding.UTF8, true)
             writer.Write value.Encoding
             writer.Write value.ContentType
             writer.Write value.Headers.Length
             for struct (name, v) in value.Headers do
                 writer.Write name
                 writer.Write v
             writer.Write value.Body.Length
             writer.Write value.Body)
            target.Write(ReadOnlySpan<byte>(buffer.GetBuffer(), 0, int buffer.Length))

/// What the cache did, for the tests (PC1-PC17) and `/up`-style inspection: lookups made, hits served, entries stored. `AfterLookup`
/// is a test seam: it runs after a miss's ticket is taken and before the render (PC12 commits a write there).
[<Sealed>]
type PageCacheStats() =
    [<DefaultValue>]
    val mutable private lookups: int64
    [<DefaultValue>]
    val mutable private hits: int64
    [<DefaultValue>]
    val mutable private stores: int64
    member this.Lookups = Interlocked.Read &this.lookups
    member this.Hits = Interlocked.Read &this.hits
    member this.Stores = Interlocked.Read &this.stores
    member val AfterLookup: Action = Action(ignore) with get, set
    member internal this.Lookup() = Interlocked.Increment &this.lookups |> ignore
    member internal this.Hit() = Interlocked.Increment &this.hits |> ignore
    member internal this.Store() = Interlocked.Increment &this.stores |> ignore

module PageCache =
    /// Where the route table leaves the matched row's `controller#action` (`Pages.EndpointKey` is this).
    [<Literal>]
    let EndpointKey = "campfire.web.endpoint"

    /// The lookup's record of the request, in `HttpContext.Items`: the key, the epoch and `Set-Cookie`'s count at the lookup.
    type internal Ticket = { Key: string; Epoch: int64; Cookies: int }

    [<Literal>]
    let private TicketItem = "campfire.web.page_cache.ticket"

    /// Rails' `CACHE_HEADERS`, plus the preload `Link`. Never `Set-Cookie`.
    let private kept = [| "ETag"; "Last-Modified"; "Cache-Control"; "Vary"; "Link" |]

    /// A page over this is not kept.
    [<Literal>]
    let private MaxBody = 1048576

    let private lifetime = TimeSpan.FromSeconds 60.0

    let private tags: string[] = [| "pages" |]

    let private lookupOptions =
        HybridCacheEntryOptions(Flags = (HybridCacheEntryFlags.DisableUnderlyingData ||| HybridCacheEntryFlags.DisableDistributedCache))

    let private storeOptions =
        HybridCacheEntryOptions(
            Expiration = lifetime,
            LocalCacheExpiration = lifetime,
            Flags = HybridCacheEntryFlags.DisableDistributedCache
        )

    /// The encoder for a request that takes compression, or nothing: the framework's provider (gzip or Brotli at their fastest).
    let internal encoderFor (http: HttpContext) : ICompressionProvider voption =
        match http.RequestServices.GetService<IResponseCompressionProvider>() with
        | null -> ValueNone
        | provider ->
            if provider.CheckRequestAcceptsCompression http && provider.ShouldCompressResponse http then
                match provider.GetCompressionProvider http with
                | null -> ValueNone
                | encoder -> ValueSome encoder
            else
                ValueNone

    /// The encoding the render will choose: the provider's pick for this request (the response has no content type yet, so
    /// `ShouldCompressResponse` is not asked; a render that is not compressed from a kept page stores nothing).
    let private encodingOf (http: HttpContext) : string | null =
        match http.RequestServices.GetService<IResponseCompressionProvider>() with
        | null -> null
        | provider ->
            if provider.CheckRequestAcceptsCompression http then
                match provider.GetCompressionProvider http with
                | null -> null
                | encoder -> encoder.EncodingName
            else
                null

    /// P8: every field the four pages read, hashed (the key stays under HybridCache's 1,024 characters and keeps ids and the
    /// user agent out of memory dumps).
    let private keyOf (http: HttpContext) (epoch: int64) (encoding: string) (user: User) (session: Session) : string =
        let request = http.Request
        let headers = request.Headers
        let query =
            match request.QueryString.Value with
            | null -> ""
            | query -> query
        let endpoint =
            match http.Items.TryGetValue EndpointKey with
            | true, (:? string as endpoint) -> endpoint
            | _ -> ""
        let text =
            String.Join(
                "\u0001",
                [| "pc1"
                   string epoch
                   endpoint
                   request.Scheme
                   string request.Host.Value
                   Respond.rawPath http + query
                   headers.Accept.ToString()
                   headers["Turbo-Frame"].ToString()
                   headers["X-Requested-With"].ToString()
                   encoding
                   headers.UserAgent.ToString()
                   string user.Id
                   string session.Id
                   defaultArg (Option.ofObj request.Cookies["last_room"]) "" |]
            )
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes text))

    /// A hit's handler: the status, the kept headers, `Content-Encoding`, `Content-Length` and the bytes.
    let private respondWith (entry: PageEntry) : HttpHandler =
        fun http ->
            task {
                let response = http.Response
                response.StatusCode <- 200
                response.Headers.ContentType <- entry.ContentType
                for struct (name, value) in entry.Headers do
                    response.Headers.Append(name, value)
                response.Headers.ContentEncoding <- entry.Encoding
                response.ContentLength <- Nullable(int64 entry.Body.Length)
                try
                    do! response.Body.WriteAsync(ReadOnlyMemory<byte> entry.Body)
                with
                // The client went away; the host has nothing more to say about it.
                | :? OperationCanceledException
                | :? IOException -> ()
            }
            :> Task

    /// Upstream's `cache_read_response` at the point it sits: eligible (P9) and found, the entry's handler; else `render ()` with
    /// a ticket left for `capture`.
    let serve (http: HttpContext) (render: unit -> Task<Result<HttpHandler, Error>>) : Task<Result<HttpHandler, Error>> =
        let current = Current.get http
        let eligible =
            HttpMethods.IsGet http.Request.Method
            && current.AuthenticatedBy = BySession
            && (Data.app http).Config.ResponseCacheBytes > 0L
            && http.Request.Headers.IfNoneMatch.Count = 0
            && http.Request.Headers.IfModifiedSince.Count = 0
            && (let flash = Respond.flash http
                isNull flash.Notice && isNull flash.Alert)
        match eligible, current.User, current.Session, encodingOf http with
        | true, Some user, Some session, (NonNull encoding) ->
            let gate = (Data.app http).Gate
            let epoch = gate.Epoch
            let key = keyOf http epoch encoding user session
            task {
                let cache = http.RequestServices.GetRequiredService<HybridCache>()
                let stats = http.RequestServices.GetRequiredService<PageCacheStats>()
                stats.Lookup()
                let! (entry: PageEntry) =
                    cache.GetOrCreateAsync(
                        key,
                        0,
                        (fun (_: int) (_: CancellationToken) -> ValueTask<PageEntry>(Unchecked.defaultof<PageEntry>)),
                        lookupOptions,
                        null,
                        http.RequestAborted
                    )
                match box entry with
                | null ->
                    http.Items[TicketItem] <- box { Key = key; Epoch = epoch; Cookies = http.Response.Headers.SetCookie.Count }
                    stats.AfterLookup.Invoke()
                    return! render ()
                | _ ->
                    stats.Hit()
                    return Ok(respondWith entry)
            }
        | _ -> render ()

    /// Called by `Pages.send` after a kept compressed page went out: stores the entry if P10's conditions hold (200, no cookie
    /// added since the lookup, no commit since the lookup, at most 1 MB).
    let capture (http: HttpContext) (status: int) (contentType: string) (page: CompressedPage) : unit =
        match http.Items.TryGetValue TicketItem with
        | true, (:? Ticket as ticket) when
            status = 200
            && page.Bytes.Length <= MaxBody
            && http.Response.Headers.SetCookie.Count = ticket.Cookies
            && (Data.app http).Gate.Epoch = ticket.Epoch
            ->
            http.Items.Remove TicketItem |> ignore
            let headers =
                [| for name in kept do
                       for value in http.Response.Headers[name] do
                           match value with
                           | null -> ()
                           | value -> struct (name, value) |]
            let entry = PageEntry(page.Encoding, page.Bytes, contentType, headers)
            let cache = http.RequestServices.GetRequiredService<HybridCache>()
            http.RequestServices.GetRequiredService<PageCacheStats>().Store()
            let stored = cache.SetAsync(ticket.Key, entry, storeOptions, tags)
            if not stored.IsCompletedSuccessfully then stored.AsTask() |> ignore
        | _ -> ()
