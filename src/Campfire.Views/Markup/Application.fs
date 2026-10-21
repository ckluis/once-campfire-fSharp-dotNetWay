// `ApplicationHelper` and `RoomsHelper`'s navigation links as Falco.Markup functions (plans/design-views.md V13).
namespace Campfire.Views.Markup

open Falco.Markup
open Campfire.Routes
open Campfire.Views

module Application =
    /// `tag.span(text, class: "for-screen-reader")`.
    let screenReader (text: string) : XmlNode = Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text text ]

    /// `link_back_to(destination)`: an arrow and "Go Back", with no whitespace between.
    let linkBackTo (ctx: ViewContext) (destination: string) : XmlNode =
        Links.linkTo
            destination
            [ Attr.class' "btn" ]
            [ Assets.imageSized ctx "arrow-left.svg" 20 [ Attr.create "aria-hidden" "true" ]; screenReader "Go Back" ]

    /// `link_back_to_last_room_visited`.
    let linkBackToLastRoomVisited (ctx: ViewContext) : XmlNode =
        match ctx.LastRoomVisitedId with
        | Some roomId -> linkBackTo ctx (Routes.room roomId)
        | None -> linkBackTo ctx (Routes.root ())
