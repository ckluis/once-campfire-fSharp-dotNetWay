// Port of rust/crates/views/templates/rooms/opens/edit.html (reference/app/views/rooms/opens/edit.html.erb)
/// `rooms/opens/edit.html.erb`.
module Campfire.Views.Templates.Rooms.Opens.Edit

open Campfire.Routes
open Campfire.Views
open Campfire.Views.Templates.Rooms
open Campfire.Views.Rooms

/// The page: `@page_title`, the nav, and the form in the edit layout.
let rendered (ctx: ViewContext) (form: OpenFormView) : Campfire.Views.Layouts.ViewPage =
    let roomId = defaultArg form.Room.Id 0L
    { Regions =
        Layouts._Edit.regions
            ctx
            (Some("Edit settings for " + form.Room.DisplayName))
            form.CanAdminister
            roomId
            form.Room.DisplayName
            (_Form.render ctx form (Routes.editRoomsClosed roomId))
      Fragments = [] }
