// Port of rust/crates/campfire/src/controllers/accounts/logos.rs
//
// `Accounts::LogosController` (reference/app/controllers/accounts/logos_controller.rb): the account logo (or the stock app
// icon) as a PNG, public so it can be the PWA icon. Web actions (unit 2.10).
namespace Campfire.App.Controllers

open System
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Campfire.Views
open Campfire.Kit
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Storage
open Campfire.App.Web

module Logos =
    /// `expires_in 5.minutes, public: true, stale_while_revalidate: 1.week`
    [<Literal>]
    let private MaxAge = 300UL

    [<Literal>]
    let private StaleWhileRevalidate = 604_800UL

    /// `allow_unauthenticated_access only: :show`
    let show: Web.Action =
        fun http ->
            taskResult {
                Native.useLiveResponse http // `include ActiveStorage::Streaming`
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                let! (account: Account option) = Data.read http (fun db -> Account.first db)

                // `stale?(etag: Current.account)`; there's no accounts/logos/show template to digest.
                let freshness =
                    { Freshness.Default with
                        Etag =
                            match account with
                            | Some account -> FragmentCache.cacheKeyWithVersion "accounts" account.Id (DateTimeOffset(account.UpdatedAt))
                            | None -> null }
                match Respond.freshWhen http freshness with
                | ValueSome notModified -> return notModified
                | ValueNone ->
                    Respond.expiresIn http MaxAge { ExpiresIn.Default with Public = true; StaleWhileRevalidate = ValueSome StaleWhileRevalidate }

                    let app = Data.app http
                    let small = Params.value http "size" = "small"
                    let! (variant: Campfire.Storage.Blob option) =
                        match account with
                        // `logo.variant(size).processed if logo.variable?`: :small is 192, :large 512, both PNG.
                        | Some account ->
                            let size = if small then 192L else 512L
                            AttachmentWrites.processedVariant app (Record.account account.Id) "logo" (Variation.resizeToLimit size size (Some "png"))
                            |> Task.map (Result.mapError Legacy)
                        | None -> Task.FromResult(Ok None)
                    match variant with
                    | Some variant ->
                        let path = DiskService.pathFor app.Storage.Service variant.Key
                        return! Respond.sendFile path (SendOptions.Inline "image/png")
                    // send_stock_icon
                    | None ->
                        let filename = if small then "app-icon-192.png" else "app-icon.png"
                        let! path = Avatars.assetFile $"logos/{filename}"
                        return! Respond.sendFile path (SendOptions.Inline "image/png")
            }

    /// `Current.account.logo.destroy`
    let destroy: Web.Action =
        fun http ->
            taskResult {
                Native.useLiveResponse http // `include ActiveStorage::Streaming`
                do! Before.chain Before.Default http
                do! Before.ensureCanAdminister http
                let! (account: Account) = AccountsController.currentAccount http
                let! (_: bool) = Data.write http (fun tx -> AttachmentWrites.destroy tx (Record.account account.Id) "logo")
                return! Respond.redirectTo http (Respond.urlFor http (Routes.editAccount ()))
            }
