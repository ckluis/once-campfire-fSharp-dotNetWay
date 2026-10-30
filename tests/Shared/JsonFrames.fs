/// Decoded comparison of Action Cable frames (plans/design-cable.md C15).
///
/// Rails escapes `<`, `>` and `&` in frames as `<` and friends; System.Text.Json with relaxed escaping writes them
/// raw. Both decode to the same JSON, so frames are compared after one canonical round trip: key order and string
/// contents are kept (an `identifier` is compared as the decoded string, since the JS client finds subscriptions with
/// `identifier ===`), number text is kept (`1` and `1.0` differ), and escaping is dropped.
module Campfire.Tests.JsonFrames

open System.Text.Encodings.Web
open System.Text.Json
open System.Text.Json.Nodes
open Xunit

let private options = JsonSerializerOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

/// A JSON frame in canonical form. Anything that is not a JSON object or array (`upgrade 101 ...`, `close Some(...)`,
/// `http 404 ...`, `end`) is a harness line, not a frame, and is returned unchanged.
let canonical (frame: string) : string =
    let trimmed = frame.TrimStart()
    if trimmed.StartsWith '{' || trimmed.StartsWith '[' then
        try
            match JsonNode.Parse frame with
            | null -> frame
            | node -> node.ToJsonString options
        with :? JsonException ->
            frame
    else
        frame

/// Asserts two frame lists are equal frame by frame (same count, same order) under `canonical`.
let assertSameFrames (expected: string list) (actual: string list) : unit =
    let show (frames: string list) = frames |> List.map canonical
    Assert.True(
        (show expected = show actual),
        $"frames differ.\nexpected: {show expected}\nactual:   {show actual}"
    )

/// One frame against one frame.
let assertSameFrame (expected: string) (actual: string) : unit = assertSameFrames [ expected ] [ actual ]

/// Same frames regardless of order (a step whose frames race in Rails): both sides canonicalized, then sorted.
let assertSameFramesUnordered (expected: string list) (actual: string list) : unit =
    assertSameFrames (expected |> List.map canonical |> List.sort) (actual |> List.map canonical |> List.sort)
