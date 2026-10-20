// plans/design-kit-auth.md section 3 (Web/Current.fs): Rails' `Current`, one per request, in `HttpContext.Features`.
//
// `AuthenticatedBy` is the one `Current.fs` (App) already defines.
namespace Campfire.App.Web

open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.Db

[<Sealed; AllowNullLiteral>]
type Current() =
    member val Session: Session option = None with get, set
    member val User: User option = None with get, set
    member val AuthenticatedBy = Anonymous with get, set
    member val Platform: ApplicationPlatform option = None with get, set
    /// A database failure while the cookie scheme looked the session up (the scheme can only reject; `Authentication.restore` reports it).
    member val LookupError: Error option = None with get, set

module Current =
    /// This request's `Current`, made on first use.
    let get (http: HttpContext) : Current =
        match http.Features.Get<Current>() with
        | null ->
            let created = Current()
            http.Features.Set<Current> created
            created
        | existing -> existing

    let user (http: HttpContext) : User option = (get http).User

    let signedIn (http: HttpContext) : bool = (get http).User.IsSome

    /// `Current.user` where a before-action guarantees one.
    let requireUser (http: HttpContext) : Result<User, Error> =
        match user http with
        | Some user -> Ok user
        | None -> Result.Error(Internal(exn "no Current.user"))
