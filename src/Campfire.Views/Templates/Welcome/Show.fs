// Port of rust/crates/views/templates/welcome/show.html (reference/app/views/welcome/show.html.erb)
/// `welcome/show.html.erb`: shown to users who aren't in any room yet.
module Campfire.Views.Templates.Welcome.Show

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup

/// The page: `@page_title`, `@body_class`, `content_for :sidebar` and the empty message area. `currentUserName` is
/// `Current.user.name`.
let rendered (ctx: ViewContext) (currentUserName: string) : ViewPage =
    { Regions =
        { Regions.content
            [ Elem.div
                  [ Attr.id "message-area"; Attr.class' "message-area" ]
                  [ Elem.div
                        [ Attr.class' "message-area--empty min-width center" ]
                        [ Elem.figure
                              [ Attr.class' "center pad" ]
                              [ Assets.image ctx "messages-empty.svg" [ Attr.create "aria-hidden" "true"; Attr.class' "colorize--black translucent" ]
                                Html.sp
                                Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text currentUserName ] ] ] ] ] with
            Title = Some "No rooms yet"
            BodyClass = Some "sidebar"
            Sidebar = [ Markup.Users.sidebarFrame (Some(Routes.userSidebar ())) ] }
      Fragments = [] }
