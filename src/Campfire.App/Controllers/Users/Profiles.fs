// Port of rust/crates/campfire/src/controllers/users/profiles.rs
//
// `Users::ProfilesController` (reference/app/controllers/users/profiles_controller.rb): the signed-in user's own profile.
// Web actions (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.Views
open Campfire.Views.Users
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.App.Web

module Profiles =
    module ShowPage = Campfire.Views.Templates.Users.Profiles.Show

    /// `set_user` (`Current.user`); memberships partitioned into direct and shared rooms.
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let! (user: User) = Current.requireUser http
                let secrets = (Data.app http).Secrets
                let transferId = Accounts.transferId secrets user.Id (Data.now http)
                let! (directMemberships, sharedMemberships) = Data.read http (fun db -> Accounts.profileMemberships db user)
                let! avatarAttached = Data.read http (fun db -> (Attachments.attachedBlob db "User" user.Id "avatar").IsSome)
                let profile: ProfileShow =
                    { User = Common.userSummary secrets user
                      AvatarAttached = avatarAttached
                      TransferId = transferId
                      SharedMemberships = sharedMemberships
                      DirectMemberships = directMemberships }
                return!
                    Pages.page http 200 (fun ctx -> ShowPage.rendered ctx profile)
            }

    /// `@user.update user_params`, then `redirect_to user_profile_url, notice: update_notice`.
    let update: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (user: User) = Current.requireUser http

                // params.require(:user).permit(:name, :avatar, :email_address, :password, :bio).compact
                let! required = Params.require http "user"
                let parameters = Params.permit required [ "name"; "email_address"; "password"; "bio" ]
                let present (key: string) : string option = Map.tryFind key parameters
                // `password=` ignores a blank password.
                let! passwordDigest = Data.passwordDigest http (present "password" |> Option.filter (fun password -> password <> ""))
                let changes =
                    { UserChanges.none with
                        Name = present "name"
                        EmailAddress = present "email_address" |> Option.map Some
                        PasswordDigest = passwordDigest
                        Bio = present "bio" |> Option.map Some }
                // `.compact` drops a nil avatar before it's assigned; any other value counts as given.
                let avatar, given = Uploads.assignment http required "avatar"
                // `params[:user][:avatar] ? ... : "✓"`: any non-nil value counts.
                let notice = if given then "It may take up to 30 minutes to change everywhere." else "✓"

                let! avatar = Uploads.stage http avatar
                let! pending =
                    Data.write http (fun tx ->
                        User.update tx user changes |> ignore
                        AttachmentWrites.assign tx (Record.user user.Id) "avatar" avatar)
                AttachmentWrites.analyzeLater (Data.app http) pending

                return! Respond.redirectToWith http (Respond.urlFor http (Routes.userProfile ())) { RedirectOptions.Default with Notice = notice }
            }
