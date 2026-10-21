// Port of rust/crates/campfire/src/channels/presence.rs
//
// `PresenceChannel` (reference/app/channels/presence_channel.rb): a `RoomChannel` that marks the membership
// connected while subscribed (`Membership::Connectable`) and tells the user's other windows the room has been
// read.
module Campfire.App.Channels.Presence

open System.Threading.Tasks
open Campfire.Cable
open Campfire.Db

let private nilMembership () : exn = System.Exception "undefined method for nil (membership)"

let channel () : Channel<CableUser> =
    let room: Room option ref = ref None

    // `@room` is only nil after a rejection, when these callbacks don't run.
    let ids (sub: Subscription<CableUser>) : int64 * int64 =
        match room.Value with
        | Some room -> room.Id, sub.CurrentUser.Id
        | None -> failwith "undefined method 'memberships' for nil"

    /// `@room.memberships.find_by(user: current_user)`, which is nil (and so raises NoMethodError) once the
    /// membership is gone.
    let membership (sub: Subscription<CableUser>) : Membership =
        let roomId, userId = ids sub
        match RoomChannel.Access.read sub (fun db -> Membership.findByRoomAndUser db roomId userId) with
        | Error error -> raise (RoomChannel.dbError error)
        | Ok None -> raise (nilMembership ())
        | Ok(Some membership) -> membership

    let withMembership (sub: Subscription<CableUser>) (change: Ef.Tx -> Membership -> unit) : Task =
        task {
            let roomId, userId = ids sub
            let! found =
                RoomChannel.Access.write sub (fun tx ->
                    match Membership.findByRoomAndUser tx.Db roomId userId with
                    | Some membership ->
                        change tx membership
                        true
                    | None -> false)
            match found with
            | Error error -> raise (RoomChannel.dbError error)
            | Ok true -> ()
            | Ok false -> raise (nilMembership ())
        }

    /// `present`: `membership.present`, then `broadcast_read_room`.
    let present (sub: Subscription<CableUser>) : Task =
        task {
            do! withMembership sub (fun tx membership -> Membership.present tx membership)
            // `membership.room_id` finds the membership again.
            let membership = membership sub
            BroadcastNames.readRoom sub.Broadcaster sub.CurrentUser.Id membership.RoomId |> ignore
        }

    /// `absent`: `membership.disconnected`.
    let absent (sub: Subscription<CableUser>) : Task =
        withMembership sub (fun tx membership -> Membership.disconnected tx membership |> ignore)

    /// `refresh`: `membership.refresh_connection`.
    let refresh (sub: Subscription<CableUser>) : Task =
        withMembership sub (fun tx membership -> Membership.refreshConnection tx membership |> ignore)

    let resubscribe (sub: Subscription<CableUser>) : unit =
        room.Value <- RoomChannel.subscribe sub

    let ran (work: Task) : Task<bool> =
        task {
            do! work
            return true
        }

    { Channel.empty with
        // `subscribed`, then `on_subscribe :present, unless: :subscription_rejected?`.
        Subscribed =
            fun sub ->
                resubscribe sub
                if sub.Rejected then Task.CompletedTask else present sub
        // `on_unsubscribe :absent, unless: :subscription_rejected?`.
        Unsubscribed = fun sub -> if sub.Rejected then Task.CompletedTask else absent sub
        Perform =
            fun action _ sub ->
                if sub.Rejected then
                    Task.FromResult false
                else
                    match action with
                    | "present" -> ran (present sub)
                    | "absent" -> ran (absent sub)
                    | "refresh" -> ran (refresh sub)
                    | "subscribed" ->
                        resubscribe sub
                        Task.FromResult true
                    | _ -> Task.FromResult false }
