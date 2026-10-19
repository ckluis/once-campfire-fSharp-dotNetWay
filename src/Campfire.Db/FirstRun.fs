// Port of rust/crates/db/src/models/first_run.rs on EF Core (design section 4, unit 1.3).
// `reference/app/models/first_run.rb`
namespace Campfire.Db

open Microsoft.EntityFrameworkCore
open Campfire.Db.Ef

module FirstRun =
    [<Literal>]
    let AccountName = "Campfire"

    [<Literal>]
    let FirstRoomName = "All Talk"

    /// `FirstRun.create!(user_params)`: the account, an administrator, and the first (open) room. Rails runs these as separate
    /// saves; the open room grants itself to every active user after commit (`Rooms::Open#grant_access_to_all_users`), which
    /// includes the administrator, so Rails' explicit `grant_to` that follows is a no-op and is not repeated. The room is
    /// inserted here as a row until unit 1.4 moves `Room` onto EF.
    let create (tx: Tx) (name: string) (emailAddress: string) (passwordDigest: PasswordDigest) : User =
        Account.create tx AccountName |> ignore
        let administrator =
            User.create
                tx
                { NewUser.create name with
                    EmailAddress = Some emailAddress
                    PasswordDigest = Some passwordDigest
                    Role = Role.Administrator }
        let room =
            Data.insert
                tx.Db
                { Id = 0L
                  Name = FirstRoomName
                  Type = RoomType.Open
                  CreatorId = administrator.Id
                  CreatedAt = System.DateTime.MinValue
                  UpdatedAt = System.DateTime.MinValue }
        let roomId = room.Id
        let involvement = Names.involvementToDb Involvement.Mentions
        tx.AfterCommit(fun tx ->
            tx.Db.Database.ExecuteSql(
                $"INSERT INTO memberships (created_at, involvement, room_id, updated_at, user_id) SELECT STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), {involvement}, {roomId}, STRFTIME('%%Y-%%m-%%d %%H:%%M:%%f', 'NOW'), users.id FROM users WHERE users.status = 0 ON CONFLICT DO NOTHING"
            )
            |> ignore)
        administrator
