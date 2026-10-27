// Port of rust/crates/campfire/src/integrations/opengraph/fetch.rs
//
// `Opengraph::Fetch` (reference/app/models/opengraph/fetch.rb): GET or HEAD against a pinned address, following up to
// 10 responses (any 3xx is a redirect), each redirect target parsed, required to be http(s), and resolved through the
// private network guard again. A document must be a 200 `text/html` of at most 5MB, by `Content-Length` and by what's
// actually read.
namespace Campfire.App.Integrations

open System
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Campfire.RichText

/// What an unfurl needs: the factory's "opengraph" client, the resolver that pins each address, and the clock for deadlines.
type OpengraphEnv =
    { Http: IHttpClientFactory
      Resolver: IResolver
      Time: TimeProvider }

type FetchError =
    | TooManyRedirects
    | RedirectDenied
    /// `URI::InvalidURIError` (a missing or unparsable `Location`).
    | InvalidUri
    | Guard of GuardError
    | Transport of TransportError

module FetchError =
    let message (error: FetchError) : string =
        match error with
        | TooManyRedirects -> "Opengraph::Fetch::TooManyRedirectsError"
        | RedirectDenied -> "Opengraph::Fetch::RedirectDeniedError"
        | InvalidUri -> "bad URI"
        | Guard error -> GuardError.message error
        | Transport error -> TransportError.message error

/// URLs on `System.Uri` (J6) behind the RFC 3986 character check.
module OpengraphUri =
    let parse (value: string) : Uri voption = Attachables.tryAbsoluteUri value

    let isHttp (uri: Uri) : bool = uri.Scheme = Uri.UriSchemeHttp || uri.Scheme = Uri.UriSchemeHttps

    /// `uri.host`: none for a URI without an authority (`mailto:foo@bar` has none in Ruby; `System.Uri` reads "bar").
    let host (uri: Uri) : string option =
        let original = uri.OriginalString
        let colon = original.IndexOf ':'
        if uri.Host = "" || colon < 0 || not (original.AsSpan(colon + 1).StartsWith "//") then None else Some uri.Host

module OpengraphFetch =
    [<Literal>]
    let AllowedDocumentContentType = "text/html"

    [<Literal>]
    let MaxBodySize = 5242880

    [<Literal>]
    let MaxRedirects = 10

    /// `Net::HTTP.start(url.host, url.port, ipaddr: ip, use_ssl: ...)` and `http.request(request_class.new(url))`, on the "opengraph" client
    /// (5 s to the response head; the unfurl as a whole has `UnfurlDeadline`).
    let private send (env: OpengraphEnv) (url: Uri) (ip: IPAddress) (meth: string) (cancellation: CancellationToken) : Task<Result<HttpResponseMessage, FetchError>> =
        task {
            use request = new HttpRequestMessage(HttpMethod meth, url)
            Clients.pin ip request |> ignore
            let client = env.Http.CreateClient Clients.Opengraph
            try
                let! response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)
                return Ok response
            with e when not cancellation.IsCancellationRequested ->
                return Error(Transport(TransportError.classify e))
        }

    let private resolveRedirect (env: OpengraphEnv) (location: string option) : Task<Result<Uri * IPAddress, FetchError>> =
        task {
            match location |> Option.map OpengraphUri.parse with
            | None
            | Some ValueNone -> return Error InvalidUri
            | Some(ValueSome url) ->
                if not (OpengraphUri.isHttp url) then
                    return Error RedirectDenied
                else
                    match! Guard.resolve env.Resolver (defaultArg (OpengraphUri.host url) "") with
                    | Ok ip -> return Ok(url, ip)
                    | Error error -> return Error(Guard error)
        }

    let private header (response: HttpResponseMessage) (name: string) : string option =
        match response.Headers.NonValidated.TryGetValues name with
        | true, values -> Some(values.ToString())
        | _ ->
            match response.Content.Headers.NonValidated.TryGetValues name with
            | true, values -> Some(values.ToString())
            | _ -> None

    /// Rails' loop: each 3xx is followed by hand, its target parsed, required to be http(s) and resolved through the guard again.
    let private request (env: OpengraphEnv) (url: Uri) (ip: IPAddress) (meth: string) (cancellation: CancellationToken) : Task<Result<HttpResponseMessage, FetchError>> =
        task {
            let mutable url = url
            let mutable ip = ip
            let mutable result: Result<HttpResponseMessage, FetchError> option = None
            let mutable attempt = 0
            while result.IsNone && attempt < MaxRedirects do
                match! send env url ip meth cancellation with
                | Error error -> result <- Some(Error error)
                | Ok response ->
                    let status = int response.StatusCode
                    if status >= 300 && status < 400 then
                        let location = header response "location"
                        response.Dispose()
                        match! resolveRedirect env location with
                        | Ok(next, nextIp) ->
                            url <- next
                            ip <- nextIp
                        | Error error -> result <- Some(Error error)
                    else
                        result <- Some(Ok response)
                attempt <- attempt + 1
            return defaultArg result (Error TooManyRedirects)
        }

    /// `fetch_document(url, ip:)`: the body, or `None` when the response isn't acceptable.
    let fetchDocument (env: OpengraphEnv) (url: Uri) (ip: IPAddress) (cancellation: CancellationToken) : Task<Result<byte[] option, FetchError>> =
        task {
            match! request env url ip "GET" cancellation with
            | Error error -> return Error error
            | Ok response ->
                use response = response
                if int response.StatusCode <> 200 || Clients.contentType response <> Some AllowedDocumentContentType then
                    return Ok None
                else
                    match response.Content.Headers.ContentLength |> Option.ofNullable with
                    | Some length when length > int64 MaxBodySize -> return Ok None
                    | _ ->
                        try
                            match! Clients.readLimited response.Content MaxBodySize cancellation with
                            | ValueSome body -> return Ok(Some body)
                            | ValueNone -> return Ok None
                        with e when not cancellation.IsCancellationRequested ->
                            return Error(Transport(TransportError.classify e))
        }

    /// `fetch_content_type(url, ip:)`: the final response's `Content-Type`, whatever its status.
    let fetchContentType (env: OpengraphEnv) (url: Uri) (ip: IPAddress) (cancellation: CancellationToken) : Task<Result<string option, FetchError>> =
        task {
            match! request env url ip "HEAD" cancellation with
            | Error error -> return Error error
            | Ok response ->
                use response = response
                return Ok(header response "content-type")
        }
