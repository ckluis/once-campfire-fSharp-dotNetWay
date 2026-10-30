// Design: plans/design-data-ef.md E2-E4. The records the message tables map to.
// Port of rust/crates/db/src/models/{message,boost,rich_text_record}.rs (`reference/app/models/{message,boost}.rb`, ActionText::RichText).
namespace Campfire.Db

open System

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

/// `action_text_rich_texts`: Campfire has one, `Message#body`.
[<CLIMutable>]
type RichTextRecord =
    { Id: int64
      RecordType: string
      RecordId: int64
      Name: string
      Body: string | null
      CreatedAt: DateTime
      UpdatedAt: DateTime }

/// `searches` (`reference/app/models/search.rb`): a user's recent queries.
[<CLIMutable>]
type Search =
    { Id: int64
      UserId: int64
      Query: string
      CreatedAt: DateTime
      UpdatedAt: DateTime }
