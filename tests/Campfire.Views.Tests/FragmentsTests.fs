// F1 and F2 (plans/design-views.md section 5): the fragment cache is HybridCache over a size-limited memory cache
// (`Fragments.add`), and its keys are Rails' `cache_key_with_version` with the partial and variant. They replace the tests of
// the byte-bounded store the port had (`FragmentCache`, rust/crates/views/src/fragment_cache.rs).
module Campfire.Views.Tests.FragmentsTests

open System
open System.Globalization
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Caching.Hybrid
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Xunit
open Campfire.RailsCompat
open Campfire.Views
open Campfire.Views.Differential.Inputs
open Campfire.Views.Tests.GoldenB

let private time (text: string) : Timestamp = (Timestamps.tryParse text).Value

let private provider (sizeLimit: int64) : ServiceProvider =
    let services = ServiceCollection()
    Fragments.add services sizeLimit
    services.BuildServiceProvider()

let private fragment (size: int) (c: char) : CachedFragment = CachedFragment(String(c, size))

let private render (fragment: CachedFragment) : CancellationToken -> ValueTask<CachedFragment> =
    fun _ -> ValueTask<CachedFragment> fragment

// ---- F1: HybridCache ---------------------------------------------------------------------------------------------------

[<Fact>]
let ``F1 a hit returns the same instance`` () =
    task {
        use services = provider (int64 Fragments.DefaultMaxBytes)
        let cache = services.GetRequiredService<HybridCache>()
        let! first = (Fragments.fetch cache "a" (render (fragment 100 'a'))).AsTask()
        let! second = (Fragments.fetch cache "a" (fun _ -> failwith "rendered again")).AsTask()
        Assert.Same(first, second)
        Assert.Equal(String('a', 100), second.ToString())
        // A different key is a different fragment.
        let! other = (Fragments.fetch cache "b" (render (fragment 100 'b'))).AsTask()
        Assert.NotSame(first, other)
    }

[<Fact>]
let ``F1 sixty-four concurrent misses on one key render once`` () =
    task {
        use services = provider (int64 Fragments.DefaultMaxBytes)
        let cache = services.GetRequiredService<HybridCache>()
        let mutable renders = 0
        let slow (_: CancellationToken) : ValueTask<CachedFragment> =
            ValueTask<CachedFragment>(
                task {
                    Interlocked.Increment(&renders) |> ignore
                    do! Task.Delay 100
                    return fragment 500 'x'
                }
            )
        let! results =
            Task.WhenAll [| for _ in 1..64 -> Task.Run<CachedFragment>(Func<Task<CachedFragment>>(fun () -> (Fragments.fetch cache "contended" slow).AsTask())) |]
        Assert.Equal(1, renders)
        for result in results do
            Assert.Same(results[0], result)
    }

[<Fact>]
let ``F1 the size limit is honoured`` () =
    task {
        let limit = 2_000_000L
        use services = provider limit
        let cache = services.GetRequiredService<HybridCache>()
        let memory = services.GetRequiredService<IMemoryCache>() :?> MemoryCache
        // Twice the limit in fragments of 9.4 KB.
        for i in 0..400 do
            let! _ = (Fragments.fetch cache $"key/{i}" (render (fragment 9_400 'x'))).AsTask()
            ()
        let statistics = nonNull (memory.GetCurrentStatistics())
        let estimated = statistics.CurrentEstimatedSize.Value
        Assert.True(estimated <= limit, $"{estimated} estimated against {limit}")
        Assert.True(memory.Count < 401, $"{memory.Count} entries kept of 401")
        Assert.True(memory.Count > 100, $"{memory.Count} entries kept: the limit is not being used")
    }

[<Fact>]
let ``F1 entries live seven days`` () =
    use services = provider (int64 Fragments.DefaultMaxBytes)
    let options = services.GetRequiredService<IOptions<HybridCacheOptions>>().Value
    let entry = nonNull options.DefaultEntryOptions
    Assert.Equal(TimeSpan.FromDays 7.0, entry.Expiration.Value)
    Assert.Equal(TimeSpan.FromDays 7.0, entry.LocalCacheExpiration.Value)
    // Local memory only: the session's distributed cache must not become a second level.
    Assert.True(entry.Flags.Value.HasFlag HybridCacheEntryFlags.DisableDistributedCache)

// ---- F2: keys ---------------------------------------------------------------------------------------------------------

/// The keys built with the framework's formatting, which the buffer-built ones must match byte for byte: `{:06}` puts a
/// negative sub-second part's sign inside the width.
module private Formatted =
    let private epoch = DateTimeOffset.UnixEpoch.UtcTicks

    let cacheVersion (updatedAt: Timestamp) : string =
        let micros = ((updatedAt.UtcTicks - epoch) % 10_000_000L) / 10L
        let padded =
            if micros < 0L then "-" + (-micros).ToString(CultureInfo.InvariantCulture).PadLeft(5, '0')
            else micros.ToString(CultureInfo.InvariantCulture).PadLeft(6, '0')
        updatedAt.UtcDateTime.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + padded

    let cacheKeyWithVersion (table: string) (id: int64) (updatedAt: Timestamp) : string =
        $"{table}/{id}-{cacheVersion updatedAt}"

/// Timestamps with the calendar's edges and every sub-second precision.
let private timestamps: Timestamp list =
    [ "1970-01-01T00:00:00Z"
      "1970-01-01T00:00:00.000001Z"
      "1999-12-31T23:59:59.999999Z"
      "2000-02-29T12:34:56.5Z"
      "2024-02-29T23:59:59.000999Z"
      "2024-06-01T12:00:00.000123Z"
      "2024-06-01T12:00:00.000123456Z"
      "2024-12-31T23:59:59.1Z"
      "2038-01-19T03:14:08Z"
      "2100-03-01T00:00:00.00001Z"
      "9999-12-30T21:59:59.999999999Z"
      "1969-12-31T23:59:59.5Z"
      "1900-01-01T00:00:00Z"
      "0001-01-01T00:00:00.25Z"
      "2024-11-03T01:30:00.123456-07:00" ]
    |> List.map time

[<Fact>]
let ``F2 keys match Rails' cache key with version byte for byte`` () =
    let ids = [| 0L; 1L; 7L; 10L; 99L; 100L; 12_345L; 1_000_000_000L; Int64.MaxValue; -1L; Int64.MinValue |]
    timestamps
    |> List.iteri (fun i at ->
        Assert.Equal(Formatted.cacheVersion at, Fragments.cacheVersion at)
        let id = ids[i % ids.Length]
        Assert.Equal(Formatted.cacheKeyWithVersion "messages" id at, Fragments.cacheKeyWithVersion "messages" id at)
        Assert.Equal(
            "views/messages/_message/" + Formatted.cacheKeyWithVersion "messages" id at + "/presentation-v3",
            Fragments.key "messages/_message" "messages" id at (Some "presentation-v3"))
        Assert.Equal(
            "views/users/sidebars/rooms/_direct/" + Formatted.cacheKeyWithVersion "memberships" id at,
            Fragments.key "users/sidebars/rooms/_direct" "memberships" id at None))

[<Fact>]
let ``F2 the key vectors are Rails'`` () =
    Assert.Equal("messages/1-20240601120000000123", Fragments.cacheKeyWithVersion "messages" 1L (time "2024-06-01T12:00:00.000123Z"))
    Assert.Equal(
        "views/messages/_message/messages/7-20240601120000000123/presentation-v3",
        Fragments.key "messages/_message" "messages" 7L (time "2024-06-01T12:00:00.000123Z") (Some "presentation-v3"))
    Assert.Equal(
        "views/messages/boosts/_boost/boosts/3-19700101000000000000",
        Fragments.key "messages/boosts/_boost" "boosts" 3L Timestamps.unixEpoch None)
    Assert.Equal(
        "views/users/sidebars/rooms/_direct/memberships/5-19700101000000000000",
        Fragments.key "users/sidebars/rooms/_direct" "memberships" 5L Timestamps.unixEpoch None)

[<Fact>]
let ``F2 a message's key changes with its updated_at and a boost's change touches the message`` () =
    task {
        let g = golden "messages_boosts_index"
        let message = messageView g.Input
        Assert.True(message.Boosts.Length >= 2, "the golden message has two boosts")
        let ctx = g.Context
        use services = provider (int64 Fragments.DefaultMaxBytes)
        let cache = services.GetRequiredService<HybridCache>()
        let key (m: Messages.MessageView) = Fragments.key "messages/_message" "messages" m.Id m.UpdatedAt (Some "presentation-v3")
        let later = message.UpdatedAt.AddSeconds 1.0
        Assert.NotEqual<string>(key message, key { message with UpdatedAt = later })

        // Warm: the message and each boost render once and are then hits.
        let! first = (MessagesCached.message cache ctx message).AsTask()
        let! boostsBefore = (BoostsCached.boosts cache ctx message.Boosts).AsTask()
        let! again = (MessagesCached.message cache ctx message).AsTask()
        Assert.Same(first, again)

        // A boost changes (`touch: true` on the message): the boost has a new key, so does its message, the other boosts are hits.
        let changed = { message.Boosts[0] with UpdatedAt = later }
        let touched = { message with UpdatedAt = later; Boosts = changed :: message.Boosts.Tail }
        let! boostsAfter = (BoostsCached.boosts cache ctx touched.Boosts).AsTask()
        Assert.NotSame(boostsBefore[0], boostsAfter[0])
        for i in 1 .. boostsBefore.Length - 1 do
            Assert.Same(boostsBefore[i], boostsAfter[i])
        let! second = (MessagesCached.message cache ctx touched).AsTask()
        Assert.NotSame(first, second)
        // The message's fragment holds its boosts' fragments, as Rails nests `cache boost` inside `cache message`.
        for boost in boostsAfter do
            Assert.Contains(boost.ToString(), second.ToString())
    }
