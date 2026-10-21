// Port of rust/crates/campfire/src/channels.rs (the types, and the GlobalIDs the channels and broadcasts name things by)
//
// Action Cable: `reference/app/channels`, the broadcasts the app makes (`Broadcasts`), and revocation
// (`Revocation`). `CableServices` builds the cable server with these channels, and the app routes the
// models' events to `Broadcasts` and `Revocation`.
//
// Every channel matches its Ruby class: identifier, streams, payloads, and callback order
// (`on_subscribe` runs after `subscribed`, before the confirmation). Ruby makes every public
// method an action, including a publicly redefined `subscribed`, so those are performable here
// too.
namespace Campfire.App.Channels

open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Campfire.RailsCompat.Clock

/// `identified_by :current_user`: the user as loaded when the connection opened.
type CableUser = { Id: int64; Name: string }

/// The cable server, identified by `current_user`.
type Cable = CableServer<CableUser>

/// The cable services of the running host, for what the app builds before the host exists (`AppState`, `Jobs`). The server
/// is a DI singleton (it needs the host's scope factory and lifetime); `CableServices.add` attaches the host's provider
/// when the host starts, and `Resolve` reads from it.
[<Sealed>]
type CableHandle() =
    let mutable services: System.IServiceProvider | null = null

    member _.Attach(provider: System.IServiceProvider) : unit = System.Threading.Volatile.Write(&services, provider)

    /// A cable service, once the host has started; `ValueNone` before.
    member _.TryResolve<'T when 'T: not null>() : 'T voption =
        match System.Threading.Volatile.Read(&services) with
        | null -> ValueNone
        | provider -> ValueSome(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<'T> provider)

    member this.Resolve<'T when 'T: not null>() : 'T =
        match this.TryResolve<'T>() with
        | ValueSome service -> service
        | ValueNone -> failwith "the cable is not available before the host has started"

module Gid =
    let userGid (userId: int64) : GlobalId = GlobalId.create "User" (string userId)

    /// A room's GlobalID names its STI class (`gid://campfire/Rooms::Open/1`).
    let roomGid (room: Room) : GlobalId = GlobalId.create (RoomType.className room.Type) (string room.Id)

    /// `connection_gid`: the user's GlobalID, which `remote_connections.where(current_user:)` matches.
    let connectionIdentifier (user: CableUser) : string = string (userGid user.Id)
