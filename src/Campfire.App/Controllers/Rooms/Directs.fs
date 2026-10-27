// Port of rust/crates/campfire/src/controllers/rooms/directs.rs
//
// `Rooms::DirectsController` (reference/app/controllers/rooms/directs_controller.rb). `index`
// and `destroy` are RoomsController's (`destroy` with this controller's `set_room` and an
// `ensure_can_administer` that always passes); `show` is RoomsController's without `set_room`,
// so `remember_last_room_visited` raises on the nil `@room` (`show`).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Rails.Ruby
open Campfire.Views
open Campfire.Views.Rooms
open Campfire.App.Web

module Directs =
    module NewPage = Campfire.Views.Templates.Rooms.Directs.New
    module EditPage = Campfire.Views.Templates.Rooms.Directs.Edit


    /// `show`: the room page, which checks membership. Rails inherits RoomsController#show without
    /// setting `@room`, so `remember_last_room_visited` raises (a 500).
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                match (match Params.value http "id" with null -> None | id -> Ruby.integerCast id) with
                | None -> return! Result.Error NotFound
                | Some id -> return! RoomsController.redirectToRoom http id
            }

    let ``new``: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                return! Pages.page http 200 NewPage.rendered
            }

    /// `broadcast_create_room`: `users/sidebars/rooms/_direct` for each membership, to its user.
    let private broadcastCreateRoom (http: HttpContext) (room: Room) : System.Threading.Tasks.Task<Result<unit, Error>> =
        taskResult {
            let app = Data.app http
            let! memberships = Data.read http (fun db -> Membership.forRoom db room.Id)
            let! directRooms =
                Data.readAsync http (fun db ->
                    task {
                        let presenter = Presenter(db, app, None)
                        let rooms = ResizeArray<int64 * CachedFragment>()
                        for membership in memberships do
                            let direct = presenter.SidebarDirect membership
                            let! html = UsersCached.directRoom app.Fragments (Page.fragmentContext app) direct
                            rooms.Add((membership.Id, html))
                        return List.ofSeq rooms
                    })
            return!
                Data.read http (fun db ->
                    app.Broadcasts.DirectRoomCreate(db, room, Rendered.partials { Rendered.empty with DirectRooms = directRooms }))
        }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (user: User) = Current.requireUser http
                // selected_users: `User.where(id: selected_users_ids.including(Current.user.id))`
                let ids = RoomsController.userIdsParam http @ [ user.Id ]
                let! (room: Room) =
                    Data.write http (fun tx ->
                        let users = RoomsController.existingUserIds tx.Db ids
                        Room.findOrCreateDirectFor tx users user.Id)
                do! broadcastCreateRoom http room
                return! RoomsController.redirectToRoom http room.Id
            }

    let edit: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = RoomsController.setRoom http Directs
                let! (current: User) = Current.requireUser http
                let app = Data.app http
                let! edit =
                    Data.read http (fun db ->
                        let presenter = Presenter(db, app, None)
                        // `@room.users.many? ? @room.users.without(Current.user) : @room.users`
                        let users = Room.users db room
                        let users = if users.Length > 1 then users |> List.filter (fun user -> user.Id <> current.Id) else users
                        ({ RoomId = room.Id
                           DisplayName = presenter.RoomDisplayName(room, Some current)
                           Users = users |> List.map (PresenterSupport.userView app.Secrets) }: DirectEditView))
                return!
                    Pages.page http 200 (fun ctx -> EditPage.rendered ctx edit)
            }

    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = RoomsController.setRoom http Directs
                // ensure_can_administer: every member of a direct room can.
                return! RoomsController.destroyRoom http room
            }
