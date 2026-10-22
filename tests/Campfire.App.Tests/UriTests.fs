// Port of the tests in rust/crates/richtext/src/uri.rs
module Campfire.App.Tests.UriTests

open Xunit
open Campfire.RichText

let private ok (r: Result<'a, 'e>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "expected Ok, got Error %A" e

let private isError (r: Result<'a, 'e>) : bool =
    match r with
    | Error _ -> true
    | Ok _ -> false

[<Fact>]
let ``parses_like_ruby`` () =
    let uri = ok (RubyUri.parse "https://x.com/dhh/status/1?s=20")
    Assert.Equal(Some "x.com", uri.Host)
    Assert.Equal(Some "s=20", uri.Query)
    Assert.True(isError (RubyUri.parse "http://exa mple.com/ "))
    Assert.Equal(None, (ok (RubyUri.parse "https:/rooms/1")).Host)
    Assert.Equal(Some "rooms/1", (ok (RubyUri.parse "https:rooms/1")).Opaque)
    Assert.Equal(Some "", (ok (RubyUri.parse "https://")).Host)
    Assert.Equal(Some "[::1]", (ok (RubyUri.parse "http://[::1]/x")).Host)
    Assert.Equal(Error InvalidComponent, RubyUri.parse "mailto:foo")
    Assert.True(not (isError (RubyUri.parse "mailto:a@b.com")))
    Assert.Equal("https://x.com/a", RubyUri.toS (ok (RubyUri.parse "https://x.com:443/a")))

[<Fact>]
let ``downcases_the_scheme_like_ruby`` () =
    // `URI.parse(s)` in the reference: `scheme=` downcases.
    Assert.Equal(Some "https", (ok (RubyUri.parse "HTTPS://x.com/a")).Scheme)
    Assert.Equal("http://X.com/a", RubyUri.toS (ok (RubyUri.parse "HTTP://X.com:80/a")))
    Assert.Equal("file:///etc", RubyUri.toS (ok (RubyUri.parse "FILE:///etc")))
    Assert.Equal("mailto:a@b.com", RubyUri.toS (ok (RubyUri.parse "MailTo:a@b.com")))
