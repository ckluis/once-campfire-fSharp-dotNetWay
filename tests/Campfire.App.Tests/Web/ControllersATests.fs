// plans/design-kit-auth.md section 4, unit 2.9 (Controllers A): the sessions, first run, welcome, PWA, transfer, health,
// Turbo Native and Action Mailbox controllers as Web actions, over the whole app on a parity seed. N1-N8 check what the
// move to `HttpContext` could change: the headers a Web action's response carries (Rails' default headers and cache
// control, the version headers of the chain), the answers of the controllers outside `ApplicationController`, the CSRF order
// of the conductor, the per-IP sign-in limiter (K15) and the authentication order of the root page.
module Campfire.App.Tests.Web.ControllersATests

open System
open System.Threading.Tasks
open Xunit
open Campfire.App.Tests.Support

let private chromeAgent =
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"

[<Fact>]
let ``N1 a page of a Web action has Rails' default headers, the version headers and no body digest`` () =
    task {
        use! test = bootSeeded "default"
        let! page = test.Send((get "/session/new").With("user-agent", chromeAgent))
        Assert.Equal(200, page.Status)
        Assert.Equal(Some "text/html; charset=utf-8", page.ContentType)
        Assert.Equal(Some "SAMEORIGIN", page.Header "x-frame-options")
        Assert.Equal(Some "nosniff", page.Header "x-content-type-options")
        Assert.Equal(Some "strict-origin-when-cross-origin", page.Header "referrer-policy")
        Assert.True((page.Header "x-version").IsSome, "the chain's version header")
        // The default cache control of a page with a body; K14: no `Rack::ETag` digest.
        Assert.Equal(Some "max-age=0, private, must-revalidate", page.Header "cache-control")
        Assert.Equal(None, page.Header "etag")
        Assert.True((page.Header "link").IsSome, "the stylesheets' preload header")
        // A second look sends the same headers once each.
        Assert.Equal(1, (page.HeaderValues "x-frame-options").Length)
        Assert.Equal(1, (page.HeaderValues "cache-control").Length)
    }

[<Fact>]
let ``N2 a redirect and a bare head answer no-cache with the default headers`` () =
    task {
        use! test = bootSeeded "default"
        let! root = test.Send(get "/")
        Assert.Equal(302, root.Status)
        Assert.Equal(Some "http://campfire.test/session/new", root.Location)
        // (The session middleware widens it to `no-cache,no-store` when it sets its cookie for the return-to URL.)
        Assert.StartsWith("no-cache", root.Header "cache-control" |> Option.defaultValue "")
        Assert.Equal(Some "SAMEORIGIN", root.Header "x-frame-options")
        let! ingress = test.Send(request "POST" "/rails/action_mailbox/relay/inbound_emails")
        Assert.Equal(404, ingress.Status)
        Assert.Equal(Some "text/html", ingress.ContentType)
        Assert.Equal(None, ingress.Header "x-version")
        Assert.Equal(Some "SAMEORIGIN", ingress.Header "x-frame-options")
    }

[<Fact>]
let ``N3 the health check answers HTML and JSON, without the chain`` () =
    task {
        use! test = bootSeeded "default"
        let! html = test.Send(get "/up")
        Assert.Equal(200, html.Status)
        Assert.Contains("background-color: green", html.Text)
        Assert.Equal(Some "text/html; charset=utf-8", html.ContentType)
        Assert.Equal(None, html.Header "x-version")
        let! json = test.Send((get "/up.json"))
        Assert.Equal(200, json.Status)
        Assert.Equal(Some "application/json; charset=utf-8", json.ContentType)
        Assert.Equal("up", (json.Json.RootElement.GetProperty "status").GetString())
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", (json.Json.RootElement.GetProperty "timestamp").GetString())
        // Neither format is acceptable: Rails' `respond_to` answers 406.
        let! xml = test.Send((get "/up").With("accept", "image/png"))
        Assert.Equal(406, xml.Status)
    }

[<Fact>]
let ``N4 Turbo Native navigation answers its text`` () =
    task {
        use! test = bootSeeded "default"
        for path, text in [ "/recede_historical_location", "Going back…"; "/resume_historical_location", "Staying put…"; "/refresh_historical_location", "Refreshing…" ] do
            let! reply = test.Send(get path)
            Assert.Equal(200, reply.Status)
            Assert.Equal(text, reply.Text)
            Assert.Equal(Some "text/html; charset=utf-8", reply.ContentType)
    }

[<Fact>]
let ``N5 the conductor checks the request's origin before it refuses`` () =
    task {
        use! test = bootSeeded "default"
        let! sameOrigin = test.Send((request "POST" "/rails/conductor/action_mailbox/inbound_emails").With("sec-fetch-site", "same-origin"))
        Assert.Equal(403, sameOrigin.Status)
        let! crossSite = test.Send((request "POST" "/rails/conductor/action_mailbox/inbound_emails").With("sec-fetch-site", "cross-site"))
        Assert.Equal(422, crossSite.Status)
        // A GET is not a write: the conductor refuses it as the development-only page it is.
        let! read = test.Send(get "/rails/conductor/action_mailbox/inbound_emails")
        Assert.Equal(403, read.Status)
    }

[<Fact>]
let ``N6 the PWA files are open to anyone and their bodies are Rails'`` () =
    task {
        use! test = bootSeeded "default"
        let! worker = test.Send(get "/service-worker.js")
        Assert.Equal(200, worker.Status)
        Assert.Equal(Some "text/javascript; charset=utf-8", worker.ContentType)
        Assert.Contains("self.addEventListener", worker.Text)
        let! manifest = test.Send(get "/webmanifest.json")
        Assert.Equal(200, manifest.Status)
        Assert.Equal(Some "application/json; charset=utf-8", manifest.ContentType)
        Assert.True((manifest.Json.RootElement.GetProperty "name").GetString() <> null, "the manifest names the app")
        // `skip_forgery_protection` and `allow_unauthenticated_access`: the request has no session.
        Assert.Equal(None, worker.Header "set-cookie")
    }

[<Fact>]
let ``N7 the root goes to the last room, and to sign in for a visitor`` () =
    task {
        use! test = bootSeeded "default"
        let visitor = anonymous test
        let! signedOut = visitor.Get "/"
        Assert.Equal(302, signedOut.Status)
        Assert.Equal(Some "http://campfire.test/session/new", signedOut.Location)
        let b = david test
        let! root = b.Get "/"
        Assert.Equal(302, root.Status)
        Assert.Matches(@"^http://campfire\.test/rooms/\d+$", root.Location.Value)
        // The cookie wins over the user's first room when the user is in that room.
        b.SetCookie("last_room", string HQ)
        let! remembered = b.Get "/"
        Assert.Equal(Some $"http://campfire.test/rooms/{HQ}", remembered.Location)
    }

[<Fact>]
let ``N8 the sign in limit is per client address`` () =
    task {
        use! test = bootSeeded "default"
        let first = browser test "198.51.100.60"
        let second = browser test "198.51.100.61"
        let! _ = first.Get "/session/new"
        let! _ = second.Get "/session/new"
        for _ in 1..10 do
            let! reply = first.Form("post", "/session", [ "email_address", "david@37signals.com"; "password", "wrong" ])
            Assert.Equal(401, reply.Status)
        let! limited = first.Form("post", "/session", [ "email_address", "david@37signals.com"; "password", "wrong" ])
        Assert.Equal(429, limited.Status)
        Assert.Contains("Too many requests or unauthorized.", limited.Text)
        // Another client has its own window, and the right password still gets the limited one nowhere.
        let! other = second.Form("post", "/session", [ "email_address", "david@37signals.com"; "password", "wrong" ])
        Assert.Equal(401, other.Status)
        let! stillLimited = first.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(429, stillLimited.Status)
        let! signedIn = second.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(302, signedIn.Status)
    }
