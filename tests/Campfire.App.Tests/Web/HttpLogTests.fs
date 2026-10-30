// plans/design-front.md section 5, unit 7.1: LG1-LG4 (the request log is `UseHttpLogging`, F8) and CZ1-CZ3 (the response compression
// gains zstd after Brotli and Gzip, F10).
module Campfire.App.Tests.Web.HttpLogTests

open System
open System.IO
open System.IO.Compression
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Diagnostics
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.HttpOverrides
open Microsoft.AspNetCore.ResponseCompression
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Xunit
open Campfire.App
open Campfire.App.Tests.Support
open Campfire.App.Web

let private edgeOn = { EdgeConfig.production true (Boot.errorPages ()) with LogRequests = true }

let private write (http: HttpContext) (text: string) : Task =
    Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(http.Response, text)

/// `UseHttpLogging` first, then the forwarded headers, as `Services.pipeline` orders them, over a handler that answers 200; the
/// log goes to `logs`.
let private startLogging (logs: LogCapture) : Task<Host> =
    startPipelineOn
        (WebApplicationOptions())
        ignore
        (fun services ->
            services.AddSingleton<ILoggerFactory>(logs) |> ignore
            services.Configure<ForwardedHeadersOptions>(Edge.configureForwardedHeaders edgeOn) |> ignore
            HttpLog.add services)
        (fun app ->
            app.UseHttpLogging().UseForwardedHeaders() |> ignore
            app.Run(RequestDelegate(fun http -> write http "ok")))

let private httpLines (logs: LogCapture) : string list =
    logs.Text.Split('\n') |> Array.filter (fun line -> line.Contains "Microsoft.AspNetCore.HttpLogging") |> List.ofArray

/// A log entry is several lines (`Path: ...`); the capture joins entries with a newline too, so cut at each category.
let private entries (logs: LogCapture) : string list =
    let text = logs.Text
    text.Split("Information Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware: ", StringSplitOptions.RemoveEmptyEntries)
    |> Array.filter (fun e -> e.Contains "Path:")
    |> List.ofArray

[<Fact>]
let ``LG1 one combined line per request with the method, path, query, protocol, status and duration`` () =
    task {
        let logs = new LogCapture()
        use! host = startLogging logs
        for i in 1..3 do
            let! reply = host.Send(get $"/things/{i}?x=1&y=two")
            Assert.Equal(200, reply.Status)
        let lines = entries logs
        Assert.Equal(3, lines.Length)
        let first = lines.Head
        for expected in [ "Method: GET"; "Path: /things/1"; "QueryString: ?x=1&y=two"; "Protocol: HTTP/1.1"; "StatusCode: 200"; "Duration: " ] do
            Assert.Contains(expected, first)
    }

[<Fact>]
let ``LG2 remote_addr and user_agent are present, an IPv4-mapped peer is written as IPv4 and a trusted proxy's X-Forwarded-For is reflected`` () =
    task {
        let logs = new LogCapture()
        use! host = startLogging logs
        let! _ = host.Send((get "/a").With("user-agent", "probe/1.0"))
        // The proxy (loopback is a trusted one) says the client is an IPv4-mapped IPv6 address.
        let! _ = host.Send((get "/b").With("user-agent", "probe/2.0").With("x-forwarded-for", "::ffff:203.0.113.9"))
        let! _ = host.Send((get "/c").With("x-forwarded-for", "2001:db8::7"))
        let lines = entries logs
        Assert.Equal(3, lines.Length)
        Assert.Contains("remote_addr: 127.0.0.1", lines[0])
        Assert.Contains("user_agent: probe/1.0", lines[0])
        Assert.Contains("remote_addr: 203.0.113.9", lines[1])
        Assert.DoesNotContain("::ffff:", lines[1])
        Assert.Contains("user_agent: probe/2.0", lines[1])
        Assert.Contains("remote_addr: 2001:db8::7", lines[2])
        Assert.DoesNotContain("user_agent", lines[2])
    }

[<Fact>]
let ``LG3 no request or response header value but the user agent appears in the line`` () =
    task {
        let logs = new LogCapture()
        use! host = startLogging logs
        let! _ =
            host.Send(
                (get "/secret")
                    .With("cookie", "session_token=secret-cookie-value")
                    .With("authorization", "Bearer secret-token-value")
                    .With("referer", "https://example.test/referrer-value")
                    .With("user-agent", "ua/1\tbad")
            )
        let line = (entries logs).Head
        for hidden in [ "secret-cookie-value"; "session_token"; "secret-token-value"; "referrer-value"; "text/plain"; "Content-Type"; "Set-Cookie" ] do
            Assert.DoesNotContain(hidden, line)
        Assert.Contains("user_agent: ua/1 bad", line)
    }

[<Fact>]
let ``LG4 the pipeline logs only with LogRequests (LOG_REQUESTS=false leaves the middleware out)`` () =
    task {
        let lines (logRequests: bool) =
            task {
                use! test = bootEmptyWith [ "LOG_REQUESTS", (if logRequests then "true" else "false") ]
                let! reply = test.Send(get "/session/new")
                Assert.True(reply.Status < 400, $"{reply.Status}")
                return test.Captured
            }
        let! on = lines true
        Assert.Equal(1, (entries on).Length)
        Assert.Contains("Path: /session/new", (entries on).Head)
        // The test hosts' boot leaves it off.
        let! off = lines false
        Assert.Empty(httpLines off)
    }

// --- Request log scope (plans/design-close.md C5, unit 9.3): LS1-LS5 -------------------------------------------

let private scopedEntries (test: Test) : string list = entries test.Captured

/// What the request log wrote for the paths so far, one string per entry.
let private loggedPaths (test: Test) : string list =
    scopedEntries test
    |> List.choose (fun e ->
        e.Split('\n') |> Array.tryFind (fun l -> l.StartsWith "Path:") |> Option.map (fun l -> l.Substring(5).Trim()))

[<Fact>]
let ``LS1 a page, a post, a 404 under /assets that is not a table route and a bot call each log one line`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let d = david test
        let! page = d.Get $"/rooms/{ALL_TALK}"
        Assert.Equal(200, page.Status)
        let! post = d.Write((request "POST" $"/rooms/{ALL_TALK}/messages").With("accept", "text/vnd.turbo-stream.html, text/html").Form(formBody [ "message[body]", "hello" ]))
        Assert.True(post.Status < 400, $"{post.Status} {post.Text}")
        let! missing = d.Get "/assets/not-in-the-table-0000.css"
        Assert.Equal(404, missing.Status)
        let! bot = (anonymous test).Send((request "POST" $"/rooms/{ALL_TALK}/{BENDER_KEY}/messages").WithBody "Beep")
        Assert.Equal(201, bot.Status)
        let paths = loggedPaths test
        Assert.Equal(4, paths.Length)
        Assert.Contains($"/rooms/{ALL_TALK}", paths)
        Assert.Contains($"/rooms/{ALL_TALK}/messages", paths)
        Assert.Contains("/assets/not-in-the-table-0000.css", paths)
        Assert.Equal(4, scopedEntries test |> List.filter (fun e -> e.Contains "StatusCode:") |> List.length)
    }

[<Fact>]
let ``LS2 a fingerprinted asset, a public file, an avatar hit and miss and /up log none`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let d = david test
        let! asset = d.Get(StaticAssets.path "composer.css")
        Assert.Equal(200, asset.Status)
        let! robots = d.Get "/robots.txt"
        Assert.Equal(200, robots.Status)
        let token = test.Label "avatar_tokens.david"
        let! hit = d.Get $"/users/{token}/avatar"
        Assert.Equal(200, hit.Status)
        let! miss = (anonymous test).Get $"/users/{token}/avatar"
        Assert.Equal(302, miss.Status)
        let! up = d.Get "/up"
        Assert.Equal(200, up.Status)
        Assert.Empty(scopedEntries test)
        // One page afterwards is the only line.
        let! _ = d.Get $"/rooms/{ALL_TALK}"
        Assert.Equal(1, (scopedEntries test).Length)
    }

[<Fact>]
let ``LS3 an avatar request whose handler throws logs PublicExceptions' line with status 500`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let logs = test.Captured
        use! host =
            startPipelineOn
                (WebApplicationOptions())
                StaticAssets.configureHost
                (fun services ->
                    services.AddSingleton<ILoggerFactory>(logs) |> ignore
                    services.AddSingleton<EdgeConfig>(edgeOn) |> ignore
                    services.AddSingleton<AppState>(test.App) |> ignore
                    Logging.add services test.App.Secrets
                    HttpLog.add services)
                (fun app ->
                    HttpLog.useLogging app
                    app.UseExceptionHandler(ExceptionHandlerOptions(ExceptionHandler = PublicExceptions.exceptionHandler)) |> ignore
                    app.Run(RequestDelegate(fun _ -> failwith "boom")))
        let before = (entries logs).Length
        let! reply = host.Send(get "/users/abc123/avatar")
        Assert.Equal(500, reply.Status)
        Assert.Equal(before, (entries logs).Length)
        Assert.Contains("request failed", logs.Text)
        Assert.Contains("path=\"/users/abc123/avatar\"", logs.Text)
        // A page that throws is in both.
        let! page = host.Send(get "/rooms/1")
        Assert.Equal(500, page.Status)
        Assert.Equal(before + 1, (entries logs).Length)
        Assert.Contains("StatusCode: 500", List.last (entries logs))
    }

type private CountingInterceptor() =
    member val Paths = System.Collections.Concurrent.ConcurrentQueue<string>()
    interface Microsoft.AspNetCore.HttpLogging.IHttpLoggingInterceptor with
        member this.OnRequestAsync(context) =
            this.Paths.Enqueue(nonNull context.HttpContext.Request.Path.Value)
            ValueTask.CompletedTask
        member _.OnResponseAsync(_) = ValueTask.CompletedTask

[<Fact>]
let ``LS4 the HTTP logging middleware does not run for an asset request`` () =
    task {
        let counter = CountingInterceptor()
        use! test =
            bootSeededServices None ignore (fun services -> services.AddSingleton<Microsoft.AspNetCore.HttpLogging.IHttpLoggingInterceptor>(counter) |> ignore) [ "LOG_REQUESTS", "true" ] "default"
        let! asset = (david test).Get(StaticAssets.path "composer.css")
        Assert.Equal(200, asset.Status)
        let! robots = (david test).Get "/robots.txt"
        Assert.Equal(200, robots.Status)
        Assert.Empty(counter.Paths)
        let! page = (david test).Get $"/rooms/{ALL_TALK}"
        Assert.Equal(200, page.Status)
        Assert.Equal<string list>([ $"/rooms/{ALL_TALK}" ], List.ofSeq counter.Paths)
    }

[<Fact>]
let ``LS5 before the asset table is loaded logged answers false only for /up and avatars, and nothing throws`` () =
    let ctx (path: string) =
        let http = DefaultHttpContext()
        http.Request.Method <- "GET"
        http.Request.Path <- PathString path
        http
    let beforeLoad = HttpLog.loggedWith (fun _ -> false)
    Assert.False(beforeLoad (ctx "/up"))
    Assert.False(beforeLoad (ctx "/users/abc/avatar"))
    Assert.False(beforeLoad (ctx "/users/abc/avatar.png"))
    for path in [ "/"; "/session/new"; "/rooms/1"; "/assets/composer.css"; "/robots.txt"; "/users/abc"; "/up/x"; "" ] do
        Assert.True(beforeLoad (ctx path), path)
    // The real lookup, loaded or not, answers for any path without throwing (the empty path included).
    for path in [ ""; "/"; "/assets/composer.css"; "/ROBOTS.TXT" ] do
        StaticAssets.isAssetRoute (PathString path) |> ignore
        HttpLog.logged (ctx path) |> ignore
    let assetPath = try Some(StaticAssets.path "composer.css") with _ -> None
    match assetPath with
    | Some route -> Assert.True(StaticAssets.isAssetRoute (PathString route)) ; Assert.False(HttpLog.logged (ctx route))
    | None -> ()

[<Fact>]
let ``LS6 a write to the avatar route and a POST to /up or an asset route each log one line`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let d = david test
        // `Users::AvatarsController#destroy` changes data: an operator must see it, as Rails' and Thruster's logs show it.
        let! gone = d.Form("delete", $"/users/{DAVID}/avatar", [])
        Assert.True(gone.Status < 400, $"{gone.Status} {gone.Text}")
        let! deleted = d.Write(request "DELETE" $"/users/{DAVID}/avatar")
        Assert.True(deleted.Status < 500, $"{deleted.Status}")
        let! up = d.Write(request "POST" "/up")
        Assert.True(up.Status >= 400, $"{up.Status}")
        let! asset = d.Write(request "POST" (StaticAssets.path "composer.css"))
        Assert.True(asset.Status >= 400, $"{asset.Status}")
        let paths = loggedPaths test
        Assert.Equal<string list>([ $"/users/{DAVID}/avatar"; $"/users/{DAVID}/avatar"; "/up"; StaticAssets.path "composer.css" ], paths)
        // A GET of the avatar and /up still logs nothing.
        let! _ = d.Get "/up"
        Assert.Equal(4, (loggedPaths test).Length)
    }

// --- Compression ----------------------------------------------------------------------------------------------

let private startCompression () : Task<Host> =
    startPipelineOn
        (WebApplicationOptions())
        ignore
        (fun services ->
            services.AddResponseCompression(Compression.configure) |> ignore
            services.AddSingleton<IResponseCompressionProvider>(fun sp ->
                Compression.SkipSmall(ResponseCompressionProvider(sp, sp.GetRequiredService<IOptions<ResponseCompressionOptions>>()))
                :> IResponseCompressionProvider)
            |> ignore)
        (fun app ->
            app.UseResponseCompression() |> ignore
            app.Run(
                RequestDelegate(fun http ->
                    let body = String.replicate 400 "campfire "
                    match http.Request.Path.Value with
                    | "/big" ->
                        http.Response.ContentType <- "text/html; charset=utf-8"
                        write http body
                    | "/small" ->
                        http.Response.ContentType <- "text/html; charset=utf-8"
                        http.Response.ContentLength <- 100L
                        write http (String('x', 100))
                    | _ ->
                        http.Response.ContentType <- "image/png"
                        http.Response.ContentLength <- int64 body.Length
                        write http body)
            ))

let private big = String.replicate 400 "campfire "

[<Fact>]
let ``CZ1 Accept-Encoding zstd gets zstd that decompresses to the identity body`` () =
    task {
        use! host = startCompression ()
        let! reply = host.Send((get "/big").With("accept-encoding", "zstd"))
        Assert.Equal(Some "zstd", reply.Header "content-encoding")
        Assert.Contains("Accept-Encoding", String.Join(", ", reply.HeaderValues "vary"))
        use input = new MemoryStream(reply.Body)
        use decoder = new ZstandardStream(input, CompressionMode.Decompress)
        use output = new MemoryStream()
        decoder.CopyTo output
        Assert.Equal(big, Text.Encoding.UTF8.GetString(output.ToArray()))
        Assert.True(reply.Body.Length < big.Length / 4)
    }

[<Fact>]
let ``CZ2 Chrome's encodings get br, gzip gets gzip, none gets identity`` () =
    task {
        use! host = startCompression ()
        let encoding (accept: string option) =
            task {
                let req = get "/big"
                let! reply = host.Send(match accept with Some a -> req.With("accept-encoding", a) | None -> req)
                return reply.Header "content-encoding"
            }
        Assert.Equal(Some "br", (encoding (Some "gzip, deflate, br, zstd")).Result)
        Assert.Equal(Some "gzip", (encoding (Some "gzip")).Result)
        Assert.Equal(None, (encoding None).Result)
        Assert.Equal(None, (encoding (Some "identity")).Result)
    }

[<Fact>]
let ``CZ3 a response under 256 bytes and an image are not compressed and carry no Vary`` () =
    task {
        use! host = startCompression ()
        let accept = "gzip, deflate, br, zstd"
        let! small = host.Send((get "/small").With("accept-encoding", accept))
        Assert.Equal(None, small.Header "content-encoding")
        Assert.Equal(None, small.Header "vary")
        let! image = host.Send((get "/image.png").With("accept-encoding", accept))
        Assert.Equal(None, image.Header "content-encoding")
        Assert.Equal(None, image.Header "vary")
        Assert.Equal(big.Length, image.Body.Length)
    }

// --- Security review (plans/design-front.md section 11) ------------------------------------------------------

/// A raw request line, so that the target reaches Kestrel exactly as written (HttpClient would re-encode it).
let private sendRawWith (host: Host) (target: string) (extraHeader: byte[]) : Task<string> =
    task {
        use client = new Net.Sockets.TcpClient()
        do! client.ConnectAsync(Net.IPAddress.Loopback, host.Port)
        use stream = client.GetStream()
        let bytes =
            Array.concat
                [ Text.Encoding.ASCII.GetBytes $"GET {target} HTTP/1.1\r\nHost: localhost\r\n"
                  extraHeader
                  Text.Encoding.ASCII.GetBytes "Connection: close\r\n\r\n" ]
        do! stream.WriteAsync(ReadOnlyMemory<byte> bytes)
        use reader = new StreamReader(stream)
        let! head = reader.ReadLineAsync()
        return nonNull head
    }

let private sendRaw (host: Host) (target: string) : Task<string> = sendRawWith host target [||]

/// No control character (C0, DEL, C1) and no Unicode line or paragraph separator.
let private printable (text: string) : bool =
    not (text |> Seq.exists (fun c -> Char.IsControl c || c = '\u2028' || c = '\u2029'))

[<Fact>]
let ``SR1 a client's control characters in the path, query or user agent never reach the log line raw`` () =
    task {
        let logs = new LogCapture()
        use! host = startLogging logs
        // Percent-encoded line breaks, an escape (a terminal sequence), NEL and LINE SEPARATOR, in the path and the query.
        let! status = sendRaw host "/a%0D%0Ab%1B%5B31m%C2%85%E2%80%A8c?x=%0D%0Ay%1B"
        Assert.StartsWith("HTTP/1.1 200", status)
        let entry = (entries logs) |> List.exactlyOne
        // The entry's own layout is several lines (the console formatter's SingleLine folds them); what the client sent is not.
        let fields = entry.Split('\n') |> Array.filter (fun line -> line.StartsWith "Path:" || line.StartsWith "QueryString:")
        Assert.Equal(2, fields.Length)
        for field in fields do
            Assert.True(printable field, $"control character in {field}")
        // A user agent of raw bytes (UTF-8 NEL, an escape sequence): Kestrel refuses it, or it is logged with no control character.
        let agent = Array.concat [ Text.Encoding.ASCII.GetBytes "User-Agent: ua"; [| 0xC2uy; 0x85uy |]; Text.Encoding.ASCII.GetBytes "x\u001b[2Jy\r\n" ]
        let! status = sendRawWith host "/ua" agent
        if status.StartsWith "HTTP/1.1 200" then
            let line =
                entries logs |> List.find (fun e -> e.Contains "Path: /ua") |> fun e -> e.Split('\n') |> Array.find (fun l -> l.StartsWith "user_agent:")
            Assert.True(printable line, $"control character in {line}")
        else
            Assert.StartsWith("HTTP/1.1 400", status)
    }

[<Fact>]
let ``SR2 query values Rails' filter_parameters names are logged as [FILTERED], as Rails' request line logs them`` () =
    task {
        let logs = new LogCapture()
        use! host = startLogging logs
        let! reply = host.Send(get "/x?q=hello&token=tok-secret&user%5Bpassword%5D=pw-secret&Email_Address=mail-secret&before=12;otp=otp-secret&message.body=body-secret")
        Assert.Equal(200, reply.Status)
        // The entry alone: the capture also holds the hosting diagnostics' own lines (Information, filtered out in production by
        // `Boot.createLoggerFactory`'s `Microsoft` = Warning).
        let entry = (entries logs) |> List.exactlyOne |> fun e -> e.Substring(0, e.IndexOf "Duration:")
        for secret in [ "tok-secret"; "pw-secret"; "mail-secret"; "body-secret" ] do
            Assert.DoesNotContain(secret, entry)
        Assert.Contains("QueryString: ?q=hello&token=[FILTERED]&user%5Bpassword%5D=[FILTERED]&Email_Address=[FILTERED]&before=12;otp=[FILTERED]&message.body=[FILTERED]", entry)
        // A query with nothing to filter is logged as sent.
        let! _ = host.Send(get "/y?before=42&q=tokens")
        Assert.Contains("QueryString: ?before=42&q=tokens", (entries logs)[1])
    }

[<Fact>]
let ``SR3 the production loggers write the HTTP logging line but not the framework's own request lines, which carry the unfiltered URL`` () =
    let dir = Directory.CreateTempSubdirectory("campfire-app-test").FullName
    try
        // Even at debug (`RAILS_LOG_LEVEL`), `Microsoft.*` stays at Warning: `Hosting.Diagnostics`' "Request starting ... <URL>" is never written.
        use loggers = Boot.createLoggerFactory (configFor dir [ "RAILS_LOG_LEVEL", "debug" ])
        Assert.True((loggers.CreateLogger "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware").IsEnabled LogLevel.Information)
        for category in [ "Microsoft.AspNetCore.Hosting.Diagnostics"; "Microsoft.AspNetCore.Server.Kestrel"; "System.Net.Http.HttpClient.acme.LogicalHandler" ] do
            Assert.False((loggers.CreateLogger category).IsEnabled LogLevel.Information, category)
    finally
        try Directory.Delete(dir, true) with _ -> ()
