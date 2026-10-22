// Unit 5.1 of plans/design-richtext.md: `Html5` (the parser, the limits and caps, the serializer, the DOM helpers) against the trees
// the old parser (Gumbo's port) built, frozen in corpus/trees.jsonl and corpus/context-trees.jsonl before it was deleted (P1-P5),
// and against Gumbo's truth table and the port's hostile shapes (L1-L4).
module Campfire.RichText.Tests.Html5Tests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open AngleSharp.Dom
open Microsoft.Data.Sqlite
open Xunit
open Campfire.RichText
open Campfire.RichText.Next
open Campfire.RichText.Tests.Support
open Campfire.Tests

// --- Trees ---------------------------------------------------------------------------------------------------------------------

let private quote (s: string) : string = JsonSerializer.Serialize s

let private line (out: StringBuilder) (depth: int) (text: string) = out.Append(' ', depth).Append(text).Append('\n') |> ignore

let private attributesText (attributes: (string * string) list) : string =
    attributes |> List.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b)) |> List.map (fun (k, v) -> $"{k}={quote v}") |> String.concat " "

/// The same dump of a container's children.
let treeOf (container: INode) : string =
    let out = StringBuilder()
    let rec walk (node: INode) (depth: int) =
        for child in node.ChildNodes do
            match child with
            | :? IElement as e ->
                let attributes = [ for a in e.Attributes -> Html5.qualifiedName a, a.Value ]
                line out depth $"<{e.NamespaceUri} {e.LocalName} {attributesText attributes}>"
                walk e (depth + 1)
            | :? IComment as c -> line out depth ("#comment " + quote c.Data)
            | :? IText as t -> line out depth ("#text " + quote t.Data)
            | _ -> line out depth $"?{child.NodeName}"
    walk container 0
    out.ToString()

let newTree (html: string) : Result<string, ParseError> = Html5.parseBody html |> Result.map treeOf

/// The old parser's trees, frozen by unit 5.7 from the old `Dom` on the 1,479 inputs (labels as `inputs` makes them) and on the few
/// strings the P2 tests name (`p2:` and the markup). One JSON line each: `{"label", "tree"}`.
let private loadFrozen (file: string) : IReadOnlyDictionary<string, string> =
    let d = Dictionary<string, string>()
    for line in File.ReadLines(Repo.path $"tests/Campfire.RichText.Tests/corpus/{file}") do
        use j = JsonDocument.Parse line
        d[j.RootElement.GetProperty("label").GetString() |> nonNull] <- j.RootElement.GetProperty("tree").GetString() |> nonNull
    d

let private frozenTrees = lazy (loadFrozen "trees.jsonl")
let private frozenContextTrees = lazy (loadFrozen "context-trees.jsonl")

/// What the old parser built for a P2 string, as stored.
let private oldShown (html: string) : string = frozenTrees.Force().["p2:" + html]

// --- The 1,479 inputs ----------------------------------------------------------------------------------------------------------

let private str (e: JsonElement) : string = e.GetString() |> nonNull

/// (label, body): the corpus's bodies, Rails' presentations of them, and the default seed's message bodies.
let inputs =
    lazy
        (let corpus = JsonDocument.Parse(File.ReadAllText(Repo.path "tests/Campfire.RichText.Tests/corpus/expected.json"))
         let cases = corpus.RootElement.GetProperty("cases").EnumerateArray() |> Seq.toArray
         let bodies = [ for c in cases -> "corpus:" + str (c.GetProperty "name"), str (c.GetProperty "body") ]
         let presentations =
             [ for c in cases do
                   let p = c.GetProperty "presentation"
                   match p.TryGetProperty "ok" with
                   | true, ok when ok.ValueKind = JsonValueKind.String -> yield "rails:" + str (c.GetProperty "name"), str ok
                   | _ -> () ]
         let seeds =
             match Repo.seed "default" with
             | None -> []
             | Some dir ->
                 let database = Path.Combine(dir, "db", "production.sqlite3")
                 use connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly")
                 connection.Open()
                 use command = connection.CreateCommand()
                 command.CommandText <- "SELECT id, body FROM action_text_rich_texts WHERE body IS NOT NULL AND body != '' ORDER BY id"
                 use reader = command.ExecuteReader()
                 [ while reader.Read() do
                       yield $"seed:{reader.GetInt64 0}", reader.GetString 1 ]
         bodies, presentations, seeds)

/// What a body and its refusal look like on either side, for comparing.
let private shown (r: Result<string, ParseError>) : string =
    match r with
    | Ok tree -> tree
    | Error e -> $"refused: {e.Message}"

/// P1's named list (S2's, plans/design-richtext.md section 2): corpus bodies whose tree differs from the old parser's (frozen), none of
/// the seed's 164 and none of Rails' 657 presentations. Fuzz 188, 362 and 395 were reduced in 5.7 (deleting tokens while the two
/// trees still differed) to one shape each; `malformed select` and fuzz 125, 177 and 325 are S2's reading, the same select model.
/// Fuzz 362 is `<select><option><span>`: an element inside an `<option>` (the 2025 content model keeps it, Gumbo flattens it to text). Fuzz 188 is `<select><hr>` (AngleSharp
/// drops an `<hr>` start tag in a select, Gumbo keeps it); fuzz 395 is `<template><form><summary></form>text` (the `</form>` closes
/// the `<summary>` for Gumbo and not for AngleSharp, so the text stays in it); `limits attributes on an unfinished tag` is a limit
/// boundary (L1's list). S2 listed twelve: fuzz 44 and 63 (a `<table>` in a `<p>`, which a
/// document without a doctype, in quirks mode, leaves inside the `<p>`) are equal here because every parse starts with a doctype;
/// "limits attributes at the limit and a duplicate" because the attribute count is made before AngleSharp drops a duplicate, as
/// Gumbo's is; and "limits too deep before the adoption agency cuts" because an element's depth is checked at the next token.
let p1Differences =
    set
        [ "corpus:malformed select"; "corpus:limits attributes on an unfinished tag"
          "corpus:fuzz 125"; "corpus:fuzz 177"; "corpus:fuzz 188"; "corpus:fuzz 325"; "corpus:fuzz 362"
          "corpus:fuzz 395" ]

[<Fact>]
let ``P1 trees with namespaces equal the frozen old-parser trees on the 1,479 inputs, S2's named list apart`` () =
    let bodies, presentations, seeds = inputs.Force()
    Assert.Equal(658, bodies.Length)
    Assert.True(seeds.IsEmpty || seeds.Length = 164, $"{seeds.Length} seed bodies")
    let differing = ResizeArray<string>()
    let report = StringBuilder()
    for (label, html) in bodies @ presentations @ seeds do
        let o = frozenTrees.Force().[label]
        let n = shown (newTree html)
        if o <> n then
            differing.Add label
            // The first line where the trees part, with the line before it
            let a = o.Split('\n')
            let b = n.Split('\n')
            let at = Seq.zip a b |> Seq.tryFindIndex (fun (x, y) -> x <> y) |> Option.defaultValue (min a.Length b.Length)
            let around (t: string[]) = String.Join("\n", t |> Array.skip (max 0 (at - 1)) |> Array.truncate 3 |> Array.map (fun l -> if l.Length > 160 then l.Substring(0, 160) else l))
            report.AppendLine($"{label} at line {at}\n--- old\n{around a}\n--- new\n{around b}") |> ignore
    let unexpected = differing |> Seq.filter (fun l -> not (p1Differences.Contains l)) |> List.ofSeq
    let vanished = p1Differences |> Set.filter (fun l -> not (differing.Contains l)) |> List.ofSeq
    let unlisted = String.Join("; ", unexpected)
    let gone = String.Join("; ", vanished)
    Assert.True(unexpected.IsEmpty, $"{unexpected.Length} unlisted: {unlisted}\n{report}")
    Assert.True(vanished.IsEmpty, $"listed but equal now: {gone}")

// --- L1: Gumbo's truth table -----------------------------------------------------------------------------------------------------

let private numberedAttributes (first: int) (last: int) : string = [ for i in first..last -> $"a{i}={i}" ] |> String.concat " "

let private b n = String.replicate n "<b>"
let private span n = String.replicate n "<span>"
let private div n = String.replicate n "<div>"

let private reopened =
    let bs = [ for i in 1..399 -> $"<b id={i}>" ] |> String.concat ""
    $"<p>{bs}</p><div><div><table>x"

/// (name, input, what Gumbo answers: from DomTests, which took them from Nokogiri 1.19.4 in the reference image). `None` is Ok.
let private gumbo : (string * string * ParseError option) list =
    let attributes = numberedAttributes 1 400
    [ "400 b", b 400, None
      "401 b", b 401, Some TreeDepthExceeded
      "401 b and text", b 401 + "x", Some TreeDepthExceeded
      "400 b and p", b 400 + "<p>", Some TreeDepthExceeded
      "397 b and table td", b 397 + "<table><td>", Some TreeDepthExceeded
      "400 b and br", b 400 + "<br>", None
      "400 b and an end p", b 400 + "</p>", None
      "401 b closed again", b 401 + String.replicate 401 "</b>", Some TreeDepthExceeded
      "adoption agency moves blocks up", String.replicate 3 ("<b>" + span 300 + div 10 + "</b>"), None
      "adoption agency, too deep before", "<b>" + span 390 + div 10 + "</b>" + div 300, Some TreeDepthExceeded
      "reopened b in table text", reopened, None
      "reopened b and a comment", reopened + "<!---->", Some TreeDepthExceeded
      "400 attributes", $"<p {attributes}>x</p>", None
      "401 attributes", $"<p {numberedAttributes 1 401}>x</p>", Some TooManyAttributes
      "1000 equal attributes", $"""<p {String.replicate 1000 "a=1 "}>x</p>""", None
      "401 attributes in a textarea", $"<textarea><p {numberedAttributes 1 401}>", None
      "400 attributes and a duplicate", $"<p {attributes} a1=again>x</p>", Some TooManyAttributes
      "attributes on an end tag", $"<p>x</p {attributes} a401>", Some TooManyAttributes
      "attributes on an unfinished tag", $"<p {attributes} a401", Some TooManyAttributes ]

let private parseOutcome (html: string) : ParseError option =
    match Html5.parseBody html with
    | Ok _ -> None
    | Error e -> Some e

/// Where the depth is counted on elements as they are created (AngleSharp exposes no stack) or an unfinished tag is lost with the
/// input's end, the answer differs from Gumbo's; each is also a `known-differences.json` entry from unit 5.6 on.
let private l1Boundaries : (string * ParseError option) list =
    [ // The </p> with no p open makes an empty p at depth 401, which Gumbo pops at once (design note 8: "refused here")
      "400 b and an end p", Some TreeDepthExceeded
      // Gumbo checks again at the comment after it reopened the b's past the limit; here the last element's depth is checked at the end
      "reopened b and a comment", None
      // An attribute still being read when the input ends is lost to the tokenizer's callback
      "attributes on an unfinished tag", None ]

[<Fact>]
let ``L1 Gumbo's truth table, a named list of boundary cases apart`` () =
    let report = ResizeArray<string>()
    for (name, input, expected) in gumbo do
        let actual = parseOutcome input
        match List.tryFind (fun (n, _) -> n = name) l1Boundaries with
        | Some(_, recorded) ->
            if actual <> recorded then report.Add $"{name}: boundary recorded {recorded}, got {actual}"
            if actual = expected then report.Add $"{name}: listed as a boundary but equals Gumbo ({expected})"
        | None -> if actual <> expected then report.Add $"{name}: Gumbo {expected}, got {actual}"
    Assert.True(report.Count = 0, String.Join("\n", report))


// --- P2-P5 ---------------------------------------------------------------------------------------------------------------------

let private containerOf (html: string) : IElement = okP (Html5.parseBody html)

let private firstOf (n: INode) : INode = nonNull n.FirstChild
let private lastOf (n: INode) : INode = nonNull n.LastChild

let private roundtrip (html: string) : string = Html5.innerHtml (containerOf html)

[<Fact>]
let ``P2 a template's contents are its children`` () =
    let container = containerOf "<template><p>x</p><template><i>y</i></template></template>z"
    Assert.Equal(
        "<http://www.w3.org/1999/xhtml template >\n <http://www.w3.org/1999/xhtml p >\n  #text \"x\"\n <http://www.w3.org/1999/xhtml template >\n  <http://www.w3.org/1999/xhtml i >\n   #text \"y\"\n#text \"z\"\n",
        treeOf container
    )
    Assert.Equal("<template><p>x</p><template><i>y</i></template></template>z", Html5.innerHtml container)

[<Fact>]
let ``P2 a leading byte order mark is dropped, one later is kept`` () =
    Assert.Equal("﻿x", roundtrip "﻿﻿x")
    Assert.Equal("<script></script>﻿x", roundtrip "<script></script>﻿x")

[<Fact>]
let ``P2 a leading frameset parses as the fragment algorithm does`` () =
    for html in
        [ "<frameset><frame></frameset>x"; " <!--c--><frameset><p>y</p>"; "<frameset><svg><p>y</p></svg>"; "<frameset>"; "x<frameset>y"
          "\n<frameset rows=1><frame src=a>text" ] do
        Assert.Equal(oldShown html, shown (newTree html))
    Assert.Equal("x", roundtrip "<frameset><frame></frameset>x")
    Assert.Equal("<!--c-->\n<p>y</p>", roundtrip "<!--c-->\n<frameset><p>y</p>")
    Assert.Equal("", roundtrip "<frameset>")
    // The letter that closes the window for a frameset is not left in the tree
    Assert.Equal("yz", roundtrip "<frameset>yz")

[<Fact>]
let ``P2 html and body tags add nothing`` () =
    Assert.Equal("<p>x</p>", roundtrip "<html a=1><p>x</p></html>")
    Assert.Equal("x", roundtrip "<body a=1 class=b>x</body>")
    Assert.Equal(oldShown "<head><title>t</title></head>x", shown (newTree "<head><title>t</title></head>x"))
    Assert.Equal(oldShown "<body><body>x", shown (newTree "<body><body>x"))
    // The container is the body: the attributes of a body tag in the input do not reach it as markup of the container's children
    Assert.Equal("", roundtrip "<html a=1>")

[<Fact>]
let ``P2 comments after body and html stay in the container`` () =
    Assert.Equal(oldShown "<p>a</p></body><!--c--></html><!--d-->", shown (newTree "<p>a</p></body><!--c--></html><!--d-->"))
    Assert.Equal("<p>a</p><!--c--><!--d-->", roundtrip "<p>a</p></body><!--c--></html><!--d-->")
    Assert.Equal(oldShown "<p>a</p></body>text</body>more", shown (newTree "<p>a</p></body>text</body>more"))
    // Known difference: a comment after </body> that content follows arrives after that content
    Assert.Equal("<p>a</p>tail<!--c-->", roundtrip "<p>a</p></body><!--c-->tail")
    Assert.Equal("<p>a</p><!--c-->tail", Html5.innerHtml (okP (Html5.parseBody "<p>a</p><!--c-->tail")))

[<Fact>]
let ``P2 the document algorithm's remaining differences from Gumbo are the ones recorded`` () =
    // The 2025 standard's select content model: Gumbo drops the b's element, the document parse keeps it (P1's list)
    Assert.Equal("<select><option>a<b>b</b></option></select>", roundtrip "<select><option>a<b>b</b></select>")
    // AngleSharp still expands <isindex> as the 2008 standard did; Gumbo (and browsers) leave an element of that name. Its form,
    // rules, label and input go through the sanitizer's allowlist (unit 5.2), the label's sentence does not.
    Assert.Equal(
        "<form><hr><label>This is a searchable index. Enter search keywords: <input name=\"isindex\"></label><hr></form>",
        roundtrip "<isindex>"
    )

[<Fact>]
let ``P3 the serializer is Nokogiri's`` () =
    Assert.Equal("x", roundtrip "<td>x</td>")
    Assert.Equal("<p></p><table><tbody><tr><td>a</td></tr></tbody></table>", roundtrip "<p><table><tr><td>a</td></tr></table>")
    Assert.Equal(
        "<a title=\"a<b>c\" href=\"x&amp;y&nbsp;z\">t&lt;&nbsp;&gt;\"'</a>",
        roundtrip "<a title='a<b>c' href=\"x&y z\">t&lt; >\"'</a>"
    )
    Assert.Equal("<pre>\nx</pre>", roundtrip "<pre>\n\nx</pre>")
    Assert.Equal("<noscript><b>x</b></noscript>", roundtrip "<noscript><b>x</b></noscript>")
    Assert.Equal("<!--?php x ?-->", roundtrip "<?php x ?>")
    Assert.Equal("<svg viewBox=\"0 0 1 1\"><clipPath></clipPath></svg>", roundtrip "<SVG viewBox='0 0 1 1'><CLIPPATH/></SVG>")


[<Fact>]
let ``P3 serializer rules`` () =
    // One newline lost per round trip: the parser drops the first, the serializer adds none
    Assert.Equal("<pre>\nx</pre>", roundtrip "<pre>\n\nx</pre>")
    Assert.Equal("<textarea>\nx</textarea>", roundtrip "<textarea>\n\nx</textarea>")
    Assert.Equal("<listing>\nx</listing>", roundtrip "<listing>\n\nx</listing>")
    Assert.Equal("<pre>x</pre>", roundtrip "<pre>\nx</pre>")
    // Raw text
    for html in
        [ "<noscript>a&b</noscript>"; "<script>a<b&c</script>"; "<style>a>b{c:'&'}</style>"; "<xmp><b>&</xmp>"
          "<iframe><b>&</iframe>"; "<noembed><b>&</noembed>"; "<noframes><b>&</noframes>"; "<plaintext><b>&</plaintext>" ] do
        Assert.Equal(html, roundtrip (html.Replace("</plaintext>", "")))
    // Escaping: text has & nbsp < >, attributes & nbsp "; < and > stay raw in attributes
    Assert.Equal("<p>a&amp;b&nbsp;c&lt;d&gt;e\"f'</p>", roundtrip "<p>a&amp;b&nbsp;c&lt;d&gt;e\"f'</p>")
    Assert.Equal("<p title=\"a&amp;b&nbsp;c&quot;d<e>f'g\"></p>", roundtrip "<p title='a&amp;b&nbsp;c\"d<e>f&apos;g'></p>")
    // Void elements have no closing tag
    Assert.Equal("<br><img src=\"x\"><input><p></p>", roundtrip "<br><img src=x><input><p>")
    Assert.Equal("a<br>b", roundtrip "a<br/>b")
    // Foreign attribute names
    Assert.Equal(
        "<svg xmlns:xlink=\"u\" xml:lang=\"en\" xlink:href=\"a\"><use xlink:href=\"b\"></use></svg>",
        roundtrip "<svg xmlns:xlink=u xml:lang=en xlink:href=a><use xlink:href=b></svg>"
    )
    // Comments, and what is not markup
    Assert.Equal("<!--x--><!--?php y ?--><!--[CDATA[z]]-->", roundtrip "<!--x--><?php y ?><![CDATA[z]]>")
    // Doctype, html, head and body contribute nothing
    Assert.Equal("<p>x</p>", roundtrip "<!DOCTYPE html><html><head></head><body><p>x</p></body></html>")

[<Fact>]
let ``P3 the deduplicated attributes are kept as the tokenizer left them`` () =
    Assert.Equal("<p title=\"a\" id=\"c\">x</p>", roundtrip "<p title=a TITLE=b id=c title=d>x</p>")
    Assert.Equal(
        "<svg xlink:href=\"a\" href=\"b\" viewBox=\"c\"><a xlink:href=\"d\">x</a></svg>",
        roundtrip "<svg xlink:href=a href=b viewbox=c><a xlink:href=d>x</a></svg>"
    )


[<Fact>]
let ``P4 element-context parses give the frozen old ParseNodes' trees for the markup the pipeline builds`` () =
    // What the pipeline hands an element-context parse: partials it rendered ("<div>" + inner + "</div>", figures, a gallery's
    // markup), "@Name", "[caption]", and the plain texts of the corpus (the text of a node, as `replace` puts it back).
    let corpus = JsonDocument.Parse(File.ReadAllText(Repo.path "tests/Campfire.RichText.Tests/corpus/expected.json"))
    let plainTexts =
        [ for c in corpus.RootElement.GetProperty("cases").EnumerateArray() do
              match (c.GetProperty "plain_text").TryGetProperty "ok" with
              | true, ok when ok.ValueKind = JsonValueKind.String -> yield "plain:" + str (c.GetProperty "name"), str ok
              | _ -> () ]
    let contexts = [ "action-text-attachment"; "div"; "p"; "li"; "figure"; "td" ]
    let strings =
        [ "@Name"; "[caption]"; "<div>x</div>"; "<div><p>a</p>b</div>"; "<figure><img src=x></figure>"; "<tr><td>x</td></tr>"; "<li>x"
          "text &amp; more"; "<figure class=\"attachment attachment--file\"><a href=\"/x\"><figcaption>c &amp; d</figcaption></a></figure>"
          "<div class=\"attachment-gallery attachment-gallery--2\"><figure><img src=\"a\"></figure><figure><img src=\"b\"></figure></div>" ]
    let owner = nonNull (containerOf "").Owner
    let oldNodes (context: string) (label: string) : string = frozenContextTrees.Force().[$"{context}/{label}"]
    let newNodes (context: string) (html: string) : string =
        match Html5.parseIn (owner.CreateElement context) html with
        | Error e -> $"refused: {e.Message}"
        | Ok nodes ->
            let holder = owner.CreateElement "div"
            for node in nodes do
                Assert.Null node.Parent
                holder.AppendChild node |> ignore
            treeOf holder
    // Set aside: `select` (P1's content-model difference) everywhere, and foreign content in a table cell, the one context still parsed
    // with AngleSharp's fragment algorithm (the next test); every other context parses as the body does (checkpoint after 5.4)
    let isForeign (context: string) (html: string) =
        [ "<select"; yield! (if context = "td" then [ "<svg"; "<math" ] else []) ]
        |> List.exists (fun tag -> html.Contains(tag, StringComparison.OrdinalIgnoreCase))
    let differing = ResizeArray<string>()
    let firstDiffs = ResizeArray<string>()
    let mutable foreign = 0
    for context in contexts do
        for (label, html) in plainTexts @ [ for s in strings -> "string:" + s, s ] do
            // Foreign content and select in an element-context parse are differences recorded elsewhere
            if isForeign context html then
                foreign <- foreign + 1
            else
                let o = oldNodes context label
                let n = newNodes context html
                if o <> n then
                    differing.Add $"{context}/{label}"
                    if firstDiffs.Count < 4 then
                        let a = o.Split('\n')
                        let b = n.Split('\n')
                        let at = Seq.zip a b |> Seq.tryFindIndex (fun (x, y) -> x <> y) |> Option.defaultValue 0
                        firstDiffs.Add $"{context}/{label} line {at}:\n old {a[min at (a.Length - 1)]}\n new {b[min at (b.Length - 1)]}"
    let listed = String.Join("; ", differing |> Seq.truncate 20)
    Assert.True(foreign > 0, "no string with foreign content was set aside")
    Assert.True(differing.Count = 0, $"{differing.Count} differ: {listed}\n{String.Join('\n', firstDiffs)}")

[<Fact>]
let ``P4 an element-context parse breaks HTML out of foreign content as Gumbo does, except in a table cell`` () =
    // A context the fragment algorithm treats as the body is parsed as the body (linear, and <svg><p> breaks out as in Gumbo). A table
    // cell still goes through AngleSharp's fragment algorithm, which keeps the pre-2017 rule (design note S2): the pipeline never hands
    // it a message's own markup.
    let owner = nonNull (containerOf "").Owner
    let parsedIn (context: string) =
        let nodes = okP (Html5.parseIn (owner.CreateElement context) "<svg><p>y</p></svg>")
        let holder = owner.CreateElement "div"
        for node in nodes do
            Assert.Null node.Parent
            holder.AppendChild node |> ignore
        treeOf holder
    let brokenOut = "<http://www.w3.org/2000/svg svg >\n<http://www.w3.org/1999/xhtml p >\n #text \"y\"\n"
    for context in [ "div"; "p"; "li"; "figure"; "action-text-attachment"; "span" ] do
        Assert.Equal(brokenOut, parsedIn context)
    Assert.Equal("<http://www.w3.org/2000/svg svg >\n <http://www.w3.org/2000/svg p >\n  #text \"y\"\n", parsedIn "td")
    Assert.Equal("<svg></svg><p>y</p>", roundtrip "<svg><p>y</p></svg>")

[<Fact>]
let ``P5 an element-context parse of many top-level nodes is linear`` () =
    // ParseFragment moves its top-level nodes one at a time (a caption of 9,500 <b>s in plain text: 1.3 s before the checkpoint)
    let owner = nonNull (containerOf "").Owner
    let html = String.replicate 9_900 "x<br>"
    Html5.parseIn (owner.CreateElement "div") html |> ignore
    let started = CpuTimer()
    let nodes = okP (Html5.parseIn (owner.CreateElement "div") html)
    let elapsed = started.Elapsed
    Assert.Equal(19_800, nodes.Length)
    Assert.True(elapsed < TimeSpan.FromMilliseconds 150.0, $"9,900 x<br> in a div context took {elapsed}")

// P5-L4 below measure CPU time with the process's own timer (Support.CpuTimer), after one parse to compile everything.

let private warm () =
    Html5.parseBody "<p>see <b>x</b></p><table><tr><td>y</td></tr></table>" |> ignore
    Html5.parseBody (String.replicate 401 "<div>") |> ignore

let private timed (what: string) (limit: TimeSpan) (f: unit -> 'a) : 'a =
    warm ()
    let started = CpuTimer()
    let result = f ()
    Assert.True(started.Elapsed < limit, $"{what} took {started.Elapsed}, limit {limit}")
    result

let private ms (n: float) = TimeSpan.FromMilliseconds n

[<Fact>]
let ``P5 no step of parseBody is quadratic`` () =
    // 20,000 of either is over the 10,000-element cap (L3): refused at once, not parsed. Below the cap, and at its edge, the work
    // is the input's size: AngleSharp's ParseFragment would take 1.3 s here (design note S3).
    for (what, html) in
        [ "9,900 paragraphs", String.replicate 9_900 "<p>x</p>"
          "9,900 text and br", String.replicate 9_900 "x<br>"
          "9,900 top-level texts and comments", String.replicate 9_900 "x<!---->"
          "20,000 chunks of 1 KB text", String.replicate 20_000 (String('y', 1024)) ] do
        let r = timed what (bound (TimeSpan.FromSeconds 2.0) (ms 50.0)) (fun () -> Html5.parseBody html)
        Assert.True(r.IsOk, what)
    for (what, html) in [ "20,000 paragraphs", String.replicate 20_000 "<p>x</p>"; "20,000 text and br", String.replicate 20_000 "x<br>" ] do
        let r = timed what (bound (TimeSpan.FromSeconds 2.0) (ms 50.0)) (fun () -> Html5.parseBody html)
        Assert.Equal(Error TooManyElements, r |> Result.map ignore)

// --- L2-L4 -----------------------------------------------------------------------------------------------------------------------

/// The error `html` is refused with, and the least CPU time in three tries (the process's CPU time takes in the runtime's background
/// threads: tiered compilation, the collector after a 32 MB string).
let private refused (what: string) (limit: TimeSpan) (html: string) : ParseError =
    warm ()
    let mutable best = TimeSpan.MaxValue
    let mutable error = None
    for _ in 1..3 do
        let started = CpuTimer()
        let r = Html5.parseBody html
        best <- min best started.Elapsed
        match r with
        | Error e -> error <- Some e
        | Ok _ -> failwith $"{what} parsed"
    Assert.True(best < limit, $"{what} took {best}, limit {limit}")
    error.Value

[<Fact>]
let ``L2 the port's hostile shapes are refused or parsed within its bounds`` () =
    let quick = bound (TimeSpan.FromSeconds 5.0) (ms 100.0)
    Assert.Equal(TreeDepthExceeded, refused "400 KB of nested divs" quick (String.replicate 80_000 "<div>"))
    Assert.Equal(TreeDepthExceeded, refused "480 KB of a b" quick (String.replicate 80_000 "<a><b>"))
    Assert.Equal(TreeDepthExceeded, refused "390 KB of a div div" quick (String.replicate 30_000 "<a><div><div>"))
    let tooDeep = String.replicate 401 "<div>"
    let rest = String('x', 16 * 1024 * 1024)
    // The rest of a body is not read once it is too deep, so far as tokens go. AngleSharp reads a comment or a tag name whole before
    // any callback sees it (the port stopped inside the token), so those two cost the reading of 16 MB: about 100 ms.
    for (what, body, limit) in
        [ "tags", tooDeep + String.replicate (rest.Length / 6) "<a><b>", ms 100.0
          "a comment", tooDeep + "<!--" + rest, ms 250.0
          "a tag name", tooDeep + "<" + rest, ms 250.0 ] do
        Assert.Equal(TreeDepthExceeded, refused $"16 MB of {what}" (bound (TimeSpan.FromSeconds 1.0) limit) body)
    let attributes = [ for i in 1..64_000 -> $"a{i}=1" ]
    Assert.Equal(TooManyAttributes, refused "64,000 attributes" quick $"<b {String.Join(' ', attributes)}>x</b>")
    // 550 KB of <html> tags with 400 new attributes each: Rails renders the empty message; the cap refuses it (class B)
    let htmlTags =
        [ for tag in 0..199 ->
              let names = [ for i in 1 .. Html5.MaxAttributes -> $"a{tag * Html5.MaxAttributes + i}" ]
              $"<html {String.Join(' ', names)}>" ]
        |> String.concat ""
    Assert.True(htmlTags.Length > 500_000)
    Assert.Equal(TooManyRootAttributes, refused "550 KB of html tags" quick htmlTags)
    // 800 KB of <br> in a table
    // Foster parenting is the one quadratic step left, up to the element cap; the port's bound for it was one second
    Assert.Equal(TooManyElements, refused "800 KB of br in a table" (bound (TimeSpan.FromSeconds 5.0) (TimeSpan.FromSeconds 1.0)) ("<table>" + String.replicate 200_000 "<br>"))

[<Fact>]
let ``L3 the caps`` () =
    let quick = bound (TimeSpan.FromSeconds 2.0) (ms 50.0)
    // The worst a parse can cost under the caps: foster-parented elements
    let table = "<table>" + String.replicate 9_000 "<br>"
    Assert.True((timed "9,000 br after a table" (bound (TimeSpan.FromSeconds 5.0) (ms 400.0)) (fun () -> Html5.parseBody table)).IsOk)
    // Elements: 10,000 fit, 10,001 do not
    Assert.True((Html5.parseBody (String.replicate 10_000 "<b></b>")).IsOk)
    Assert.Equal(TooManyElements, refused "10,001 elements" quick (String.replicate 10_001 "<b></b>"))
    Assert.Equal(TooManyElements, refused "10,001 elements, nested" quick (String.replicate 10_001 "<i></i>" + "<b>"))
    // Tokens: 40,000 fit, 40,001 do not
    Assert.True((Html5.parseBody (String.replicate 40_000 "<!---->")).IsOk)
    Assert.Equal(TooManyTokens, refused "40,001 tokens" quick (String.replicate 40_001 "<!---->"))
    Assert.Equal(TooManyTokens, refused "160,000 ignored end tags" quick ("x" + String.replicate 160_000 "</q>x"))
    // Attributes over html and body tags: 400 fit, 401 do not
    let names (first: int) (last: int) = [ for i in first..last -> $"a{i}" ] |> String.concat " "
    Assert.True((Html5.parseBody $"<html {names 1 200}><body {names 201 400}>x").IsOk)
    Assert.Equal(TooManyRootAttributes, refused "401 root attributes" quick $"<html {names 1 200}><body {names 201 401}>x")
    Assert.Equal(TooManyRootAttributes, refused "root attributes over many tags" quick (String.replicate 300 "<html a=1 b=2>"))
    // One tag: 400 distinct fit (and equal ones count once), 401 do not
    Assert.True((Html5.parseBody $"<p {names 1 400}>x</p>").IsOk)
    Assert.Equal(TooManyAttributes, refused "401 attributes on a tag" quick $"<p {names 1 401}>x</p>")
    // A legitimate long message parses, and none of this leaves the next parse refused
    let paragraphs = String.replicate 8_000 "<p>some text</p>\\n"
    let rows = "<table>" + String.replicate 2_000 "<tr><td>a</td><td>b</td><td>c</td></tr>" + "</table>"
    for (what, html) in [ "8,000 paragraphs", paragraphs; "a 2,000-row table", rows ] do
        let r = timed what (bound (TimeSpan.FromSeconds 2.0) (ms 100.0)) (fun () -> Html5.parseBody html)
        Assert.True(r.IsOk, what)

[<Fact>]
let ``L3 the other shapes AngleSharp could be slow on stay bounded`` () =
    let quick = bound (TimeSpan.FromSeconds 5.0) (ms 400.0)
    // Whatever the outcome (parsed or refused), none may cost more than the caps allow
    for (what, html) in
        [ "10,000 nested templates", String.replicate 10_000 "<template>"
          "400 nested templates", String.replicate 400 "<template>"
          "40,000 flat svg", String.replicate 20_000 "<svg></svg>"
          "10,000 nested svg", String.replicate 10_000 "<svg>"
          "200,000 equal attributes", "<p " + String.replicate 200_000 "a=1 " + ">x</p>"
          "9,000 table cells misplaced", String.replicate 9_000 "<table><td>"
          "9,000 formatting reopened", "<table>" + String.replicate 4_000 "<b>x" + "</table>"
          "9,000 select options", "<select>" + String.replicate 9_000 "<option>x" + "</select>"
          "12,000 stray end tags", String.replicate 12_000 "</p>"
          "ignored tags splitting text", String.replicate 12_000 "x</q>"
          "nested tables", String.replicate 3_000 "<table><tr><td>" ] do
        let started = CpuTimer()
        let outcome = match Html5.parseBody html with Ok _ -> "parsed" | Error e -> e.Message
        let taken = started.Elapsed
        Assert.True(taken < quick, $"{what} ({outcome}) took {taken}")

[<Fact>]
let ``L4 the shared parser gives single-threaded results on 8 threads and keeps no document`` () =
    let bodies, _, seeds = inputs.Force()
    let mixed =
        [| for (_, b) in seeds @ bodies -> b
           yield String.replicate 401 "<div>"
           yield $"<p {numberedAttributes 1 401}>x</p>"
           yield String.replicate 12_000 "<b></b>"
           yield "<template><p>x</p></template>y" |]
    let answer (html: string) : string =
        match Html5.parseBody html with
        | Ok body -> Html5.innerHtml body
        | Error e -> $"refused: {e.Message}"
    let expected = mixed |> Array.map answer
    Assert.Contains(expected, fun e -> e.StartsWith "refused")
    for i in 1..2_000 do answer mixed[i % mixed.Length] |> ignore
    GC.Collect(2, GCCollectionMode.Forced, true, true)
    GC.WaitForPendingFinalizers()
    GC.Collect(2, GCCollectionMode.Forced, true, true)
    let before = GC.GetTotalMemory true
    let wrong = System.Collections.Concurrent.ConcurrentBag<string>()
    let work (thread: int) =
        for i in 0 .. 19_999 do
            let index = (i * 7 + thread * 131) % mixed.Length
            if answer mixed[index] <> expected[index] then wrong.Add $"thread {thread} input {index}"
    Parallel.For(0, 8, ParallelOptions(MaxDegreeOfParallelism = 8), fun thread -> work thread) |> ignore
    GC.Collect(2, GCCollectionMode.Forced, true, true)
    GC.WaitForPendingFinalizers()
    GC.Collect(2, GCCollectionMode.Forced, true, true)
    let after = GC.GetTotalMemory true
    Assert.True(wrong.IsEmpty, $"{wrong.Count} parses differ from the single-threaded ones, first: {Seq.tryHead wrong}")
    Assert.True(after - before < 5L * 1024L * 1024L, $"managed memory grew by {(after - before) / 1024L} KB")

// --- The DOM helpers -----------------------------------------------------------------------------------------------------------

[<Fact>]
let ``the DOM helpers`` () =
    let container = containerOf "<p id=a>x<b>y</b></p><!--c--><svg xlink:href=u><a>z</a></svg>"
    let names = Html5.descendants container |> List.map (fun n -> n.NodeName)
    Assert.Equal<string list>([ "P"; "#text"; "B"; "#text"; "#comment"; "svg"; "a"; "#text" ], names)
    let p = firstOf container
    Assert.Equal(ValueSome "a", Html5.attr p "id")
    Assert.Equal(ValueNone, Html5.attr p "class")
    let svg = Html5.descendants container |> List.find ((fun n -> Html5.isNamed n "svg"))
    Assert.Equal(ValueSome "u", Html5.attr svg "xlink:href")
    Assert.Equal(ValueNone, Html5.attr svg "href")
    Assert.True(Html5.isNamed p "p")
    Assert.False(Html5.isNamed p "b")
    Assert.False(Html5.isNamed (lastOf container) "text")
    // clone is deep and in the same document
    let copy = Html5.clone container
    Assert.True(obj.ReferenceEquals(nonNull copy.Owner, nonNull container.Owner))
    Assert.Equal(Html5.innerHtml container, Html5.innerHtml copy)
    Html5.setInnerHtml copy "<i>new</i>" |> okP
    Assert.Equal("<i>new</i>", Html5.innerHtml copy)
    Assert.StartsWith("<p id=\"a\">", Html5.innerHtml container)
    // inner_html= parses in the element's context
    let table = containerOf "<table><tbody><tr id=r></tr></tbody></table>"
    let row = Html5.descendants table |> List.find ((fun n -> Html5.isNamed n "tr")) :?> IElement
    Html5.setInnerHtml row "<td>cell</td>" |> okP
    Assert.Equal("<table><tbody><tr id=\"r\"><td>cell</td></tr></tbody></table>", Html5.innerHtml table)
    // replace(html) parses in the parent's context and puts the nodes where the node was
    let list = containerOf "<ul><li>a</li><li id=m>b</li><li>c</li></ul>"
    let middle = Html5.descendants list |> List.find (fun n -> Html5.attr n "id" = ValueSome "m")
    Html5.replaceWithHtml middle "<li>x</li><li>y</li>" |> okP
    Assert.Equal("<ul><li>a</li><li>x</li><li>y</li><li>c</li></ul>", Html5.innerHtml list)
    // toHtml: the node itself; a container's children
    Assert.Equal("<ul><li>a</li><li>x</li><li>y</li><li>c</li></ul>", Html5.toHtml (firstOf list))
    // A refusal leaves the tree alone
    Assert.Equal(Error TreeDepthExceeded, Html5.setInnerHtml row (String.replicate 401 "<div>"))
    Assert.Equal("<td>cell</td>", Html5.innerHtml row)

