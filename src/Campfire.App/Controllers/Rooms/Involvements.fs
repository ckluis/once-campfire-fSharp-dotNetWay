// Port of rust/crates/campfire/src/controllers/rooms/involvements.rs
//
// `Rooms::InvolvementsController` (reference/app/controllers/rooms/involvements_controller.rb).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Rooms
open Campfire.App.Web

module Involvements =
    module ShowPage = Campfire.Views.Templates.Rooms.Involvements.Show

    /// `params[:involvement]` as the enum casts it: a blank value (missing, "", "  ", `[]`) is stored
    /// as nil, anything that isn't one of the values raises ArgumentError ('... is not a valid
    /// involvement'). Verified against the reference with `update!(involvement: "")`.
    let private involvementParam (http: HttpContext) : Result<Involvement option, Error> =
        match Params.lookup http "involvement" with
        | Absent -> Ok None
        | Str value when System.String.IsNullOrWhiteSpace value -> Ok None
        | Str value ->
            match Involvement.fromName value with
            | Some involvement -> Ok(Some involvement)
            | None -> Result.Error(Internal(exn $"\"{value}\" is not a valid involvement"))
        // An empty array is blank; any other hash or array is not a valid value.
        | Structured ->
            if Params.values http "involvement" |> List.isEmpty then
                Ok None
            else
                Result.Error(Internal(exn "is not a valid involvement"))

    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (membership: Membership), (room: Room) = Before.setRoom http
                let involvement: InvolvementView =
                    { RoomId = room.Id
                      Kind = PresenterSupport.roomKind room.Type
                      Involvement = (if membership.Involvement.HasValue then Involvement.name membership.Involvement.Value else "") }
                return! Pages.page http 200 (fun ctx -> Layouts.ViewPage.content [ ShowPage.render ctx involvement ] [])
            }

    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (membership: Membership), (room: Room) = Before.setRoom http
                let! involvement = involvementParam http
                let previous = if membership.Involvement.HasValue then Some membership.Involvement.Value else None
                let! (membership: Membership) = Data.write http (fun tx -> Membership.updateInvolvement tx membership involvement)

                // broadcast_visibility_changes
                let! partials = RoomsController.renderSharedRoom http room
                do!
                    (Data.app http).Broadcasts.InvolvementChange(room, membership, previous, Rendered.partials partials)
                    |> Result.mapError (fun nilInquiry -> Internal(exn (string nilInquiry)))

                return! Respond.redirectTo http (Respond.urlFor http (Routes.roomInvolvement room.Id))
            }
