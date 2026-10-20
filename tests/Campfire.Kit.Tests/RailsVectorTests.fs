// Port of rust/crates/kit/tests/rails_vectors.rs
//
// Old-browser-tab continuity: a session cookie and a signed cookie issued by the reference Rails
// app (`vectors/rails_compat.json`) are accepted by the kit, and what the kit writes back decodes
// to the same session. Forgery protection is by `Sec-Fetch-Site` rather than Rails' tokens, so a
// tab opened before an upgrade keeps working without one.
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

let private createSession (c: Ctx) =
    act {
        let session = c.Session()
        let csrf = match session.Get "_csrf_token" with ValueSome v -> v | ValueNone -> Value.Null
        let id = match session.Id with null -> Value.Null | id -> Value.String id
        let body = Value.Object [ "id", id; "csrf", csrf ]
        session.Insert("return_to_after_authenticating", "/rooms/1")
        return c.Json(Status.Ok, body)
    }

let private whoami (c: Ctx) =
    act {
        let token = match c.Cookies.Signed "session_token" with null -> Value.Null | t -> Value.String t
        return c.Json(Status.Ok, Value.Object [ "token", token ])
    }

let private startApp () : Task<TestApp * Secrets * Timestamp> =
    task {
        let kit, secrets, now = kitFromVectors ()
        let route = Adapter.route kit
        let! app = start kit [ route "/session" [ "POST", createSession ]; route "/whoami" [ "GET", whoami ] ]
        return app, secrets, now
    }

let private postSession (app: TestApp) (cookie: string) (site: string) (token: string option) (origin: string option) : Task<Reply> =
    let request =
        (post "/session").With("host", "localhost:3000").With("cookie", cookie).With("sec-fetch-site", site)
    let request = match origin with Some origin -> request.With("origin", origin) | None -> request
    let request =
        match token with
        | Some token -> request.Form $"authenticity_token={CookieJar.escape token}"
        | None -> request.WithBody ""
    app.Send request

[<Fact>]
let ``rails sessions carry over`` () =
    task {
        let! app, secrets, now = startApp ()
        use _ = app
        let session = root().GetProperty "session"
        let sessionCookieRaw = str (session.GetProperty "session_cookie_raw")
        let cookie = "_campfire_session=" + CookieJar.escape sessionCookieRaw
        let formToken = str (session.GetProperty "session_form_token")

        // A form from a page Rails rendered still posts its token; it's ignored, not required.
        let! fromOldTab = postSession app cookie "same-origin" (Some formToken) None
        Assert.Equal(200, fromOldTab.Status)
        let setCookie = fromOldTab.Cookies |> List.exactlyOne
        let expectedSession = session.GetProperty "session"
        Assert.Equal(Some(Value.String(str (expectedSession.GetProperty "session_id"))), fromOldTab.Json.TryGet "id")
        // Rails' token stays in the session, unused.
        Assert.Equal(Some(Value.String(str (expectedSession.GetProperty "_csrf_token"))), fromOldTab.Json.TryGet "csrf")

        // What we write back after a change is the same session plus the change, in Rails' format.
        let raw = Cookies.unescape ((setCookie.Substring("_campfire_session=".Length)).Split(';')[0])
        let decoded = (Cookies.decrypt secrets "_campfire_session" raw now).Value
        let expected =
            match (Json.parse (Text.Encoding.UTF8.GetBytes(expectedSession.GetRawText()))).Value with
            | Value.Object entries -> Value.Object(entries @ [ "return_to_after_authenticating", Value.String "/rooms/1" ])
            | other -> other
        Assert.Equal(expected, decoded)
        Assert.EndsWith("; path=/; expires=Mon, 01 Jan 2046 12:00:00 GMT; httponly; samesite=lax", setCookie)

        let! noToken = postSession app cookie "same-origin" None None
        Assert.Equal(200, noToken.Status)
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
