// plans/design-kit-auth.md section 3 (Web/Params.fs): Rails' `params` over the framework's own readers.
//
// Route values, the framework's form (`Request.Form`, already parsed once by the method override for a POST, and
// by `load` for any other method), the query string, and a JSON body read once. Precedence is Rails':
// path parameters over the body over the query, and a repeated key keeps its last value. Bracket names are
// flat keys (`message[body]`, `ids[]`); a JSON body reads the same names as properties. Not reproduced (class B,
// README Known differences): Rack's type-conflict errors (`a=1&a[]=2`), its depth limit, and the grouping of
// `a[][b]` pairs that interleave other keys.
namespace Campfire.App.Web

open System
open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

/// What `params[:k]` holds, as far as a caller can tell: nothing, a string, or something else (a hash, an array, a
/// number, a boolean), which Rails' `String` methods fail on.
type ParamValue =
    | Absent
    | Str of string
    | Structured

/// A hash parameter (`params[:message]`): its scalar and array members by name.
type Scope =
    { Name: string
      Get: string -> ParamValue
      Many: string -> string list }

module Params =
    /// Where the kit's `dispatch` leaves a JSON body it has read, for a handler that runs after it (until 2.13).
    [<Literal>]
    let JsonBodyKey = Campfire.Kit.Adapter.JsonBodyItemKey

    let private wrapKey = "campfire.web.wrap-parameters"

    let private isJson (http: HttpContext) : bool =
        match http.Request.ContentType with
        | null -> false
        | ct ->
            let media = (match ct.IndexOf ';' with -1 -> ct | i -> ct.Substring(0, i)).Trim().ToLowerInvariant()
            media = "application/json" || media = "text/x-json" || media = "application/jsonrequest" || media = "application/problem+json"

    /// Make the body readable by the synchronous accessors: the form is read (the framework caches it), a JSON body is
    /// read into memory once and kept. A form the framework cannot read is a 400, as in Rails.
    let load (http: HttpContext) : Task<Result<unit, Error>> =
        task {
            try
                if http.Request.HasFormContentType then
                    let! _ = http.Request.ReadFormAsync()
                    return Ok()
                elif isJson http && not (http.Items.ContainsKey JsonBodyKey) then
                    use buffer = new MemoryStream()
                    do! http.Request.Body.CopyToAsync buffer
                    http.Items[JsonBodyKey] <- buffer.ToArray()
                    return Ok()
                else
                    return Ok()
            with
            | :? InvalidDataException as e -> return Result.Error(BadRequest e.Message)
        }

    /// The JSON body's root, parsed once; `Error` when it is not JSON.
    let private jsonRoot (http: HttpContext) : Result<JsonElement voption, string> =
        match http.Items.TryGetValue "campfire.web.params.json" with
        | true, (:? Result<JsonElement voption, string> as cached) -> cached
        | _ ->
            let parsed =
                match http.Items.TryGetValue JsonBodyKey with
                | true, (:? (byte[]) as bytes) when bytes.Length > 0 ->
                    try
                        Ok(ValueSome(JsonDocument.Parse(bytes).RootElement))
                    with :? JsonException as e ->
                        Result.Error e.Message
                | _ -> Ok ValueNone
            http.Items["campfire.web.params.json"] <- box parsed
            parsed

    /// A body that is JSON and not JSON is Rails' `ParseError`: 400.
    let validate (http: HttpContext) : Result<unit, Error> =
        match jsonRoot http with
        | Ok _ -> Ok()
        | Result.Error message -> Result.Error(BadRequest message)

    let private jsonValue (element: JsonElement) : ParamValue =
        match element.ValueKind with
        | JsonValueKind.String -> Str(element.GetString() |> nonNull)
        | JsonValueKind.Null | JsonValueKind.Undefined -> Absent
        | _ -> Structured

    let private jsonProperty (root: JsonElement) (name: string) : JsonElement voption =
        match root.ValueKind with
        | JsonValueKind.Object ->
            // A repeated name keeps its last value, as a Ruby hash does.
            let mutable found = ValueNone
            for p in root.EnumerateObject() do
                if p.NameEquals name then found <- ValueSome p.Value
            found
        | _ -> ValueNone

    let private form (http: HttpContext) : IFormCollection =
        if http.Request.HasFormContentType then
            try
                http.Request.Form
            with :? InvalidDataException ->
                FormCollection(null) :> IFormCollection
        else
            FormCollection(null) :> IFormCollection

    let private last (values: StringValues) : string = values[values.Count - 1] |> nonNull

    /// A flat key in the body, then the query: the string, `Structured` when only `key[...]` names exist, or `Absent`.
    let private flat (http: HttpContext) (key: string) : ParamValue =
        let lookup (get: string -> StringValues) (names: string seq) =
            match get key with
            | v when v.Count > 0 -> Str(last v)
            | _ -> if names |> Seq.exists (fun n -> n.StartsWith(key + "[", StringComparison.Ordinal)) then Structured else Absent
        let form = form http
        // An uploaded file is a name too (`user[avatar]` alone makes `params[:user]` a hash).
        let names = if form.Files.Count = 0 then form.Keys :> seq<string> else Seq.append form.Keys (form.Files |> Seq.map (fun f -> f.Name))
        match lookup (fun k -> form[k]) names with
        | Absent -> lookup (fun k -> http.Request.Query[k]) (http.Request.Query.Keys :> seq<string>)
        | found -> found

    let private wrapped (http: HttpContext) : (string * string list voption) option =
        match http.Items.TryGetValue wrapKey with
        | true, (:? (string * string list voption) as w) -> Some w
        | _ -> None

    /// `wrap_parameters format: [:json]`: for a JSON request, the body's top-level keys are also readable under `key`
    /// (only those in `include` when the model has attributes), unless the body already has `key`.
    let wrap (http: HttpContext) (key: string) (``include``: string list voption) : unit =
        if isJson http then http.Items[wrapKey] <- box (key, ``include``)

    /// `params[:k]`: route values over the JSON or form body over the query.
    let lookup (http: HttpContext) (key: string) : ParamValue =
        let route =
            match http.Request.RouteValues.TryGetValue key with
            | true, null -> Absent
            | true, value -> Str(Convert.ToString(value, Globalization.CultureInfo.InvariantCulture) |> nonNull)
            | _ -> Absent
        if route <> Absent then
            route
        else
            match jsonRoot http with
            | Ok(ValueSome root) ->
                match jsonProperty root key with
                | ValueSome element -> jsonValue element
                | ValueNone -> Absent
            | _ ->
                match flat http key with
                | Absent -> Absent
                | found -> found

    /// `params[:k]` as a string; `null` when it is absent or not a string.
    let value (http: HttpContext) (key: string) : string | null =
        match lookup http key with
        | Str s -> s
        | _ -> null

    /// `params[:a][:b]`: the form name `a[b]`, or the JSON property `b` of the object `a` (or of the body itself when
    /// `wrap` named `a`).
    let nested (http: HttpContext) (outer: string) (inner: string) : string | null =
        match jsonRoot http with
        | Ok(ValueSome root) ->
            let inObject =
                match jsonProperty root outer with
                | ValueSome obj -> jsonProperty obj inner
                | ValueNone ->
                    match wrapped http with
                    | Some(key, include') when key = outer && (match include' with ValueSome names -> List.contains inner names | ValueNone -> inner <> "authenticity_token" && inner <> "_method" && inner <> "utf8") ->
                        jsonProperty root inner
                    | _ -> ValueNone
            match inObject with
            | ValueSome element -> (match jsonValue element with Str s -> s | _ -> null)
            | ValueNone -> null
        | _ ->
            match flat http $"{outer}[{inner}]" with
            | Str s -> s
            | _ -> null

    let private jsonStrings (element: JsonElement) : string list =
        if element.ValueKind = JsonValueKind.Array then
            [ for e in element.EnumerateArray() do
                  match jsonValue e with
                  | Str s -> s
                  | _ -> () ]
        else
            []

    /// `params[:k]` as an array: the repeated `k[]` names, or a JSON array of strings.
    let values (http: HttpContext) (key: string) : string list =
        match jsonRoot http with
        | Ok(ValueSome root) ->
            match jsonProperty root key with
            | ValueSome element -> jsonStrings element
            | ValueNone -> []
        | _ ->
            let fromForm = (form http)[$"{key}[]"]
            let strings = if fromForm.Count > 0 then fromForm else http.Request.Query[$"{key}[]"]
            [ for s in strings -> s |> nonNull ]

    /// `params[:a][:b]` as a hash of scalars (`permit(b: {})`): the members `a[b][k]` of the form or query, or the properties of a
    /// JSON object, by name and as text. `None` when `b` is not a hash.
    let hashMembers (http: HttpContext) (outer: string) (inner: string) : (string * string) list option =
        let text (element: JsonElement) : string =
            match element.ValueKind with
            | JsonValueKind.String -> element.GetString() |> nonNull
            | JsonValueKind.Null | JsonValueKind.Undefined -> ""
            | JsonValueKind.True -> "true"
            | JsonValueKind.False -> "false"
            | _ -> element.GetRawText()
        match jsonRoot http with
        | Ok(ValueSome root) ->
            match jsonProperty root outer with
            | ValueSome obj ->
                match jsonProperty obj inner with
                | ValueSome members when members.ValueKind = JsonValueKind.Object ->
                    Some [ for p in members.EnumerateObject() -> p.Name, text p.Value ]
                | _ -> None
            | ValueNone -> None
        | _ ->
            let prefix = $"{outer}[{inner}]["
            let members (keys: string seq) (get: string -> StringValues) =
                [ for key in keys do
                      if key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith("]", StringComparison.Ordinal) then
                          let name = key.Substring(prefix.Length, key.Length - prefix.Length - 1)
                          if name.Length > 0 && not (name.Contains '[') && not (name.Contains ']') then
                              yield name, last (get key) ]
            let form = form http
            match members form.Keys (fun k -> form[k]) with
            | [] ->
                match members http.Request.Query.Keys (fun k -> http.Request.Query[k]) with
                | [] -> None
                | found -> Some found
            | found -> Some found

    /// `ActionController::Parameters#present?` for a scalar: not blank.
    let isPresent (value: ParamValue) : bool =
        match value with
        | Absent -> false
        | Str s -> not (String.IsNullOrWhiteSpace s)
        | Structured -> true

    let private scopeOf (http: HttpContext) (name: string) : Scope =
        { Name = name
          Get =
            fun inner ->
                match nested http name inner with
                | null ->
                    // A member that is an object or array is `Structured`.
                    let isStructured =
                        match jsonRoot http with
                        | Ok(ValueSome root) ->
                            match jsonProperty root name with
                            | ValueSome obj -> (match jsonProperty obj inner with ValueSome e -> jsonValue e = Structured | ValueNone -> false)
                            | ValueNone -> false
                        | _ -> (flat http $"{name}[{inner}]") = Structured
                    if isStructured then Structured else Absent
                | s -> Str s
          Many =
            fun inner ->
                match jsonRoot http with
                | Ok(ValueSome root) ->
                    match jsonProperty root name with
                    | ValueSome obj -> (match jsonProperty obj inner with ValueSome e -> jsonStrings e | ValueNone -> [])
                    | _ -> []
                | _ -> values http $"{name}[{inner}]" }

    /// `params.require(:name)`: the hash under `name`; a missing or blank one is `ParameterMissing` (400).
    let require (http: HttpContext) (name: string) : Result<Scope, Error> =
        let present =
            match lookup http name with
            | Structured -> true
            | _ -> false
        let wrappedBody =
            match wrapped http, jsonRoot http with
            | Some(key, _), Ok(ValueSome root) when key = name -> root.ValueKind = JsonValueKind.Object && root.GetPropertyCount() > 0
            | _ -> false
        if present || wrappedBody then Ok(scopeOf http name) else Result.Error(ParameterMissing name)

    /// `params.require(:name)` for a scalar: a blank string is `ParameterMissing` too.
    let requireString (http: HttpContext) (name: string) : Result<string, Error> =
        match lookup http name with
        | Str s when not (String.IsNullOrWhiteSpace s) -> Ok s
        | _ -> Result.Error(ParameterMissing name)

    /// `permit(*keys)`: the named scalar members, by name; anything else (nested hashes, undeclared keys) is dropped.
    let permit (scope: Scope) (keys: string list) : Map<string, string> =
        keys
        |> List.choose (fun key ->
            match scope.Get key with
            | Str s -> Some(key, s)
            | _ -> None)
        |> Map.ofList
