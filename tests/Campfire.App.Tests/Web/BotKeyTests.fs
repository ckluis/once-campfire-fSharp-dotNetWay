// plans/design-kit-auth.md section 5, unit 2.3 (bot keys): B1-B4, and the key that is not a string. The whole app over a
// parity seed; the bot key is the `BotKey` authentication scheme, tried after the session scheme.
module Campfire.App.Tests.Web.BotKeyTests

open Xunit
open Campfire.App.Tests.Support

let private messages = $"/rooms/{ALL_TALK}/{BENDER_KEY}/messages"

[<Fact>]
let ``B1 a valid key creates a message as the bot`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        let! created = bot.Send((request "POST" messages).WithBody "Beep boop")
        Assert.True((created.Status = 201), created.Text)
        let creator = test.Sql(fun conn -> RawSql.scalar<int64> conn "SELECT creator_id FROM messages WHERE room_id = ? ORDER BY id DESC LIMIT 1" [ box ALL_TALK ])
        Assert.Equal(BENDER, creator)
        // `authenticated_by` is the bot key: `deny_bots` refuses the same key on a page that is not the bot API.
        let! denied = bot.Get $"/rooms/{ALL_TALK}/messages?bot_key={BENDER_KEY}"
        Assert.Equal(403, denied.Status)
    }

[<Fact>]
let ``B2 a wrong or blank key is sent to sign in`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        for key in [ "1-nope"; "394959859-wrong"; "%20"; "394959859"; "-" ] do
            let! reply = bot.Get $"/rooms/{ALL_TALK}/{key}/messages"
            Assert.Equal(302, reply.Status)
            Assert.Equal(Some "http://campfire.test/session/new", reply.Location)
        let! blank = bot.Get $"/rooms/{ALL_TALK}/messages?bot_key="
        Assert.Equal(302, blank.Status)
    }

[<Fact>]
let ``B3 a valid key in the query of a non-bot page is forbidden`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        for page in [ $"/rooms/{ALL_TALK}"; "/account/edit"; "/" ] do
            let! reply = bot.Get $"{page}?bot_key={BENDER_KEY}"
            Assert.Equal(403, reply.Status)
        // A session wins over a key: David is signed in, so his page is not a bot's.
        let david = david test
        let! signedIn = david.Get $"/rooms/{ALL_TALK}?bot_key={BENDER_KEY}"
        Assert.Equal(200, signedIn.Status)
    }

[<Fact>]
let ``B4 a cross-site bot request is not a forgery`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        let cross = (request "POST" messages).With("origin", "http://evil.example").With("sec-fetch-site", "cross-site").WithBody "From afar"
        let! reply = bot.Send cross
        Assert.True((reply.Status = 201), reply.Text)
        // The same request without a bot key is a forgery (or, signed out, a redirect before the check).
        let david = david test
        let! forged = david.Send((request "POST" $"/rooms/{ALL_TALK}/messages").With("origin", "http://evil.example").With("sec-fetch-site", "cross-site").Form(formBody [ "message[body]", "x" ]))
        Assert.Equal(422, forged.Status)
    }

[<Fact>]
let ``B5 a key that is not a string is a server error, as Rails' strip is`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        let! reply = bot.Get $"/rooms/{ALL_TALK}/messages?bot_key[a]=1"
        Assert.Equal(500, reply.Status)
    }

[<Fact>]
let ``B6 a key authenticates nothing outside the before-action chain`` () =
    task {
        use! test = bootSeeded "default"
        let bot = anonymous test
        let! up = bot.Get $"/up?bot_key={BENDER_KEY}"
        Assert.Equal(200, up.Status)
        let! robots = bot.Get $"/robots.txt?bot_key={BENDER_KEY}"
        Assert.Equal(200, robots.Status)
    }
