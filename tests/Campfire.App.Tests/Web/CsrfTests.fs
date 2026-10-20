// plans/design-kit-auth.md section 5, unit 2.4 (CSRF): C1-C12. The whole app over a parity seed; the forgery check is
// .NET 11's `ICsrfProtection`, called from the before-action chain (K5): a form post, a Turbo-style `DELETE` through
// `_method` and a JSON `PATCH`, each against every Fetch Metadata and `Origin` case, and the denial is Rails' 422 page.
module Campfire.App.Tests.Web.CsrfTests

open System.IO
open System.Threading.Tasks
open Xunit
open Campfire.App.Tests.Support

/// The three kinds of write: a form, a `DELETE` through `_method` (`button_to`), and JSON.
let private writes: (string * (unit -> Req)) list =
    [ "form",
      fun () -> (request "POST" $"/rooms/{ALL_TALK}/messages").With("accept", "text/vnd.turbo-stream.html, text/html, application/xhtml+xml").Form(formBody [ "message[body]", "hello" ])
      "delete",
      fun () -> (request "POST" $"/users/{DAVID}/avatar").Form(formBody [ "_method", "delete" ])
      "json",
      fun () ->
          (request "PATCH" "/account")
              .With("content-type", "application/json")
              .With("accept", "application/json")
              .WithBody """{"account":{"name":"Campfire"}}""" ]

let private page422 = lazy (File.ReadAllBytes(Campfire.Tests.Repo.path "reference/public/422.html"))

/// Send a write as a signed-in David (a fresh browser each time).
let private send (test: Test) (build: unit -> Req) (headers: (string * string) list) : Task<Reply> =
    let b = david test
    b.Send(headers |> List.fold (fun (r: Req) (k, v) -> r.With(k, v)) (build ()))

let private allowed (name: string) (reply: Reply) =
    Assert.True((reply.Status < 400), $"{name}: {reply.Status} {reply.Text}")
    // Authentication runs before the forgery check: a redirect to sign in would pass vacuously.
    Assert.False((reply.Location |> Option.exists (fun l -> l.EndsWith "/session/new")), $"{name}: signed out")

let private forged (name: string) (reply: Reply) =
    Assert.True((reply.Status = 422), $"{name}: {reply.Status} {reply.Location}")
    if name.StartsWith "json" then
        Assert.Contains("\"status\":422", reply.Text)
    else
        Assert.Equal<byte[]>(page422.Value, reply.Body)

[<Fact>]
let ``C1 same-origin writes pass`` () =
    task {
        use! test = bootSeeded "default"
        for name, build in writes do
            let! reply = send test build [ "sec-fetch-site", "same-origin" ]
            allowed name reply
    }

[<Fact>]
let ``C2 user-initiated (none) writes pass`` () =
    task {
        use! test = bootSeeded "default"
        for name, build in writes do
            let! reply = send test build [ "sec-fetch-site", "none" ]
            allowed name reply
    }

[<Fact>]
let ``C3 cross-site writes are 422 with the public page`` () =
    task {
        use! test = bootSeeded "default"
        for name, build in writes do
            let! reply = send test build [ "sec-fetch-site", "cross-site" ]
            forged name reply
    }

[<Fact>]
let ``C4 same-site writes (another subdomain) are 422`` () =
    task {
        use! test = bootSeeded "default"
        for name, build in writes do
            let! reply = send test build [ "sec-fetch-site", "same-site" ]
            forged name reply
    }

[<Fact>]
let ``C5 without Fetch Metadata a matching Origin passes`` () =
    task {
        use! test = bootSeeded "default"
        for name, build in writes do
            let! reply = send test build [ "origin", "http://campfire.test" ]
            allowed name reply
    }

[<Fact>]
let ``C6 without Fetch Metadata another Origin is 422`` () =
    task {
        use! test = bootSeeded "default"
        for name, build in writes do
            for origin in [ "http://evil.example"; "https://campfire.test"; "null" ] do
                let! reply = send test build [ "origin", origin ]
                forged $"{name} {origin}" reply
    }

[<Fact>]
let ``C7 neither header (not a browser) passes`` () =
    task {
        use! test = bootSeeded "default"
        for name, build in writes do
            let! reply = send test build []
            allowed name reply
    }

[<Fact>]
let ``C8 a GET is never checked`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let! reply = b.Send((get $"/rooms/{ALL_TALK}").With("sec-fetch-site", "cross-site"))
        Assert.Equal(200, reply.Status)
        let! head = b.Send((request "HEAD" $"/rooms/{ALL_TALK}").With("sec-fetch-site", "cross-site"))
        Assert.Equal(200, head.Status)
    }

[<Fact>]
let ``C9 a cross-site write that reads no form is still refused`` () =
    task {
        use! test = bootSeeded "default"
        // No body at all (a Turbo DELETE with no payload): the check is the headers, not a form read.
        let! reply = send test (fun () -> request "DELETE" $"/users/{DAVID}/avatar") [ "sec-fetch-site", "cross-site" ]
        forged "bodyless delete" reply
        // And the framework's verdict is the only thing that stopped it.
        let! allowedReply = send test (fun () -> request "DELETE" $"/users/{DAVID}/avatar") [ "sec-fetch-site", "same-origin" ]
        allowed "bodyless delete" allowedReply
    }

[<Fact>]
let ``C10 a sec-fetch-site the framework does not know is refused`` () =
    task {
        use! test = bootSeeded "default"
        let! reply = send test (snd writes.Head) [ "sec-fetch-site", "bogus" ]
        forged "bogus" reply
    }

[<Fact>]
let ``C11 a bot key skips the check`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        let cross = (request "POST" $"/rooms/{ALL_TALK}/{BENDER_KEY}/messages").With("sec-fetch-site", "cross-site").WithBody "From afar"
        let! reply = bot.Send cross
        Assert.True((reply.Status = 201), reply.Text)
    }

[<Fact>]
let ``C12 under assume_ssl a browser's https Origin matches`` () =
    task {
        use! test = bootSeededConfigured ignore [ "DISABLE_SSL", " " ] "default"
        for name, build in writes do
            let! reply = send test build [ "origin", "https://campfire.test" ]
            allowed name reply
        // The page's own scheme is https under assume_ssl, so an http Origin is another origin.
        let! reply = send test (snd writes.Head) [ "origin", "http://campfire.test" ]
        forged "http origin under assume_ssl" reply
        // Behind a proxy that says https, the same.
        let! viaProxy = send test (snd writes.Head) [ "x-forwarded-proto", "https"; "origin", "https://campfire.test" ]
        allowed "x-forwarded-proto" viaProxy
    }
