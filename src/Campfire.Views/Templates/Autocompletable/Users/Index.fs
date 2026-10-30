// Port of rust/crates/views/templates/autocompletable/users/index.html (reference/app/views/autocompletable/users/index.html.erb)
/// `autocompletable/users/index.html.erb`: `<lexxy-prompt-item>`s for the mentions prompt, rendered without a layout.
module Campfire.Views.Templates.Autocompletable.Users.Index

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

/// The items, a space between each two (the ERB's collection leaves whitespace; custom elements flow inline).
let render (ctx: ViewContext) (users: MentionUser list) : XmlNode list =
    [ for position, user in List.indexed users do
          if position > 0 then Html.sp
          _PromptItem.render ctx user ]
