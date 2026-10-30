// Port of the #[cfg(test)] module in rust/crates/views/src/helpers/forms.rs (the id and name sanitizing; the form helpers
// themselves are Falco.Markup functions, tested through the templates and the oracle)
module Campfire.Views.Tests.FormsTests

open Xunit
open Campfire.Views.Helpers

[<Fact>]
let ``sanitizes nested object names`` () =
    Assert.Equal("account_settings", Forms.sanitizeObjectName "account[settings]")
    Assert.Equal("user", Forms.sanitizeObjectName "user")

[<Fact>]
let ``a character that is not allowed in an id is one underscore, not one per UTF-16 unit`` () =
    // Rust maps `chars()`: an emoji is one character.
    Assert.Equal("__ab", Forms.sanitizeToId "😀 a]b")
    Assert.Equal("a_b-c:d.e", Forms.sanitizeToId "a b-c:d.e")
    Assert.Equal("a_b", Forms.sanitizeObjectName "a😀b")
    Assert.Equal("a_b", Forms.sanitizeObjectName "a[b]")
