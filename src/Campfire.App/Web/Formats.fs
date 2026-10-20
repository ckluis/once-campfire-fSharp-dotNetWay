// plans/design-kit-auth.md section 3 (Web/Formats.fs): `request.formats` and `respond_to`.
//
// The Accept header is parsed by the framework (`MediaTypeHeaderValue.TryParseList`) and stably sorted by quality;
// what stays Rails' is the order of the rules: `params[:format]`, then the Accept header unless it is a browser's
// (`BROWSER_LIKE_ACCEPTS`) or absent, then the path extension, then Html (Js for XHR), the `text/*` and
// `application/*` expansion, and Turbo's MIME first. Not reproduced (class B, README Known differences): Ruby's
// `String#to_f` reading of odd `q` values, and `Mime::Type.parse`'s `text/xml` folding; an Accept header the
// framework cannot parse is `UnknownFormat` (406), as Rails' `InvalidMimeType` is.
namespace Campfire.App.Web

open System
open Microsoft.AspNetCore.Http
open Microsoft.Net.Http.Headers

type Format =
    | Html
    | TurboStream
    | Json
    | Js
    | Text
    | Xml
    | Svg
    /// A registered type the app does not branch on (`png`, `css`, ...), by its Rails symbol.
    | Other of string
    /// `*/*`, only meaningful in negotiation.
    | Any

module Formats =
    /// `Mime::Type` registrations in Rails' order (the order matters for the `text/*` expansion): the format, its
    /// type, synonyms, and the extensions that name it.
    let private registry: (Format * string * string list * string list) list =
        [ Html, "text/html", [ "application/xhtml+xml" ], [ "html"; "xhtml" ]
          Text, "text/plain", [], [ "text"; "txt" ]
          Js, "text/javascript", [ "application/javascript"; "application/x-javascript" ], [ "js" ]
          Other "css", "text/css", [], [ "css" ]
          Other "ics", "text/calendar", [], [ "ics" ]
          Other "csv", "text/csv", [], [ "csv" ]
          Other "vcf", "text/vcard", [], [ "vcf" ]
          Other "vtt", "text/vtt", [], [ "vtt" ]
          Other "md", "text/markdown", [], [ "md"; "markdown" ]
          Other "png", "image/png", [], [ "png" ]
          Other "jpeg", "image/jpeg", [], [ "jpg"; "jpeg"; "jpe"; "pjpeg" ]
          Other "gif", "image/gif", [], [ "gif" ]
          Other "bmp", "image/bmp", [], [ "bmp" ]
          Other "tiff", "image/tiff", [], [ "tif"; "tiff" ]
          Svg, "image/svg+xml", [], [ "svg" ]
          Other "webp", "image/webp", [], [ "webp" ]
          Other "mpeg", "video/mpeg", [], [ "mpg"; "mpeg"; "mpe" ]
          Other "mp3", "audio/mpeg", [], [ "mp1"; "mp2"; "mp3" ]
          Other "ogg", "audio/ogg", [], [ "oga"; "ogg"; "spx"; "opus" ]
          Other "m4a", "audio/aac", [ "audio/mp4" ], [ "m4a"; "mpg4"; "aac" ]
          Other "webm", "video/webm", [], [ "webm" ]
          Other "mp4", "video/mp4", [], [ "mp4"; "m4v" ]
          Other "otf", "font/otf", [], [ "otf" ]
          Other "ttf", "font/ttf", [], [ "ttf" ]
          Other "woff", "font/woff", [], [ "woff" ]
          Other "woff2", "font/woff2", [], [ "woff2" ]
          Xml, "application/xml", [ "text/xml"; "application/x-xml" ], [ "xml" ]
          Other "rss", "application/rss+xml", [], [ "rss" ]
          Other "atom", "application/atom+xml", [], [ "atom" ]
          Other "yaml", "application/x-yaml", [ "text/yaml" ], [ "yml"; "yaml" ]
          Other "multipart_form", "multipart/form-data", [], []
          Other "url_encoded_form", "application/x-www-form-urlencoded", [], []
          Json, "application/json", [ "text/x-json"; "application/jsonrequest"; "application/problem+json" ], [ "json" ]
          Other "pdf", "application/pdf", [], [ "pdf" ]
          Other "zip", "application/zip", [], [ "zip" ]
          Other "gzip", "application/gzip", [ "application/x-gzip" ], [ "gz" ]
          TurboStream, "text/vnd.turbo-stream.html", [], [] ]

    /// The Rails symbol (`:html`, `:turbo_stream`).
    let symbol (format: Format) : string =
        match format with
        | Html -> "html"
        | TurboStream -> "turbo_stream"
        | Json -> "json"
        | Js -> "js"
        | Text -> "text"
        | Xml -> "xml"
        | Svg -> "svg"
        | Other s -> s
        | Any -> "*/*"

    /// The type a response in this format is sent as.
    let mimeType (format: Format) : string =
        match registry |> List.tryFind (fun (f, _, _, _) -> f = format) with
        | Some(_, mime, _, _) -> mime
        | None -> "*/*"

    /// `Mime[ext]`: by symbol or extension.
    let ofExtension (extension: string) : Format option =
        registry
        |> List.tryFind (fun (f, _, _, extensions) -> symbol f = extension || List.contains extension extensions)
        |> Option.map (fun (f, _, _, _) -> f)

    let private ofMime (mime: string) : Format option =
        registry
        |> List.tryFind (fun (_, m, synonyms, _) ->
            String.Equals(m, mime, StringComparison.OrdinalIgnoreCase)
            || synonyms |> List.exists (fun s -> String.Equals(s, mime, StringComparison.OrdinalIgnoreCase)))
        |> Option.map (fun (f, _, _, _) -> f)

    /// `text/*` and `application/*` expand to every registered type under them, in registration order.
    let private expand (media: string) : Format list =
        match media with
        | "*/*" -> [ Any ]
        | "text/*" | "application/*" ->
            let prefix = media.Substring(0, media.Length - 1)
            registry
            |> List.filter (fun (_, m, synonyms, _) -> (m :: synonyms) |> List.exists (fun s -> s.Contains(prefix, StringComparison.Ordinal)))
            |> List.map (fun (f, _, _, _) -> f)
        | _ -> ofMime media |> Option.toList

    /// `Mime::Type.parse(accept)`, registered types only, in quality order (a stable sort: the header's order breaks ties).
    let parseAccept (header: string) : Result<Format list, Error> =
        match MediaTypeHeaderValue.TryParseList([| header |]) with
        | true, parsed when not (isNull parsed) ->
            // Rails gives a bare `*/*` no weight at all, the framework 1.0.
            let quality (m: MediaTypeHeaderValue) =
                match Option.ofNullable m.Quality with
                | Some q -> q
                | None -> if m.MediaType.Value = "*/*" then 0.0 else 1.0
            nonNull parsed
            |> Seq.toList
            |> List.sortWith (fun a b -> compare (quality b) (quality a))
            |> List.collect (fun m -> expand (m.MediaType.Value |> nonNull))
            |> List.distinct
            |> Ok
        | _ -> Result.Error UnknownFormat

    /// `BROWSER_LIKE_ACCEPTS = /,\s*\*\/\*|\*\/\*\s*,/`: a browser's list, which Rails does not negotiate.
    let private browserLike (accept: string) : bool =
        let compact = String(accept.ToCharArray() |> Array.filter (fun c -> not (Char.IsWhiteSpace c)))
        compact.Contains(",*/*", StringComparison.Ordinal) || compact.Contains("*/*,", StringComparison.Ordinal)

    /// `valid_accept_header`: the `Accept` header (or an XHR's content type) is what names the format.
    let private validAcceptHeader (request: HttpRequest) : bool =
        let accept = request.Headers.Accept.ToString().Trim()
        let xhr = String.Equals(request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
        (xhr && (accept <> "" || not (String.IsNullOrEmpty request.ContentType))) || (accept <> "" && not (browserLike accept))

    /// `request.should_apply_vary_header?`: the format came from the `Accept` header, not a format parameter.
    let shouldApplyVary (http: HttpContext) : bool = isNull (Params.value http "format") && validAcceptHeader http.Request

    /// `request.formats`: what the request asks for, most wanted first.
    let requested (http: HttpContext) : Result<Format list, Error> =
        let request = http.Request
        let accept = request.Headers.Accept.ToString().Trim()
        let xhr = String.Equals(request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase)
        match Params.value http "format" with
        | null ->
            if validAcceptHeader request then
                if accept = "" then
                    match request.ContentType with
                    | null -> Ok []
                    | ct ->
                        let media = (ct.Split [| ';'; ',' |]).[0].Trim()
                        Ok(ofMime media |> Option.toList)
                else
                    parseAccept accept
            else
                let path = request.Path.Value |> nonNull
                let fromPath =
                    match path.LastIndexOf '.' with
                    | -1 -> None
                    | dot -> ofExtension (path.Substring(dot + 1))
                Ok [ defaultArg fromPath (if xhr then Js else Html) ]
        | format -> Ok(ofExtension format |> Option.toList)

    /// `request.negotiate_mime(order)`: the first acceptable format the action offers.
    let negotiate (formats: Format list) (offered: Format list) : Format option =
        let rec go (rest: Format list) =
            match rest with
            | Any :: _ -> List.tryHead offered
            | priority :: rest -> if List.contains priority offered then Some priority else go rest
            | [] -> if List.contains Any offered then List.tryHead formats else None
        go formats

    /// `respond_to`: the format the action answers in, or `UnknownFormat` (406).
    let respondTo (http: HttpContext) (offered: Format list) : Result<Format, Error> =
        requested http
        |> Result.bind (fun formats ->
            match negotiate formats offered with
            | Some Any -> Ok(defaultArg (List.tryHead offered) Html)
            | Some format -> Ok format
            | None -> Result.Error UnknownFormat)

    /// `turbo_frame_request?`: a non-blank `Turbo-Frame` header.
    let isTurboFrame (http: HttpContext) : bool = not (String.IsNullOrWhiteSpace(http.Request.Headers["Turbo-Frame"].ToString()))
