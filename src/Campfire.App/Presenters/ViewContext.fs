// Port of rust/crates/campfire/src/controllers/presenters/view_context.rs
//
// Builds the `Campfire.Views.ViewContext` every page renders with: what the application layout and the
// helpers read from `Current`, `request`, `flash`, the session and the config
// (`reference/app/views/layouts/application.html.erb` and `app/helpers`).
//
// Gather the per-request data with `Layout.load` (it reads the database), then render inside
// `Layout.render`, which lends the templates a `ViewContext` for this request:
//
//     let! layout = Layout.load c
//     let html = Layout.render layout c (fun ctx -> Render.page 0 (fun w -> Templates.Sessions.New.render w ctx ...))
//     return layout.Page(c, Status.Ok, html)
namespace Campfire.App.Presenters

open System
open System.Threading.Tasks
open Campfire.App
open Campfire.Assets
open Campfire.Db
open Campfire.Views

/// Everything the layout needs, loaded before rendering.
type Layout =
    { CurrentUser: Campfire.Views.CurrentUser option
      Account: AccountSummary
      CustomStyles: string option
      Platform: Platform
      LastRoomVisitedId: int64 option
      VapidPublicKey: string option
      AppVersion: string }

module ViewContextBuilder =
    /// The layout's `stylesheet_link_tag :all, "data-turbo-track": "reload"`: the assets are fixed at
    /// build time, so it renders once per process.
    let private tags = lazy (Assets.stylesheetLinkTagAll [ "data-turbo-track", "reload" ])

    let stylesheetTags () : Campfire.Assets.Tags.StylesheetTags = tags.Value

    /// `Current.user` as the layout's meta tags and helpers see it.
    let currentUser (secrets: Campfire.RailsCompat.Secrets) (user: User) : Campfire.Views.CurrentUser =
        { Id = user.Id
          Name = user.Name
          Administrator = User.canAdminister user None false
          Bot = User.isBot user
          AvatarUrl = Common.avatarPath secrets user }

    /// `Current.account` for the layout: its name, `fresh_account_logo_path` and whether a logo is
    /// attached.
    let accountSummary (account: Account option) (hasLogo: bool) : AccountSummary =
        { Name = account |> Option.map (fun account -> account.Name) |> Option.defaultValue ""
          LogoUrl = Accounts.freshAccountLogoPath account None
          HasLogo = hasLogo }
