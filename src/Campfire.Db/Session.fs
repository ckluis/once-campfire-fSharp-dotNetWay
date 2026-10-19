// Port of rust/crates/db/src/models/session.rs on EF Core (design section 4, unit 1.3).
// `reference/app/models/session.rb`
namespace Campfire.Db

open System
open System.Linq
open Microsoft.EntityFrameworkCore
open Campfire.Db.Ef

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Session =
    /// `Session::ACTIVITY_REFRESH_RATE`
    let activityRefreshRate : TimeSpan = TimeSpan.FromHours 1.0

    let find (db: CampfireDb) (id: int64) : Session = db.Sessions.Find id |> Data.found "Session"

    /// `Session.find_by(token:)` (the sign-in lookup of every authenticated request).
    let findByToken (db: CampfireDb) (token: string) : Session option =
        // `FromSql`: the lookup every authenticated request makes (unit 1.10: no expression tree to build).
        db.Sessions.FromSql($"SELECT * FROM sessions WHERE token = {token} LIMIT 1") |> Data.sqlFirst

    let forUser (db: CampfireDb) (userId: int64) : Session list =
        db.Sessions.Where(fun s -> s.UserId = EF.Parameter userId).ToList() |> List.ofSeq

    let countForUser (db: CampfireDb) (userId: int64) : int64 =
        db.Sessions.Where(fun s -> s.UserId = EF.Parameter userId).LongCount()

    /// `user.sessions.start!(user_agent:, ip_address:)`: a new 24-character base58 `has_secure_token`, and
    /// `last_active_at ||= Time.now` in `before_create`.
    let start (tx: Tx) (userId: int64) (userAgent: string option) (ipAddress: string option) : Session =
        let now = tx.Now()
        Data.insert
            tx.Db
            { Id = 0L
              UserId = userId
              Token = Tokens.base58 24
              IpAddress = Option.toObj ipAddress
              UserAgent = Option.toObj userAgent
              LastActiveAt = now
              CreatedAt = now
              UpdatedAt = now }

    /// Whether `resume` would refresh the session at `now`: its activity is over an hour old.
    let needsResume (session: Session) (now: DateTime) : bool = session.LastActiveAt < now - activityRefreshRate

    /// `resume`: refreshes activity, user agent and IP at most once an hour.
    let resume (tx: Tx) (session: Session) (userAgent: string option) (ipAddress: string option) : Session =
        let now = tx.Now()
        if not (needsResume session now) then
            session
        else
            Data.update
                tx.Db
                session
                { session with
                    UserAgent = Option.toObj userAgent
                    IpAddress = Option.toObj ipAddress
                    LastActiveAt = now }

    /// `destroy!`
    let destroy (tx: Tx) (session: Session) : unit = Data.remove tx.Db session
