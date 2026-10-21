// Port of rust/crates/views/templates/users/_ban_button.html (reference/app/views/users/_ban_button.html.erb)
/// `users/_ban_button.html.erb`: ban or lift the ban on a user.
module Campfire.Views.Templates.Users._BanButton

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (ctx: ViewContext) (user: UserSummary) : XmlNode =
    if user.Active then
        Forms.buttonTo
            (Routes.userBan user.Id)
            "post"
            [ Attr.class' "btn full-width"
              Attr.create
                  "data-turbo-confirm"
                  "Are you sure you want to ban this user? This will log them out, delete their messages, and block their IP addresses." ]
            [ Assets.image ctx "cancel.svg" [ Attr.create "aria-hidden" "true"; Html.attr "aria-label" ("Ban " + user.Name) ]
              Html.sp
              Elem.span [] [ Html.text ("Ban " + user.Name) ] ]
    else
        Forms.buttonTo
            (Routes.userBan user.Id)
            "delete"
            [ Attr.class' "btn btn--negative full-width"
              Attr.create "data-turbo-confirm" "Are you sure you want to remove the ban on this user?" ]
            [ Assets.image ctx "cancel.svg" [ Attr.create "aria-hidden" "true"; Html.attr "aria-label" ("Remove Ban " + user.Name) ]
              Html.sp
              Elem.span [] [ Text.raw "Remove ban" ] ]
