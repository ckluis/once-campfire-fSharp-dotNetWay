// plans/design-kit-auth.md section 4, unit 2.9 (Web/Data.fs): the database as a Web action reaches it.
//
// The request's `CampfireDb` is the scoped context the host leased from the pooled factory (the
// `CampfireDb` service). Reads run on it where the action is (synchronous EF, E6); writes go through `Writes.run`, one at a time
// behind the `WriteGate`. A failure is the error `AppErrors.dbError` maps it to.
namespace Campfire.App.Web

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open System
open Campfire.App
open Campfire.Db

module Data =
    /// `c.app()`
    let app (http: HttpContext) : AppState = http.RequestServices.GetRequiredService<AppState>()

    /// `Time.current` on the harness's clock (frozen in the parity runs).
    let now (http: HttpContext) : System.DateTimeOffset = http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()

    /// The request's database context, with its connection opened once, on first use (P3): EF then neither opens nor closes it for
    /// each statement (every `Open` re-registers EF's SQL functions and takes the pool's lock), and the scope's disposal of the
    /// context closes it, back to the pool. Lazy, not at the scope's creation: a WebSocket request's scope lives as long as the
    /// socket, and a Cable or job scope never comes through here. `OpenConnection` counts, so the hybrid reads' own open/close pair
    /// (`FastReads`) and a transaction's nest inside it.
    let db (http: HttpContext) : Campfire.Db.Ef.CampfireDb =
        let db = http.RequestServices.GetRequiredService<Campfire.Db.Ef.CampfireDb>()
        if db.Database.GetDbConnection().State <> System.Data.ConnectionState.Open then db.Database.OpenConnection()
        db

    // P4 (unit 10.3): reads that repeat inside one request are made once. The memo lives in the request's `Items`; a write (`tryWrite`) drops it,
    // so a read after a write sees the write. Keys are a tag and two ids.
    [<Sealed>]
    type private Memo() =
        member val Values = System.Collections.Generic.Dictionary<struct (int * int64 * int64), obj>()

    type private Held<'T>(value: 'T) =
        member _.Value = value

    let private memoKey = obj ()

    let private memo (http: HttpContext) (tag: int) (a: int64) (b: int64) (read: unit -> 'T) : 'T =
        let memo =
            match http.Items.TryGetValue memoKey with
            | true, (:? Memo as memo) -> memo
            | _ ->
                let memo = Memo()
                http.Items[memoKey] <- memo
                memo
        match memo.Values.TryGetValue(struct (tag, a, b)) with
        | true, (:? Held<'T> as held) -> held.Value
        | _ ->
            let value = read ()
            memo.Values[struct (tag, a, b)] <- Held value
            value

    let private forget (http: HttpContext) : unit = http.Items.Remove memoKey |> ignore

    /// `Current.account` (`Account.first`), read once a request.
    let account (http: HttpContext) (db: Campfire.Db.Ef.CampfireDb) : Account option = memo http 1 0L 0L (fun () -> Account.first db)

    /// `Current.user.rooms.find_by(id:)`, read once a request for a (user, room) pair: `set_room` and `last_room_visited` meet here.
    let roomForUser (http: HttpContext) (db: Campfire.Db.Ef.CampfireDb) (userId: int64) (roomId: int64) : Room option =
        memo http 2 userId roomId (fun () -> Room.findForUser db userId roomId)

    /// `last_room_visited` on a read that is already on the request's context, for the user and the `last_room` cookie's id: the cookie's
    /// room if the user is in it, else `Current.user.rooms.original`.
    let lastRoomVisited (http: HttpContext) (db: Campfire.Db.Ef.CampfireDb) (userId: int64) (lastRoom: int64 option) : Room option =
        let found =
            match lastRoom with
            | Some roomId -> roomForUser http db userId roomId
            | None -> None
        match found with
        | Some room -> Some room
        | None -> memo http 3 userId 0L (fun () -> Room.originalForUser db userId)

    /// A database failure as a Web error: a missing record is `NotFound` (what `rescue_from RecordNotFound` matches), the rest is
    /// the error `AppErrors.dbError` maps it to.
    let webError (error: DbError) : Error =
        match error with
        | RecordNotFound _ -> NotFound
        | other -> AppErrors.dbError other

    /// `Reads.run` over the request's context.
    let read (http: HttpContext) (f: Campfire.Db.Ef.CampfireDb -> 'T) : Result<'T, Error> =
        Campfire.Db.Ef.Reads.run (db http) f |> Result.mapError webError

    /// `Writes.run` over the request's context, the failure left as a `DbError` for actions that rescue one
    /// (`ActiveRecord::RecordNotUnique`).
    let tryWrite (http: HttpContext) (f: Campfire.Db.Ef.Tx -> 'T) : Task<Result<'T, DbError>> =
        let app = app http
        task {
            let! result = Campfire.Db.Ef.Writes.run app.Gate app.Env (db http) f
            forget http
            return result
        }

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

    /// `read` for a read that awaits (a page's fragments are fetched up front while the request's context is in use).
    let readAsync (http: HttpContext) (f: Campfire.Db.Ef.CampfireDb -> Task<'T>) : Task<Result<'T, Error>> =
        task {
            let! result = Campfire.Db.Ef.Reads.runAsync (db http) f
            return result |> Result.mapError webError
        }

    /// Runs `f` with a presenter on the request's context and host (`MessagesController#present`).
    let present (http: HttpContext) (f: Campfire.App.Presenters.Presenter -> 'T) : Result<'T, Error> =
        let app = app http
        let requestHost = Some http.Request.Host.Host
        read http (fun db -> f (Campfire.App.Presenters.Presenter(db, app, requestHost)))

    /// `present` for what awaits the fragment cache (`Presenter.Messages`, `Presenter.MessageItem`).
    let presentAsync (http: HttpContext) (f: Campfire.App.Presenters.Presenter -> Task<'T>) : Task<Result<'T, Error>> =
        let app = app http
        let requestHost = Some http.Request.Host.Host
        readAsync http (fun db -> f (Campfire.App.Presenters.Presenter(db, app, requestHost)))
