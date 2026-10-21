// Port of rust/crates/campfire/src/channels/typing_notifications.rs
//
// `TypingNotificationsChannel` (reference/app/channels/typing_notifications_channel.rb).
module Campfire.App.Channels.TypingNotifications

open System.Threading.Tasks
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat

let channel (access: RoomChannel.Access) : Channel<CableUser> =
    let room: Room option ref = ref None

    /// `broadcast_to @room, action:, user: current_user.slice(:id, :name)`.
    let broadcast (action: string) (sub: Subscription<CableUser>) : unit =
        match room.Value with
        // `@room` is only nil when `subscribed` failed, and Rails raises NoMethodError.
        | None -> failwith "undefined method 'to_gid_param' for nil"
        | Some room ->
            let user = sub.CurrentUser
            let payload = Payload.ofValue {| action = action; user = {| id = user.Id; name = user.Name |} |}
            sub.BroadcastTo([ GlobalId.toParam (Gid.roomGid room) ], payload)

    let resubscribe sub =
        room.Value <- RoomChannel.subscribe access sub

    { Channel.empty with
        Subscribed =
            fun sub ->
                resubscribe sub
                Task.CompletedTask
        Perform =
            fun action _ sub ->
                if sub.Rejected then
                    Task.FromResult false
                else
                    match action with
                    | "start"
                    | "stop" ->
                        broadcast action sub
                        Task.FromResult true
                    | "subscribed" ->
                        resubscribe sub
                        Task.FromResult true
                    | _ -> Task.FromResult false }
