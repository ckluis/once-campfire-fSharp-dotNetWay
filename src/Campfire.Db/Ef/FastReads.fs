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

    [<CLIMutable>]
    type private MembershipRow =
        { Id: int64
          RoomId: int64
          UserId: int64
          Involvement: string | null
          UnreadAt: Nullable<DateTime>
          ConnectedAt: Nullable<DateTime>
          Connections: int64
          CreatedAt: DateTime
          UpdatedAt: DateTime }

    let private ofRoomRow (r: RoomRow | null) : Room option =
        match r with
        | null -> None
        | r ->
            Some
                { Id = r.Id
                  Name = r.Name
                  Type = Names.roomTypeOfDb r.Type
                  CreatorId = r.CreatorId
                  CreatedAt = r.CreatedAt
                  UpdatedAt = r.UpdatedAt }

    // Process-wide Dapper settings (snake case, Rails' time text), set once when the first context options are built.
    let private configure =
        lazy
            (DefaultTypeMap.MatchNamesWithUnderscores <- true
             SqlMapper.AddTypeHandler(RailsDateTimeHandler()))

    let ensureConfigured () : unit = configure.Force()

    // The log hook: EF's command log does not see Dapper, so a sink registered for a database (its Data Source) is told each
    // statement's text (parameters are placeholders, never values). Tests register one; production registers none.
    let private sinks = ConcurrentDictionary<string, Action<string>>()
    // `SqliteConnection.DataSource` is the path as written while the connection is closed and the resolved absolute path once it is open
    // (the request's connection is, since P3: macOS resolves /var to /private/var), so a sink is keyed by the last two segments.
    let private sinkKey (dataSource: string) : string =
        let parts = dataSource.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
        String.Join("/", parts |> Array.skip (max 0 (parts.Length - 2)))
    let mutable private sinkCount = 0

    /// Lists every Dapper statement run against the database at `dataSource` (as `RelationalEventId.CommandExecuted` lists EF's).
    let logTo (dataSource: string) (sink: Action<string>) : unit =
        sinks[sinkKey dataSource] <- sink
        Volatile.Write(&sinkCount, sinks.Count)

    let private note (db: CampfireDb) (sql: string) : unit =
        if Volatile.Read(&sinkCount) > 0 then
            let mutable sink = Unchecked.defaultof<Action<string>>
            if sinks.TryGetValue(sinkKey (db.Database.GetDbConnection().DataSource), &sink) then sink.Invoke sql

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
    let private originalRoomForUserSql =
        "SELECT rooms.id, rooms.name, rooms.type, rooms.creator_id, rooms.created_at, rooms.updated_at FROM rooms INNER JOIN memberships ON rooms.id = memberships.room_id WHERE memberships.user_id = @userId ORDER BY rooms.created_at LIMIT 1"
    let private roomByIdSql = "SELECT id, name, type, creator_id, created_at, updated_at FROM rooms WHERE id = @id LIMIT 1"
    let private originalRoomSql = "SELECT id, name, type, creator_id, created_at, updated_at FROM rooms ORDER BY created_at LIMIT 1"
    let private membershipSql =
        "SELECT id, room_id, user_id, involvement, unread_at, connected_at, connections, created_at, updated_at FROM memberships WHERE room_id = @roomId AND user_id = @userId LIMIT 1"
    let private messageSql = "SELECT id, client_message_id, created_at, creator_id, room_id, updated_at FROM messages WHERE id = @id LIMIT 1"
    let private lastPageSql =
        "SELECT id, client_message_id, created_at, creator_id, room_id, updated_at FROM messages WHERE room_id = @roomId ORDER BY created_at DESC LIMIT 40"
    let private pageBeforeSql =
        "SELECT id, client_message_id, created_at, creator_id, room_id, updated_at FROM messages WHERE room_id = @roomId AND created_at < @at ORDER BY created_at DESC LIMIT 40"
    let private attachedBlobSql =
        "SELECT b.id, b.key, b.filename, b.content_type, b.metadata, b.service_name, b.byte_size, b.checksum, b.created_at FROM active_storage_attachments a INNER JOIN active_storage_blobs b ON a.blob_id = b.id WHERE a.record_type = @recordType AND a.record_id = @recordId AND a.name = @name ORDER BY a.id LIMIT 1"

    let private visibleRowsSql =
        "SELECT memberships.id AS membership_id, memberships.room_id AS room_id, memberships.user_id AS user_id, memberships.involvement AS involvement, memberships.unread_at AS unread_at, memberships.connected_at AS connected_at, memberships.connections AS connections, memberships.created_at AS membership_created_at, memberships.updated_at AS membership_updated_at, rooms.name AS room_name, rooms.type AS room_type, rooms.creator_id AS room_creator_id, rooms.created_at AS room_created_at, rooms.updated_at AS room_updated_at FROM memberships INNER JOIN rooms ON rooms.id = memberships.room_id WHERE memberships.user_id = @userId AND memberships.involvement != 'invisible' ORDER BY LOWER(rooms.name)"
    let private roomsOfTypeSql =
        "SELECT rooms.id, rooms.name, rooms.type, rooms.creator_id, rooms.created_at, rooms.updated_at FROM rooms INNER JOIN memberships ON rooms.id = memberships.room_id WHERE memberships.user_id = @userId AND rooms.type = @roomType"
    let private directMemberIdsSql =
        "SELECT memberships.user_id FROM memberships WHERE room_id IN (SELECT rooms.id FROM rooms INNER JOIN memberships AS own ON rooms.id = own.room_id WHERE own.user_id = @userId AND rooms.type = @roomType) ORDER BY memberships.id"
    let private activeExcludingSql =
        "SELECT id, name, email_address, password_digest, role, status, bio, bot_token, created_at, updated_at FROM users WHERE status = 0 AND id NOT IN (SELECT value FROM json_each(@ids)) ORDER BY created_at LIMIT @limit"
    let private searchesSql = "SELECT id, user_id, query, created_at, updated_at FROM searches WHERE user_id = @userId ORDER BY updated_at DESC"
    let private searchReachableSql =
        "SELECT messages.id, messages.client_message_id, messages.created_at, messages.creator_id, messages.room_id, messages.updated_at FROM messages INNER JOIN rooms ON messages.room_id = rooms.id INNER JOIN memberships ON rooms.id = memberships.room_id JOIN message_search_index idx ON messages.id = idx.rowid WHERE memberships.user_id = @userId AND (idx.body MATCH @terms) ORDER BY messages.created_at DESC LIMIT 100"

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
        onConnection db roomForUserSql (fun c t -> c.QueryFirstOrDefault<RoomRow>(roomForUserSql, {| userId = userId; roomId = roomId |}, t) |> ofRoomRow)

    /// `Room.find_by(id:)`
    let roomById (db: CampfireDb) (id: int64) : Room option =
        onConnection db roomByIdSql (fun c t -> c.QueryFirstOrDefault<RoomRow>(roomByIdSql, {| id = id |}, t) |> ofRoomRow)

    /// `Room.original`: the oldest room.
    let originalRoom (db: CampfireDb) : Room option =
        onConnection db originalRoomSql (fun c t -> c.QueryFirstOrDefault<RoomRow>(originalRoomSql, null, t) |> ofRoomRow)

    /// `user.rooms.original`: the oldest of the user's rooms. (Hybrid since 10.3: the EF LINQ form converted its F# lambdas to expression
    /// trees on every call, FSharp.Core's quotation reflection, about 3% of a page's CPU.)
    let originalRoomForUser (db: CampfireDb) (userId: int64) : Room option =
        onConnection db originalRoomForUserSql (fun c t -> c.QueryFirstOrDefault<RoomRow>(originalRoomForUserSql, {| userId = userId |}, t) |> ofRoomRow)

    /// `room.memberships.find_by(user:)`
    let membershipByRoomAndUser (db: CampfireDb) (roomId: int64) (userId: int64) : Membership option =
        onConnection db membershipSql (fun c t ->
            match c.QueryFirstOrDefault<MembershipRow>(membershipSql, {| roomId = roomId; userId = userId |}, t) with
            | null -> None
            | r ->
                Some
                    { Id = r.Id
                      RoomId = r.RoomId
                      UserId = r.UserId
                      Involvement = (match r.Involvement with null -> Nullable() | i -> Nullable(Names.involvementOfDb i))
                      UnreadAt = r.UnreadAt
                      ConnectedAt = r.ConnectedAt
                      Connections = r.Connections
                      CreatedAt = r.CreatedAt
                      UpdatedAt = r.UpdatedAt })

    /// `Message.find_by(id:)`
    let messageById (db: CampfireDb) (id: int64) : Message option =
        onConnection db messageSql (fun c t -> c.QueryFirstOrDefault<Message>(messageSql, {| id = id |}, t) |> Option.ofObj)

    /// `room.messages.last_page`: the newest 40, oldest first.
    let lastPage (db: CampfireDb) (roomId: int64) : Message list =
        onConnection db lastPageSql (fun c t -> c.Query<Message>(lastPageSql, {| roomId = roomId |}, t) |> Seq.toList |> List.rev)

    /// `room.messages.page_before`: up to 40 before `at`, oldest first.
    let pageBefore (db: CampfireDb) (roomId: int64) (at: DateTime) : Message list =
        onConnection db pageBeforeSql (fun c t ->
            c.Query<Message>(pageBeforeSql, {| roomId = roomId; at = RailsTime.toDb at |}, t) |> Seq.toList |> List.rev)

    /// `has_one_attached`: the blob of the record's first attachment under `name`, by attachment id.
    let attachedBlob (db: CampfireDb) (recordType: string) (recordId: int64) (name: string) : Blob option =
        onConnection db attachedBlobSql (fun c t ->
            c.QueryFirstOrDefault<Blob>(attachedBlobSql, {| recordType = recordType; recordId = recordId; name = name |}, t) |> Option.ofObj)

    /// `user.memberships.visible.with_ordered_room`: the sidebar's one statement, each membership flat with its room (times as text).
    let visibleMembershipRoomRows (db: CampfireDb) (userId: int64) : MembershipRoomRow list =
        onConnection db visibleRowsSql (fun c t -> c.Query<MembershipRoomRow>(visibleRowsSql, {| userId = userId |}, t) |> Seq.toList)

    /// `user.rooms.<type>`: the rooms of the user's memberships of one type (`roomType` is the database string).
    let roomsOfType (db: CampfireDb) (userId: int64) (roomType: string) : Room list =
        onConnection db roomsOfTypeSql (fun c t ->
            c.Query<RoomRow>(roomsOfTypeSql, {| userId = userId; roomType = roomType |}, t)
            |> Seq.map (fun r -> (ofRoomRow r).Value)
            |> Seq.toList)

    /// The user ids of every membership in the rooms of one type the user is in, in membership order.
    let memberUserIdsOfRoomsOfType (db: CampfireDb) (userId: int64) (roomType: string) : int64 list =
        onConnection db directMemberIdsSql (fun c t -> c.Query<int64>(directMemberIdsSql, {| userId = userId; roomType = roomType |}, t) |> Seq.toList)

    /// `User.active.where.not(id: ids).order(:created_at).limit(n)`; `ids` travel as one JSON array parameter.
    let activeUsersExcluding (db: CampfireDb) (ids: int64[]) (limit: int) : User list =
        let json = "[" + String.Join(",", ids) + "]"
        onConnection db activeExcludingSql (fun c t -> c.Query<User>(activeExcludingSql, {| ids = json; limit = limit |}, t) |> Seq.toList)

    /// `user.searches.ordered`: most recent first.
    let searchesOrdered (db: CampfireDb) (userId: int64) : Search list =
        onConnection db searchesSql (fun c t -> c.Query<Search>(searchesSql, {| userId = userId |}, t) |> Seq.toList)

    /// `Current.user.reachable_messages.search(query).last(100)`, oldest first; `terms` is the quoted FTS5 match expression.
    let searchReachable (db: CampfireDb) (userId: int64) (terms: string) : Message list =
        onConnection db searchReachableSql (fun c t ->
            c.Query<Message>(searchReachableSql, {| userId = userId; terms = terms |}, t) |> Seq.toList |> List.rev)
