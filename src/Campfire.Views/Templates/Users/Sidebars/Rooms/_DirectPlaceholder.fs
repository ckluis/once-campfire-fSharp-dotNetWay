// Port of rust/crates/views/templates/users/sidebars/rooms/_direct_placeholder.html (reference/app/views/users/sidebars/rooms/_direct_placeholder.html.erb)
/// `users/sidebars/rooms/_direct_placeholder.html.erb`: a button to start a ping with a user.
module Campfire.Views.Templates.Users.Sidebars.Rooms._DirectPlaceholder

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Users

let render (ctx: ViewContext) (user: UserSummary) : XmlNode =
    Forms.buttonTo
        (Campfire.Views.Helpers.Url.roomsDirectsWithUser user.Id)
        "post"
        [ Attr.class' "direct borderless fill-transparent unpad" ]
        [ Elem.span [ Attr.class' "avatar" ] [ Assets.image ctx user.AvatarPath [ Attr.create "aria-hidden" "true" ] ]
          Html.sp
          Elem.span
              [ Attr.class' "direct__author flex align-center gap max-width min-width border-radius txt-small" ]
              [ Elem.span
                    [ Attr.class' "txt-nowrap overflow-ellipsis" ]
                    [ Markup.Application.screenReader "Start a ping with"; Html.sp; Html.text user.FirstName ] ] ]
