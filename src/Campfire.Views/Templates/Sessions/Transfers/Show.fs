// Port of rust/crates/views/templates/sessions/transfers/show.html (reference/app/views/sessions/transfers/show.html.erb)
/// `sessions/transfers/show.html.erb`: an auto-submitting form that PUTs back to the page's own URL
/// (`url_for({})`, i.e. `session_transfer_path(id)`).
module Campfire.Views.Templates.Sessions.Transfers.Show

open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup

/// The page. `action` is the request path, `session_transfer_path(params[:id])`. A block-less `form_with` renders only the opening
/// tag and hidden fields, never `</form>` (`Html.unclosed`): the layout's footer ends up inside it, as in the reference's DOM.
let rendered (action: string) : ViewPage =
    { Regions =
        Regions.content
            [ Html.unclosed
                  "form"
                  (Forms.formWith action "put" [ Falco.Markup.Attr.create "data-controller" "auto-submit" ] []) ]
      Fragments = [] }
