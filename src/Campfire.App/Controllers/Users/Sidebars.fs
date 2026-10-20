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

    let private showSize = RenderSize()

    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let! (user: User) = Current.requireUser http
                let app = Data.app http
                let secrets = app.Secrets
                let fragments = app.FragmentCache
                let! sidebar, restricted =
                    Data.read http (fun db ->
                        // The direct rooms' fragments come from the store the render then uses.
                        let sidebar = FragmentCache.withCache fragments (fun () -> Accounts.sidebar db secrets user)
                        let restricted =
                            Account.first db
                            |> Option.exists (fun account -> AccountSettings.restrictRoomCreationToAdministrators (Account.settings account))
                        sidebar, restricted)

                let data: SidebarShow =
                    { CurrentUser = Common.userSummary secrets user
                      // turbo_stream_from :rooms / turbo_stream_from Current.user, :rooms
                      RoomsStream = Campfire.RailsCompat.Turbo.signedStreamName secrets [ "rooms" ]
                      UserRoomsStream = Campfire.RailsCompat.Turbo.signedStreamName secrets [ Campfire.RailsCompat.GlobalId.toParam (Gid.userGid user.Id); "rooms" ]
                      DirectMemberships = sidebar.DirectMemberships
                      DirectPlaceholderUsers = sidebar.DirectPlaceholderUsers
                      OtherMemberships = sidebar.OtherMemberships
                      // `Current.user.administrator? || !Current.account.settings.restrict_room_creation_to_administrators?`
                      CanCreateRooms = User.isAdministrator user || not restricted }
                return!
                    Pages.framedPage
                        http
                        200
                        showSize
                        (fun w ctx -> ShowPage.render w ctx data)
                        ShowPage.head
                        (fun w ctx -> ShowPage.content w ctx data)
            }
