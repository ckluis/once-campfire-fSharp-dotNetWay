// Design: plans/design-data-ef.md E2-E3. The records the room tables map to, with the enums they use.
// Port of rust/crates/db/src/models/{room,membership}.rs (`reference/app/models/{room,membership}.rb`, `rooms/*.rb`).
namespace Campfire.Db

open System

/// `enum :involvement, %w[ invisible nothing mentions everything ].index_by(&:itself)` (strings in the database, through
/// the converter in `Ef/CampfireDb.fs`).
type Involvement =
    | Invisible = 0
    | Nothing = 1
    | Mentions = 2
    | Everything = 3

module Involvement =
    let name (involvement: Involvement) : string =
        match involvement with
        | Involvement.Invisible -> "invisible"
        | Involvement.Nothing -> "nothing"
        | Involvement.Mentions -> "mentions"
        | Involvement.Everything -> "everything"
        | other -> failwith $"involvement {other} out of range"

    let fromName (name: string) : Involvement option =
        match name with
        | "invisible" -> Some Involvement.Invisible
        | "nothing" -> Some Involvement.Nothing
        | "mentions" -> Some Involvement.Mentions
        | "everything" -> Some Involvement.Everything
        | _ -> None

/// The STI `type` column (`Rooms::Open`, `Rooms::Closed`, `Rooms::Direct`).
type RoomType =
    | Open = 0
    | Closed = 1
    | Direct = 2

module RoomType =
    let className (roomType: RoomType) : string =
        match roomType with
        | RoomType.Open -> "Rooms::Open"
        | RoomType.Closed -> "Rooms::Closed"
        | RoomType.Direct -> "Rooms::Direct"
        | other -> failwith $"room type {other} out of range"

    let fromClassName (name: string) : RoomType option =
        match name with
        | "Rooms::Open" -> Some RoomType.Open
        | "Rooms::Closed" -> Some RoomType.Closed
        | "Rooms::Direct" -> Some RoomType.Direct
        | _ -> None

    /// `default_involvement`: "everything" in direct rooms, "mentions" elsewhere.
    let defaultInvolvement (roomType: RoomType) : Involvement =
        match roomType with
        | RoomType.Direct -> Involvement.Everything
        | _ -> Involvement.Mentions

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
      /// Nullable in the schema (default "mentions").
      Involvement: Nullable<Involvement>
      UnreadAt: Nullable<DateTime>
      ConnectedAt: Nullable<DateTime>
      Connections: int64
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// What the sidebar's one statement returns (`Membership.visibleWithOrderedRoom`): a membership and its room, flat. `SqlQuery`
/// maps columns by name; the times stay text here and `RailsTime.ofDb` reads them.
[<CLIMutable>]
type MembershipRoomRow =
    { MembershipId: int64
      RoomId: int64
      UserId: int64
      Involvement: string | null
      UnreadAt: string | null
      ConnectedAt: string | null
      Connections: int64
      MembershipCreatedAt: string
      MembershipUpdatedAt: string
      RoomName: string | null
      RoomType: string
      RoomCreatorId: int64
      RoomCreatedAt: string
      RoomUpdatedAt: string }
