// Port of the #[cfg(test)] module in rust/crates/kit/src/session.rs (the flash; the session itself is ASP.NET Core's since unit 2.5)
module Campfire.Kit.Tests.SessionTests

open System
open Xunit
open Campfire.RailsCompat
open Campfire.Kit
open Campfire.Kit.Tests.Helpers

[<Fact>]
let ``flash round trip discards after one request`` () =
    let flash = Flash()
    flash.SetNotice "✓"
    let stored = flash.ToSessionValue()
    Assert.Equal(ValueSome(json """{"discard": [], "flashes": {"notice": "✓"}}"""), stored)
    let next = Flash.FromSessionValue stored
    Assert.Equal("✓", next.Notice)
    Assert.Equal(ValueNone, next.ToSessionValue())

[<Fact>]
let ``flash now is not persisted`` () =
    let flash = Flash()
    flash.Now("alert", "Too many requests or unauthorized.")
    Assert.Equal("Too many requests or unauthorized.", flash.Alert)
    Assert.Equal(ValueNone, flash.ToSessionValue())

[<Fact>]
let ``flash keep and legacy discard lists`` () =
    let stored = json """{"discard": ["alert"], "flashes": {"notice": "hi", "alert": "gone"}}"""
    let flash = Flash.FromSessionValue(ValueSome stored)
    Assert.Null flash.Alert
    flash.Keep(ValueSome "notice")
    Assert.Equal(ValueSome(json """{"discard": [], "flashes": {"notice": "hi"}}"""), flash.ToSessionValue())
