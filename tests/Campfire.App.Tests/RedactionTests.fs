// plans/design-jobs.md section 5, unit 8.6: RD1-RD6 (J11). A bot API path carries the bot's key and a webhook URL holds its secret in
// the path: the log lines carry a keyed hash of them (`Logging`, the HMAC redactor), never the value. Everything over `FakeLogger`
// (the capture is a `FakeLogCollector`).
module Campfire.App.Tests.RedactionTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Diagnostics
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Campfire.App
open Campfire.App.Integrations
open Campfire.App.Tests.Support
open Campfire.App.Web
open Campfire.Db

let private botKey = BENDER_KEY
let private botKeyLabel = "BenderBot123"

/// The keyed form of `value` as the app logs it.
let private redacted (test: Test) (value: string) : string = (Logging.forSecrets test.App.Secrets).Redact value

/// What production writes: `Boot.createLoggerFactory`'s filters (the framework's categories at Warning, the request log at
/// Information, the app's at Information), over the capture, which records everything.
let private shipped (test: Test) : string =
    let collector = (test.App.Loggers :?> CapturingLoggers).Collector
    collector.GetSnapshot()
    |> Seq.filter (fun record ->
        let category = record.Category |> Option.ofObj |> Option.defaultValue ""
        let framework = category.StartsWith "Microsoft" || category.StartsWith "System"
        if category = "Microsoft.AspNetCore.HttpLogging.HttpLoggingMiddleware" then record.Level >= LogLevel.Information
        elif framework then record.Level >= LogLevel.Warning
        else record.Level >= LogLevel.Information)
    |> Seq.map (fun record -> $"{record.Level} {record.Category}: {record.Message}")
    |> String.concat "\n"

let private noKey (logs: string) : unit =
    Assert.DoesNotContain(botKey, logs)
    Assert.DoesNotContain(botKeyLabel, logs)

[<Fact>]
let ``RD1 a bot API request's log line holds the HMAC form of the key and not the key`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let bot = anonymous test
        let! created = bot.Send((request "POST" $"/rooms/{ALL_TALK}/{botKey}/messages").WithBody "Beep boop")
        Assert.True((created.Status = 201), created.Text)
        let! listed = bot.Get $"/rooms/{ALL_TALK}/{botKey}/messages/1.json"
        ignore listed
        let hash = redacted test botKey
        Assert.StartsWith("1:", hash)
        let logs = shipped test
        noKey logs
        Assert.Contains($"Path: /rooms/{ALL_TALK}/{hash}/messages", logs)
        // The other fields of the line are still there.
        Assert.Contains("Method: POST", logs)
        Assert.Contains("StatusCode: 201", logs)
        Assert.Contains("remote_addr", logs)
        // A page that is not the bot API keeps its path as it is.
        let! page = (david test).Get $"/rooms/{ALL_TALK}"
        Assert.Equal(200, page.Status)
        Assert.Contains($"Path: /rooms/{ALL_TALK}\n", shipped test + "\n")
    }

[<Fact>]
let ``RD2 a failure on a bot path logs no key`` () =
    task {
        use! test = bootSeededRequestLog "default"
        // The exception page's own line (`request failed ... path=`), over a handler that throws on a bot path.
        use! host =
            Support.startPipelineWith
                (fun services ->
                    services.AddSingleton<EdgeConfig>(EdgeConfig.production true (Boot.errorPages ())) |> ignore
                    services.AddSingleton<AppState>(test.App) |> ignore
                    Logging.add services test.App.Secrets)
                (fun app ->
                    app.UseExceptionHandler(ExceptionHandlerOptions(ExceptionHandler = PublicExceptions.exceptionHandler)) |> ignore
                    app.Run(RequestDelegate(fun _ -> failwith "boom")))
        let! reply = host.Send(get $"/rooms/{ALL_TALK}/{botKey}/messages")
        Assert.Equal(500, reply.Status)
        let! rejected = host.Send(get $"/rooms/{ALL_TALK}/{botKey}/messages.json")
        ignore rejected
        let logs = shipped test
        noKey logs
        Assert.Contains("request failed", logs)
        Assert.Contains($"path=\"/rooms/{ALL_TALK}/{redacted test botKey}/messages\"", logs)
        // Without a redactor registered (a bare host), nothing of the key survives either.
        use! bare =
            Support.startPipelineWith
                (fun services ->
                    services.AddSingleton<EdgeConfig>(EdgeConfig.production true (Boot.errorPages ())) |> ignore
                    services.AddSingleton<AppState>(test.App) |> ignore)
                (fun app ->
                    app.UseExceptionHandler(ExceptionHandlerOptions(ExceptionHandler = PublicExceptions.exceptionHandler)) |> ignore
                    app.Run(RequestDelegate(fun _ -> failwith "boom")))
        let! _ = bare.Send(get $"/rooms/{ALL_TALK}/{botKey}/messages")
        noKey (shipped test)
        Assert.Contains($"/rooms/{ALL_TALK}/{Logging.Erased}/messages", (shipped test))
    }

[<Fact>]
let ``RD3 a webhook job failing on a bad URL logs no part of the URL`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let secret = "s3cr3t-hook-token"
        let url = $"http://exa mple.com/hooks/{secret}?k=v"
        do! test.Execute("UPDATE webhooks SET url = ? WHERE user_id = ?", [ box url; box BENDER ])
        let messageId = test.Sql(fun conn -> RawSql.scalar<int64> conn "SELECT id FROM messages WHERE room_id = ? ORDER BY id DESC LIMIT 1" [ box ALL_TALK ])
        let registry = Registry<AppState>()
        IntegrationJobs.registerJobs registry
        let handler =
            match registry.TryGet JobKind.DeliverWebhook with
            | ValueSome handler -> handler
            | ValueNone -> failwith "no webhook handler"
        match! handler test.App (Event.DeliverWebhook(BENDER, messageId)) with
        | Error message ->
            Assert.DoesNotContain(secret, message)
            Assert.DoesNotContain("mple.com", message)
            Assert.Contains("bad URI (is not URI?)", message)
            Assert.Contains(redacted test url, message)
        | Ok() -> failwith "the job did not fail"
        // Through the runner's own line: `job failed job=... error=...`.
        let logger = (test.Booted.App.Loggers).CreateLogger "campfire.jobs"
        let! failed = handler test.App (Event.DeliverWebhook(BENDER, messageId))
        match failed with
        | Error message -> logger.LogError("job failed job={Job} error={Error}", "deliver_webhook", message)
        | Ok() -> ()
        Assert.DoesNotContain(secret, (shipped test))
        Assert.Contains("job failed", (shipped test))
        // A Ruby-quoted URL (non-ASCII characters) is covered in its JSON form too.
        let quoted = Logging.redactUrl (Logging.forSecrets test.App.Secrets) "http://h/é secret" "bad URI (is not URI?): \"http://h/é secret\""
        Assert.DoesNotContain("secret", quoted)
        // An event that is not a job is logged by name, never by its fields.
        Assert.Equal("PushMessage", JobKind.eventName (Event.PushMessage(1L, 2L)))
        Assert.Equal("DeliverWebhook", JobKind.eventName (Event.DeliverWebhook(1L, 2L)))
    }

[<Fact>]
let ``RD5 sign-in, throttle and join log no e-mail address and no token`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let visitor = browser test "198.51.100.7"
        let email = "nobody.rd5@example.com"
        let mutable throttled = false
        for _ in 1..12 do
            let! reply = visitor.Form("post", "/session", [ "email_address", email; "password", "wrong-password-rd5" ])
            if reply.Status = 429 then throttled <- true
        Assert.True(throttled, "the sign-in was never throttled")
        let! joined = visitor.Get "/join/rd5-join-code-0123"
        ignore joined
        let! joinPost = visitor.Form("post", "/join/rd5-join-code-0123", [ "user[name]", "RD5"; "user[email_address]", email; "user[password]", "wrong-password-rd5" ])
        ignore joinPost
        let logs = shipped test
        Assert.DoesNotContain(email, logs)
        Assert.DoesNotContain("wrong-password-rd5", logs)
        Assert.DoesNotContain("rd5-join-code", logs.Replace("/join/rd5-join-code-0123", ""))
    }

[<Fact>]
let ``RD6 the same key gives the same hash within an install and other secrets give other hashes`` () =
    task {
        use! one = bootSeededRequestLog "default"
        let first = redacted one botKey
        Assert.Equal(first, redacted one botKey)
        Assert.NotEqual<string>(first, redacted one (botKey + "x"))
        // The redactor is derived from the secret, so a second app on the same secret agrees and another secret does not.
        use! two = bootSeededRequestLog "default"
        Assert.Equal(first, redacted two botKey)
        let services = ServiceCollection()
        Logging.add services (Campfire.RailsCompat.Secrets.create "another-secret-key-base")
        use provider = services.BuildServiceProvider()
        let other = (Logging.redactor (provider.GetRequiredService<Microsoft.Extensions.Compliance.Redaction.IRedactorProvider>())).Redact botKey
        Assert.NotEqual<string>(first, other)
        Assert.DoesNotContain(botKey, first)
    }

/// SR-J3 (Phase 8 security review): the router matches literal segments without regard to case, so `/ROOMS/:id/:bot_key/MESSAGES`
/// is the bot API too; the request line and the failure line must not carry its key either.
[<Fact>]
let ``RD7 a bot API path in another letter case logs no key`` () =
    task {
        use! test = bootSeededRequestLog "default"
        let bot = anonymous test
        let statuses = ResizeArray<string>()
        for path in
            [ $"/ROOMS/{ALL_TALK}/{botKey}/MESSAGES"
              $"/Rooms/{ALL_TALK}/{botKey}/Messages.json"
              $"/rooms/{ALL_TALK}/{botKey}/messages/1/BOOSTS" ] do
            let! reply = bot.Send((request "POST" path).WithBody "Beep boop")
            statuses.Add $"{path}={reply.Status}"
        let logs = shipped test
        Assert.False(logs.Contains botKey || logs.Contains botKeyLabel, $"""the key was logged ({String.Join(", ", statuses)})""")
        Assert.Contains($"Path: /ROOMS/{ALL_TALK}/{redacted test botKey}/MESSAGES", logs)
    }
