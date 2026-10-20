// plans/design-kit-auth.md section 3 (Web/Errors.fs): the errors a Web action can return.
//
// The cases are Rails' exception kinds (`ActionDispatch::ExceptionWrapper.rescue_responses`), plus `Halt` for a
// before-action that rendered or redirected. Until unit 2.13 the legacy `Campfire.Kit.Error` rides along as
// `Legacy`, and `respond` renders every error through the kit's existing `Ctx.Finish` (the public pages, the
// log line, the default headers), so a Web action's failure is byte for byte a legacy action's.
namespace Campfire.App.Web

open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Campfire.Kit

type Error =
    | NotFound
    | BadRequest of string
    | ParameterMissing of string
    | UnknownFormat
    | InvalidAuthenticityToken of string
    | InvalidCrossOriginRequest
    | UnsafeRedirect of string
    | Status of int
    | Internal of exn
    /// A before-action rendered or redirected: stop and send this.
    | Halt of HttpHandler
    /// A kit error (until unit 2.13 deletes the kit's `Ctx`).
    | Legacy of Campfire.Kit.Error

module Errors =
    /// The kit error an error stands for (`Halt` has none).
    let toKit (error: Error) : Campfire.Kit.Error =
        match error with
        | NotFound -> Campfire.Kit.Error.NotFound
        | BadRequest message -> Campfire.Kit.Error.BadRequest message
        | ParameterMissing name -> Campfire.Kit.Error.ParameterMissing name
        | UnknownFormat -> Campfire.Kit.Error.UnknownFormat
        | InvalidAuthenticityToken message -> Campfire.Kit.Error.InvalidAuthenticityToken message
        | InvalidCrossOriginRequest -> Campfire.Kit.Error.InvalidCrossOriginRequest
        | UnsafeRedirect message -> Campfire.Kit.Error.UnsafeRedirect message
        | Status status -> Campfire.Kit.Error.Status status
        | Internal e -> Campfire.Kit.Error.Internal e
        | Legacy error -> error
        | Halt _ -> Campfire.Kit.Error.Internal(exn "Halt has no kit error")

    /// `ActionDispatch::PublicExceptions` for an error: `public/<status>.html`, or the JSON/XML body for another
    /// format. `Halt` is the response itself.
    let respond (error: Error) : HttpHandler =
        match error with
        | Halt handler -> handler
        | other ->
            fun http ->
                let kit = http.RequestServices.GetRequiredService<Kit>()
                let failure: Result<Response, Campfire.Kit.Error> = Result.Error(toKit other)
                Adapter.dispatch kit (fun _ -> Task.FromResult failure) http
