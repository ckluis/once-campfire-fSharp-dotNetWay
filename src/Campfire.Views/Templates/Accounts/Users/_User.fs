// Port of rust/crates/views/templates/accounts/users/_user.html (reference/app/views/accounts/users/_user.html.erb)
/// `accounts/users/_user.html.erb`: a person on the account page, with the admin's role, remove and settings buttons.
module Campfire.Views.Templates.Accounts.Users._User

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// The administrator's role form: a hidden checkbox behind the crown, submitted on change.
let private roleForm (ctx: ViewContext) (user: UserSummary) : XmlNode =
    let domId = Campfire.Views.Helpers.Turbo.domId "user" user.Id (Some "role")
    Forms.formWith
        (Routes.accountUser user.Id)
        "patch"
        [ Attr.create "data-controller" "form" ]
        [ Elem.label
              [ Attr.class' "btn txt-small flex-item-no-shrink"; Html.attr "for" domId ]
              [ Elem.span
                    [ Attr.class' "for-screen-reader" ]
                    [ Html.text ("Role: " + (if user.Administrator then "Administrator" else "Member")) ]
                Html.sp
                Assets.imageSized ctx "crown.svg" 20 [ hidden ]
                Html.sp
                yield!
                    Forms.checkBox
                        "user"
                        "role"
                        (Some domId)
                        (ctx.IsCurrentUser user.Id)
                        [ Attr.create "data-action" "form#submit"; Attr.create "hidden" "hidden" ]
                        "administrator"
                        "member"
                        (Role.asStr user.Role) ] ]

let render (ctx: ViewContext) (user: UserSummary) : XmlNode =
    Elem.li
        [ Html.attr "class" ("flex align-center gap margin-none " + (if user.Banned then "banned" else "")) ]
        [ Elem.figure
              [ Attr.class' "avatar flex-item-no-shrink"; Attr.create "style" "--avatar-size: 3.75ch;" ]
              [ Markup.Users.avatarTag ctx user.Avatar [ Attr.create "loading" "lazy" ] ]
          Elem.div
              [ Attr.class' "min-width" ]
              [ Elem.div [ Attr.class' "overflow-ellipsis fill-shade" ] [ Elem.strong [] [ Html.text user.Name ] ] ]
          Elem.hr [ Attr.class' "separator"; hidden ]
          if ctx.CanAdminister && user.Active then
              if not user.Bot then
                  roleForm ctx user
              if not (ctx.IsCurrentUser user.Id) then
                  Forms.buttonTo
                      (Routes.accountUser user.Id)
                      "delete"
                      [ Attr.class' "btn txt-small flex-item-no-shrink btn--negative"
                        Attr.create
                            "data-turbo-confirm"
                            "Are you sure you want to permanently remove this person from the account? This can’t be undone." ]
                      [ Assets.imageSized ctx "minus.svg" 20 [ hidden ]
                        Html.sp
                        Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Delete "; Html.text user.Name ] ]
          if ctx.IsCurrentUser user.Id then
              Links.linkTo
                  (Routes.userProfile ())
                  [ Attr.class' "btn txt-small flex-item-no-shrink"; Attr.create "target" "_top" ]
                  [ Assets.imageSized ctx "pencil.svg" 20 [ hidden ]
                    Html.sp
                    Application.screenReader "My settings" ] ]
