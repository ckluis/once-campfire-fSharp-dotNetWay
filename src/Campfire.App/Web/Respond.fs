// plans/design-kit-auth.md section 4, unit 2.8 (Web/Respond.fs): what a controller says back, on `HttpContext`.
//
// Redirects (`redirect_to`, `redirect_back_or_to`, with Rails' unsafe-redirect rules), conditional GET (`fresh_when`,
// `stale?`: weak ETags, `Last-Modified`, 304), `expires_in` and `send_file`. The headers the helpers decide are set on
// the response as they are called, so a later `Halt` or error response keeps them (as Rails' `response.headers` do);
// the helpers that answer return the `HttpHandler` that writes the rest.
//
// The framework does the transport: the `If-None-Match` and `If-Modified-Since` headers are the typed headers
// (`GetTypedHeaders`), `send_file` is `Results.File` with range processing (a `Range` request is answered 206/416, which
// Rails' `send_file` does not do: README Known differences), and compression is `UseResponseCompression` (K11).
//
// Not reproduced (K14): `Rack::ETag`'s body hash on every page. The default `Cache-Control` for a 200 that set none
// is `Native.wrap`'s.
namespace Campfire.App.Web

open System
open System.Security.Cryptography
open System.Text
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.Net.Http.Headers

/// `Cache-Control` directives set by `expires_in`, `fresh_when(public:)`, `no_store` and friends,
/// normalized like `ActionDispatch::Http::Cache::Response#merge_and_normalize_cache_control!`.
[<CustomEquality; NoComparison>]
type CacheControl =
    { MaxAge: uint64 voption
      Public: bool
      Private: bool
      MustRevalidate: bool
      NoCache: bool
      NoStore: bool
      MustUnderstand: bool
      StaleWhileRevalidate: uint64 voption
      StaleIfError: uint64 voption
      Immutable: bool
      Extras: string list }

    override this.Equals(other: obj) =
        match other with
        | :? CacheControl as o ->
            this.MaxAge = o.MaxAge
            && this.Public = o.Public
            && this.Private = o.Private
            && this.MustRevalidate = o.MustRevalidate
            && this.NoCache = o.NoCache
            && this.NoStore = o.NoStore
            && this.MustUnderstand = o.MustUnderstand
            && this.StaleWhileRevalidate = o.StaleWhileRevalidate
            && this.StaleIfError = o.StaleIfError
            && this.Immutable = o.Immutable
            && this.Extras = o.Extras
        | _ -> false

    override this.GetHashCode() = hash (this.MaxAge, this.Public, this.NoStore, this.NoCache)

    member this.IsEmpty: bool =
        this.MaxAge.IsNone
        && not this.Public
        && not this.Private
        && not this.MustRevalidate
        && not this.NoCache
        && not this.NoStore
        && not this.MustUnderstand
        && this.StaleWhileRevalidate.IsNone
        && this.StaleIfError.IsNone
        && not this.Immutable
        && this.Extras.IsEmpty

    member this.ToHeader() : string | null =
        if this.IsEmpty then
            null
        else
            let options = ResizeArray<string>()
            if this.NoStore then
                if this.Private then options.Add "private"
                if this.MustUnderstand then options.Add "must-understand"
                options.Add "no-store"
            elif this.NoCache then
                if this.Public then options.Add "public"
                options.Add "no-cache"
                options.AddRange this.Extras
            else
                match this.MaxAge with
                | ValueSome maxAge -> options.Add $"max-age={maxAge}"
                | ValueNone -> ()
                options.Add(if this.Public then "public" else "private")
                if this.MustRevalidate then options.Add "must-revalidate"
                match this.StaleWhileRevalidate with
                | ValueSome swr -> options.Add $"stale-while-revalidate={swr}"
                | ValueNone -> ()
                match this.StaleIfError with
                | ValueSome sie -> options.Add $"stale-if-error={sie}"
                | ValueNone -> ()
                if this.Immutable then options.Add "immutable"
                options.AddRange this.Extras
            String.Join(", ", options)

module CacheControl =
    /// No directives at all (`Cache-Control` left to the middleware).
    let Empty: CacheControl =
        { MaxAge = ValueNone
          Public = false
          Private = false
          MustRevalidate = false
          NoCache = false
          NoStore = false
          MustUnderstand = false
          StaleWhileRevalidate = ValueNone
          StaleIfError = ValueNone
          Immutable = false
          Extras = [] }

/// Options for `expires_in`.
type ExpiresIn =
    { Public: bool
      MustRevalidate: bool
      StaleWhileRevalidate: uint64 voption
      StaleIfError: uint64 voption
      Immutable: bool }

module ExpiresIn =
    let Default: ExpiresIn =
        { Public = false
          MustRevalidate = false
          StaleWhileRevalidate = ValueNone
          StaleIfError = ValueNone
          Immutable = false }

/// Options for `send_file` / `send_data`.
type SendOptions =
    { Filename: string | null
      /// `type:`; defaults to the filename's MIME type, else `application/octet-stream`.
      ContentType: string | null
      /// `disposition:`; null omits the header. Defaults to `attachment`.
      Disposition: string | null
      Status: int }

module SendOptions =
    let Default: SendOptions =
        { Filename = null
          ContentType = null
          Disposition = "attachment"
          Status = 200 }

    /// `type: content_type, disposition: "inline"`
    let Inline (contentType: string) : SendOptions =
        { Default with
            ContentType = contentType
            Disposition = "inline" }

/// Options for `redirect_to`.
type RedirectOptions =
    {
        /// Defaults to 302 Found, like Rails.
        Status: int
        Notice: string | null
        Alert: string | null
        AllowOtherHost: bool
    }

module RedirectOptions =
    let Default: RedirectOptions =
        { Status = 302
          Notice = null
          Alert = null
          AllowOtherHost = false }

/// Validators for `fresh_when` / `stale?`.
type Freshness =
    {
        /// The weak validator, already expanded to a cache key (`record.cache_key_with_version`).
        Etag: string | null
        StrongEtag: string | null
        LastModified: DateTimeOffset voption
        Public: bool
        /// The template digest `ETagWithTemplateDigest` would add.
        Template: string | null
    }

module Freshness =
    let Default: Freshness =
        { Etag = null
          StrongEtag = null
          LastModified = ValueNone
          Public = false
          Template = null }

    /// A weak validator, as `fresh_when(record)` makes.
    let OfEtag (validator: string) : Freshness = { Default with Etag = validator }

module Respond =
    // --- flash ---------------------------------------------------------------------------------------

    let private flashItem = "campfire.web.flash"

    /// `flash`, loaded from the session on first use and committed by `commit` before the response starts.
    let flash (http: HttpContext) : Flash =
        match http.Items[flashItem] with
        | :? Flash as loaded -> loaded
        | _ ->
            let loaded = Flash.load http
            http.Items[flashItem] <- loaded
            loaded

    /// `commit_flash`, then the session itself: what the request wrote is stored before the response goes out (the session
    /// middleware would do it after, when a quick client has already asked again).
    let commit (http: HttpContext) : Task =
        match http.Items[flashItem] with
        | :? Flash as pending ->
            http.Items.Remove flashItem |> ignore
            Flash.commit http pending
        | _ -> ()
        SessionReset.commit http

    /// `reset_session`: no data, no flash, and the browser's session cookie goes.
    let resetSession (http: HttpContext) : unit =
        SessionReset.run http
        http.Items.Remove flashItem |> ignore

    /// The raw request path, still percent-encoded and without its query (`PATH_INFO`).
    let rawPath (http: HttpContext) : string =
        let target =
            match http.Features.Get<IHttpRequestFeature>() with
            | null -> http.Request.Path.Value
            | feature -> feature.RawTarget
        match target with
        | null
        | "" -> "/"
        | target ->
            let target = match target.IndexOf '?' with -1 -> target | q -> target.Substring(0, q)
            if target.StartsWith '/' then
                target
            else
                // Absolute form: `http://host/path`.
                match target.IndexOf("://", StringComparison.Ordinal) with
                | -1 -> target
                | at ->
                    match target.IndexOf('/', at + 3) with
                    | -1 -> "/"
                    | slash -> target.Substring slash

    // --- redirects -----------------------------------------------------------------------------------

    /// The host of an absolute or protocol-relative URL, null for paths.
    let private urlHost (url: string) : string | null =
        let rest: string | null =
            if url.StartsWith("//", StringComparison.Ordinal) then
                url.Substring 2
            else
                match url.IndexOf("://", StringComparison.Ordinal) with
                | -1 -> null
                | at ->
                    let scheme = url.Substring(0, at)
                    if scheme.Length = 0 || not (Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '+' || c = '-' || c = '.') scheme) then
                        null
                    else
                        url.Substring(at + 3)
        match rest with
        | null -> null
        | rest ->
            let cut = rest.IndexOfAny [| '/'; '?'; '#' |]
            let authority = if cut < 0 then rest else rest.Substring(0, cut)
            let hostPort = authority.Substring(authority.LastIndexOf '@' + 1)
            if hostPort.StartsWith '[' then
                let close = hostPort.IndexOf ']'
                if close < 0 then hostPort else hostPort.Substring(0, close + 1)
            else
                match hostPort.IndexOf ':' with
                | -1 -> hostPort
                | colon -> hostPort.Substring(0, colon)

    /// Rust's `{:?}` of a `&str`, which the reference's messages quote their locations with.
    let private quoted (s: string) : string =
        let out = StringBuilder(s.Length + 2)
        out.Append '"' |> ignore
        for c in s do
            match c with
            | '"' -> out.Append "\\\"" |> ignore
            | '\\' -> out.Append "\\\\" |> ignore
            | '\n' -> out.Append "\\n" |> ignore
            | '\r' -> out.Append "\\r" |> ignore
            | '\t' -> out.Append "\\t" |> ignore
            | c when c < ' ' || c = '\127' -> out.Append("\\u{").Append((int c).ToString "x").Append('}') |> ignore
            | c -> out.Append c |> ignore
        out.Append('"').ToString()

    /// An absolute URL for `path` on this request's host (what `*_url` helpers produce).
    let urlFor (http: HttpContext) (path: string) : string =
        let request = http.Request
        let host = request.Host
        // `request.host_with_port`: the port only when it is not the scheme's default.
        let authority =
            if host.Port.HasValue && host.Port.Value = (if request.IsHttps then 443 else 80) then host.Host else host.ToString()
        $"{request.Scheme}://{authority}{path}"

    /// `_url_host_allowed?`: a path, or a URL on this request's host. A backslash anywhere makes the URL invalid for Ruby's `URI` (the check rescues `URI::Error` and answers false), and browsers
    /// read `\` as `/`, so `/\evil.com` and `http://evil.com\@host/` are refused outright.
    let internal hostAllowed (http: HttpContext) (url: string) : bool =
        if url.Contains '\\' then
            false
        else
        match urlHost url with
        | null -> url.StartsWith '/' && not (url.StartsWith("//", StringComparison.Ordinal))
        | host -> String.Equals(host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase)

    /// `_compute_redirect_to_location` for strings: absolute URLs pass through, paths get the request's protocol and host,
    /// path-relative URLs raise (`action_on_path_relative_redirect`).
    let private computeLocation (http: HttpContext) (location: string) : Result<string, Campfire.App.Web.Error> =
        let isAbsolute =
            location.StartsWith("//", StringComparison.Ordinal)
            || (location.Length > 0
                && Char.IsAsciiLetter location[0]
                && (match location.IndexOf ':' with
                    | -1 -> false
                    | colon ->
                        location.Substring(0, colon)
                        |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '-' || c = '+' || c = '.')))
        if isAbsolute then
            Ok(location.Replace("\000", "").Replace("\r", "").Replace("\n", ""))
        elif location.Length > 0 && not (location.StartsWith '/') && not (location.StartsWith '?') then
            Result.Error(Campfire.App.Web.Error.UnsafeRedirect $"Path relative URL redirect detected: {quoted location}")
        else
            Ok((urlFor http location).Replace("\000", "").Replace("\r", "").Replace("\n", ""))

    /// `redirect_to location, status:, notice:, alert:, allow_other_host:`: the handler that answers, or `UnsafeRedirect`.
    let redirectToWith (http: HttpContext) (location: string) (options: RedirectOptions) : Result<HttpHandler, Campfire.App.Web.Error> =
        match options.Notice with
        | null -> ()
        | notice -> (flash http).SetNotice notice
        match options.Alert with
        | null -> ()
        | alert -> (flash http).SetAlert alert
        match computeLocation http location with
        | Result.Error e -> Result.Error e
        | Ok location ->
            if location |> Seq.exists (fun c -> (c >= '\000' && c <= '\008') || (c >= '\010' && c <= '\031')) then
                Result.Error(Campfire.App.Web.Error.UnsafeRedirect $"The redirect URL {location} contains illegal characters")
            elif not options.AllowOtherHost && not (hostAllowed http location) then
                Result.Error(Campfire.App.Web.Error.UnsafeRedirect $"Unsafe redirect to {quoted location}")
            else
                Ok(fun (http: HttpContext) ->
                    http.Response.StatusCode <- options.Status
                    http.Response.ContentType <- "text/html; charset=utf-8"
                    http.Response.Headers.Location <- location
                    http.Response.ContentLength <- 0L
                    Task.CompletedTask)

    /// A status that carries no body (`1xx`, `204`, `304`).
    let noBody (status: int) : bool = (status >= 100 && status <= 199) || status = 204 || status = 304

    /// `100..=199 | 204 | 205 | 304`: a bare `head` leaves the content type off these.
    let noContent (status: int) : bool = noBody status || status = 205

    /// `head status`: no body, and unless the status has none a bare content type of the request's format.
    let head (http: HttpContext) (status: int) : HttpHandler =
        let contentType =
            if noContent status then
                null
            else
                match Formats.requested http with
                | Ok(first :: _) when first <> Any -> Formats.mimeType first
                | _ -> "text/html"
        fun (http: HttpContext) ->
            http.Response.StatusCode <- status
            if not (isNull contentType) then http.Response.ContentType <- contentType
            if not (noBody status) then http.Response.ContentLength <- 0L
            Task.CompletedTask

    /// `redirect_to location` (302).
    let redirectTo (http: HttpContext) (location: string) : Result<HttpHandler, Campfire.App.Web.Error> =
        redirectToWith http location RedirectOptions.Default

    /// `redirect_back_or_to fallback`: the referer when it is on this host.
    let redirectBackOrTo (http: HttpContext) (fallback: string) : Result<HttpHandler, Campfire.App.Web.Error> =
        match http.Request.Headers.Referer.ToString() with
        | "" -> redirectTo http fallback
        | referer -> if hostAllowed http referer then redirectTo http referer else redirectTo http fallback

    /// `head status, location:` (`head :created, location: message_url(...)`): no body and a `Location` header, with no host check.
    let headWithLocation (http: HttpContext) (status: int) (location: string) : Result<HttpHandler, Campfire.App.Web.Error> =
        computeLocation http location
        |> Result.map (fun location ->
            let written = head http status
            fun (http: HttpContext) ->
                http.Response.Headers.Location <- location
                written http)

    // --- cache headers -------------------------------------------------------------------------------

    /// `expires_in seconds, public:, stale_while_revalidate:, ...`
    let expiresIn (http: HttpContext) (seconds: uint64) (options: ExpiresIn) : unit =
        let control =
            { CacheControl.Empty with
                MaxAge = ValueSome seconds
                Public = options.Public
                MustRevalidate = options.MustRevalidate
                StaleWhileRevalidate = options.StaleWhileRevalidate
                StaleIfError = options.StaleIfError
                Immutable = options.Immutable }
        http.Response.Headers.CacheControl <- control.ToHeader()
        if not (http.Response.Headers.ContainsKey HeaderNames.Date) then
            http.Response.Headers.Date <- HeaderUtilities.FormatDate DateTimeOffset.UtcNow

    // --- conditional GET -----------------------------------------------------------------------------

    /// `request.fresh?(response)` with `strict_freshness` (the 8.0 default): an `If-None-Match` list naming the ETag (or
    /// `*`), or else an `If-Modified-Since` no earlier than `Last-Modified`.
    let private isFresh (http: HttpContext) (etag: string | null) (lastModified: DateTimeOffset voption) : bool =
        let typed = http.Request.GetTypedHeaders()
        if http.Request.Headers.IfNoneMatch.Count = 0 then
            match typed.IfModifiedSince, lastModified with
            | since, ValueSome modified when since.HasValue -> since.Value >= modified
            | _ -> false
        else
            match etag with
            | null -> false
            | etag -> typed.IfNoneMatch |> Seq.exists (fun candidate -> candidate = EntityTagHeaderValue.Any || candidate.ToString() = etag)

    /// `[validator, *etaggers]` digested like `ActiveSupport::Digest.hexdigest(expand_cache_key(...))`. The etaggers are
    /// turbo-rails' frame etagger, `ETagWithTemplateDigest` and `ETagWithFlash`.
    let private combineEtags (http: HttpContext) (validator: string | null) (freshness: Freshness) : string =
        let parts = ResizeArray<string>()
        match validator with
        | null -> ()
        | v -> parts.Add v
        if Formats.isTurboFrame http then parts.Add "frame"
        match freshness.Template with
        | null -> ()
        | template -> parts.Add template
        let flash = flash http
        if not flash.IsEmpty then
            parts.Add(String.Join("&", flash.Keys |> List.map (fun k -> $"{k}={flash.GetStr k}")))
        Convert.ToHexStringLower(ReadOnlySpan<byte>(SHA256.HashData(Encoding.UTF8.GetBytes(String.Join("/", parts))), 0, 16))

    /// `fresh_when`: sets `ETag`, `Last-Modified` and `Cache-Control` (unless `expires_in` already did) and returns the
    /// handler that answers 304 when the request is fresh.
    let freshWhen (http: HttpContext) (freshness: Freshness) : HttpHandler voption =
        let headers = http.Response.Headers
        let etagged = not (isNull freshness.StrongEtag) || not (isNull freshness.Etag) || not (isNull freshness.Template)
        let mutable etag: string | null = null
        if etagged then
            let validator, weak =
                match freshness.StrongEtag with
                | null -> freshness.Etag, true
                | strong -> strong, false
            let digest = combineEtags http validator freshness
            etag <- (if weak then $"W/\"{digest}\"" else $"\"{digest}\"")
            headers.ETag <- etag
        match freshness.LastModified with
        | ValueSome modified -> headers.LastModified <- HeaderUtilities.FormatDate modified
        | ValueNone -> ()
        if etagged || freshness.LastModified.IsSome then
            if not (headers.ContainsKey HeaderNames.CacheControl) then
                let visibility = if freshness.Public then "public" else "private"
                headers.CacheControl <- $"max-age=0, {visibility}, must-revalidate"
        if isFresh http etag freshness.LastModified then
            ValueSome(fun (http: HttpContext) ->
                http.Response.StatusCode <- 304
                http.Response.ContentLength <- Nullable()
                http.Response.ContentType <- null
                Task.CompletedTask)
        else
            ValueNone

    /// `stale?`: the inverse of freshness, after setting the validators; the 304 handler when it is fresh.
    let stale (http: HttpContext) (freshness: Freshness) : Result<unit, HttpHandler> =
        match freshWhen http freshness with
        | ValueSome notModified -> Result.Error notModified
        | ValueNone -> Ok()

    // --- files ---------------------------------------------------------------------------------------

    /// `send_file path, type:, disposition:, filename:`: the framework's file result, with range processing; the
    /// `Content-Disposition` is Rails' (`ActionDispatch::Http::ContentDisposition`), not the framework's.
    let sendFile (path: string) (options: SendOptions) : Result<HttpHandler, Campfire.App.Web.Error> =
        if not (IO.File.Exists path) then
            Result.Error(Campfire.App.Web.Error.Internal(Exception $"Cannot read file {path}"))
        else
            let filename = match options.Filename with null -> IO.Path.GetFileName path | name -> name
            let contentType =
                match options.ContentType with
                | null ->
                    match filename.LastIndexOf '.' with
                    | -1
                    | 0 -> "application/octet-stream"
                    | dot ->
                        match Formats.ofExtension (filename.Substring(dot + 1).ToLowerInvariant()) with
                        | Some format -> Formats.mimeType format
                        | None -> "application/octet-stream"
                | given -> given
            Ok(fun (http: HttpContext) ->
                match options.Disposition with
                | null -> ()
                | disposition -> http.Response.Headers.ContentDisposition <- Campfire.Rails.ContentDisposition.format disposition filename
                http.Response.Headers["content-transfer-encoding"] <- "binary"
                // A relative path is a virtual one to `Results.File` (under the web root): the storage directory may be relative.
                let result = Results.File(IO.Path.GetFullPath path, contentType, enableRangeProcessing = true)
                result.ExecuteAsync http)
