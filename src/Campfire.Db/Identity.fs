// Design: plans/design-data-ef.md E2-E3. The records the identity tables map to, with the enums and the password types they use.
// Port of rust/crates/db/src/models/{user,session,account}.rs (`reference/app/models/{user,session,account}.rb`).
namespace Campfire.Db

open System
open Campfire.Rails
// The Rails project's `Timestamp` (an alias of DateTimeOffset) is not used here: models keep UTC `DateTime` (RailsTime, E4).
open Campfire.Db

/// `enum :role, %i[ member administrator bot ]`
type Role =
    | Member = 0
    | Administrator = 1
    | Bot = 2

module Role =
    let name (role: Role) : string =
        match role with
        | Role.Member -> "member"
        | Role.Administrator -> "administrator"
        | _ -> "bot"

    let fromName (name: string) : Role option =
        match name with
        | "member" -> Some Role.Member
        | "administrator" -> Some Role.Administrator
        | "bot" -> Some Role.Bot
        | _ -> None



/// `enum :status, %i[ active deactivated banned ], default: :active`
type Status =
    | Active = 0
    | Deactivated = 1
    | Banned = 2

module Status =
    let name (status: Status) : string =
        match status with
        | Status.Active -> "active"
        | Status.Deactivated -> "deactivated"
        | _ -> "banned"

    let fromName (name: string) : Status option =
        match name with
        | "active" -> Some Status.Active
        | "deactivated" -> Some Status.Deactivated
        | "banned" -> Some Status.Banned
        | _ -> None



/// `users`: the Active Record model's attributes, one record for the whole app (EF maps it; the views' presenters read it).
/// Nullable columns are `string | null`, never `option` (E3); times are UTC `DateTime`s (E4).
[<CLIMutable>]
type User =
    { Id: int64
      Name: string
      EmailAddress: string | null
      PasswordDigest: string | null
      Role: Role
      Status: Status
      Bio: string | null
      BotToken: string | null
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// `sessions`
[<CLIMutable>]
type Session =
    { Id: int64
      UserId: int64
      Token: string
      IpAddress: string | null
      UserAgent: string | null
      LastActiveAt: DateTime
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// `accounts`
[<CLIMutable>]
type Account =
    { Id: int64
      Name: string
      JoinCode: string
      CustomStyles: string | null
      /// The raw `settings` JSON text; read it through `Account.settings`.
      Settings: string | null
      SingletonGuard: int64
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// A password hashed for `password_digest`. Hashing takes about 250 ms at cost 12, so it's done
/// before the write that saves it, never on the writer.
type PasswordDigest = PasswordDigest of string

/// Attributes for `User.create!`.
type NewUser =
    { Name: string
      EmailAddress: string option
      /// `has_secure_password`'s `password=`, already hashed.
      PasswordDigest: PasswordDigest option
      Role: Role
      Bio: string option
      BotToken: string option }

module NewUser =
    let create (name: string) : NewUser =
        { Name = name
          EmailAddress = None
          PasswordDigest = None
          Role = Role.Member
          Bio = None
          BotToken = None }

/// Attributes for `user.update`. `None` leaves an attribute alone.
type UserChanges =
    { Name: string option
      EmailAddress: string option option
      PasswordDigest: PasswordDigest option
      Role: Role option
      Status: Status option
      Bio: string option option
      BotToken: string option option }

module UserChanges =
    let none : UserChanges =
        { Name = None
          EmailAddress = None
          PasswordDigest = None
          Role = None
          Status = None
          Bio = None
          BotToken = None }

module PasswordDigest =
    let intoString (PasswordDigest digest) : string = digest

    /// `BCrypt::Password.create(password, cost:)`, in the `$2a$` format bcrypt-ruby writes.
    let digest (password: string) (cost: int) : string =
        match Password.digestWithCost password cost with
        | Ok digest -> digest
        | Error e -> Err.fail (DbError.other $"{e}")

    /// `BCrypt::Password.create(password, cost:)`. Blocking.
    let create (password: string) (cost: int) : PasswordDigest = PasswordDigest(digest password cost)

    /// `create` on the thread pool.
    let hash (password: string) (cost: int) : System.Threading.Tasks.Task<PasswordDigest> =
        System.Threading.Tasks.Task.Run(fun () -> create password cost)

