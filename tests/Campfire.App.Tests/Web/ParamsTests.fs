// plans/design-kit-auth.md section 5, unit 2.7 (Params and formats): P1-P4. `Web.Params` over the framework's readers
// (route values, `Request.Form`, the query string, a JSON body read once), on a bare `HttpContext`, and end to end
// through the app for the cases that cross the legacy kit (a form read once, a JSON body, a missing key).
module Campfire.App.Tests.Web.ParamsTests

open System.IO
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Xunit
open Campfire.App.Web
open Campfire.App.Tests.Support

/// A request as the framework would hand it to an action: method, query, content type and body, route values.
let private make (query: string) (contentType: string | null) (body: string) (route: (string * string) list) : HttpContext =
    let http = DefaultHttpContext()
    http.Request.Method <- "POST"
    http.Request.QueryString <- QueryString query
    if not (isNull contentType) then http.Request.ContentType <- contentType
    http.Request.Body <- new MemoryStream(Encoding.UTF8.GetBytes body)
    for k, v in route do
        http.Request.RouteValues[k] <- v
    http

let private loaded (http: HttpContext) : HttpContext =
    match (Params.load http).GetAwaiter().GetResult() with
    | Ok() -> http
    | Error e -> failwith $"{e}"

let private form = "application/x-www-form-urlencoded"

[<Fact>]
let ``P1 route values win over the body, which wins over the query`` () =
    task {
        let http = loaded (make "?id=query&only=q" form "id=body&b=1" [ "id", "route" ])
        Assert.Equal("route", Params.value http "id")
        let noRoute = loaded (make "?id=query&only=q" form "id=body&b=1" [])
        Assert.Equal("body", Params.value noRoute "id")
        Assert.Equal("q", Params.value noRoute "only")
        // A repeated key keeps its last value.
        let repeated = loaded (make "?a=1&a=2" null "" [])
        Assert.Equal("2", Params.value repeated "a")
        // The same order over JSON.
        let json = loaded (make "?id=query" "application/json" """{"id":"body"}""" [])
        Assert.Equal("body", Params.value json "id")
        // End to end: the bot key in the path wins over a wrong one in the query.
        use! test = bootSeeded "default"
        let! reply = (anonymous test).Send((request "POST" $"/rooms/{ALL_TALK}/{BENDER_KEY}/messages?bot_key=1-nope").WithBody "Route wins")
        Assert.True((reply.Status = 201), reply.Text)
    }

[<Fact>]
let ``P2 message[body] reads from a form, a query string and JSON`` () =
    task {
        let urlencoded = loaded (make "" form "message%5Bbody%5D=hello&message[x]=1" [])
        Assert.Equal("hello", Params.nested urlencoded "message" "body")
        let query = loaded (make "?message[body]=from+query" null "" [])
        Assert.Equal("from query", Params.nested query "message" "body")
        let json = loaded (make "" "application/json" """{"message":{"body":"from json","n":1}}""" [])
        Assert.Equal("from json", Params.nested json "message" "body")
        // A number is not a string: `params[:message][:n]` is no String.
        Assert.Equal(null, Params.nested json "message" "n")
        Assert.Equal(Structured, Params.lookup json "message")
        Assert.Equal([ "a"; "b" ], Params.values (loaded (make "?ids[]=a&ids[]=b" null "" [])) "ids")
        Assert.Equal([ "a"; "b" ], Params.values (loaded (make "" "application/json" """{"ids":["a",1,"b"]}""" [])) "ids")
        // End to end: the same message from a form and from JSON.
        use! test = bootSeeded "default"
        let b = david test
        let count () = test.Sql(fun conn -> RawSql.scalar<int64> conn "SELECT COUNT(*) FROM messages WHERE room_id = ?" [ box ALL_TALK ])
        let before = count ()
        let! asForm = b.Write((request "POST" $"/rooms/{ALL_TALK}/messages").With("accept", "text/vnd.turbo-stream.html").Form(formBody [ "message[body]", "form post" ]))
        Assert.True((asForm.Status < 300), asForm.Text)
        let! asJson =
            b.Write(
                (request "POST" $"/rooms/{ALL_TALK}/messages")
                    .With("accept", "text/vnd.turbo-stream.html")
                    .With("content-type", "application/json")
                    .WithBody """{"message":{"body":"json post"}}"""
            )
        Assert.True((asJson.Status < 300), asJson.Text)
        Assert.Equal(before + 2L, count ())
    }

[<Fact>]
let ``P3 wrap_parameters nests a JSON body under the controller's key`` () =
    task {
        let http = loaded (make "" "application/json" """{"endpoint":"http://e","p256dh_key":"a","other":"x"}""" [])
        Assert.Equal(null, Params.nested http "push_subscription" "endpoint")
        Params.wrap http "push_subscription" ValueNone
        Assert.Equal("http://e", Params.nested http "push_subscription" "endpoint")
        let scope = Params.require http "push_subscription" |> Result.defaultWith (fun e -> failwith $"{e}")
        Assert.Equal<Map<string, string>>(Map [ "endpoint", "http://e"; "p256dh_key", "a" ], Params.permit scope [ "endpoint"; "p256dh_key"; "auth_key" ])
        // Only the model's attributes when it has them, and not at all for a form.
        let narrow = loaded (make "" "application/json" """{"endpoint":"http://e","other":"x"}""" [])
        Params.wrap narrow "push_subscription" (ValueSome [ "endpoint" ])
        Assert.Equal(null, Params.nested narrow "push_subscription" "other")
        let notJson = loaded (make "" form "endpoint=e" [])
        Params.wrap notJson "push_subscription" ValueNone
        Assert.Equal(null, Params.nested notJson "push_subscription" "endpoint")
        // A body that already has the key is left alone.
        let own = loaded (make "" "application/json" """{"push_subscription":{"endpoint":"own"},"endpoint":"top"}""" [])
        Params.wrap own "push_subscription" ValueNone
        Assert.Equal("own", Params.nested own "push_subscription" "endpoint")
        // End to end: the app's push subscription endpoint takes the nested JSON a client sends.
        use! test = bootSeeded "default"
        let body = """{"push_subscription":{"endpoint":"http://example.com/push","p256dh_key":"a","auth_key":"b"}}"""
        let! reply = (david test).Write((request "POST" "/users/me/push_subscriptions").With("content-type", "application/json").WithBody body)
        // 422, not 400: the nested params were read and the model refused the `http` endpoint.
        Assert.Equal(422, reply.Status)
        // The same attributes flat, as `wrap_parameters` lets a client send them, are read the same way.
        let flat = """{"endpoint":"http://example.com/push","p256dh_key":"a","auth_key":"b"}"""
        let! wrapped = (david test).Write((request "POST" "/users/me/push_subscriptions").With("content-type", "application/json").WithBody flat)
        Assert.Equal(422, wrapped.Status)
    }

[<Fact>]
let ``P4 a missing or blank key is ParameterMissing, a malformed body a bad request`` () =
    task {
        let http = loaded (make "?blank=+&message=" form "" [])
        Assert.Equal(Result<Scope, Error>.Error(ParameterMissing "message") |> string, Params.require http "message" |> string)
        match Params.requireString http "blank" with
        | Error(ParameterMissing "blank") -> ()
        | other -> failwith $"{other}"
        match Params.requireString (loaded (make "?url=http://x" null "" [])) "url" with
        | Ok "http://x" -> ()
        | other -> failwith $"{other}"
        match Params.validate (loaded (make "" "application/json" "{bad" [])) with
        | Error(BadRequest _) -> ()
        | other -> failwith $"{other}"
        // End to end: a message post with no `message` is a 400 with the public page.
        use! test = bootSeeded "default"
        let! reply = (david test).Write((request "POST" $"/rooms/{ALL_TALK}/messages").With("accept", "text/vnd.turbo-stream.html").Form(formBody [ "other", "1" ]))
        Assert.Equal(400, reply.Status)
        let! malformed = (david test).Write((request "POST" $"/rooms/{ALL_TALK}/messages").With("content-type", "application/json").WithBody "{bad")
        Assert.Equal(400, malformed.Status)
    }
