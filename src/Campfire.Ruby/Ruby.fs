// Port of rust/crates/ruby/src/lib.rs
/// Ruby's own string behaviour, as Rails applies it, in one place for every crate: ERB
/// escaping, `String#to_i` and `#strip`, `Float#to_s`, `CGI.escape`,
/// `ERB::Util.url_encode`, and how Active Record binds an integer. Each is
/// checked against Ruby itself on every character from U+0000 to U+00FF and a list of edge cases
/// (`vectors/ruby_core.json`, written by `reference-tools/ruby_core.rb`).
///
/// It depends on nothing but the base library. `Erb` is a module of its own, as in
/// Rust; the rest is re-exported here the way `lib.rs` re-exports it.
module Campfire.Ruby.Ruby

let floatToS = Float.floatToS
let integerCast = Integer.integerCast
let toI = Integer.toI
let toIChecked = Integer.toIChecked
let strip = RubyString.strip

/// `CGI.escape`: everything but `A-Za-z0-9_.-~` is percent-encoded, and a space becomes `+`.
let cgiEscape (s: string) : string = System.Uri.EscapeDataString(s).Replace("%20", "+")

/// `ERB::Util.url_encode`: the same set, a space as `%20`. Addressable's `encode_component(s, UNRESERVED)` keeps it too.
let urlEncode (s: string) : string = System.Uri.EscapeDataString s
