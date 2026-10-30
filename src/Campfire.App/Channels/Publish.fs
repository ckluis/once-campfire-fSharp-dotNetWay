// Phase 3 (plans/design-cable.md C16): the one way the app publishes to the cable. Broadcasts, remote disconnects and Presence's
// `readRoom` go through here, and it refuses to run inside `Writes.run`'s transaction (`WriteGate.InWrite`, true from BEGIN to
// COMMIT): a client could otherwise be told about a row that a rollback then removes. The after-commit queue, where the event
// sink hands `DisconnectUser` to `Revocation`, runs with it false.
module Campfire.App.Channels.Publish

open System
open Campfire.Cable
open Campfire.Db

let private beforeCommit (what: string) : exn =
    InvalidOperationException $"{what} inside a write: publish to the cable after the transaction has committed"

/// `ActionCable.server.broadcast(broadcasting, message)`, after the write.
let broadcast (server: ICableBroadcaster) (broadcasting: string) (payload: Payload) : int =
    if WriteGate.InWrite then raise (beforeCommit $"broadcast to {broadcasting}")
    server.Broadcast(broadcasting, payload)

/// `remote_connections.where(current_user: user).disconnect`, after the write. Returns the connections reached.
let disconnectUser (cable: Cable) (userId: int64) (reconnect: bool) : int =
    if WriteGate.InWrite then raise (beforeCommit $"disconnect of user {userId}")
    cable.Disconnect(string (Gid.userGid userId), reconnect)

/// The cable as `Broadcasts` and Turbo's helpers see it: every broadcast through `broadcast`.
let guarded (server: ICableBroadcaster) : ICableBroadcaster =
    { new ICableBroadcaster with
        member _.Broadcast(broadcasting, payload) = broadcast server broadcasting payload }
