// Port of the helpers at the top of rust/crates/campfire/src/controllers/presenters.rs that the layout,
// the rich text resolver and the controllers' shared code need (`user_summary`, `epoch_string`, the
// avatar paths; the presenter that maps rows to the views' models, `Presenter`, follows with the
// message controllers), `storage_error` of rust/crates/campfire/src/active_storage.rs, which every
// attachment lookup needs, and `touch` of presenters/accounts.rs, which `Attachments` calls (F# files
// are ordered, and `Accounts` reads attachments).
namespace Campfire.App.Presenters

open System
open System.Linq
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Campfire.Db
open Campfire.Rails
open Campfire.Rails.Routes
open Campfire.Views.Users

module Common =
    /// `Time#to_fs(:number)`: `%Y%m%d%H%M%S` in UTC (the app's time zone).
    let toFsNumberOf (time: DateTime) : string =
        time.ToUniversalTime().ToString("yyyyMMddHHmmss", Globalization.CultureInfo.InvariantCulture)

    /// `user.avatar_token`: `signed_id(purpose: :avatar)`.
    let avatarToken (secrets: Secrets) (userId: int64) : string = SignedId.generate secrets "User" userId (Some "avatar") None

    /// `fresh_user_avatar_path(user)`.
    let avatarPath (secrets: Secrets) (user: User) : string =
        Routes.freshUserAvatar (avatarToken secrets user.Id) (toFsNumberOf user.UpdatedAt)

    /// A storage error inside a database closure. (The rows are EF's now, so SQLite's own errors never pass through here:
    /// they are raised by the context and `Writes.run` maps them.)
    let storageError (error: Campfire.Storage.StorageError) : DbError =
        Other(Exception(Campfire.Storage.StorageError.message error))

    /// `storageError` as the exception a model raises inside a read or write.
    let raiseStorage (result: Campfire.Storage.StorageResult<'T>) : 'T =
        match result with
        | Ok value -> value
        | Error error -> Err.fail (storageError error)

    /// `to_fs(:epoch)` as a string (milliseconds), for an entity's `DateTime`.
    let epochStringOf (time: DateTime) : string =
        string (Campfire.Views.MessagesSupport.epochMs (DateTimeOffset(time)))

    /// A `User` row as the users views see it.
    let userSummary (secrets: Secrets) (user: User) : UserSummary =
        { Id = user.Id
          Name = user.Name
          Bio = Option.ofObj user.Bio
          EmailAddress = Option.ofObj user.EmailAddress
          Role =
            (match user.Role with
             | Campfire.Db.Role.Member -> Campfire.Views.Users.Role.Member
             | Campfire.Db.Role.Administrator -> Campfire.Views.Users.Role.Administrator
             | Campfire.Db.Role.Bot -> Campfire.Views.Users.Role.Bot
             | other -> failwith $"role {int other} out of range")
          Status =
            (match user.Status with
             | Campfire.Db.Status.Active -> Campfire.Views.Users.Status.Active
             | Campfire.Db.Status.Deactivated -> Campfire.Views.Users.Status.Deactivated
             | Campfire.Db.Status.Banned -> Campfire.Views.Users.Status.Banned
             | other -> failwith $"status {int other} out of range")
          AvatarPath = avatarPath secrets user }

    /// `record.touch` for the records an attachment belongs to, in the caller's write: a message also touches its room
    /// (`Message belongs_to :room, touch: true`), users and accounts only themselves. Other record types are left alone.
    let touchRecord (tx: Campfire.Db.Ef.Tx) (recordType: string) (id: int64) : unit =
        let db = tx.Db
        let now = tx.Now()
        match recordType with
        | "User" -> db.Users.Where(fun u -> u.Id = EF.Parameter id).ExecuteUpdate(fun s -> s.SetProperty((fun u -> u.UpdatedAt), now) |> ignore) |> ignore
        | "Account" -> db.Accounts.Where(fun a -> a.Id = EF.Parameter id).ExecuteUpdate(fun s -> s.SetProperty((fun a -> a.UpdatedAt), now) |> ignore) |> ignore
        | "Message" -> Message.touch tx (Message.find db id) |> ignore
        | _ -> ()
