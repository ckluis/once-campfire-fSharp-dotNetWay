// Port of rust/crates/campfire/src/channels/room.rs
//
// `RoomChannel` (reference/app/channels/room_channel.rb), and the room lookup that `PresenceChannel` and
// `TypingNotificationsChannel` inherit from it.
module Campfire.App.Channels.RoomChannel

open System
open System.Threading.Tasks
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Campfire.Ruby

/// What the room channels read and write with: the pooled context factory, the write gate and the write environment
/// (the cable's `Deps`).
type Access =
    { Factory: Microsoft.EntityFrameworkCore.IDbContextFactory<Campfire.Db.Ef.CampfireDb>
      Gate: WriteGate
      Env: Campfire.Db.Ef.Env }

module Access =
    /// `Reads.run` on a leased context, as the port's `Database.Read` boundary.
    let read (access: Access) (f: Campfire.Db.Ef.CampfireDb -> 'T) : Result<'T, DbError> =
        use db = access.Factory.CreateDbContext()
        Campfire.Db.Ef.Reads.run db f

    /// `Writes.run` on a leased context: the gate, the transaction, the after-commit queue.
    let write (access: Access) (f: Campfire.Db.Ef.Tx -> 'T) : Task<Result<'T, DbError>> =
        task {
            use db = access.Factory.CreateDbContext()
            return! Campfire.Db.Ef.Writes.run access.Gate access.Env db f
        }

/// A `DbError` as the exception a callback would have raised.
let dbError (error: DbError) : ChannelError = Channel.error (DbError.display error)

/// How Active Record casts a value for an integer `id` condition: numbers are truncated, booleans are 1 and 0,
/// and strings go through `to_i` unless they don't start like a number (`non_numeric_string?`), which matches
/// nothing. So does anything out of range.
let castId (value: Value) : int64 option =
    let fromFloat (f: float) = if Double.IsFinite f && abs f < 9.2e18 then Some(int64 (Math.Truncate f)) else None
    match value with
    | Value.Int n -> Some n
    | Value.UInt n -> if n <= uint64 Int64.MaxValue then Some(int64 n) else fromFloat (float n)
    | Value.Float f -> fromFloat f
    | Value.Bool b -> Some(if b then 1L else 0L)
    | Value.String s -> Ruby.integerCast s
    | _ -> None

/// `current_user.rooms.find_by(id: params[:room_id])`.
let private findRoom (access: Access) (sub: Subscription<CableUser>) : Task<ChannelResult<Room option>> =
    task {
        match sub.Param "room_id" |> Option.bind castId with
        | None -> return Ok None
        | Some roomId ->
            let userId = sub.CurrentUser.Id
            match Access.read access (fun db -> Room.findForUser db userId roomId) with
            | Ok room -> return Ok room
            | Error error -> return Error(dbError error)
    }

/// `RoomChannel#subscribed`: `stream_for @room` if the user is a member, else `reject`.
let subscribe (access: Access) (sub: Subscription<CableUser>) : Task<ChannelResult<Room option>> =
    task {
        match! findRoom access sub with
        | Error error -> return Error error
        | Ok room ->
            match room with
            | Some room -> sub.StreamFor [ GlobalId.toParam (Gid.roomGid room) ]
            | None -> sub.Reject()
            return Ok room
    }

let channel (access: Access) : Channel<CableUser> =
    let room: Room option ref = ref None
    { Channel.empty with
        Subscribed =
            fun sub ->
                task {
                    match! subscribe access sub with
                    | Ok found ->
                        room.Value <- found
                        return Ok()
                    | Error error -> return Error error
                }
        Perform =
            fun action _ sub ->
                task {
                    match action with
                    | "subscribed" when not sub.Rejected ->
                        match! subscribe access sub with
                        | Ok found ->
                            room.Value <- found
                            return Ok true
                        | Error error -> return Error error
                    | _ -> return Ok false
                } }
