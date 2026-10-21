// Port of rust/crates/views/tests/rooms_views.rs (`display_names` is in MessagesTests): DOM parity of the room views
// with the reference app (goldens in rust/crates/views/tests/golden/b, read here and never written).
module Campfire.Views.Tests.RoomsViewsTests

open Xunit
open Campfire.Views
open Campfire.Views.Templates
open Campfire.Views.Differential.Inputs
open Campfire.Views.Tests.GoldenB

/// A Falco page in the writer's application layout (`Layouts.page`, until unit 4.10), as text.
let private page (g: Golden) (viewPage: Layouts.ViewPage) : string = (Layouts.page g.Context false viewPage).ToString()

let private show (name: string) =
    let g = golden name
    let show = showView g.Context g.Input
    g.AssertDom(page g (Rooms.ShowPage.rendered g.Context show))

/// A room page as it's served keeps every message as a fragment the kit gets as it is, in the application layout and in the
/// Turbo-Frame layout.
[<Fact>]
let ``show keeps every message as a fragment`` () =
    let g = golden "rooms_show_member"
    let show = showView g.Context g.Input
    Assert.NotEmpty show.Messages
    let viewPage = Rooms.ShowPage.rendered g.Context show
    for frame in [ false; true ] do
        let output = Layouts.page g.Context frame viewPage
        Assert.Equal(show.Messages.Length, output.Fragments.Length)
        for message, fragment in Seq.zip show.Messages output.Fragments do
            Assert.Same(message.Html, fragment)
        Assert.Equal(frame, output.ToString().StartsWith "<html>")

[<Fact>]
let ``show closed room`` () = show "rooms_show_closed"

[<Fact>]
let ``show original room with invitation`` () = show "rooms_show_original"

[<Fact>]
let ``show direct room`` () = show "rooms_show_direct"

[<Fact>]
let ``show room as member`` () = show "rooms_show_member"

[<Fact>]
let ``show room around message`` () = show "rooms_show_at_message"

[<Fact>]
let ``new open room`` () =
    let g = golden "rooms_opens_new"
    let form = openFormView g.Input
    g.AssertDom(page g (Rooms.Opens.New.rendered g.Context form))

[<Fact>]
let ``edit open room`` () =
    for name in [ "rooms_opens_edit"; "rooms_opens_edit_member" ] do
        let g = golden name
        let form = openFormView g.Input
        g.AssertDom(page g (Rooms.Opens.Edit.rendered g.Context form))

[<Fact>]
let ``new closed room`` () =
    let g = golden "rooms_closeds_new"
    let form = closedFormView g.Input
    g.AssertDom(page g (Rooms.Closeds.New.rendered g.Context form))

[<Fact>]
let ``edit closed room`` () =
    for name in [ "rooms_closeds_edit"; "rooms_closeds_edit_member" ] do
        let g = golden name
        let form = closedFormView g.Input
        g.AssertDom(page g (Rooms.Closeds.Edit.rendered g.Context form))

[<Fact>]
let ``new direct room`` () =
    let g = golden "rooms_directs_new"
    g.AssertDom(page g (Rooms.Directs.New.rendered g.Context))

[<Fact>]
let ``edit direct room`` () =
    let g = golden "rooms_directs_edit"
    let edit = directEditView g.Input
    g.AssertDom(page g (Rooms.Directs.Edit.rendered g.Context edit))

[<Fact>]
let ``involvement`` () =
    for name in [ "rooms_involvements_show"; "rooms_involvements_show_direct" ] do
        let g = golden name
        let involvement = involvementView g.Input
        g.AssertContent((HtmlOutput.ofNode (Rooms.Involvements.Show.render g.Context involvement) []).ToString())

[<Fact>]
let ``refresh stream`` () =
    let g = golden "rooms_refreshes_show"
    let refresh = refreshView g.Context g.Input
    g.AssertDom((Rooms.Refreshes.ShowTurboStream.output refresh).ToString())
