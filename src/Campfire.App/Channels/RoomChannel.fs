// Port of rust/crates/campfire/src/channels/room.rs
//
// `RoomChannel` (reference/app/channels/room_channel.rb), and the room lookup that `PresenceChannel` and
// `TypingNotificationsChannel` inherit from it.
module Campfire.App.Channels.RoomChannel

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Campfire.Ruby

/// What the room channels read and write with: the `CampfireDb` of the command's scope (C12: one per command, returned to the
/// pool when the command ends), the write gate and the write environment.
module Access =
    /// `Reads.run` on the command's context.
    let read (sub: Subscription<CableUser>) (f: Campfire.Db.Ef.CampfireDb -> 'T) : Result<'T, DbError> =
        Campfire.Db.Ef.Reads.run (sub.Services.GetRequiredService<Campfire.Db.Ef.CampfireDb>()) f

    /// `Writes.run` on the command's context: the gate, the transaction, the after-commit queue.
    let write (sub: Subscription<CableUser>) (f: Campfire.Db.Ef.Tx -> 'T) : Task<Result<'T, DbError>> =
        let services = sub.Services
        Campfire.Db.Ef.Writes.run
            (services.GetRequiredService<WriteGate>())
            (services.GetRequiredService<Campfire.Db.Ef.Env>())
            (services.GetRequiredService<Campfire.Db.Ef.CampfireDb>())
            f

/// A `DbError` as the exception a callback would have raised.
let dbError (error: DbError) : exn = Exception(DbError.display error)

/// How Active Record casts a value for an integer `id` condition: numbers are truncated, booleans are 1 and 0,
/// and strings go through `to_i` unless they don't start like a number (`non_numeric_string?`), which matches
/// nothing. So does anything out of range.
let castId (value: JsonElement) : int64 option =
    let fromFloat (f: float) = if Double.IsFinite f && abs f < 9.2e18 then Some(int64 (Math.Truncate f)) else None
    match value.ValueKind with
    | JsonValueKind.Number ->
        match value.TryGetInt64() with
        | true, n -> Some n
        | _ -> fromFloat (value.GetDouble())
    | JsonValueKind.True -> Some 1L
    | JsonValueKind.False -> Some 0L
    | JsonValueKind.String -> Ruby.integerCast (value.GetString() |> nonNull)
    | _ -> None

/// `current_user.rooms.find_by(id: params[:room_id])`.
let private findRoom (sub: Subscription<CableUser>) : Room option =
    match sub.Param "room_id" |> ValueOption.toOption |> Option.bind castId with
    | None -> None
    | Some roomId ->
        let userId = sub.CurrentUser.Id
        match Access.read sub (fun db -> Room.findForUser db userId roomId) with
        | Ok room -> room
        | Error error -> raise (dbError error)

/// `RoomChannel#subscribed`: `stream_for @room` if the user is a member, else `reject`.
let subscribe (sub: Subscription<CableUser>) : Room option =
    let room = findRoom sub
    match room with
    | Some room -> sub.StreamFor [ GlobalId.toParam (Gid.roomGid room) ]
    | None -> sub.Reject()
    room

let channel () : Channel<CableUser> =
    let room: Room option ref = ref None
    { Channel.empty with
        Subscribed =
            fun sub ->
                room.Value <- subscribe sub
                Task.CompletedTask
        Perform =
            fun action _ sub ->
                match action with
                | "subscribed" when not sub.Rejected ->
                    room.Value <- subscribe sub
                    Task.FromResult true
                | _ -> Task.FromResult false }
