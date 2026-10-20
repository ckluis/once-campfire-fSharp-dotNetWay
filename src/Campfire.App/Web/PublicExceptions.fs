// plans/design-kit-auth.md K13, unit 2.8 (Web/PublicExceptions.fs): `ActionDispatch::PublicExceptions` for an `Error`.
//
// Rails renders a public page only for an exception (`head :forbidden` stays empty): `public/<status>.html` as it is, or for
// another format `{ status:, error: }` as that format's body. The renderer writes straight to the response, with the
// default headers and `no-cache` Rails' middleware add, and the log line `ShowExceptions` writes. Statuses and messages
// are `Campfire.Kit.Error`'s while the kit's `Ctx` lives (unit 2.13).
namespace Campfire.App.Web

open System
open System.Text
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Diagnostics
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Net.Http.Headers

module PublicExceptions =
    /// `Rack::Utils::HTTP_STATUS_CODES`.
    let private reason (status: int) : string =
        match status with
        | 422 -> "Unprocessable Content"
        | 413 -> "Content Too Large"
        | _ ->
            match Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase status with
            | "" -> "Internal Server Error"
            | reason -> reason

    /// Builder's text escaping (`Hash#to_xml`).
    let private xmlEscape (text: string) : string = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")

    /// `body.public_send("to_#{format}")` for the formats a `{ status:, error: }` hash responds to.
    let private bodyFor (format: Format option) (status: int) : string voption =
        let error = reason status
        match format with
        | Some Json -> ValueSome $"""{{"status":{status},"error":"{error}"}}"""
        | Some Xml ->
            ValueSome $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<hash>\n  <status type=\"integer\">{status}</status>\n  <error>{xmlEscape error}</error>\n</hash>\n"
        | Some(Other "yaml") -> ValueSome $"---\n:status: {status}\n:error: {error}\n"
        | _ -> ValueNone

    /// An error as the public exception page: the status, the page or format body, and the headers every response has.
    let respond (error: Error) : HttpHandler =
        match error with
        | Halt handler -> handler
        | other ->
            fun http ->
                task {
                    let kit = http.RequestServices.GetRequiredService<Campfire.Kit.Kit>()
                    let kitError = Errors.toKit other
                    let status = Campfire.Kit.Error.status kitError
                    let logger = kit.Logger
                    let level = if status >= 500 then LogLevel.Error else LogLevel.Information
                    if logger.IsEnabled level then
                        let path = http.Request.Path.ToString()
                        if status >= 500 then
                            logger.LogError("request failed error={Error} path=\"{Path}\"", Campfire.Kit.Error.display kitError, path)
                        else
                            logger.LogInformation("request rejected error={Error} path=\"{Path}\"", Campfire.Kit.Error.display kitError, path)
                    // `request.formats.first`, `*/*` included (only `formats` failing falls back to HTML).
                    let format = match Formats.requested http with Ok(first :: _) -> Some first | _ -> None
                    let contentType = match format with Some f -> Formats.mimeType f | None -> "text/html"
                    let head = HttpMethods.IsHead http.Request.Method
                    let body =
                        if head then
                            ReadOnlyMemory<byte>.Empty
                        else
                            match bodyFor format status with
                            | ValueSome text -> ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes text)
                            | ValueNone ->
                                // PublicExceptions answers X-Cascade: pass; ShowExceptions turns that into an empty page.
                                match kit.ErrorPages.Get status with
                                | ValueSome html -> html
                                | ValueNone -> ReadOnlyMemory<byte>.Empty
                    do! Respond.commit http
                    let response = http.Response
                    response.StatusCode <- status
                    response.Headers.ContentType <- $"{contentType}; charset=UTF-8"
                    response.Headers.ContentLength <- Nullable(int64 body.Length)
                    for (name, value) in kit.Config.DefaultHeaders do
                        if not (response.Headers.ContainsKey name) then response.Headers.Append(name, value)
                    if not (response.Headers.ContainsKey HeaderNames.CacheControl) then response.Headers.CacheControl <- "no-cache"
                    if not body.IsEmpty then do! response.Body.WriteAsync body
                }
                :> Task

    /// `UseExceptionHandler`'s handler (K13): what escaped the pipeline, rendered like any action's failure. A request the
    /// host refused (`BadHttpRequestException`: 413 for a body over the limit, 408, 400) keeps its status; a form the
    /// framework could not read is a 400; anything else is Rails' 500 with `public/500.html`.
    let exceptionHandler: RequestDelegate =
        RequestDelegate(fun http ->
            let error =
                match http.Features.Get<IExceptionHandlerFeature>() with
                | null -> Internal(exn "exception handler without an exception")
                | feature ->
                    match feature.Error with
                    | :? BadHttpRequestException as e -> Status e.StatusCode
                    | :? System.IO.InvalidDataException -> Status 400
                    | e -> Internal e
            let kit = http.RequestServices.GetRequiredService<Campfire.Kit.Kit>()
            Campfire.Kit.Adapter.abandonBody kit http
            respond error http)
