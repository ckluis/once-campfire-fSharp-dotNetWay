// Core/Html.fs (the encoding module of the Falco.Markup views), against Falco.Markup 1.4.0's
// own renderer. Falco writes attribute values raw; these tests are what the lint (bin/lint-markup) relies on being true of Html.attr.
module Campfire.Views.Tests.HtmlTests

open Xunit
open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

let private hostile = "a\"b<c>&d'e é 😀"

[<Fact>]
let ``falco writes an attribute value raw and Html.attr encodes it`` () =
    // The reason the lint exists (plans/design-views.md S1).
    Assert.Equal("<div title=\"a\"b<c>\"></div>", renderNode (Elem.div [ Attr.title "a\"b<c>" ] []))
    Assert.Equal("<div title=\"a&quot;b&lt;c&gt;&amp;d&#39;e &#233; &#128512;\"></div>", renderNode (Elem.div [ Html.attr "title" hostile ] []))

[<Fact>]
let ``text is encoded with the same rule`` () =
    Assert.Equal("<p>a&quot;b&lt;c&gt;&amp;d&#39;e &#233; &#128512;</p>", renderNode (Elem.p [] [ Html.text hostile ]))
    Assert.Equal(renderNode (Elem.p [] [ Text.enc hostile ]), renderNode (Elem.p [] [ Html.text hostile ]))

[<Fact>]
let ``integers, spaces and newlines`` () =
    Assert.Equal("<p>-42</p>", renderNode (Elem.p [] [ Html.int -42L ]))
    Assert.Equal("<div id=\"7\"></div>", renderNode (Elem.div [ Html.attrInt "id" 7L ] []))
    Assert.Equal("<p>a b\n</p>", renderNode (Elem.p [] [ Text.enc "a"; Html.sp; Text.enc "b"; Html.nl ]))

[<Fact>]
let ``trusted text is written as it is`` () =
    Assert.Equal("<div><b>x</b></div>", renderNode (Elem.div [] [ Html.trusted (Html.Trusted.ofSanitized "<b>x</b>") ]))
    Assert.Equal("<style>a{}</style>", renderNode (Elem.style [] [ Html.trusted (Html.Trusted.ofAccountStyles "a{}") ]))
