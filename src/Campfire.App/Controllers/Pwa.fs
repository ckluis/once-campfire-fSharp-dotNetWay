// Port of rust/crates/campfire/src/controllers/pwa.rs
//
// `PwaController` (reference/app/controllers/pwa_controller.rb): the web app manifest and the service worker, at stable
// URLs. Web actions (unit 2.9).
namespace Campfire.App.Controllers

open System
open FsToolkit.ErrorHandling
open Campfire.Views
open Campfire.Views.Templates.Pwa
open Campfire.Assets
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.App.Web

module Pwa =
    /// `allow_unauthenticated_access`, `skip_forgery_protection`
    let private before: Before = Before.skipForgeryProtection (Before.allowUnauthenticatedAccess Before.Default)

    /// `pwa/service_worker.js`
    let serviceWorker: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                do! Formats.respondTo http [ Format.Js ] |> Result.map ignore
                return Pages.renderText http 200 "text/javascript; charset=utf-8" ServiceWorker.js
            }

    /// `pwa/manifest.json.erb`
    let manifest: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain before http
                do! Formats.respondTo http [ Format.Json ] |> Result.map ignore
                let! account = Data.read http (fun db -> Account.first db)
                let logoPathSmall = Accounts.freshAccountLogoPath account (Some "small")
                let logoPath = Accounts.freshAccountLogoPath account None
                let baseUrl = Respond.urlFor http ""
                let body =
                    Render.plain (fun w ->
                        Manifest.render w (account |> Option.map (fun account -> account.Name)) logoPathSmall logoPath baseUrl Assets.assetPath)
                return Pages.renderAs http 200 "application/json; charset=utf-8" (ReadOnlyMemory<byte> body)
            }
