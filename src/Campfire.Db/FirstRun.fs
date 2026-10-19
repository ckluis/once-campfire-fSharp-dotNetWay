// Port of rust/crates/db/src/models/first_run.rs on EF Core (design section 4, unit 1.3).
// `reference/app/models/first_run.rb`
namespace Campfire.Db

open Campfire.Db.Ef

module FirstRun =
    [<Literal>]
    let AccountName = "Campfire"

    [<Literal>]
    let FirstRoomName = "All Talk"

    /// `FirstRun.create!(user_params)`: the account, an administrator, and the first (open) room. Rails runs these as separate
    /// saves; the open room grants itself to every active user after commit (`Rooms::Open#grant_access_to_all_users`), which
    /// includes the administrator, so Rails' explicit `grant_to` that follows is a no-op and is not repeated.
    let create (tx: Tx) (name: string) (emailAddress: string) (passwordDigest: PasswordDigest) : User =
        Account.create tx AccountName |> ignore
        let administrator =
            User.create
                tx
                { NewUser.create name with
                    EmailAddress = Some emailAddress
                    PasswordDigest = Some passwordDigest
                    Role = Role.Administrator }
        Room.create tx RoomType.Open (Some FirstRoomName) administrator.Id |> ignore
        administrator
