// Port of rust/crates/views/templates/rooms/closeds/new.html (reference/app/views/rooms/closeds/new.html.erb)
/// `rooms/closeds/new.html.erb`.
module Campfire.Views.Templates.Rooms.Closeds.New

open Campfire.Routes
open Campfire.Views
open Campfire.Views.Templates.Rooms
open Campfire.Views.Rooms

/// The page: `@page_title`, the nav, and the form in the new-room layout.
let rendered (ctx: ViewContext) (form: ClosedFormView) : Campfire.Views.Layouts.ViewPage =
    { Regions = Layouts._New.regions ctx (Some "New chat room") (_Form.render ctx form (Routes.newRoomsOpen ()))
      Fragments = [] }
