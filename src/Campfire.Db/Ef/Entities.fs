// Design: plans/design-data-ef.md E2-E4. One record per Rails table; names, nullability and types follow `schema.sql`.
// User, Session and Account (Identity.fs), Room and Membership (Rooms.fs) and Message, Boost and RichTextRecord (Messages.fs) live in namespace Campfire.Db with their enums;
// `Names` maps Involvement and RoomType to the strings the database holds.
namespace Campfire.Db.Ef

open System
open Campfire.Db

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
