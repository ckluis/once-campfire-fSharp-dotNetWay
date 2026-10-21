// plans/design-views.md V5, V7: the framework's HybridCache (local memory only) holds the page's kept compressed forms, and in
// unit 4.3 the fragments.
namespace Campfire.Views

open System
open System.Buffers
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Caching.Hybrid
open Microsoft.Extensions.DependencyInjection

/// Lets HybridCache measure a fragment (its serialized length is the entry's size against the memory cache's `SizeLimit`); the
/// local cache keeps the instance itself, so this runs for the size and never for a hit.
[<Sealed>]
type CachedFragmentSerializer() =
    interface IHybridCacheSerializer<CachedFragment> with
        member _.Deserialize(source: ReadOnlySequence<byte>) = CachedFragment(source.ToArray())
        member _.Serialize(value: CachedFragment, target: IBufferWriter<byte>) = target.Write(ReadOnlySpan<byte> value.Bytes)

/// A page already compressed: the `Content-Encoding` it carries and its bytes. Immutable, so a hit is the one instance.
[<Sealed; System.ComponentModel.ImmutableObject(true)>]
type CompressedPage(encoding: string, bytes: byte[]) =
    member _.Encoding: string = encoding
    member _.Bytes: byte[] = bytes

[<Sealed>]
type CompressedPageSerializer() =
    interface IHybridCacheSerializer<CompressedPage> with
        member _.Deserialize(source: ReadOnlySequence<byte>) =
            let all = source.ToArray()
            let encodingLength = int all[0]
            CompressedPage(Encoding.ASCII.GetString(all, 1, encodingLength), all[1 + encodingLength ..])

        member _.Serialize(value: CompressedPage, target: IBufferWriter<byte>) =
            target.Write(ReadOnlySpan<byte>([| byte value.Encoding.Length |]))
            target.Write(ReadOnlySpan<byte>(Encoding.ASCII.GetBytes value.Encoding))
            target.Write(ReadOnlySpan<byte> value.Bytes)

module Fragments =
    /// How long an entry lives: HybridCache's default is five minutes, and a message fragment is keyed by its version.
    let private lifetime = TimeSpan.FromDays 7.0

    /// A memory cache bounded by `sizeLimit` bytes (HybridCache sets each entry's `Size` to its serialized length) and a
    /// HybridCache over it with no L2: the session's distributed cache must not become one.
    let add (services: IServiceCollection) (sizeLimit: int64) : unit =
        services.AddMemoryCache(fun options ->
            options.SizeLimit <- Nullable sizeLimit
            options.TrackStatistics <- true)
        |> ignore
        services
            .AddHybridCache(fun options ->
                options.MaximumPayloadBytes <- 4L * 1024L * 1024L
                options.DefaultEntryOptions <-
                    HybridCacheEntryOptions(
                        Expiration = lifetime,
                        LocalCacheExpiration = lifetime,
                        Flags = HybridCacheEntryFlags.DisableDistributedCache
                    ))
            .AddSerializer<CachedFragment, CachedFragmentSerializer>()
            .AddSerializer<CompressedPage, CompressedPageSerializer>()
        |> ignore

    /// The kept compressed form of a page, keyed `page/<encoding>/<SHA-256 hex>` of its parts and fragments (V7): made with
    /// `compress` once, however many requests ask at the same moment, then handed out as the same instance.
    let compressedPage
        (cache: HybridCache)
        (encoding: string)
        (page: HtmlOutput)
        (compress: HtmlOutput -> byte[])
        : ValueTask<CompressedPage> =
        let key = String.Concat("page/", encoding, "/", Convert.ToHexStringLower(page.Digest()))
        cache.GetOrCreateAsync(
            key,
            struct (page, compress, encoding),
            (fun (struct (page: HtmlOutput, compress: HtmlOutput -> byte[], encoding: string)) (_: CancellationToken) ->
                ValueTask<CompressedPage>(CompressedPage(encoding, compress page)))
        )
