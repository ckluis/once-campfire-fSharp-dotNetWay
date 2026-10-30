// Port of rust/crates/campfire/src/integrations/opengraph/metadata.rs
//
// `Opengraph::Metadata` and its `Fetching` concern (reference/app/models/opengraph/metadata.rb,
// reference/app/models/opengraph/metadata/fetching.rb).
namespace Campfire.App.Integrations

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Campfire.Rails
open Campfire.RichText

/// Where the Rails action raises (a 500).
type UnfurlError = Raised of string

module UnfurlError =
    let message (error: UnfurlError) : string =
        match error with
        | Raised cls -> $"raised {cls}"

module OpengraphMetadata =
    let private twitterHosts = [ "twitter.com"; "www.twitter.com"; "x.com"; "www.x.com" ]

    [<Literal>]
    let private FxTwitterHost = "fxtwitter.com"

    let private allowedImageContentTypes = [ "image/jpeg"; "image/png"; "image/gif"; "image/webp" ]

    /// Pages parsed at once. The blocking work outlives an unfurl that gives up at its deadline, so it's bounded by
    /// permits the work itself holds, not by the unfurl's slot.
    [<Literal>]
    let private MaxConcurrentParses = 4

    let private parses = new SemaphoreSlim(MaxConcurrentParses)

    /// Runs CPU-bound work (parsing and sanitizing pages of up to 5MB) off the async workers.
    let offTheRuntime (work: unit -> 'T) : Task<'T> =
        task {
            do! parses.WaitAsync()
            try
                return! Task.Run work
            finally
                parses.Release() |> ignore
        }

    /// The model's attributes as instance variables, in the order they were first assigned (which is the order
    /// `render json:` emits them).
    type Metadata = { Attributes: (string * string option) list }

    let private assign (attributes: (string * string option) list) (key: string) (value: string option) : (string * string option) list =
        if attributes |> List.exists (fun (k, _) -> k = key) then
            attributes |> List.map (fun (k, v) -> if k = key then k, value else k, v)
        else
            attributes @ [ key, value ]

    let get (metadata: Metadata) (key: string) : string option =
        metadata.Attributes |> List.tryFind (fun (k, _) -> k = key) |> Option.bind snd

    /// `tweet_url?`. A URL `System.Uri` refuses is no tweet; Rails' `URI::InvalidComponentError` for some of them (`mailto:` among them) is
    /// an unfurl of nothing here (README, class B).
    let private tweetUrl (url: string) : bool =
        match OpengraphUri.parse url with
        | ValueSome uri ->
            (OpengraphUri.host uri |> Option.exists (fun h -> List.contains h twitterHosts))
            && (let path = uri.AbsolutePath in not (OpengraphDocument.isBlank path) && path <> "/")
        | ValueNone -> false

    /// `replace_twitter_domain_for_opengraph_support`
    let private replaceTwitterDomain (url: string) : string option =
        match OpengraphUri.parse url with
        | ValueNone -> None
        | ValueSome uri ->
            if List.contains uri.Host twitterHosts then
                let builder = UriBuilder(uri, Host = FxTwitterHost)
                Some builder.Uri.AbsoluteUri
            else
                Some uri.OriginalString

    /// `fetch_document(untrusted_url)`: tweets are read through fxtwitter.com. A tweet whose fxtwitter page can't be
    /// read raises (`nil.force_encoding`), as does a URL that `URI.parse` rejects with `URI::InvalidComponentError`.
    let private fetchDocument (env: OpengraphEnv) (url: string) (cancellation: CancellationToken) (logger: ILogger) : Task<Result<byte[] option, UnfurlError>> =
        task {
            match tweetUrl url with
            | true ->
                let fxtwitterUrl = replaceTwitterDomain url
                let! html = Location(env, fxtwitterUrl, cancellation, logger).ReadHtml()
                match html with
                | Some html -> return Ok(Some html)
                | None -> return Error(Raised "NoMethodError")
            | false ->
                let! html = Location(env, Some url, cancellation, logger).ReadHtml()
                return Ok html
        }

    /// `valid_canonical_url(url, fallback)`
    let private validCanonicalUrl (env: OpengraphEnv) (url: string option) (fallback: string) (cancellation: CancellationToken) (logger: ILogger) : Task<string> =
        task {
            match url with
            | Some url ->
                let! valid = Location(env, Some url, cancellation, logger).IsValid()
                return if valid then url else fallback
            | None -> return fallback
        }

    /// `valid_image_content_type(image)`: kept only when a HEAD says it's a JPEG, PNG, GIF or WebP.
    let private validImageContentType (env: OpengraphEnv) (image: string option) (cancellation: CancellationToken) (logger: ILogger) : Task<string option> =
        task {
            match image |> Option.filter (fun i -> not (OpengraphDocument.isBlank i)) with
            | None -> return None
            | Some image ->
                match OpengraphUri.parse image with
                | ValueNone ->
                    logger.LogWarning("Failed to fetch image content tpye: {Image} (bad URI(is not URI?): {Quoted})", image, Json.generate (Value.String image))
                    return None
                | ValueSome _ ->
                    let! contentType = Location(env, Some image, cancellation, logger).FetchContentType()
                    match contentType |> Option.map (fun c -> c.ToLowerInvariant()) with
                    | Some contentType when List.contains contentType allowedImageContentTypes -> return Some image
                    | _ -> return None
        }

    /// `Metadata.from_url(url)`
    let fromUrl (env: OpengraphEnv) (url: string) (cancellation: CancellationToken) (logger: ILogger) : Task<Result<Metadata, UnfurlError>> =
        task {
            match! fetchDocument env url cancellation logger with
            | Error error -> return Error error
            | Ok body ->
                let! found = offTheRuntime (fun () -> OpengraphDocument.opengraphAttributes body)
                let og (key: string) = found |> List.tryFind (fun (k, _) -> k = key) |> Option.map snd
                let! canonicalUrl = validCanonicalUrl env (og "url") url cancellation logger
                let! image = validImageContentType env (og "image") cancellation logger
                let attributes = found |> List.map (fun (k, v) -> k, Some v)
                let attributes = assign attributes "url" (Some canonicalUrl)
                let attributes = assign attributes "image" image
                return Ok { Attributes = attributes }
        }

    /// `strip_tags` (Rails::HTML5::FullSanitizer): the text of the HTML5 fragment, serialized (on `Html5`, R10).
    let private stripTags (html: string) : Result<string, UnfurlError> =
        if html = "" then
            Ok ""
        else
            match Html5.parseBody html with
            | Error _ -> Error(Raised "ArgumentError")
            | Ok root ->
                let text =
                    String.Concat(
                        [ for node in Html5.descendants root do
                              match node with
                              | :? AngleSharp.Dom.IText as text -> text.Data
                              | _ -> () ]
                    )
                let out = System.Text.StringBuilder()
                Html5.escapeText text out
                Ok(out.ToString())

    /// `sanitize` (Rails::HTML5::SafeListSanitizer with its default allowlist).
    let private sanitizeHtml (html: string) : Result<string, UnfurlError> =
        match Sanitizer.sanitize html SafeList.defaults with
        | Ok html -> Ok html
        | Error _ -> Error(Raised "ArgumentError")

    /// `strip_tags` then `sanitize`, as `before_validation` runs them.
    let stripAndSanitize (html: string) : Result<string, UnfurlError> = stripTags html |> Result.bind sanitizeHtml

    /// `valid?`: sanitizes the title and description first (`before_validation`), then checks presence and, when
    /// there's an image, that it's a valid location.
    let validate (env: OpengraphEnv) (metadata: Metadata) (cancellation: CancellationToken) (logger: ILogger) : Task<Result<Metadata * bool, UnfurlError>> =
        task {
            let sanitizedKeys = [ "title"; "description" ]
            let values = sanitizedKeys |> List.map (get metadata)
            let! sanitized =
                offTheRuntime (fun () ->
                    values
                    |> List.map (fun value ->
                        match value with
                        | Some v -> stripAndSanitize v |> Result.map Some
                        | None -> Ok None))
            let mutable attributes = metadata.Attributes
            let mutable failure: UnfurlError option = None
            for (key, value) in List.zip sanitizedKeys sanitized do
                match value with
                | Ok value -> if failure.IsNone then attributes <- assign attributes key value
                | Error error -> if failure.IsNone then failure <- Some error
            match failure with
            | Some error -> return Error error
            | None ->
                let metadata = { Attributes = attributes }
                let present key = get metadata key |> Option.exists (fun v -> not (OpengraphDocument.isBlank v))
                let mutable valid = present "title" && present "url" && present "description"
                match get metadata "image" |> Option.filter (fun i -> not (OpengraphDocument.isBlank i)) with
                | Some image ->
                    let! imageValid = Location(env, Some image, cancellation, logger).IsValid()
                    valid <- valid && imageValid
                | None -> ()
                return Ok(metadata, valid)
        }

    /// `render json: opengraph` after `valid?`: `instance_values`, which by then include the validation context and
    /// the (empty) errors.
    let toJson (metadata: Metadata) : string =
        let json = Text.StringBuilder("{")
        for (key, value) in metadata.Attributes do
            let encoded =
                match value with
                | Some value -> Json.encode (Value.String value)
                | None -> Json.encode Value.Null
            json.Append(Json.encode (Value.String key)).Append(':').Append(encoded).Append(',') |> ignore
        json.Append("\"context_for_validation\":{\"context\":null},\"errors\":{}}") |> ignore
        json.ToString()
