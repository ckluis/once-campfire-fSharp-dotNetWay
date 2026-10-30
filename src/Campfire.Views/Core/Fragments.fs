// plans/design-views.md V5, V7: the framework's HybridCache (local memory only) holds the rendered fragments (`cache record do ...
// end` in ERB) and the page's kept compressed forms. The first rendering of a record version is what every later render reuses,
// whoever renders it: a broadcast renders without a request, and the pages that show the same message afterwards repeat that
// rendering byte for byte. So nothing in a fragment may depend on the request unless its key does (a message's "Copy link"
// carries a path, not the request's host).
//
// Fragments are fetched before a template renders, with `await` (`MessagesCached`, `BoostsCached`, `UsersCached`): a template
// takes the fetched `CachedFragment`s as arguments and never reaches the cache. This module is the only one that calls HybridCache
// (`bin/lint-markup` enforces it).
namespace Campfire.Views

open System
open System.Buffers
open System.Globalization
open System.Text
open System.Threading
open System.Threading.Tasks
open Campfire.Rails
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

/// A key under construction in a buffer each thread reuses: keys are built for every cached record on every request, hit or
/// miss, and the one string made from it is all a hit allocates.
[<Sealed; AbstractClass>]
type private KeyScratch private () =
    [<ThreadStatic; DefaultValue>]
    static val mutable private chars: char[] | null

    static member Rent(length: int) : char[] =
        match KeyScratch.chars with
        | null ->
            let chars = Array.zeroCreate (max 256 length)
            KeyScratch.chars <- chars
            chars
        | chars when chars.Length < length ->
            let chars = Array.zeroCreate (max length (chars.Length * 2))
            KeyScratch.chars <- chars
            chars
        | chars -> chars

module Fragments =
    /// The default size of the fragment cache: `MemoryStore`'s default `size`, 32 MB.
    [<Literal>]
    let DefaultMaxBytes = 33554432

    /// How long an entry lives: HybridCache's default is five minutes, and a message fragment is keyed by its version.
    let private lifetime = TimeSpan.FromDays 7.0

    let private epochTicks = DateTimeOffset.UnixEpoch.UtcTicks

    /// `Time#to_fs(:usec)` of a record's `updated_at`: its `cache_version`, `%Y%m%d%H%M%S` and six digits of microseconds,
    /// in UTC.
    let cacheVersion (updatedAt: Timestamp) : string =
        let utc = updatedAt.UtcDateTime
        // The sub-second part has the timestamp's sign (jiff's `subsec_microsecond`).
        let microseconds = ((updatedAt.UtcTicks - epochTicks) % 10_000_000L) / 10L
        let fraction =
            if microseconds < 0L then
                // Before 1970 `{:06}` prints the negative sub-second part with its sign inside the width; no record is that
                // old, so this just keeps those bytes the same.
                let magnitude = uint64 -microseconds
                "-" + magnitude.ToString(CultureInfo.InvariantCulture).PadLeft((if magnitude >= 100000UL then 6 else 5), '0')
            else
                microseconds.ToString("D6", CultureInfo.InvariantCulture)
        String.Concat(utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture), fraction)

    /// `record.cache_key_with_version`: `"messages/1-20240601120000000000"`.
    let cacheKeyWithVersion (table: string) (id: int64) (updatedAt: Timestamp) : string =
        String.Concat(table, "/", id.ToString(CultureInfo.InvariantCulture), "-", cacheVersion updatedAt)

    /// A fragment's key, Rails' `cache_key_with_version` with the partial and an optional variant, and no template digest (an
    /// in-process cache empties on deploy): `views/messages/_message/messages/<id>-<updated_at usec>/presentation-v3`.
    let key (partial: string) (table: string) (id: int64) (updatedAt: Timestamp) (variant: string option) : string =
        let version = cacheVersion updatedAt
        let variant = match variant with Some variant -> variant | None -> ""
        let length = 6 + partial.Length + 1 + table.Length + 1 + 20 + 1 + version.Length + 1 + variant.Length
        let chars = KeyScratch.Rent length
        let at = ref 0
        let put (text: string) =
            text.CopyTo(0, chars, at.Value, text.Length)
            at.Value <- at.Value + text.Length
        put "views/"
        put partial
        put "/"
        put table
        put "/"
        let mutable written = 0
        id.TryFormat(Span<char>(chars, at.Value, 20), &written, ReadOnlySpan<char>(), CultureInfo.InvariantCulture) |> ignore
        at.Value <- at.Value + written
        put "-"
        put version
        if variant.Length > 0 then
            put "/"
            put variant
        String(chars, 0, at.Value)

    /// `Rails.cache.fetch(key) { render }` for a rendered fragment: a hit is the one cached instance (and completes
    /// synchronously), a miss runs `render` once however many requests ask at the same moment.
    let fetch
        (cache: HybridCache)
        (key: string)
        (render: CancellationToken -> ValueTask<CachedFragment>)
        : ValueTask<CachedFragment> =
        cache.GetOrCreateAsync(
            key,
            render,
            (fun (render: CancellationToken -> ValueTask<CachedFragment>) (token: CancellationToken) -> render token)
        )

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
