// Port of rust/crates/views/templates/users/show.html (reference/app/views/users/show.html.erb)
/// `users/show.html.erb`: a person's page.
module Campfire.Views.Templates.Users.Show

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup
open Campfire.Views.Users

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// `content_for :nav`.
let private nav (ctx: ViewContext) (user: UserSummary) : XmlNode list =
    [ Elem.div [ Attr.class' "flex-item-justify-start" ] [ Application.linkBack ctx ]
      Elem.div
          [ Attr.class' "flex align-center gap flex-item-justify-end" ]
          [ if ctx.IsCurrentUser user.Id then
                Links.linkTo
                    (Routes.userProfile ())
                    [ Attr.class' "btn" ]
                    [ Assets.image ctx "pencil.svg" [ hidden ]; Html.sp; Application.screenReader "Edit my profile" ] ] ]

/// The page itself. `transferId` is `user.transfer_id`, for `users/profiles/_transfer` (shown to administrators).
let private content (ctx: ViewContext) (user: UserSummary) (transferId: string) : XmlNode =
    Elem.section
        [ Attr.class' "panel txt-align-center" ]
        [ Elem.div
              [ Html.attr "class" ("flex flex-column gap " + (if user.Banned then "banned" else "")) ]
              [ Elem.div
                    [ Attr.class' "avatar txt-xx-large center"; Attr.create "style" "background: white" ]
                    [ Assets.image ctx user.AvatarPath [ Attr.create "alt" "Profile avatar"; Attr.class' "avatar" ] ]
                if user.Bot then
                    Elem.div
                        [ Attr.class' "pad-double--inline push--inline push--block-start" ]
                        [ if user.Active then
                              Markup.Users.buttonToDirectRoomWith ctx user.Id
                          else
                              Elem.div [] [ Html.text user.Name; Text.raw " is no longer on this account" ] ]
                elif not user.Deactivated then
                    Elem.div
                        [ Attr.class' "flex flex-column gap"; Attr.create "style" "--row-gap: calc(var(--block-space) / 3)" ]
                        [ Elem.h1 [ Attr.class' "txt-x-large txt-tight-lines margin-none" ] [ Html.text user.Name ]
                          if ctx.CanAdminister then
                              Elem.div [] [ Links.mailTo (defaultArg user.EmailAddress "") ]
                          Elem.div [] [ Html.text (defaultArg user.Bio "") ] ]
                    if user.Active then
                        Elem.div
                            [ Attr.class' "pad-inline-double margin-inline margin-block-start" ]
                            [ Forms.buttonTo
                                  (Campfire.Views.Helpers.Url.roomsDirectsWithUser user.Id)
                                  "post"
                                  [ Attr.class' "btn btn--reversed full-width txt-large" ]
                                  [ Assets.image
                                        ctx
                                        "messages.svg"
                                        [ hidden; Html.attr "aria-label" ("Ping " + user.Name) ] ] ]
                        if ctx.CanAdminister then
                            Elem.hr [ Attr.class' "margin-block-start borderless" ]
                            Profiles._Transfer.render ctx user transferId
                    if ctx.CanAdminister && not (ctx.IsCurrentUser user.Id) then
                        Elem.div [ Attr.class' "margin-block-start" ] [ _BanButton.render ctx user ]
                else
                    Elem.div
                        []
                        [ Elem.h1 [ Attr.class' "txt-x-large margin-none" ] [ Html.text user.Name ]
                          Elem.div [] [ Html.text user.Name; Text.raw " is no longer on this account" ] ] ] ]

/// The page: `@page_title`, the nav and the content.
let rendered (ctx: ViewContext) (user: UserSummary) (transferId: string) : ViewPage =
    { Regions =
        { Regions.content [ content ctx user transferId ] with
            Title = Some user.Name
            Nav = nav ctx user }
      Fragments = [] }
