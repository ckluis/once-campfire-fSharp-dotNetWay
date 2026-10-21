// Port of rust/crates/views/templates/users/_mention.html (reference/app/views/users/_mention.html.erb)
/// `users/_mention.html.erb`: the mention attachment's HTML. A span rather than a div: mentions render
/// inline inside the <p> paragraphs the editor produces, and block elements would make the browser
/// split the paragraph when parsing.
module Campfire.Views.Templates.Users._Mention

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (ctx: ViewContext) (user: MentionUser) : XmlNode =
    Elem.span
        [ Attr.class' "mention"; Html.attr "sgid" user.AttachableSgid ]
        [ Markup.Users.avatarTag ctx user.User.Avatar []; Html.sp; Html.text user.User.Name ]
