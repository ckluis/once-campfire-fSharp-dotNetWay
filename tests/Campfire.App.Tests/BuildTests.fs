// plans/design-jobs.md J14 (unit 8.7a): `Boot.build` is the app's one host. BB1-BB4 build it over an in-memory configuration on free
// loopback ports: the database is prepared before Kestrel binds, both ports answer, the services come from the host's provider, and a
// bad configuration fails the build before a listener exists.
module Campfire.App.Tests.BuildTests

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Campfire.App

let private freePort () : int =
    let listener = new TcpListener(IPAddress.Loopback, 0)
    listener.Start()
    let port = (listener.LocalEndpoint :?> IPEndPoint).Port
    listener.Stop()
    port

let private configuration (dir: string) (httpPort: int) (targetPort: int) (extra: (string * string) list) : WebApplicationBuilder -> unit =
    fun builder ->
        let values =
            Dictionary<string, string | null>(
                dict (
                    [ "SECRET_KEY_BASE", "build-test-secret-key-base"
                      "DISABLE_SSL", "true"
                      "CAMPFIRE_STORAGE_PATH", dir
                      "HTTP_PORT", string httpPort
                      "TARGET_PORT", string targetPort
                      "LOG_REQUESTS", "false" ]
                    @ extra
                )
            )
        builder.Configuration.AddInMemoryCollection values |> ignore

/// Records, when the host starts it, whether the database file exists and whether HTTP_PORT already accepts connections.
type private StartProbe(databasePath: string, port: int) =
    member val DatabaseExisted = false with get, set
    member val PortOpen = true with get, set

    interface IHostedService with
        member this.StartAsync(_: CancellationToken) : Task =
            task {
                this.DatabaseExisted <- File.Exists databasePath
                use client = new TcpClient()
                try
                    do! client.ConnectAsync(IPAddress.Loopback, port)
                    this.PortOpen <- true
                with _ ->
                    this.PortOpen <- false
            }

        member _.StopAsync(_: CancellationToken) : Task = Task.CompletedTask

let private withTempDir (f: string -> Task) : Task =
    task {
        let dir = Directory.CreateTempSubdirectory("campfire-build-test").FullName
        try
            do! f dir
        finally
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools()
            try
                Directory.Delete(dir, true)
            with _ ->
                ()
    }

[<Fact>]
let ``BB1 the database is prepared before Kestrel binds, and /up answers on HTTP_PORT and on TARGET_PORT`` () =
    withTempDir (fun dir ->
        task {
            let httpPort, targetPort = freePort (), freePort ()
            let databasePath = Path.Combine(dir, "db", "production.sqlite3")
            let probe = StartProbe(databasePath, httpPort)
            let app =
                Boot.build NullLoggerFactory.Instance (configuration dir httpPort targetPort []) (fun services ->
                    services.AddSingleton<IHostedService>(probe) |> ignore)
            do! app.StartAsync()
            try
                Assert.True(probe.DatabaseExisted, "the database file exists when the first hosted service starts")
                Assert.False(probe.PortOpen, "nothing listens on HTTP_PORT while hosted services start")
                use client = new HttpClient()
                for port in [ httpPort; targetPort ] do
                    let! response = client.GetAsync($"http://127.0.0.1:{port}/up")
                    Assert.Equal(HttpStatusCode.OK, response.StatusCode)
                let! first = client.GetAsync($"http://127.0.0.1:{httpPort}/session/new")
                Assert.NotEqual(HttpStatusCode.InternalServerError, first.StatusCode)
            finally
                app.StopAsync().GetAwaiter().GetResult()
                (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
            Boot.closeDatabaseAt databasePath NullLogger.Instance
        }
        :> Task)

[<Fact>]
let ``BB2 the app's services are the host's: one AppState, one client factory, the cable from the same provider`` () =
    withTempDir (fun dir ->
        task {
            let app = Boot.build NullLoggerFactory.Instance (configuration dir (freePort ()) (freePort ()) []) ignore
            do! app.StartAsync()
            try
                let state = app.Services.GetRequiredService<AppState>()
                Assert.Same(state, app.Services.GetRequiredService<AppState>())
                Assert.Same(app.Services.GetRequiredService<System.Net.Http.IHttpClientFactory>(), state.Http)
                Assert.Same(app.Services.GetRequiredService<Microsoft.Extensions.Caching.Hybrid.HybridCache>(), state.Fragments)
                Assert.Same(app.Services.GetRequiredService<Campfire.Cable.CableServer<Campfire.App.Channels.CableUser>>(), state.Cable)
                Assert.Same(app.Services.GetRequiredService<Campfire.Db.WriteGate>(), state.Gate)
                Assert.Same(app.Services.GetRequiredService<Campfire.App.Jobs>(), state.Jobs)
                Assert.True(state.WebPush.IsNone, "no VAPID keys: Web Push is off")
                Assert.Equal(dir, Path.GetDirectoryName(Path.GetDirectoryName(state.Config.Storage.Database)))
            finally
                app.StopAsync().GetAwaiter().GetResult()
                (app :> IAsyncDisposable).DisposeAsync().AsTask().GetAwaiter().GetResult()
        }
        :> Task)

[<Fact>]
let ``BB3 a bad configuration fails the build, before a listener exists`` () =
    withTempDir (fun dir ->
        task {
            let fails (extra: (string * string) list) : string =
                let e =
                    Assert.Throws<Exception>(fun () ->
                        Boot.build NullLoggerFactory.Instance (configuration dir (freePort ()) (freePort ()) extra) ignore |> ignore)
                e.Message
            Assert.Contains("secret_key_base", fails [ "SECRET_KEY_BASE", ""; "SECRET_KEY_BASE_DUMMY", "" ])
            Assert.Contains("JOB_CONCURRENCY=\"two\" is not a number", fails [ "JOB_CONCURRENCY", "two" ])
            Assert.Contains("CAMPFIRE_FROZEN_TIME=yesterday is not an RFC 3339 timestamp", fails [ "CAMPFIRE_FROZEN_TIME", "yesterday" ])
        }
        :> Task)
