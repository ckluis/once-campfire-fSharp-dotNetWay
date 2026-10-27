// Port of rust/crates/campfire/src/integrations/opengraph.rs
//
// Link unfurling: `UnfurlLinksController#create` (reference/app/controllers/unfurl_links_controller.rb) over
// `Opengraph::Metadata`, `Location`, `Fetch` and `Document` (reference/app/models/opengraph).
//
// Every address is resolved through the private network guard and pinned, every redirect is re-checked, and documents
// are capped at 5MB and 10 responses. The requests go through the "opengraph" client (`Clients`).
//
// Unlike Rails, which gives each connect and read 60 seconds, an unfurl has 10 seconds in all and each request 5 to its
// response head, the client's limiter lets 16 requests run at once, and parsing runs off the async workers: the endpoint
// is open to any signed-in user and fetches pages they choose.
namespace Campfire.App.Integrations

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions

/// What `UnfurlLinksController#create` responds with.
type Unfurl =
    /// `render json: opengraph` (200, `application/json`): the body.
    | Json of string
    /// `head :no_content`
    | NoContent

module Opengraph =
    /// The most one unfurl may take, redirects and the image check included.
    let UnfurlDeadline: TimeSpan = TimeSpan.FromSeconds 10.0

    let unfurlWithin (env: OpengraphEnv) (url: string) (deadline: TimeSpan) (logger: ILogger) : Task<Result<Unfurl, UnfurlError>> =
        task {
            use cancellation = new CancellationTokenSource(deadline, env.Time)
            let token = cancellation.Token
            let unfurling =
                task {
                    match! OpengraphMetadata.fromUrl env url token logger with
                    | Error error -> return Error error
                    | Ok metadata ->
                        match! OpengraphMetadata.validate env metadata token logger with
                        | Error error -> return Error error
                        | Ok(metadata, true) -> return Ok(Unfurl.Json(OpengraphMetadata.toJson metadata))
                        | Ok(_, false) -> return Ok NoContent
                }
            try
                return! unfurling
            with _ when cancellation.IsCancellationRequested ->
                logger.LogWarning("Gave up unfurling {Url} after {Deadline}", url, deadline)
                return Ok NoContent
        }

    /// The action after `params.require(:url)` (a missing or blank `url` is the controller's 400). One that runs out
    /// of time unfurls nothing.
    let unfurlLogged (env: OpengraphEnv) (url: string) (logger: ILogger) : Task<Result<Unfurl, UnfurlError>> =
        unfurlWithin env url UnfurlDeadline logger

    let unfurl (env: OpengraphEnv) (url: string) : Task<Result<Unfurl, UnfurlError>> = unfurlLogged env url NullLogger.Instance
