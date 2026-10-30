// Port of rust/crates/campfire/src/controllers/qr_code.rs
//
// `QrCodeController` (reference/app/controllers/qr_code_controller.rb): a QR code SVG for a
// Base64url-encoded URL. A Web action (unit 2.11). (`QrCode/Rqrcode.fs` is the port of the gem.)
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.App.Web

module QrCode =
    /// `allow_unauthenticated_access`
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                // `Base64.urlsafe_decode64(params[:id])` raises ArgumentError (a 500) on malformed input.
                let id = match Params.value http "id" with null -> "" | id -> id
                let! url =
                    match Campfire.Rails.RailsEncoding.urlsafeDecode id with
                    | Some url -> Ok url
                    | None -> Result.Error(Internal(exn "invalid base64"))
                // Too much to encode is the client's doing (rqrcode raises, a 500 in Rails).
                let! qrCode =
                    match Rqrcode.svgBytes url with
                    | Some svg -> Ok svg
                    | None -> Result.Error(Status 422)

                // `expires_in 1.year, public: true`
                Respond.expiresIn http OutputCaching.QrCodeMaxAge { ExpiresIn.Default with Public = true }
                return Pages.renderText http 200 "image/svg+xml; charset=utf-8" qrCode
            }
