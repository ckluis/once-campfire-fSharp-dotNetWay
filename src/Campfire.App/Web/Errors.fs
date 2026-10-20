// plans/design-kit-auth.md section 3 (Web/Errors.fs): the errors a Web action can return.
//
// The cases are Rails' exception kinds (`ActionDispatch::ExceptionWrapper.rescue_responses`), plus `Halt` for a
// before-action that rendered or redirected. Until unit 2.13 the legacy `Campfire.Kit.Error` rides along as
// `Legacy`. `PublicExceptions` (unit 2.8) renders them.
namespace Campfire.App.Web

open Falco
open Microsoft.AspNetCore.Http
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
