// No Rust test of rust/crates/campfire is ported here: this stands in for the contract of
// rust/crates/kit/src/deflater/splice.rs (the kit's page parts) and `Campfire.Views.Tests.KitIntegrationTests`.
//
// The contract Phase 4 left in `Campfire.Views.Tests.KitIntegrationTests`, run through the app's own glue
// (`FragmentGlue`): a page rendered with `Render.page` is a text and the fragments it recorded, the kit's
// `PageParts.Splice` takes those, and the kit's SHA and pieces for each fragment hang off the view fragment
// and are counted by the fragment cache.
module Campfire.App.Tests.FragmentGlueTests

open System
open System.Buffers
open System.IO
open System.IO.Compression
open Xunit
open Campfire.App
open Campfire.Views

let private splice (page: RecordedPage) : Campfire.App.Web.PageParts =
    match FragmentGlue.splice page with
    | Ok parts -> parts
    | Error _ -> failwith "a page of messages has fragments big enough for parts"

/// A message's partial, as the fragment cache holds it: ~3 KB of markup that depends on the message alone.
let private message (n: int) (w: Out) : unit =
    w.Raw $"<div id=\"message_{n}\" class=\"message\">"
    for i in 1..40 do
        w.Raw "<span class=\"word\">hello "
        w.Text $"there <{n}> & {i}"
        w.Raw "</span> "
    w.Raw "</div>\n"

/// A room page: a layout around the messages the cache hands over.
let private roomPage (cache: FragmentCache) (ids: int list) : RecordedPage =
    FragmentCache.withCache cache (fun () ->
        Render.page 0 (fun w ->
            w.Raw "<html><head><title>Room</title></head><body><div id=\"messages\">\n"
            for n in ids do
                w.Fragment(FragmentCache.fetch (fun key -> key.Append $"messages/{n}") (message n))
            w.Raw "</div></body></html>\n"))

[<Fact>]
let ``a recorded room page splices into the kits page parts and is the page`` () =
    let cache = FragmentCache FragmentCacheLimits.DefaultMaxBytes
    let ids = [ 1..30 ]
    let page = roomPage cache ids
    Assert.Equal(30, page.Fragments.Length)
    let parts = splice page
    let whole = page.ToArray()
    Assert.Equal(whole.Length, parts.BodyLength)
    let plain = ArrayBufferWriter<byte>()
    parts.WritePlain plain
    Assert.Equal<byte[]>(whole, plain.WrittenSpan.ToArray())
    // The next request for the page is the same fragments, so the same ETag.
    let again = splice (roomPage cache ids)
    Assert.Equal(parts.Etag(), again.Etag())
    // A page with one more message is another page.
    Assert.True(parts.Etag() <> (splice (roomPage cache [ 1..31 ])).Etag())

[<Fact>]
let ``the kit fragment of a view fragment is made once`` () =
    let fragment = Fragment "<p>hello</p>"
    let first = FragmentGlue.kitFragment fragment
    Assert.Same(first, FragmentGlue.kitFragment fragment)
    Assert.True(fragment.HeldBytes >= fragment.Length)

[<Fact>]
let ``the cache counts the kits SHA, and a pages fragments stay while the rest age out`` () =
    // Room for ~20 messages with the digest the kit keeps for them.
    let budget = 100 * 1024
    let cache = FragmentCache budget
    let page = [ 1..8 ]
    for round in 1..40 do
        // The page is shown (and hashed, which stores the digests), then other rooms' messages churn.
        let parts = splice (roomPage cache page)
        parts.Etag() |> ignore
        for n in 1000 + round * 20 .. 1000 + round * 20 + 19 do
            FragmentCache.withCache cache (fun () -> FragmentCache.fetch (fun key -> key.Append $"messages/{n}") (message n)) |> ignore
        Assert.True(cache.Bytes <= budget, $"{cache.Bytes} bytes counted, over the budget of {budget}")
    for n in page do
        let fragment = FragmentCache.withCache cache (fun () -> FragmentCache.read (fun key -> key.Append $"messages/{n}"))
        Assert.NotNull fragment
        Assert.True(fragment.HeldBytes >= fragment.Length + 32, "with the kit's SHA counted in")
    // (`Campfire.Views.Tests` checks the cache's own bookkeeping and what is really held, which are internal to it.)
