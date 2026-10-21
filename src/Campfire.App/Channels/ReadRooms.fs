// Port of rust/crates/campfire/src/channels/read_rooms.rs
//
// `ReadRoomsChannel` (reference/app/channels/read_rooms_channel.rb).
module Campfire.App.Channels.ReadRooms

open System.Threading.Tasks
open Campfire.Cable

/// The user's own stream of rooms read in another window.
let streamNameFor (userId: int64) : string = $"user_{userId}_reads"

let private subscribed (sub: Subscription<CableUser>) : Task =
    sub.StreamFrom(streamNameFor sub.CurrentUser.Id)
    Task.CompletedTask

let channel: Channel<CableUser> =
    { Channel.empty with
        Subscribed = subscribed
        // `subscribed` is public, so it's an action too.
        Perform =
            fun action _ sub ->
                match action with
                | "subscribed" ->
                    task {
                        do! subscribed sub
                        return true
                    }
                | _ -> Task.FromResult false }
