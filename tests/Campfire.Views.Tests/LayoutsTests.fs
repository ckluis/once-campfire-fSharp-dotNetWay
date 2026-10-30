// plans/design-views.md unit 4.10 (O3): the two layouts on Falco.Markup, and what the application layout writes that is not data
// (the asset tags, the account's custom styles).
module Campfire.Views.Tests.LayoutsTests

open System
open AngleSharp.Html.Parser
open Falco.Markup
open Xunit
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Tests.Facts

let private nonNull (value: 'a | null) : 'a =
    match value with
    | null -> failwith "unexpected null"
    | value -> value

let private parse (html: string) = HtmlParser().ParseDocument html

let private marked: Layouts.Regions =
    { Title = Some "T <1>"
      BodyClass = Some "sidebar"
      Head = [ Elem.meta [ Attr.create "name" "page-head"; Attr.create "content" "yes" ] ]
      Nav = [ Elem.div [ Attr.create "id" "in-nav" ] [] ]
      Content = [ Elem.p [ Attr.create "id" "in-content" ] [ Text.raw "page" ] ]
      Footer = [ Elem.div [ Attr.create "id" "in-footer" ] [] ]
      Sidebar = [ Elem.div [ Attr.create "id" "in-sidebar" ] [] ] }

/// O3: the frame layout of a page holds exactly its `Head` and `Content` (no doctype, no nav, footer or sidebar), and the
/// application layout starts with the doctype and holds every region.
[<Fact>]
let ``O3 the frame layout holds the head and the content, the application layout starts with the doctype`` () =
    let ctx = context "welcome" Request.none
    let page: Layouts.ViewPage = { Regions = marked; Fragments = [] }
    let framed = (Layouts.page ctx true page).ToString()
    Assert.False(framed.StartsWith "<!DOCTYPE", "turbo-rails' frame layout has no doctype")
    Assert.StartsWith("<html><head>", framed)
    let frame = parse framed
    Assert.Equal<string list>([ "meta" ], [ for e in (nonNull frame.Head).Children -> e.LocalName ])
    Assert.Equal<string list>([ "p" ], [ for e in (nonNull frame.Body).Children -> e.LocalName ])
    Assert.Equal("page", (nonNull (frame.QuerySelector "p#in-content")).TextContent)
    for absent in [ "nav"; "footer"; "aside"; "dialog"; "#in-nav"; "#in-footer"; "#in-sidebar" ] do
        Assert.Null(frame.QuerySelector absent)
    let full = (Layouts.page ctx false page).ToString()
    Assert.StartsWith("<!DOCTYPE html>", full)
    let document = parse full
    Assert.Equal("T <1>", document.Title)
    for id in [ "in-nav"; "in-content"; "in-footer"; "in-sidebar" ] do
        Assert.NotNull(document.GetElementById id)
    Assert.NotNull(document.QuerySelector "nav#nav #in-nav")
    Assert.NotNull(document.QuerySelector "main#main-content #in-content")
    Assert.NotNull(document.QuerySelector "main#main-content footer#footer #in-footer")
    Assert.NotNull(document.QuerySelector "aside#sidebar #in-sidebar")
    Assert.NotNull(document.QuerySelector "head meta[name=page-head]")
    Assert.StartsWith("sidebar", (nonNull document.Body).ClassName)
    // The lightbox and the app logo are the layout's own.
    Assert.NotNull(document.QuerySelector "dialog.lightbox")
    Assert.NotNull(document.QuerySelector "a#app-logo img[alt='Campfire logo']")

/// Fragments in the page's regions stay fragments in both layouts, and every one of them is placed.
[<Fact>]
let ``a page's fragments stay fragments in the application layout and the frame layout`` () =
    let ctx = context "welcome" Request.none
    let items = [ CachedFragment(String('a', 1500)); CachedFragment "<li>small</li>"; CachedFragment(String('b', 1500)) ]
    let list = [ Elem.ul [] [ for item in items -> Html.fragment item ] ]
    let page = Layouts.ViewPage.content list items
    for framed in [ false; true ] do
        let output = Layouts.page ctx framed page
        Assert.Equal(3, output.Fragments.Length)
        Assert.Same(items[0], output.Fragments[0])
        Assert.Contains("<ul>" + String('a', 1500) + "<li>small</li>" + String('b', 1500) + "</ul>", output.ToString())
    Assert.Throws<InvalidOperationException>(fun () -> Layouts.page ctx false { page with Fragments = [] } |> ignore) |> ignore

/// The asset tags are written as they are, each as one string, between the stylesheet's place and the import map's.
[<Fact>]
let ``the stylesheet and import map tags are written as given`` () =
    let ctx =
        { context "welcome" Request.none with
            StylesheetTags = Html.Trusted.ofAssetTags "<link rel=\"stylesheet\" href=\"/assets/a.css\" />"
            ImportmapTags = Html.Trusted.ofAssetTags "<script type=\"importmap\">{}</script>" }
    let full = (Layouts.page ctx false { Regions = marked; Fragments = [] }).ToString()
    let css = full.IndexOf "<link rel=\"stylesheet\" href=\"/assets/a.css\" />"
    let map = full.IndexOf "<script type=\"importmap\">{}</script>"
    Assert.True(css > 0 && map > css, $"stylesheet at {css}, import map at {map}")
    Assert.True(map < full.IndexOf "<meta name=\"page-head\"", "the import map comes before the page's head")

/// Hostile custom CSS, as the `style` element's text: a `</style>`, the slot marker, a forged slot and a `<`.
[<Fact>]
let ``hostile custom styles land inside the style element`` () =
    let hostile = "a { content: \"x\" } </style><script>alert(1)</script><STYLE> b < c { }  <0> \\ "
    let ctx = { context "welcome" Request.none with CustomStyles = Some hostile }
    let output = Layouts.page ctx false { Regions = marked; Fragments = [] }
    let document = parse (output.ToString())
    let styles = document.QuerySelectorAll "style[data-turbo-track=reload]"
    Assert.Equal(1, styles.Length)
    let style = styles[0]
    for script in document.Scripts do
        Assert.DoesNotContain("alert", script.TextContent)
    // Everything the account typed is in the one element's text (a `</style` is written `<\/style`, which CSS reads the same).
    Assert.Contains("b < c", style.TextContent)
    Assert.Contains("<\\/style><script>alert(1)</script><STYLE> b < c", style.TextContent)
    Assert.Contains("\\E000 ", style.TextContent)
    // The element ends where the layout ends it: what follows is the page.
    Assert.NotNull(style.NextElementSibling)
    // Without custom styles, no element.
    let none = parse ((Layouts.page (context "welcome" Request.none) false { Regions = marked; Fragments = [] }).ToString())
    Assert.Null(none.QuerySelector "style[data-turbo-track=reload]")

/// A flash alert and a flash notice (text, never markup).
[<Fact>]
let ``a flash is text in the live region`` () =
    let ctx = { context "welcome" Request.none with FlashAlert = Some "<b>no</b>" }
    let document = parse ((Layouts.page ctx false { Regions = marked; Fragments = [] }).ToString())
    Assert.Equal("<b>no</b>", (nonNull (document.QuerySelector ".flash [role=alert]")).TextContent)
    Assert.Equal("--flash-background: var(--color-negative)", (nonNull (document.QuerySelector ".flash__inner")).GetAttribute "style")
    Assert.Null(document.QuerySelector ".flash b")
