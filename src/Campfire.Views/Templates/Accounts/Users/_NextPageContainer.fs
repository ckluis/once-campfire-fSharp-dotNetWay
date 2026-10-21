// Port of rust/crates/views/templates/accounts/users/_next_page_container.html (reference/app/views/accounts/users/_next_page_container.html.erb)
/// `accounts/users/_next_page_container.html.erb`: the lazy frame that loads the next page of users.
module Campfire.Views.Templates.Accounts.Users._NextPageContainer

open Falco.Markup
open Campfire.Views.Helpers
open Campfire.Views.Markup

let render (page: string) : XmlNode =
    Turbo.frameTag
        "next_page_container"
        None
        None
        [ Attr.create "loading" "lazy"
          Html.attr "src" ("/account/users.turbo_stream?page=" + Url.cgiEscape page)
          Attr.class' "flex center" ]
        [ Elem.div [ Attr.class' "spinner center" ] [] ]
