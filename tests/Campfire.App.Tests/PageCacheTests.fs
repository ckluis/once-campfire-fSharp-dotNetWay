/// Design: plans/design-perf.md section 5, PC0-PC17 (unit 10.4). The page cache (`Web/PageCache.fs`) on the room page and the messages
/// page: a hit is the bytes of the render, authorization is fresh on every request, and nothing stale or per-user is ever kept.
/// A hit is counted by `PageCacheStats` (lookups, hits, entries stored), which the app registers beside the cache.
module Campfire.App.Tests.PageCacheTests

open System
open System.IO
open System.IO.Compression
open System.Threading
open System.Threading.Tasks
open System.Runtime.CompilerServices
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.Caching.Hybrid
open Microsoft.Extensions.Caching.Memory
open Microsoft.Extensions.DependencyInjection
open Xunit
open Campfire.App.Tests.Support
open Campfire.App.Web
open Campfire.Db
open Campfire.Views

let private stats (test: Test) : PageCacheStats = test.Host.Services.GetRequiredService<PageCacheStats>()

/// A request that takes gzip, as a browser's does.
let private gz (path: string) : Req = (get path).With("accept-encoding", "gzip")

let private room = $"/rooms/{ALL_TALK}"
let private messages = $"/rooms/{ALL_TALK}/messages"
let private sidebar = "/users/me/sidebar"
let private search = "/searches?q=coffee"
let private fourPages = [ room; messages; sidebar; search ]

/// The body, inflated when it went out gzipped.
let private text (reply: Reply) : string =
    match reply.Header "content-encoding" with
    | Some "gzip" ->
        use input = new MemoryStream(reply.Body)
        use zip = new GZipStream(input, CompressionMode.Decompress)
        use reader = new StreamReader(zip)
        reader.ReadToEnd()
    | _ -> reply.Text

let private comparable (reply: Reply) : string list =
    reply.Headers
    |> List.filter (fun (k, _) -> not (k.Equals("date", StringComparison.OrdinalIgnoreCase) || k.Equals("x-request-id", StringComparison.OrdinalIgnoreCase) || k.Equals("x-runtime", StringComparison.OrdinalIgnoreCase)))
    |> List.map (fun (k, v) -> k.ToLowerInvariant() + ": " + v)
    |> List.sort

let private signedIn (test: Test) (who: string) : Task<Browser> =
    task {
        let b = anonymous test
        do! b.SignIn(test.Label $"emails.{who}")
        return b
    }

let private post (b: Browser) (body: string) : Task<Reply> =
    b.Write(((request "POST" messages).With("accept", "text/vnd.turbo-stream.html, text/html")).Form(formBody [ "message[body]", body ]))

[<Fact>]
let ``PC0 a lookup with DisableUnderlyingData returns the default and never calls a factory`` () =
    task {
        let services = ServiceCollection()
        Fragments.add services (8L * 1024L * 1024L)
        services.AddSingleton<IHybridCacheSerializer<PageEntry>, PageEntrySerializer>() |> ignore
        use provider = services.BuildServiceProvider()
        let cache = provider.GetRequiredService<HybridCache>()
        let called = ref 0
        let lookup () =
            cache
                .GetOrCreateAsync(
                    "k",
                    0,
                    (fun (_: int) (_: CancellationToken) ->
                        Interlocked.Increment called |> ignore
                        ValueTask<PageEntry>(Unchecked.defaultof<PageEntry>)),
                    HybridCacheEntryOptions(Flags = (HybridCacheEntryFlags.DisableUnderlyingData ||| HybridCacheEntryFlags.DisableDistributedCache)),
                    null,
                    CancellationToken.None
                )
                .AsTask()
        let! missing = lookup ()
        Assert.True(isNull (box missing))
        Assert.Equal(0, called.Value)
        let entry = PageEntry("gzip", [| 1uy; 2uy |], "text/html", [| struct ("ETag", "x") |])
        do! cache.SetAsync("k", entry, HybridCacheEntryOptions(Flags = HybridCacheEntryFlags.DisableDistributedCache), [| "pages" |]).AsTask()
        let! found = lookup ()
        Assert.True(obj.ReferenceEquals(entry, found))
        Assert.Equal(0, called.Value)
        // The serializer is what measures an entry for the size limit; it round-trips.
        let serializer = PageEntrySerializer() :> IHybridCacheSerializer<PageEntry>
        let writer = System.Buffers.ArrayBufferWriter<byte>()
        serializer.Serialize(entry, writer)
        let back = serializer.Deserialize(System.Buffers.ReadOnlySequence<byte>(writer.WrittenMemory))
        Assert.Equal<byte[]>(entry.Body, back.Body)
        Assert.Equal("gzip", back.Encoding)
        Assert.Equal(1, back.Headers.Length)
    }

[<Fact>]
let ``PC1 a second GET of the room page and the messages page is a hit with the render's bytes and headers`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        // The seed's session is stale, so the first request resumes it (a `session_token` cookie, once): not part of what is compared.
        let! _ = (david test).Send(get room)
        for path in [ room; messages ] do
            let b1, b2 = david test, david test
            let hits = stats.Hits
            let! first = b1.Send(gz path)
            let! second = b2.Send(gz path)
            Assert.Equal(200, first.Status)
            Assert.Equal(200, second.Status)
            Assert.Equal(hits + 1L, stats.Hits)
            Assert.Equal<byte[]>(first.Body, second.Body)
            Assert.Equal<string>(String.Join("\n", comparable first), String.Join("\n", comparable second))
            if path = room then
                // Neither request carried `last_room`, so the before-action sets it on both, hit or miss.
                Assert.Contains(first.SetCookies, fun c -> c.StartsWith "last_room=")
                Assert.Contains(second.SetCookies, fun c -> c.StartsWith "last_room=")
            if path = room then
                // The same browser again: it carries the cookie now, so this is another key, then a hit that sets none.
                let stores = stats.Stores
                let! third = b1.Send(gz path)
                Assert.Equal(stores + 1L, stats.Stores)
                let hits = stats.Hits
                let! fourth = b1.Send(gz path)
                Assert.Equal(hits + 1L, stats.Hits)
                Assert.Equal<byte[]>(third.Body, fourth.Body)
                Assert.DoesNotContain(fourth.SetCookies, fun c -> c.StartsWith "last_room=")
    }

[<Fact>]
let ``PC2 a session deleted between two GETs redirects to sign in, not a cached page`` () =
    task {
        use! test = bootSeeded "default"
        for path in [ room; messages ] do
            let b = david test
            let! _ = b.Send(gz path)
            let! warm = b.Send(gz path)
            Assert.Equal(200, warm.Status)
        do! test.Execute("DELETE FROM sessions", [])
        for path in [ room; messages ] do
            let! reply = (david test).Send(gz path)
            Assert.Equal(302, reply.Status)
            Assert.Contains("/session/new", reply.Location.Value)
    }

[<Fact>]
let ``PC3 a membership removed between two GETs gets set_room's answer, not the cached page`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        for path in [ room; messages ] do
            let! _ = b.Send(gz path)
            let! warm = b.Send(gz path)
            Assert.Equal(200, warm.Status)
        do! test.Execute("DELETE FROM memberships WHERE user_id = ? AND room_id = ?", [ box DAVID; box ALL_TALK ])
        for path in [ room; messages ] do
            let! reply = b.Send(gz path)
            Assert.NotEqual(200, reply.Status)
    }

[<Fact>]
let ``PC4 a user deactivated between two GETs never gets the cached page`` () =
    task {
        use! test = bootSeeded "default"
        let! jason = signedIn test "jason"
        let! _ = jason.Send(gz room)
        let! _ = jason.Send(gz room)
        let! warm = jason.Send(gz room)
        Assert.Equal(200, warm.Status)
        // The administrator deactivates him through the action (it ends his sessions, as Rails' `user.deactivate` does).
        let! admin = signedIn test "david"
        let! gone = admin.Form("delete", $"/account/users/{JASON}", [])
        Assert.True(gone.Status < 400, gone.Text)
        let! reply = jason.Send(gz room)
        Assert.NotEqual(200, reply.Status)
    }

[<Fact>]
let ``PC5 two users, and two sessions of one user, get their own pages`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let! a = signedIn test "david"
        let! b = signedIn test "jason"
        let! a2 = signedIn test "david"
        for browser in [ a; b; a2 ] do
            let! _ = browser.Send(gz room)
            ()
        let stores = stats.Stores
        let! pa = a.Send(gz room)
        let! pb = b.Send(gz room)
        let! pa2 = a2.Send(gz room)
        Assert.Equal(stores + 3L, stats.Stores)
        Assert.NotEqual<string>(text pa, text pb)
        // Each one's repeat is its own entry's hit, and the bytes are its own.
        for (browser: Browser), (first: Reply) in [ a, pa; b, pb; a2, pa2 ] do
            let hits = stats.Hits
            let! again = browser.Send(gz room)
            Assert.Equal(hits + 1L, stats.Hits)
            Assert.Equal<string>(text first, text again)
    }

[<Fact>]
let ``PC6 after a write the next GET of the room page and the messages page shows it`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let! b = signedIn test "david"
        let warm () =
            task {
                for path in [ room; messages ] do
                    let! _ = b.Send(gz path)
                    let! _ = b.Send(gz path)
                    let hits = stats.Hits
                    let! _ = b.Send(gz path)
                    Assert.Equal(hits + 1L, stats.Hits)
            }
        // A message.
        do! warm ()
        let! reply = post b "pc6 marker message"
        Assert.InRange(reply.Status, 200, 299)
        for path in [ room; messages ] do
            let! page = b.Send(gz path)
            Assert.Contains("pc6 marker message", text page)
        // A change of the account's custom styles and of the user's name (each redirects with a notice, which the next request sweeps).
        do! warm ()
        let! styles = b.Form("patch", "/account/custom_styles", [ "account[custom_styles]", "body { color: #abc123 }" ])
        Assert.True(styles.Status < 400, styles.Text)
        let! _sweep = b.Send(gz "/users/me/profile")
        let! page = b.Send(gz room)
        Assert.Contains("abc123", text page)
        do! warm ()
        let! rename = b.Form("patch", "/users/me/profile", [ "user[name]", "Pcsix Renamed" ])
        Assert.True(rename.Status < 400, rename.Text)
        let! _sweep = b.Send(gz "/users/me/profile")
        let! page = b.Send(gz room)
        Assert.Contains("Pcsix Renamed", text page)
        // A change of involvement: the page is made again.
        do! warm ()
        let! involve = b.Form("patch", $"/rooms/{ALL_TALK}/involvement", [ "involvement", "invisible" ])
        Assert.True(involve.Status < 400, involve.Text)
        let! _sweep = b.Send(gz "/users/me/profile")
        let hits = stats.Hits
        let! _ = b.Send(gz room)
        Assert.Equal(hits, stats.Hits)
    }

[<Fact>]
let ``PC7 a write made outside an HTTP request bumps the epoch and the next GET renders`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let b = david test
        let! _ = b.Send(gz room)
        let! _ = david(test).Send(gz room)
        let! _ = b.Send(gz room)
        let epoch = test.App.Gate.Epoch
        let app = test.App
        use context = app.DbFactory.CreateDbContext()
        let! written = Ef.Writes.run app.Gate app.Env context (fun _ -> ())
        Assert.True(Result.isOk written)
        Assert.True(test.App.Gate.Epoch >= epoch + 2L, "a commit and its gate exit both count")
        let hits = stats.Hits
        let! _ = b.Send(gz room)
        Assert.Equal(hits, stats.Hits)
    }

[<Fact>]
let ``PC8 a stale session's resume cookie is not replayed, and a request with a flash is neither looked up nor stored`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let stale = DateTime.UtcNow - TimeSpan.FromHours 2.0
        do! test.Execute("UPDATE sessions SET last_active_at = ?", [ box (RailsTime.toDb stale) ])
        let b = david test
        let! resumed = b.Send(gz room)
        Assert.Equal(200, resumed.Status)
        let! next = david(test).Send(gz room)
        Assert.Equal(200, next.Status)
        Assert.DoesNotContain(next.SetCookies, fun c -> c.StartsWith "session_token=")
        // A flash: the request after a profile update shows its notice, and is neither looked up nor stored.
        let! signed = signedIn test "david"
        let! _ = signed.Send(gz room)
        let! rename = signed.Form("patch", "/users/me/profile", [ "user[name]", "Flash Check" ])
        Assert.True(rename.Status < 400)
        let lookups, stores = stats.Lookups, stats.Stores
        let! flashed = signed.Send(gz room)
        Assert.Equal(200, flashed.Status)
        Assert.Equal(lookups, stats.Lookups)
        Assert.Equal(stores, stats.Stores)
        let! after = signed.Send(gz room)
        Assert.Equal(lookups + 1L, stats.Lookups)
        Assert.Equal(200, after.Status)
    }

[<Fact>]
let ``PC9 Accept, Turbo-Frame, Accept-Encoding and User-Agent variants are separate entries, and identity renders`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let b = david test
        let! _ = b.Send(gz room)
        let variants =
            [ "gzip", gz room
              "br", (get room).With("accept-encoding", "br")
              "user agent", (gz room).With("user-agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36")
              "accept", (gz room).With("accept", "text/html")
              "turbo frame", (gz room).With("turbo-frame", "room") ]
        for name, req in variants do
            let stores = stats.Stores
            let! reply = b.Send req
            Assert.Equal(200, reply.Status)
            Assert.True(stats.Stores = stores + 1L, $"{name}: a new entry")
        let hits = stats.Hits
        let! _ = b.Send(gz room)
        Assert.Equal(hits + 1L, stats.Hits)
        let lookups, stores = stats.Lookups, stats.Stores
        let! identity = b.Send(get room)
        Assert.Equal(200, identity.Status)
        Assert.Equal(lookups, stats.Lookups)
        Assert.Equal(stores, stats.Stores)
    }

[<Fact>]
let ``PC10 a conditional request keeps its 304, and a cached messages page carries the render's validators`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let! _ = (david test).Send(get messages)
        let! first = (david test).Send(gz messages)
        let! cached = (david test).Send(gz messages)
        Assert.Equal(1L, stats.Hits)
        for name in [ "etag"; "last-modified"; "cache-control"; "vary" ] do
            Assert.Equal<string option>(first.Header name, cached.Header name)
        let etag = (first.Header "etag").Value
        let lookups, hits = stats.Lookups, stats.Hits
        let! conditional = (david test).Send((gz messages).With("if-none-match", etag))
        Assert.Equal(304, conditional.Status)
        Assert.Equal(lookups, stats.Lookups)
        Assert.Equal(hits, stats.Hits)
    }

[<Fact>]
let ``PC11 HEAD renders as before, with no body, and is never served from an entry`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let b = david test
        let! _ = b.Send(gz room)
        let! _ = b.Send(gz room)
        let lookups, hits, stores = stats.Lookups, stats.Hits, stats.Stores
        let! head = b.Send((request "HEAD" room).With("accept-encoding", "gzip"))
        Assert.Equal(200, head.Status)
        Assert.Equal(0, head.Body.Length)
        Assert.Equal((lookups, hits, stores), (stats.Lookups, stats.Hits, stats.Stores))
    }

[<Fact>]
let ``PC12 a commit between the lookup and the store leaves nothing stored`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let b = david test
        stats.AfterLookup <- Action(fun () -> test.App.Gate.Bump())
        let stores = stats.Stores
        let! reply = b.Send(gz room)
        Assert.Equal(200, reply.Status)
        Assert.Equal(stores, stats.Stores)
        stats.AfterLookup <- Action(ignore)
        let! _ = b.Send(gz room)
        Assert.Equal(stores + 1L, stats.Stores)
        let hits = stats.Hits
        let! _ = b.Send(gz room)
        Assert.Equal(hits + 1L, stats.Hits)
    }

[<Fact>]
let ``PC13 with CAMPFIRE_RESPONSE_CACHE_MB=0 nothing is looked up or stored, on all four pages`` () =
    task {
        use! test = bootSeededConfigured ignore [ "CAMPFIRE_RESPONSE_CACHE_MB", "0" ] "default"
        let stats = stats test
        let b = david test
        for path in fourPages do
            for _ in 1..3 do
                let! reply = b.Send(gz path)
                Assert.Equal(200, reply.Status)
        Assert.Equal((0L, 0L, 0L), (stats.Lookups, stats.Hits, stats.Stores))
    }

[<Fact>]
let ``PC14 a body over the bound is not stored, on all four pages`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let b = david test
        // The bound is 1 MB; the seam lowers it so the seed's pages (tens of KB) are over it.
        stats.MaxBody <- 512
        for path in fourPages do
            for _ in 1..2 do
                let! reply = b.Send(gz path)
                Assert.Equal(200, reply.Status)
        Assert.Equal(0L, stats.Stores)
        Assert.Equal(0L, stats.Hits)
        // The default bound keeps each of them.
        stats.MaxBody <- 1048576
        for path in fourPages do
            let! _ = b.Send(gz path)
            let hits = stats.Hits
            let! _ = b.Send(gz path)
            Assert.Equal(hits + 1L, stats.Hits)
    }

[<Fact>]
let ``PC1b a second GET of the sidebar and the search page is a hit with the render's bytes, and a search bumps nothing`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let! _ = (david test).Send(get room)
        for path in [ sidebar; search ] do
            let b = david test
            // The first request has no `last_room` cookie and sets one, so the second is another key: warm both.
            let! _ = b.Send(gz path)
            let! _ = b.Send(gz path)
            let hits = stats.Hits
            let epoch = test.App.Gate.Epoch
            let! first = b.Send(gz path)
            let! second = b.Send(gz path)
            Assert.Equal(200, first.Status)
            let cookies = String.Join(";", first.SetCookies) + " / " + String.Join(";", second.SetCookies)
            Assert.True(hits + 2L = stats.Hits, $"{path}: hits {hits} -> {stats.Hits}, cookies {cookies}")
            Assert.Equal<byte[]>(first.Body, second.Body)
            Assert.Equal<string>(String.Join("\n", comparable first), String.Join("\n", comparable second))
            Assert.Equal(epoch, test.App.Gate.Epoch)
    }

[<Fact>]
let ``PC18 a write from outside the app shows on a cached page once the entry's lifetime ends`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        // The lifetime is 60 s in production; the seam shortens it so the test waits for real.
        stats.Lifetime <- TimeSpan.FromSeconds 2.0
        let b = david test
        let! _ = b.Send(gz sidebar)
        let! warm = b.Send(gz sidebar)
        let hits = stats.Hits
        let! cached = b.Send(gz sidebar)
        Assert.Equal(hits + 1L, stats.Hits)
        Assert.DoesNotContain("Outside Write", text cached)
        // A raw connection: no gate, so no epoch bump.
        test.Sql(fun conn -> RawSql.run conn "UPDATE rooms SET name = ? WHERE id = ?" [ box "Outside Write"; box ALL_TALK ])
        let! stale = b.Send(gz sidebar)
        Assert.Equal<byte[]>(warm.Body, stale.Body)
        do! Task.Delay 2500
        let! fresh = b.Send(gz sidebar)
        Assert.Contains("Outside Write", text fresh)
    }

[<Fact>]
let ``PC15 a bot-key request to the messages page never looks up or stores`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let bot = anonymous test
        for _ in 1..2 do
            let! _ = bot.Send(gz $"{messages}?bot_key={BENDER_KEY}")
            ()
        Assert.Equal((0L, 0L, 0L), (stats.Lookups, stats.Hits, stats.Stores))
    }

[<Fact>]
let ``PC16 the four pages do not change with Referer`` () =
    task {
        // Rendered every time (the cache is off), so a page that read the referer would show it.
        use! test = bootSeededConfigured ignore [ "CAMPFIRE_RESPONSE_CACHE_MB", "0" ] "default"
        for path in fourPages do
            let! plain = (david test).Send(gz path)
            for referer in [ $"http://campfire.test{room}"; "http://campfire.test/searches?q=coffee"; "https://elsewhere.example/" ] do
                let! withReferer = (david test).Send((gz path).With("referer", referer))
                Assert.Equal(200, withReferer.Status)
                Assert.Equal<string>(text plain, text withReferer)
    }

[<Fact>]
let ``PC17 sixteen readers and one writer: after each post's 2xx, the writer's next GET has its message`` () =
    task {
        use! test = bootSeeded "default"
        let stop = DateTime.UtcNow.AddSeconds 3.0
        let readers =
            [ for _ in 1..16 ->
                  let b = david test
                  Task.Run(fun () ->
                      task {
                          while DateTime.UtcNow < stop do
                              let! reply = b.Send(gz room)
                              Assert.Equal(200, reply.Status)
                      }
                      :> Task) ]
        let writer = david test
        let posted = ref 0
        let missing = ref []
        let write =
            Task.Run(fun () ->
                task {
                    let mutable n = 0
                    while DateTime.UtcNow < stop do
                        n <- n + 1
                        let marker = $"pc17 marker {n}"
                        let! reply = post writer marker
                        if reply.Status >= 200 && reply.Status < 300 then
                            Interlocked.Increment posted |> ignore
                            for path in [ room; messages ] do
                                let! page = writer.Send(gz path)
                                if not ((text page).Contains marker) then lock missing (fun () -> missing.Value <- $"{path} lacks {marker}" :: missing.Value)
                }
                :> Task)
        do! Task.WhenAll(write :: readers)
        Assert.True(posted.Value > 0)
        Assert.Empty(missing.Value)
    }

// --- Security review of Phases 9 and 10 (plans/design-perf.md section 11) ------------------------------------------------

[<Fact>]
let ``PC19 a commit after the before-actions read the room and before the lookup leaves that render unstored`` () =
    task {
        use! test = bootSeeded "default"
        let stats = stats test
        let b = david test
        // Warm: the stale seed session resumes and `last_room` is set, so the next requests are one key.
        let! _ = b.Send(gz room)
        let! _ = b.Send(gz room)
        let app = test.App
        let fired = ref 0
        // A rename commits after `set_room` read the room (and `restore_authentication` the user), before the page cache takes its key:
        // the render below is made from the rows of before the commit.
        stats.BeforeLookup <-
            Action(fun () ->
                if Interlocked.Increment fired = 1 then
                    use context = app.DbFactory.CreateDbContext()
                    let written =
                        (Ef.Writes.run app.Gate app.Env context (fun tx ->
                            tx.Db.Database.ExecuteSql(
                                FormattableStringFactory.Create("UPDATE rooms SET name = {0} WHERE id = {1}", [| box "Pcnineteen Renamed"; box ALL_TALK |])
                            )
                            |> ignore))
                            .GetAwaiter()
                            .GetResult()
                    Assert.True(Result.isOk written))
        let! during = b.Send(gz room)
        Assert.Equal(200, during.Status)
        Assert.Equal(1, fired.Value)
        // The premise: this render shows the name `set_room` read, from before the rename.
        Assert.DoesNotContain("Pcnineteen Renamed", text during)
        stats.BeforeLookup <- Action(ignore)
        // A client that reloads after the rename's broadcast must not get that render back.
        let! after = b.Send(gz room)
        Assert.Equal(200, after.Status)
        Assert.Contains("Pcnineteen Renamed", text after)
    }

[<Fact>]
let ``PC20 entries made under keys a signed-in client controls stay inside the memory cache's size limit`` () =
    task {
        // 1 MB of fragments plus 1 MB of pages: one memory cache with a 2 MB limit.
        use! test = bootSeededConfigured ignore [ "CAMPFIRE_RESPONSE_CACHE_MB", "1"; "CAMPFIRE_FRAGMENT_CACHE_MB", "1" ] "default"
        let stats = stats test
        let memory = test.Host.Services.GetRequiredService<IMemoryCache>() :?> MemoryCache
        let b = david test
        let! _ = b.Send(gz room)
        let! first = b.Send(gz room)
        Assert.Equal(200, first.Status)
        // Each User-Agent (and Host, query, Accept) is a key of its own: 300 of them, each page tens of KB, several times the limit.
        for i in 1..300 do
            let! reply = b.Send((gz $"{room}?n={i}").With("user-agent", $"Mozilla/5.0 PC20/{i}"))
            Assert.Equal(200, reply.Status)
        Assert.True(stats.Stores >= 300L, $"stores {stats.Stores}")
        let statistics = nonNull (memory.GetCurrentStatistics())
        let size = statistics.CurrentEstimatedSize.GetValueOrDefault()
        Assert.True(size > 0L, "entries carry a size")
        Assert.True(size <= 2L * 1024L * 1024L, $"estimated size {size} over the 2 MB limit, {memory.Count} entries")
    }
