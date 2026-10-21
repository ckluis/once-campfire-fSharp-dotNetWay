// plans/design-kit-auth.md section 5, unit 2.2 (cookie auth): A1-A8. The whole app over a parity seed; the
// `session_token` cookie is ASP.NET Core's cookie scheme with Rails' signed-cookie bytes.
module Campfire.App.Tests.Web.AuthenticationTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.Extensions.DependencyInjection
open Xunit
open Campfire.App.Tests.Support
open Campfire.App.Web
open Campfire.RailsCompat

let private davidSessionId = 481019661L

let private dbTime (at: DateTime) : string = Campfire.Db.RailsTime.toDb at

/// Puts David's seed session's `last_active_at` `age` ago.
/// The harness clock A5 freezes the app on: a fixed instant, so no assertion depends on the wall clock.
let private frozenNow = DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero)

let private ageSessionFrom (now: DateTime) (test: Test) (age: TimeSpan) : Task<unit> =
    test.Execute(
        "UPDATE sessions SET last_active_at = ? WHERE id = ?",
        [ box (dbTime (now - age)); box davidSessionId ]
    )

let private ageSession (test: Test) (age: TimeSpan) : Task<unit> = ageSessionFrom DateTime.UtcNow test age

type private Row = { UserAgent: string | null; Ip: string | null; LastActive: DateTime; UpdatedAt: DateTime }

let private sessionRow (test: Test) (id: int64) : Row =
    test.Sql(fun conn ->
        use command = conn.CreateCommand()
        command.CommandText <- "SELECT user_agent, ip_address, last_active_at, updated_at FROM sessions WHERE id = @id"
        command.Parameters.AddWithValue("@id", id) |> ignore
        use reader = command.ExecuteReader()
        Assert.True(reader.Read())
        let text (i: int) = if reader.IsDBNull i then null else reader.GetString i
        let time (i: int) = DateTime.SpecifyKind(DateTime.Parse(reader.GetString i, Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc)
        { UserAgent = text 0; Ip = text 1; LastActive = time 2; UpdatedAt = time 3 })

let private sessionCount (test: Test) (userId: int64) : int64 =
    test.Sql(fun conn -> RawSql.scalar<int64> conn "SELECT COUNT(*) FROM sessions WHERE user_id = ?" [ box userId ])

let private cookieValue (header: string) : string =
    let pair = header.Split(';')[0]
    pair.Substring(pair.IndexOf '=' + 1)

[<Fact>]
let ``A1 a rails minted cookie signs David in without a new cookie`` () =
    task {
        use! test = bootSeeded "default"
        do! ageSession test (TimeSpan.FromMinutes 5.0)
        let! reply = test.Send(getWithCookie "/account/edit" (davidCookie ()))
        Assert.Equal(200, reply.Status)
        Assert.Contains("current-user-id", reply.Text)
        Assert.Contains(string DAVID, reply.Text)
        Assert.True(reply.Header("set-cookie").IsNone, $"{reply.Headers}")
    }

[<Fact>]
let ``A2 a forged or tampered cookie is sent to sign in and is not deleted`` () =
    task {
        use! test = bootSeeded "default"
        let _, _, forged = sessionVectors ()
        let cookie = davidCookie ()
        let flipped = cookie.Substring(0, cookie.Length - 1) + (if cookie.EndsWith "0" then "1" else "0")
        for cookie in [ forged; flipped ] do
            let! reply = test.Send(getWithCookie "/account/edit" cookie)
            Assert.Equal(302, reply.Status)
            Assert.Equal(Some "http://campfire.test/session/new", reply.Location)
            Assert.False((reply.SetCookies |> List.exists (fun c -> c.StartsWith "session_token=")), $"{reply.SetCookies}")
    }

[<Fact>]
let ``A3 sign in sets the cookie Rails reads, dated 20 calendar years out`` () =
    task {
        use! test = bootSeeded "default"
        let b = browser test "198.51.100.30"
        let before = DateTimeOffset.UtcNow
        let! page = b.Get "/session/new"
        Assert.Equal(200, page.Status)
        let! reply = b.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        let after = DateTimeOffset.UtcNow
        Assert.Equal(302, reply.Status)
        let header = reply.SetCookies |> List.find (fun c -> c.StartsWith "session_token=")
        let lower = header.ToLowerInvariant()
        Assert.Contains("path=/", lower)
        Assert.Contains("httponly", lower)
        Assert.Contains("samesite=lax", lower)
        Assert.DoesNotContain("secure", lower)
        // Rails' verifier reads it, under the purpose `cookie.session_token`, as the new session's token.
        let wire = cookieValue header
        let raw = Uri.UnescapeDataString wire
        let token = test.Sql(fun conn -> RawSql.scalar<string> conn "SELECT token FROM sessions WHERE user_id = ? ORDER BY id DESC LIMIT 1" [ box DAVID ])
        Assert.Equal(Some token, Cookies.verifySigned test.App.Secrets "session_token" raw (DateTimeOffset.UtcNow))
        // The date: the cookie's `expires` attribute and the signed `exp` are both 20 calendar years from now.
        let expires =
            lower.Split(';') |> Array.map (fun a -> a.Trim()) |> Array.find (fun a -> a.StartsWith "expires=")
        let expires = DateTimeOffset.Parse(expires.Substring "expires=".Length, Globalization.CultureInfo.InvariantCulture)
        Assert.InRange(expires, before.AddYears(20).AddSeconds(-2.0), after.AddYears(20).AddSeconds(2.0))
        // A tampered copy does not verify.
        Assert.Equal(None, Cookies.verifySigned test.App.Secrets "session_token" (raw + "0") (DateTimeOffset.UtcNow))
    }

[<Fact>]
let ``A3 the cookie is secure under assume_ssl`` () =
    task {
        use! test = bootSeededConfigured ignore [ "DISABLE_SSL", " " ] "default"
        let b = browser test "198.51.100.31"
        let! _ = b.Get "/session/new"
        let! signedIn = b.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(302, signedIn.Status)
        let header = signedIn.SetCookies |> List.find (fun c -> c.StartsWith "session_token=")
        Assert.Contains("secure", header.ToLowerInvariant())
    }

[<Fact>]
let ``A4 padding is percent-encoded on the wire both ways`` () =
    task {
        use! test = bootSeeded "default"
        // Out: a token whose signed value ends in `=` padding (a 19-character one, like the forged vector's) goes out
        // as `%3D`, byte for byte as Rack escapes it.
        let run (http: Microsoft.AspNetCore.Http.HttpContext) : Task =
            task {
                let claims = [ System.Security.Claims.Claim(Schemes.TokenClaim, "not-a-session-token") ]
                let principal = System.Security.Claims.ClaimsPrincipal(System.Security.Claims.ClaimsIdentity(claims, Schemes.Session))
                let now = DateTimeOffset.UtcNow
                let properties =
                    AuthenticationProperties(IsPersistent = true, IssuedUtc = Nullable now, ExpiresUtc = Nullable(Cookies.permanentExpiresAt now))
                do! http.SignInAsync(Schemes.Session, principal, properties)
                http.Response.StatusCode <- 204
            }
        let pipeline (app: Microsoft.AspNetCore.Builder.IApplicationBuilder) =
            Microsoft.AspNetCore.Builder.RunExtensions.Run(app, Microsoft.AspNetCore.Http.RequestDelegate run)
        use! host = startPipelineWith test.Booted.ConfigureServices pipeline
        let! reply = host.Send(get "/")
        let header = reply.SetCookies |> List.find (fun c -> c.StartsWith "session_token=")
        let wire = cookieValue header
        Assert.True(wire.Contains "%3D%3D--", header)
        Assert.DoesNotContain("=", wire)
        Assert.Equal(Cookies.escape (Uri.UnescapeDataString wire), wire)
        Assert.Equal(Some "not-a-session-token", Cookies.verifySigned test.App.Secrets "session_token" (Uri.UnescapeDataString wire) DateTimeOffset.UtcNow)
        // In: the forged vector's cookie arrives with `%3D%3D`; its signature is valid, and it is the lookup that refuses it.
        let _, _, forged = sessionVectors ()
        Assert.Contains("%3D%3D", forged)
        let! refused = test.Send(getWithCookie "/account/edit" forged)
        Assert.Equal(302, refused.Status)
        // And the Rails-issued cookie of David, with `%3D`s or without, is read.
        let! david = test.Send(getWithCookie "/account/edit" (davidCookie ()))
        Assert.Equal(200, david.Status)
        // Our own cookie, as a browser sends it back, signs in.
        let b = browser test "198.51.100.32"
        let! _ = b.Get "/session/new"
        let! signedIn = b.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(302, signedIn.Status)
        let! page = b.Get "/account/edit"
        Assert.Equal(200, page.Status)
    }

[<Fact>]
let ``A5 an hour old activity is refreshed and the cookie re-issued, a recent one is left alone`` () =
    task {
        use! test = bootSeededFrozen frozenNow "default"
        let cookie = davidCookie ()
        do! ageSessionFrom frozenNow.UtcDateTime test (TimeSpan.FromMinutes 30.0)
        let before = sessionRow test davidSessionId
        let! recent = test.Send(getWithCookie "/account/edit" cookie)
        Assert.Equal(200, recent.Status)
        Assert.True(recent.Header("set-cookie").IsNone, $"{recent.Headers}")
        Assert.Equal(before, sessionRow test davidSessionId)

        do! ageSessionFrom frozenNow.UtcDateTime test (TimeSpan.FromHours 2.0)
        let started = frozenNow.UtcDateTime
        let! stale =
            test.Send((getWithCookie "/account/edit" cookie).With("x-forwarded-for", "203.0.113.50").With("user-agent", "Test/1.0"))
        Assert.Equal(200, stale.Status)
        let row = sessionRow test davidSessionId
        Assert.Equal("203.0.113.50", row.Ip)
        Assert.Equal("Test/1.0", row.UserAgent)
        Assert.True(row.LastActive >= started, $"{row.LastActive}")
        Assert.True(row.UpdatedAt >= started, $"{row.UpdatedAt}")
        let header = stale.SetCookies |> List.find (fun c -> c.StartsWith "session_token=")
        let raw = Uri.UnescapeDataString(cookieValue header)
        Assert.Equal(Some "AxJs94fteQ5Autv2VrKsH68c", Cookies.verifySigned test.App.Secrets "session_token" raw frozenNow)
        // The framework's no-cache headers for sign in are not Rails' answer to a page.
        Assert.Equal(Some "max-age=0, private, must-revalidate", stale.Header "cache-control")
        Assert.True(stale.Header("pragma").IsNone && stale.Header("expires").IsNone, $"{stale.Headers}")
    }

[<Fact>]
let ``A6 sign out removes the row and the cookie and leaves the user's other session`` () =
    task {
        use! test = bootSeeded "default"
        let other = david test
        let b = browser test "198.51.100.33"
        let! _ = b.Get "/session/new"
        let! signedIn = b.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(302, signedIn.Status)
        Assert.Equal(2L, sessionCount test DAVID)
        let! out = b.Form("delete", "/session", [])
        Assert.Equal(302, out.Status)
        let deleted = out.SetCookies |> List.find (fun c -> c.StartsWith "session_token=")
        Assert.StartsWith("session_token=;", deleted)
        Assert.Contains("1970", deleted)
        // Rails' `cookies.delete`: `max-age=0`, no `httponly` (the replay compares these attributes).
        Assert.Contains("max-age=0", deleted.ToLowerInvariant())
        Assert.DoesNotContain("httponly", deleted.ToLowerInvariant())
        Assert.Equal(1L, sessionCount test DAVID)
        let! after = b.Get "/account/edit"
        Assert.Equal(302, after.Status)
        let! still = other.Get "/account/edit"
        Assert.Equal(200, still.Status)
    }

[<Fact>]
let ``A7 requests that do not authenticate never read the cookie`` () =
    task {
        use! test = bootSeeded "default"
        let calls = ref 0
        let services (services: IServiceCollection) =
            test.Booted.ConfigureServices services
            services.PostConfigure<CookieAuthenticationOptions>(Schemes.Session, fun options ->
                let inner = options.TicketDataFormat
                options.TicketDataFormat <-
                    { new ISecureDataFormat<AuthenticationTicket> with
                        member _.Protect(data) = inner.Protect data
                        member _.Protect(data, purpose) = inner.Protect(data, purpose)
                        member _.Unprotect(text) = calls.Value <- calls.Value + 1; inner.Unprotect text
                        member _.Unprotect(text, purpose) = calls.Value <- calls.Value + 1; inner.Unprotect(text, purpose) })
            |> ignore
        use! host = startPipelineWith services test.Booted.Pipeline
        let cookie = davidCookie ()
        let asset = Campfire.Assets.Assets.manifest () |> Seq.head |> snd
        for path in [ "/up"; $"{Campfire.Assets.Assets.Prefix}/{asset}"; "/webmanifest.json" ] do
            let! reply = host.Send(getWithCookie path cookie)
            Assert.True((reply.Status = 200), $"{path} {reply.Status}")
        Assert.Equal(0, calls.Value)
        // The counter works: a page that authenticates reads the cookie.
        let! page = host.Send(getWithCookie "/account/edit" cookie)
        Assert.Equal(200, page.Status)
        Assert.True(calls.Value >= 1)
    }

[<Fact>]
let ``A8 a session whose user is gone is not signed in`` () =
    task {
        use! test = bootSeeded "default"
        let token = "OrphanedSessionToken1234"
        let now = dbTime DateTime.UtcNow
        // No row of `users` has this id, and the foreign key would refuse it; the database is the test's own.
        test.Sql(fun conn ->
            RawSql.run conn "PRAGMA foreign_keys = OFF" []
            RawSql.run
                conn
                "INSERT INTO sessions (user_id, token, ip_address, user_agent, last_active_at, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?)"
                [ box 999999999L; box token; box "127.0.0.1"; box "Test"; box now; box now; box now ])
        let signed = Cookies.sign test.App.Secrets "session_token" token (Some(Cookies.permanentExpiresAt DateTimeOffset.UtcNow))
        let! reply = test.Send(getWithCookie "/account/edit" $"session_token={Cookies.escape signed}")
        Assert.Equal(302, reply.Status)
        Assert.Equal(Some "http://campfire.test/session/new", reply.Location)
    }
