// plans/design-front.md section 5, unit 7.1: LG1-LG4 (the request log is `UseHttpLogging`, F8) and CZ1-CZ3 (the response compression
// gains zstd after Brotli and Gzip, F10).
module Campfire.App.Tests.Web.HttpLogTests

open System
open System.IO
open System.IO.Compression
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
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
let private startLogging (logs: CapturingLoggers) : Task<Host> =
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

let private httpLines (logs: CapturingLoggers) : string list =
    logs.Text.Split('\n') |> Array.filter (fun line -> line.Contains "Microsoft.AspNetCore.HttpLogging") |> List.ofArray

/// A log entry is several lines (`Path: ...`); the capture joins entries with a newline too, so cut at each category.
let private entries (logs: CapturingLoggers) : string list =
    let text = logs.Text
    text.Split("Information Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware: ", StringSplitOptions.RemoveEmptyEntries)
    |> Array.filter (fun e -> e.Contains "Path:")
    |> List.ofArray

[<Fact>]
let ``LG1 one combined line per request with the method, path, query, protocol, status and duration`` () =
    task {
        let logs = new CapturingLoggers()
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
        let logs = new CapturingLoggers()
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
        let logs = new CapturingLoggers()
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
        let dir = Directory.CreateTempSubdirectory("campfire-app-test").FullName
        try
            let lines (logRequests: bool) =
                task {
                    let logs = new CapturingLoggers()
                    let! booted = Boot.bootEdge (fun edge -> { edge with LogRequests = logRequests }) None ignore (configFor dir []) logs
                    let! host =
                        startPipelineWith (fun services -> booted.ConfigureServices services; services.AddSingleton<ILoggerFactory>(logs) |> ignore) booted.Pipeline
                    try
                        let! reply = host.Send(get "/up")
                        Assert.Equal(200, reply.Status)
                    finally
                        (host :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
                    return logs
                }
            let! on = lines true
            Assert.Equal(1, (entries on).Length)
            Assert.Contains("Path: /up", (entries on).Head)
            // The test hosts' boot leaves it off.
            let! off = lines false
            Assert.Empty(httpLines off)
        finally
            try Directory.Delete(dir, true) with _ -> ()
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
        let logs = new CapturingLoggers()
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
        let logs = new CapturingLoggers()
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
