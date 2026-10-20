// Port of rust/crates/campfire/src/controllers/presenters/page.rs
//
// Rendering helpers for the controllers: pages in the application layout (or turbo-rails' frame layout
// for Turbo-Frame requests), and partials rendered outside a request for broadcasts
// (`ApplicationController.render`).
namespace Campfire.App.Presenters

open System.Threading.Tasks
open Campfire.App
open Campfire.App.Channels
open Campfire.Assets
open Campfire.Db
open Campfire.Views

module Page =
    /// `renderDetached` during a request: URLs get the request's host through `default_url_options`
    /// (`SetCurrentRequest`), see `Web.Pages.rendererBaseUrl`.
    let renderDetachedAt (app: AppState) (account: Account option) (baseUrl: string) (render: ViewContext -> 'T) : 'T =
        let stylesheets = ViewContextBuilder.stylesheetTags ()
        let ctx: ViewContext =
            { CurrentUser = None
              Account = ViewContextBuilder.accountSummary account false
              FlashNotice = None
              FlashAlert = None
              Platform = Platform.none
              VapidPublicKey = app.VapidPublicKey
              AssetPath = Assets.assetPath
              ImportmapTags = Assets.javascriptImportmapTags ()
              StylesheetTags = stylesheets.Html
              CustomStyles = None
              CableUrl = "/cable"
              BaseUrl = baseUrl
              RequestUrl = baseUrl + "/"
              Referrer = None
              LastRoomVisitedId = None
              AppVersion = app.Config.AppVersion }
        // Renders outside a request (broadcasts from jobs) share the fragment cache too.
        FragmentCache.withCache app.FragmentCache (fun () -> render ctx)

    /// Renders with the `ViewContext` `ApplicationController.render` has: no request, no
    /// `Current.user`, no CSRF tokens, and the renderer's default host (`http://example.org`).
    let renderDetached (app: AppState) (account: Account option) (render: ViewContext -> 'T) : 'T =
        renderDetachedAt app account "http://example.org" render

/// The broadcast partials, rendered up front by the controller (which has the view models) and handed
/// to `Broadcasts`.
type Rendered =
    { Message: Campfire.Views.Fragment option
      MessagePresentation: string option
      Boost: Campfire.Views.Fragment option
      SharedRoom: string option
      /// `users/sidebars/rooms/_direct`, per membership id.
      DirectRooms: (int64 * Campfire.Views.Fragment) list }

module Rendered =
    let empty: Rendered =
        { Message = None
          MessagePresentation = None
          Boost = None
          SharedRoom = None
          DirectRooms = [] }

    let partials (rendered: Rendered) : IPartials =
        { new IPartials with
            member _.Message _ = rendered.Message |> Option.map (fun fragment -> fragment.Memory) |> Option.defaultValue System.ReadOnlyMemory<byte>.Empty
            member _.MessagePresentation _ = defaultArg rendered.MessagePresentation ""
            member _.Boost _ = rendered.Boost |> Option.map (fun fragment -> fragment.Memory) |> Option.defaultValue System.ReadOnlyMemory<byte>.Empty
            member _.SharedRoom _ = defaultArg rendered.SharedRoom ""

            member _.DirectRoom(membership: Membership) =
                rendered.DirectRooms
                |> List.tryFind (fun (id, _) -> id = membership.Id)
                |> Option.map (fun (_, html) -> string html)
                |> Option.defaultValue "" }
