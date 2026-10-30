// Port of rust/crates/views/templates/accounts/users/index.turbo_stream.html (reference/app/views/accounts/users/index.turbo_stream.erb)
/// `accounts/users/index.turbo_stream.erb`: the next page of users, and the container for the page after it. Streams are custom
/// elements, so the ERB's blank line between the two renders as a space (V4).
module Campfire.Views.Templates.Accounts.Users.IndexTurboStream

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (ctx: ViewContext) (users: UserSummary list) (nextPage: string option) : XmlNode list =
    [ Turbo.stream "replace" "next_page_container" [ for user in users -> _User.render ctx user ]
      match nextPage with
      | Some page ->
          Html.sp
          Turbo.stream "append" "account_users" [ _NextPageContainer.render page ]
      | None -> () ]

/// The response.
let output (ctx: ViewContext) (users: UserSummary list) (nextPage: string option) : HtmlOutput =
    HtmlOutput.ofNodes (render ctx users nextPage) []
