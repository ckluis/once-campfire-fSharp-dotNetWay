// plans/design-kit-auth.md section 4, unit 2.13 (Web/Outgoing.fs): a response written the way Puma writes a Rack response.
//
// The Active Storage endpoints and the public files answer with headers whose values come from the request or the filesystem
// (`?disposition=` on a proxied blob) and with a file, a range of one or a stream for a body. A header value holding a line
// break goes out one line per header line, and a line holding any other control character is left out (Puma's
// `str_headers`); the framework's own header collection refuses such values. Everything else answers through `Pages`,
// `Respond` and the framework.
namespace Campfire.App.Web

open System
open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives
open Campfire.App

/// A response's headers (`http::HeaderMap`): case-insensitive names, any number of values for a
/// name. A response has about ten, so this is arrays scanned in order, not a dictionary.
[<Sealed; AllowNullLiteral>]
type HeaderMap() =
    let mutable names: string[] = Array.zeroCreate 8
    let mutable values: string[] = Array.zeroCreate 8
    let mutable count = 0

    static let sameName (a: string) (b: string) = String.Equals(a, b, StringComparison.OrdinalIgnoreCase)

    member _.Count = count

    member _.NameAt(i: int) : string = names[i]
    member _.ValueAt(i: int) : string = values[i]

    member _.IndexOf(name: string) : int =
        let mutable found = -1
        let mutable i = 0
        while found < 0 && i < count do
            if sameName names[i] name then found <- i
            i <- i + 1
        found

    member this.Contains(name: string) : bool = this.IndexOf name >= 0

    /// The first value for `name`, null when there is none.
    member this.Get(name: string) : string | null =
        match this.IndexOf name with
        | -1 -> null
        | i -> values[i]

    /// Add a value after those `name` already has.
    member _.Append(name: string, value: string) : unit =
        if count = names.Length then
            Array.Resize(&names, count * 2)
            Array.Resize(&values, count * 2)
        names[count] <- name
        values[count] <- value
        count <- count + 1

    member this.Remove(name: string) : unit =
        let mutable kept = 0
        for i in 0 .. count - 1 do
            if not (sameName names[i] name) then
                names[kept] <- names[i]
                values[kept] <- values[i]
                kept <- kept + 1
        for i in kept .. count - 1 do
            names[i] <- Unchecked.defaultof<string>
            values[i] <- Unchecked.defaultof<string>
        count <- kept

    /// Replace every value `name` has with `value` (`HeaderMap::insert`).
    member this.Insert(name: string, value: string) : unit =
        match this.IndexOf name with
        | -1 -> this.Append(name, value)
        | first ->
            names[first] <- name
            values[first] <- value
            let mutable kept = first + 1
            for i in first + 1 .. count - 1 do
                if not (sameName names[i] name) then
                    names[kept] <- names[i]
                    values[kept] <- values[i]
                    kept <- kept + 1
            for i in kept .. count - 1 do
                names[i] <- Unchecked.defaultof<string>
                values[i] <- Unchecked.defaultof<string>
            count <- kept

    /// Every value for `name`, in order.
    member this.GetAll(name: string) : string list =
        [ for i in 0 .. count - 1 do
              if sameName names[i] name then
                  values[i] ]

    /// `HeaderValue::from_str`: no control characters except tab, and no DEL.
    static member IsValidValue(value: string) : bool =
        let mutable ok = true
        let mutable i = 0
        while ok && i < value.Length do
            let c = value[i]
            if (c < ' ' && c <> '\t') || c = '\127' then ok <- false
            i <- i + 1
        ok

    /// Replaces `name` with `value` the way Puma writes a Rack header (puma 7.2.1,
    /// `Puma::Request#str_headers`). A value with a line break goes out one line per header line
    /// (`split("\n")`, which drops trailing empty lines), and a line holding any other control
    /// character (`ILLEGAL_HEADER_VALUE_REGEX`) is left out. Values come from the request at times,
    /// like `?disposition=` on a proxied blob. Puma writes a DEL, which Thruster then refuses with a
    /// 502; that line is left out too.
    member this.SetPumaValue(name: string, value: string) : unit =
        if HeaderMap.IsValidValue value then
            this.Insert(name, value)
        else
            this.Remove name
            let lines = value.Split '\n'
            let mutable last = lines.Length
            while last > 0 && lines[last - 1] = "" do
                last <- last - 1
            for i in 0 .. last - 1 do
                if HeaderMap.IsValidValue lines[i] then this.Append(name, lines[i])

/// A file (or a byte range of one) to stream from disk.
type FileBody = { Path: string; Offset: int64; Len: int64 }

type OutgoingBody =
    | NoBody
    | InMemory of ReadOnlyMemory<byte>
    | OnDisk of FileBody

[<Sealed; AllowNullLiteral>]
type Outgoing(status: int) =
    member val Status: int = status with get, set
    member val Headers: HeaderMap = HeaderMap() with get, set
    member val Body: OutgoingBody = NoBody with get, set

    /// Marks a response `ActionDispatch::Static` served (a public file or an asset): the middleware
    /// below `Static` in the reference (`Rack::Runtime`, `ActionDispatch::RequestId`) never saw it.
    member val StaticFile: bool = false with get, set

    member this.ContentType(contentType: string) : Outgoing = this.Header("content-type", contentType)

    /// Set (replace) a header, as Puma would write the value (see `HeaderMap.SetPumaValue`).
    member this.Header(name: string, value: string) : Outgoing =
        this.Headers.SetPumaValue(name, value)
        this

module Outgoing =
    /// Puts `map` on the response the way Puma writes it: a header line with nothing after its colon is one Puma writes (the first line
    /// of a `Content-Disposition` that begins with a line break), which `Append` would drop, so the values of a name that has one go
    /// out together, as a list.
    let applyHeaders (http: HttpContext) (map: HeaderMap) : unit =
        let headers = http.Response.Headers
        let mutable empty = false
        for i in 0 .. map.Count - 1 do
            if map.ValueAt i = "" then empty <- true
        if not empty then
            for i in 0 .. map.Count - 1 do
                headers.Append(map.NameAt i, StringValues(map.ValueAt i))
        else
            let written = Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            for i in 0 .. map.Count - 1 do
                let name = map.NameAt i
                if written.Add name then headers.Append(name, StringValues(Array.ofList (map.GetAll name)))

    let private copyHeaders (http: HttpContext) (response: Outgoing) : unit =
        http.Response.StatusCode <- response.Status
        applyHeaders http response.Headers

    /// Hand a finished response to the host, streaming files and dropping HEAD bodies (`Rack::Head`) while keeping their
    /// `Content-Length`. A file that can't be opened is a bare 500, before any header goes out.
    let write (logger: Microsoft.Extensions.Logging.ILogger) (http: HttpContext) (response: Outgoing) : Task =
        task {
            let head = HttpMethods.IsHead http.Request.Method
            if response.StaticFile then (Edge.state http).SkipRequestHeaders <- true
            let unreadable =
                match response.Body with
                | OnDisk file -> not head && not (System.IO.File.Exists file.Path)
                | _ -> false
            if unreadable then
                match response.Body with
                | OnDisk file -> Microsoft.Extensions.Logging.LoggerExtensions.LogError(logger, "send_file failed path={Path}", file.Path)
                | _ -> ()
                http.Response.StatusCode <- 500
            else
                // The length the host will send: known for a buffer and a file.
                match response.Body with
                | InMemory bytes -> response.Headers.Insert("content-length", string bytes.Length)
                | OnDisk file -> response.Headers.Insert("content-length", string file.Len)
                | NoBody -> ()
                if head && Edge.headFromPost http then response.Headers.Remove "content-length"
                copyHeaders http response
                try
                    if not head then
                        match response.Body with
                        | NoBody -> ()
                        | InMemory bytes -> do! http.Response.Body.WriteAsync bytes
                        | OnDisk file -> do! http.Response.SendFileAsync(file.Path, file.Offset, Nullable file.Len)
                with
                // The client went away; the host has nothing more to say about it.
                | :? OperationCanceledException
                | :? IOException -> ()
        }

    /// The handler that writes `response`.
    let handler (response: Outgoing) : Falco.HttpHandler =
        fun http ->
            let logger = (Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<AppState>(http.RequestServices)).Loggers.CreateLogger "campfire"
            write logger http response
