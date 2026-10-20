// Port of rust/crates/kit/tests/rails_vectors.rs
//
// Old-browser-tab continuity: a signed cookie issued by the reference Rails app (`vectors/rails_compat.json`) is
// accepted by the kit. Rails' encrypted session cookie is no longer read (unit 2.5, K6); App test F5 holds that.
//
// The forgery check is the framework's `ICsrfProtection` now (unit 2.4); its matrix is `Web/CsrfTests.fs` in the App tests.
module Campfire.Kit.Tests.RailsVectorTests

open System
open System.Net
open System.Text.Json
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Xunit
open Campfire.RailsCompat
open Campfire.RailsCompat.Clock
open Campfire.Kit
open Campfire.Kit.Tests.Helpers
open Campfire.Kit.Tests.Harness
open Campfire.Tests

let private vectors = lazy (Repo.vector "rails_compat")

let private root () = vectors.Value.RootElement

let private str (e: JsonElement) : string = nonNull (e.GetString())

let private kitFromVectors () : Kit * Secrets * Timestamp =
    let secrets = Secrets.create (str (root().GetProperty "secret_key_base"))
    let now = ts (str (root().GetProperty "now"))
    Kit(KitConfig.Default, secrets, TestClock.FrozenAt now, null), secrets, now

let private whoami (c: Ctx) =
    act {
        let token = match c.Cookies.Signed "session_token" with null -> Value.Null | t -> Value.String t
        return c.Json(Status.Ok, Value.Object [ "token", token ])
    }

let private startApp () : Task<TestApp * Secrets * Timestamp> =
    task {
        let kit, secrets, now = kitFromVectors ()
        let route = Adapter.route kit
        let! app = start kit [ route "/whoami" [ "GET", whoami ] ]
        return app, secrets, now
    }

[<Fact>]
let ``rails signed session token cookie is read`` () =
    task {
        let! app, _, _ = startApp ()
        use _ = app
        let session = root().GetProperty "session"
        let tokenRaw = str (session.GetProperty "session_token_raw")
        let cookie = "session_token=" + CookieJar.escape tokenRaw
        let! reply = app.Send((get "/whoami").With("cookie", cookie))
        Assert.Equal(Some(Value.String(str (session.GetProperty "session_token_value"))), reply.Json.TryGet "token")
    }

/// A context for `request`, as the adapter would build one.
let private ctxFor (config: KitConfig) (meth: string) (headers: (string * string) list) : Ctx =
    let kit, _, _ = kitFromVectors ()
    let kit = Kit({ config with ForceSsl = config.ForceSsl }, kit.Secrets, kit.Clock, null)
    let map = HeaderDictionary()
    for (k, v) in headers do
        map.Append(k, v)
    let request = Request.Create(meth, meth, "/session", null, null, null, map, IPAddress.Loopback, ReadOnlyMemory.Empty, config.Proxy)
    Ctx(kit, Microsoft.AspNetCore.Http.DefaultHttpContext(), request, ParamMap(), ParamMap(), ParamMap(), CookieJar(kit.Secrets, kit.Clock))
