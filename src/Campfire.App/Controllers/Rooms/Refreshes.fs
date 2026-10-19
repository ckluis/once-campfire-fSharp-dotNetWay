// Port of rust/crates/campfire/src/controllers/rooms/refreshes.rs
//
// `Rooms::RefreshesController` (reference/app/controllers/rooms/refreshes_controller.rb): what
// changed in a room since the client last loaded it.
namespace Campfire.App.Controllers

open System
open System.Threading.Tasks
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Kit
open Campfire.Ruby
open Campfire.Views
open Campfire.Views.Rooms

module Refreshes =
    module ShowPage = Campfire.Views.Templates.Rooms.Refreshes.ShowTurboStream

    let private showSize = RenderSize()

    let private minMicrosecond = (DateTime.MinValue.Ticks - DateTime.UnixEpoch.Ticks) / 10L
    let private maxMicrosecond = (DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks) / 10L

    /// `Time.at(0, params[:since].to_i, :millisecond)`
    let private setLastUpdatedAt (c: Ctx) : Result<DateTime, Error> =
        let since =
            match c.Param "since" with
            | ValueNone -> Ok 0L
            | ValueSome(Param.Str value) -> Ok(Ruby.toI value)
            | ValueSome Param.Null -> Ok 0L
            // `to_i` isn't defined for a hash or an array.
            | ValueSome _ -> Error(Internal(exn "undefined method 'to_i'"))
        since
        |> Result.map (fun since ->
            // Outside the representable range (a crafted `since`), the nearest end of it (here the .NET
            // `DateTime` range, which only matters to what `created_at >` compares against).
            let microseconds = Int128.op_Implicit since * Int128.op_Implicit 1000L
            let clamped =
                if microseconds < Int128.op_Implicit minMicrosecond then minMicrosecond
                elif microseconds > Int128.op_Implicit maxMicrosecond then maxMicrosecond
                else int64 microseconds
            DateTime(DateTime.UnixEpoch.Ticks + clamped * 10L, DateTimeKind.Utc))

    let show (c: Ctx) : Task<Result<Response, Error>> =
        act {
            do! Concerns.beforeActions c Before.Default
            let! (_: Membership), (room: Room) = Concerns.setRoom c
            let! lastUpdatedAt = setLastUpdatedAt c
            do! c.RespondTo [ Format.TurboStream ] |> Result.map ignore

            let app = c.App
            let requestHost = Some c.Request.Host
            let since = RailsTime.truncate lastUpdatedAt
            let! refresh =
                c.Read(fun db ->
                    let newMessages = Message.pageCreatedSince db room.Id since
                    let newIds = newMessages |> List.map (fun message -> message.Id)
                    let updatedMessages = Message.pageUpdatedSince db room.Id since newIds
                    let presenter = Presenter(db, app, requestHost)
                    FragmentCache.withCache app.FragmentCache (fun () ->
                        ({ RoomId = room.Id
                           RoomKind = PresenterSupport.roomKind room.Type
                           NewMessages = presenter.Messages newMessages
                           UpdatedMessages = presenter.Messages updatedMessages }: RefreshView)))
            return!
                Page.bare c Status.Ok Format.TurboStream (fun ctx ->
                    showSize.Render(fun w -> ShowPage.render w ctx refresh))
        }
