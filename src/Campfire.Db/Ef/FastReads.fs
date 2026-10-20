// Spike (bench/results/spike-dapper): the room page's and messages page's reads with Dapper on the context's own connection.
// EF Core keeps the model, the writes, the transactions and the interceptors; these are queries only (CQRS, Microsoft's .NET
// microservices guide). Each returns the same entity records the EF queries return.
namespace Campfire.Db.Ef

open System
open System.Data
open Dapper
open Microsoft.EntityFrameworkCore
open Campfire.Db

/// Active Record's text for a `datetime(6)` column, read by Dapper: the same `RailsTime` the EF converter uses.
type RailsDateTimeHandler() =
    inherit SqlMapper.TypeHandler<DateTime>()
    override _.Parse(value: obj) = RailsTime.ofDb (string value)
    override _.SetValue(parameter: IDbDataParameter, value: DateTime) =
        parameter.DbType <- DbType.String
        parameter.Value <- RailsTime.toDb value

module FastReads =
    // Dapper ignores a TypeHandler for an enum type (it parses the column with Enum.Parse), so the two string-backed enums
    // (Rooms::Open/Closed/Direct and the involvement names) are read as strings into these rows and mapped here.
    [<CLIMutable>]
    type RoomRow =
        { Id: int64
          Name: string | null
          Type: string
          CreatorId: int64
          CreatedAt: DateTime
          UpdatedAt: DateTime }

    [<CLIMutable>]
    type MembershipRow =
        { Id: int64
          RoomId: int64
          UserId: int64
          Involvement: string | null
          UnreadAt: Nullable<DateTime>
          ConnectedAt: Nullable<DateTime>
          Connections: int64
          CreatedAt: DateTime
          UpdatedAt: DateTime }

    let private configure =
        lazy
            (DefaultTypeMap.MatchNamesWithUnderscores <- true
             SqlMapper.AddTypeHandler(RailsDateTimeHandler()))

    /// The switch: `CAMPFIRE_READS=dapper` (read once; tests and benches flip it).
    let mutable enabled : bool =
        Environment.GetEnvironmentVariable "CAMPFIRE_READS" = "dapper"

    /// Runs `f` on the context's connection, opened and closed through EF's connection so its interceptors (the pragmas) run
    /// as they do for an EF query.
    let inline private onConnection (db: CampfireDb) ([<InlineIfLambda>] f: IDbConnection -> 'T) : 'T =
        configure.Force()
        db.Database.OpenConnection()
        try
            f (db.Database.GetDbConnection())
        finally
            db.Database.CloseConnection()

    let private ofRoomRow (r: RoomRow | null) : Room option =
        match r with
        | null -> None
        | r -> Some { Id = r.Id; Name = r.Name; Type = Names.roomTypeOfDb r.Type; CreatorId = r.CreatorId; CreatedAt = r.CreatedAt; UpdatedAt = r.UpdatedAt }

    let private messageColumns = "id, client_message_id, created_at, creator_id, room_id, updated_at"

    let sessionByToken (db: CampfireDb) (token: string) : Session option =
        onConnection db (fun c -> c.QueryFirstOrDefault<Session>("SELECT * FROM sessions WHERE token = @token LIMIT 1", {| token = token |}) |> Option.ofObj)

    let userById (db: CampfireDb) (id: int64) : User option =
        onConnection db (fun c -> c.QueryFirstOrDefault<User>("SELECT * FROM users WHERE id = @id LIMIT 1", {| id = id |}) |> Option.ofObj)

    let roomById (db: CampfireDb) (id: int64) : Room option =
        onConnection db (fun c -> c.QueryFirstOrDefault<RoomRow>("SELECT * FROM rooms WHERE id = @id LIMIT 1", {| id = id |}) |> ofRoomRow)

    let roomForUser (db: CampfireDb) (userId: int64) (roomId: int64) : Room option =
        onConnection db (fun c ->
            c.QueryFirstOrDefault<RoomRow>(
                "SELECT rooms.* FROM rooms INNER JOIN memberships ON rooms.id = memberships.room_id WHERE memberships.user_id = @userId AND rooms.id = @roomId LIMIT 1",
                {| userId = userId; roomId = roomId |}
            )
            |> ofRoomRow)

    let originalRoom (db: CampfireDb) : Room option =
        onConnection db (fun c -> c.QueryFirstOrDefault<RoomRow>("SELECT * FROM rooms ORDER BY created_at LIMIT 1") |> ofRoomRow)

    let membershipByRoomAndUser (db: CampfireDb) (roomId: int64) (userId: int64) : Membership option =
        onConnection db (fun c ->
            match c.QueryFirstOrDefault<MembershipRow>(
                "SELECT * FROM memberships WHERE room_id = @roomId AND user_id = @userId LIMIT 1",
                {| roomId = roomId; userId = userId |}
            ) with
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

    let messageById (db: CampfireDb) (id: int64) : Message option =
        onConnection db (fun c ->
            c.QueryFirstOrDefault<Message>($"SELECT {messageColumns} FROM messages WHERE id = @id LIMIT 1", {| id = id |}) |> Option.ofObj)

    /// The newest 40 of the room, oldest first.
    let lastPage (db: CampfireDb) (roomId: int64) : Message list =
        onConnection db (fun c ->
            c.Query<Message>($"SELECT {messageColumns} FROM messages WHERE room_id = @roomId ORDER BY created_at DESC LIMIT 40", {| roomId = roomId |})
            |> Seq.toList
            |> List.rev)

    /// Up to 40 before `at`, oldest first.
    let pageBefore (db: CampfireDb) (roomId: int64) (at: DateTime) : Message list =
        onConnection db (fun c ->
            c.Query<Message>(
                $"SELECT {messageColumns} FROM messages WHERE room_id = @roomId AND created_at < @at ORDER BY created_at DESC LIMIT 40",
                {| roomId = roomId; at = RailsTime.toDb at |}
            )
            |> Seq.toList
            |> List.rev)

    let accountFirst (db: CampfireDb) : Account option =
        onConnection db (fun c -> c.QueryFirstOrDefault<Account>("SELECT * FROM accounts ORDER BY id LIMIT 1") |> Option.ofObj)

    /// `has_one_attached`: the blob of the record's first attachment under `name`, by attachment id.
    let attachedBlob (db: CampfireDb) (recordType: string) (recordId: int64) (name: string) : Blob option =
        onConnection db (fun c ->
            c.QueryFirstOrDefault<Blob>(
                "SELECT b.* FROM active_storage_attachments a INNER JOIN active_storage_blobs b ON a.blob_id = b.id WHERE a.record_type = @recordType AND a.record_id = @recordId AND a.name = @name ORDER BY a.id LIMIT 1",
                {| recordType = recordType; recordId = recordId; name = name |}
            )
            |> Option.ofObj)
