// The migration seam between the writer templates and the Falco.Markup ones (plans/design-views.md V12). It exists so the app
// is pixel-identical after every unit of Phase 4 while templates move one directory at a time; unit 4.11 deletes it with the writer.
// It is compiled after `Layouts.fs` because `inLayout` calls the writer's layouts.
namespace Campfire.Views

open System
open System.Collections.Generic
open Falco.Markup
open Campfire.Views.Markup
open Campfire.Views.Templates.Layouts

module Legacy =
    /// A writer partial that holds no cached fragment, placed inside a Falco template as trusted text.
    let node (render: Out -> unit) : XmlNode =
        Html.trusted (Html.Trusted.ofLegacy (Render.text render))

    /// A Falco page or partial placed into a writer template; its fragments stay fragments (recorded by `Out.Fragment`).
    let write (w: Out) (page: HtmlOutput) : unit = w.Page page

    /// A Falco page's regions in the writer's layouts: the application layout, or turbo-rails' frame layout (its head and
    /// content only) for a Turbo-Frame request. Each region is rendered by Falco and written into the layout with `write`.
    let inLayout (ctx: ViewContext) (frame: bool) (page: Layouts.ViewPage) : HtmlOutput =
        let lookup = HtmlOutput.lookupOf page.Fragments
        let placed = HashSet<int64>()
        let region (nodes: XmlNode list) = HtmlOutput.split (String.Concat(nodes |> List.map renderNode)) lookup placed
        let regions = page.Regions
        let head = region regions.Head
        let nav = region regions.Nav
        let content = region regions.Content
        let footer = region regions.Footer
        let sidebar = region regions.Sidebar
        HtmlOutput.checkAllPlaced lookup placed
        let size = head.TextLength + nav.TextLength + content.TextLength + footer.TextLength + sidebar.TextLength + 4096
        Render.page size (fun w ->
            if frame then
                TurboRails.Frame.render w (fun w -> write w head) (fun w -> write w content)
            else
                Application.render
                    w
                    ctx
                    regions.Title
                    regions.BodyClass
                    (fun w -> write w head)
                    (fun w -> write w nav)
                    (fun w -> write w content)
                    (fun w -> write w footer)
                    (fun w -> write w sidebar))
