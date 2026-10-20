// plans/design-kit-auth.md section 3 (Web/Errors.fs): the errors a Web action can return.
//
// The cases are Rails' exception kinds (`ActionDispatch::ExceptionWrapper.rescue_responses`), plus `Halt` for a
// before-action that rendered or redirected. `PublicExceptions` (unit 2.8) renders them; `Errors.status` is the status
// Rails answers with and `Errors.display` the text its log line shows.
namespace Campfire.App.Web

open System
open Falco
open Microsoft.AspNetCore.Http

type Error =
    | NotFound
    /// `ActionController::BadRequest`, a parameter that cannot be built, a body that cannot be parsed.
    | BadRequest of string
    /// `ActionController::ParameterMissing`.
    | ParameterMissing of string
    /// `ActionController::UnknownFormat` / `MissingExactTemplate`.
    | UnknownFormat
    /// `ActionController::InvalidAuthenticityToken` (the `:exception` forgery strategy).
    | InvalidAuthenticityToken of string
    /// `ActionController::InvalidCrossOriginRequest`.
    | InvalidCrossOriginRequest
    /// `ActionController::Redirecting::UnsafeRedirectError` and friends (a 500).
    | UnsafeRedirect of string
    /// Any other status an action wants to answer with.
    | Status of int
    /// Like `Status`, keeping the error that caused it for the log line (`ActiveRecord::RecordInvalid` is a 422).
    | WithStatus of int * exn
    | Internal of exn
    /// A before-action rendered or redirected: stop and send this.
    | Halt of HttpHandler

module Errors =
    /// `{:#}` of an `anyhow::Error`: the message and then each cause, joined with ": ".
    let private chain (error: exn) : string =
        let messages = ResizeArray<string>()
        let mutable current: exn | null = error
        while not (isNull current) do
            let e = nonNull current
            messages.Add e.Message
            current <- e.InnerException
        String.Join(": ", messages)

    let private statusText (code: int) : string =
        match Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase code with
        | "" -> string code
        | reason -> $"{code} {reason}"

    /// The error's text as the log line shows it (`{error:#}`): its message, with causes.
    let display (error: Error) : string =
        match error with
        | Halt _ -> "halted"
        | BadRequest message -> $"bad request: {message}"
        | ParameterMissing message -> $"param is missing or the value is empty or invalid: {message}"
        | InvalidAuthenticityToken message -> $"invalid authenticity token: {message}"
        | InvalidCrossOriginRequest -> "invalid cross-origin request"
        | UnknownFormat -> "unknown format"
        | NotFound -> "not found"
        | UnsafeRedirect message -> $"unsafe redirect: {message}"
        | Status code -> statusText code
        | WithStatus(code, cause) -> $"{statusText code}: {chain cause}"
        | Internal cause -> chain cause

    /// The status Rails answers an error with (`ActionDispatch::ExceptionWrapper.rescue_responses`).
    let status (error: Error) : int =
        match error with
        | Halt _ -> StatusCodes.Status500InternalServerError
        | BadRequest _
        | ParameterMissing _ -> StatusCodes.Status400BadRequest
        | InvalidAuthenticityToken _
        | InvalidCrossOriginRequest -> StatusCodes.Status422UnprocessableEntity
        | UnknownFormat -> StatusCodes.Status406NotAcceptable
        | NotFound -> StatusCodes.Status404NotFound
        | Status code
        | WithStatus(code, _) -> code
        | UnsafeRedirect _
        | Internal _ -> StatusCodes.Status500InternalServerError
