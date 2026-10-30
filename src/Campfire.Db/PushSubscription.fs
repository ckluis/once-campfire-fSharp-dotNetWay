// Port of rust/crates/db/src/models/push_subscription.rs
// `reference/app/models/push/subscription.rb`, and the subscriber queries of
// `reference/app/models/room/message_pusher.rb`, on EF Core: LINQ, every captured value in `EF.Parameter`. The record is
// `Ef.PushSubscription`. Delivery lives in the app.
namespace Campfire.Db

open System
open System.Linq
open System.Text
open Microsoft.EntityFrameworkCore
open Campfire.Db.Ef

/// What `Room::MessagePusher#build_payload` sends.
type PushPayload = { Title: string; Body: string; Path: string }

/// The parts of `URI.parse(endpoint)` the validation looks at.
type internal EndpointUri =
    { Scheme: string
      Host: string
      Port: int option }

module PushSubscription =
    /// `Push::Subscription::PERMITTED_ENDPOINT_HOSTS`
    let permittedEndpointHosts : string list =
        [ "jmt17.google.com"; "fcm.googleapis.com"; "updates.push.services.mozilla.com"; "web.push.apple.com"; "notify.windows.com" ]

    /// How long a payload's title and body may be, counted as the bytes they take in the JSON
    /// message. An encrypted Web Push record holds at most 4096 bytes: 4078 of JSON once the padding
    /// and tag are in, and the icon, path and badge take under 150 of that.
    [<Literal>]
    let MaxPayloadTitleBytes = 256

    [<Literal>]
    let MaxPayloadBodyBytes = 3072

    /// An unsaved subscription, for validation.
    let build
        (userId: int64)
        (endpoint: string | null)
        (p256dhKey: string | null)
        (authKey: string | null)
        (userAgent: string | null)
        : PushSubscription =
        { Id = 0L
          UserId = userId
          Endpoint = endpoint
          P256dhKey = p256dhKey
          AuthKey = authKey
          UserAgent = userAgent
          CreatedAt = DateTime.MinValue
          UpdatedAt = DateTime.MinValue }

    let find (db: CampfireDb) (id: int64) : PushSubscription =
        Data.tryFirst (db.PushSubscriptions.Where(fun p -> p.Id = EF.Parameter id))
        |> Err.orNotFound "Push::Subscription"

    let count (db: CampfireDb) : int64 = db.PushSubscriptions.LongCount()

    let forUser (db: CampfireDb) (userId: int64) : PushSubscription list =
        db.PushSubscriptions.Where(fun p -> p.UserId = EF.Parameter userId).ToList() |> List.ofSeq

    let private permittedEndpointHost (host: string) : bool =
        let host = host.ToLowerInvariant()
        host <> ""
        && permittedEndpointHosts |> List.exists (fun permitted -> host = permitted || host.EndsWith("." + permitted, StringComparison.Ordinal))

    let internal parseEndpoint (endpoint: string) : EndpointUri option =
        if endpoint = "" || endpoint |> Seq.exists Char.IsWhiteSpace then
            None
        else
            match endpoint.IndexOf "://" with
            | -1 -> None
            | at ->
                let scheme = endpoint.Substring(0, at).ToLowerInvariant()
                let rest = endpoint.Substring(at + 3)
                let authority =
                    match rest.IndexOfAny [| '/'; '?'; '#' |] with
                    | -1 -> rest
                    | i -> rest.Substring(0, i)
                let authority =
                    match authority.LastIndexOf '@' with
                    | -1 -> authority
                    | i -> authority.Substring(i + 1)
                let hostAndPort =
                    if authority.EndsWith("]", StringComparison.Ordinal) then
                        Some(authority, None)
                    else
                        match authority.LastIndexOf ':' with
                        | -1 -> Some(authority, None)
                        | i ->
                            let port = authority.Substring(i + 1)
                            if port.Length > 0 && port.Length <= 5 && port |> Seq.forall Char.IsAsciiDigit && Int32.Parse port <= 65535 then
                                Some(authority.Substring(0, i), Some(Int32.Parse port))
                            else
                                None
                match hostAndPort with
                | None -> None
                | Some(host, port) ->
                    let defaultPort =
                        match scheme with
                        | "https" -> Some 443
                        | "http" -> Some 80
                        | _ -> None
                    Some
                        { Scheme = scheme
                          Host = host
                          Port = (match port with Some p -> Some p | None -> defaultPort) }

    /// `resolved_endpoint_ip`: only for a permitted https:443 endpoint.
    let resolvedEndpointIp (resolve: string -> string option) (subscription: PushSubscription) : string option =
        match Option.ofObj subscription.Endpoint |> Option.bind parseEndpoint with
        | Some uri when uri.Scheme = "https" && uri.Port = Some 443 && permittedEndpointHost uri.Host -> resolve uri.Host
        | _ -> None

    /// Validations: endpoint present, then `validate_endpoint_url`. `resolve` is
    /// `RestrictedHTTP::PrivateNetworkGuard.resolve` (None for private or unresolvable).
    let validate (resolve: string -> string option) (subscription: PushSubscription) : Errors =
        let endpoint = defaultArg (Option.ofObj subscription.Endpoint) ""
        let errors = if String.IsNullOrWhiteSpace endpoint then Errors.empty |> Errors.add "endpoint" "can't be blank" else Errors.empty
        match parseEndpoint endpoint with
        | None -> errors |> Errors.add "endpoint" "is not a valid URL"
        | Some uri when uri.Scheme <> "https" -> errors |> Errors.add "endpoint" "must use HTTPS"
        | Some uri when uri.Port <> Some 443 -> errors |> Errors.add "endpoint" "must use the default HTTPS port"
        | Some uri when not (permittedEndpointHost uri.Host) -> errors |> Errors.add "endpoint" "is not a permitted push service"
        | Some _ when (resolvedEndpointIp resolve subscription).IsNone ->
            errors |> Errors.add "endpoint" "resolves to a private or invalid IP address"
        | Some _ -> errors

    /// `create`: validates (see `validate`) then inserts. `subscription` gets its id and times in place (`Data.insert`).
    let create (tx: Tx) (subscription: PushSubscription) (resolve: string -> string option) : PushSubscription =
        Err.validate (validate resolve subscription)
        Data.insert tx.Db subscription

    let destroy (tx: Tx) (subscription: PushSubscription) : unit =
        let id = subscription.Id
        tx.Db.PushSubscriptions.Where(fun p -> p.Id = EF.Parameter id).ExecuteDelete() |> ignore

    /// `Push::Subscription.destroy_by(endpoint:, user_id:)`
    let destroyByEndpoint (tx: Tx) (userId: int64) (endpoint: string) : unit =
        tx.Db.PushSubscriptions
            .Where(fun p -> p.Endpoint = (EF.Parameter endpoint: string | null) && p.UserId = EF.Parameter userId)
            .ExecuteDelete()
        |> ignore

    /// The badge for a notification: `user.memberships.unread.count`.
    let badge (db: CampfireDb) (subscription: PushSubscription) : int64 = Membership.unreadCount db subscription.UserId

    // Room::MessagePusher

    /// The bytes `c` takes in a JSON string: `"`, `\` and the short escapes take two, other
    /// control characters six (`\u001f`).
    let private jsonLen (c: Rune) : int =
        match c.Value with
        | 0x22
        | 0x5c
        | 0x0a
        | 0x0d
        | 0x09
        | 0x08
        | 0x0c -> 2
        | v when v < 0x20 -> 6
        | _ -> c.Utf8SequenceLength

    /// `text`, cut short with an ellipsis if it takes more than `maxBytes` as a JSON string's
    /// contents.
    let internal truncateJsonString (text: string) (maxBytes: int) : string =
        let runes = text.EnumerateRunes() |> Seq.toArray
        if (runes |> Array.sumBy jsonLen) <= maxBytes then
            text
        else
            let ellipsis = Rune '…'
            let mutable used = ellipsis.Utf8SequenceLength
            let cut =
                runes
                |> Array.tryFindIndex (fun c ->
                    used <- used + jsonLen c
                    used > maxBytes)
                |> Option.defaultValue runes.Length
            let out = StringBuilder()
            for c in Array.sub runes 0 cut do
                out.Append(c.ToString()) |> ignore
            out.Append(ellipsis.ToString()).ToString()

    /// `build_payload`: direct rooms show the sender; others the room and "Sender: body".
    /// Unlike Rails, a long title or body is cut short (with an ellipsis) so the notification
    /// still fits a push message.
    let payloadFor (db: CampfireDb) (richText: RichText) (room: Room) (message: Message) : PushPayload =
        let creator = Message.creator db message
        let body = Message.plainTextBody db richText message
        let path = $"/rooms/{room.Id}"
        let title, body =
            if Room.isDirect room then
                creator.Name, body
            else
                (match room.Name with null -> "" | name -> name), $"{creator.Name}: {body}"
        { Title = truncateJsonString title MaxPayloadTitleBytes
          Body = truncateJsonString body MaxPayloadBodyBytes
          Path = path }

    /// The subscriptions of users with a membership in `roomId` that is `involvement`, not the creator's, disconnected (never
    /// connected, or last connected before the cutoff), restricted by `users` when given. The `users` join Rails writes only
    /// requires the user to exist, which the foreign key already says.
    let private forInvolvement
        (db: CampfireDb)
        (roomId: int64)
        (creatorId: int64)
        (involvement: Involvement)
        (userIds: int64[] voption)
        (now: DateTime)
        : PushSubscription list =
        let cutoff = Membership.connectionCutoff now
        let memberships =
            db.Memberships.Where(fun m ->
                (not m.ConnectedAt.HasValue || m.ConnectedAt.Value < EF.Parameter cutoff)
                && m.RoomId = EF.Parameter roomId
                && m.UserId <> EF.Parameter creatorId
                && m.Involvement.HasValue
                && m.Involvement.Value = EF.Parameter involvement)
        let subscriptions =
            match userIds with
            | ValueSome ids -> db.PushSubscriptions.Where(fun p -> Enumerable.Contains(EF.Parameter ids, p.UserId))
            | ValueNone -> db.PushSubscriptions
        subscriptions
            .Join(memberships, (fun p -> p.UserId), (fun m -> m.UserId), (fun p _ -> p))
            .ToList()
        |> List.ofSeq

    /// `push_subscriptions_for_users_involved_in_everything`
    let forUsersInvolvedInEverything (db: CampfireDb) (roomId: int64) (creatorId: int64) (now: DateTime) : PushSubscription list =
        forInvolvement db roomId creatorId Involvement.Everything ValueNone now

    /// `push_subscriptions_for_mentionable_users(message.mentionees)`
    let forMentionedUsers (db: CampfireDb) (roomId: int64) (creatorId: int64) (mentioneeIds: int64 list) (now: DateTime) : PushSubscription list =
        if List.isEmpty mentioneeIds then
            []
        else
            forInvolvement db roomId creatorId Involvement.Mentions (ValueSome(Array.ofList mentioneeIds)) now

    /// `Room::MessagePusher#push`: the payload, and the subscriptions it goes to (everything
    /// first, then mentions).
    let pushesFor (db: CampfireDb) (richText: RichText) (message: Message) (now: DateTime) : PushPayload * PushSubscription list * PushSubscription list =
        let room = Room.find db message.RoomId
        let payload = payloadFor db richText room message
        let everything = forUsersInvolvedInEverything db room.Id message.CreatorId now
        let mentioneeIds = Message.mentionees db richText message |> List.map (fun u -> u.Id)
        let mentions = forMentionedUsers db room.Id message.CreatorId mentioneeIds now
        payload, everything, mentions
