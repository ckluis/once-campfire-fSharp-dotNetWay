// Port of the tests of rust/crates/campfire/src/controllers/presenters.rs: `to_fs_number` and
// `cache_key_with_version` (`Presenters/Common.fs`), `all_emoji` and the Jbuilder keys (`Presenters/Presenter.fs`).
module Campfire.App.Tests.PresentersTests

open System
open Xunit
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Views

[<Fact>]
let ``cache versions use usec`` () =
    let time = DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1230L)
    Assert.Equal("messages/1-20240601120000000123", FragmentCache.cacheKeyWithVersion "messages" 1L (DateTimeOffset time))
    Assert.Equal("20240601120000", Common.toFsNumberOf time)

[<Fact>]
let ``all emoji matches ruby`` () =
    Assert.True(PresenterSupport.allEmoji "👍")
    Assert.True(PresenterSupport.allEmoji "❤️")
    Assert.False(PresenterSupport.allEmoji "hi 👍")
    Assert.False(PresenterSupport.allEmoji "")

[<Fact>]
let ``jbuilder keys match their formatted version`` () =
    let time = DateTimeOffset(DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc).AddTicks(1230L))
    let key = KeyBuf.Of ""
    PresenterSupport.jbuilderKey key "messages/_message" "messages" 7L time "https://example.com"
    let version = FragmentCache.cacheKeyWithVersion "messages" 7L time
    Assert.Equal($"jbuilder/views/messages/_message:{PresenterSupport.jbuilderDigest}/{version}/https://example.com", key.ToString())
