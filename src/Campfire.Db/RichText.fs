// Port of rust/crates/db/src/rich_text.rs
namespace Campfire.Db

/// What the models need from Action Text. The real pipeline lives in `Campfire.RichText`; the app plugs it in through `RichText`.
/// Tests have a stand-in, `Testing.BasicRichText`.
///
/// Both methods get the context the caller is already on (the write's transaction, or the request's read) for any record lookups.
type RichText =
    /// `ActionText::Content#to_plain_text` of a stored body. Mention attachments render as
    /// `attachable_plain_text_representation`, i.e. `"@#{name}"`
    /// (`reference/app/models/user/mentionable.rb`).
    abstract ToPlainText: db: Ef.CampfireDb * html: string -> string

    /// `body.attachables.grep(User).uniq`: user ids from mention attachments, in document
    /// order, deduplicated (`reference/app/models/message/mentionee.rb`).
    abstract MentionedUserIds: db: Ef.CampfireDb * html: string -> int64 list
