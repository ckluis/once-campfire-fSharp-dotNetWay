// Design: plans/design-data-ef.md E2-E4. One record per Rails table; names, nullability and types follow `schema.sql`.
// Role and Status are the enums in Models.fs (integers in the database); Involvement and RoomType here are the enum forms of the
// port's unions (strings in the database, through the converters in CampfireDb.fs). Unit 1.11 deletes the port's twins.
namespace Campfire.Db.Ef

open System

type Involvement =
    | Invisible = 0
    | Nothing = 1
    | Mentions = 2
    | Everything = 3

/// The STI `type` column (`Rooms::Open`, `Rooms::Closed`, `Rooms::Direct`).
type RoomType =
    | Open = 0
    | Closed = 1
    | Direct = 2

module Names =
    let roomTypeToDb (t: RoomType) : string =
        match t with
        | RoomType.Open -> "Rooms::Open"
        | RoomType.Closed -> "Rooms::Closed"
        | RoomType.Direct -> "Rooms::Direct"
        | other -> failwith $"room type {other} out of range"

    let roomTypeOfDb (s: string) : RoomType =
        match s with
        | "Rooms::Open" -> RoomType.Open
        | "Rooms::Closed" -> RoomType.Closed
        | "Rooms::Direct" -> RoomType.Direct
        | other -> failwith $"unknown room type '{other}'"

    let involvementToDb (i: Involvement) : string =
        match i with
        | Involvement.Invisible -> "invisible"
        | Involvement.Nothing -> "nothing"
        | Involvement.Mentions -> "mentions"
        | Involvement.Everything -> "everything"
        | other -> failwith $"involvement {other} out of range"

    let involvementOfDb (s: string) : Involvement =
        match s with
        | "invisible" -> Involvement.Invisible
        | "nothing" -> Involvement.Nothing
        | "mentions" -> Involvement.Mentions
        | "everything" -> Involvement.Everything
        | other -> failwith $"unknown involvement '{other}'"

[<CLIMutable>]
type User =
    { Id: int64
      Name: string
      EmailAddress: string | null
      PasswordDigest: string | null
      Role: Campfire.Db.Role
      Status: Campfire.Db.Status
      Bio: string | null
      BotToken: string | null
      CreatedAt: DateTime
      UpdatedAt: DateTime }

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

[<CLIMutable>]
type Room =
    { Id: int64
      Name: string | null
      Type: RoomType
      CreatorId: int64
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type Membership =
    { Id: int64
      RoomId: int64
      UserId: int64
      Involvement: Nullable<Involvement>
      UnreadAt: Nullable<DateTime>
      ConnectedAt: Nullable<DateTime>
      Connections: int64
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type Message =
    { Id: int64
      RoomId: int64
      CreatorId: int64
      ClientMessageId: string
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type Boost =
    { Id: int64
      MessageId: int64
      BoosterId: int64
      Content: string
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// `action_text_rich_texts`
[<CLIMutable>]
type RichTextRecord =
    { Id: int64
      RecordType: string
      RecordId: int64
      Name: string
      Body: string | null
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type Account =
    { Id: int64
      Name: string
      JoinCode: string
      CustomStyles: string | null
      /// The `settings` JSON text.
      Settings: string | null
      SingletonGuard: int64
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type Search =
    { Id: int64
      UserId: int64
      Query: string
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type Ban =
    { Id: int64
      UserId: int64
      IpAddress: string
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type Webhook =
    { Id: int64
      UserId: int64
      Url: string | null
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type PushSubscription =
    { Id: int64
      UserId: int64
      Endpoint: string | null
      P256dhKey: string | null
      AuthKey: string | null
      UserAgent: string | null
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// `active_storage_blobs` (no `updated_at`)
[<CLIMutable>]
type Blob =
    { Id: int64
      Key: string
      Filename: string
      ContentType: string | null
      /// The `metadata` JSON text.
      Metadata: string | null
      ServiceName: string
      ByteSize: int64
      Checksum: string | null
      CreatedAt: DateTime }

/// `active_storage_attachments` (no `updated_at`)
[<CLIMutable>]
type Attachment =
    { Id: int64
      Name: string
      RecordType: string
      RecordId: int64
      BlobId: int64
      CreatedAt: DateTime }

/// `active_storage_variant_records` (no timestamps)
[<CLIMutable>]
type VariantRecord =
    { Id: int64
      BlobId: int64
      VariationDigest: string }
