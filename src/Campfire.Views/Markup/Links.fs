// Rails' link helpers as Falco.Markup functions (plans/design-views.md V13). First part: what `messages/**` needs.
namespace Campfire.Views.Markup

open Falco.Markup

module Links =
    /// `link_to(url, options) do ... end`: the options in order, then `href`.
    let linkTo (url: string) (options: XmlAttribute list) (content: XmlNode list) : XmlNode =
        Elem.a (options @ [ Html.attr "href" url ]) content
