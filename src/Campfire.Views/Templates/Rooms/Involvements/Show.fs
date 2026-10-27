// Port of rust/crates/views/templates/rooms/involvements/show.html (reference/app/views/rooms/involvements/show.html.erb)
/// `rooms/involvements/show.html.erb`, with `Rooms::InvolvementsHelper`.
module Campfire.Views.Templates.Rooms.Involvements.Show

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Helpers
open Campfire.Views.Markup
open Campfire.Views.Messages
open Campfire.Views.Rooms

let render (ctx: ViewContext) (involvement: InvolvementView) : XmlNode =
    let room: RoomsHelper.InvolvementRoom =
        { Id = involvement.RoomId
          ParamKey = RoomKind.paramKey involvement.Kind
          Direct = RoomKind.isDirect involvement.Kind }
    Turbo.frameTag
        (roomDomId involvement.Kind involvement.RoomId "involvement")
        None
        None
        [ Attr.create "data-controller" "turbo-frame"
          Attr.create "data-action" "notifications:ready@window->turbo-frame#load"
          Html.attr "data-turbo-frame-url-param" (Routes.roomInvolvement involvement.RoomId) ]
        [ Markup.Rooms.buttonToChangeInvolvement ctx room involvement.Involvement ]
