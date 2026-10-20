// plans/design-kit-auth.md section 4, unit 2.11 (Controllers C): autocompletable, QR code, unfurl links, searches and the
// Active Storage endpoints as Web actions, over the whole app on the `default` parity seed. K1-K6 check what the move to
// `HttpContext` could change: the mention list's formats and headers, the QR code's cache and bad input, the search
// params, the unfurl `require`, and the Active Storage authentication (`AuthenticateAsync "Session"` or 401, never
// resuming), CSRF and a direct upload read back through the redirect, proxy and disk endpoints, whole and by range.
module Campfire.App.Tests.Web.ControllersCTests

open System
open System.Security.Cryptography
open System.Text
open System.Text.Json
open Xunit
open Microsoft.Data.Sqlite
open Campfire.App.Tests.Support

let private scalar (test: Test) (sql: string) : string =
    test.Sql(fun conn ->
        use command = new SqliteCommand(sql, conn)
        match command.ExecuteScalar() with
        | null -> "<none>"
        | value -> string value)

[<Fact>]
let ``K1 the mention list: formats, headers, the room filter and sign-in`` () =
    task {
        use! test = bootSeeded "default"
        let! anonymousReply = (anonymous test).Get "/autocompletable/users"
        Assert.Equal(302, anonymousReply.Status)
        Assert.Equal(Some "http://campfire.test/session/new", anonymousReply.Location)

        let b = david test
        let! html = b.Get "/autocompletable/users?filter=kev"
        Assert.Equal(200, html.Status)
        Assert.Equal(Some "text/html; charset=utf-8", html.ContentType)
        Assert.Contains("lexxy-prompt-item", html.Text)
        Assert.DoesNotContain("<html", html.Text)

        let! json = b.Send((get "/autocompletable/users?query=kev").With("accept", "application/json"))
        Assert.Equal(200, json.Status)
        Assert.Equal(Some "application/json; charset=utf-8", json.ContentType)
        Assert.True((json.Header "x-total-count").IsSome, "the paginated headers")
        Assert.Contains("Kevin", json.Text)

        // A blank room is every user; an unknown one is a 404; a format the action does not offer is a 406.
        let! blank = b.Get "/autocompletable/users?room_id="
        Assert.Equal(200, blank.Status)
        let! unknown = b.Get "/autocompletable/users?room_id=999999"
        Assert.Equal(404, unknown.Status)
        let! structured = b.Get "/autocompletable/users?room_id[a]=b"
        Assert.Equal(404, structured.Status)
        let! xml = b.Send((get "/autocompletable/users").With("accept", "application/xml"))
        Assert.Equal(406, xml.Status)
    }

[<Fact>]
let ``K2 the QR code is public, cached for a year, and refuses what it cannot encode`` () =
    task {
        use! test = bootSeeded "default"
        let id = Convert.ToBase64String(Encoding.UTF8.GetBytes "https://campfire.test/join/abc").Replace('+', '-').Replace('/', '_')
        let! reply = (anonymous test).Get $"/qr_code/{id}"
        Assert.Equal(200, reply.Status)
        Assert.Equal(Some "image/svg+xml; charset=utf-8", reply.ContentType)
        Assert.Equal(Some "max-age=31556952, public", reply.Header "cache-control")
        Assert.StartsWith("<?xml", reply.Text.TrimStart())
        let! bad = (anonymous test).Get "/qr_code/***"
        Assert.Equal(500, bad.Status)
        let tooMuch = Convert.ToBase64String(Encoding.UTF8.GetBytes(String('x', 20000))).Replace('+', '-').Replace('/', '_')
        let! big = (anonymous test).Get $"/qr_code/{tooMuch}"
        Assert.Equal(422, big.Status)
    }

[<Fact>]
let ``K3 searches: record, show, clear, and the params Rails would choke on`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let! created = b.Form("post", "/searches", [ "q", "hello, world!" ])
        Assert.Equal(302, created.Status)
        Assert.StartsWith("http://campfire.test/search", created.Location.Value)
        Assert.Equal("1", scalar test "select count(*) from searches where query = 'hello  world '")
        let! index = b.Get "/searches?q=hello"
        Assert.Equal(200, index.Status)
        Assert.Contains("hello", index.Text)
        let! cleared = b.Form("delete", "/searches/clear", [])
        Assert.Equal(302, cleared.Status)
        Assert.Equal("0", scalar test "select count(*) from searches")
        // `q` as a hash makes `gsub` raise; a missing one cannot be recorded (NOT NULL).
        let! hash = b.Get "/searches?q[a]=b"
        Assert.Equal(500, hash.Status)
        let! missing = b.Form("post", "/searches", [])
        Assert.Equal(500, missing.Status)
        let! anonymousReply = (anonymous test).Get "/searches"
        Assert.Equal(302, anonymousReply.Status)
    }

[<Fact>]
let ``K4 unfurling needs a url, and a hash for one is no metadata`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let! missing = b.Form("post", "/unfurl_link", [])
        Assert.Equal(400, missing.Status)
        let! blank = b.Form("post", "/unfurl_link", [ "url", " " ])
        Assert.Equal(400, blank.Status)
        let! structured = b.Form("post", "/unfurl_link", [ "url[a]", "b" ])
        Assert.Equal(204, structured.Status)
        let! cross = b.Send((request "POST" "/unfurl_link").Form("url=http%3A%2F%2Fexample.com").With("sec-fetch-site", "cross-site"))
        Assert.Equal(422, cross.Status)
    }

/// The path of an absolute URL on the test host.
let private pathOf (url: string) : string = Uri(url).PathAndQuery

[<Fact>]
let ``K5 the Active Storage writes need a session and a same-origin request`` () =
    task {
        use! test = bootSeeded "default"
        let body = """{"blob":{"filename":"hello.txt","byte_size":5,"checksum":"abc","content_type":"text/plain"}}"""
        let direct (b: Browser) (site: string) =
            b.Send(
                (request "POST" "/rails/active_storage/direct_uploads")
                    .With("content-type", "application/json")
                    .With("sec-fetch-site", site)
                    .WithBody body
            )
        let! signedOut = direct (anonymous test) "same-origin"
        Assert.Equal(401, signedOut.Status)
        Assert.Equal(Some "text/html", signedOut.ContentType)
        let! cross = direct (david test) "cross-site"
        Assert.Equal(422, cross.Status)
        let! refused = (david test).Write((request "POST" "/rails/active_storage/direct_uploads").With("content-type", "application/json").WithBody """{"blob":{"filename":"x","byte_size":"many","checksum":"c"}}""")
        Assert.Equal(422, refused.Status)
        let! missing = (david test).Write((request "POST" "/rails/active_storage/direct_uploads").With("content-type", "application/json").WithBody """{"other":1}""")
        Assert.Equal(400, missing.Status)
        let! huge = (david test).Write((request "POST" "/rails/active_storage/direct_uploads").With("content-type", "application/json").WithBody """{"blob":{"filename":"x","byte_size":99999999999,"checksum":"c"}}""")
        Assert.Equal(413, huge.Status)
        // Authentication here finds the session without resuming it: the row is not written.
        let before = scalar test "select max(last_active_at) from sessions"
        let! ok = direct (david test) "same-origin"
        Assert.Equal(200, ok.Status)
        Assert.Equal(before, scalar test "select max(last_active_at) from sessions")
        Assert.Equal(Some "application/json; charset=utf-8", ok.ContentType)
    }

[<Fact>]
let ``K6 a direct upload read back: disk PUT, redirect, proxy, ranges and the disk cache header`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let data = "hello"
        let checksum = Convert.ToBase64String(MD5.HashData(Encoding.UTF8.GetBytes data))
        let! created =
            b.Write(
                (request "POST" "/rails/active_storage/direct_uploads")
                    .With("content-type", "application/json")
                    .WithBody($"""{{"blob":{{"filename":"hello.png","byte_size":5,"checksum":"{checksum}","content_type":"image/png","metadata":{{"note":true}}}}}}""")
            )
        Assert.Equal(200, created.Status)
        use json = created.Json
        let upload = json.RootElement.GetProperty "direct_upload"
        let url = nonNull (upload.GetProperty("url").GetString())
        let signedId = nonNull (json.RootElement.GetProperty("signed_id").GetString())
        Assert.Equal(5L, json.RootElement.GetProperty("byte_size").GetInt64())
        Assert.Equal(true, json.RootElement.GetProperty("metadata").GetProperty("note").GetBoolean())

        // The PUT needs the session and the token's content type and length.
        let put (browser: Browser) (contentType: string) (payload: string) =
            browser.Send((request "PUT" (pathOf url)).With("content-type", contentType).WithBody payload)
        let! anonymousPut = put (anonymous test) "image/png" data
        Assert.Equal(401, anonymousPut.Status)
        let! wrongType = put b "text/csv" data
        Assert.Equal(422, wrongType.Status)
        let! wrongLength = put b "image/png" "hello!"
        Assert.Equal(422, wrongLength.Status)
        let! stored = put b "image/png" data
        Assert.Equal(204, stored.Status)

        // The signed id redirects to the disk service; the disk URL is public and cached for an hour.
        let! redirect = (anonymous test).Get $"/rails/active_storage/blobs/redirect/{signedId}/hello.png"
        Assert.Equal(302, redirect.Status)
        Assert.Equal(Some "max-age=300, private", redirect.Header "cache-control")
        let! file = (anonymous test).Get(pathOf (redirect.Location.Value))
        Assert.Equal(200, file.Status)
        Assert.Equal("hello", file.Text)
        Assert.Equal(Some "max-age=3600, public", file.Header "cache-control")
        let! partial = (anonymous test).Send((get (pathOf (redirect.Location.Value))).With("range", "bytes=1-3"))
        Assert.Equal(206, partial.Status)
        Assert.Equal("ell", partial.Text)

        // The proxy streams it, forever cacheable, with Rails' own byte ranges.
        let proxy = $"/rails/active_storage/blobs/proxy/{signedId}/hello.png"
        let! whole = (anonymous test).Get proxy
        Assert.Equal(200, whole.Status)
        Assert.Equal("hello", whole.Text)
        Assert.Equal(Some "bytes", whole.Header "accept-ranges")
        Assert.Contains("public", (whole.Header "cache-control").Value)
        Assert.Contains("immutable", (whole.Header "cache-control").Value)
        Assert.True((whole.Header "etag").IsSome)
        Assert.StartsWith("inline;", (whole.Header "content-disposition").Value)
        let! fresh = (anonymous test).Send((get proxy).With("if-none-match", (whole.Header "etag").Value))
        Assert.Equal(304, fresh.Status)
        let! one = (anonymous test).Send((get proxy).With("range", "bytes=1-2"))
        Assert.Equal(206, one.Status)
        Assert.Equal("el", one.Text)
        Assert.Equal(Some "bytes 1-2/5", one.Header "content-range")
        let! many = (anonymous test).Send((get proxy).With("range", "bytes=0-1,3-4"))
        Assert.Equal(206, many.Status)
        Assert.StartsWith("multipart/byteranges; boundary=", (many.Header "content-type").Value)
        Assert.Contains("he", many.Text)
        Assert.Contains("lo", many.Text)
        let! unsatisfiable = (anonymous test).Send((get proxy).With("range", "bytes=50-60"))
        Assert.Equal(416, unsatisfiable.Status)
        // A forged id is a 404 whatever the format; a download disposition is honoured.
        let! forged = (anonymous test).Get "/rails/active_storage/blobs/proxy/nope--nope/hello.png"
        Assert.Equal(404, forged.Status)
        let! attachment = (anonymous test).Get $"{proxy}?disposition=attachment"
        Assert.StartsWith("attachment;", (attachment.Header "content-disposition").Value)
    }
