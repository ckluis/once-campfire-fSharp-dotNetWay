// Port of rust/crates/campfire/src/controllers/presenters/accounts.rs
//
// Maps database rows into the view models `Campfire.Views` renders for the account, session and
// user screens (sessions, first run, users, accounts, autocompletable, pwa): what the Rails views
// read off `@user`, `Current.account` and friends, computed up front. The `ViewContext` builder
// for every page is `Layout` (`ViewContext.fs`).
//
// Queries the db library doesn't have yet are written here against the reference's SQL.
// (`touch` is in `Common`: `Attachments` needs it and F# files are ordered.)
namespace Campfire.App.Presenters

open System
open System.Linq
open System.Text
open Microsoft.EntityFrameworkCore
open Campfire.App
open Campfire.Db
open Campfire.Kit
open Campfire.RailsCompat
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Accounts
open Campfire.Views.Users

/// The sidebar of `Users::SidebarsController#show`.
type Sidebar =
    { DirectMemberships: SidebarDirectItem list
      OtherMemberships: SidebarRoom list
      DirectPlaceholderUsers: UserSummary list }

module Accounts =
    /// `User::Transferable::TRANSFER_LINK_EXPIRY_DURATION`
    let TransferLinkExpiry: TimeSpan = TimeSpan.FromHours 4.0

    /// `user.transfer_id`: `signed_id(purpose: :transfer, expires_in: 4.hours)`.
    let transferId (secrets: Secrets) (userId: int64) (now: DateTimeOffset) : string =
        SignedId.generate secrets "User" userId (Some "transfer") (Some(now + TransferLinkExpiry))

    /// `User.find_by_transfer_id(id)`: the user id, if the signature and expiry check out.
    let userIdFromTransferId (secrets: Secrets) (transferId: string) (now: DateTimeOffset) : int64 option =
        SignedId.verify secrets "User" transferId (Some "transfer") now

    /// `User.from_avatar_token(sid)`'s verification half (`find_signed!(sid, purpose: :avatar)`).
    let userIdFromAvatarToken (secrets: Secrets) (token: string) (now: DateTimeOffset) : int64 option =
        SignedId.verify secrets "User" token (Some "avatar") now

    /// `user.attachable_sgid`.
    let attachableSgid (secrets: Secrets) (userId: int64) : string =
        GlobalId.attachableSgid secrets (GlobalId.create "User" (string userId))

    /// `fresh_account_logo_path(size:)`: `v` is `Current.account&.updated_at&.to_fs(:number)`.
    let freshAccountLogoPath (account: Account option) (size: string option) : string =
        let v = account |> Option.map (fun (account: Account) -> Common.toFsNumberOf account.UpdatedAt)
        Routes.freshAccountLogo v size

    /// `platform` as the views see it (`ApplicationPlatform.new(request.user_agent)`).
    let platform (c: Ctx) : Platform = ApplicationPlatform.toView (Current.platform c)

    /// `User.administrator.first`, for `accounts/_help_contact`.
    let helpContact (db: Campfire.Db.Ef.CampfireDb) : HelpContact option =
        db.Users.Where(fun u -> u.Role = Campfire.Db.Role.Administrator).OrderBy(fun u -> u.Id)
        |> Campfire.Db.Ef.Data.tryFirst
        |> Option.map (fun user -> { Name = user.Name; EmailAddress = defaultArg (Option.ofObj user.EmailAddress) "" })

    /// `User.none?`
    let noUsers (db: Campfire.Db.Ef.CampfireDb) : bool = User.count db = 0L

    // --- Users -----------------------------------------------------------------------------------------

    /// A user for `users/_mention` and the autocompletable views.
    let mentionUser (secrets: Secrets) (user: User) : MentionUser =
        { User = Common.userSummary secrets user
          AttachableSgid = attachableSgid secrets user.Id }

    /// `room_display_name(room, for_user:)`: a direct room is named after its other members.
    let roomDisplayName (db: Campfire.Db.Ef.CampfireDb) (room: Room) (forUser: User) : string =
        let names =
            if Room.isDirect room then
                Room.users db room |> List.filter (fun user -> user.Id <> forUser.Id) |> List.map (fun user -> user.Name)
            else
                []
        Rooms.roomDisplayName (Option.ofObj room.Name) (Room.isDirect room) names (Some forUser.Name)

    /// `Room.model_name.param_key` for the room's STI class.
    let roomParamKey (roomType: RoomType) : string =
        match roomType with
        | RoomType.Open -> "rooms_open"
        | RoomType.Closed -> "rooms_closed"
        | _ -> "rooms_direct"

    /// Users::ProfilesController#show: `Current.user.memberships.with_ordered_room.partition { |m| m.room.direct? }`,
    /// returned as `(direct, shared)`.
    let profileMemberships (db: Campfire.Db.Ef.CampfireDb) (user: User) : ProfileMembership list * ProfileMembership list =
        let views =
            Membership.withOrderedRoom db user.Id
            |> List.map (fun (membership, room) ->
                { RoomId = room.Id
                  RoomParamKey = roomParamKey room.Type
                  RoomDisplayName = roomDisplayName db room user
                  Involvement = (if membership.Involvement.HasValue then Involvement.name membership.Involvement.Value else "")
                  Direct = Room.isDirect room })
        let direct, shared = views |> List.partition (fun view -> view.Direct)
        direct, shared

    // --- Sidebar -----------------------------------------------------------------------------------------

    /// `Users::SidebarsController::DIRECT_PLACEHOLDERS`
    [<Literal>]
    let DirectPlaceholders = 20L

    /// `users/sidebars/rooms/_direct` locals: `room.users.without(membership.user).presence || [ membership.user ]`.
    let sidebarDirect (db: Campfire.Db.Ef.CampfireDb) (secrets: Secrets) (membership: Membership) (room: Room) : SidebarDirect =
        let users = Room.users db room
        let members =
            match users |> List.filter (fun user -> user.Id <> membership.UserId) with
            | [] -> [ User.find db membership.UserId ]
            | others -> others
        { RoomId = room.Id
          Unread = Membership.unread membership
          UpdatedAtEpoch = Common.epochStringOf room.UpdatedAt
          Members = members |> List.map (Common.userSummary secrets)
          MembershipId = membership.Id
          MembershipUpdatedAt = DateTimeOffset(membership.UpdatedAt) }

    /// `find_direct_placeholder_users`. `exclude_user_ids` is `Membership.where(room_id: directs).pluck(:user_id).uniq`
    /// `.including(Current.user.id)`: `including` appends even when the id is already there, and the
    /// limit counts that duplicate.
    let private directPlaceholderUsers (db: Campfire.Db.Ef.CampfireDb) (secrets: Secrets) (user: User) : UserSummary list =
        let excluded = ResizeArray<int64>()
        for id in Room.userIdsOfDirectRoomsOf db user.Id do
            if not (excluded.Contains id) then excluded.Add id
        excluded.Add user.Id
        let limit = int (max (DirectPlaceholders - int64 excluded.Count) 0L)
        User.activeExcluding db (excluded.ToArray()) limit
        |> List.map (Common.userSummary secrets)

    /// Users::SidebarsController#show.
    let sidebar (db: Campfire.Db.Ef.CampfireDb) (secrets: Secrets) (user: User) : Sidebar =
        let allMemberships = Membership.visibleWithOrderedRoom db user.Id

        // `select { direct }.sort_by { |m| m.room.updated_at }.reverse`
        let direct =
            allMemberships
            |> List.filter (fun (_, room) -> Room.isDirect room)
            |> List.sortBy (fun (_, room) -> room.UpdatedAt)
            |> List.rev

        let directMemberships =
            direct
            |> List.map (fun (membership, room) ->
                // `cache membership` wraps the whole partial: on a hit Rails loads none of its members.
                match Users.cachedDirectRoomFragment membership.Id (DateTimeOffset(membership.UpdatedAt)) with
                | null -> View(sidebarDirect db secrets membership room)
                | html -> Cached html)
        let otherMemberships =
            allMemberships
            |> List.filter (fun (membership, _) -> not (direct |> List.exists (fun (d, _) -> d.Id = membership.Id)))
            |> List.map (fun (membership, room) ->
                { Id = room.Id
                  ParamKey = roomParamKey room.Type
                  Name = defaultArg (Option.ofObj room.Name) ""
                  Unread = Membership.unread membership })
        { DirectMemberships = directMemberships
          OtherMemberships = otherMemberships
          DirectPlaceholderUsers = directPlaceholderUsers db secrets user }

    // --- Account ---------------------------------------------------------------------------------------

    /// AccountsController#account_users: `User.where(status: [ :active, :banned ])` for
    /// administrators, `User.active` otherwise; `.ordered.without_bots`.
    let accountUsers (db: Campfire.Db.Ef.CampfireDb) (canAdminister: bool) : User list =
        let query = db.Users.Where(fun u -> u.Role <> Campfire.Db.Role.Bot)
        let visible =
            if canAdminister then
                query.Where(fun u -> u.Status = Campfire.Db.Status.Active || u.Status = Campfire.Db.Status.Banned)
            else
                query.Where(fun u -> u.Status = Campfire.Db.Status.Active)
        visible.OrderBy(fun u -> u.Name.ToLower()).ToList() |> List.ofSeq

    // --- Helpers ---------------------------------------------------------------------------------------

    /// `ORDER BY LOWER(name)`: SQLite lowercases ASCII only. Compared as UTF-8 bytes, the way
    /// SQLite's BINARY collation does.
    let sortByLowerName (name: 'T -> string) (items: 'T list) : 'T list =
        let lower (text: string) = String.map (fun c -> if c >= 'A' && c <= 'Z' then char (int c + 32) else c) text
        let compare (a: string) (b: string) =
            ReadOnlySpan<byte>(Encoding.UTF8.GetBytes a).SequenceCompareTo(ReadOnlySpan<byte>(Encoding.UTF8.GetBytes b))
        // Stable, as `sort_by_cached_key` is.
        items |> List.map (fun item -> lower (name item), item) |> List.sortWith (fun (a, _) (b, _) -> compare a b) |> List.map snd

    /// A bot row for `accounts/bots/_bot`: its key and `bot.rooms.without_directs.ordered`.
    let bot (db: Campfire.Db.Ef.CampfireDb) (secrets: Secrets) (bot: User) : Bot =
        let rooms = Room.forUserWithoutDirects db bot.Id |> sortByLowerName (fun room -> defaultArg (Option.ofObj room.Name) "")
        { User = Common.userSummary secrets bot
          BotKey = User.botKey bot
          Rooms = rooms |> List.map (fun room -> { Id = room.Id; Name = defaultArg (Option.ofObj room.Name) "" }) }

    /// The fields `accounts/bots/_form` fills in: `image_tag bot.avatar` is the blob's absolute
    /// redirect URL.
    let botForm (db: Campfire.Db.Ef.CampfireDb) (storage: Campfire.Storage.Storage) (baseUrl: string) (bot: User) (webhookUrl: string option) : BotForm =
        let avatar = Attachments.attachedBlob db "User" bot.Id "avatar"
        { Name = Some bot.Name
          WebhookUrl = webhookUrl
          AvatarAttachmentUrl =
            avatar |> Option.map (fun blob -> baseUrl + Campfire.Storage.Paths.blobRedirectPath storage.Verifier blob None) }

    /// A push subscription with its user agent parsed like `UserAgent.parse(push_subscription.user_agent)`.
    let pushSubscription (subscription: Campfire.Db.Ef.PushSubscription) : Campfire.Views.Users.PushSubscription =
        let agent = UserAgent.parse (defaultArg (Option.ofObj subscription.UserAgent) "")
        { Id = subscription.Id
          Endpoint = defaultArg (Option.ofObj subscription.Endpoint) ""
          Browser = UserAgent.Agent.browser agent
          Version = (UserAgent.Agent.version agent).String
          Platform = defaultArg (UserAgent.Agent.platform agent) "" }

