// Port of rust/crates/campfire/src/controllers/rooms/refreshes.rs
//
// `Rooms::RefreshesController` (reference/app/controllers/rooms/refreshes_controller.rb): what
// changed in a room since the client last loaded it.
namespace Campfire.App.Controllers

open System
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Rails.Ruby
open Campfire.Views
open Campfire.Views.Rooms
open Campfire.App.Web

module Refreshes =
    module ShowPage = Campfire.Views.Templates.Rooms.Refreshes.ShowTurboStream


    let private minMicrosecond = (DateTime.MinValue.Ticks - DateTime.UnixEpoch.Ticks) / 10L
    let private maxMicrosecond = (DateTime.MaxValue.Ticks - DateTime.UnixEpoch.Ticks) / 10L

    /// `Time.at(0, params[:since].to_i, :millisecond)`
    let private setLastUpdatedAt (http: HttpContext) : Result<DateTime, Error> =
        let since =
            match Params.lookup http "since" with
            | Absent -> Ok 0L
            | Str value -> Ok(Ruby.toI value)
            // `to_i` isn't defined for a hash or an array.
            | Structured -> Result.Error(Internal(exn "undefined method 'to_i'"))
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

    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (_: Membership), (room: Room) = Before.setRoom http
                let! lastUpdatedAt = setLastUpdatedAt http
                do! Formats.respondTo http [ Format.TurboStream ] |> Result.map ignore

                let app = Data.app http
                let requestHost = Some http.Request.Host.Host
                let since = RailsTime.truncate lastUpdatedAt
                let! refresh =
                    Data.readAsync http (fun db ->
                        task {
                            let newMessages = Message.pageCreatedSince db room.Id since
                            let newIds = newMessages |> List.map (fun message -> message.Id)
                            let updatedMessages = Message.pageUpdatedSince db room.Id since newIds
                            let presenter = Presenter(db, app, requestHost)
                            let! newItems = presenter.Messages newMessages
                            let! updatedItems = presenter.Messages updatedMessages
                            return
                                ({ RoomId = room.Id
                                   RoomKind = PresenterSupport.roomKind room.Type
                                   NewMessages = newItems
                                   UpdatedMessages = updatedItems }: RefreshView)
                        })
                return!
                    Pages.bare http 200 Format.TurboStream (fun _ -> ShowPage.output refresh)
            }
