// Port of rust/crates/views/templates/messages/_template.html (reference/app/views/messages/_template.html.erb)
/// `messages/_template.html.erb`: the client-side message template the composer clones for a message being sent. Its body is
/// script text with `$placeholders$` the client fills in, so it is written as the ERB's text (whitespace and the
/// `</div class=...>` the ERB has included) around the values.
module Campfire.Views.Templates.Messages._Template

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

/// `user` is `Current.user`.
let render (ctx: ViewContext) (user: UserView) : XmlNode =
    Elem.script
        [ Attr.create "type" "text/template"; Attr.create "data-messages-target" "template" ]
        [ Text.raw "\n  <div class=\"message message--me $messageClasses$\"\n      id=\"message_$clientMessageId$\"\n      data-format-message-target=\"message\"\n      data-user-id=\""
          Html.int user.Id
          Text.raw "\"\n      data-message-timestamp=\"$messageTimestamp$\"\n      data-messages-target=\"message\">\n    <div class=\"message__day-separator\"><time class=\"message__timestamp\" datetime=\"$messageDatetime$\" data-local-time-target=\"date\"></time></div>\n\n    <figure class=\"avatar message__avatar\">\n      <a title=\""
          Html.text user.Title
          Text.raw "\" class=\"btn avatar\" data-turbo-frame=\"_top\" href=\""
          Html.text user.Path
          Text.raw "\"><img aria-hidden=\"true\" src=\""
          Html.text user.AvatarUrl
          Text.raw "\" width=\"48\" height=\"48\" /></a>\n    </figure>\n\n    <div class=\"message__body\">\n      <div class=\"message__body-content\">\n        <div class=\"message__meta\">\n          <h3 class=\"message__heading\">\n            <span class=\"message__author\"><strong>"
          Html.text user.Name
          Text.raw "</strong></span>\n            <span class=\"message__permalink\"><time class=\"message__timestamp\" datetime=\"$messageDatetime$\" data-local-time-target=\"time\"></time></span>\n          </h3>\n          <div class=\"message__actions\">\n            <div class=\"position-relative\">\n              <span class=\"btn message__action-btn message__options-btn\">\n                <img class=\"colorize--black\" aria-hidden=\"true\" src=\""
          Html.text (ctx.Asset "menu-dots-horizontal.svg")
          Text.raw "\" />\n                <span class=\"for-screen-reader\">Message options</span>\n              </span>\n            </div class=\"position-relative\">\n          </div>\n        </div>\n        $body$\n      </div>\n    </div>\n  </div>\n" ]
