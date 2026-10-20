// plans/design-kit-auth.md section 5, unit 2.6 (Requests): R1-R6. The whole app over a parity seed; the forwarded headers
// (`UseForwardedHeaders`, `TRUSTED_PROXIES` ranges only), `assume_ssl`, `UseHttpMethodOverride` + upper-casing, the
// request id and default headers, the 16 MiB body cap and `UseExceptionHandler` are the framework's (K10, K13).
module Campfire.App.Tests.Web.RequestsTests

open System
open System.IO
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Diagnostics
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Xunit
open Campfire.App.Tests.Support

/// `ips.banned` in the seed's labels.
let private bannedIp = "203.0.113.9"

let private message (b: Browser) (headers: (string * string) list) : Task<Reply> =
    let req =
        (request "POST" $"/rooms/{ALL_TALK}/messages")
            .With("accept", "text/vnd.turbo-stream.html, text/html, application/xhtml+xml")
            .With("sec-fetch-site", "same-origin")
            .Form(formBody [ "message[body]", "hello" ])
    b.Send(headers |> List.fold (fun (r: Req) (k, v) -> r.With(k, v)) req)

/// The app's own pipeline and services, with every proxy distrusted (a peer that is no proxy: the loopback peer of the
/// test host is inside `TRUSTED_PROXIES`, so the ranges are replaced; an empty list would trust every peer).
let private startUntrusting (test: Test) : Task<Host> =
    let distrust (options: ForwardedHeadersOptions) =
        options.KnownIPNetworks.Clear()
        options.KnownIPNetworks.Add(System.Net.IPNetwork(System.Net.IPAddress.Parse "192.0.2.0", 24))
    startPipelineWith
        (fun (services: IServiceCollection) ->
            test.Booted.ConfigureServices services
            services.Configure<ForwardedHeadersOptions>(distrust) |> ignore)
        test.Booted.Pipeline

[<Fact>]
let ``R1 X-Forwarded-For from a trusted proxy sets remote_ip, from another peer it is ignored`` () =
    task {
        use! test = bootSeeded "default"
        // The ban check reads `remote_ip`: the test host's peer is loopback, inside `TRUSTED_PROXIES`.
        let! banned = message (david test) [ "x-forwarded-for", bannedIp ]
        Assert.Equal(429, banned.Status)
        // A forwarded chain walks from the right over the trusted ranges: the first public address is the client.
        let! viaChain = message (david test) [ "x-forwarded-for", $"{bannedIp}, 10.1.2.3, 192.168.0.4" ]
        Assert.Equal(429, viaChain.Status)
        let! other = message (david test) [ "x-forwarded-for", "198.51.100.77" ]
        Assert.True((other.Status < 400), $"{other.Status} {other.Text}")
        let! plain = message (david test) []
        Assert.True((plain.Status < 400), $"{plain.Status}")

        // From a peer that is no proxy, the header is only a header.
        use! untrusting = startUntrusting test
        let b = Browser(test, None)
        b.AbsorbCookieHeader(davidCookie ())
        let req =
            (request "POST" $"/rooms/{ALL_TALK}/messages")
                .With("x-forwarded-for", bannedIp)
                .With("accept", "text/vnd.turbo-stream.html, text/html, application/xhtml+xml")
                .With("sec-fetch-site", "same-origin")
                .With("cookie", (davidCookie ()))
                .Form(formBody [ "message[body]", "hello" ])
        let! ignored = untrusting.Send req
        Assert.True((ignored.Status < 400), $"{ignored.Status}")
    }

[<Fact>]
let ``R2 X-Forwarded-Proto and -Host from a trusted proxy make https URLs and secure cookies`` () =
    task {
        use! test = bootSeeded "default"
        // Signed out, a page redirects to the absolute sign-in URL, which is built from the request's scheme and host.
        let! http = test.Send(get $"/rooms/{ALL_TALK}")
        Assert.Equal(Some "http://campfire.test/session/new", http.Location)
        let! https = test.Send((get $"/rooms/{ALL_TALK}").With("x-forwarded-proto", "https"))
        Assert.Equal(Some "https://campfire.test/session/new", https.Location)
        let! hosted = test.Send((get $"/rooms/{ALL_TALK}").With("x-forwarded-proto", "https").With("x-forwarded-host", "chat.example.com"))
        Assert.Equal(Some "https://chat.example.com/session/new", hosted.Location)

        // Cookies written through `Response.Cookies` follow the scheme (`CookieSecurePolicy.SameAsRequest`).
        let b = david test
        let! plain = b.Send(get $"/rooms/{ALL_TALK}")
        let lastRoom (reply: Reply) = reply.SetCookies |> List.find (fun c -> c.StartsWith "last_room=")
        Assert.DoesNotContain("secure", lastRoom plain)
        let other = david test
        let! secure = other.Send((get $"/rooms/{QUIET_CORNER}").With("x-forwarded-proto", "https"))
        Assert.Contains("secure", lastRoom secure)

        // A peer that is no proxy cannot say so.
        use! untrusting = startUntrusting test
        let! ignored = untrusting.Send((get $"/rooms/{ALL_TALK}").With("x-forwarded-proto", "https").With("x-forwarded-host", "chat.example.com"))
        Assert.Equal(Some "http://campfire.test/session/new", ignored.Location)
    }

[<Fact>]
let ``R2 assume_ssl makes every request https`` () =
    task {
        use! test = bootSeededConfigured ignore [ "DISABLE_SSL", " " ] "default"
        let! reply = test.Send(get $"/rooms/{ALL_TALK}")
        Assert.Equal(Some "https://campfire.test/session/new", reply.Location)
        Assert.Equal(Some "max-age=63072000; includeSubDomains", reply.Header "strict-transport-security")
    }

[<Fact>]
let ``R3 a form's _method reaches the DELETE and PATCH rows`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let send (req: Req) = b.Send(req.With("sec-fetch-site", "same-origin"))
        // POST + `_method=delete`, in either case, is the DELETE row; the recognizer sees the upper-case verb.
        let! lower = send ((request "POST" $"/users/{DAVID}/avatar").Form(formBody [ "_method", "delete" ]))
        let! upper = send ((request "POST" $"/users/{DAVID}/avatar").Form(formBody [ "_method", "DELETE" ]))
        let! direct = send (request "DELETE" $"/users/{DAVID}/avatar")
        Assert.True((direct.Status < 400), $"{direct.Status}")
        Assert.Equal(direct.Status, lower.Status)
        Assert.Equal(direct.Status, upper.Status)
        Assert.Equal(direct.Location, lower.Location)
        // `_method=patch` is the PATCH row (`PATCH /account`): not the POST it was sent as, which has no such route.
        let! patched = send ((request "POST" "/account").Form(formBody [ "_method", "patch"; "account[name]", "Campfire" ]))
        let! direct = send ((request "PATCH" "/account").Form(formBody [ "account[name]", "Campfire" ]))
        Assert.True((direct.Status < 400), $"{direct.Status}")
        Assert.Equal(direct.Status, patched.Status)
        // The override also carries a multipart form (an upload with `_method=patch`).
        let png = Convert.FromBase64String "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="
        let upload =
            multipartReq (request "POST" "/account") [ "_method", "patch"; "account[name]", "Campfire" ] ("account[logo]", "logo.png", "image/png", png)
        let! multipart = send upload
        Assert.Equal(direct.Status, multipart.Status)
        // A `_method` that is no verb is that verb, which no route answers; `X-HTTP-Method-Override` is not read, and
        // neither is `_method` in a JSON body.
        let! bogus = send ((request "POST" $"/users/{DAVID}/avatar").Form(formBody [ "_method", "bogus" ]))
        Assert.Equal(404, bogus.Status)
        let! header = message b [ "x-http-method-override", "DELETE" ]
        Assert.True((header.Status < 400), $"{header.Status}")
        let! json =
            send ((request "POST" $"/rooms/{ALL_TALK}/messages").With("content-type", "application/json").With("accept", "text/vnd.turbo-stream.html").WithBody """{"_method":"delete","message":{"body":"hi"}}""")
        Assert.True((json.Status < 400), $"{json.Status} {json.Text}")
    }

/// `n` bytes of ASCII.
let private ascii (n: int) : byte[] = Array.create n (byte 'a')

/// Sends a request the server refuses before it has read the body: the raw socket client of `Support` would fail writing
/// into the closed connection, an `HttpClient` returns the response it was given.
let private sendBig (test: Test) (cookie: string option) (req: Req) : Task<Reply> =
    task {
        use client = new HttpClient(new SocketsHttpHandler(UseCookies = false))
        use message = new HttpRequestMessage(HttpMethod(req.Method), $"http://127.0.0.1:{test.Host.Port}{req.Target}")
        let content = new ByteArrayContent(defaultArg req.Body [||])
        for (k, v) in req.Headers do
            if k.Equals("host", StringComparison.OrdinalIgnoreCase) then message.Headers.Host <- v
            elif k.Equals("content-type", StringComparison.OrdinalIgnoreCase) then content.Headers.TryAddWithoutValidation(k, v) |> ignore
            else message.Headers.TryAddWithoutValidation(k, v) |> ignore
        cookie |> Option.iter (fun c -> message.Headers.TryAddWithoutValidation("cookie", c) |> ignore)
        message.Content <- content
        // The client holds the body back until the server has said it wants it (`100 Continue`) or has refused it.
        message.Headers.ExpectContinue <- true
        use! reply = client.SendAsync message
        let! body = reply.Content.ReadAsByteArrayAsync()
        return
            { Status = int reply.StatusCode
              Headers = [ for h in reply.Headers -> h.Key, String.Join(", ", h.Value) ]
              Body = body }
    }

[<Fact>]
let ``R4 a body over 16 MiB is 413, whatever its form`` () =
    task {
        use! test = bootSeeded "default"
        let write (req: Req) =
            sendBig test (Some(davidCookie ())) (req.With("sec-fetch-site", "same-origin").With("accept", "text/vnd.turbo-stream.html, text/html"))
        let big = 17 * 1024 * 1024
        // A urlencoded form (read for `_method` before the action runs).
        let! form = write { (request "POST" $"/rooms/{ALL_TALK}/messages").With("content-type", "application/x-www-form-urlencoded") with Body = Some(Array.append "message[body]="B (ascii big)) }
        Assert.Equal(413, form.Status)
        // A direct upload: a multipart body with a file over the cap.
        let! upload = write (multipartReq (request "POST" $"/rooms/{ALL_TALK}/messages") [] ("message[attachments][]", "big.bin", "application/octet-stream", ascii big))
        Assert.Equal(413, upload.Status)
        // A raw body (the bot API) and a JSON one.
        let! raw = sendBig test None { (request "POST" $"/rooms/{ALL_TALK}/{BENDER_KEY}/messages") with Body = Some(ascii big) }
        Assert.Equal(413, raw.Status)
        // Under the cap is read as usual.
        let! under = write (multipartReq (request "POST" $"/rooms/{ALL_TALK}/messages") [ "message[body]", "a file" ] ("message[attachments][]", "ok.txt", "text/plain", ascii (1024 * 1024)))
        Assert.True((under.Status < 400), $"{under.Status} {under.Text}")
    }

/// `count` chunks of `size` bytes, with no length: a body sent chunked.
type ChunkStream(count: int, size: int) =
    inherit Stream()
    let mutable left = int64 count * int64 size
    override _.CanRead = true
    override _.CanSeek = false
    override _.CanWrite = false
    override _.Length = raise (NotSupportedException())
    override _.Position with get () = raise (NotSupportedException()) and set _ = raise (NotSupportedException())
    override _.Flush() = ()
    override _.Seek(_, _) = raise (NotSupportedException())
    override _.SetLength _ = raise (NotSupportedException())
    override _.Write(_, _, _) = raise (NotSupportedException())
    override _.Read(buffer: byte[], offset: int, n: int) =
        let take = int (min (int64 n) left)
        Array.fill buffer offset take (byte 'a')
        left <- left - int64 take
        take

[<Fact>]
let ``R4 a chunked body over 16 MiB is 413 with the public page`` () =
    task {
        use! test = bootSeeded "default"
        use client = new HttpClient(new SocketsHttpHandler(UseCookies = false))
        // No `Content-Length`: the host's limit trips as the body is read, and `UseExceptionHandler` answers. The host
        // may close the connection while the client is still sending, so the client sometimes sees a reset instead of the
        // answer (a race of the client's, not of the app): try again a few times, and if the reset keeps winning, the
        // app's own log must still say it refused the body with a 413.
        let attempt () =
            task {
                let content = new StreamContent(new ChunkStream(18, 1024 * 1024))
                content.Headers.ContentType <- Headers.MediaTypeHeaderValue "text/plain"
                use message = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{test.Host.Port}/rooms/{ALL_TALK}/{BENDER_KEY}/messages", Content = content)
                message.Headers.Host <- "campfire.test"
                try
                    use! reply = client.SendAsync message
                    let! text = reply.Content.ReadAsStringAsync()
                    return Some(int reply.StatusCode, text)
                with :? HttpRequestException ->
                    return None
            }
        let mutable answer = None
        let mutable tries = 0
        while answer.IsNone && tries < 4 do
            let! got = attempt ()
            answer <- got
            tries <- tries + 1
        match answer with
        | Some(status, text) -> Assert.True((status = 413), $"{status} {text} {test.Logs}")
        | None -> Assert.True((test.Logs.Contains "request rejected error=413 Payload Too Large"), $"no reply in {tries} tries, and no 413 logged: {test.Logs}")
    }

[<Fact>]
let ``R5 X-Request-Id is echoed sanitized or generated, default headers go on pages only`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let! echoed = b.Send((get "/session/new").With("x-request-id", "abc-123_@x/y z!"))
        Assert.Equal(Some "abc-123_@xyz", echoed.Header "x-request-id")
        let! generated = b.Send(get "/session/new")
        let isGuid (reply: Reply) = fst (Guid.TryParse(reply.Header "x-request-id" |> Option.defaultValue ""))
        Assert.True(isGuid generated)
        let! blank = b.Send((get "/session/new").With("x-request-id", "  "))
        Assert.True(isGuid blank)
        // Rails' `default_headers`, on a page and not on a streamed (`ActionController::Live`) response.
        for name, value in
            [ "x-frame-options", "SAMEORIGIN"
              "x-xss-protection", "0"
              "x-content-type-options", "nosniff"
              "x-permitted-cross-domain-policies", "none"
              "referrer-policy", "strict-origin-when-cross-origin" ] do
            Assert.Equal(Some value, generated.Header name)
        let! logo = b.Send(get "/account/logo")
        Assert.Equal(200, logo.Status)
        Assert.Equal(None, logo.Header "x-frame-options")
        Assert.True((logo.Header "x-request-id").IsSome)
        Assert.True((logo.Header "x-runtime").IsSome)
    }

[<Fact>]
let ``R6 an exception outside an action is the public 500 page, JSON for a JSON client`` () =
    task {
        use! test = bootSeeded "default"
        let page = File.ReadAllBytes(Campfire.Tests.Repo.path "reference/public/500.html")
        // The app's own handler, in front of a terminal that throws (as the session store or a framework piece could).
        use! host =
            startPipelineWith test.Booted.ConfigureServices (fun builder ->
                builder.UseExceptionHandler(ExceptionHandlerOptions(ExceptionHandler = Campfire.App.Web.PublicExceptions.exceptionHandler)) |> ignore
                builder.Run(fun _ -> failwith "boom"))
        let! html = host.Send((get "/anything").With("accept", "text/html"))
        Assert.Equal(500, html.Status)
        Assert.Equal<byte[]>(page, html.Body)
        let! json = host.Send((get "/anything").With("accept", "application/json"))
        Assert.Equal(500, json.Status)
        Assert.Contains("\"status\":500", json.Text)
        // A throwing action is the same page (`Action.run`), through the real pipeline: a malformed bot key is Rails'
        // `NoMethodError` on `strip`.
        let! action = (anonymous test).Send(get $"/rooms/{ALL_TALK}?bot_key[a]=b")
        Assert.Equal(500, action.Status)
        Assert.Equal<byte[]>(page, action.Body)
    }
