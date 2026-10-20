// Port of rust/crates/kit/tests/http.rs
//
// End-to-end behavior of the HTTP layer through a real Kestrel and the kit's pipeline. The Rust tests
// call an axum router in process; these send HTTP/1.1 to a loopback port. The last test of http.rs
// goes through the front server (`front::serve_plain`); its counterpart here serves with Kestrel
// itself, and checks the peer address and a graceful stop.
module Campfire.Kit.Tests.HttpTests

open System
open System.IO
open System.Text
open System.Threading.Tasks
open Falco
open Xunit
open Campfire.RailsCompat
open Campfire.Kit
open Campfire.Kit.Tests.Helpers
open Campfire.Kit.Tests.Harness
open Campfire.Kit.Tests.HttpApp

let private startApp (config: KitConfig) : Task<TestApp> =
    let kit = kitFor config
    start kit (routes kit)

let private defaultApp () = startApp plainConfig

/// The test app as Campfire runs it in production: behind TLS (`assume_ssl`) with `force_ssl`.
let private sslApp () = startApp sslConfig

let private header (reply: Reply) (name: string) : string | null =
    match reply.Header name with
    | Some v -> v
    | None -> null

[<Fact>]
let ``renders html with rails headers`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send(get "/rooms/5")
        Assert.Equal(200, reply.Status)
        Assert.Equal("<p>Campfire room 5</p>", reply.Text)
        Assert.Equal("text/html; charset=utf-8", header reply "content-type")
        Assert.Equal("SAMEORIGIN", header reply "x-frame-options")
        Assert.Equal("nosniff", header reply "x-content-type-options")
        Assert.Equal("strict-origin-when-cross-origin", header reply "referrer-policy")
        Assert.Equal("max-age=0, private, must-revalidate", header reply "cache-control")
        Assert.StartsWith("W/\"", header reply "etag")
        Assert.Equal(36, (nonNull (header reply "x-request-id")).Length)
        Assert.NotNull(header reply "x-runtime")
        Assert.Null(header reply "server")
        Assert.Empty reply.Cookies
    }

[<Fact>]
let ``conditional get on body etag`` () =
    task {
        use! app = defaultApp ()
        let! first = app.Send(get "/rooms/5")
        let etag = nonNull (header first "etag")
        let! second = app.Send((get "/rooms/5").With("if-none-match", etag))
        Assert.Equal(304, second.Status)
        Assert.Empty second.Body
        Assert.Null(header second "content-type")

        // Lists and `*` count, as they do for `fresh_when` (RFC 9110).
        for ifNoneMatch in [ $"\"other\", {etag}"; "*" ] do
            let! listed = app.Send((get "/rooms/5").With("if-none-match", ifNoneMatch))
            Assert.True((listed.Status = 304), ifNoneMatch)
        let! other = app.Send((get "/rooms/5").With("if-none-match", "\"other\""))
        Assert.Equal(200, other.Status)
    }

[<Fact>]
let ``head requests drop the body`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send((get "/rooms/5").AsMethod "HEAD")
        Assert.Equal(200, reply.Status)
        Assert.Empty reply.Body
        Assert.Equal("22", header reply "content-length")
    }

[<Fact>]
let ``unknown routes and methods are rails 404s`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send(get "/nope")
        Assert.Equal(404, reply.Status)
        Assert.Equal("<h1>Not found</h1>", reply.Text)
        let! reply = app.Send(request "PUT" "/rooms/1")
        Assert.Equal(404, reply.Status)
        let! reply = app.Send(get "/nope.json")
        Assert.Equal("""{"status":404,"error":"Not Found"}""", reply.Text)
    }

[<Fact>]
let ``params merge query over body and path over both`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send(formPost "/echo/9?b=query&id=q" "a[b][]=1&a[b][]=2&b=body&id=body")
        let json = reply.Json
        Assert.Equal(Some(Value.Object [ "a", Value.Object [ "b", Value.Array [ Value.String "1"; Value.String "2" ] ]; "b", Value.String "query"; "id", Value.String "9" ]), json.TryGet "params")
        // A form is the framework's (`Request.Form`, read once), so the raw bytes are not kept (class B).
        Assert.Equal(Some(Value.String ""), json.TryGet "raw")
    }

[<Fact>]
let ``malformed params are 400`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send(get "/echo/1?a=1&a[b]=2")
        Assert.Equal(400, reply.Status)
        // The framework's form reader takes a stray `%` literally where Rack refuses it (class B).
        let! reply = app.Send(formPost "/echo/1" "a=%")
        Assert.Equal(200, reply.Status)
        let! reply = app.Send((post "/echo/1").With("content-type", "application/json").WithBody "{nope")
        Assert.Equal(400, reply.Status)
    }

let private member' (name: string) (value: Value) : Value option = value.TryGet name

[<Fact>]
let ``method override from form param and header`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send(formPost "/echo/1" "_method=patch&x=1")
        Assert.Equal(Some(Value.String "PATCH"), member' "method" reply.Json)
        Assert.Equal(Some(Value.String "POST"), member' "original_method" reply.Json)

        let! reply = app.Send(formPost "/echo/1" "_method=delete")
        Assert.Equal(Some(Value.String "DELETE"), member' "method" reply.Json)

        // `X-HTTP-Method-Override` is not read: `_method` is the only override (README Known differences).
        let! reply = app.Send((post "/echo/1").With("x-http-method-override", "patch").WithBody "")
        Assert.Equal(Some(Value.String "POST"), member' "method" reply.Json)

        // JSON bodies aren't forms: `_method` there doesn't count.
        let! reply =
            app.Send((post "/echo/1").With("content-type", "application/json").WithBody """{"_method":"patch","a":[1,null,2]}""")
        Assert.Equal(Some(Value.String "POST"), member' "method" reply.Json)
        Assert.Equal(Some(json "[1, 2]"), (member' "params" reply.Json) |> Option.bind (member' "a"))

        // A `_method` that is no HTTP verb makes the request that verb (upper-cased); no route answers it.
        let! reply = app.Send(formPost "/echo/1" "_method=bogus")
        Assert.Equal(404, reply.Status)
    }

[<Fact>]
let ``multipart uploads with method override`` () =
    task {
        use! app = defaultApp ()
        let boundary = "----campfire"
        // Unique to this run, so that finding it on disk afterwards means this request left it there.
        let content = "PNGDATA-" + Guid.NewGuid().ToString("N")
        let body =
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"_method\"\r\n\r\npatch\r\n\
              --{boundary}\r\nContent-Disposition: form-data; name=\"user[name]\"\r\n\r\nJo\r\n\
              --{boundary}\r\nContent-Disposition: form-data; name=\"user[avatar]\"; filename=\"me.png\"\r\nContent-Type: image/png\r\n\r\n{content}\r\n\
              --{boundary}--\r\n"
        let! reply = app.Send((post "/upload").With("content-type", $"multipart/form-data; boundary={boundary}").WithBody body)
        let expected = """{"method": "PATCH", "name": "Jo", "filename": "me.png", "content": "%CONTENT%"}""".Replace("%CONTENT%", content)
        Assert.Equal(json expected, reply.Json)
        // The spooled file is gone with the request.
        let spooled = Directory.GetFiles(Path.GetTempPath(), "RackMultipart*") |> Array.filter (fun f -> (try File.ReadAllText f = content with _ -> false))
        Assert.Empty spooled
    }

[<Fact>]
let ``missing required param is 400`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send(formPost "/upload" "other=1")
        Assert.Equal(400, reply.Status)
    }

[<Fact>]
let ``pages carry no forgery token or session`` () =
    task {
        use! app = defaultApp ()
        let! page = app.Send(get "/form")
        Assert.DoesNotContain("authenticity_token", page.Text)
        Assert.True(page.Cookies.IsEmpty, "rendering a form doesn't start a session")
    }

[<Fact>]
let ``session cookie is written only when the session changes`` () =
    task {
        use! app = defaultApp ()
        let! untouched = app.Send(get "/noop")
        Assert.Empty untouched.Cookies

        // Nothing stored and no cookie: reading starts no session.
        let! none = app.Send(get "/session")
        Assert.Equal(Some Value.Null, member' "value" none.Json)
        Assert.Empty none.Cookies

        let! set = app.Send(formPost "/session" "value=%2Frooms%2F1")
        Assert.Equal(200, set.Status)
        let cookie = set.CookieJar
        Assert.StartsWith("_campfire_flash=", cookie)

        // Reading takes it (`session.delete`), once.
        let! read = app.Send((get "/session").With("cookie", cookie))
        Assert.Equal(Some(Value.String "/rooms/1"), member' "value" read.Json)
        let! again = app.Send((get "/session").With("cookie", cookie))
        Assert.Equal(Some Value.Null, member' "value" again.Json)
        let! noop = app.Send((get "/noop").With("cookie", cookie))
        Assert.Empty noop.Cookies

        // Resetting clears the session and the browser's cookie.
        let! _ = app.Send((formPost "/session" "value=%2Frooms%2F2").With("cookie", cookie))
        let! reset = app.Send((request "DELETE" "/session").With("cookie", cookie))
        Assert.True((reset.Cookies |> List.exists (fun c -> c.StartsWith "_campfire_flash=;")), $"{reset.Cookies}")
        let! after = app.Send((get "/session").With("cookie", cookie))
        Assert.Equal(Some Value.Null, member' "value" after.Json)

        // A cookie naming no session reads as an empty one.
        let! bogus = app.Send((get "/session").With("cookie", "_campfire_flash=garbage"))
        Assert.Equal(Some Value.Null, member' "value" bogus.Json)
    }

[<Fact>]
let ``flash survives exactly one redirect`` () =
    task {
        use! app = defaultApp ()
        let! redirect = app.Send(get "/notice")
        Assert.Equal(302, redirect.Status)
        Assert.Equal(Some "http://chat.example.com/flash", redirect.Header "location")
        let cookie = redirect.CookieJar

        let! shown = app.Send((get "/flash").With("cookie", cookie))
        Assert.Equal(Some(Value.String "✓"), member' "notice" shown.Json)
        // Shown, the flash is swept from the session.
        let! gone = app.Send((get "/flash").With("cookie", shown.CookieJar))
        Assert.Equal(Some Value.Null, member' "notice" gone.Json)
    }

[<Fact>]
let ``signed permanent cookies and deletion`` () =
    task {
        use! app = defaultApp ()
        let! signedIn = app.Send(get "/sign_in")
        let cookies = signedIn.Cookies
        Assert.True(
            cookies
            |> List.exists (fun c ->
                c.StartsWith "session_token="
                && c.EndsWith "; path=/; expires=Wed, 01 Jun 2044 12:00:00 GMT; httponly; samesite=lax")
        )
        Assert.Contains("last_room=7; path=/; expires=Wed, 01 Jun 2044 12:00:00 GMT; samesite=lax", cookies)

        // Rails' Live responses send the action's cookies twice; this sends them once.
        let! live = app.Send(get "/live_sign_in")
        Assert.True((live.Cookies.Length = 2), $"{live.Cookies}")

        let jar = signedIn.CookieJar
        let! me = app.Send((get "/whoami").With("cookie", jar))
        Assert.Equal(json """{"token": "tok123", "last_room": "7"}""", me.Json)

        let! forged = app.Send((get "/whoami").With("cookie", "session_token=tok123"))
        Assert.Equal(Some Value.Null, member' "token" forged.Json)

        let! out = app.Send((get "/sign_out").With("cookie", jar))
        Assert.Equal<string list>(
            [ "session_token=; path=/; max-age=0; expires=Thu, 01 Jan 1970 00:00:00 GMT; samesite=lax" ],
            out.Cookies
        )
        let! nothing = app.Send(get "/sign_out")
        Assert.Empty nothing.Cookies
    }

[<Fact>]
let ``before actions halt the chain`` () =
    task {
        use! app = defaultApp ()
        let! anonymous = app.Send(get "/admin")
        Assert.Equal(302, anonymous.Status)
        Assert.Equal(Some "http://chat.example.com/session/new", anonymous.Header "location")
        Assert.Equal(Some "42", anonymous.Header "x-version")

        let! member' = app.Send((get "/admin").With("x-user", "jo"))
        Assert.Equal(403, member'.Status)
        Assert.Equal(Some "text/html", member'.Header "content-type")
        Assert.Empty member'.Body

        let! admin = app.Send((get "/admin").With("x-user", "admin"))
        Assert.Equal("hi admin", admin.Text)
    }

[<Fact>]
let ``respond to negotiates like rails`` () =
    task {
        use! app = defaultApp ()
        let browser = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8"
        let! html = app.Send((get "/messages").With("accept", browser))
        Assert.Equal(Some "text/html; charset=utf-8", html.Header "content-type")

        let turbo = "text/vnd.turbo-stream.html, text/html, application/xhtml+xml"
        let! stream = app.Send((post "/messages").With("accept", turbo).WithBody "")
        Assert.Equal(Some "text/vnd.turbo-stream.html; charset=utf-8", stream.Header "content-type")

        let! json = app.Send(get "/messages.json")
        Assert.Equal(Some "application/json; charset=utf-8", json.Header "content-type")
        let! json = app.Send(get "/messages?format=json")
        Assert.Equal(Some "application/json; charset=utf-8", json.Header "content-type")

        let! unacceptable = app.Send((get "/messages").With("accept", "image/png"))
        Assert.Equal(406, unacceptable.Status)
        let! invalid = app.Send((get "/messages").With("accept", "garbage"))
        Assert.Equal(406, invalid.Status)

        let! any = app.Send((get "/autocomplete").With("accept", "*/*"))
        Assert.Equal(Some "text/html; charset=utf-8", any.Header "content-type")
    }

[<Fact>]
let ``redirects like rails`` () =
    task {
        use! app = defaultApp ()
        let! plain = app.Send(get "/redirect")
        Assert.Equal(302, plain.Status)
        Assert.Equal(Some "http://chat.example.com/rooms/1?x=1", plain.Header "location")
        Assert.Equal(Some "text/html; charset=utf-8", plain.Header "content-type")
        Assert.Equal(Some "no-cache", plain.Header "cache-control")

        let! seeOther = app.Send(get "/redirect?to=see_other")
        Assert.Equal(303, seeOther.Status)

        let! relative = app.Send(get "/redirect?to=relative")
        Assert.Equal(500, relative.Status)
        let! other = app.Send(get "/redirect?to=other")
        Assert.Equal(500, other.Status)
        let! allowed = app.Send(get "/redirect?to=other_allowed")
        Assert.Equal(Some "https://docs.example/", allowed.Header "location")

        let! back = app.Send((get "/redirect?to=back").With("referer", "http://chat.example.com/rooms/3"))
        Assert.Equal(Some "http://chat.example.com/rooms/3", back.Header "location")
        let! foreign = app.Send((get "/redirect?to=back").With("referer", "https://evil.example/x"))
        Assert.Equal(Some "http://chat.example.com/fallback", foreign.Header "location")
    }

[<Fact>]
let ``head with location`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send(get "/created")
        Assert.Equal(201, reply.Status)
        Assert.Equal(Some "http://chat.example.com/rooms/1/messages/2", reply.Header "location")
        Assert.Equal(Some "text/html", reply.Header "content-type")
    }

[<Fact>]
let ``send file with disposition and the whole file`` () =
    task {
        let dir = Directory.CreateTempSubdirectory("kit-send-file")
        try
            let path = Path.Combine(dir.FullName, "logo.png")
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes "0123456789")
            let encoded = CookieJar.escape path
            use! app = defaultApp ()

            let! whole = app.Send(get $"/file?path={encoded}")
            Assert.Equal("0123456789", whole.Text)
            Assert.Equal(Some "image/png", whole.Header "content-type")
            Assert.Equal(Some "inline; filename=\"logo.png\"; filename*=UTF-8''logo.png", whole.Header "content-disposition")
            Assert.Equal(Some "binary", whole.Header "content-transfer-encoding")
            Assert.Equal(Some "10", whole.Header "content-length")

            // Rails' `send_file` ignores Range.
            let! ignored = app.Send((get $"/file?path={encoded}").With("range", "bytes=2-4"))
            Assert.Equal(200, ignored.Status)
            Assert.Equal("0123456789", ignored.Text)

            let! missing = app.Send(get "/file?path=%2Fnope")
            Assert.Equal(500, missing.Status)
        finally
            dir.Delete true
    }

[<Fact>]
let ``stale and expires in`` () =
    task {
        use! app = defaultApp ()
        let! first = app.Send(get "/logo")
        Assert.Equal(200, first.Status)
        Assert.Equal(Some "max-age=300, public, stale-while-revalidate=604800", first.Header "cache-control")
        let etag = (first.Header "etag").Value
        Assert.StartsWith("W/\"", etag)

        let! second = app.Send((get "/logo").With("if-none-match", etag))
        Assert.Equal(304, second.Status)

        let! fresh = app.Send(get "/fresh")
        Assert.Equal(Some "max-age=0, private, must-revalidate", fresh.Header "cache-control")
        let etag = (fresh.Header "etag").Value
        let! cached = app.Send((get "/fresh").With("if-none-match", etag))
        Assert.Equal(304, cached.Status)
        let! listed = app.Send((get "/fresh").With("if-none-match", $"\"other\", {etag}"))
        Assert.Equal(304, listed.Status)

        // Turbo Frame requests get a different ETag (turbo-rails' frame etagger).
        let! frame = app.Send((get "/fresh").With("turbo-frame", "x"))
        Assert.NotEqual(Some etag, frame.Header "etag")
    }

[<Fact>]
let ``remote ip from peer and proxies`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send((get "/echo/1").With("x-forwarded-for", "203.0.113.9, 10.0.0.1").With("x-test-peer", "127.0.0.1"))
        Assert.Equal(Some(Value.String "203.0.113.9"), member' "remote_ip" reply.Json)

        let! reply = app.Send((get "/echo/1").With("x-test-peer", "198.51.100.4"))
        Assert.Equal(Some(Value.String "198.51.100.4"), member' "remote_ip" reply.Json)

        // `Client-Ip` is not read, so there is no spoofing check (README Known differences).
        let! reply = app.Send((get "/echo/1").With("client-ip", "1.1.1.1").With("x-forwarded-for", "2.2.2.2"))
        Assert.Equal(200, reply.Status)
        Assert.Equal(Some(Value.String "2.2.2.2"), member' "remote_ip" reply.Json)

        // A public peer is no proxy: its `X-Forwarded-For` is ignored (Rails would believe it).
        let! reply = app.Send((get "/echo/1").With("x-forwarded-for", "203.0.113.9").With("x-test-peer", "198.51.100.4"))
        Assert.Equal(Some(Value.String "198.51.100.4"), member' "remote_ip" reply.Json)
    }

[<Fact>]
let ``force ssl redirects and hardens`` () =
    task {
        use! app = startApp { KitConfig.Default with ForceSsl = true }
        let! plain = app.Send(get "/rooms/1?x=1")
        Assert.Equal(301, plain.Status)
        Assert.Equal(Some "https://chat.example.com/rooms/1?x=1", plain.Header "location")
        let! plainPost = app.Send(formPost "/echo/1" "")
        Assert.Equal(308, plainPost.Status)

        use! production = startApp (KitConfig.Production false)
        let! secure = production.Send(get "/sign_in")
        Assert.Equal(200, secure.Status)
        Assert.Equal(Some "max-age=63072000; includeSubDomains", secure.Header "strict-transport-security")
        Assert.True(secure.Cookies |> List.forall (fun c -> c.EndsWith "; secure"))

        let! https = production.Send(get "/redirect")
        Assert.Equal(Some "https://chat.example.com/rooms/1?x=1", https.Header "location")
    }

[<Fact>]
let ``body limit is 413`` () =
    task {
        use! app = startApp { KitConfig.Default with MaxBodyBytes = ValueSome 16 }
        let! reply = app.Send(formPost "/echo/1" (String.replicate 20 "a=1&"))
        Assert.Equal(413, reply.Status)
        let! reply = app.Send((request "PATCH" "/echo/1").WithBody(String('x', 40)))
        Assert.Equal(413, reply.Status)
    }

[<Fact>]
let ``request ids are sanitized passthroughs`` () =
    task {
        use! app = defaultApp ()
        let! reply = app.Send((get "/rooms/1").With("x-request-id", "abc-123<script>"))
        Assert.Equal(Some "abc-123script", reply.Header "x-request-id")
    }

[<Fact>]
let ``serves with peer addresses and shuts down gracefully`` () =
    task {
        let app = defaultApp ()
        let! running = app
        let! reply = running.Send(get "/echo/1")
        Assert.Equal(200, reply.Status)
        Assert.Equal(Some(Value.String "127.0.0.1"), member' "remote_ip" reply.Json)
        let stopping = (running :> IAsyncDisposable).DisposeAsync().AsTask()
        let! finished = Task.WhenAny(stopping, Task.Delay(TimeSpan.FromSeconds 5.0))
        Assert.True(obj.ReferenceEquals(finished, stopping), "the server stopped")
    }
