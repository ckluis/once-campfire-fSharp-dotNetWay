// Rails' link helpers as Falco.Markup functions (plans/design-views.md V13). First part: what `messages/**` needs.
namespace Campfire.Views.Markup

open Falco.Markup
open Campfire.Rails.Ruby

module Links =
    /// `link_to(url, options) do ... end`: the options in order, then `href`.
    let linkTo (url: string) (options: XmlAttribute list) (content: XmlNode list) : XmlNode =
        Elem.a (options @ [ Html.attr "href" url ]) content

    /// `mail_to(email)`: the address percent-escaped (`ERB::Util.url_encode`, keeping "@") in the `href`, the plain address as the text.
    let mailTo (email: string) : XmlNode =
        let encoded = Ruby.urlEncode(email).Replace("%40", "@")
        Elem.a [ Html.attr "href" ("mailto:" + encoded) ] [ Html.text email ]
