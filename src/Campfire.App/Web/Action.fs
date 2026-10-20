// plans/design-kit-auth.md section 3 (Web/Action.fs): a controller action on `HttpContext`.
namespace Campfire.App.Web

open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http

/// An action either hands back the handler that writes its response, or stops with an `Error`.
type Action = HttpContext -> Task<Result<HttpHandler, Error>>

module Action =
    /// The Falco handler for an action: `Ok h` is `h`, `Halt h` is `h`, any other error is rendered by `PublicExceptions.respond`.
    /// A throwing action is an exception like any other: Rails answers 500 with `public/500.html`.
    let run (action: Action) : HttpHandler =
        fun http ->
            task {
                let! result =
                    task {
                        try
                            // The body is read once, here, for the accessors in `Params`; a body it cannot read is a 400.
                            match! Params.load http with
                            | Result.Error error -> return Result.Error error
                            | Ok() -> return! action http
                        with e ->
                            return Result.Error(Internal e)
                    }
                match result with
                | Ok handler ->
                    do! Respond.commit http
                    do! handler http
                | Result.Error error -> do! PublicExceptions.respond error http
            }
            :> Task

    /// Stop the before-action chain with a response.
    let halt (handler: HttpHandler) : Result<'a, Error> = Result.Error(Halt handler)
