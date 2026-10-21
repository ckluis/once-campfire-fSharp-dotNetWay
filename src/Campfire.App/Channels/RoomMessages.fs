// Port of rust/crates/campfire/src/channels/room_messages.rs
//
// `RoomMessagesChannel` (reference/app/channels/room_messages_channel.rb) and the `RoomStreamsAreAuthorized`
// guard prepended onto `Turbo::StreamsChannel`
// (reference/app/channels/concerns/room_streams_are_authorized.rb).
//
// A room's message stream (`<room gid param>:messages`) is only served here, and only to a current member of the
// room the verified stream name points at.
module Campfire.App.Channels.RoomMessages

open System
open System.Threading.Tasks
open Campfire.Cable
open Campfire.Cable.Turbo
open Campfire.Db
open Campfire.RailsCompat

[<Literal>]
let StreamSuffix = "messages"

/// `RoomMessagesChannel.guarded_stream?`: true for the stream names this channel guards, whoever is asking. Also
/// the guard on `Turbo::StreamsChannel`.
let guardedStream (streamName: string) : bool =
    match streamName.IndexOf ':' with
    | -1 -> false
    | colon -> streamName.Substring(colon + 1) = StreamSuffix

/// Constants a GID could name that aren't rooms (`only: Room` turns them away without raising).
let private knownModels =
    set [ "Account"; "Ban"; "Boost"; "Current"; "Membership"; "Message"; "Push::Subscription"; "Search"; "Session"; "User"; "Webhook" ]

/// `GlobalID::Locator.locate gid_param, only: Room`, with `RecordNotFound` as nil.
let private roomFrom (db: Campfire.Db.Ef.CampfireDb) (gidParam: string) : Room option =
    // `GlobalID.parse` takes the URI form or its Base64 param.
    match GlobalId.parse gidParam |> Option.orElse (GlobalId.fromParam gidParam) with
    | None -> None
    | Some gid ->
        // `find_allowed?(gid.model_class, only: Room)`: Room or one of its STI subclasses, whose `find` also
        // requires the type to match.
        let required: Result<RoomType option, string option> =
            match gid.ModelName with
            | "Room" -> Ok None
            | name ->
                match RoomType.fromClassName name with
                | Some roomType -> Ok(Some roomType)
                | None when knownModels.Contains name -> Error None
                // `model_name.constantize` raises NameError.
                | None -> Error(Some $"uninitialized constant {name}")
        match required with
        | Error None -> None
        | Error(Some error) -> failwith error
        | Ok requiredType ->
            match Int64.TryParse(gid.Id, Globalization.NumberStyles.AllowLeadingSign, Globalization.CultureInfo.InvariantCulture) with
            | false, _ -> None
            | true, id ->
                Room.findById db id
                |> Option.filter (fun room -> requiredType |> Option.forall (fun t -> t = room.Type))

/// `RoomMessagesChannel.subscribable_room(user, stream_name)`.
let subscribableRoom (db: Campfire.Db.Ef.CampfireDb) (userId: int64) (streamName: string) : Room option =
    match streamName.IndexOf ':' with
    | -1 -> None
    | colon when streamName.Substring(colon + 1) <> StreamSuffix -> None
    | colon ->
        match roomFrom db (streamName.Substring(0, colon)) with
        | Some room -> Room.findForUser db userId room.Id
        | None -> None

let channel (streams: StreamsChannel) : Channel<CableUser> =
    /// `authorized_stream_name`: the verified stream name, if present and for a room the user belongs to.
    let authorizedStreamName (sub: Subscription<CableUser>) : string option =
        match StreamsChannel.verifiedStreamNameFromParams streams sub.Params with
        | None -> None
        | Some streamName when streamName.Trim() = "" -> None
        | Some streamName ->
            let userId = sub.CurrentUser.Id
            match RoomChannel.Access.read sub (fun db -> subscribableRoom db userId streamName) with
            | Error error -> raise (RoomChannel.dbError error)
            | Ok room -> room |> Option.map (fun _ -> streamName)

    let subscribed (sub: Subscription<CableUser>) : Task =
        match authorizedStreamName sub with
        | Some streamName -> sub.StreamFrom streamName
        | None -> sub.Reject()
        Task.CompletedTask

    { Channel.empty with
        Subscribed = subscribed
        // Its public methods: `subscribed`, and `verified_stream_name_from_params` from
        // `include Turbo::Streams::StreamName::ClassMethods` (which returns without transmitting).
        Perform =
            fun action _ sub ->
                if sub.Rejected then
                    Task.FromResult false
                else
                    match action with
                    | "subscribed" ->
                        subscribed sub |> ignore
                        Task.FromResult true
                    | "verified_stream_name_from_params" ->
                        StreamsChannel.verifiedStreamNameFromParams streams sub.Params |> ignore
                        Task.FromResult true
                    | _ -> Task.FromResult false }
