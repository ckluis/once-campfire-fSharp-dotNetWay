// Port of rust/crates/campfire/src/controllers/users/sidebars.rs
//
// `Users::SidebarsController` (reference/app/controllers/users/sidebars_controller.rb): the room list, loaded into the
// `user_sidebar` turbo frame. A Web action (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.Views
open Campfire.Views.Users
open Campfire.App
open Campfire.App.Channels
open Campfire.App.Presenters
open Campfire.Db
open Campfire.App.Web

module Sidebars =
    module ShowPage = Campfire.Views.Templates.Users.Sidebars.Show

    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let! (user: User) = Current.requireUser http
                let app = Data.app http
                let secrets = app.Secrets
                let! sidebar, restricted =
                    Data.readAsync http (fun db ->
                        task {
                            // The direct rooms' fragments are fetched up front.
                            let! sidebar = Accounts.sidebar db secrets app.Fragments (Page.fragmentContext app) user
                            let restricted =
                                Account.first db
                                |> Option.exists (fun account -> AccountSettings.restrictRoomCreationToAdministrators (Account.settings account))
                            return sidebar, restricted
                        })

                let data: SidebarShow =
                    { CurrentUser = Common.userSummary secrets user
                      // turbo_stream_from :rooms / turbo_stream_from Current.user, :rooms
                      RoomsStream = Campfire.Rails.Turbo.signedStreamName secrets [ "rooms" ]
                      UserRoomsStream = Campfire.Rails.Turbo.signedStreamName secrets [ Campfire.Rails.GlobalId.toParam (Gid.userGid user.Id); "rooms" ]
                      DirectMemberships = sidebar.DirectMemberships
                      DirectPlaceholderUsers = sidebar.DirectPlaceholderUsers
                      OtherMemberships = sidebar.OtherMemberships
                      // `Current.user.administrator? || !Current.account.settings.restrict_room_creation_to_administrators?`
                      CanCreateRooms = User.isAdministrator user || not restricted }
                return! Pages.page http 200 (fun ctx -> ShowPage.rendered ctx data)
            }
