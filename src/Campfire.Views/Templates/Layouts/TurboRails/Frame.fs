// turbo-rails' app/views/layouts/turbo_rails/frame.html.erb on Falco.Markup (plans/design-views.md V8, unit 4.10)
/// turbo-rails' layout for Turbo-Frame requests, used instead of the application layout whenever a request carries a
/// `Turbo-Frame` header. It has no doctype, and it holds only the page's `head` and `content`.
module Campfire.Views.Templates.Layouts.TurboRails.Frame

open Falco.Markup
open Campfire.Views.Markup

/// `head` is the page's `:head` content; `content` is the page itself.
let render (head: XmlNode list) (content: XmlNode list) : XmlNode =
    Elem.html [] [ Elem.head [] (head @ [ Html.sp ]); Elem.body [] ((Html.sp :: content) @ [ Html.sp ]) ]
