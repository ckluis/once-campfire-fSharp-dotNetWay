// Hybrid reads (plans/dotnetway.md; "Reads: hybrid EF Core + Dapper"; spike: bench/results/spike-dapper): the hottest per-request
// lookups with Dapper on the context's own connection. EF Core keeps the model, the schema, every write, every cold query and the
// interceptors; this module is the ONLY place Dapper is called (bin/verify guards it). Parameters only, never built SQL.
namespace Campfire.Db.Ef

open System
open System.Collections.Concurrent
open System.Data
open System.Threading
open Dapper
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Storage
open Campfire.Db

/// Active Record's text for a `datetime(6)` column, read by Dapper: the same `RailsTime` the EF converter uses.
type RailsDateTimeHandler() =
    inherit SqlMapper.TypeHandler<DateTime>()
    override _.Parse(value: obj) = RailsTime.ofDb (string value)
    override _.SetValue(parameter: IDbDataParameter, value: DateTime) =
        parameter.DbType <- DbType.String
        parameter.Value <- RailsTime.toDb value

module FastReads =
    // Dapper ignores a TypeHandler for an enum type, so the string-backed RoomType is read into this row and mapped by hand.
    [<CLIMutable>]
    type private RoomRow =
        { Id: int64
          Name: string | null
          Type: string
          CreatorId: int64
          CreatedAt: DateTime
          UpdatedAt: DateTime }

    // Process-wide Dapper settings (snake case, Rails' time text), set once when the first context options are built.
    let private configure =
        lazy
            (DefaultTypeMap.MatchNamesWithUnderscores <- true
             SqlMapper.AddTypeHandler(RailsDateTimeHandler()))

    let ensureConfigured () : unit = configure.Force()

    // The log hook: EF's command log does not see Dapper, so a sink registered for a database (its Data Source) is told each
    // statement's text (parameters are placeholders, never values). Tests register one; production registers none.
    let private sinks = ConcurrentDictionary<string, Action<string>>()
    let mutable private sinkCount = 0

    /// Lists every Dapper statement run against the database at `dataSource` (as `RelationalEventId.CommandExecuted` lists EF's).
    let logTo (dataSource: string) (sink: Action<string>) : unit =
        sinks[dataSource] <- sink
        Volatile.Write(&sinkCount, sinks.Count)

    let private note (db: CampfireDb) (sql: string) : unit =
        if Volatile.Read(&sinkCount) > 0 then
            let mutable sink = Unchecked.defaultof<Action<string>>
            if sinks.TryGetValue(db.Database.GetDbConnection().DataSource, &sink) then sink.Invoke sql

    /// Runs `f` on the context's connection, opened and closed through EF so its interceptors (the pragmas) run as for an EF query;
    /// inside a write the context's transaction is passed explicitly.
    let inline private onConnection (db: CampfireDb) (sql: string) ([<InlineIfLambda>] f: IDbConnection -> IDbTransaction | null -> 'T) : 'T =
        note db sql
        db.Database.OpenConnection()
        try
            let transaction =
                match db.Database.CurrentTransaction with
                | null -> null
                | t -> t.GetDbTransaction()
            f (db.Database.GetDbConnection()) transaction
        finally
            db.Database.CloseConnection()

    let private sessionSql = "SELECT id, user_id, token, ip_address, user_agent, last_active_at, created_at, updated_at FROM sessions WHERE token = @token LIMIT 1"
    let private userSql = "SELECT id, name, email_address, password_digest, role, status, bio, bot_token, created_at, updated_at FROM users WHERE id = @id LIMIT 1"
    let private accountSql = "SELECT id, name, join_code, custom_styles, settings, singleton_guard, created_at, updated_at FROM accounts ORDER BY id LIMIT 1"
    let private roomForUserSql =
        "SELECT rooms.id, rooms.name, rooms.type, rooms.creator_id, rooms.created_at, rooms.updated_at FROM rooms INNER JOIN memberships ON rooms.id = memberships.room_id WHERE memberships.user_id = @userId AND rooms.id = @roomId LIMIT 1"

    /// `Session.find_by(token:)`
    let sessionByToken (db: CampfireDb) (token: string) : Session option =
        onConnection db sessionSql (fun c t -> c.QueryFirstOrDefault<Session>(sessionSql, {| token = token |}, t) |> Option.ofObj)

    /// `User.find_by(id:)`
    let userById (db: CampfireDb) (id: int64) : User option =
        onConnection db userSql (fun c t -> c.QueryFirstOrDefault<User>(userSql, {| id = id |}, t) |> Option.ofObj)

    /// `Account.first`
    let accountFirst (db: CampfireDb) : Account option =
        onConnection db accountSql (fun c t -> c.QueryFirstOrDefault<Account>(accountSql, null, t) |> Option.ofObj)

    /// `user.rooms.find_by(id:)`: the room through the user's membership.
    let roomForUser (db: CampfireDb) (userId: int64) (roomId: int64) : Room option =
        onConnection db roomForUserSql (fun c t ->
            match c.QueryFirstOrDefault<RoomRow>(roomForUserSql, {| userId = userId; roomId = roomId |}, t) with
            | null -> None
            | r ->
                Some
                    { Id = r.Id
                      Name = r.Name
                      Type = Names.roomTypeOfDb r.Type
                      CreatorId = r.CreatorId
                      CreatedAt = r.CreatedAt
                      UpdatedAt = r.UpdatedAt })
