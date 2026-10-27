// Port of rust/crates/campfire/src/controllers/unfurl_links.rs
//
// `UnfurlLinksController` (reference/app/controllers/unfurl_links_controller.rb): the composer
// asks for a pasted URL's OpenGraph metadata. A Web action (unit 2.11).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Integrations
open Campfire.App.Web

module UnfurlLinks =
    /// `Opengraph::Metadata.from_url(url)`, as JSON when `valid?` (`Opengraph`).
    let private opengraphJson (http: HttpContext) (url: string) : System.Threading.Tasks.Task<Result<string option, Error>> =
        task {
            let net = Network.system (Data.app http).Time
            match! Opengraph.unfurlLogged net url ((Data.app http).Loggers.CreateLogger "campfire.opengraph") with
            | Result.Error e -> return Result.Error(Internal(exn (UnfurlError.message e)))
            | Ok(Unfurl.Json json) -> return Ok(Some json)
            | Ok Unfurl.NoContent -> return Ok None
        }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                // `params.require(:url)`. A hash or array passes `require`, but `URI.parse` can't take it
                // (`InvalidURIError`, rescued), so the metadata has no title and isn't valid.
                match Params.lookup http "url" with
                | Structured -> return Respond.head http 204
                | Absent -> return! Result.Error(ParameterMissing "url")
                | Str blank when System.String.IsNullOrWhiteSpace blank -> return! Result.Error(ParameterMissing "url")
                | Str url ->
                    match! opengraphJson http url with
                    // `render json: opengraph`
                    | Some(json: string) -> return Pages.renderText http 200 Pages.JsonUtf8 json
                    | None -> return Respond.head http 204
            }
