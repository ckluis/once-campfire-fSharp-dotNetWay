// plans/design-kit-auth.md K13, unit 2.8 (Web/PublicExceptions.fs): `ActionDispatch::PublicExceptions` for an `Error`.
//
// Rails renders a public page only for an exception (`head :forbidden` stays empty): `public/<status>.html` as it is, or for
// another format `{ status:, error: }` as that format's body. The renderer writes straight to the response, with the
// default headers Rails' middleware adds (and no `Cache-Control`: `Rack::ETag` digests only 200s), and the log line `ShowExceptions` writes. Statuses and messages
// are `Errors.status` and `Errors.display`.
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
open Campfire.App

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
        | Halt handler ->
            // A before-action that redirected with a flash (`set_room`'s alert) stores it as an answered action does.
            fun http ->
                task {
                    do! Respond.commit http
                    do! handler http
                }
                :> Task
        | other ->
            fun http ->
                task {
                    let edge = http.RequestServices.GetRequiredService<EdgeConfig>()
                    let status = Errors.status other
                    let logger = (http.RequestServices.GetRequiredService<AppState>()).Loggers.CreateLogger "campfire"
                    let level = if status >= 500 then LogLevel.Error else LogLevel.Information
                    if logger.IsEnabled level then
                        // J11: a bot API path carries the bot's key, so the line logs its keyed hash.
                        let redactor =
                            match http.RequestServices.GetService<Microsoft.Extensions.Compliance.Redaction.IRedactorProvider>() with
                            | null -> null
                            | provider -> Logging.redactor provider
                        let path = Logging.redactBotPath redactor (http.Request.Path.ToString())
                        if status >= 500 then
                            logger.LogError("request failed error={Error} path=\"{Path}\"", Errors.display other, path)
                        else
                            logger.LogInformation("request rejected error={Error} path=\"{Path}\"", Errors.display other, path)
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
                                match edge.ErrorPages.Get status with
                                | ValueSome html -> html
                                | ValueNone -> ReadOnlyMemory<byte>.Empty
                    do! Respond.commit http
                    let response = http.Response
                    response.StatusCode <- status
                    response.Headers.ContentType <- $"{contentType}; charset=UTF-8"
                    response.Headers.ContentLength <- Nullable(int64 body.Length)
                    for (name, value) in edge.DefaultHeaders do
                        if not (response.Headers.ContainsKey name) then response.Headers.Append(name, value)
                    // A page rendered by a middleware outside the controller carries none of the controller's headers.
                    response.Headers.Remove "x-version" |> ignore
                    response.Headers.Remove "x-rev" |> ignore
                    if not body.IsEmpty then do! response.Body.WriteAsync body
                }
                :> Task

    /// `UseExceptionHandler`'s handler (K13): what escaped the pipeline, rendered like any action's failure. A request the
    /// host refused (`BadHttpRequestException`: 413 for a body over the limit, 408, 400) keeps its status; a form the
    /// framework could not read or finish is a 400; anything else is Rails' 500 with `public/500.html`.
    let exceptionHandler: RequestDelegate =
        RequestDelegate(fun http ->
            let error =
                match http.Features.Get<IExceptionHandlerFeature>() with
                | null -> Internal(exn "exception handler without an exception")
                | feature ->
                    match feature.Error with
                    | :? BadHttpRequestException as e -> Status e.StatusCode
                    | :? System.IO.InvalidDataException -> Status 400
                    // The method override reads every form POST before routing; a body it could not finish (a multipart body
                    // cut short) is Rack's `EmptyContentError`, a 400, not a 500.
                    | :? System.IO.IOException when http.Request.HasFormContentType && not http.RequestAborted.IsCancellationRequested -> Status 400
                    | e -> Internal e
            respond error http)
