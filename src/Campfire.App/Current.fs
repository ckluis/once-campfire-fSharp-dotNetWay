// Port of the `Current` part of rust/crates/campfire/src/concerns.rs
//
// `authenticated_by`; `Web/Current.fs` holds the rest of `Current`.
namespace Campfire.App

open Campfire.Db

/// `authenticated_by` (`"".inquiry`, `"session"` or `"bot_key"`).
type AuthenticatedBy =
    | Anonymous
    | BySession
    | BotKey
