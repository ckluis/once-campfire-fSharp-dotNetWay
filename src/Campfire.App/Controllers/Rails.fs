// The controllers Rails and its gems supply: `Rails::HealthController`, turbo-rails' native navigation and Action Mailbox's
// ingress and conductor routes. Web actions (unit 2.9). None of them is an `ApplicationController`, so none runs its
// before-action chain (no authentication, no version headers).
namespace Campfire.App.Controllers

open System
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.App.Web

/// `Rails::HealthController`
module Health =
    let show: Web.Action =
        fun http ->
            taskResult {
                let! format = Formats.respondTo http [ Format.Html; Format.Json ]
                if format = Format.Json then
                    let timestamp = DateTimeOffset.FromUnixTimeSeconds((Data.now http).ToUnixTimeSeconds()).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                    return
                        Pages.json
                            http
                            200
                            (Campfire.Rails.Value.Object [ "status", Campfire.Rails.Value.String "up"; "timestamp", Campfire.Rails.Value.String timestamp ])
                else
                    return Pages.html http """<!DOCTYPE html><html><body style="background-color: green"></body></html>"""
            }

/// `Turbo::Native::NavigationController` (turbo-rails)
module TurboNative =
    let recede: Web.Action = fun http -> Task.FromResult(Ok(Pages.html http "Going back…"))

    let resume: Web.Action = fun http -> Task.FromResult(Ok(Pages.html http "Staying put…"))

    let refresh: Web.Action = fun http -> Task.FromResult(Ok(Pages.html http "Refreshing…"))

/// Action Mailbox's ingress and conductor routes, which Campfire doesn't use.
module Mailbox =
    /// `ActionMailbox::BaseController#ensure_configured`: no ingress is configured.
    let ingressNotConfigured: Web.Action = fun http -> Task.FromResult(Ok(Respond.head http 404))

    /// `Rails::Conductor::BaseController`: CSRF first, then `ensure_development_env`.
    let conductor: Web.Action =
        fun http ->
            taskResult {
                do! Csrf.verifyAuthenticityToken http
                return Respond.head http 403
            }
