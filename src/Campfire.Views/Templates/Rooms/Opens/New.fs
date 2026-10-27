// Port of rust/crates/views/templates/rooms/opens/new.html (reference/app/views/rooms/opens/new.html.erb)
/// `rooms/opens/new.html.erb`.
module Campfire.Views.Templates.Rooms.Opens.New

open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Templates.Rooms
open Campfire.Views.Rooms

/// The page: `@page_title`, the nav, and the form in the new-room layout.
let rendered (ctx: ViewContext) (form: OpenFormView) : Campfire.Views.Layouts.ViewPage =
    { Regions = Layouts._New.regions ctx (Some "New chat room") (_Form.render ctx form (Routes.newRoomsClosed ()))
      Fragments = [] }
