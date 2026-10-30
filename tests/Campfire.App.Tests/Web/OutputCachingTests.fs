// plans/design-front.md section 5, unit 7.2: OC1-OC9 (the front server's response cache is OutputCache with one base policy per Rails
// public route family, F9).
module Campfire.App.Tests.Web.OutputCachingTests

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.OutputCaching
open Microsoft.AspNetCore.ResponseCompression
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Xunit
open Campfire.App.Tests.Support
open Campfire.App.Web

let private write (http: HttpContext) (text: string) : Task =
    Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(http.Response, text)

/// A store that remembers every key looked up and every key stored (what OC6 and OC7 ask).
type RecordingStore() =
    let entries = ConcurrentDictionary<string, struct (byte[] * DateTime)>()
    member val Gets = ConcurrentQueue<string>()
    member val Sets = ConcurrentQueue<string>()
    interface IOutputCacheStore with
        member _.EvictByTagAsync(_, _) = ValueTask.CompletedTask
        member this.GetAsync(key, _) =
            this.Gets.Enqueue key
            let found: byte[] | null = (match entries.TryGetValue key with | true, struct (v, until) when until > DateTime.UtcNow -> v | _ -> null)
            ValueTask<byte[] | null>(found)
        member this.SetAsync(key, value, _, validFor, _) =
            this.Sets.Enqueue key
            entries[key] <- struct (value, DateTime.UtcNow + validFor)
            ValueTask.CompletedTask

let private svg = String.replicate 60 "<circle r=\"1\"/>"

/// Rails' routes by their path, as stubs: how many times the app ran for each target is what the tests count.
type App() =
    member val Runs = ConcurrentDictionary<string, int>()
    member this.Count(path: string) : int = match this.Runs.TryGetValue path with | true, n -> n | _ -> 0
    member this.Handle(http: HttpContext) : Task =
        let path = http.Request.Path.Value |> Option.ofObj |> Option.defaultValue ""
        this.Runs.AddOrUpdate(path, 1, fun _ n -> n + 1) |> ignore
        http.Response.ContentType <- "image/svg+xml"
        match path with
        | "/rooms/1" ->
            task {
                do! Task.Delay 300
                do! write http "<html>page</html>"
            }
        | "/users/9/avatar" ->
            // The hourly session resume: a response that sets a cookie.
            http.Response.Headers.Append("Set-Cookie", "session_token=abc; path=/")
            write http svg
        | "/users/7/avatar" when not (http.Request.Headers.ContainsKey "Cookie") ->
            // Rails' answer to a client with no session: the sign-in redirect, never a cacheable 200.
            http.Response.StatusCode <- 302
            http.Response.Headers.Location <- "/session/new"
            Task.CompletedTask
        | "/users/8/avatar" ->
            // The way the session middleware and cookie authentication add theirs: as the response starts, after the action.
            http.Response.OnStarting(fun () ->
                http.Response.Headers.Append("Set-Cookie", "session_token=user-8-secret; path=/")
                Task.CompletedTask)
            // A HEAD answered without a body write: the response starts only after the middleware has returned.
            if HttpMethods.IsHead http.Request.Method then
                http.Response.ContentLength <- int64 svg.Length
                Task.CompletedTask
            else
                write http svg
        | _ when http.Request.Headers.ContainsKey "Range" ->
            http.Response.StatusCode <- 206
            write http "<circle"
        | _ ->
            http.Response.Headers.ETag <- "\"v1\""
            write http svg

let private startCache (configure: OutputCacheOptions -> unit) (store: RecordingStore) (app: App) : Task<Host> =
    startPipelineOn
        (WebApplicationOptions())
        ignore
        (fun services ->
            services.AddSingleton<IOutputCacheStore>(store) |> ignore
            services.AddOutputCache(configure) |> ignore
            services.AddResponseCompression(Compression.configure) |> ignore)
        (fun builder ->
            builder.UseOutputCache().UseResponseCompression() |> ignore
            builder.Run(RequestDelegate(fun http -> app.Handle http)))

let private production = OutputCaching.configure (64L * 1024L * 1024L) (1024L * 1024L)

let private avatarAge (reply: Reply) = reply.Header "age"

[<Fact>]
let ``OC1 an avatar is served once by the app and then from the cache, with Age`` () =
    task {
        let app = App()
        use! host = startCache production (RecordingStore()) app
        let! first = host.Send(get "/users/1/avatar")
        let! second = host.Send(get "/users/1/avatar")
        Assert.Equal(200, first.Status)
        Assert.Equal(200, second.Status)
        Assert.Equal(1, app.Count "/users/1/avatar")
        Assert.True((avatarAge first).IsNone)
        Assert.True((avatarAge second).IsSome)
        Assert.Equal<byte[]>(first.Body, second.Body)
    }

[<Fact>]
let ``OC2 each family's lifetime is its controller's constant, and an entry expires`` () =
    task {
        let seconds = OutputCaching.families |> List.map (fun (name, _, age) -> name, age.TotalSeconds) |> Map.ofList
        Assert.Equal(float OutputCaching.AvatarMaxAge, seconds["avatar"])
        Assert.Equal(1800.0, seconds["avatar"])
        Assert.Equal(300.0, seconds["account logo"])
        Assert.Equal(31_556_952.0, seconds["qr code"])
        Assert.Equal(3_155_695_200.0, seconds["active storage proxy"])
        Assert.Equal(3600.0, seconds["active storage disk"])
        // A family with a 1 s lifetime: a hit at once, a miss after 2.5 s (a slow host only makes the entry older).
        let app = App()
        let short (options: OutputCacheOptions) =
            options.AddBasePolicy(fun (builder: OutputCachePolicyBuilder) ->
                builder
                    .With(Func<OutputCacheContext, bool>(fun c -> c.HttpContext.Request.Path.Value = "/users/3/avatar"))
                    .Expire(TimeSpan.FromSeconds 1.0)
                    .SetVaryByHeader("Accept-Encoding")
                |> ignore)
        use! host = startCache short (RecordingStore()) app
        let! _ = host.Send(get "/users/3/avatar")
        let! _ = host.Send(get "/users/3/avatar")
        Assert.Equal(1, app.Count "/users/3/avatar")
        do! Task.Delay 2500
        let! _ = host.Send(get "/users/3/avatar")
        Assert.Equal(2, app.Count "/users/3/avatar")
    }

[<Fact>]
let ``OC3 a different query and a different case of the path are misses`` () =
    task {
        let app = App()
        use! host = startCache production (RecordingStore()) app
        let! _ = host.Send(get "/users/1/avatar?v=1")
        let! _ = host.Send(get "/users/1/avatar?v=1")
        Assert.Equal(1, app.Count "/users/1/avatar")
        let! _ = host.Send(get "/users/1/avatar?v=2")
        Assert.Equal(2, app.Count "/users/1/avatar")
        let! _ = host.Send(get "/USERS/1/avatar")
        let! _ = host.Send(get "/USERS/1/avatar")
        Assert.Equal(2, app.Count "/USERS/1/avatar")
    }

[<Fact>]
let ``OC4 a Range request bypasses the cache and gets 206 from the app`` () =
    task {
        let app = App()
        use! host = startCache production (RecordingStore()) app
        let! _ = host.Send(get "/users/1/avatar")
        let! ranged = host.Send((get "/users/1/avatar").With("range", "bytes=0-6"))
        Assert.Equal(206, ranged.Status)
        Assert.Equal(2, app.Count "/users/1/avatar")
        let! again = host.Send((get "/users/1/avatar").With("range", "bytes=0-6"))
        Assert.Equal(206, again.Status)
        Assert.Equal(3, app.Count "/users/1/avatar")
    }

[<Fact>]
let ``OC5 If-None-Match with the entry's ETag gets 304 without the app`` () =
    task {
        let app = App()
        use! host = startCache production (RecordingStore()) app
        let! first = host.Send(get "/users/1/avatar")
        Assert.Equal(Some "\"v1\"", first.Header "etag")
        let! conditional = host.Send((get "/users/1/avatar").With("if-none-match", "\"v1\""))
        Assert.Equal(304, conditional.Status)
        Assert.Equal(1, app.Count "/users/1/avatar")
    }

[<Fact>]
let ``OC6 a room page is never looked up, stored or locked`` () =
    task {
        let app = App()
        let store = RecordingStore()
        use! host = startCache production store app
        let clock = Stopwatch.StartNew()
        let! replies = Task.WhenAll [ for _ in 1..16 -> host.Send(get "/rooms/1") ]
        clock.Stop()
        Assert.All(replies, fun reply -> Assert.Equal(200, reply.Status))
        Assert.Equal(16, app.Count "/rooms/1")
        // Locked, 16 requests would queue behind one page (16 x 300 ms); unlocked they overlap.
        Assert.True(clock.ElapsedMilliseconds < 2000L, $"16 concurrent pages took {clock.ElapsedMilliseconds} ms")
        Assert.Empty(store.Gets)
        Assert.Empty(store.Sets)
    }

[<Fact>]
let ``OC7 a response that sets a cookie is not stored and the next request reaches the app`` () =
    task {
        let app = App()
        let store = RecordingStore()
        use! host = startCache production store app
        let! first = host.Send(get "/users/9/avatar")
        Assert.NotEmpty first.SetCookies
        let! second = host.Send(get "/users/9/avatar")
        Assert.NotEmpty second.SetCookies
        Assert.Equal(2, app.Count "/users/9/avatar")
        Assert.Empty(store.Sets)
    }

[<Fact>]
let ``OC8 gzip, identity and br clients each get their encoding from their own entry`` () =
    task {
        let app = App()
        use! host = startCache production (RecordingStore()) app
        let encodings = [ "gzip", Some "gzip"; "identity", None; "gzip, deflate, br, zstd", Some "br" ]
        for round in 1..2 do
            for (accept, expected) in encodings do
                let! reply = host.Send((get "/users/1/avatar").With("accept-encoding", accept))
                Assert.Equal(200, reply.Status)
                Assert.Equal(expected, reply.Header "content-encoding")
                if round = 2 then Assert.True((reply.Header "age").IsSome, $"{accept} was not a hit")
        // One run of the app per encoding, however many requests.
        Assert.Equal(3, app.Count "/users/1/avatar")
    }

[<Fact>]
let ``OC9 each family's predicate matches Rails' route shapes and not a page that shares a prefix`` () =
    let matches (name: string) (path: string) =
        let (_, predicate, _) = OutputCaching.families |> List.find (fun (n, _, _) -> n = name)
        predicate (PathString path)
    let anywhere (path: string) = OutputCaching.families |> List.exists (fun (_, predicate, _) -> predicate (PathString path))
    for path in [ "/users/12/avatar"; "/users/12/avatar.svg"; "/users/eyJf-abc--123/avatar" ] do
        Assert.True(matches "avatar" path, path)
    for path in [ "/account/logo"; "/account/logo.png" ] do
        Assert.True(matches "account logo" path, path)
    for path in [ "/qr_code/aHR0cHM6Ly9l"; "/qr_code/aHR0cHM6Ly9l.svg" ] do
        Assert.True(matches "qr code" path, path)
    for path in
        [ "/rails/active_storage/blobs/proxy/SIGNED/photo.jpg"
          "/rails/active_storage/blobs/proxy/SIGNED/dir/photo.jpg"
          "/rails/active_storage/representations/proxy/BLOB/VARIATION/photo.jpg" ] do
        Assert.True(matches "active storage proxy" path, path)
    Assert.True(matches "active storage disk" "/rails/active_storage/disk/KEY/photo.jpg")
    for path in
        [ "/users/1/profile"; "/users/1"; "/users/1/avatar/extra"; "/users//avatar"; "/users/avatar"; "/USERS/1/avatar"
          "/account/edit"; "/account/logo/extra"; "/account"; "/qr_code"; "/qr_code/"; "/qr_code/a/b"
          "/rails/active_storage/direct_uploads"; "/rails/active_storage/disk/TOKEN"
          "/rails/active_storage/blobs/redirect/SIGNED/photo.jpg"; "/rails/active_storage/blobs/SIGNED/photo.jpg"
          "/rails/active_storage/blobs/proxy/SIGNED"; "/rooms/1"; "/rooms/1/@123"; "/"; "/assets/app-123.js" ] do
        Assert.False(anywhere path, path)

// --- Security review (plans/design-front.md section 11) ------------------------------------------------------

[<Fact>]
let ``SR6 a cookie added as the response starts is never stored or replayed to another client, for GET or HEAD`` () =
    task {
        let app = App()
        let store = RecordingStore()
        use! host = startCache production store app
        let accept = "gzip, deflate, br, zstd"
        // The signed-in client's GET (compressed, so the body reaches the cache's stream only after the action) gets its cookie...
        let! mine = host.Send((get "/users/8/avatar").With("accept-encoding", accept).With("cookie", "session_token=old"))
        Assert.Contains(mine.SetCookies, fun c -> c.Contains "user-8-secret")
        // ...and the entry is not stored: the next client, with no cookie, reaches the app and is not handed the first one's.
        Assert.Empty(store.Sets)
        let! other = host.Send((get "/users/8/avatar").With("accept-encoding", accept))
        Assert.Equal(2, app.Count "/users/8/avatar")
        Assert.Contains(other.SetCookies, fun c -> c.Contains "user-8-secret")
        // HEAD: the response has not started when the action returns (no body), so the cookie comes after the cache's check. The
        // entry is stored (without the cookie), and a hit from it hands the next client none.
        let! firstHead = host.Send((request "HEAD" "/users/8/avatar").With("cookie", "session_token=old"))
        Assert.Contains(firstHead.SetCookies, fun c -> c.Contains "user-8-secret")
        let runs = app.Count "/users/8/avatar"
        let! secondHead = host.Send(request "HEAD" "/users/8/avatar")
        Assert.Equal(runs, app.Count "/users/8/avatar")
        Assert.Empty(secondHead.SetCookies)
        Assert.All(store.Sets, fun key -> Assert.StartsWith("HEAD", key))
    }

[<Fact>]
let ``SR8 a request with Authorization neither reads nor fills the cache, and only a 200 is ever stored`` () =
    task {
        let app = App()
        let store = RecordingStore()
        use! host = startCache production store app
        let! filled = host.Send(get "/users/1/avatar")
        Assert.Equal(200, filled.Status)
        Assert.Equal(1, store.Sets.Count)
        let gets = store.Gets.Count
        // An Authorization header (a bot or an API client): the app answers, the entry is neither read nor replaced.
        let! authorized = host.Send((get "/users/1/avatar").With("authorization", "Bearer secret"))
        Assert.Equal(200, authorized.Status)
        Assert.Equal(None, authorized.Header "age")
        Assert.Equal(2, app.Count "/users/1/avatar")
        Assert.Equal(gets, store.Gets.Count)
        Assert.Equal(1, store.Sets.Count)
        // A redirect (the anonymous answer of a signed-in route) is not stored, so it can never be replayed to a signed-in client.
        let! anonymous = host.Send(get "/users/7/avatar")
        Assert.Equal(302, anonymous.Status)
        Assert.Equal(1, store.Sets.Count)
        let! signedIn = host.Send((get "/users/7/avatar").With("cookie", "session_token=x"))
        Assert.Equal(200, signedIn.Status)
        Assert.Equal(2, app.Count "/users/7/avatar")
    }
