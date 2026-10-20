// plans/design-kit-auth.md K6/K7 (unit 2.5): Rails' flash and `return_to_after_authenticating`, kept in ASP.NET Core's
// session (`AddSession` over the in-memory distributed cache, cookie `_campfire_flash`) instead of Rails' encrypted
// `_campfire_session` cookie, which is neither read nor written.
//
// `Flash` is `ActionDispatch::Flash::FlashHash` (set for the next request, swept at the end of the request that loads it,
// `flash.now`, `keep`). The `Flash` module loads it from the session and writes the survivors back; `ReturnTo` is the
// URL a sign-in returns to. Nothing touches the session unless the request carried its cookie or is writing to it, so
// a page that uses neither costs the store nothing.
namespace Campfire.App.Web

open System
open System.Collections.Generic
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Campfire.RailsCompat

/// Where the session lives for the request: its cookie and the keys the app keeps in it.
module SessionKeys =
    /// K6: not Rails' `_campfire_session`, so a person coming over from Rails logs no unprotect warning and Rails' cookie
    /// (and what it holds) survives a switch back.
    [<Literal>]
    let CookieName = "_campfire_flash"

    [<Literal>]
    let Flash = "flash"

    [<Literal>]
    let ReturnTo = "return_to_after_authenticating"

    /// `HttpContext.Items` mark: the session was written, so `SessionReset.commit` stores it before the response goes out.
    [<Literal>]
    let DirtyItem = "campfire.web.session.dirty"

    /// Whether the request carries the session's cookie, and so may have something stored.
    let hasCookie (http: HttpContext) : bool = http.Request.Cookies.ContainsKey CookieName

    /// The session for a write, marked so it is committed ahead of the response.
    let forWrite (http: HttpContext) : ISession =
        http.Items[DirtyItem] <- box true
        http.Session

/// `ActionDispatch::Flash::FlashHash`, stored in the session under `"flash"` as
/// `{ "discard" => [], "flashes" => { ... } }`.
[<Sealed>]
type Flash() =
    let flashes = ResizeArray<KeyValuePair<string, Value>>()
    let discarded = ResizeArray<string>()

    /// `FlashHash.from_session_value`: everything loaded is marked for discard at the end of this
    /// request, minus what the previous request had already discarded.
    static member FromSessionValue(value: Value voption) : Flash =
        let flash = Flash()
        match value with
        | ValueSome(Value.Object _ as stored) ->
            let alreadyDiscarded =
                match stored.TryGet "discard" with
                | Some(Value.Array items) -> [ for item in items do match item with Value.String s -> s | _ -> () ]
                | _ -> []
            match stored.TryGet "flashes" with
            | Some(Value.Object entries) ->
                for (k, v) in entries do
                    if not (List.contains k alreadyDiscarded) then
                        flash.AddLoaded(k, v)
            | _ -> ()
        | _ -> ()
        flash

    member private _.AddLoaded(key: string, value: Value) =
        flashes.Add(KeyValuePair(key, value))
        discarded.Add key

    /// `FlashHash#to_session_value`: ValueNone when nothing survives.
    member _.ToSessionValue() : Value voption =
        let keep = [ for kv in flashes do if not (discarded.Contains kv.Key) then kv.Key, kv.Value ]
        if keep.IsEmpty then
            ValueNone
        else
            ValueSome(Value.Object [ "discard", Value.Array []; "flashes", Value.Object keep ])

    member _.Get(key: string) : Value voption =
        match flashes.FindIndex(fun kv -> kv.Key = key) with
        | -1 -> ValueNone
        | i -> ValueSome flashes[i].Value

    member this.GetStr(key: string) : string | null =
        match this.Get key with
        | ValueSome(Value.String s) -> s
        | _ -> null

    /// `flash[key] = value`: shown on the next request.
    member _.Set(key: string, value: Value) : unit =
        discarded.RemoveAll(fun k -> k = key) |> ignore
        match flashes.FindIndex(fun kv -> kv.Key = key) with
        | -1 -> flashes.Add(KeyValuePair(key, value))
        | i -> flashes[i] <- KeyValuePair(key, value)

    member this.Set(key: string, value: string) : unit = this.Set(key, Value.String value)

    /// `flash.now[key] = value`: shown on this request only.
    member this.Now(key: string, value: Value) : unit =
        this.Set(key, value)
        this.Discard(ValueSome key)

    member this.Now(key: string, value: string) : unit = this.Now(key, Value.String value)

    member _.Keep(key: string voption) : unit =
        match key with
        | ValueSome key -> discarded.RemoveAll(fun k -> k = key) |> ignore
        | ValueNone -> discarded.Clear()

    member _.Discard(key: string voption) : unit =
        let keys =
            match key with
            | ValueSome key -> [ key ]
            | ValueNone -> [ for kv in flashes -> kv.Key ]
        for key in keys do
            if not (discarded.Contains key) then discarded.Add key

    member _.Delete(key: string) : unit =
        discarded.RemoveAll(fun k -> k = key) |> ignore
        flashes.RemoveAll(fun kv -> kv.Key = key) |> ignore

    /// Whether it was read from a stored value (so an emptied flash must remove it).
    member val FromSession = false with get, set

    member _.IsEmpty: bool = flashes.Count = 0

    member _.Keys: string list = [ for kv in flashes -> kv.Key ]

    member this.Notice: string | null = this.GetStr "notice"
    member this.Alert: string | null = this.GetStr "alert"
    member this.SetNotice(message: string) : unit = this.Set("notice", message)
    member this.SetAlert(message: string) : unit = this.Set("alert", message)


module Flash =
    /// `flash`, loaded from the session on first use: empty, without touching the store, for a request that carries no
    /// session cookie.
    let load (http: HttpContext) : Flash =
        if SessionKeys.hasCookie http && not (isNull (http.Features.Get<ISessionFeature>())) then
            match http.Session.GetString SessionKeys.Flash with
            | null -> Flash()
            | stored ->
                let flash =
                    match Json.parse (Encoding.UTF8.GetBytes stored) with
                    | Some value -> Flash.FromSessionValue(ValueSome value)
                    | None -> Flash()
                flash.FromSession <- true
                flash
        else
            Flash()

    /// `commit_flash`: what survives this request goes back to the session, and nothing is left behind when it is empty.
    let commit (http: HttpContext) (pending: Flash) : unit =
        if not pending.IsEmpty || pending.FromSession then
            match pending.ToSessionValue() with
            | ValueSome value -> (SessionKeys.forWrite http).SetString(SessionKeys.Flash, Json.encode value)
            | ValueNone -> if pending.FromSession then (SessionKeys.forWrite http).Remove SessionKeys.Flash

module ReturnTo =
    /// `session[:return_to_after_authenticating] = url`
    let set (http: HttpContext) (url: string) : unit =
        (SessionKeys.forWrite http).SetString(SessionKeys.ReturnTo, url)

    /// `session.delete(:return_to_after_authenticating)`
    let take (http: HttpContext) : string | null =
        if SessionKeys.hasCookie http && not (isNull (http.Features.Get<ISessionFeature>())) then
            match http.Session.GetString SessionKeys.ReturnTo with
            | null -> null
            | url ->
                (SessionKeys.forWrite http).Remove SessionKeys.ReturnTo
                url
        else
            null

module SessionReset =
    /// `reset_session`: nothing stays in the session, and the browser's cookie for it goes.
    let run (http: HttpContext) : unit =
        if SessionKeys.hasCookie http && not (isNull (http.Features.Get<ISessionFeature>())) then
            (SessionKeys.forWrite http).Clear()
            http.Response.Cookies.Delete(SessionKeys.CookieName, CookieOptions(Path = "/"))

    /// Stores what the request wrote to the session before the response starts (the session middleware would do it after
    /// the response, when a quick client has already asked again).
    let commit (http: HttpContext) : Task =
        if http.Items.ContainsKey SessionKeys.DirtyItem && not (isNull (http.Features.Get<ISessionFeature>())) then
            http.Session.CommitAsync()
        else
            Task.CompletedTask
