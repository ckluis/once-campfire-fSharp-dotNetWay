// plans/design-kit-auth.md section 5, unit 2.5 (session and flash): F1-F7. The flash and the return-to URL live in ASP.NET
// Core's session (cookie `_campfire_flash`, K6), `last_room` goes out through `Response.Cookies`. The whole app over a
// parity seed.
module Campfire.App.Tests.Web.FlashTests

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Caching.Distributed
open Microsoft.Extensions.Logging
open Xunit
open Campfire.App.Tests.Support

let private flashCookie (reply: Reply) : string option =
    reply.SetCookies |> List.tryFind (fun c -> c.StartsWith "_campfire_flash=")

/// `PATCH /account` redirects to the account page with the "✓" notice.
let private rename (admin: Browser) (accountId: string) : Task<Reply> =
    admin.Form("patch", $"/account.{accountId}", [ "account[name]", "Renamed" ])

[<Fact>]
let ``F1 a notice renders once`` () =
    task {
        use! test = bootSeeded "default"
        let admin = browser test "198.51.100.40"
        do! admin.SignIn(test.Label "emails.david")
        let accountId = test.Label "accounts.signal"
        let! updated = rename admin accountId
        Assert.Equal(302, updated.Status)
        Assert.True((flashCookie updated).IsSome, $"{updated.SetCookies}")
        let! shown = admin.Get "/account/edit"
        Assert.Equal(200, shown.Status)
        Assert.Contains("class=\"flash\" data-controller", shown.Text)
        let! gone = admin.Get "/account/edit"
        Assert.Equal(200, gone.Status)
        Assert.DoesNotContain("class=\"flash\" data-controller", gone.Text)
        Assert.True((shown.Text.Contains "class=\"flash\" data-controller"), "the notice element is the marker the second page lacks")
    }

[<Fact>]
let ``F2 a failed sign in shows the alert on that page only`` () =
    task {
        use! test = bootSeeded "default"
        let b = browser test "198.51.100.41"
        let! _ = b.Get "/session/new"
        let! failed = b.Form("post", "/session", [ "email_address", "david@37signals.com"; "password", "wrong" ])
        Assert.Equal(401, failed.Status)
        Assert.Contains("Too many requests or unauthorized.", failed.Text)
        // flash.now is never stored.
        Assert.True((flashCookie failed).IsNone, $"{failed.SetCookies}")
        let! next = b.Get "/session/new"
        Assert.Equal(200, next.Status)
        Assert.DoesNotContain("Too many requests or unauthorized.", next.Text)
    }

[<Fact>]
let ``F3 a response that never loads the flash does not consume it`` () =
    task {
        use! test = bootSeeded "default"
        let admin = browser test "198.51.100.42"
        do! admin.SignIn(test.Label "emails.david")
        let! updated = rename admin (test.Label "accounts.signal")
        Assert.Equal(302, updated.Status)
        // A turbo-stream write in between: Rails sweeps only a flash that was loaded.
        let! posted =
            admin.Write(
                (request "POST" $"/rooms/{ALL_TALK}/messages")
                    .With("accept", "text/vnd.turbo-stream.html, text/html, application/xhtml+xml")
                    .Form(formBody [ "message[body]", "between"; "message[client_message_id]", "f3" ])
            )
        Assert.True((posted.Status < 400), $"{posted.Status}")
        Assert.True((flashCookie posted).IsNone, $"{posted.SetCookies}")
        let! shown = admin.Get "/account/edit"
        Assert.Contains("class=\"flash\" data-controller", shown.Text)
    }

[<Fact>]
let ``F4 return_to sends the signed out visitor back to the absolute URL`` () =
    task {
        use! test = bootSeeded "default"
        let b = browser test "198.51.100.43"
        let! bounced = b.Get $"/rooms/{ALL_TALK}"
        Assert.Equal(302, bounced.Status)
        Assert.Equal(Some "http://campfire.test/session/new", bounced.Location)
        Assert.True((flashCookie bounced).IsSome, "the return-to URL is in the session")
        let! _ = b.Get "/session/new"
        let! signedIn = b.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(302, signedIn.Status)
        Assert.Equal(Some $"http://campfire.test/rooms/{ALL_TALK}", signedIn.Location)
        // Taken once: a second sign in lands on the root.
        let! out = b.Write(request "DELETE" "/session")
        Assert.Equal(302, out.Status)
        let! again = b.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(Some "http://campfire.test/", again.Location)
    }

[<Fact>]
let ``F5 rails own session cookie is neither read nor rewritten and logs no warning`` () =
    task {
        use! test = bootSeeded "default"
        let logs = new CapturingLoggers()
        let services (services: IServiceCollection) =
            test.Booted.ConfigureServices services
            services.AddSingleton<ILoggerFactory>(logs :> ILoggerFactory) |> ignore
        use! host = startPipelineWith services test.Booted.Pipeline
        let rails = "_campfire_session=" + String('x', 120)
        let! reply = host.Send(getWithCookie "/session/new" (rails + "; " + davidCookie ()))
        Assert.True((reply.Status = 200 || reply.Status = 302), $"{reply.Status}")
        Assert.DoesNotContain("_campfire_session", String.Join(" ", reply.SetCookies))
        Assert.True((flashCookie reply).IsNone, $"{reply.SetCookies}")
        let text = logs.Text
        Assert.DoesNotContain("unprotect", text.ToLowerInvariant())
        Assert.DoesNotContain("Warning", text)
    }

[<Fact>]
let ``F6 last_room is set as Rails does and only when it changes`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let before = DateTimeOffset.UtcNow
        let! first = b.Get $"/rooms/{ALL_TALK}"
        let after = DateTimeOffset.UtcNow
        let header = first.SetCookies |> List.find (fun c -> c.StartsWith $"last_room={ALL_TALK}")
        let lower = header.ToLowerInvariant()
        Assert.Contains("path=/", lower)
        Assert.Contains("samesite=lax", lower)
        Assert.DoesNotContain("httponly", lower)
        let expires = lower.Split(';') |> Array.map (fun a -> a.Trim()) |> Array.find (fun a -> a.StartsWith "expires=")
        let expires = DateTimeOffset.Parse(expires.Substring "expires=".Length, Globalization.CultureInfo.InvariantCulture)
        Assert.InRange(expires, before.AddYears(20).AddSeconds(-2.0), after.AddYears(20).AddSeconds(2.0))
        let! again = b.Get $"/rooms/{ALL_TALK}"
        Assert.False((again.SetCookies |> List.exists (fun c -> c.StartsWith "last_room=")), "unchanged")
    }

[<Fact>]
let ``F7 the eleventh sign in attempt within the window is 429`` () =
    task {
        use! test = bootSeeded "default"
        let b = browser test "198.51.100.44"
        let! _ = b.Get "/session/new"
        let mutable last = 0
        for attempt in 1..11 do
            let! reply = b.Form("post", "/session", [ "email_address", "david@37signals.com"; "password", "wrong" ])
            last <- reply.Status
            Assert.Equal((if attempt <= 10 then 401 else 429), reply.Status)
            Assert.Contains("Too many requests or unauthorized.", reply.Text)
        Assert.Equal(429, last)
    }

/// A page that uses neither the flash nor the return-to URL never reads or writes the session store (the session
/// middleware's own `RefreshAsync` of its fresh key is a no-op lookup and is not counted).
[<Fact>]
let ``F8 pages without a session cookie do not touch the store`` () =
    task {
        use! test = bootSeeded "default"
        let touched = ref 0
        let hit () = touched.Value <- touched.Value + 1
        let services (services: IServiceCollection) =
            test.Booted.ConfigureServices services
            services.AddSingleton<IDistributedCache>(fun _ ->
                { new IDistributedCache with
                    member _.Get(_) = hit (); null
                    member _.GetAsync(_, _) = hit (); Task.FromResult<byte[] | null>(null)
                    member _.Set(_, _, _) = hit ()
                    member _.SetAsync(_, _, _, _) = hit (); Task.CompletedTask
                    member _.Refresh(_) = ()
                    member _.RefreshAsync(_, _) = Task.CompletedTask
                    member _.Remove(_) = hit ()
                    member _.RemoveAsync(_, _) = hit (); Task.CompletedTask })
            |> ignore
        use! host = startPipelineWith services test.Booted.Pipeline
        let cookie = davidCookie ()
        for path in [ "/session/new"; "/up"; "/account/edit" ] do
            let! reply = host.Send(getWithCookie path cookie)
            Assert.True((reply.Status = 200), $"{path} {reply.Status}")
        Assert.Equal(0, touched.Value)
    }
