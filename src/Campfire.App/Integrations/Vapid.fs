// Port of rust/crates/campfire/src/integrations/web_push/vapid.rs
//
// The boot-time check of the VAPID key pair (plans/design-jobs.md J7). Lib.Net.Http.WebPush signs the JWT (`aud`, `exp`, `sub`) and
// writes `Authorization: vapid t=...,k=...`; this only turns Web Push off, with a log line, when the pair is unusable.
namespace Campfire.App.Integrations

open System
open System.Numerics
open System.Security.Cryptography
open System.Text
open Campfire.App
open Campfire.Rails

type EncryptionError =
    /// `ArgumentError`: a blank argument, bad Base64, or a payload over 4096 bytes.
    | Argument of string
    /// `OpenSSL::PKey::EC::Point::Error` and friends: the subscription's key isn't a P-256 point.
    | InvalidKey of string

module EncryptionError =
    let message (error: EncryptionError) : string =
        match error with
        | Argument message
        | InvalidKey message -> message

module WebPushEncoding =
    /// `WebPush.decode64` (`Base64.urlsafe_decode64`): either alphabet, padding optional.
    let decode64 (value: string) : Result<byte[], EncryptionError> =
        match RailsEncoding.urlsafeDecode value with
        | Some bytes -> Ok bytes
        | None -> Error(Argument "invalid base64")

    /// `trim_encode64`: urlsafe Base64 without padding.
    let encode64Nopad (bytes: byte[]) : string = RailsEncoding.urlsafeEncodeUnpadded bytes

module P256 =
    let private p = BigInteger.Parse("115792089210356248762697446949407573530086143415290314195533631308867097853951")
    let private b = BigInteger.Parse("41058363725152142129326129780047268409114441015993725554835256314039467401291")

    let private modPow (value: BigInteger) (exponent: BigInteger) = BigInteger.ModPow(value, exponent, p)

    let private fixed32 (value: BigInteger) : byte[] =
        let bytes = value.ToByteArray(true, true)
        Array.append (Array.zeroCreate (32 - bytes.Length)) bytes

    /// `PublicKey::from_sec1_bytes`: an uncompressed (65 bytes) or compressed (33 bytes) point on the curve.
    let parsePoint (bytes: byte[]) : Result<ECPoint, string> =
        let fail = Error "invalid encoding"
        if bytes.Length = 65 && bytes[0] = 4uy then
            Ok(ECPoint(X = bytes[1..32], Y = bytes[33..64]))
        elif bytes.Length = 33 && (bytes[0] = 2uy || bytes[0] = 3uy) then
            let x = BigInteger(ReadOnlySpan bytes[1..], true, true)
            if x >= p then
                fail
            else
                let rhs = (modPow x (BigInteger 3) - BigInteger 3 * x + b) % p
                let rhs = if rhs.Sign < 0 then rhs + p else rhs
                let y = modPow rhs ((p + BigInteger.One) / BigInteger 4)
                if modPow y (BigInteger 2) <> rhs then
                    fail
                else
                    let wantOdd = bytes[0] = 3uy
                    let y = if y.IsEven = wantOdd then p - y else y
                    Ok(ECPoint(X = bytes[1..], Y = fixed32 y))
        else
            fail

    /// A key pair as an ECDH key, from the private scalar and the public point it belongs to.
    let keyOf (scalar: byte[]) (q: ECPoint) : ECDiffieHellman =
        ECDiffieHellman.Create(ECParameters(Curve = ECCurve.NamedCurves.nistP256, D = scalar, Q = q))

    /// The key's public point as an uncompressed SEC1 point.
    let uncompressed (key: ECDiffieHellman) : byte[] =
        let q = key.ExportParameters(false).Q
        Array.concat [ [| 4uy |]; nonNull q.X; nonNull q.Y ]

type VapidError =
    | Missing
    | InvalidPublicKey
    | InvalidPrivateKey
    | Mismatched

module VapidError =
    let message (error: VapidError) : string =
        match error with
        | Missing -> "VAPID_PUBLIC_KEY and VAPID_PRIVATE_KEY aren't set"
        | InvalidPublicKey -> "VAPID_PUBLIC_KEY isn't a Base64 P-256 public key"
        | InvalidPrivateKey -> "VAPID_PRIVATE_KEY isn't a Base64 P-256 private key"
        | Mismatched -> "VAPID_PUBLIC_KEY isn't the public key of VAPID_PRIVATE_KEY"

/// `WebPush::Notification#vapid_identification`: the subject and `Rails.configuration.x.vapid`
/// (`VAPID_PUBLIC_KEY`/`VAPID_PRIVATE_KEY`), checked once at boot so that a bad key turns Web Push off rather than
/// failing (and being blamed on) each subscription. The keys are kept as unpadded URL-safe Base64, the form the library reads.
[<Sealed>]
type VapidConfig private (subject: string, publicKey: string, privateKey: string) =
    /// P-256's group order: a private scalar is valid from 1 up to one below it.
    static let order =
        [| 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0x00uy; 0x00uy; 0x00uy; 0x00uy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy; 0xFFuy
           0xBCuy; 0xE6uy; 0xFAuy; 0xADuy; 0xA7uy; 0x17uy; 0x9Euy; 0x84uy; 0xF3uy; 0xB9uy; 0xCAuy; 0xC2uy; 0xFCuy; 0x63uy; 0x25uy; 0x51uy |]

    static member private ScalarInRange(scalar: byte[]) : bool =
        scalar |> Array.exists (fun b -> b <> 0uy) && ReadOnlySpan<byte>(scalar).SequenceCompareTo(ReadOnlySpan<byte> order) < 0

    /// `VapidKey.from_keys(public_key, private_key)`: the public key is sent as given, so it must be the private key's (a signature the
    /// public key alone verifies says so).
    static member Create(subject: string, publicKey: string, privateKey: string) : Result<VapidConfig, VapidError> =
        match WebPushEncoding.decode64 publicKey with
        | Error _ -> Error InvalidPublicKey
        | Ok publicBytes ->
            match P256.parsePoint publicBytes with
            | Error _ -> Error InvalidPublicKey
            | Ok point ->
                match WebPushEncoding.decode64 privateKey with
                | Error _ -> Error InvalidPrivateKey
                | Ok privateBytes when privateBytes.Length > 32 -> Error InvalidPrivateKey
                | Ok privateBytes ->
                    let scalar = Array.append (Array.zeroCreate (32 - privateBytes.Length)) privateBytes
                    if not (VapidConfig.ScalarInRange scalar) then
                        Error InvalidPrivateKey
                    else
                        try
                            use signing = ECDsa.Create(ECParameters(Curve = ECCurve.NamedCurves.nistP256, D = scalar, Q = point))
                            use verifying = ECDsa.Create(ECParameters(Curve = ECCurve.NamedCurves.nistP256, Q = point))
                            let probe = Encoding.ASCII.GetBytes "vapid"
                            let signature = signing.SignData(probe, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                            if verifying.VerifyData(probe, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation) then
                                Ok(VapidConfig(subject, WebPushEncoding.encode64Nopad publicBytes, WebPushEncoding.encode64Nopad scalar))
                            else
                                Error Mismatched
                        with :? CryptographicException ->
                            Error Mismatched

    static member FromConfig(config: CampfireOptions) : Result<VapidConfig, VapidError> =
        match config.VapidPublicKey, config.VapidPrivateKey with
        | Some publicKey, Some privateKey -> VapidConfig.Create(config.VapidSubject, publicKey, privateKey)
        | _ -> Error Missing

    member _.Subject: string = subject
    member _.PublicKey: string = publicKey
    member _.PrivateKey: string = privateKey
