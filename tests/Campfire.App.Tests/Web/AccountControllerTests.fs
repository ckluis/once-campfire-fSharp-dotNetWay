// plans/design-kit-auth.md section 4, unit 2.10 (Controllers B): accounts, bots, join codes, custom styles, logos, users and
// their nested controllers (avatars, bans, profiles, sidebars, push subscriptions, test notifications) as Web actions, over
// the whole app on the `default` parity seed. J1-J10 check what the move to `HttpContext` could change: the administrator
// guard, the hash and file params, the Turbo Stream list, the join flow's errors, the conditional GETs of the public images,
// and the push subscription params.
module Campfire.App.Tests.Web.AccountControllerTests

open System
open Xunit
open Microsoft.Data.Sqlite
open Campfire.App.Tests.Support

let private scalar (test: Test) (sql: string) : string =
    test.Sql(fun conn ->
        use command = new SqliteCommand(sql, conn)
        match command.ExecuteScalar() with
        | null -> "<none>"
        | value -> string value)

let private signedIn (test: Test) (ip: string) (email: string) =
    task {
        let b = browser test ip
        do! b.SignIn(test.Label email)
        return b
    }

[<Fact>]
let ``J1 the administrator-only writes answer 403 to a member, and run for an administrator`` () =
    task {
        use! test = bootSeeded "default"
        let! kevin = signedIn test "198.51.100.70" "emails.jz"
        let before = (scalar test "select join_code from accounts")
        let! refused = kevin.Form("post", "/account/join_code", [])
        Assert.Equal(403, refused.Status)
        Assert.Equal(Some "text/html", refused.ContentType)
        Assert.Equal("", refused.Text)
        let! page = kevin.Get "/account/custom_styles/edit"
        Assert.Equal(403, page.Status)
        let! ban = kevin.Form("post", $"""/users/{test.Label "users.mallory"}/ban""", [])
        Assert.Equal(403, ban.Status)
        Assert.Equal(before, (scalar test "select join_code from accounts"))

        let b = david test
        let! reset = b.Form("post", "/account/join_code", [])
        Assert.Equal(302, reset.Status)
        Assert.Equal(Some "http://campfire.test/account/edit", reset.Location)
        Assert.NotEqual<string>(before, (scalar test "select join_code from accounts"))
    }

[<Fact>]
let ``J2 the account form reads its name and its settings hash`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let! reply =
            b.Form(
                "patch",
                "/account",
                [ "account[name]", "Renamed"
                  "account[settings][restrict_room_creation_to_administrators]", "true"
                  "account[unpermitted]", "x" ]
            )
        Assert.Equal(302, reply.Status)
        Assert.Equal(Some "http://campfire.test/account/edit", reply.Location)
        Assert.Equal("Renamed", (scalar test "select name from accounts"))
        Assert.Contains("restrict_room_creation_to_administrators", (scalar test "select settings from accounts"))
        let! edit = b.Get "/account/edit"
        Assert.Equal(200, edit.Status)
        Assert.Contains("✓", edit.Text)
        // The flash is shown once.
        let! again = b.Get "/account/edit"
        Assert.DoesNotContain("flash", again.Text.Replace("flash-", "").Replace("/assets/flash.", ""), StringComparison.Ordinal)
        // A JSON body carries the same hash.
        let! json =
            b.Write(
                (request "PATCH" "/account")
                    .With("content-type", "application/json")
                    .WithBody("""{"account":{"name":"Json Name","settings":{"restrict_room_creation_to_administrators":"false"}}}""")
            )
        Assert.Equal(302, json.Status)
        Assert.Equal("Json Name", (scalar test "select name from accounts"))
    }

[<Fact>]
let ``J3 the people list's next pages are a Turbo Stream, and only that`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let! stream = b.Send((get "/account/users?page=1").With("accept", "text/vnd.turbo-stream.html"))
        Assert.Equal(200, stream.Status)
        Assert.Equal(Some "text/vnd.turbo-stream.html; charset=utf-8", stream.ContentType)
        let! html = b.Get "/account/users"
        Assert.Equal(406, html.Status)
        let! anonymous = (anonymous test).Send((get "/account/users").With("accept", "text/vnd.turbo-stream.html"))
        Assert.Equal(302, anonymous.Status)
    }

[<Fact>]
let ``J4 a role change and a removal`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let jz = test.Label "users.jz"
        let kevin = test.Label "users.kevin"
        let jason = test.Label "users.jason"
        let administrator = (scalar test $"select role from users where id = {jason}")
        let! promoted = b.Form("patch", $"/account/users/{kevin}", [ "user[role]", "administrator" ])
        Assert.Equal(302, promoted.Status)
        Assert.Equal(Some "http://campfire.test/account/edit", promoted.Location)
        Assert.Equal(administrator, (scalar test $"select role from users where id = {kevin}"))
        // Anything else is a member.
        let! demoted = b.Form("patch", $"/account/users/{kevin}", [ "user[role]", "root" ])
        Assert.Equal(302, demoted.Status)
        Assert.NotEqual<string>(administrator, (scalar test $"select role from users where id = {kevin}"))
        let! missing = b.Form("patch", $"/account/users/{kevin}", [])
        Assert.Equal(400, missing.Status)
        let! gone = b.Form("delete", $"/account/users/{jz}", [])
        Assert.Equal(302, gone.Status)
        let! unknown = b.Form("delete", "/account/users/999999", [])
        Assert.Equal(404, unknown.Status)
        let! rita = b.Form("delete", $"""/account/users/{test.Label "users.rita"}""", [])
        Assert.Equal(404, rita.Status)
    }

[<Fact>]
let ``J5 bots: list, create, change, new key and remove`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let bender = test.Label "users.bender"
        let! index = b.Get "/account/bots"
        Assert.Equal(200, index.Status)
        Assert.Contains("Bender Bot", index.Text)
        let! edit = b.Get $"/account/bots/{bender}/edit"
        Assert.Equal(200, edit.Status)
        let! created = b.Form("post", "/account/bots", [ "user[name]", "Hook"; "user[webhook_url]", "http://example.com/hook" ])
        Assert.Equal(302, created.Status)
        Assert.Equal(Some "http://campfire.test/account/bots", created.Location)
        let hook = (scalar test "select id from users where name = 'Hook'")
        Assert.Equal("http://example.com/hook", (scalar test $"select url from webhooks where user_id = {hook}"))
        let! renamed = b.Form("patch", $"/account/bots/{hook}", [ "user[name]", "Hooked"; "user[webhook_url]", "http://example.com/other" ])
        Assert.Equal(302, renamed.Status)
        Assert.Equal("Hooked", (scalar test $"select name from users where id = {hook}"))
        let token = (scalar test $"select bot_token from users where id = {hook}")
        let! reset = b.Form("patch", $"/account/bots/{hook}/key", [])
        Assert.Equal(302, reset.Status)
        Assert.Equal(Some "http://campfire.test/account/bots", reset.Location)
        Assert.NotEqual<string>(token, (scalar test $"select bot_token from users where id = {hook}"))
        let! removed = b.Form("delete", $"/account/bots/{hook}", [])
        Assert.Equal(302, removed.Status)
        let! gone = b.Get $"/account/bots/{hook}/edit"
        Assert.Equal(404, gone.Status)
        // A bot needs a name (users.name is NOT NULL): Rails answers 500.
        let! nameless = b.Form("post", "/account/bots", [ "user[webhook_url]", "http://example.com/x" ])
        Assert.Equal(500, nameless.Status)
        // A human is not a bot.
        let! human = b.Get $"""/account/bots/{test.Label "users.kevin"}/edit"""
        Assert.Equal(404, human.Status)
    }

[<Fact>]
let ``J6 joining with the account's code`` () =
    task {
        use! test = bootSeeded "default"
        let code = test.Label "join_codes.signal"
        let visitor = anonymous test
        let! form = visitor.Get $"/join/{code}"
        Assert.Equal(200, form.Status)
        Assert.Contains("user[name]", form.Text)
        let! wrong = visitor.Get "/join/nope"
        Assert.Equal(404, wrong.Status)
        Assert.Equal("", wrong.Text)
        // Someone signed in has nothing to join.
        let! signedIn = (david test).Get $"/join/{code}"
        Assert.Equal(302, signedIn.Status)
        Assert.Equal(Some "http://campfire.test/", signedIn.Location)

        let! joined =
            visitor.Form(
                "post",
                $"/join/{code}",
                [ "user[name]", "Newbie"; "user[email_address]", "newbie@example.com"; "user[password]", "secret123456" ]
            )
        Assert.Equal(302, joined.Status)
        Assert.Equal(Some "http://campfire.test/", joined.Location)
        Assert.True(joined.SetCookies |> List.exists (fun c -> c.StartsWith "session_token="), "a session starts")
        Assert.Equal("1", (scalar test "select count(*) from users where email_address = 'newbie@example.com'"))
        let! root = visitor.Get "/"
        Assert.Equal(302, root.Status)
        Assert.Matches(@"^http://campfire\.test/rooms/\d+$", root.Location.Value)

        // An email address already taken goes to sign in with it filled in.
        let again = anonymous test
        let! taken =
            again.Form(
                "post",
                $"/join/{code}",
                [ "user[name]", "Twin"; "user[email_address]", "david@37signals.com"; "user[password]", "secret123456" ]
            )
        Assert.Equal(302, taken.Status)
        Assert.Equal(Some "http://campfire.test/session/new?email_address=david%4037signals.com", taken.Location)
        let! wrongCode = again.Form("post", "/join/nope", [ "user[name]", "X" ])
        Assert.Equal(404, wrongCode.Status)
    }

[<Fact>]
let ``J7 avatars answer a signed-in user, with conditional GETs, and their owner can remove one`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let jason = test.Label "users.jason"
        let! profile = b.Get "/users/me/profile"
        Assert.Equal(200, profile.Status)
        let at = profile.Text.IndexOf "/users/eyJ"
        Assert.True(at >= 0, "the profile shows the avatar's signed path")
        let avatarPath = profile.Text.Substring(at).Split([| '?'; '"' |])[0]
        let! initials = b.Get avatarPath
        Assert.Equal(200, initials.Status)
        Assert.Equal(Some "image/svg+xml; charset=utf-8", initials.ContentType)
        Assert.Equal(Some "max-age=1800, public, stale-while-revalidate=604800", initials.Header "cache-control")
        let etag = initials.Header "etag"
        Assert.True(etag.IsSome && etag.Value.StartsWith "W/\"", "a weak ETag")
        let! fresh = b.Send((get avatarPath).With("if-none-match", etag.Value))
        Assert.Equal(304, fresh.Status)
        Assert.Equal("", fresh.Text)
        // A bad signature is a bare 404.
        let! forged = b.Get "/users/eyJfforged/avatar"
        Assert.Equal(404, forged.Status)
        // Someone else's profile page has Jason's uploaded avatar (moon.jpg), a WebP.
        let! sidebar = b.Get $"/users/{jason}"
        Assert.Equal(200, sidebar.Status)
        let at = sidebar.Text.IndexOf "/users/eyJ"
        let jasonPath = sidebar.Text.Substring(at).Split([| '?'; '"' |])[0]
        let! image = b.Get jasonPath
        Assert.Equal(200, image.Status)
        Assert.Equal(Some "image/webp", image.ContentType)
        Assert.Equal(Some "max-age=1800, public, stale-while-revalidate=604800", image.Header "cache-control")
    }

[<Fact>]
let ``J8 profile: the page, a name change, and the sidebar frame`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let davidId = test.Label "users.david"
        let! page = b.Get "/users/me/profile"
        Assert.Equal(200, page.Status)
        let! updated = b.Form("patch", "/users/me/profile", [ "user[name]", "Dave"; "user[bio]", "Hello" ])
        Assert.Equal(302, updated.Status)
        Assert.Equal(Some "http://campfire.test/users/me/profile", updated.Location)
        Assert.Equal("Dave", (scalar test $"select name from users where id = {davidId}"))
        let! shown = b.Get "/users/me/profile"
        Assert.Contains("✓", shown.Text)
        // A blank password changes nothing; a new one changes the digest.
        let digest = (scalar test $"select password_digest from users where id = {davidId}")
        let! _ = b.Form("patch", "/users/me/profile", [ "user[password]", "" ])
        Assert.Equal(digest, (scalar test $"select password_digest from users where id = {davidId}"))
        let! _ = b.Form("patch", "/users/me/profile", [ "user[password]", "a new password" ])
        Assert.NotEqual<string>(digest, (scalar test $"select password_digest from users where id = {davidId}"))
        let! sidebar = b.Send((get "/users/me/sidebar").With("turbo-frame", "user_sidebar"))
        Assert.Equal(200, sidebar.Status)
        Assert.Contains("turbo-cable-stream-source", sidebar.Text)
        let! user = b.Get $"""/users/{test.Label "users.kevin"}"""
        Assert.Equal(200, user.Status)
        let! unknown = b.Get "/users/999999"
        Assert.Equal(404, unknown.Status)
    }

[<Fact>]
let ``J9 push subscriptions: list, params, removal and test notifications`` () =
    task {
        use! test = bootSeeded "default"
        let b = david test
        let mine = test.Label "push_subscriptions.david_chrome"
        let theirs = test.Label "push_subscriptions.jason_chrome"
        let! index = b.Get "/users/me/push_subscriptions"
        Assert.Equal(200, index.Status)
        // A JSON body wrapped under the model's name, or not: an endpoint that is not HTTPS fails validation (422); no params at all is 400.
        let post (body: string) =
            b.Write((request "POST" "/users/me/push_subscriptions").With("content-type", "application/json").With("accept", "application/json").WithBody body)
        let! invalid = post """{"endpoint":"http://push.example.com/x","p256dh_key":"k","auth_key":"a"}"""
        Assert.Equal(422, invalid.Status)
        let! wrapped = post """{"push_subscription":{"endpoint":"http://push.example.com/x","p256dh_key":"k","auth_key":"a"}}"""
        Assert.Equal(422, wrapped.Status)
        let! empty = post "{}"
        Assert.Equal(400, empty.Status)
        // Someone else's subscription is not found, and not removed.
        let! notFound = b.Form("post", $"/users/me/push_subscriptions/{theirs}/test_notifications", [])
        Assert.Equal(404, notFound.Status)
        let! foreign = b.Form("delete", $"/users/me/push_subscriptions/{theirs}", [])
        Assert.Equal(302, foreign.Status)
        Assert.Equal("1", (scalar test $"select count(*) from push_subscriptions where id = {theirs}"))
        let! removed = b.Form("delete", $"/users/me/push_subscriptions/{mine}", [])
        Assert.Equal(302, removed.Status)
        Assert.Equal(Some "http://campfire.test/users/me/push_subscriptions", removed.Location)
        Assert.Equal("0", (scalar test $"select count(*) from push_subscriptions where id = {mine}"))
    }

[<Fact>]
let ``J10 the logo is a public image with a conditional GET`` () =
    task {
        use! test = bootSeeded "default"
        let visitor = anonymous test
        let! stock = visitor.Get "/account/logo"
        Assert.Equal(200, stock.Status)
        Assert.Equal(Some "image/png", stock.ContentType)
        Assert.Equal(Some "max-age=300, public, stale-while-revalidate=604800", stock.Header "cache-control")
        Assert.Equal(Some "inline; filename=\"app-icon.png\"; filename*=UTF-8''app-icon.png", stock.Header "content-disposition")
        let! small = visitor.Get "/account/logo?size=small"
        Assert.Equal(200, small.Status)
        Assert.Equal(Some "inline; filename=\"app-icon-192.png\"; filename*=UTF-8''app-icon-192.png", small.Header "content-disposition")
        let etag = stock.Header "etag"
        Assert.True(etag.IsSome, "a weak ETag from the account")
        let! fresh = visitor.Send((get "/account/logo").With("if-none-match", etag.Value))
        Assert.Equal(304, fresh.Status)
        // Removing a logo is for administrators.
        let! refused = (anonymous test).Form("delete", "/account/logo", [])
        Assert.Equal(302, refused.Status)
    }
