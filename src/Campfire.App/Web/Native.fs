// plans/design-kit-auth.md section 4, unit 2.9 (Web/Native.fs): what every response of a Web action has.
//
// `Ctx.Finish` gave the legacy actions `action_dispatch.default_headers`, a default `Content-Type` and
// `Cache-Control`, and `Rack::ETag`'s digest. A Web action's response gets the first three as the response starts
// (`OnStarting`, only where the action did not set them), and no digest (K14: the same DOM, pixels and data; hashing every
// body is cost for no visible effect). An error does not pass through here: `PublicExceptions` is Rails' middleware, which
// writes its own headers. The default `Cache-Control` is Rails' for a page with a body (`max-age=0, private,
// must-revalidate`, which `Rack::ETag` pairs with the digest it adds) and `no-cache` for anything without one.
namespace Campfire.App.Web

open System
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Net.Http.Headers

module Native =
    let private headersOnStarting =
        Func<obj, Task>(fun state ->
            match state with
            | :? HttpContext as http ->
                let response = http.Response
                let headers = response.Headers
                let kit = http.RequestServices.GetRequiredService<Campfire.Kit.Kit>()
                for (name, value) in kit.Config.DefaultHeaders do
                    if not (headers.ContainsKey name) then headers.Append(name, value)
                let status = response.StatusCode
                if not (headers.ContainsKey HeaderNames.ContentType) && not (Respond.noContent status) then
                    headers.ContentType <- "text/html; charset=utf-8"
                if not (headers.ContainsKey HeaderNames.CacheControl) then
                    let digestible = (status = 200 || status = 201) && response.ContentLength.GetValueOrDefault() > 0L
                    headers.CacheControl <- (if digestible then "max-age=0, private, must-revalidate" else "no-cache")
            | _ -> ()
            Task.CompletedTask)

    /// The handler, preceded by the registration of the headers every response has.
    let private finishing (handler: HttpHandler) : HttpHandler =
        fun http ->
            http.Response.OnStarting(headersOnStarting, (http :> obj))
            handler http

    /// A Web action as a route row has it: what it answers (or halts with) is finished with the default headers.
    let wrap (action: Campfire.App.Web.Action) : Campfire.App.Web.Action =
        fun http ->
            task {
                let! result = action http
                match result with
                | Ok handler -> return Ok(finishing handler)
                | Result.Error(Halt handler) -> return Result.Error(Halt(finishing handler))
                | other -> return other
            }
