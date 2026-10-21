// Phase 3 (plans/design-cable.md C14): Action Cable frames as UTF-8 bytes, written with System.Text.Json.
//
// `DisconnectReason` is still defined in Protocol.fs (the old transport); 3.4 deletes that file and moves the type here.
namespace Campfire.Cable

open System
open System.Buffers
open System.Globalization
open System.Text.Encodings.Web
open System.Text.Json

module CableJson =
    /// Relaxed escaping: `<`, `>`, `&` and `"`-free text are written raw; both decode to what Rails sends (S6).
    let Options: JsonSerializerOptions =
        let o = JsonSerializerOptions()
        o.Encoder <- JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        o

    let WriterOptions: JsonWriterOptions = JsonWriterOptions(Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

/// One broadcast's message, encoded once and shared by every frame that carries it.
[<Struct>]
type Payload = internal { Json: byte[] }

module Payload =
    /// `JsonSerializer.SerializeToUtf8Bytes(v, CableJson.Options)`; F# anonymous records write their fields alphabetically (S7).
    let ofValue (value: 'T) : Payload =
        { Json = JsonSerializer.SerializeToUtf8Bytes(value, CableJson.Options) }

    /// A JSON string: a Turbo stream tag.
    let ofString (value: string) : Payload =
        { Json = JsonSerializer.SerializeToUtf8Bytes(value, CableJson.Options) }

    /// The encoded bytes (tests, and the old transport until 3.4).
    let toBytes (payload: Payload) : byte[] = payload.Json

module Frames =
    let Protocols: string[] = [| "actioncable-v1-json"; "actioncable-unsupported" |]

    let private utf8 (s: string) : ReadOnlyMemory<byte> = ReadOnlyMemory(Text.Encoding.UTF8.GetBytes s)

    /// `{"type":"welcome"}`, built once.
    let welcome: ReadOnlyMemory<byte> = utf8 """{"type":"welcome"}"""

    /// `{"type":"ping","message":<unix seconds>}`
    let ping (unixSeconds: int64) : ReadOnlyMemory<byte> =
        utf8 ("{\"type\":\"ping\",\"message\":" + unixSeconds.ToString(CultureInfo.InvariantCulture) + "}")

    let private reasonText (reason: DisconnectReason) =
        match reason with
        | Unauthorized -> "unauthorized"
        | InvalidRequest -> "invalid_request"
        | ServerRestart -> "server_restart"
        | Remote -> "remote"

    /// `{"type":"disconnect","reason":<string or null>,"reconnect":<bool>}`
    let disconnect (reason: DisconnectReason option) (reconnect: bool) : ReadOnlyMemory<byte> =
        let reason =
            match reason with
            | Some r -> "\"" + reasonText r + "\""
            | None -> "null"
        utf8 ("{\"type\":\"disconnect\",\"reason\":" + reason + ",\"reconnect\":" + (if reconnect then "true" else "false") + "}")

    /// The identifier as an encoded JSON string (it is a string inside the frame, compared as a string by the JS client).
    let identifierJson (identifier: string) : byte[] =
        JsonSerializer.SerializeToUtf8Bytes(identifier, CableJson.Options)

    let private withIdentifier (prefix: string) (identifierJson: byte[]) (suffix: ReadOnlyMemory<byte>) : ReadOnlyMemory<byte> =
        let head = Text.Encoding.UTF8.GetBytes prefix
        let bytes = Array.zeroCreate<byte> (head.Length + identifierJson.Length + suffix.Length)
        head.CopyTo(bytes, 0)
        identifierJson.CopyTo(bytes, head.Length)
        suffix.CopyTo(Memory(bytes, head.Length + identifierJson.Length, suffix.Length))
        ReadOnlyMemory bytes

    let private confirmSuffix = utf8 ",\"type\":\"confirm_subscription\"}"
    let private rejectSuffix = utf8 ",\"type\":\"reject_subscription\"}"

    /// `{"identifier":..,"type":"confirm_subscription"}`
    let confirmation (identifierJson: byte[]) : ReadOnlyMemory<byte> =
        withIdentifier "{\"identifier\":" identifierJson confirmSuffix

    /// `{"identifier":..,"type":"reject_subscription"}`
    let rejection (identifierJson: byte[]) : ReadOnlyMemory<byte> =
        withIdentifier "{\"identifier\":" identifierJson rejectSuffix

    /// `{"identifier":..,"message":..}`; the payload is passed through as encoded.
    let message (identifierJson: byte[]) (payload: Payload) : ReadOnlyMemory<byte> =
        let head = "{\"identifier\":"B
        let mid = ",\"message\":"B
        let json = payload.Json
        let bytes = Array.zeroCreate<byte> (head.Length + identifierJson.Length + mid.Length + json.Length + 1)
        let mutable at = 0
        let put (src: byte[]) =
            src.CopyTo(bytes, at)
            at <- at + src.Length
        put head
        put identifierJson
        put mid
        put json
        bytes[at] <- byte '}'
        ReadOnlyMemory bytes
