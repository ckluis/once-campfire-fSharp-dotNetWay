// plans/design-kit-auth.md section 4, unit 2.1 (the seam): the services later units use resolve from a request on
// net11.0, `Action.run` and `Current` behave, and the route table's rows are wrapped.
module Campfire.App.Tests.Web.SeamTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Antiforgery
open Microsoft.AspNetCore.Session
open Microsoft.Extensions.DependencyInjection
open Xunit
open Campfire.App
open Campfire.App.Tests.Support
open Campfire.App.Web

let private run (handler: HttpContext -> Task) : IApplicationBuilder -> unit =
    fun app -> Microsoft.AspNetCore.Builder.RunExtensions.Run(app, RequestDelegate handler)

let private write (http: HttpContext) (text: string) : Task =
    Microsoft.AspNetCore.Http.HttpResponseWritingExtensions.WriteAsync(http.Response, text)

[<Fact>]
let ``a request resolves ICsrfProtection, IAuthenticationSchemeProvider and ISessionStore on net11`` () =
    task {
        use! test = bootSeeded "default"
        let handler (http: HttpContext) : Task =
            let services = http.RequestServices
            let csrf = services.GetService<ICsrfProtection>()
            let schemes = services.GetService<IAuthenticationSchemeProvider>()
            let sessions = services.GetService<ISessionStore>()
            write http $"{Environment.Version.Major} {not (isNull (box csrf))} {not (isNull (box schemes))} {not (isNull (box sessions))}"
        use! host = startPipelineWith test.Booted.ConfigureServices (run handler)
        let! reply = host.Send(get "/")
        Assert.Equal("11 True True True", reply.Text)
    }

[<Fact>]
let ``no authentication scheme is the default, so nothing authenticates unasked`` () =
    task {
        use! test = bootSeeded "default"
        let handler (http: HttpContext) : Task =
            task {
                let schemes = http.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>()
                let! authenticate = schemes.GetDefaultAuthenticateSchemeAsync()
                let! challenge = schemes.GetDefaultChallengeSchemeAsync()
                do! write http $"{isNull (box authenticate)} {isNull (box challenge)}"
            }
        use! host = startPipelineWith test.Booted.ConfigureServices (run handler)
        let! reply = host.Send(get "/")
        Assert.Equal("True True", reply.Text)
    }

let private actionHost (test: Test) (action: Action) =
    startPipelineWith test.Booted.ConfigureServices (run (Action.run action))

[<Fact>]
let ``Action.run sends the handler an action returns, a halt as it is, and an error as the kit renders it`` () =
    task {
        use! test = bootSeeded "default"
        let action: Action =
            fun http ->
                let target = http.Request.Path.Value
                match target with
                | "/ok" -> Task.FromResult(Ok(fun http -> write http "fine"))
                | "/halt" -> Task.FromResult(Action.halt (fun http -> http.Response.StatusCode <- 418; write http "halted"))
                | "/missing" -> Task.FromResult(Result.Error Campfire.App.Web.Error.NotFound)
                | "/status" -> Task.FromResult(Result.Error(Campfire.App.Web.Error.Status 503))
                | "/unprocessable" -> Task.FromResult(Result.Error(Campfire.App.Web.Error.InvalidAuthenticityToken "no"))
                | _ -> failwith "boom"
        use! host = actionHost test action
        let! ok = host.Send(get "/ok")
        Assert.Equal((200, "fine"), (ok.Status, ok.Text))
        let! halted = host.Send(get "/halt")
        Assert.Equal((418, "halted"), (halted.Status, halted.Text))
        let! missing = host.Send(get "/missing")
        Assert.Equal(404, missing.Status)
        Assert.Contains("<!DOCTYPE html>", missing.Text)
        let! status = host.Send(get "/status")
        Assert.Equal(503, status.Status)
        let! unprocessable = host.Send(get "/unprocessable")
        Assert.Equal(422, unprocessable.Status)
        let! boom = host.Send(get "/boom")
        Assert.Equal(500, boom.Status)
    }

[<Fact>]
let ``Current is one object per request`` () =
    task {
        use! test = bootSeeded "default"
        let seen = Collections.Concurrent.ConcurrentBag<Current>()
        let handler (http: HttpContext) : Task =
            let first = Current.get http
            let again = Current.get http
            seen.Add first
            write http $"{obj.ReferenceEquals(first, again)} {Current.signedIn http} {first.AuthenticatedBy}"
        use! host = startPipelineWith test.Booted.ConfigureServices (run handler)
        let! one = host.Send(get "/")
        let! two = host.Send(get "/")
        Assert.Equal("True False Anonymous", one.Text)
        Assert.Equal("True False Anonymous", two.Text)
        Assert.Equal(2, (seen |> Seq.distinct |> Seq.length))
    }

[<Fact>]
let ``every row of the route table is a wrapped Web action and the table dispatches through them`` () =
    task {
        use! test = bootSeeded "default"
        for row in RouteTable.routes.Value do
            Assert.False(isNull (box row.Action), row.Endpoint)
        let! up = test.Send(get "/up")
        Assert.Equal(200, up.Status)
        let! missing = test.Send(get "/no/such/page")
        Assert.Equal(404, missing.Status)
        let! badPath = test.Send(get "/users/%ff/avatar")
        Assert.Equal(400, badPath.Status)
    }
