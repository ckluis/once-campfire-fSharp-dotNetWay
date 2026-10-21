// Port of rust/crates/campfire/src/controllers/rooms/closeds.rs
//
// `Rooms::ClosedsController` (reference/app/controllers/rooms/closeds_controller.rb). `index` is
// RoomsController's; `destroy` is RoomsController's without `set_room`
// (`RoomsController.destroyWithoutRoom`).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Views
open Campfire.Views.Rooms
open Campfire.App.Web

module Closeds =
    module NewPage = Campfire.Views.Templates.Rooms.Closeds.New
    module EditPage = Campfire.Views.Templates.Rooms.Closeds.Edit


    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = RoomsController.setRoom http WithoutDirects
                Pages.rememberLastRoomVisited http room.Id
                return! RoomsController.redirectToRoom http room.Id
            }

    let ``new``: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! RoomsController.ensurePermissionToCreateRooms http
                let! (user: User) = Current.requireUser http
                // `@users = User.active.ordered`; the form shows them all as unselected.
                let! users = Opens.activeUsers http
                let form: ClosedFormView =
                    { Room = { Id = None; Name = Some Opens.DefaultRoomName }
                      CanAdminister = true
                      CurrentUserId = user.Id
                      SelectedUsers = []
                      UnselectedUsers = users }
                return!
                    Pages.viewPage http 200 (fun ctx -> NewPage.rendered ctx form)
            }

    /// `broadcast_create_room` / `broadcast_update_room`: the shared-room partial, rendered once, to
    /// every member's own rooms stream.
    let private broadcastToMembers (http: HttpContext) (room: Room) (update: bool) : Result<unit, Error> =
        result {
            let! partials = RoomsController.renderSharedRoom http room
            let broadcasts = (Data.app http).Broadcasts
            return!
                Data.read http (fun db ->
                    if update then
                        broadcasts.ClosedRoomUpdate(db, room, Rendered.partials partials)
                    else
                        broadcasts.ClosedRoomCreate(db, room, Rendered.partials partials))
        }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! RoomsController.ensurePermissionToCreateRooms http
                let! name = RoomsController.roomNameParam http
                let name = Option.flatten name
                let! (user: User) = Current.requireUser http
                let granteeIds = RoomsController.userIdsParam http
                // Rooms::Closed.create_for(room_params, users: grantees)
                let! (room: Room) =
                    Data.write http (fun tx ->
                        let grantees = RoomsController.existingUserIds tx.Db granteeIds
                        Room.createFor tx RoomType.Closed name user.Id grantees)
                do! broadcastToMembers http room false
                return! RoomsController.redirectToRoom http room.Id
            }

    let edit: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = RoomsController.setRoom http WithoutDirects
                // force_room_type
                let room = { room with Type = RoomType.Closed }
                let! (current: User) = Current.requireUser http
                let secrets = (Data.app http).Secrets
                let roomId = room.Id
                let! selected, unselected =
                    Data.read http (fun db ->
                        let selectedIds = Room.userIds db (Room.find db roomId)
                        let selected, unselected =
                            User.activeOrdered db |> List.partition (fun user -> List.contains user.Id selectedIds)
                        let views = List.map (PresenterSupport.userView secrets)
                        views selected, views unselected)
                let form: ClosedFormView =
                    { Room = { Id = Some room.Id; Name = Option.ofObj room.Name }
                      CanAdminister = User.canAdminister current (Some room.CreatorId) false
                      CurrentUserId = current.Id
                      SelectedUsers = selected
                      UnselectedUsers = unselected }
                return!
                    Pages.viewPage http 200 (fun ctx -> EditPage.rendered ctx form)
            }

    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = RoomsController.setRoom http WithoutDirects
                do! RoomsController.ensureCanAdminister http room
                let! name = RoomsController.roomNameParam http
                let granteeIds = RoomsController.userIdsParam http
                // force_room_type, then `@room.update! room_params`
                let! (room: Room) = Data.write http (fun tx -> Room.update tx room name (Some RoomType.Closed))
                // `@room.memberships.revise(granted: grantees, revoked: revokees)`
                do!
                    Data.write http (fun tx ->
                        let granted = RoomsController.existingUserIds tx.Db granteeIds
                        let revoked = Room.userIds tx.Db room |> List.filter (fun id -> not (List.contains id granteeIds))
                        Room.revise tx room granted revoked)
                do! broadcastToMembers http room true
                return! RoomsController.redirectToRoom http room.Id
            }
