// Port of rust/crates/campfire/src/controllers/sessions/transfers.rs
//
// `Sessions::TransfersController` (reference/app/controllers/sessions/transfers_controller.rb): sign in on another device with
// a user's transfer link. Web actions (unit 2.9).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.App.Web

module SessionTransfers =
    module TransferPage = Campfire.Views.Templates.Sessions.Transfers.Show

    let private showSize = Campfire.Views.RenderSize()

    /// `allow_unauthenticated_access`: an auto-submitting form that PUTs back to this URL.
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                // `url_for({})`: this request's own path.
                let action = Respond.rawPath http
                return!
                    Pages.framedPage
                        http
                        200
                        showSize
                        (fun w ctx -> TransferPage.render w ctx action)
                        TransferPage.head
                        (fun w _ -> TransferPage.content w action)
            }

    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                let transferId = match Params.value http "id" with null -> "" | id -> id
                let userId = Accounts.userIdFromTransferId (Data.app http).Secrets transferId (Data.now http)
                // `User.active.find_by_transfer_id(params[:id])`
                let! user =
                    match userId with
                    | Some id -> Data.read http (fun db -> User.findById db id |> Option.filter User.isActive)
                    | None -> Ok None
                match user with
                | Some user ->
                    let! (_: Session) = Authentication.startNewSessionFor http user
                    let location =
                        match Campfire.Kit.ReturnTo.take http with
                        | null -> Respond.urlFor http (Routes.root ())
                        | url -> url
                    return! Respond.redirectTo http location
                | None -> return Respond.head http 400
            }
