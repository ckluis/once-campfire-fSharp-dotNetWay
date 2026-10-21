/// Layouts for `reference/app/views/layouts` and turbo-rails' frame layout, on Falco.Markup (plans/design-views.md V8).
///
/// A page module (a module under `Templates/`) returns a `ViewPage`: what the ERB sets as `@page_title` and `@body_class`, the
/// regions it fills (`content_for`'s `head`, `nav`, `footer`, `sidebar`, and the page itself as `content`) and the cached
/// fragments its nodes place. `Layouts.page` puts it in the application layout, or in turbo-rails' frame layout (its head and
/// content only) for a Turbo-Frame request.
module Campfire.Views.Layouts

open Falco.Markup
open Campfire.Views
open Campfire.Views.Templates.Layouts

/// What `content_for` and `yield` give the layout (plans/design-views.md V8): a Falco page module returns its regions as nodes.
type Regions =
    { Title: string option
      BodyClass: string option
      Head: XmlNode list
      Nav: XmlNode list
      Content: XmlNode list
      Footer: XmlNode list
      Sidebar: XmlNode list }

module Regions =
    /// A content-only template.
    let content (nodes: XmlNode list) : Regions =
        { Title = None
          BodyClass = None
          Head = []
          Nav = []
          Content = nodes
          Footer = []
          Sidebar = [] }

/// A Falco page: its regions, and the cached fragments its nodes place with `Html.fragment`.
type ViewPage =
    { Regions: Regions
      Fragments: CachedFragment list }

module ViewPage =
    /// A content-only template and the fragments it places.
    let content (nodes: XmlNode list) (fragments: CachedFragment seq) : ViewPage =
        { Regions = Regions.content nodes
          Fragments = List.ofSeq fragments }

/// The application layout around a page's regions, as a document (render it with `HtmlOutput.ofDocument`).
let application (ctx: ViewContext) (regions: Regions) : XmlNode =
    Application.render ctx regions.Title regions.BodyClass regions.Head regions.Nav regions.Content regions.Footer regions.Sidebar

/// turbo-rails' frame layout around a page's `head` and `content` (render it with `HtmlOutput.ofNode`: it has no doctype).
let frame (regions: Regions) : XmlNode = TurboRails.Frame.render regions.Head regions.Content

/// A page in the application layout, or for a Turbo-Frame request (`framed`) in turbo-rails' frame layout, written in parts:
/// its fragments stay what the cache holds.
let page (ctx: ViewContext) (framed: bool) (page: ViewPage) : HtmlOutput =
    if framed then
        HtmlOutput.ofNode (frame page.Regions) page.Fragments
    else
        HtmlOutput.ofDocument (application ctx page.Regions) page.Fragments
