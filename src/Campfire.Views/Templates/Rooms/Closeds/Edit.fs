// Port of rust/crates/views/templates/rooms/closeds/edit.html (reference/app/views/rooms/closeds/edit.html.erb)
/// `rooms/closeds/edit.html.erb`.
module Campfire.Views.Templates.Rooms.Closeds.Edit

open Campfire.Routes
open Campfire.Views
open Campfire.Views.Templates.Rooms
open Campfire.Views.Rooms

/// The page: `@page_title`, the nav, and the form in the edit layout.
let rendered (ctx: ViewContext) (form: ClosedFormView) : Campfire.Views.Layouts.ViewPage =
    let roomId = defaultArg form.Room.Id 0L
    { Regions =
        Layouts._Edit.regions
            ctx
            (Some("Edit settings for " + form.Room.DisplayName))
            form.CanAdminister
            roomId
            form.Room.DisplayName
            (_Form.render ctx form (Routes.editRoomsOpen roomId))
      Fragments = [] }
