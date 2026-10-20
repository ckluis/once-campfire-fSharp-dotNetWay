// plans/design-kit-auth.md section 4, unit 2.12 (Controllers D, the hot paths): rooms and `rooms/*`, messages, boosts and the bot API as
// Web actions, over the whole app on the `default` parity seed. D1-D5 check what the move to `HttpContext` could change: the raw
// request body of a bot (a form post is read by the method override first), `set_room`'s alert, the `last_room` cookie, the refresh
// and involvement params, and the pages with no body hash (K14).
module Campfire.App.Tests.Web.ControllersDTests

open System
open Xunit
open Campfire.App.Tests.Support

[<Fact>]
let ``D1 a bot's raw body survives the form reader and the method override`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        let b = $"/rooms/{ALL_TALK}/{BENDER_KEY}/messages"
        // `curl -d 'Beep form'` posts a form: the raw body is still the message.
        let! created = bot.Send((request "POST" b).Form "Beep form")
        Assert.True((created.Status = 201), created.Text)
        let! index = bot.Get b
        Assert.Contains("Beep form", index.Text)
        // A JSON content type keeps its raw body too.
        let! json = bot.Send(((request "POST" b).With("content-type", "application/json")).WithBody "{\"as\":\"text\"}")
        Assert.Equal(201, json.Status)
        let! index = bot.Get b
        Assert.Contains("as", index.Text)
        // A blank form body is unprocessable, a body-less post too.
        let! blank = bot.Send((request "POST" b).Form "  ")
        Assert.Equal(422, blank.Status)
        let! none = bot.Send(request "POST" b)
        Assert.Equal(422, none.Status)
    }

[<Fact>]
let ``D2 set_room sends a missing room back to the root with an alert`` () =
    task {
        use! test = bootSeeded "default"
        let david = david test
        let! missing = david.Get "/rooms/999999"
        Assert.Equal(302, missing.Status)
        Assert.Equal(Some "http://campfire.test/", missing.Location)
        // The alert was stored with the redirect (a halting before-action commits the flash too).
        let! root = david.Get "/"
        Assert.Equal(302, root.Status)
        let! landing = david.Get(Uri(root.Location.Value).PathAndQuery)
        Assert.Contains("Room not found or inaccessible", landing.Text)
        // A direct room is not an open room's: `rooms/opens/:id` redirects, scoped to open and closed rooms.
        let! direct = david.Get $"/rooms/opens/{DIRECT_KEVIN_BENDER}"
        Assert.Equal(302, direct.Status)
        Assert.Equal(Some "http://campfire.test/", direct.Location)
    }

[<Fact>]
let ``D3 the last_room cookie is set on a room page and only when it changes`` () =
    task {
        use! test = bootSeeded "default"
        let david = david test
        let! first = david.Get $"/rooms/{ALL_TALK}"
        Assert.Equal(200, first.Status)
        let lastRoom = first.HeaderValues "set-cookie" |> List.filter (fun c -> c.StartsWith "last_room=")
        Assert.Equal(1, lastRoom.Length)
        Assert.Contains("path=/", lastRoom.Head)
        let! again = david.Send((get $"/rooms/{ALL_TALK}").With("cookie", $"last_room={ALL_TALK}"))
        Assert.Equal(200, again.Status)
        Assert.Empty(again.HeaderValues "set-cookie" |> List.filter (fun c -> c.StartsWith "last_room="))
        // K14: no body hash on a page.
        Assert.Equal(None, first.Header "etag")
    }

[<Fact>]
let ``D4 refresh reads since as an integer and rejects a hash`` () =
    task {
        use! test = bootSeeded "default"
        let david = david test
        let stream (path: string) = (get path).With("accept", "text/vnd.turbo-stream.html")
        let! ok = david.Send(stream $"/rooms/{ALL_TALK}/refresh?since=0")
        Assert.Equal(200, ok.Status)
        Assert.Equal(Some "text/vnd.turbo-stream.html; charset=utf-8", ok.ContentType)
        let! junk = david.Send(stream $"/rooms/{ALL_TALK}/refresh?since=abc")
        Assert.Equal(200, junk.Status)
        let! hash = david.Send(stream $"/rooms/{ALL_TALK}/refresh?since[a]=1")
        Assert.Equal(500, hash.Status)
        let! html = david.Get $"/rooms/{ALL_TALK}/refresh"
        Assert.Equal(406, html.Status)
    }

[<Fact>]
let ``D5 involvement: blank clears it, a name sets it, anything else is a 500`` () =
    task {
        use! test = bootSeeded "default"
        let david = david test
        let path = $"/rooms/{ALL_TALK}/involvement"
        let! page = david.Get path
        Assert.Equal(200, page.Status)
        let! set = david.Write((request "PATCH" path).Form "involvement=everything")
        Assert.Equal(302, set.Status)
        let! cleared = david.Write((request "PATCH" path).Form "involvement=")
        Assert.Equal(302, cleared.Status)
        let! bad = david.Write((request "PATCH" path).Form "involvement=nope")
        Assert.Equal(500, bad.Status)
        let! hash = david.Write((request "PATCH" path).Form "involvement[a]=1")
        Assert.Equal(500, hash.Status)
    }
