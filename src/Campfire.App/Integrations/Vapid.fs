// Port of rust/crates/campfire/src/integrations/web_push/vapid.rs
//
// The boot-time check of the VAPID key pair (plans/design-jobs.md J7). Lib.Net.Http.WebPush signs the JWT (`aud`, `exp`, `sub`) and
// writes `Authorization: vapid t=...,k=...`; this only turns Web Push off, with a log line, when the pair is unusable.
namespace Campfire.App.Integrations

open System
open System.Security.Cryptography
open System.Text
open Campfire.App
open Campfire.RailsCompat

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
