// plans/design-kit-auth.md section 4, unit 2.9 (Web/Data.fs): the database as a Web action reaches it.
//
// The request's `CampfireDb` is the scoped context the host leased from the pooled factory (`Ctx.Db` is the same
// service). Reads run on it where the action is (synchronous EF, E6); writes go through `Writes.run`, one at a time
// behind the `WriteGate`. A failure is the `Legacy` error `AppErrors.dbError` maps it to, until unit 2.13.
namespace Campfire.App.Web

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Campfire.App
open Campfire.Db

module Data =
    /// `c.app()`
    let app (http: HttpContext) : AppState = http.RequestServices.GetRequiredService<AppState>()

    /// `Time.current` on the harness's clock (frozen in the parity runs).
    let now (http: HttpContext) : System.DateTimeOffset = http.RequestServices.GetRequiredService<Campfire.Kit.Kit>().Clock.Now()

    /// The request's database context.
    let db (http: HttpContext) : Campfire.Db.Ef.CampfireDb = http.RequestServices.GetRequiredService<Campfire.Db.Ef.CampfireDb>()

    /// A database failure as a Web error: a missing record is `NotFound` (what `rescue_from RecordNotFound` matches), the rest is
    /// the `Legacy` error `AppErrors.dbError` maps it to.
    let webError (error: DbError) : Error =
        match error with
        | RecordNotFound _ -> NotFound
        | other -> Legacy(AppErrors.dbError other)

    /// `Reads.run` over the request's context.
    let read (http: HttpContext) (f: Campfire.Db.Ef.CampfireDb -> 'T) : Result<'T, Error> =
        Campfire.Db.Ef.Reads.run (db http) f |> Result.mapError webError

    /// `Writes.run` over the request's context, the failure left as a `DbError` for actions that rescue one
    /// (`ActiveRecord::RecordNotUnique`).
    let tryWrite (http: HttpContext) (f: Campfire.Db.Ef.Tx -> 'T) : Task<Result<'T, DbError>> =
        let app = app http
        Campfire.Db.Ef.Writes.run app.Gate app.Env (db http) f

    /// `Writes.run` over the request's context: the gate, the transaction, the after-commit queue.
    let write (http: HttpContext) (f: Campfire.Db.Ef.Tx -> 'T) : Task<Result<'T, Error>> =
        task {
            let! result = tryWrite http f
            return result |> Result.mapError webError
        }

    /// `password=`: BCrypt on the thread pool, `None` for no password.
    let passwordDigest (http: HttpContext) (password: string option) : Task<Result<PasswordDigest option, Error>> =
        task {
            match password with
            | None -> return Ok None
            | Some password ->
                try
                    let! digest = PasswordDigest.hash password (app http).Env.BcryptCost
                    return Ok(Some digest)
                with e ->
                    return Result.Error(Internal e)
        }

    /// Runs `f` with a presenter on the request's context and host (`MessagesController#present`).
    let present (http: HttpContext) (f: Campfire.App.Presenters.Presenter -> 'T) : Result<'T, Error> =
        let app = app http
        let requestHost = Some http.Request.Host.Host
        read http (fun db ->
            let presenter = Campfire.App.Presenters.Presenter(db, app, requestHost)
            // The Jbuilder partials (`json.cache!`) read the fragment cache on this thread.
            Campfire.Views.FragmentCache.withCache app.FragmentCache (fun () -> f presenter))
