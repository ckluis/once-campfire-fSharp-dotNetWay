// Port of rust/crates/campfire/src/controllers/rooms/opens.rs
//
// `Rooms::OpensController` (reference/app/controllers/rooms/opens_controller.rb). `index` is
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

module Opens =
    module NewPage = Campfire.Views.Templates.Rooms.Opens.New
    module EditPage = Campfire.Views.Templates.Rooms.Opens.Edit


    /// `DEFAULT_ROOM_NAME`
    [<Literal>]
    let DefaultRoomName = "New room"

    /// `User.active.ordered`
    let activeUsers (http: HttpContext) : Result<Campfire.Views.Messages.UserView list, Error> =
        let secrets = (Data.app http).Secrets
        Data.read http (fun db -> User.activeOrdered db |> List.map (PresenterSupport.userView secrets))

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
                let! users = activeUsers http
                let form: OpenFormView =
                    { Room = { Id = None; Name = Some DefaultRoomName }
                      CanAdminister = true
                      Users = users }
                return!
                    Pages.page http 200 (fun ctx -> NewPage.rendered ctx form)
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! RoomsController.ensurePermissionToCreateRooms http
                let! name = RoomsController.roomNameParam http
                let name = Option.flatten name
                let! (user: User) = Current.requireUser http
                // Rooms::Open.create_for(room_params, users: Current.user)
                let! (room: Room) = Data.write http (fun tx -> Room.createFor tx RoomType.Open name user.Id [ user.Id ])
                let! partials = RoomsController.renderSharedRoom http room
                (Data.app http).Broadcasts.OpenRoomCreate(room, Rendered.partials partials)
                return! RoomsController.redirectToRoom http room.Id
            }

    let edit: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = RoomsController.setRoom http WithoutDirects
                // force_room_type
                let room = { room with Type = RoomType.Open }
                let! (user: User) = Current.requireUser http
                let! users = activeUsers http
                let form: OpenFormView =
                    { Room = { Id = Some room.Id; Name = Option.ofObj room.Name }
                      CanAdminister = User.canAdminister user (Some room.CreatorId) false
                      Users = users }
                return!
                    Pages.page http 200 (fun ctx -> EditPage.rendered ctx form)
            }

    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = RoomsController.setRoom http WithoutDirects
                do! RoomsController.ensureCanAdminister http room
                let! name = RoomsController.roomNameParam http
                // force_room_type, then `@room.update! room_params` saves the name and the new type.
                let! (room: Room) = Data.write http (fun tx -> Room.update tx room name (Some RoomType.Open))
                let! partials = RoomsController.renderSharedRoom http room
                (Data.app http).Broadcasts.OpenRoomUpdate(room, Rendered.partials partials)
                return! RoomsController.redirectToRoom http room.Id
            }
