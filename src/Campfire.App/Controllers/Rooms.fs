// Port of rust/crates/campfire/src/controllers/rooms.rs
//
// `RoomsController` (reference/app/controllers/rooms_controller.rb), plus what its subclasses
// share: `set_room` over a `room_scope`, `ensure_can_administer`, `ensure_permission_to_create_rooms`.
//
// The subclasses (`Rooms::OpensController` and friends) re-declare `before_action :set_room`
// (and `ensure_can_administer`) with their own `only:`, which *replaces* the parent's callback.
// So the actions they inherit from here but don't list (`destroy` for opens/closeds, `show` for
// directs) run without `set_room` and raise on the nil `@room`, as in the reference.
namespace Campfire.App.Controllers

open System
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Channels
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby
open Campfire.Views
open Campfire.App.Web

/// `room_scope`: which of `Current.user.rooms` a controller may act on.
type RoomScope =
    /// `Current.user.rooms` (RoomsController)
    | All
    /// `Current.user.rooms.without_directs` (opens, closeds)
    | WithoutDirects
    /// `Current.user.rooms.directs` (directs)
    | Directs

module RoomScope =
    let includes (scope: RoomScope) (room: Room) : bool =
        match scope with
        | All -> true
        | WithoutDirects -> room.Type <> RoomType.Direct
        | Directs -> room.Type = RoomType.Direct

module RoomsController =
    module ShowPage = Campfire.Views.Templates.Rooms.ShowPage
    module SharedPartial = Campfire.Views.Templates.Users.Sidebars.Rooms._Shared


    // --- Shared before-actions ---------------------------------------------------------------------

    /// `set_room`: `room_scope.find_by(id: params[:room_id] || params[:id])`, or back to the root with
    /// an alert.
    let setRoom (http: HttpContext) (scope: RoomScope) : Result<Room, Error> =
        result {
            let! (user: User) = Current.requireUser http
            let id =
                match Params.value http "room_id" with
                | null -> Params.value http "id"
                | id -> id
            let! (room: Room option) =
                match (match id with null -> None | id -> Ruby.integerCast id) with
                | Some id -> Data.read http (fun db -> Room.findForUser db user.Id id)
                | None -> Ok None
            match room |> Option.filter (RoomScope.includes scope) with
            | Some room -> return room
            | None ->
                let! redirect =
                    Respond.redirectToWith
                        http
                        (Respond.urlFor http (Routes.root ()))
                        { RedirectOptions.Default with Alert = "Room not found or inaccessible" }
                return! Action.halt redirect
        }

    /// `ensure_can_administer`: `head :forbidden unless Current.user.can_administer?(@room)`.
    let ensureCanAdminister (http: HttpContext) (room: Room) : Result<unit, Error> =
        match Current.requireUser http with
        | Result.Error e -> Result.Error e
        | Ok user ->
            if User.canAdminister user (Some room.CreatorId) false then
                Ok()
            else
                Action.halt (Before.head 403)

    /// `ensure_permission_to_create_rooms`
    let ensurePermissionToCreateRooms (http: HttpContext) : Result<unit, Error> =
        result {
            let! (user: User) = Current.requireUser http
            let! account = Data.read http Account.first
            let restricted =
                account
                |> Option.exists (fun account -> AccountSettings.restrictRoomCreationToAdministrators (Account.settings account))
            if restricted && not (User.isAdministrator user) then
                return! Action.halt (Before.head 403)
        }

    // --- Helpers -----------------------------------------------------------------------------------

    let redirectToRoot (http: HttpContext) : Result<Falco.HttpHandler, Error> = Respond.redirectTo http (Respond.urlFor http (Routes.root ()))

    let redirectToRoom (http: HttpContext) (roomId: int64) : Result<Falco.HttpHandler, Error> =
        Respond.redirectTo http (Respond.urlFor http (Routes.room roomId))

    /// `params.require(:room).permit(:name)`: `Some name` when the name was submitted.
    let roomNameParam (http: HttpContext) : Result<string option option, Error> =
        Params.require http "room"
        |> Result.map (fun room ->
            match room.Get "name" with
            | Str name -> Some(Some name)
            | Structured -> Some None
            | Absent -> None)

    /// `params.fetch(:user_ids, [])` as ids `User.where(id:)` can match.
    let userIdsParam (http: HttpContext) : int64 list =
        match Params.values http "user_ids" with
        | [] ->
            match Params.lookup http "user_ids" with
            | Str value -> Ruby.integerCast value |> Option.toList
            | _ -> []
        | values -> values |> List.choose Ruby.integerCast

    /// `User.where(id: ids)`, as ids of existing users (in id order, like the query).
    let existingUserIds (db: Campfire.Db.Ef.CampfireDb) (ids: int64 list) : int64 list = User.whereIds db ids |> List.map (fun user -> user.Id)

    /// Renders `users/sidebars/rooms/_shared` for `room` outside a request.
    let renderSharedRoom (http: HttpContext) (room: Room) : Result<Rendered, Error> =
        let app = Data.app http
        let baseUrl = Pages.rendererBaseUrl http
        Data.read http (fun db ->
            let presenter = Presenter(db, app, None)
            let sidebarRoom = presenter.SidebarRoom room
            let account = Account.first db
            let html =
                Page.renderDetachedAt app account baseUrl (fun _ -> Render.text (fun w -> SharedPartial.render w sidebarRoom))
            { Rendered.empty with SharedRoom = Some html })

    /// `rooms/show` with `find_messages`: the page around `params[:message_id]`, else the last page.
    let private renderShow (http: HttpContext) (room: Room) : Threading.Tasks.Task<Result<Falco.HttpHandler, Error>> =
        taskResult {
            let app = Data.app http
            let! (user: User) = Current.requireUser http
            let messageId = match Params.value http "message_id" with null -> None | id -> Ruby.integerCast id
            let requestHost = Some http.Request.Host.Host
            let! show =
                Data.readAsync http (fun db ->
                    task {
                        let original = Room.original db |> Option.exists (fun original -> original.Id = room.Id)
                        let messages =
                            match messageId |> Option.bind (fun id -> Message.findById db id) with
                            | Some message when message.RoomId = room.Id -> Message.pageAround db room.Id message
                            | _ -> Message.lastPage db room.Id
                        let presenter = Presenter(db, app, requestHost)
                        let roomGid = Campfire.RailsCompat.GlobalId.toParam (Gid.roomGid room)
                        // The page's message fragments are fetched up front.
                        let! items = presenter.Messages messages
                        return
                            ({ Room = presenter.RoomView(room, user)
                               UpdatedAt = DateTimeOffset(room.UpdatedAt)
                               User = PresenterSupport.userView app.Secrets user
                               Messages = items
                               Invitation = original && not (Message.paged db room.Id)
                               JoinCode = Account.first db |> Option.map (fun account -> account.JoinCode) |> Option.defaultValue ""
                               MessagesStreamName = Campfire.RailsCompat.Turbo.signedStreamName app.Secrets [ roomGid; "messages" ] }: Campfire.Views.Rooms.ShowView)
                    })
            return!
                Pages.viewPage http 200 (fun ctx -> ShowPage.rendered ctx show)
        }

    let destroyRoom (http: HttpContext) (room: Room) : Threading.Tasks.Task<Result<Falco.HttpHandler, Error>> =
        taskResult {
            do! Data.write http (fun tx -> Room.destroy tx room)
            // broadcast_remove_to :rooms, target: [ @room, :list ]
            (Data.app http).Broadcasts.RoomRemove room
            return! redirectToRoot http
        }

    // --- Actions -----------------------------------------------------------------------------------

    /// `index`: `redirect_to room_url(Current.user.rooms.last)` (inherited by the room-type
    /// controllers). With no rooms, `room_url(nil)` raises.
    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (user: User) = Current.requireUser http
                let! (room: Room option) = Data.read http (fun db -> Room.lastForUser db user.Id)
                match room with
                | None -> return! Result.Error(Internal(exn "No route matches room_url(nil)"))
                | Some room -> return! redirectToRoom http room.Id
            }

    /// `show`, also `GET /rooms/:room_id/@:message_id`.
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = setRoom http All
                Pages.rememberLastRoomVisited http room.Id
                return! renderShow http room
            }

    /// `destroy` (RoomsController and `Rooms::DirectsController`).
    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (room: Room) = setRoom http All
                do! ensureCanAdminister http room
                return! destroyRoom http room
            }

    /// `destroy` inherited by `Rooms::OpensController` and `Rooms::ClosedsController`, whose
    /// `set_room` doesn't run for it: `nil.destroy` raises NoMethodError.
    let destroyWithoutRoom: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                return! Result.Error(Internal(exn "undefined method 'destroy' for nil"))
            }
