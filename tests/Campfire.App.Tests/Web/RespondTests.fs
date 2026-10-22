// plans/design-kit-auth.md section 5, unit 2.8 (Responses and compression): X1-X5. `Web.Respond` over a small pipeline on the app's
// own services (redirects, conditional GET, `expires_in`, `send_file`), and the framework's response compression (K11) over
// the whole app.
module Campfire.App.Tests.Web.RespondTests

open System
open System.IO
open System.IO.Compression
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Xunit
open Campfire.App.Tests.Support
open Campfire.App.Web

/// Runs `routes` as web actions, on the session and the services of the booted app.
let private startRoutes (test: Test) (routes: HttpContext -> Task<Result<HttpHandler, Campfire.App.Web.Error>>) : Task<Host> =
    startPipelineWith test.Booted.ConfigureServices (fun app ->
        app.UseCookiePolicy() |> ignore
        app.UseSession() |> ignore
        app.Run(RequestDelegate(fun http -> Action.run routes http)))

let private ok (result: Result<HttpHandler, Campfire.App.Web.Error>) : Task<Result<HttpHandler, Campfire.App.Web.Error>> = Task.FromResult result

let private gunzip (bytes: byte[]) : byte[] =
    use input = new MemoryStream(bytes)
    use decoder = new GZipStream(input, CompressionMode.Decompress)
    use output = new MemoryStream()
    decoder.CopyTo output
    output.ToArray()

let private unbrotli (bytes: byte[]) : byte[] =
    use input = new MemoryStream(bytes)
    use decoder = new BrotliStream(input, CompressionMode.Decompress)
    use output = new MemoryStream()
    decoder.CopyTo output
    output.ToArray()

[<Fact>]
let ``X1b redirect_back refuses a Referer that backslash, userinfo or protocol-relative forms point at another host`` () =
    task {
        use! test = bootSeeded "default"
        let routes (http: HttpContext) =
            match http.Request.Path.Value with
            | "/back" -> ok (Respond.redirectBackOrTo http "/fallback")
            | _ -> ok (Result.Error NotFound)
        use! host = startRoutes test routes
        let back (referer: string) = host.Send((get "/back").With("referer", referer))
        // Same host, or a path: followed.
        for followed in [ "http://campfire.test/rooms/1"; "http://user@campfire.test/rooms/1" ] do
            let! reply = back followed
            Assert.Equal(Some followed, reply.Location)
        let! pathReply = back "/rooms/1"
        Assert.Equal(Some "http://campfire.test/rooms/1", pathReply.Location)
        // Another host, in every form a parser might disagree about: the fallback.
        for refused in
            [ "http://evil.com\\@campfire.test/"
              "http://campfire.test\\@evil.com/"
              "/\\evil.com"
              "\\\\evil.com"
              "//evil.com"
              "//evil.com/rooms/1"
              "http://evil.com/rooms/1"
              "http://campfire.test@evil.com/"
              "http://evil.com:80@campfire.test.evil.com/" ] do
            let! reply = back refused
            Assert.Equal(Some "http://campfire.test/fallback", reply.Location)
    }

[<Fact>]
let ``X1 redirects: paths and same-host URLs pass, another host or a relative path is an UnsafeRedirect`` () =
    task {
        use! test = bootSeeded "default"
        let routes (http: HttpContext) =
            match http.Request.Path.Value with
            | "/path" -> ok (Respond.redirectTo http "/rooms/1")
            | "/query" -> ok (Respond.redirectTo http "?page=2")
            | "/absolute" -> ok (Respond.redirectTo http "http://campfire.test/rooms/1")
            | "/other" -> ok (Respond.redirectTo http "http://evil.test/phish")
            | "/protocol-relative" -> ok (Respond.redirectTo http "//evil.test/phish")
            | "/relative" -> ok (Respond.redirectTo http "rooms/1")
            | "/allowed" -> ok (Respond.redirectToWith http "http://evil.test/ok" { RedirectOptions.Default with AllowOtherHost = true; Status = 303 })
            | "/back" -> ok (Respond.redirectBackOrTo http "/fallback")
            | "/notice" -> ok (Respond.redirectToWith http "/shown" { RedirectOptions.Default with Notice = "Saved" })
            | "/shown" ->
                let text = match (Respond.flash http).Notice with null -> "none" | notice -> notice
                ok (Ok(Response.ofPlainText text))
            | _ -> ok (Result.Error NotFound)
        use! host = startRoutes test routes
        let send (path: string) = host.Send(get path)

        let! path = send "/path"
        Assert.Equal(302, path.Status)
        Assert.Equal(Some "http://campfire.test/rooms/1", path.Location)
        let! query = send "/query"
        Assert.Equal(Some "http://campfire.test?page=2", query.Location)
        let! absolute = send "/absolute"
        Assert.Equal(Some "http://campfire.test/rooms/1", absolute.Location)

        // The refusals are Rails' `UnsafeRedirectError`: a 500 with the public page.
        for refused in [ "/other"; "/protocol-relative"; "/relative" ] do
            let! reply = send refused
            Assert.Equal(500, reply.Status)
            Assert.Equal(None, reply.Location)
        let! allowed = send "/allowed"
        Assert.Equal(303, allowed.Status)
        Assert.Equal(Some "http://evil.test/ok", allowed.Location)

        // `redirect_back_or_to`: the referer on this host, else the fallback.
        let! back = host.Send((get "/back").With("referer", "http://campfire.test/rooms/2?x=1"))
        Assert.Equal(Some "http://campfire.test/rooms/2?x=1", back.Location)
        let! foreign = host.Send((get "/back").With("referer", "http://evil.test/"))
        Assert.Equal(Some "http://campfire.test/fallback", foreign.Location)
        let! none = send "/back"
        Assert.Equal(Some "http://campfire.test/fallback", none.Location)

        // The notice rides the session cookie to the next request, once.
        let! noticed = send "/notice"
        Assert.Equal(302, noticed.Status)
        let cookie = noticed.SetCookies |> List.find (fun c -> c.StartsWith "_campfire_flash=") |> fun c -> c.Split(';')[0]
        let! shown = host.Send((get "/shown").With("cookie", cookie))
        Assert.Equal("Saved", shown.Text)
    }

[<Fact>]
let ``X2 fresh_when answers 304 for a matching ETag or a recent enough Last-Modified`` () =
    task {
        use! test = bootSeeded "default"
        let modified = DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero)
        let routes (http: HttpContext) =
            let body = Ok(Response.ofPlainText "page")
            match http.Request.Path.Value with
            | "/etag" ->
                match Respond.freshWhen http (Freshness.OfEtag "record/1-20260302") with
                | ValueSome notModified -> ok (Ok notModified)
                | ValueNone -> ok body
            | "/public" ->
                match Respond.freshWhen http { Freshness.OfEtag "record/1" with Public = true } with
                | ValueSome notModified -> ok (Ok notModified)
                | ValueNone -> ok body
            | "/dated" ->
                match Respond.freshWhen http { Freshness.Default with LastModified = ValueSome modified } with
                | ValueSome notModified -> ok (Ok notModified)
                | ValueNone -> ok body
            | "/stale" ->
                match Respond.stale http (Freshness.OfEtag "record/2") with
                | Result.Error notModified -> ok (Ok notModified)
                | Ok() -> ok body
            | _ -> ok (Result.Error NotFound)
        use! host = startRoutes test routes

        let! first = host.Send(get "/etag")
        Assert.Equal(200, first.Status)
        Assert.Equal("page", first.Text)
        let etag = (first.Header "etag").Value
        Assert.StartsWith("W/\"", etag)
        Assert.Equal(Some "max-age=0, private, must-revalidate", first.Header "cache-control")

        // The validator it handed out, or `*`, or a list that includes it: 304, still with the validator, without a body.
        for ifNoneMatch in [ etag; "*"; $"\"other\", {etag}" ] do
            let! again = host.Send((get "/etag").With("if-none-match", ifNoneMatch))
            Assert.Equal(304, again.Status)
            Assert.Empty again.Body
            Assert.Equal(Some etag, again.Header "etag")
        let! changed = host.Send((get "/etag").With("if-none-match", "W/\"0123456789abcdef0123456789abcdef\""))
        Assert.Equal(200, changed.Status)

        // A Turbo Frame request is another representation of the same record.
        let! framed = host.Send((get "/etag").With("turbo-frame", "messages"))
        Assert.NotEqual(Some etag, framed.Header "etag")

        // `public: true`.
        let! shared = host.Send(get "/public")
        Assert.Equal(Some "max-age=0, public, must-revalidate", shared.Header "cache-control")

        // `Last-Modified`: an `If-Modified-Since` no earlier than it is fresh, an earlier one is not.
        let! dated = host.Send(get "/dated")
        Assert.Equal(200, dated.Status)
        Assert.Equal(Some "Mon, 02 Mar 2026 16:00:00 GMT", dated.Header "last-modified")
        let! since = host.Send((get "/dated").With("if-modified-since", "Mon, 02 Mar 2026 16:00:00 GMT"))
        Assert.Equal(304, since.Status)
        let! later = host.Send((get "/dated").With("if-modified-since", "Tue, 03 Mar 2026 16:00:00 GMT"))
        Assert.Equal(304, later.Status)
        let! earlier = host.Send((get "/dated").With("if-modified-since", "Sun, 01 Mar 2026 16:00:00 GMT"))
        Assert.Equal(200, earlier.Status)
        // With strict freshness an `If-None-Match` that does not match settles it, whatever `If-Modified-Since` says.
        let! stricter = host.Send((get "/etag").With("if-none-match", "\"nope\"").With("if-modified-since", "Tue, 03 Mar 2026 16:00:00 GMT"))
        Assert.Equal(200, stricter.Status)

        // `stale?` is the same check, as a branch.
        let! staleFirst = host.Send(get "/stale")
        Assert.Equal(200, staleFirst.Status)
        let! staleAgain = host.Send((get "/stale").With("if-none-match", (staleFirst.Header "etag").Value))
        Assert.Equal(304, staleAgain.Status)
    }

[<Fact>]
let ``X3 expires_in writes Rails' Cache-Control, public and stale-while-revalidate`` () =
    task {
        use! test = bootSeeded "default"
        let routes (http: HttpContext) =
            match http.Request.Path.Value with
            | "/avatar" ->
                Respond.expiresIn http 300UL { ExpiresIn.Default with Public = true; StaleWhileRevalidate = ValueSome 1800UL }
                ok (Ok(Response.ofPlainText "avatar"))
            | "/private" ->
                Respond.expiresIn http 60UL ExpiresIn.Default
                ok (Ok(Response.ofPlainText "private"))
            | "/immutable" ->
                Respond.expiresIn http 3153600000UL { ExpiresIn.Default with Public = true; Immutable = true }
                ok (Ok(Response.ofPlainText "blob"))
            | _ -> ok (Result.Error NotFound)
        use! host = startRoutes test routes
        let! avatar = host.Send(get "/avatar")
        Assert.Equal(Some "max-age=300, public, stale-while-revalidate=1800", avatar.Header "cache-control")
        Assert.True((avatar.Header "date").IsSome)
        let! priv = host.Send(get "/private")
        Assert.Equal(Some "max-age=60, private", priv.Header "cache-control")
        let! immutable = host.Send(get "/immutable")
        Assert.Equal(Some "max-age=3153600000, public, immutable", immutable.Header "cache-control")
    }

/// The page, as the app sends it, with and without a compression the client asks for.
[<Fact>]
let ``X4 HTML and turbo-stream responses are gzipped or Brotli'd once, with Vary, and small ones are not`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let page (encoding: string option) =
            let req = get $"/rooms/{ALL_TALK}"
            b.Send(match encoding with Some e -> req.With("accept-encoding", e) | None -> req)

        let! plain = page None
        Assert.Equal(200, plain.Status)
        Assert.Equal(None, plain.Header "content-encoding")
        Assert.True((plain.Body.Length > 1000), "a room page is not small")

        let! gzipped = page (Some "gzip")
        Assert.Equal(Some "gzip", gzipped.Header "content-encoding")
        Assert.Equal([ "gzip" ], gzipped.HeaderValues "content-encoding")
        Assert.True((String.Join(", ", gzipped.HeaderValues "vary")).Contains "Accept-Encoding", $"{gzipped.Headers}")
        // A room page is a page of cached fragments (an `HtmlOutput`): its compressed form is kept in HybridCache by the SHA-256 of
        // its parts and fragments, so the second request is the same bytes, and a change of the page is another page (P1).
        let! again = page (Some "gzip")
        Assert.Equal<byte[]>(gzipped.Body, again.Body)
        Assert.Equal(Some(string gzipped.Body.Length), gzipped.Header "content-length")
        Assert.Equal<byte[]>(plain.Body, gunzip gzipped.Body)
        Assert.True((gzipped.Body.Length < plain.Body.Length / 2), $"{gzipped.Body.Length} of {plain.Body.Length}")
        // The validators are the page's, compressed or not.
        Assert.Equal(plain.Header "etag", gzipped.Header "etag")

        let! brotli = page (Some "br")
        Assert.Equal([ "br" ], brotli.HeaderValues "content-encoding")
        Assert.Equal<byte[]>(plain.Body, unbrotli brotli.Body)

        // A client that takes both gets one of them, once; `identity` alone is no compression.
        let! both = page (Some "gzip, br")
        Assert.Equal(1, both.HeaderValues("content-encoding").Length)
        Assert.Equal<byte[]>(plain.Body, (match both.Header "content-encoding" with Some "br" -> unbrotli both.Body | _ -> gunzip both.Body))
        let! identity = page (Some "identity")
        Assert.Equal(None, identity.Header "content-encoding")

        // A turbo-stream write.
        let! stream =
            b.Write(
                (request "POST" $"/rooms/{ALL_TALK}/messages")
                    .With("accept", "text/vnd.turbo-stream.html, text/html, application/xhtml+xml")
                    .With("accept-encoding", "gzip")
                    .Form(formBody [ "message[body]", "compressed hello" ])
            )
        Assert.Equal(200, stream.Status)
        Assert.Equal(Some "gzip", stream.Header "content-encoding")
        Assert.Contains("Accept-Encoding", String.Join(", ", stream.HeaderValues "vary"))
        Assert.Contains("<turbo-stream", Text.Encoding.UTF8.GetString(gunzip stream.Body))

        // Under `Compression.MinimumLength` the bytes go out as they are: the health page, an empty redirect.
        let! up = test.Send((get "/up").With("accept-encoding", "gzip"))
        Assert.Equal(200, up.Status)
        Assert.Equal(None, up.Header "content-encoding")
        let! redirect = test.Send((get $"/rooms/{ALL_TALK}").With("accept-encoding", "gzip"))
        Assert.Equal(302, redirect.Status)
        Assert.Equal(None, redirect.Header "content-encoding")
    }

/// An empty error (`head :not_found` for a wrong join code) goes out compressed when the client takes gzip or Brotli, as
/// `Rack::Deflater` sends it: Firefox shows its own "empty response" error page for an error document of zero bytes
/// (NS_ERROR_NET_EMPTY_RESPONSE in the full pixel matrix), and a blank page for a few bytes of gzip that decode to nothing.
[<Fact>]
let ``X4b an empty error response is compressed when the client takes it`` () =
    task {
        use! test = bootSeeded "default"
        let join (encoding: string option) =
            let req = get "/join/not-a-code"
            test.Send(match encoding with Some e -> req.With("accept-encoding", e) | None -> req)

        let! firefox = join (Some "gzip, deflate, br, zstd")
        Assert.Equal(404, firefox.Status)
        Assert.Equal(1, firefox.HeaderValues("content-encoding").Length)
        Assert.True(firefox.Body.Length > 0, "a compressed empty body has bytes on the wire")
        Assert.Empty(match firefox.Header "content-encoding" with Some "br" -> unbrotli firefox.Body | _ -> gunzip firefox.Body)
        let! gzipped = join (Some "gzip")
        Assert.Equal((404, Some "gzip"), (gzipped.Status, gzipped.Header "content-encoding"))
        Assert.Empty(gunzip gzipped.Body)
        let! head = test.Send((request "HEAD" "/join/not-a-code").With("accept-encoding", "gzip"))
        Assert.Equal(404, head.Status)
        Assert.Empty(head.Body)
        // No compression asked: as before, an empty body with its length.
        for encoding in [ None; Some "identity" ] do
            let! plain = join encoding
            Assert.Equal((404, None, Some "0"), (plain.Status, plain.Header "content-encoding", plain.Header "content-length"))
            Assert.Empty(plain.Body)
        // A success with no body is still sent as it is.
        let! up = test.Send((get "/up").With("accept-encoding", "gzip"))
        Assert.Equal((200, None), (up.Status, up.Header "content-encoding"))
    }

[<Fact>]
let ``X5 send_file answers ranges, with Rails' Content-Disposition`` () =
    task {
        use! test = bootSeeded "default"
        let path = Path.Combine(Path.GetTempPath(), "respond-x5-" + Guid.NewGuid().ToString("N") + ".txt")
        let content = Array.init 1000 (fun i -> byte ('a' + char (i % 26)))
        File.WriteAllBytes(path, content)
        try
            let routes (http: HttpContext) =
                match http.Request.Path.Value with
                | "/file" -> ok (Respond.sendFile path { SendOptions.Default with Filename = "notes ü.txt" })
                | "/inline" -> ok (Respond.sendFile path (SendOptions.Inline "text/plain"))
                // The storage directory may be relative (`storage/files/..`): `Results.File` would read it under the web root.
                | "/relative" -> ok (Respond.sendFile (Path.GetRelativePath(Environment.CurrentDirectory, path)) SendOptions.Default)
                | "/missing" -> ok (Respond.sendFile (path + ".gone") SendOptions.Default)
                | _ -> ok (Result.Error NotFound)
            use! host = startRoutes test routes

            let! whole = host.Send(get "/file")
            Assert.Equal(200, whole.Status)
            Assert.Equal<byte[]>(content, whole.Body)
            Assert.Equal(Some "bytes", whole.Header "accept-ranges")
            Assert.Equal(Some "text/plain", whole.Header "content-type")
            Assert.Equal(Some "attachment; filename=\"notes u.txt\"; filename*=UTF-8''notes%20%C3%BC.txt", whole.Header "content-disposition")

            let! part = host.Send((get "/file").With("range", "bytes=10-19"))
            Assert.Equal(206, part.Status)
            Assert.Equal(Some "bytes 10-19/1000", part.Header "content-range")
            Assert.Equal<byte[]>(content[10..19], part.Body)
            let! tail = host.Send((get "/file").With("range", "bytes=-5"))
            Assert.Equal(206, tail.Status)
            Assert.Equal<byte[]>(content[995..], tail.Body)
            let! beyond = host.Send((get "/file").With("range", "bytes=5000-"))
            Assert.Equal(416, beyond.Status)

            let! relative = host.Send(get "/relative")
            Assert.Equal(200, relative.Status)
            Assert.Equal<byte[]>(content, relative.Body)

            let! inline' = host.Send(get "/inline")
            Assert.StartsWith("inline; filename=", (inline'.Header "content-disposition").Value)
            // A file that cannot be opened is Rails' 500.
            let! missing = host.Send(get "/missing")
            Assert.Equal(500, missing.Status)
        finally
            File.Delete path
    }
