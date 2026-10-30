/// F1-F4: Phase 3's Frames module (plans/design-cable.md section 5).
module Campfire.Cable.Tests.FramesTests

open System
open System.IO
open System.Text
open System.Text.Json
open Xunit
open Campfire.Cable
open Campfire.Tests.JsonFrames

let private text (m: ReadOnlyMemory<byte>) = Encoding.UTF8.GetString m.Span

let private goldenPath () =
    let own = Campfire.Tests.Repo.path "tests/Campfire.Cable.Tests/golden/reference.json"
    if File.Exists own then own else Campfire.Tests.Repo.path "rust/crates/cable/tests/golden/reference.json"

[<Fact>]
let ``F1 frames equal the recorded ones under canonical`` () =
    assertSameFrame """{"type":"welcome"}""" (text Frames.welcome)
    assertSameFrame """{"type":"ping","message":1700000000}""" (text (Frames.ping 1700000000L))
    assertSameFrame """{"type":"disconnect","reason":"unauthorized","reconnect":false}""" (text (Frames.disconnect (Some Unauthorized) false))
    assertSameFrame """{"type":"disconnect","reason":"invalid_request","reconnect":false}""" (text (Frames.disconnect (Some InvalidRequest) false))
    assertSameFrame """{"type":"disconnect","reason":"server_restart","reconnect":true}""" (text (Frames.disconnect (Some ServerRestart) true))
    assertSameFrame """{"type":"disconnect","reason":"remote","reconnect":true}""" (text (Frames.disconnect (Some Remote) true))
    assertSameFrame """{"type":"disconnect","reason":null,"reconnect":false}""" (text (Frames.disconnect None false))
    let id = Frames.identifierJson """{"channel":"RoomChannel","room_id":"5"}"""
    assertSameFrame """{"identifier":"{\"channel\":\"RoomChannel\",\"room_id\":\"5\"}","type":"confirm_subscription"}""" (text (Frames.confirmation id))
    assertSameFrame """{"identifier":"{\"channel\":\"RoomChannel\",\"room_id\":\"5\"}","type":"reject_subscription"}""" (text (Frames.rejection id))
    assertSameFrame
        """{"identifier":"{\"channel\":\"RoomChannel\",\"room_id\":\"5\"}","message":{"action":"start"}}"""
        (text (Frames.message id (Payload.ofValue {| action = "start" |})))
    Assert.Equal<string[]>([| "actioncable-v1-json"; "actioncable-unsupported" |], Frames.Protocols)

[<Fact>]
let ``F1 every recorded welcome, disconnect, confirmation and rejection is rebuilt exactly`` () =
    use document = JsonDocument.Parse(File.ReadAllText(goldenPath ()))
    let mutable checkedFrames = 0
    for session in document.RootElement.GetProperty("sessions").EnumerateObject() do
        for exchange in session.Value.EnumerateArray() do
            for f in exchange.GetProperty("frames").EnumerateArray() do
                let frame = f.GetString() |> nonNull
                // Harness lines (`upgrade 101 ...`, `close ...`, `end`) and the normalized ping are not frames to rebuild.
                use parsed = JsonDocument.Parse(if frame.StartsWith '{' && not (frame.Contains "<unix>") then frame else "{}")
                let root = parsed.RootElement
                let kind = match root.TryGetProperty "type" with | true, t -> t.GetString() | _ -> null
                let rebuilt =
                    match kind with
                    | "welcome" -> Some(text Frames.welcome)
                    | "confirm_subscription" -> Some(text (Frames.confirmation (Frames.identifierJson (root.GetProperty("identifier").GetString() |> nonNull))))
                    | "reject_subscription" -> Some(text (Frames.rejection (Frames.identifierJson (root.GetProperty("identifier").GetString() |> nonNull))))
                    | "disconnect" ->
                        let reason =
                            match root.GetProperty("reason").GetString() with
                            | "unauthorized" -> Some Unauthorized
                            | "invalid_request" -> Some InvalidRequest
                            | "server_restart" -> Some ServerRestart
                            | "remote" -> Some Remote
                            | _ -> None
                        Some(text (Frames.disconnect reason (root.GetProperty("reconnect").GetBoolean())))
                    | _ -> None
                match rebuilt with
                | Some r ->
                    assertSameFrame frame r
                    checkedFrames <- checkedFrames + 1
                | None -> ()
    Assert.True(checkedFrames > 0, "the recording holds no protocol frames to compare")

[<Fact>]
let ``F2 an identifier with quotes, angle brackets, non-ASCII and an emoji decodes back exactly`` () =
    let identifier = "{\"channel\":\"A\",\"q\":\"say \\\"hi\\\" <b>&</b> é 日本 \U0001F600\"}"
    for frame in
        [ Frames.confirmation (Frames.identifierJson identifier)
          Frames.rejection (Frames.identifierJson identifier)
          Frames.message (Frames.identifierJson identifier) (Payload.ofValue {| a = 1 |}) ] do
        use d = JsonDocument.Parse(frame.ToArray())
        Assert.Equal(identifier, d.RootElement.GetProperty("identifier").GetString() |> nonNull)

[<Fact>]
let ``F3 an anonymous record writes action before user`` () =
    let json = text (Frames.message (Frames.identifierJson "x") (Payload.ofValue {| action = "start"; user = {| id = 1L; name = "x" |} |}))
    Assert.Equal("""{"identifier":"x","message":{"action":"start","user":{"id":1,"name":"x"}}}""", json)

[<Fact>]
let ``F4 a Turbo tag with quotes, angle brackets, ampersand and U+2028 decodes to the tag`` () =
    let tag = "<turbo-stream action=\"append\" target=\"a&b\"><template>x y</template></turbo-stream>"
    let frame = Frames.message (Frames.identifierJson "x") (Payload.ofString tag)
    use d = JsonDocument.Parse(frame.ToArray())
    Assert.Equal(tag, d.RootElement.GetProperty("message").GetString() |> nonNull)
