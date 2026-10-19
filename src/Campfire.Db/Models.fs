// Port of rust/crates/db/src/models/{user,membership,room,message,boost,webhook}.rs
//
// These models call one another (a user's ban destroys messages, a message touches its room, a room
// destroys its messages), and F# files can't, so they share a recursive namespace.
namespace rec Campfire.Db

open System
open System.Linq
open System.Runtime.CompilerServices
open System.Text
open Microsoft.EntityFrameworkCore
open Campfire.RailsCompat
// RailsCompat has a `Timestamp` of its own (an alias of DateTimeOffset); ours is the one models use.
open Campfire.Db

// ---------------------------------------------------------------------------------------------
// User
// `reference/app/models/user.rb` and `user/*.rb` (Role, Bot, Bannable, Mentionable; Avatar
// and Transferable are signed ids, which live in `Campfire.RailsCompat`).
// ---------------------------------------------------------------------------------------------

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module User =
    // ---- The port's Conn-based readers, for the models that have not moved to EF yet (units 1.4-1.9 delete them, 1.11 the rest).

    /// `SELECT` and the columns `User.fromRow` reads, `FROM "users"`: queries built outside
    /// this module start with it.
    [<Literal>]
    let Select = Selects.User

    [<Literal>]
    let private findByIdSql = Selects.User + " WHERE \"users\".\"id\" = ? LIMIT 1"

    /// Reads a user from `Columns.User`, starting at column `offset`.
    let fromRowAt (r: Row) (offset: int) : User =
        { Id = r.Int64(offset + 0)
          Name = r.Text(offset + 1)
          EmailAddress = Option.toObj (r.OptText(offset + 2))
          PasswordDigest = Option.toObj (r.OptText(offset + 3))
          Role = Role.ofSql (r.Int64(offset + 4))
          Status = Status.ofSql (r.Int64(offset + 5))
          Bio = Option.toObj (r.OptText(offset + 6))
          BotToken = Option.toObj (r.OptText(offset + 7))
          CreatedAt = RailsTime.truncate ((r.Timestamp(offset + 8)).ToDateTimeOffset().UtcDateTime)
          UpdatedAt = RailsTime.truncate ((r.Timestamp(offset + 9)).ToDateTimeOffset().UtcDateTime) }

    let fromRow (r: Row) : User = fromRowAt r 0

    /// `User.find_by_sql`: the users a query starting with `User.Select` returns.
    let findBySql (conn: Conn) (sql: string) (args: SqlArg[]) : User list = conn.QueryAll(sql, args, fromRow)

    let findByIdLegacy (conn: Conn) (id: int64) : User option = conn.QueryOne(findByIdSql, [| I id |], fromRow)

    let findLegacy (conn: Conn) (id: int64) : User = findByIdLegacy conn id |> Err.orNotFound "User"

    // ---- Finders and scopes (EF, LINQ; every captured value goes through `EF.Parameter` so the query cache keeps hitting)

    /// `User.generate_bot_token`
    let generateBotToken () : string = Sql.alphanumeric 12

    /// `User.find_by(id:)`
    let findById (db: Ef.CampfireDb) (id: int64) : User option =
        db.Users.Where(fun u -> u.Id = EF.Parameter id) |> Ef.Data.tryFirst

    /// `User.find`
    let find (db: Ef.CampfireDb) (id: int64) : User = findById db id |> Err.orNotFound "User"

    /// `User.active.find(id)`
    let findActive (db: Ef.CampfireDb) (id: int64) : User =
        db.Users.Where(fun u -> u.Status = Status.Active && u.Id = EF.Parameter id) |> Ef.Data.tryFirst |> Err.orNotFound "User"

    let findByEmailAddress (db: Ef.CampfireDb) (emailAddress: string) : User option =
        db.Users.Where(fun u -> u.EmailAddress = EF.Parameter emailAddress) |> Ef.Data.tryFirst

    let all (db: Ef.CampfireDb) : User list = db.Users.ToList() |> List.ofSeq

    let count (db: Ef.CampfireDb) : int64 = db.Users.LongCount()

    /// `User.where(id: ids)`
    let whereIds (db: Ef.CampfireDb) (ids: int64 list) : User list =
        let wanted = Array.ofList ids
        db.Users.Where(fun u -> Enumerable.Contains(EF.Parameter wanted, u.Id)).ToList() |> List.ofSeq

    /// `User.active.ordered`
    let activeOrdered (db: Ef.CampfireDb) : User list =
        db.Users.Where(fun u -> u.Status = Status.Active).OrderBy(fun u -> u.Name.ToLower()).ToList() |> List.ofSeq

    /// `User.active`
    let active (db: Ef.CampfireDb) : User list =
        db.Users.Where(fun u -> u.Status = Status.Active).ToList() |> List.ofSeq

    /// `User.active.filtered_by(query).ordered`
    let activeFilteredByOrdered (db: Ef.CampfireDb) (query: string) : User list =
        let pattern = $"%%{query}%%"
        db.Users
            .Where(fun u -> u.Status = Status.Active && EF.Functions.Like(u.Name, EF.Parameter pattern))
            .OrderBy(fun u -> u.Name.ToLower())
            .ToList()
        |> List.ofSeq

    /// `User.active.ordered.without_bots`
    let activeOrderedWithoutBots (db: Ef.CampfireDb) : User list =
        db.Users
            .Where(fun u -> u.Status = Status.Active && u.Role <> Role.Bot)
            .OrderBy(fun u -> u.Name.ToLower())
            .ToList()
        |> List.ofSeq

    /// `User.active_bots.ordered`
    let activeBotsOrdered (db: Ef.CampfireDb) : User list =
        db.Users
            .Where(fun u -> u.Status = Status.Active && u.Role = Role.Bot)
            .OrderBy(fun u -> u.Name.ToLower())
            .ToList()
        |> List.ofSeq

    /// `User.active_bots.find(id)`
    let findActiveBot (db: Ef.CampfireDb) (id: int64) : User =
        db.Users.Where(fun u -> u.Status = Status.Active && u.Role = Role.Bot && u.Id = EF.Parameter id)
        |> Ef.Data.tryFirst
        |> Err.orNotFound "User"

    /// `User.active.find_by(email_address:)`: the lookup half of `authenticate_by` (the sign-in lookup).
    let findActiveByEmailAddress (db: Ef.CampfireDb) (emailAddress: string) : User option =
        db.Users.Where(fun u -> u.Status = Status.Active && u.EmailAddress = EF.Parameter emailAddress) |> Ef.Data.tryFirst

    let private dummyDigest = "$2a$12$FiKmSp4UhLvSB4Sd/ZUjQunyKP6.NjDRHdr5LnKUVk.BUn4Mq12WS"

    /// `MENTION_CONTENT_TYPE`
    [<Literal>]
    let MentionContentType = "application/vnd.campfire.mention"

    /// `has_secure_password`'s `authenticate`.
    let authenticate (user: User) (password: string) : bool =
        match user.PasswordDigest with
        | null -> false
        | "" -> false
        | digest -> Password.verify password digest

    /// The password half of `User.active.authenticate_by(email_address:, password:)`, given
    /// what `findActiveByEmailAddress` found. Blocking (bcrypt), so it runs with no
    /// connection held. A blank password returns nil before the lookup in Rails; callers skip
    /// the lookup for one too.
    let authenticated (candidate: User option) (password: string) : User option =
        if password = "" then
            None
        else
            match candidate with
            | Some user -> if authenticate user password then Some user else None
            | None ->
                // authenticate_by hashes anyway so a missing account takes as long as a wrong password.
                Password.verify password dummyDigest |> ignore
                None

    /// `User.authenticate_bot(bot_key)`: `"#{id}-#{bot_token}"`
    let authenticateBot (db: Ef.CampfireDb) (botKey: string) : User option =
        // Ruby's `split("-")` drops trailing empty fields; a key without a token finds nothing.
        match botKey.Split '-' with
        | parts when parts.Length >= 2 ->
            let token = parts[1]
            // Rails compares `id` (an integer column) with the string: SQLite's integer affinity makes "12abc" never equal 12.
            match Int64.TryParse parts[0] with
            | true, id when string id = parts[0] ->
                db.Users.Where(fun u ->
                    u.Status = Status.Active && u.Role = Role.Bot && u.Id = EF.Parameter id && u.BotToken = EF.Parameter token)
                |> Ef.Data.tryFirst
            | _ -> None
        | _ -> None

    // ---- Creating

    /// `after_create_commit :grant_membership_to_open_rooms`: `Membership.insert_all` stamps the rows with SQLite's own clock
    /// (millisecond text, as Rails' `insert_all` does), and leaves `involvement` to the column default. One statement for every
    /// open room: a `FromSql`-family call because LINQ has no `INSERT ... SELECT`.
    let private grantMembershipToOpenRooms (tx: Ef.Tx) (userId: int64) : unit =
        tx.Db.Database.ExecuteSql(
            $"INSERT INTO memberships (created_at, room_id, updated_at, user_id) SELECT STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), rooms.id, STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), {userId} FROM rooms WHERE rooms.type = 'Rooms::Open' ON CONFLICT DO NOTHING"
        )
        |> ignore

    /// `User.create!`: inserts, then grants memberships to every open room after commit.
    let create (tx: Ef.Tx) (attributes: NewUser) : User =
        let now = tx.Now()
        let user =
            Ef.Data.insert
                tx.Db
                { Id = 0L
                  Name = attributes.Name
                  EmailAddress = Option.toObj attributes.EmailAddress
                  PasswordDigest = attributes.PasswordDigest |> Option.map PasswordDigest.intoString |> Option.toObj
                  Role = attributes.Role
                  Status = Status.Active
                  Bio = Option.toObj attributes.Bio
                  BotToken = Option.toObj attributes.BotToken
                  CreatedAt = now
                  UpdatedAt = now }
        tx.AfterCommit(fun tx -> grantMembershipToOpenRooms tx user.Id)
        user

    /// `create_webhook!(url:)`, for the bot that owns it (the webhook model itself moves in unit 1.8).
    let private addWebhook (tx: Ef.Tx) (userId: int64) (url: string) : unit =
        Ef.Data.insert tx.Db ({ Id = 0L; UserId = userId; Url = url; CreatedAt = DateTime.MinValue; UpdatedAt = DateTime.MinValue }: Ef.Webhook)
        |> ignore

    /// `User.create_bot!`
    let createBot (tx: Ef.Tx) (name: string) (webhookUrl: string option) : User =
        let user =
            create tx { NewUser.create name with BotToken = Some(generateBotToken ()); Role = Role.Bot }
        match webhookUrl with
        | Some url -> addWebhook tx user.Id url
        | None -> ()
        user

    // ---- Updating

    /// `user.update(attributes)`: writes only what changed, and nothing at all (not even `updated_at`) when nothing did.
    let update (tx: Ef.Tx) (user: User) (changes: UserChanges) : User =
        let mutable updated = user
        match changes.Name with
        | Some name -> updated <- { updated with Name = name }
        | None -> ()
        match changes.EmailAddress with
        | Some email -> updated <- { updated with EmailAddress = Option.toObj email }
        | None -> ()
        match changes.PasswordDigest with
        | Some digest -> updated <- { updated with PasswordDigest = PasswordDigest.intoString digest }
        | None -> ()
        match changes.Role with
        | Some role -> updated <- { updated with Role = role }
        | None -> ()
        match changes.Status with
        | Some status -> updated <- { updated with Status = status }
        | None -> ()
        match changes.Bio with
        | Some bio -> updated <- { updated with Bio = Option.toObj bio }
        | None -> ()
        match changes.BotToken with
        | Some token -> updated <- { updated with BotToken = Option.toObj token }
        | None -> ()
        Ef.Data.update tx.Db user updated

    /// `user.webhook`
    let webhook (db: Ef.CampfireDb) (user: User) : Ef.Webhook option =
        let id = user.Id
        db.Webhooks.Where(fun w -> w.UserId = EF.Parameter id) |> Ef.Data.tryFirst

    let webhookUrl (db: Ef.CampfireDb) (user: User) : string option =
        webhook db user |> Option.bind (fun w -> Option.ofObj w.Url)

    /// `update_bot!`: the webhook first, then the user, in one transaction.
    let updateBot (tx: Ef.Tx) (user: User) (changes: UserChanges) (webhookUrl: string option) : User =
        let existing = webhook tx.Db user
        let url = webhookUrl |> Option.filter (fun u -> not (String.IsNullOrWhiteSpace u))
        match url, existing with
        | Some url, Some webhook -> Ef.Data.update tx.Db webhook { webhook with Url = url } |> ignore
        | Some url, None -> addWebhook tx user.Id url
        | None, Some webhook -> Ef.Data.remove tx.Db webhook
        | None, None -> ()
        update tx user changes

    let resetBotKey (tx: Ef.Tx) (user: User) : User =
        update tx user { UserChanges.none with BotToken = Some(Some(generateBotToken ())) }

    /// After commit: a connection authenticating meanwhile then either finds the sessions gone or
    /// is already listening for this (see `Campfire.Cable`'s connection setup).
    let private closeRemoteConnections (tx: Ef.Tx) (user: User) (reconnect: bool) : unit =
        tx.EmitAfterCommit(DisconnectUser(user.Id, reconnect))

    /// `reset_remote_connections`: disconnect this user's sockets and let them reconnect.
    let resetRemoteConnections (tx: Ef.Tx) (user: User) : unit = closeRemoteConnections tx user true

    let private deactivatedEmailAddress (user: User) : string option =
        let uuid = Sql.uuid ()
        Option.ofObj user.EmailAddress |> Option.map (fun e -> e.Replace("@", $"-deactivated-{uuid}@"))

    /// `deactivate`: queues the socket disconnect (after commit: Rails does it mid-transaction, the Rust known
    /// difference, design-data-ef 3.6), then removes non-direct memberships, push subscriptions, searches and
    /// sessions, and scrambles the email address.
    let deactivate (tx: Ef.Tx) (user: User) : User =
        closeRemoteConnections tx user false
        let db = tx.Db
        let id = user.Id
        // A subquery over `rooms`: LINQ (an F# lambda inside a lambda) cannot say it, so it is the SQL Rails writes.
        db.Database.ExecuteSql(
            $"DELETE FROM memberships WHERE id IN (SELECT memberships.id FROM memberships INNER JOIN rooms AS room ON room.id = memberships.room_id WHERE memberships.user_id = {id} AND room.type != 'Rooms::Direct')"
        )
        |> ignore
        db.PushSubscriptions.Where(fun p -> p.UserId = EF.Parameter id).ExecuteDelete() |> ignore
        db.Searches.Where(fun s -> s.UserId = EF.Parameter id).ExecuteDelete() |> ignore
        db.Sessions.Where(fun s -> s.UserId = EF.Parameter id).ExecuteDelete() |> ignore
        let email = deactivatedEmailAddress user
        update tx user { UserChanges.none with Status = Some Status.Deactivated; EmailAddress = Some email }

    /// `User::Bannable#ban`
    let ban (tx: Ef.Tx) (user: User) : User =
        let db = tx.Db
        let id = user.Id
        // create_bans_from_sessions: `sessions.pluck(:ip_address).compact_blank.uniq`
        let ips = db.Sessions.Where(fun s -> s.UserId = EF.Parameter id).Select(fun s -> s.IpAddress).ToList()
        let seen = ResizeArray<string>()
        for ip in ips do
            match ip with
            | null -> ()
            | ip when String.IsNullOrWhiteSpace ip || seen.Contains ip -> ()
            | ip ->
                Err.validate (Ban.validate ip)
                Ef.Data.insert db ({ Id = 0L; UserId = id; IpAddress = ip; CreatedAt = DateTime.MinValue; UpdatedAt = DateTime.MinValue }: Ef.Ban)
                |> ignore
                seen.Add ip
        // apply_ban
        closeRemoteConnections tx user false
        db.Sessions.Where(fun s -> s.UserId = EF.Parameter id).ExecuteDelete() |> ignore
        tx.EmitAfterCommit(RemoveBannedContent id)
        update tx user { UserChanges.none with Status = Some Status.Banned }

    let unban (tx: Ef.Tx) (user: User) : User =
        let id = user.Id
        tx.Db.Bans.Where(fun b -> b.UserId = EF.Parameter id).ExecuteDelete() |> ignore
        update tx user { UserChanges.none with Status = Some Status.Active }

    /// `remove_banned_content`: destroys every message the user wrote. Returns them so the
    /// caller can `broadcast_remove` each one.
    let removeBannedContent (tx: Ef.Tx) (user: User) : Message list =
        let messages = Message.byCreator tx.Db user.Id
        for message in messages do
            Message.destroy tx message
        messages

    /// `deliver_webhook_later(message)`: only bots with a webhook.
    let deliverWebhookLater (tx: Ef.Tx) (user: User) (messageId: int64) : unit =
        if (webhook tx.Db user).IsSome then
            tx.EmitAfterCommit(DeliverWebhook(user.Id, messageId))

    // ---- Attributes (pure)


    /// `name.scan(/\b\w/).join`. Ruby's `\w` is ASCII-only, but `\b` treats any Unicode
    /// letter or digit as a word character, so "Émile" contributes nothing.
    let initials (user: User) : string =
        let isWordChar (r: Rune) =
            Rune.IsLetter r
            || Rune.IsNumber r
            || r.Value = int '_'
        let result = StringBuilder()
        let mutable previous: Rune option = None
        for c in user.Name.EnumerateRunes() do
            let asciiWord = (c.IsAscii && Rune.IsLetterOrDigit c) || c.Value = int '_'
            let boundary =
                match previous with
                | None -> true
                | Some p -> not (isWordChar p)
            if asciiWord && boundary then result.Append(c.ToString()) |> ignore
            previous <- Some c
        result.ToString()

    /// `[ name, bio ].compact_blank.join(" – ")`
    let title (user: User) : string =
        [ Some user.Name; Option.ofObj user.Bio ]
        |> List.choose id
        |> List.filter (fun s -> not (String.IsNullOrWhiteSpace s))
        |> String.concat " – "

    let botKey (user: User) : string = $"{user.Id}-{(match user.BotToken with | null -> String.Empty | token -> token)}"

    let isMember (user: User) : bool = user.Role = Role.Member

    let isAdministrator (user: User) : bool = user.Role = Role.Administrator

    let isBot (user: User) : bool = user.Role = Role.Bot

    let isActive (user: User) : bool = user.Status = Status.Active

    let isDeactivated (user: User) : bool = user.Status = Status.Deactivated

    let isBanned (user: User) : bool = user.Status = Status.Banned

    /// `can_administer?(record)`: administrators, the record's creator, or a new record.
    let canAdminister (user: User) (recordCreatorId: int64 option) (recordIsNew: bool) : bool =
        isAdministrator user || recordCreatorId = Some user.Id || recordIsNew

    /// `attachable_plain_text_representation`
    let attachablePlainTextRepresentation (user: User) : string = $"@{user.Name}"

    let reload (db: Ef.CampfireDb) (user: User) : User = find db user.Id

// ---------------------------------------------------------------------------------------------
// Membership
// `reference/app/models/membership.rb` and `membership/connectable.rb`.
// ---------------------------------------------------------------------------------------------

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Membership =
    /// `Membership::Connectable::CONNECTION_TTL`
    let connectionTtl : TimeSpan = TimeSpan.FromSeconds 60.0

    /// `CONNECTION_TTL.ago`
    let connectionCutoff (now: DateTime) : DateTime = now - connectionTtl

    // ---- The port's Conn-based readers, for the models that have not moved to EF yet (Message, push: units 1.5-1.8; 1.11 deletes them).

    let private involvementOfLegacy (r: Row) (i: int) : Nullable<Involvement> =
        match r.OptText i with
        | None -> Nullable()
        | Some name ->
            match Involvement.fromName name with
            | Some involvement -> Nullable involvement
            | None -> failwith $"unknown involvement \"{name}\""

    let private timeOf (t: Timestamp) : DateTime = RailsTime.truncate (t.ToDateTimeOffset().UtcDateTime)

    let fromRowAt (r: Row) (offset: int) : Membership =
        { Id = r.Int64(offset + 0)
          RoomId = r.Int64(offset + 1)
          UserId = r.Int64(offset + 2)
          Involvement = involvementOfLegacy r (offset + 3)
          UnreadAt = (match r.OptTimestamp(offset + 4) with Some t -> Nullable(timeOf t) | None -> Nullable())
          ConnectedAt = (match r.OptTimestamp(offset + 5) with Some t -> Nullable(timeOf t) | None -> Nullable())
          Connections = r.Int64(offset + 6)
          CreatedAt = timeOf (r.Timestamp(offset + 7))
          UpdatedAt = timeOf (r.Timestamp(offset + 8)) }

    let fromRow (r: Row) : Membership = fromRowAt r 0

    /// `user.memberships.unread.count`: the push badge.
    let unreadCountLegacy (conn: Conn) (userId: int64) : int64 =
        conn.Count(
            """SELECT COUNT(*) FROM "memberships" WHERE "memberships"."user_id" = ? AND "memberships"."unread_at" IS NOT NULL""",
            [| I userId |]
        )

    /// `CONNECTION_TTL.ago`, for the port's `Timestamp`.
    let connectionCutoffLegacy (now: Timestamp) : Timestamp = now.Ago connectionTtl

    // ---- Finders and scopes (EF, LINQ; every captured value goes through `EF.Parameter`)

    let find (db: Ef.CampfireDb) (id: int64) : Membership =
        db.Memberships.Where(fun m -> m.Id = EF.Parameter id) |> Ef.Data.tryFirst |> Err.orNotFound "Membership"

    let count (db: Ef.CampfireDb) : int64 = db.Memberships.LongCount()

    let forUser (db: Ef.CampfireDb) (userId: int64) : Membership list =
        db.Memberships.Where(fun m -> m.UserId = EF.Parameter userId).ToList() |> List.ofSeq

    let forRoom (db: Ef.CampfireDb) (roomId: int64) : Membership list =
        db.Memberships.Where(fun m -> m.RoomId = EF.Parameter roomId).ToList() |> List.ofSeq

    /// `room.memberships.find_by(user:)` / `user.memberships.find_by(room_id:)`
    let findByRoomAndUser (db: Ef.CampfireDb) (roomId: int64) (userId: int64) : Membership option =
        db.Memberships.Where(fun m -> m.RoomId = EF.Parameter roomId && m.UserId = EF.Parameter userId) |> Ef.Data.tryFirst

    let private ofRow (r: MembershipRoomRow) : Membership * Room =
        let time (text: string) = RailsTime.ofDb text
        let optTime (text: string | null) : Nullable<DateTime> =
            match text with
            | null -> Nullable()
            | text -> Nullable(time text)
        { Id = r.MembershipId
          RoomId = r.RoomId
          UserId = r.UserId
          Involvement =
            (match r.Involvement with
             | null -> Nullable()
             | name -> Nullable(Ef.Names.involvementOfDb name))
          UnreadAt = optTime r.UnreadAt
          ConnectedAt = optTime r.ConnectedAt
          Connections = r.Connections
          CreatedAt = time r.MembershipCreatedAt
          UpdatedAt = time r.MembershipUpdatedAt },
        { Id = r.RoomId
          Name = r.RoomName
          Type = Ef.Names.roomTypeOfDb r.RoomType
          CreatorId = r.RoomCreatorId
          CreatedAt = time r.RoomCreatedAt
          UpdatedAt = time r.RoomUpdatedAt }

    /// `user.memberships.visible.with_ordered_room`, each with its room: the sidebar, one statement (`SqlQuery`, design 4).
    let visibleWithOrderedRoom (db: Ef.CampfireDb) (userId: int64) : (Membership * Room) list =
        db.Database
            .SqlQuery<MembershipRoomRow>(
                $"SELECT memberships.id AS membership_id, memberships.room_id AS room_id, memberships.user_id AS user_id, memberships.involvement AS involvement, memberships.unread_at AS unread_at, memberships.connected_at AS connected_at, memberships.connections AS connections, memberships.created_at AS membership_created_at, memberships.updated_at AS membership_updated_at, rooms.name AS room_name, rooms.type AS room_type, rooms.creator_id AS room_creator_id, rooms.created_at AS room_created_at, rooms.updated_at AS room_updated_at FROM memberships INNER JOIN rooms ON rooms.id = memberships.room_id WHERE memberships.user_id = {userId} AND memberships.involvement != 'invisible' ORDER BY LOWER(rooms.name)"
            )
            .ToList()
        |> Seq.map ofRow
        |> List.ofSeq

    /// `user.memberships.with_ordered_room` (invisible included).
    let withOrderedRoom (db: Ef.CampfireDb) (userId: int64) : (Membership * Room) list =
        db.Database
            .SqlQuery<MembershipRoomRow>(
                $"SELECT memberships.id AS membership_id, memberships.room_id AS room_id, memberships.user_id AS user_id, memberships.involvement AS involvement, memberships.unread_at AS unread_at, memberships.connected_at AS connected_at, memberships.connections AS connections, memberships.created_at AS membership_created_at, memberships.updated_at AS membership_updated_at, rooms.name AS room_name, rooms.type AS room_type, rooms.creator_id AS room_creator_id, rooms.created_at AS room_created_at, rooms.updated_at AS room_updated_at FROM memberships INNER JOIN rooms ON rooms.id = memberships.room_id WHERE memberships.user_id = {userId} ORDER BY LOWER(rooms.name)"
            )
            .ToList()
        |> Seq.map ofRow
        |> List.ofSeq

    /// `user.memberships.without_direct_rooms.count`
    let countWithoutDirectRooms (db: Ef.CampfireDb) (userId: int64) : int64 =
        db.Memberships
            .Where(fun m -> m.UserId = EF.Parameter userId)
            .Join(db.Rooms.Where(fun r -> r.Type <> RoomType.Direct), (fun m -> m.RoomId), (fun r -> r.Id), (fun m _ -> m))
            .LongCount()

    /// `user.memberships.unread.count`: the push badge.
    let unreadCount (db: Ef.CampfireDb) (userId: int64) : int64 =
        db.Memberships.LongCount(fun m -> m.UserId = EF.Parameter userId && m.UnreadAt.HasValue)

    /// `Membership.connected.exists?(id)`
    let connectedExists (db: Ef.CampfireDb) (id: int64) (now: DateTime) : bool =
        let cutoff = connectionCutoff now
        db.Memberships.Any(fun m -> m.Id = EF.Parameter id && m.ConnectedAt.HasValue && m.ConnectedAt.Value >= EF.Parameter cutoff)

    /// `Membership.disconnected.exists?(id)`
    let disconnectedExists (db: Ef.CampfireDb) (id: int64) (now: DateTime) : bool =
        let cutoff = connectionCutoff now
        db.Memberships.Any(fun m -> m.Id = EF.Parameter id && (not m.ConnectedAt.HasValue || m.ConnectedAt.Value < EF.Parameter cutoff))

    // ---- Involvement and read state

    let involvedIn (membership: Membership) (involvement: Involvement) : bool =
        membership.Involvement.HasValue && membership.Involvement.Value = involvement

    /// `update!(involvement:)`
    let updateInvolvement (tx: Ef.Tx) (membership: Membership) (involvement: Involvement option) : Membership =
        let value = match involvement with Some i -> Nullable i | None -> Nullable()
        Ef.Data.update tx.Db membership { membership with Involvement = value }

    /// `read`: `update!(unread_at: nil)`
    let read (tx: Ef.Tx) (membership: Membership) : Membership =
        Ef.Data.update tx.Db membership { membership with UnreadAt = Nullable() }

    let unread (membership: Membership) : bool = membership.UnreadAt.HasValue

    /// `destroy`: the user's sockets reconnect after commit, so their subscriptions to this room are dropped.
    let destroy (tx: Ef.Tx) (membership: Membership) : unit =
        Ef.Data.remove tx.Db membership
        let userId = membership.UserId
        tx.AfterCommit(fun tx -> tx.EmitAfterCommit(DisconnectUser(userId, true)))

    // ---- Membership::Connectable

    /// `Membership.disconnect_all`
    let disconnectAll (tx: Ef.Tx) : int =
        let now = tx.Now()
        let cutoff = connectionCutoff now
        tx.Db.Memberships
            .Where(fun m -> m.ConnectedAt.HasValue && m.ConnectedAt.Value >= EF.Parameter cutoff)
            .ExecuteUpdate(fun s ->
                s.SetProperty((fun m -> m.ConnectedAt), Nullable<DateTime>())
                 .SetProperty((fun m -> m.Connections), 0L)
                 .SetProperty((fun m -> m.UpdatedAt), now)
                |> ignore)

    /// `Membership.connect(membership, connections)`: no `updated_at`.
    let connect (tx: Ef.Tx) (id: int64) (connections: int64) : unit =
        let now = tx.Now()
        tx.Db.Memberships
            .Where(fun m -> m.Id = EF.Parameter id)
            .ExecuteUpdate(fun s ->
                s.SetProperty((fun m -> m.Connections), connections)
                 .SetProperty((fun m -> m.ConnectedAt), Nullable now)
                 .SetProperty((fun m -> m.UnreadAt), Nullable<DateTime>())
                |> ignore)
        |> ignore

    /// `connected?`
    let isConnected (membership: Membership) (now: DateTime) : bool =
        membership.ConnectedAt.HasValue && membership.ConnectedAt.Value >= connectionCutoff now

    /// `present`
    let present (tx: Ef.Tx) (membership: Membership) : unit =
        let connections = if isConnected membership (tx.Now()) then membership.Connections + 1L else 1L
        connect tx membership.Id connections

    /// `increment!(:connections, touch: true)` / `decrement!`, and `update!(connections:)` (a no-op when unchanged): the
    /// context stamps `updated_at` on what changed.
    let private setConnections (tx: Ef.Tx) (membership: Membership) (connections: int64) : Membership =
        Ef.Data.update tx.Db membership { membership with Connections = connections }

    let private incrementConnections (tx: Ef.Tx) (membership: Membership) : Membership =
        setConnections tx membership (if isConnected membership (tx.Now()) then membership.Connections + 1L else 1L)

    let private decrementConnections (tx: Ef.Tx) (membership: Membership) : Membership =
        setConnections tx membership (if isConnected membership (tx.Now()) then membership.Connections - 1L else 0L)

    /// `touch :connected_at`
    let private touchConnectedAt (tx: Ef.Tx) (membership: Membership) : Membership =
        let now = tx.Now()
        tx.Db.Memberships
            .Where(fun m -> m.Id = EF.Parameter membership.Id)
            .ExecuteUpdate(fun s ->
                s.SetProperty((fun m -> m.ConnectedAt), Nullable now).SetProperty((fun m -> m.UpdatedAt), now) |> ignore)
        |> ignore
        { membership with ConnectedAt = Nullable now; UpdatedAt = now }

    /// `connected`
    let connected (tx: Ef.Tx) (membership: Membership) : Membership =
        incrementConnections tx membership |> touchConnectedAt tx

    /// `disconnected`
    let disconnected (tx: Ef.Tx) (membership: Membership) : Membership =
        let membership = decrementConnections tx membership
        if membership.Connections < 1L && membership.ConnectedAt.HasValue then
            let now = tx.Now()
            tx.Db.Memberships
                .Where(fun m -> m.Id = EF.Parameter membership.Id)
                .ExecuteUpdate(fun s ->
                    s.SetProperty((fun m -> m.ConnectedAt), Nullable<DateTime>()).SetProperty((fun m -> m.UpdatedAt), now) |> ignore)
            |> ignore
            { membership with ConnectedAt = Nullable(); UpdatedAt = now }
        else
            membership

    /// `refresh_connection`
    let refreshConnection (tx: Ef.Tx) (membership: Membership) : Membership =
        let membership = if not (isConnected membership (tx.Now())) then incrementConnections tx membership else membership
        touchConnectedAt tx membership

    let reload (db: Ef.CampfireDb) (membership: Membership) : Membership = find db membership.Id

// ---------------------------------------------------------------------------------------------
// Room
// `reference/app/models/room.rb`, `rooms/*.rb` and `room/message_pusher.rb`'s queries.
// ---------------------------------------------------------------------------------------------

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Room =
    /// Rows per `INSERT` of memberships, well under SQLite's bound-variable limit.
    [<Literal>]
    let private MembershipInsertBatch = 1_000

    // ---- The port's Conn-based readers, for push (unit 1.8 moves them).

    let private roomTypeOfLegacy (r: Row) (i: int) : RoomType =
        let name = r.Text i
        match RoomType.fromClassName name with
        | Some roomType -> roomType
        | None -> failwith $"unknown room type \"{name}\""

    let fromRowAt (r: Row) (offset: int) : Room =
        { Id = r.Int64(offset + 0)
          Name = Option.toObj (r.OptText(offset + 1))
          Type = roomTypeOfLegacy r (offset + 2)
          CreatorId = r.Int64(offset + 3)
          CreatedAt = RailsTime.truncate ((r.Timestamp(offset + 4)).ToDateTimeOffset().UtcDateTime)
          UpdatedAt = RailsTime.truncate ((r.Timestamp(offset + 5)).ToDateTimeOffset().UtcDateTime) }

    let fromRow (r: Row) : Room = fromRowAt r 0

    let findByIdLegacy (conn: Conn) (id: int64) : Room option =
        conn.QueryOne(Selects.Room + " WHERE \"rooms\".\"id\" = ? LIMIT 1", [| I id |], fromRow)

    let findLegacy (conn: Conn) (id: int64) : Room = findByIdLegacy conn id |> Err.orNotFound "Room"

    // ---- Finders and scopes (EF, LINQ)

    let findById (db: Ef.CampfireDb) (id: int64) : Room option =
        db.Rooms.Where(fun r -> r.Id = EF.Parameter id) |> Ef.Data.tryFirst

    let find (db: Ef.CampfireDb) (id: int64) : Room = findById db id |> Err.orNotFound "Room"

    let all (db: Ef.CampfireDb) : Room list = db.Rooms.ToList() |> List.ofSeq

    /// `Room.opens` / `closeds` / `directs`
    let ofType (db: Ef.CampfireDb) (roomType: RoomType) : Room list =
        db.Rooms.Where(fun r -> r.Type = EF.Parameter roomType).ToList() |> List.ofSeq

    let countOfType (db: Ef.CampfireDb) (roomType: RoomType) : int64 =
        db.Rooms.LongCount(fun r -> r.Type = EF.Parameter roomType)

    /// `Room.original`: the oldest room.
    let original (db: Ef.CampfireDb) : Room option = db.Rooms.OrderBy(fun r -> r.CreatedAt) |> Ef.Data.tryFirst

    // `Current.user.rooms` and its scopes: the rooms of the user's memberships.

    let private ofUser (db: Ef.CampfireDb) (userId: int64) : IQueryable<Room> =
        db.Rooms.Join(db.Memberships.Where(fun m -> m.UserId = EF.Parameter userId), (fun r -> r.Id), (fun m -> m.RoomId), (fun r _ -> r))

    /// `user.rooms`
    let forUser (db: Ef.CampfireDb) (userId: int64) : Room list = (ofUser db userId).ToList() |> List.ofSeq

    /// `user.rooms.find_by(id:)`
    let findForUser (db: Ef.CampfireDb) (userId: int64) (roomId: int64) : Room option =
        (ofUser db userId).Where(fun r -> r.Id = EF.Parameter roomId) |> Ef.Data.tryFirst

    /// `user.rooms.directs` / `.opens` / `.closeds`
    let forUserOfType (db: Ef.CampfireDb) (userId: int64) (roomType: RoomType) : Room list =
        (ofUser db userId).Where(fun r -> r.Type = EF.Parameter roomType).ToList() |> List.ofSeq

    /// `user.rooms.without_directs`
    let forUserWithoutDirects (db: Ef.CampfireDb) (userId: int64) : Room list =
        (ofUser db userId).Where(fun r -> r.Type <> RoomType.Direct).ToList() |> List.ofSeq

    /// `user.rooms.original`
    let originalForUser (db: Ef.CampfireDb) (userId: int64) : Room option =
        (ofUser db userId).OrderBy(fun r -> r.CreatedAt) |> Ef.Data.tryFirst

    /// `user.rooms.last`
    let lastForUser (db: Ef.CampfireDb) (userId: int64) : Room option =
        (ofUser db userId).OrderByDescending(fun r -> r.Id) |> Ef.Data.tryFirst

    // ---- Memberships

    let memberships (db: Ef.CampfireDb) (room: Room) : Membership list = Membership.forRoom db room.Id

    /// `memberships.grant_to(users)`: `Membership.insert_all(... { room_id:, user_id:, involvement: })` stamps the rows with
    /// SQLite's own clock (what `insert_all` does; LINQ has no `INSERT ... VALUES` with an expression, so this is SQL), skipping
    /// members already there.
    let private insertMemberships (tx: Ef.Tx) (roomId: int64) (involvement: Involvement) (userIds: int64 list) : unit =
        // In batches: SQLite binds at most 32,766 variables per statement, 3 per row here.
        for batch in List.chunkBySize MembershipInsertBatch userIds do
            let rows =
                batch
                |> List.mapi (fun i _ ->
                    $"(STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), {{0}}, {{1}}, STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), {{{i + 2}}})")
            let format =
                "INSERT INTO memberships (created_at, involvement, room_id, updated_at, user_id) VALUES "
                + String.concat ", " rows
                + " ON CONFLICT DO NOTHING"
            let args: (obj | null) array =
                Array.ofList (box (Ef.Names.involvementToDb involvement) :: box roomId :: (batch |> List.map box))
                |> Array.map (fun o -> o)
            tx.Db.Database.ExecuteSql(FormattableStringFactory.Create(format, args)) |> ignore

    /// `memberships.grant_to(users)`: with the room's default involvement.
    let grantTo (tx: Ef.Tx) (room: Room) (userIds: int64 list) : unit =
        insertMemberships tx room.Id (RoomType.defaultInvolvement room.Type) userIds

    /// `memberships.grant_to(User.active)`, from `Rooms::Open`'s `after_save_commit`: one `INSERT ... SELECT`.
    let private grantToActiveUsers (tx: Ef.Tx) (roomId: int64) : unit =
        let involvement = Ef.Names.involvementToDb (RoomType.defaultInvolvement RoomType.Open)
        tx.Db.Database.ExecuteSql(
            $"INSERT INTO memberships (created_at, involvement, room_id, updated_at, user_id) SELECT STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), {involvement}, {roomId}, STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), users.id FROM users WHERE users.status = 0 ON CONFLICT DO NOTHING"
        )
        |> ignore

    // ---- Creating

    /// `Rooms::<Type>.create!(name:, creator:)`. An open room grants itself to every active user after commit
    /// (`Rooms::Open#grant_access_to_all_users`).
    let create (tx: Ef.Tx) (roomType: RoomType) (name: string option) (creatorId: int64) : Room =
        let room =
            Ef.Data.insert
                tx.Db
                { Id = 0L
                  Name = Option.toObj name
                  Type = roomType
                  CreatorId = creatorId
                  CreatedAt = DateTime.MinValue
                  UpdatedAt = DateTime.MinValue }
        if roomType = RoomType.Open then
            let id = room.Id
            tx.AfterCommit(fun tx -> grantToActiveUsers tx id)
        room

    /// `Room.create_for(attributes, users:)`
    let createFor (tx: Ef.Tx) (roomType: RoomType) (name: string option) (creatorId: int64) (userIds: int64 list) : Room =
        let room = create tx roomType name creatorId
        grantTo tx room userIds
        room

    /// `room.user_ids`
    let userIds (db: Ef.CampfireDb) (room: Room) : int64 list =
        let id = room.Id
        db.Memberships.Where(fun m -> m.RoomId = EF.Parameter id).Select(fun m -> m.UserId).ToList() |> List.ofSeq

    /// `Rooms::Direct.find_for`: the direct room whose members are exactly `userIds'` (candidates are the first user's directs).
    let findDirectFor (db: Ef.CampfireDb) (userIds': int64 list) : Room option =
        match userIds' with
        | [] -> None
        | first :: _ ->
            let wanted = Set.ofList userIds'
            forUserOfType db first RoomType.Direct |> List.tryFind (fun room -> Set.ofList (userIds db room) = wanted)

    /// `Rooms::Direct.find_or_create_for(users)`: the direct room whose members are exactly `user_ids`, created (by
    /// `creator_id`, i.e. `Current.user`) if there isn't one.
    let findOrCreateDirectFor (tx: Ef.Tx) (userIds: int64 list) (creatorId: int64) : Room =
        match findDirectFor tx.Db userIds with
        | Some room -> room
        | None -> createFor tx RoomType.Direct None creatorId userIds

    // ---- Updating

    /// `room.update!(name:, type:)`. A direct room can't change type (`direct_rooms_keep_their_type`). Becoming open grants every
    /// active user after commit.
    let update (tx: Ef.Tx) (room: Room) (name: string option option) (roomType: RoomType option) : Room =
        let name = name |> Option.filter (fun n -> n <> Option.ofObj room.Name)
        let roomType = roomType |> Option.filter (fun t -> t <> room.Type)
        if roomType.IsSome && room.Type = RoomType.Direct then
            Err.validate (Errors.empty |> Errors.add "type" "can't be changed for a direct room")
        if name.IsNone && roomType.IsNone then
            room
        else
            let updated =
                { room with
                    Name = (match name with Some n -> Option.toObj n | None -> room.Name)
                    Type = defaultArg roomType room.Type }
            let saved = Ef.Data.update tx.Db room updated
            if roomType = Some RoomType.Open then
                let id = room.Id
                tx.AfterCommit(fun tx -> grantToActiveUsers tx id)
            saved

    /// `touch`: `belongs_to :room, touch: true` on messages.
    let touch (tx: Ef.Tx) (roomId: int64) : unit =
        let now = tx.Now()
        tx.Db.Rooms
            .Where(fun r -> r.Id = EF.Parameter roomId)
            .ExecuteUpdate(fun s -> s.SetProperty((fun r -> r.UpdatedAt), now) |> ignore)
        |> ignore

    /// `memberships.where.not(involvement: :invisible).disconnected.where.not(user: creator).update_all(unread_at: message.created_at)`
    /// (SQL's `!=` leaves a NULL involvement out, as `HasValue` does here).
    let private unreadMemberships (tx: Ef.Tx) (roomId: int64) (message: Message) : unit =
        let now = tx.Now()
        let cutoff = Membership.connectionCutoff now
        let creatorId = message.CreatorId
        let createdAt = message.CreatedAt
        tx.Db.Memberships
            .Where(fun m ->
                m.RoomId = EF.Parameter roomId
                && m.Involvement.HasValue
                && m.Involvement.Value <> Involvement.Invisible
                && (not m.ConnectedAt.HasValue || m.ConnectedAt.Value < EF.Parameter cutoff)
                && m.UserId <> EF.Parameter creatorId)
            .ExecuteUpdate(fun s ->
                s.SetProperty((fun m -> m.UnreadAt), Nullable createdAt).SetProperty((fun m -> m.UpdatedAt), now) |> ignore)
        |> ignore

    /// `room.receive(message)`, from the message's `after_create_commit`: marks members unread, then enqueues the push.
    let receive (tx: Ef.Tx) (roomId: int64) (message: Message) : unit =
        unreadMemberships tx roomId message
        tx.EmitAfterCommit(PushMessage(roomId, message.Id))

    /// `room.destroy`: the messages (`dependent: :destroy`, each with its callbacks), then memberships, deleted without
    /// callbacks, then the room.
    let destroy (tx: Ef.Tx) (room: Room) : unit =
        let id = room.Id
        for message in Message.forRoom tx.Db id do
            Message.destroy tx message
        tx.Db.Memberships.Where(fun m -> m.RoomId = EF.Parameter id).ExecuteDelete() |> ignore
        tx.Db.Rooms.Where(fun r -> r.Id = EF.Parameter id).ExecuteDelete() |> ignore

    /// `memberships.revoke_from(users)`: `destroy_by`, so each removed member is disconnected (with reconnect) after commit.
    let revokeFrom (tx: Ef.Tx) (room: Room) (userIds: int64 list) : unit =
        let id = room.Id
        let ids = Array.ofList userIds
        let revoked =
            tx.Db.Memberships.Where(fun m -> m.RoomId = EF.Parameter id && Enumerable.Contains(EF.Parameter ids, m.UserId)).ToList()
        for membership in revoked do
            Membership.destroy tx membership

    /// `memberships.revise(granted:, revoked:)`
    let revise (tx: Ef.Tx) (room: Room) (granted: int64 list) (revoked: int64 list) : unit =
        if not (List.isEmpty granted) then grantTo tx room granted
        if not (List.isEmpty revoked) then revokeFrom tx room revoked

    let private members (db: Ef.CampfireDb) (room: Room) : IQueryable<User> =
        let id = room.Id
        db.Users.Join(db.Memberships.Where(fun m -> m.RoomId = EF.Parameter id), (fun u -> u.Id), (fun m -> m.UserId), (fun u _ -> u))

    /// `room.users`
    let users (db: Ef.CampfireDb) (room: Room) : User list = (members db room).ToList() |> List.ofSeq

    /// `room.users.active_bots`
    let activeBots (db: Ef.CampfireDb) (room: Room) : User list =
        (members db room).Where(fun u -> u.Status = Status.Active && u.Role = Role.Bot).ToList() |> List.ofSeq

    let isOpen (room: Room) : bool = room.Type = RoomType.Open

    let isClosed (room: Room) : bool = room.Type = RoomType.Closed

    let isDirect (room: Room) : bool = room.Type = RoomType.Direct

    let defaultInvolvement (room: Room) : Involvement = RoomType.defaultInvolvement room.Type

    let reload (db: Ef.CampfireDb) (room: Room) : Room = find db room.Id

// ---------------------------------------------------------------------------------------------
// Message
// `reference/app/models/message.rb` and `message/*.rb` (Attachment, Mentionee, Pagination,
// Searchable; Broadcasts belong to the app).
// ---------------------------------------------------------------------------------------------

/// Attributes for `room.messages.create!` / `create_with_attachment!`.
type NewMessage =
    { RoomId: int64
      CreatorId: int64
      /// Defaults to a random UUID (`before_create`; "Bots don't care").
      ClientMessageId: string option
      /// The stored Action Text body, if one was assigned.
      Body: string option
      /// An already-saved blob to attach as `attachment`.
      AttachmentBlobId: int64 option }

module NewMessage =
    let create (roomId: int64) (creatorId: int64) : NewMessage =
        { RoomId = roomId
          CreatorId = creatorId
          ClientMessageId = None
          Body = None
          AttachmentBlobId = None }

type ContentType =
    | Attachment
    | Sound
    | Text

module ContentType =
    let name (contentType: ContentType) : string =
        match contentType with
        | Attachment -> "attachment"
        | Sound -> "sound"
        | Text -> "text"

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Message =
    /// `Message::Pagination::PAGE_SIZE`
    [<Literal>]
    let PageSize = 40L

    [<Literal>]
    let private RecordType = "Message"

    /// `room.messages`
    let private inRoom (db: Ef.CampfireDb) (roomId: int64) : IQueryable<Message> =
        db.Messages.Where(fun m -> m.RoomId = EF.Parameter roomId)

    let findById (db: Ef.CampfireDb) (id: int64) : Message option =
        db.Messages.Where(fun m -> m.Id = EF.Parameter id) |> Ef.Data.tryFirst

    let find (db: Ef.CampfireDb) (id: int64) : Message = findById db id |> Err.orNotFound "Message"

    /// `Message.last`
    let last (db: Ef.CampfireDb) : Message option = db.Messages.OrderByDescending(fun m -> m.Id) |> Ef.Data.tryFirst

    let count (db: Ef.CampfireDb) : int64 = db.Messages.LongCount()

    /// `user.messages`
    let byCreator (db: Ef.CampfireDb) (creatorId: int64) : Message list =
        db.Messages.Where(fun m -> m.CreatorId = EF.Parameter creatorId).ToList() |> List.ofSeq

    /// `room.messages`
    let forRoom (db: Ef.CampfireDb) (roomId: int64) : Message list = (inRoom db roomId).ToList() |> List.ofSeq

    /// `room.messages.find(id)`
    let findInRoom (db: Ef.CampfireDb) (roomId: int64) (id: int64) : Message =
        (inRoom db roomId).Where(fun m -> m.Id = EF.Parameter id) |> Ef.Data.tryFirst |> Err.orNotFound "Message"

    /// `room.messages.count`
    let countInRoom (db: Ef.CampfireDb) (roomId: int64) : int64 = (inRoom db roomId).LongCount()

    /// `Current.user.reachable_messages`: the messages of the rooms the user is a member of.
    let private reachable (db: Ef.CampfireDb) (userId: int64) : IQueryable<Message> =
        db.Messages.Join(db.Memberships.Where(fun ms -> ms.UserId = EF.Parameter userId), (fun m -> m.RoomId), (fun ms -> ms.RoomId), (fun m _ -> m))

    /// `Current.user.reachable_messages.find(id)`
    let findReachable (db: Ef.CampfireDb) (userId: int64) (id: int64) : Message =
        (reachable db userId).Where(fun m -> m.Id = EF.Parameter id) |> Ef.Data.tryFirst |> Err.orNotFound "Message"

    // Message::Pagination

    /// `room.messages.last_page`: the newest 40, oldest first.
    let lastPage (db: Ef.CampfireDb) (roomId: int64) : Message list =
        (inRoom db roomId).OrderByDescending(fun m -> m.CreatedAt).Take(40).ToList() |> List.ofSeq |> List.rev

    /// `room.messages.first_page`
    let firstPage (db: Ef.CampfireDb) (roomId: int64) : Message list =
        (inRoom db roomId).OrderBy(fun m -> m.CreatedAt).Take(40).ToList() |> List.ofSeq

    /// `room.messages.page_before(message)`
    let pageBefore (db: Ef.CampfireDb) (roomId: int64) (message: Message) : Message list =
        let at = message.CreatedAt
        (inRoom db roomId).Where(fun m -> m.CreatedAt < EF.Parameter at).OrderByDescending(fun m -> m.CreatedAt).Take(40).ToList()
        |> List.ofSeq
        |> List.rev

    /// `room.messages.page_after(message)`
    let pageAfter (db: Ef.CampfireDb) (roomId: int64) (message: Message) : Message list =
        let at = message.CreatedAt
        (inRoom db roomId).Where(fun m -> m.CreatedAt > EF.Parameter at).OrderBy(fun m -> m.CreatedAt).Take(40).ToList() |> List.ofSeq

    /// `room.messages.page_around(message)`: up to 40 before, the message, up to 40 after.
    let pageAround (db: Ef.CampfireDb) (roomId: int64) (message: Message) : Message list =
        pageBefore db roomId message @ [ message ] @ pageAfter db roomId message

    /// `room.messages.page_created_since(time)`
    let pageCreatedSince (db: Ef.CampfireDb) (roomId: int64) (time: DateTime) : Message list =
        (inRoom db roomId).Where(fun m -> m.CreatedAt > EF.Parameter time).OrderBy(fun m -> m.CreatedAt).Take(40).ToList() |> List.ofSeq

    /// `room.messages.without(excluding).page_updated_since(time)`
    let pageUpdatedSince (db: Ef.CampfireDb) (roomId: int64) (time: DateTime) (excluding: int64 list) : Message list =
        let candidates = inRoom db roomId
        let candidates =
            if List.isEmpty excluding then
                candidates
            else
                let ids = Array.ofList excluding
                candidates.Where(fun m -> not (Enumerable.Contains(EF.Parameter ids, m.Id)))
        candidates.Where(fun m -> m.UpdatedAt > EF.Parameter time).OrderByDescending(fun m -> m.CreatedAt).Take(40).ToList()
        |> List.ofSeq
        |> List.rev

    /// `room.messages.before(message).exists?`
    let existsBefore (db: Ef.CampfireDb) (roomId: int64) (message: Message) : bool =
        let at = message.CreatedAt
        (inRoom db roomId).Any(fun m -> m.CreatedAt < EF.Parameter at)

    /// `room.messages.after(message).exists?`
    let existsAfter (db: Ef.CampfireDb) (roomId: int64) (message: Message) : bool =
        let at = message.CreatedAt
        (inRoom db roomId).Any(fun m -> m.CreatedAt > EF.Parameter at)

    /// `room.messages.paged?`: more than a page (`count > PAGE_SIZE`). Asks whether a row exists past the first page rather than
    /// counting the whole room, which grows without bound.
    let paged (db: Ef.CampfireDb) (roomId: int64) : bool = (inRoom db roomId).OrderBy(fun m -> m.Id).Skip(40).Any()

    // Message::Searchable

    /// Each word of a search as an FTS5 string, so every word must appear and none is read as query
    /// syntax. Rails passes the words straight to `MATCH`, where `NOT`, `AND`, `OR` or `NEAR` in the
    /// wrong place is a syntax error (a 500).
    /// NULs separate words too: SQLite would end the query string at one.
    let private matchTerms (query: string) : string =
        query.Split([| '\000' |])
        |> Array.collect (fun part -> part.Split(Array.empty<char>, StringSplitOptions.RemoveEmptyEntries))
        |> Array.map (fun word -> "\"" + word.Replace("\"", "\"\"") + "\"")
        |> String.concat " "

    /// `room.messages.search(query)`: FTS5 `MATCH` (EF has no FTS5, so `FromSql`; E13), in message order.
    let searchInRoom (db: Ef.CampfireDb) (roomId: int64) (query: string) : Message list =
        let terms = matchTerms query
        if terms = "" then
            []
        else
            db.Messages
                .FromSql($"SELECT messages.* FROM messages JOIN message_search_index idx ON messages.id = idx.rowid WHERE messages.room_id = {roomId} AND (idx.body MATCH {terms})")
                .OrderBy(fun m -> m.CreatedAt)
                .ToList()
            |> List.ofSeq

    /// `Current.user.reachable_messages.search(query).last(100)`
    let searchReachable (db: Ef.CampfireDb) (userId: int64) (query: string) : Message list =
        let terms = matchTerms query
        if terms = "" then
            []
        else
            db.Messages
                .FromSql(
                    $"SELECT messages.* FROM messages INNER JOIN rooms ON messages.room_id = rooms.id INNER JOIN memberships ON rooms.id = memberships.room_id JOIN message_search_index idx ON messages.id = idx.rowid WHERE memberships.user_id = {userId} AND (idx.body MATCH {terms})"
                )
                .OrderByDescending(fun m -> m.CreatedAt)
                .Take(100)
                .ToList()
            |> List.ofSeq
            |> List.rev

    // Attributes and associations

    let room (db: Ef.CampfireDb) (message: Message) : Room = Room.find db message.RoomId

    let creator (db: Ef.CampfireDb) (message: Message) : User = User.find db message.CreatorId

    let boosts (db: Ef.CampfireDb) (message: Message) : Boost list = Boost.forMessageOrdered db message.Id

    let body (db: Ef.CampfireDb) (message: Message) : RichTextRecord option = RichTextRecord.findFor db RecordType message.Id "body"

    /// `message.body.body` as stored HTML, if any.
    let bodyHtml (db: Ef.CampfireDb) (message: Message) : string option =
        body db message |> Option.bind (fun b -> Option.ofObj b.Body)

    /// `has_one_attached :attachment`'s row, if attached.
    let private attachmentRow (db: Ef.CampfireDb) (messageId: int64) : Ef.Attachment option =
        db.Attachments.Where(fun a -> a.RecordId = EF.Parameter messageId && a.RecordType = RecordType && a.Name = "attachment")
        |> Ef.Data.tryFirst

    /// The attachment and its blob, if attached.
    let attachment (db: Ef.CampfireDb) (message: Message) : (Ef.Attachment * Ef.Blob) option =
        match attachmentRow db message.Id with
        | Some attachment ->
            let blobId = attachment.BlobId
            let blob = db.Blobs.Where(fun b -> b.Id = EF.Parameter blobId) |> Ef.Data.tryFirst |> Err.orNotFound "ActiveStorage::Blob"
            Some(attachment, blob)
        | None -> None

    /// `plain_text_body`: `body.to_plain_text.presence || attachment&.filename&.to_s || ""`
    let plainTextBody (db: Ef.CampfireDb) (richText: RichText) (message: Message) : string =
        let fromBody =
            match bodyHtml db message with
            | Some html ->
                let text = richText.ToPlainText(db, html)
                if not (String.IsNullOrWhiteSpace text) then Some text else None
            | None -> None
        match fromBody with
        | Some text -> text
        | None ->
            match attachment db message with
            | Some(_, blob) -> blob.Filename
            | None -> ""

    /// `plain_text_body.match(/\A\/play (?<name>\w+)\z/)` then `Sound.find_by_name`.
    let soundIn (plainText: string) : Sound option =
        if not (plainText.StartsWith("/play ", StringComparison.Ordinal)) then
            None
        else
            let name = plainText.Substring 6
            // `\z` allows no trailing newline; `\w` is ASCII word characters.
            if name = "" || not (name |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || c = '_')) then None else Sound.findByName name

    /// `sound`: a body of exactly `/play <name>` naming a built-in sound.
    let sound (db: Ef.CampfireDb) (richText: RichText) (message: Message) : Sound option = soundIn (plainTextBody db richText message)

    /// `content_type`
    let contentType (db: Ef.CampfireDb) (richText: RichText) (message: Message) : ContentType =
        if (attachmentRow db message.Id).IsSome then ContentType.Attachment
        elif (sound db richText message).IsSome then ContentType.Sound
        else ContentType.Text

    /// `room.users.where(id: ids)`
    let mentioneesInRoom (db: Ef.CampfireDb) (roomId: int64) (userIds: int64 list) : User list =
        if List.isEmpty userIds then
            []
        else
            let ids = Array.ofList userIds
            db.Users
                .Join(db.Memberships.Where(fun ms -> ms.RoomId = EF.Parameter roomId), (fun u -> u.Id), (fun ms -> ms.UserId), (fun u _ -> u))
                .Where(fun u -> Enumerable.Contains(EF.Parameter ids, u.Id))
                .ToList()
            |> List.ofSeq

    /// `mentionees`: mentioned users who are members of the room.
    let mentionees (db: Ef.CampfireDb) (richText: RichText) (message: Message) : User list =
        let ids =
            match bodyHtml db message with
            | Some html -> richText.MentionedUserIds(db, html)
            | None -> []
        mentioneesInRoom db message.RoomId ids

    // Creating, updating, destroying

    /// The search index (E13): EF has no FTS5, so these are `ExecuteSql`, in after-commit hooks.
    let private createInIndex (tx: Ef.Tx) (message: Message) : unit =
        let plain = plainTextBody tx.Db tx.RichText message
        tx.Db.Database.ExecuteSql($"insert into message_search_index(rowid, body) values ({message.Id}, {plain})") |> ignore

    let private updateInIndex (tx: Ef.Tx) (message: Message) : unit =
        let plain = plainTextBody tx.Db tx.RichText message
        tx.Db.Database.ExecuteSql($"update message_search_index set body = {plain} where rowid = {message.Id}") |> ignore

    let private removeFromIndex (tx: Ef.Tx) (id: int64) : unit =
        tx.Db.Database.ExecuteSql($"delete from message_search_index where rowid = {id}") |> ignore

    let private touchRow (tx: Ef.Tx) (id: int64) : DateTime =
        let now = tx.Now()
        tx.Db.Messages
            .Where(fun m -> m.Id = EF.Parameter id)
            .ExecuteUpdate(fun s -> s.SetProperty((fun m -> m.UpdatedAt), now) |> ignore)
        |> ignore
        now

    let private createAttachment (tx: Ef.Tx) (messageId: int64) (blobId: int64) : unit =
        Ef.Data.insert
            tx.Db
            ({ Id = 0L; Name = "attachment"; RecordType = RecordType; RecordId = messageId; BlobId = blobId; CreatedAt = DateTime.MinValue }: Ef.Attachment)
        |> ignore

    /// `room.messages.create!` (or `create_with_attachment!` given a blob). Inside the
    /// transaction: the message, its body (which touches the message), its attachment, and
    /// the room touch. After commit: the search index, then `room.receive` (unread
    /// memberships and the push job).
    let create (tx: Ef.Tx) (attributes: NewMessage) : Message =
        let clientMessageId = attributes.ClientMessageId |> Option.defaultWith Sql.uuid
        let mutable message =
            Ef.Data.insert
                tx.Db
                { Id = 0L
                  RoomId = attributes.RoomId
                  CreatorId = attributes.CreatorId
                  ClientMessageId = clientMessageId
                  CreatedAt = DateTime.MinValue
                  UpdatedAt = DateTime.MinValue }
        let id = message.Id

        let mutable touched = false
        match attributes.Body with
        | Some body ->
            RichTextRecord.create tx RecordType id "body" body |> ignore
            touched <- true
        | None -> ()
        match attributes.AttachmentBlobId with
        | Some blobId ->
            createAttachment tx id blobId
            touched <- true
        | None -> ()
        if touched then message <- { message with UpdatedAt = touchRow tx id }
        Room.touch tx message.RoomId

        let committed = message
        tx.AfterCommit(fun tx ->
            createInIndex tx committed
            Room.receive tx committed.RoomId committed)
        message

    /// `touch` (from a boost, or the body): the message, then its room, then a reindex
    /// after commit.
    let touch (tx: Ef.Tx) (message: Message) : Message =
        let touched = { message with UpdatedAt = touchRow tx message.Id }
        Room.touch tx message.RoomId
        let id = message.Id
        tx.AfterCommit(fun tx -> updateInIndex tx (find tx.Db id))
        touched

    /// `message.update!(body:)`: the body changes, which touches the message and its room;
    /// the search index follows after commit.
    let updateBody (tx: Ef.Tx) (message: Message) (body: string) : Message =
        match RichTextRecord.findFor tx.Db RecordType message.Id "body" with
        | Some record when record.Body = body -> message
        | Some record ->
            RichTextRecord.updateBody tx record body |> ignore
            touch tx message
        | None ->
            RichTextRecord.create tx RecordType message.Id "body" body |> ignore
            touch tx message

    /// `update!(attachment: blob or nil)`: `has_one_attached` replaces the attachment. The old
    /// one is destroyed (its blob purged after commit, `dependent: :purge_later`) and each
    /// attachment change touches the message (`belongs_to :record, touch: true`), and so its room.
    let replaceAttachment (tx: Ef.Tx) (message: Message) (blobId: int64 option) : Message =
        let mutable message = message
        match attachmentRow tx.Db message.Id with
        | Some attachment ->
            Ef.Data.remove tx.Db attachment
            tx.EmitAfterCommit(PurgeBlob attachment.BlobId)
            message <- touch tx message
        | None -> ()
        match blobId with
        | Some blobId ->
            createAttachment tx message.Id blobId
            message <- touch tx message
        | None -> ()
        message

    /// `destroy`: its attachment (blob purged later), boosts and body, then the message, and
    /// the room is touched. The search index entry goes after commit.
    let destroy (tx: Ef.Tx) (message: Message) : unit =
        let db = tx.Db
        let id = message.Id
        match attachmentRow db id with
        | Some attachment ->
            Ef.Data.remove db attachment
            tx.EmitAfterCommit(PurgeBlob attachment.BlobId)
        | None -> ()
        db.Boosts.Where(fun b -> b.MessageId = EF.Parameter id).ExecuteDelete() |> ignore
        db.RichTexts.Where(fun r -> r.RecordId = EF.Parameter id && r.RecordType = RecordType && r.Name = "body").ExecuteDelete() |> ignore
        db.Messages.Where(fun m -> m.Id = EF.Parameter id).ExecuteDelete() |> ignore
        Room.touch tx message.RoomId
        tx.AfterCommit(fun tx -> removeFromIndex tx id)

    let reload (db: Ef.CampfireDb) (message: Message) : Message = find db message.Id

// ---------------------------------------------------------------------------------------------
// Boost
// `reference/app/models/boost.rb`
// ---------------------------------------------------------------------------------------------

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Boost =
    let find (db: Ef.CampfireDb) (id: int64) : Boost =
        db.Boosts.Where(fun b -> b.Id = EF.Parameter id) |> Ef.Data.tryFirst |> Err.orNotFound "Boost"

    let forMessage (db: Ef.CampfireDb) (messageId: int64) : Boost list =
        db.Boosts.Where(fun b -> b.MessageId = EF.Parameter messageId).ToList() |> List.ofSeq

    /// `message.boosts.ordered`
    let forMessageOrdered (db: Ef.CampfireDb) (messageId: int64) : Boost list =
        db.Boosts.Where(fun b -> b.MessageId = EF.Parameter messageId).OrderBy(fun b -> b.CreatedAt).ToList() |> List.ofSeq

    /// `message.boosts.find_by!(id:, booster:)`
    let findByMessageAndBooster (db: Ef.CampfireDb) (messageId: int64) (id: int64) (boosterId: int64) : Boost =
        db.Boosts.Where(fun b -> b.MessageId = EF.Parameter messageId && b.Id = EF.Parameter id && b.BoosterId = EF.Parameter boosterId)
        |> Ef.Data.tryFirst
        |> Err.orNotFound "Boost"

    /// `message.boosts.create!(content:, booster:)`: touches the message (and so the room).
    let create (tx: Ef.Tx) (messageId: int64) (boosterId: int64) (content: string) : Boost =
        let boost =
            Ef.Data.insert
                tx.Db
                { Id = 0L
                  MessageId = messageId
                  BoosterId = boosterId
                  Content = content
                  CreatedAt = DateTime.MinValue
                  UpdatedAt = DateTime.MinValue }
        Message.touch tx (Message.find tx.Db messageId) |> ignore
        boost

    /// `destroy!`: touches the message (and so the room).
    let destroy (tx: Ef.Tx) (boost: Boost) : unit =
        Ef.Data.remove tx.Db boost
        Message.touch tx (Message.find tx.Db boost.MessageId) |> ignore

// ---------------------------------------------------------------------------------------------
// Webhook
// `reference/app/models/webhook.rb`: the row and the payload. Delivery (HTTP, replies)
// lives in the app.
// ---------------------------------------------------------------------------------------------

type Webhook =
    { Id: int64
      UserId: int64
      Url: string option
      CreatedAt: Timestamp
      UpdatedAt: Timestamp }

module Webhook =
    /// `Webhook::ENDPOINT_TIMEOUT`
    [<Literal>]
    let EndpointTimeoutSeconds = 7

    let private fromRow (r: Row) : Webhook =
        { Id = r.Int64 0
          UserId = r.Int64 1
          Url = r.OptText 2
          CreatedAt = r.Timestamp 3
          UpdatedAt = r.Timestamp 4 }

    let findByUser (conn: Conn) (userId: int64) : Webhook option =
        conn.QueryOne(Selects.Webhook + " WHERE \"webhooks\".\"user_id\" = ? LIMIT 1", [| I userId |], fromRow)

    /// `create_webhook!(url:)`
    let create (tx: Tx) (userId: int64) (url: string option) : Webhook =
        let now = tx.Now()
        let id =
            tx.Conn.QueryRow(
                """INSERT INTO "webhooks" ("created_at", "updated_at", "url", "user_id") VALUES (?, ?, ?, ?) RETURNING "id" """,
                [| T now; T now; Sql.optS url; I userId |],
                fun r -> r.Int64 0
            )
        { Id = id
          UserId = userId
          Url = url
          CreatedAt = now
          UpdatedAt = now }

    /// `webhook.update!(url:)`
    let updateUrl (tx: Tx) (webhook: Webhook) (url: string) : Webhook =
        if webhook.Url = Some url then
            webhook
        else
            let now = tx.Now()
            tx.Conn.Execute(
                """UPDATE "webhooks" SET "updated_at" = ?, "url" = ? WHERE "webhooks"."id" = ?""",
                [| T now; S url; I webhook.Id |]
            )
            |> ignore
            { webhook with Url = Some url; UpdatedAt = now }

    let destroy (tx: Tx) (webhook: Webhook) : unit =
        tx.Conn.Execute("""DELETE FROM "webhooks" WHERE "webhooks"."id" = ?""", [| I webhook.Id |]) |> ignore

    /// Removes `@Recipient` mentions and leading/trailing (Unicode) whitespace.
    let private withoutRecipientMentions (body: string) (recipient: User) : string =
        body.Replace(User.attachablePlainTextRepresentation recipient, "").Trim()

    /// The JSON body `deliver` posts. `roomBotMessagesPath` and `messagePath` come from
    /// the route helpers (`room_bot_messages_path(room, bot_key)`, `room_at_message_path(room, message)`).
    let payload
        (db: Ef.CampfireDb)
        (richText: RichText)
        (webhook: Ef.Webhook)
        (message: Message)
        (roomBotMessagesPath: string)
        (messagePath: string)
        : string =
        let creator = Message.creator db message
        let room = Room.find db message.RoomId
        let recipient = User.find db webhook.UserId
        let html = Message.bodyHtml db message
        let plain = withoutRecipientMentions (Message.plainTextBody db richText message) recipient

        let optional (value: string option) =
            match value with
            | Some s -> Value.String s
            | None -> Value.Null

        // Hash order as written in `Webhook#payload`.
        let body =
            Value.Object
                [ "user", Value.Object [ "id", Value.Int creator.Id; "name", Value.String creator.Name ]
                  "room", Value.Object [ "id", Value.Int room.Id; "name", optional (Option.ofObj room.Name); "path", Value.String roomBotMessagesPath ]
                  "message",
                  Value.Object
                      [ "id", Value.Int message.Id
                        "body", Value.Object [ "html", optional html; "plain", Value.String plain ]
                        "path", Value.String messagePath ] ]
        Json.encode body
