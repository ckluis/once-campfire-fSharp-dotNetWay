// Port of rust/crates/views/tests/messages_views.rs: DOM parity of the message views with the reference app
// (goldens in rust/crates/views/tests/golden/b, read here and never written).
module Campfire.Views.Tests.MessagesViewsTests

open AngleSharp.Html.Parser
open Falco.Markup
open Xunit
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Differential.Inputs
open Campfire.Views.Tests.GoldenB

[<Fact>]
let ``show text message`` () =
    let g = golden "messages_show_text"
    let fragment = renderedMessage g.Context (messageView g.Input)
    g.AssertContent((Templates.Messages.Show.output fragment).ToString())

/// The partial is cached once for every request, so nothing in it may come from the request's Host header (README,
/// Known differences).
[<Fact>]
let ``message partial is the same on every host`` () =
    let g = golden "messages_show_text"
    let message = messageView g.Input
    let render (g: Golden) =
        let boosts = renderedBoosts g.Context message
        (HtmlOutput.ofNode (Templates.Messages._Message.render g.Context message boosts) boosts).ToString()
    let onTheReferenceHost = render g
    Assert.Equal(onTheReferenceHost, render { g with BaseUrl = Some "https://evil.example" })
    Assert.Contains($"data-copy-to-clipboard-url-value=\"/rooms/{message.RoomId}/@{message.Id}\"", onTheReferenceHost)

[<Fact>]
let ``show image message`` () =
    let g = golden "messages_show_image"
    let fragment = renderedMessage g.Context (messageView g.Input)
    g.AssertContent((Templates.Messages.Show.output fragment).ToString())

[<Fact>]
let ``index`` () =
    let g = golden "messages_index"
    let messages = arr (g.InputAt "messages") |> List.map (messageItem g.Context)
    g.AssertContent((Templates.Messages.Index.output messages).ToString())

[<Fact>]
let ``edit text message`` () =
    let g = golden "messages_edit_text"
    let edit = editView g.Input
    g.AssertContent((HtmlOutput.ofNode (Templates.Messages.Edit.render g.Context edit) []).ToString())

[<Fact>]
let ``edit attachment message`` () =
    let g = golden "messages_edit_attachment"
    let edit = editView g.Input
    g.AssertContent((HtmlOutput.ofNode (Templates.Messages.Edit.render g.Context edit) []).ToString())

[<Fact>]
let ``create stream`` () =
    let g = golden "messages_create"
    let message = messageItem g.Context (g.InputAt "message")
    let roomKind = roomKind (g.InputAt "room_kind")
    g.AssertDom((Templates.Messages.CreateTurboStream.output message roomKind).ToString())

[<Fact>]
let ``destroy stream`` () =
    let g = golden "messages_destroy"
    let message = messageView g.Input
    g.AssertDom((Templates.Messages.DestroyTurboStream.output message).ToString())

[<Fact>]
let ``room not found`` () =
    let g = golden "messages_room_not_found"
    g.AssertContent((HtmlOutput.ofNode Templates.Messages.RoomNotFound.render []).ToString())

[<Fact>]
let ``boosts index`` () =
    let g = golden "messages_boosts_index"
    let message = messageView g.Input
    g.AssertContent((Templates.Messages.Boosts.Index.output g.Context message (renderedBoosts g.Context message)).ToString())

[<Fact>]
let ``new boost`` () =
    let g = golden "messages_boosts_new"
    let message = messageView (g.InputAt "message")
    let user = userView (g.InputAt "user")
    g.AssertContent((HtmlOutput.ofNode (Templates.Messages.Boosts.New.render g.Context message user) []).ToString())

[<Fact>]
let ``by bots index json`` () =
    let g = golden "messages_by_bots_index"
    let input = arr g.Input |> List.map messageJson
    Assert.Equal(str (get g.Json "raw"), MessagesJson.byBotsIndex input)

[<Fact>]
let ``by bots show json`` () =
    let g = golden "messages_by_bots_show"
    Assert.Equal(str (get g.Json "raw"), MessagesJson.byBotsShow (messageJson g.Input))

[<Fact>]
let ``boosts by bots show json`` () =
    let g = golden "messages_boosts_by_bots_show"
    Assert.Equal(str (get g.Json "raw"), MessagesJson.boostsByBotsShow (boostJson g.Input))

/// T1 (plans/design-views.md section 5): a value that starts with a newline and holds `</textarea>`, `&lt;` and quotes survives
/// the page. The message editor carries it in the `value` attribute of `lexxy-editor`; a `textarea` carries it as its text, after
/// the newline `Html.nl` writes (what Rails' `text_area` does), so the parser's dropped first newline is not the value's.
[<Fact>]
let ``T1 an edit form value survives a newline, a closing textarea and an entity`` () =
    let hostile = "\n<p>a \"quoted\" & it's</p></textarea>&lt;script&gt;"
    let g = golden "messages_edit_text"
    let edit = { editView g.Input with EditableBodyHtml = hostile }
    let html = (HtmlOutput.ofNode (Templates.Messages.Edit.render g.Context edit) []).ToString()
    let document = HtmlParser().ParseDocument html
    let editor = match document.QuerySelector "lexxy-editor" with | null -> failwith "no editor" | e -> e
    Assert.Equal(hostile, editor.GetAttribute "value")
    Assert.Null(document.QuerySelector "script")
    let area = (HtmlOutput.ofNode (Elem.textarea [] [ Html.nl; Html.text hostile ]) []).ToString()
    let parsed = HtmlParser().ParseDocument area
    let textarea = match parsed.QuerySelector "textarea" with | :? AngleSharp.Html.Dom.IHtmlTextAreaElement as t -> t | _ -> failwith "no textarea"
    Assert.Equal(hostile, textarea.Value.Replace("\r\n", "\n"))
