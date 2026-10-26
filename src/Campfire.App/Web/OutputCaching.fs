// plans/design-front.md F9 (unit 7.2): the response cache of the front server (Thruster's `cache`) is ASP.NET Core's OutputCache, with
// one base policy per route family that Rails marks `public`. A family matches its route's path shape exactly, so a page never meets
// the cache: no lookup, no lock, no buffering. Every other request passes untouched.
namespace Campfire.App.Web

open System
open System.Text.RegularExpressions
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.OutputCaching

module OutputCaching =
    // The controllers' own constants (they read these, so a lifetime cannot drift from the cache's).
    /// `expires_in 30.minutes, public: true` (`Users::AvatarsController#show`).
    [<Literal>]
    let AvatarMaxAge = 1800UL
    /// `expires_in 5.minutes, public: true` (`Accounts::LogosController#show`).
    [<Literal>]
    let LogoMaxAge = 300UL
    /// `expires_in 1.year, public: true` (`QrCodeController#show`).
    [<Literal>]
    let QrCodeMaxAge = 31_556_952UL
    /// `http_cache_forever`: `expires_in 100.years` (the Active Storage proxy controllers).
    [<Literal>]
    let HundredYears = 3_155_695_200UL
    /// The disk service's `show`: `Cache-Control: max-age=3600, public`.
    [<Literal>]
    let DiskMaxAge = 3600UL

    let private pattern (text: string) = Regex(text, RegexOptions.Compiled ||| RegexOptions.CultureInvariant ||| RegexOptions.NonBacktracking)

    // A route segment (`:id`) is non-empty and has no `/` or `.`; `*filename` is one or more segments; `(.:format)` is optional.
    let private avatar = pattern @"\A/users/[^/.]+/avatar(\.[^/.]+)?\z"
    let private logo = pattern @"\A/account/logo(\.[^/.]+)?\z"
    let private qrCode = pattern @"\A/qr_code/[^/.]+(\.[^/.]+)?\z"
    let private proxy =
        pattern @"\A/rails/active_storage/(blobs/proxy/[^/]+|representations/proxy/[^/]+/[^/]+)/[^/]+(/[^/]*)*\z"
    let private diskShow = pattern @"\A/rails/active_storage/disk/[^/]+/[^/]+(/[^/]*)*\z"

    let private under (prefix: string) (regex: Regex) (path: PathString) : bool =
        match path.Value with
        | null -> false
        | value -> value.StartsWith(prefix, StringComparison.Ordinal) && regex.IsMatch value

    /// Rails' public routes: a name, the path-shape predicate (case-sensitive, as the cache's keys are) and the controller's max-age.
    let families: (string * (PathString -> bool) * TimeSpan) list =
        [ "avatar", under "/users/" avatar, TimeSpan.FromSeconds(float AvatarMaxAge)
          "account logo", under "/account/" logo, TimeSpan.FromSeconds(float LogoMaxAge)
          "qr code", under "/qr_code/" qrCode, TimeSpan.FromSeconds(float QrCodeMaxAge)
          "active storage proxy", under "/rails/active_storage/" proxy, TimeSpan.FromSeconds(float HundredYears)
          "active storage disk", under "/rails/active_storage/disk/" diskShow, TimeSpan.FromSeconds(float DiskMaxAge) ]

    let private noRange (http: HttpContext) : bool = not (http.Request.Headers.ContainsKey "Range")

    /// `CACHE_SIZE` and `MAX_CACHE_ITEM_SIZE`; one base policy per family: its path shape, no `Range`, the controller's lifetime, one
    /// entry per `Accept-Encoding` (the entry is the encoded response: the cache sits outside compression).
    let configure (cacheSize: int64) (maxItemSize: int64) (options: OutputCacheOptions) : unit =
        options.UseCaseSensitivePaths <- true
        options.SizeLimit <- cacheSize
        options.MaximumBodySize <- maxItemSize
        for (_, matches, maxAge) in families do
            options.AddBasePolicy(fun (builder: OutputCachePolicyBuilder) ->
                builder
                    .With(Func<OutputCacheContext, bool>(fun context -> matches context.HttpContext.Request.Path && noRange context.HttpContext))
                    .Expire(maxAge)
                    .SetVaryByHeader("Accept-Encoding")
                |> ignore)
