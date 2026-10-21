// Port of rust/crates/views/templates/autocompletable/users/_prompt_item.html (reference/app/views/autocompletable/users/_prompt_item.html.erb)
/// `autocompletable/users/_prompt_item.html.erb`: one `<lexxy-prompt-item>` of the mentions prompt.
module Campfire.Views.Templates.Autocompletable.Users._PromptItem

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (ctx: ViewContext) (user: MentionUser) : XmlNode =
    Elem.create
        "lexxy-prompt-item"
        [ Html.attr "search" user.User.Name; Html.attr "sgid" user.AttachableSgid ]
        [ Elem.template
              [ Attr.create "type" "menu" ]
              [ Elem.span
                    [ Attr.class' "autocomplete__item flex align-center gap unpad" ]
                    [ Markup.Users.avatarTag ctx user.User.Avatar []
                      Html.sp
                      Elem.span [ Attr.class' "autocompletable__name" ] [ Html.text user.User.Name ] ] ]
          Elem.template [ Attr.create "type" "editor" ] [ Campfire.Views.Templates.Users._Mention.render ctx user ] ]
