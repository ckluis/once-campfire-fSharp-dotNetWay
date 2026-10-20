// Port of the `Current` part of rust/crates/campfire/src/concerns.rs
//
// `authenticated_by` and the room `last_room_visited` finds; `Web/Current.fs` holds the rest of `Current`.
namespace Campfire.App

open Campfire.Db

/// `authenticated_by` (`"".inquiry`, `"session"` or `"bot_key"`).
type AuthenticatedBy =
    | Anonymous
    | BySession
    | BotKey

module Current =
    /// `last_room_visited` inside a read that's already on the request's context, for the user and the
    /// `lastRoomCookie`: the cookie's room if the user is in it, else `Current.user.rooms.original`.
    let lastRoomVisitedIn (db: Campfire.Db.Ef.CampfireDb) (userId: int64) (lastRoom: int64 option) : Room option =
        let found =
            match lastRoom with
            | Some roomId -> Room.findForUser db userId roomId
            | None -> None
        match found with
        | Some room -> Some room
        | None -> Room.originalForUser db userId
