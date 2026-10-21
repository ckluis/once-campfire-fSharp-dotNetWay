// The migration seam between the writer templates and the Falco.Markup ones (plans/design-views.md V12). It exists so the app
// is pixel-identical after every unit of Phase 4 while templates move one directory at a time; unit 4.11 deletes it with the writer.
namespace Campfire.Views

open Falco.Markup
open Campfire.Views.Markup

module Legacy =
    /// A writer partial that holds no cached fragment, placed inside a Falco template as trusted text.
    let node (render: Out -> unit) : XmlNode =
        Html.trusted (Html.Trusted.ofLegacy (Render.text render))
