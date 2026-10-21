// Port of rust/crates/campfire/src/controllers/welcome.rs
//
// `WelcomeController` (reference/app/controllers/welcome_controller.rb): the root URL. A Web action (unit 2.9).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Views.Templates.Welcome
open Campfire.App.Web

module Welcome =
    /// To the last room visited, or a page saying there are no rooms yet.
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! user = Current.requireUser http
                let! anyRooms = Data.read http (fun db -> not (List.isEmpty (Room.forUser db user.Id)))
                if anyRooms then
                    // `redirect_to room_url(last_room_visited)`
                    let! (lastRoom: Room option) = Pages.lastRoomVisited http
                    match lastRoom with
                    | Some room -> return! Respond.redirectTo http (Respond.urlFor http (Routes.room room.Id))
                    | None -> return! Result.Error(Internal(exn "no last room"))
                else
                    do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                    return! Pages.page http 200 (fun ctx -> Show.rendered ctx user.Name)
            }
