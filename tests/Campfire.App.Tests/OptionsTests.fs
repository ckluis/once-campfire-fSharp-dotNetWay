// plans/design-jobs.md J13 (unit 8.8): OP1-OP7. `CampfireOptions` and `FrontSettings` are `IOptions`, filled from the host's
// configuration (the tests of Rust's `config.rs` and Thruster's names are OP1 and OP3), validated with Rails' own messages, and checked
// when the host starts: a bad configuration fails `StartAsync`, before a listener binds.
module Campfire.App.Tests.OptionsTests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Sockets
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging.Abstractions
open Microsoft.Extensions.Options
open Xunit
open Campfire.App
open Campfire.App.Web
open Campfire.App.Tests.Support

let private freePort () : int =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

/// A provider with the options registered over an in-memory configuration, the way `Boot.build` registers them.
let private providerOf (vars: (string * string) list) : ServiceProvider =
    let configuration =
        ConfigurationBuilder()
            .AddInMemoryCollection(vars |> List.map (fun (k, v) -> KeyValuePair<string, string | null>(k, v)))
            .Build()
    let services = ServiceCollection()
    services.AddSingleton<IConfiguration>(configuration) |> ignore
    CampfireOptionsRegistration.add services
    FrontSettingsRegistration.add services
    services.BuildServiceProvider()

let private options (vars: (string * string) list) : CampfireOptions =
    use provider = providerOf vars
    provider.GetRequiredService<IOptions<CampfireOptions>>().Value

/// The message of the validation failure.
let private failure (vars: (string * string) list) : string =
    let e = Assert.Throws<OptionsValidationException>(fun () -> options vars |> ignore)
    e.Message

let private front (vars: (string * string) list) : FrontSettings =
    use provider = providerOf vars
    provider.GetRequiredService<IOptions<FrontSettings>>().Value

let private withSecret (vars: (string * string) list) = ("SECRET_KEY_BASE", "abc") :: vars

[<Fact>]
let ``OP1 requires a secret key base, and the dummy makes a throwaway one`` () =
    Assert.Contains("Missing `secret_key_base` for 'production' environment, set SECRET_KEY_BASE", failure [])
    Assert.Equal(128, (options [ "SECRET_KEY_BASE_DUMMY", "1" ]).SecretKeyBase.Length)

[<Fact>]
let ``OP1 production defaults`` () =
    let config = options (withSecret [])
    Assert.False config.DisableSsl
    Assert.Equal("0", config.AppVersion)
    Assert.Equal(None, config.GitRevision)
    Assert.Equal(Path.Combine("storage", "db", "production.sqlite3"), config.Storage.Database)
    Assert.Equal(Path.Combine("storage", "files"), config.Storage.Files)
    Assert.Equal(Path.Combine("storage", "backups", "production.sqlite3"), StoragePaths.backupFile config.Storage)
    Assert.Equal(32L * 1024L * 1024L, config.FragmentBytes)
    Assert.Equal(2, config.JobConcurrency)

[<Fact>]
let ``OP1 fragment cache size in megabytes`` () =
    Assert.Equal(64L * 1024L * 1024L, (options (withSecret [ "CAMPFIRE_FRAGMENT_CACHE_MB", "64" ])).FragmentBytes)
    Assert.Contains("CAMPFIRE_FRAGMENT_CACHE_MB=\"lots\" is not a number", failure (withSecret [ "CAMPFIRE_FRAGMENT_CACHE_MB", "lots" ]))

[<Fact>]
let ``OP1 a number that is not one names the variable and quotes the value, as Ruby's parse does`` () =
    Assert.Equal("JOB_CONCURRENCY=\"two \\\"many\\\"\" is not a number", failure (withSecret [ "JOB_CONCURRENCY", "two \"many\"" ]))
    Assert.Equal(3, (options (withSecret [ "JOB_CONCURRENCY", "+3" ])).JobConcurrency)
    Assert.Equal(1, (options (withSecret [ "JOB_CONCURRENCY", "0" ])).JobConcurrency)

[<Fact>]
let ``OP1 the secret is the first problem reported`` () =
    Assert.Contains("secret_key_base", failure [ "JOB_CONCURRENCY", "two" ])

[<Fact>]
let ``OP1 version falls back to the revision`` () =
    let config = options (withSecret [ "APP_VERSION", ""; "GIT_REVISION", "abc123" ])
    Assert.Equal("abc123", config.AppVersion)
    Assert.Equal(Some "abc123", config.GitRevision)

[<Fact>]
let ``OP1 disable ssl is any non blank value`` () =
    Assert.True((options (withSecret [ "DISABLE_SSL", "false" ])).DisableSsl)
    Assert.False((options (withSecret [ "DISABLE_SSL", " " ])).DisableSsl)

[<Fact>]
let ``OP1 vapid keys must not be blank`` () =
    let config = options (withSecret [ "VAPID_PUBLIC_KEY", ""; "VAPID_PRIVATE_KEY", " " ])
    Assert.Equal((None, None), (config.VapidPublicKey, config.VapidPrivateKey))

[<Fact>]
let ``OP1 vapid subject defaults to the tls domain`` () =
    let subject (vars: (string * string) list) = (options (withSecret vars)).VapidSubject
    Assert.Equal("mailto:ops@example.com", subject [ "VAPID_SUBJECT", "mailto:ops@example.com"; "TLS_DOMAIN", "chat.example.com" ])
    Assert.Equal("https://chat.example.com", subject [ "TLS_DOMAIN", " , chat.example.com,other.example.com" ])
    // Without a TLS domain it is this port's project, where Rust's names its own (a divergence in README.md).
    Assert.Equal("https://github.com/ckluis/once-campfire-fsharp", subject [ "VAPID_SUBJECT", " " ])

[<Fact>]
let ``OP1 storage overrides`` () =
    let config = options (withSecret [ "CAMPFIRE_STORAGE_PATH", "/rails/storage"; "CAMPFIRE_FILES_PATH", "/seed/storage" ])
    Assert.Equal("/rails/storage/db/production.sqlite3", config.Storage.Database)
    Assert.Equal("/seed/storage", config.Storage.Files)

/// A host over the options and a free pair of ports, not started.
let private build (dir: string) (extra: (string * string) list) : WebApplication =
    Boot.build
        NullLoggerFactory.Instance
        (fun builder ->
            let values =
                Dictionary<string, string | null>(
                    dict (
                        [ "SECRET_KEY_BASE", "options-test-secret-key-base"
                          "DISABLE_SSL", "true"
                          "CAMPFIRE_STORAGE_PATH", dir
                          "HTTP_PORT", string (freePort ())
                          "TARGET_PORT", string (freePort ())
                          "LOG_REQUESTS", "false" ]
                        @ extra
                    )
                )
            builder.Configuration.AddInMemoryCollection values |> ignore)
        ignore

let private withScratchDir (f: string -> Task) : Task =
    task {
        let dir = Directory.CreateTempSubdirectory("campfire-options-test").FullName
        try
            do! f dir
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
            try
                Directory.Delete(dir, true)
            with _ ->
                ()
    }

/// Building does not throw; `StartAsync` does, with `message`, and nothing listens on the host's ports.
let private failsAtStart (extra: (string * string) list) (message: string) : Task =
    withScratchDir (fun dir ->
        task {
            let httpPort = freePort ()
            let targetPort = freePort ()
            let app = build dir ([ "HTTP_PORT", string httpPort; "TARGET_PORT", string targetPort ] @ extra)
            let! e = Assert.ThrowsAnyAsync<Exception>(fun () -> app.StartAsync())
            Assert.Contains(message, e.Message)
            for port in [ httpPort; targetPort ] do
                use client = new TcpClient()
                let! connected =
                    task {
                        try
                            do! client.ConnectAsync(IPAddress.Loopback, port)
                            return true
                        with _ ->
                            return false
                    }
                Assert.False(connected, $"nothing listens on {port}")
            (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
        }
        :> Task)

[<Fact>]
let ``OP2 a missing SECRET_KEY_BASE fails StartAsync with Rails' message before a listener binds`` () =
    failsAtStart [ "SECRET_KEY_BASE", "" ] "Missing `secret_key_base` for 'production' environment, set SECRET_KEY_BASE"

[<Fact>]
let ``OP2 a non-number JOB_CONCURRENCY fails StartAsync with the variable and the value`` () =
    failsAtStart [ "JOB_CONCURRENCY", "two" ] "JOB_CONCURRENCY=\"two\" is not a number"

[<Fact>]
let ``OP3 THRUSTER_HTTP_PORT wins over HTTP_PORT, and Go's ParseBool and Atoi apply`` () =
    let settings = front [ "HTTP_PORT", "8080"; "THRUSTER_HTTP_PORT", "9090"; "HTTPS_PORT", "8443"; "LOG_REQUESTS", "yes"; "CACHE_SIZE", "1.5" ]
    Assert.Equal(9090, settings.HttpPort)
    Assert.Equal(8443, settings.HttpsPort)
    // "yes" is not Go's boolean and "1.5" is not an Atoi: the defaults.
    Assert.True settings.LogRequests
    Assert.Equal(64L * 1024L * 1024L, settings.CacheSize)
    Assert.False((front [ "LOG_REQUESTS", "F" ]).LogRequests)
    Assert.Equal(7L, (front [ "THRUSTER_CACHE_SIZE", "+7"; "CACHE_SIZE", "9" ]).CacheSize)

[<Fact>]
let ``OP4 TLS_DOMAIN with no usable name fails at start, before a listener binds`` () =
    failsAtStart [ "TLS_DOMAIN", "10.0.0.1" ] "TLS_DOMAIN holds no usable name"

[<Fact>]
let ``OP4 a list with one usable name is valid`` () =
    Assert.Equal<string list>([ "nodot"; "chat.example.test" ], (front [ "TLS_DOMAIN", "nodot,chat.example.test" ]).TlsDomains)
    use provider = providerOf [ "TLS_DOMAIN", "nodot,chat.example.test" ]
    provider.GetRequiredService<IOptions<FrontSettings>>().Value |> ignore

[<Fact>]
let ``OP5 a bad CAMPFIRE_FROZEN_TIME fails at start`` () =
    failsAtStart [ "CAMPFIRE_FROZEN_TIME", "yesterday" ] "CAMPFIRE_FROZEN_TIME=yesterday is not an RFC 3339 timestamp"

[<Fact>]
let ``OP5 a good one freezes the clock`` () =
    withScratchDir (fun dir ->
        task {
            let app = build dir [ "CAMPFIRE_FROZEN_TIME", "2026-03-02T16:00:00Z" ]
            do! app.StartAsync()
            try
                Assert.Equal(DateTimeOffset.Parse "2026-03-02T16:00:00Z", app.Services.GetRequiredService<TimeProvider>().GetUtcNow())
            finally
                app.StopAsync().GetAwaiter().GetResult()
                (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
        }
        :> Task)

[<Fact>]
let ``OP6 invalid VAPID keys do not fail: the host boots, logs that Web Push is off and serves /up`` () =
    withScratchDir (fun dir ->
        task {
            let logs = new LogCapture()
            let httpPort = freePort ()
            let app =
                Boot.build
                    logs
                    (fun builder ->
                        let values =
                            Dictionary<string, string | null>(
                                dict
                                    [ "SECRET_KEY_BASE", "options-test-secret-key-base"
                                      "DISABLE_SSL", "true"
                                      "CAMPFIRE_STORAGE_PATH", dir
                                      "HTTP_PORT", string httpPort
                                      "TARGET_PORT", string (freePort ())
                                      "LOG_REQUESTS", "false"
                                      "VAPID_PUBLIC_KEY", "AAAA"
                                      "VAPID_PRIVATE_KEY", "BBBB" ]
                            )
                        builder.Configuration.AddInMemoryCollection values |> ignore)
                    ignore
            do! app.StartAsync()
            try
                use client = new System.Net.Http.HttpClient()
                let! response = client.GetAsync($"http://127.0.0.1:{httpPort}/up")
                Assert.Equal(HttpStatusCode.OK, response.StatusCode)
                Assert.Contains("Web Push is off", logs.Text)
                Assert.True((app.Services.GetRequiredService<AppState>()).WebPush.IsNone)
            finally
                app.StopAsync().GetAwaiter().GetResult()
                (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
        }
        :> Task)

[<Fact>]
let ``OP7 the options are one singleton, read once, not per request or per scope`` () =
    let configuration = ConfigurationBuilder().AddInMemoryCollection([ KeyValuePair<string, string | null>("SECRET_KEY_BASE", "abc") ]).Build()
    let services = ServiceCollection()
    services.AddSingleton<IConfiguration>(configuration) |> ignore
    CampfireOptionsRegistration.add services
    FrontSettingsRegistration.add services
    use provider = services.BuildServiceProvider()
    let first = provider.GetRequiredService<IOptions<CampfireOptions>>().Value
    configuration["SECRET_KEY_BASE"] <- "changed"
    use scope = provider.CreateScope()
    Assert.Same(first, scope.ServiceProvider.GetRequiredService<IOptions<CampfireOptions>>().Value)
    Assert.Same(first, provider.GetRequiredService<CampfireOptions>())
    Assert.Equal("abc", first.SecretKeyBase)
    Assert.Same(provider.GetRequiredService<IOptions<FrontSettings>>().Value, provider.GetRequiredService<FrontSettings>())

/// SR-J4 (Phase 8 security review): the option records hold `SECRET_KEY_BASE`, `VAPID_PRIVATE_KEY` and `EAB_HMAC_KEY`. Printed (a log
/// template's `{Options}`, `%A`, an exception message built from one), they show that a secret is set and never what it is.
[<Fact>]
let ``OP8 the option records never print a secret`` () =
    let secrets = [ "sekrit-base-0123456789"; "vapid-private-0123456789"; "eab-hmac-0123456789" ]
    let campfire = options [ "SECRET_KEY_BASE", secrets[0]; "VAPID_PUBLIC_KEY", "vapid-public"; "VAPID_PRIVATE_KEY", secrets[1] ]
    let settings = front [ "EAB_KID", "kid-1"; "EAB_HMAC_KEY", secrets[2] ]
    Assert.True(settings.ExternalAccount.IsSome)
    for text in [ string campfire; sprintf "%A" campfire; sprintf "%O" campfire; string settings; sprintf "%A" settings; sprintf "%O" settings ] do
        for secret in secrets do
            Assert.DoesNotContain(secret, text)
    // What is not secret still prints, so the records stay useful in a debugger and a log.
    Assert.Contains("vapid-public", string campfire)
    Assert.Contains("kid-1", string settings)
    Assert.Contains("[FILTERED]", string campfire)
