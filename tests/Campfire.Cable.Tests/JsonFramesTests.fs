/// G1: the canonical frame comparison (plans/design-cable.md C15).
module Campfire.Cable.Tests.JsonFramesTests

open Xunit
open Campfire.Tests.JsonFrames

let private differ (a: string) (b: string) =
    Assert.NotEqual<string>(canonical a, canonical b)
    Assert.ThrowsAny<exn>(fun () -> assertSameFrame a b) |> ignore

[<Fact>]
let ``G1 Rails escaping and relaxed escaping compare equal`` () =
    let rails = """{"identifier":"{\"channel\":\"RoomChannel\"}","message":"<turbo-stream action=\"append\">&</turbo-stream>"}"""
    let ours = """{"identifier":"{\"channel\":\"RoomChannel\"}","message":"<turbo-stream action=\"append\">&</turbo-stream>"}"""
    Assert.Equal<string>(canonical rails, canonical ours)
    assertSameFrame rails ours
    assertSameFrames [ rails; ours ] [ ours; rails ]
    // Non-ASCII written raw or escaped, and as surrogate escapes.
    assertSameFrame """{"message":"é 😀"}""" """{"message":"é 😀"}"""

[<Fact>]
let ``G1 a changed value, swapped keys or another number form compare unequal`` () =
    differ """{"a":1,"b":"x"}""" """{"a":1,"b":"y"}"""
    differ """{"a":1,"b":2}""" """{"b":2,"a":1}"""
    differ """{"message":1}""" """{"message":1.0}"""
    // An identifier is the decoded string, byte for byte: a different key order inside it differs.
    differ """{"identifier":"{\"channel\":\"A\",\"room_id\":1}"}""" """{"identifier":"{\"room_id\":1,\"channel\":\"A\"}"}"""
    // A different count or order of frames.
    Assert.ThrowsAny<exn>(fun () -> assertSameFrames [ "{}"; "{\"a\":1}" ] [ "{\"a\":1}"; "{}" ]) |> ignore
    Assert.ThrowsAny<exn>(fun () -> assertSameFrames [ "{}" ] [ "{}"; "{}" ]) |> ignore

[<Fact>]
let ``G1 harness lines pass through unchanged`` () =
    Assert.Equal("upgrade 101 protocol=actioncable-v1-json", canonical "upgrade 101 protocol=actioncable-v1-json")
    Assert.Equal("close Some((1000, \"\"))", canonical "close Some((1000, \"\"))")
    differ "http 404 text/plain Page not found" "http 404 text/plain Not found"
    assertSameFrame "end" "end"
