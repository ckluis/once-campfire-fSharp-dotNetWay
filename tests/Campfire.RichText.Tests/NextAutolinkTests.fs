// Unit 5.5 of plans/design-richtext.md: rails_autolink on AngleSharp nodes (`Next.Autolink`) and the presentation built on it.
// M1 (the presentation against the old pipeline on the 1,479 inputs), A1 (the linker's cases, `pre` and `code` included), A2 (the
// attribute-value breakouts against the new linker) and A3 (linking adds no parse). C1, N1 and N2 are in CorpusTests (they use its
// oracle helpers); the wide cases of the linear-time bound are in NextContentTests.
module Campfire.RichText.Tests.NextAutolinkTests

open System
open Xunit
open Campfire.RichText
open Campfire.RichText.Tests.Html5Tests
open Campfire.RichText.Tests.NextContentTests
open Campfire.RichText.Tests.Support

let private ctx = render (NoRecords()) (Some "once.campfire.test")

let private presentationFull (body: string) : string = okR (Next.ActionText.messagePresentation body ctx)

/// The presentation without the layout's wrapper.
let private presentation (body: string) : string =
    let full = presentationFull body
    let opening = "<div class=\"lexxy-content\">\n  "
    let closing = "\n</div>\n"
    Assert.True(full.StartsWith(opening, StringComparison.Ordinal) && full.EndsWith(closing, StringComparison.Ordinal), full)
    full.Substring(opening.Length, full.Length - opening.Length - closing.Length)

let private firstElement (html: string) (selector: string) : AngleSharp.Dom.IElement =
    match Next.Html5.parseBody html with
    | Ok root -> nonNull (root.QuerySelector selector)
    | Error e -> failwith e.Message

let private contains (haystack: string) (needle: string) : bool = haystack.Contains(needle, StringComparison.Ordinal)

let private countOf (haystack: string) (needle: string) : int =
    let mutable count = 0
    let mutable i = haystack.IndexOf(needle, StringComparison.Ordinal)
    while i >= 0 do
        count <- count + 1
        i <- haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal)
    count

let private presentationOld (body: string) (c: RenderContext) : string = ActionText.messagePresentation body c |> shownR
let private presentationNew (body: string) (c: RenderContext) : string = Next.ActionText.messagePresentation body c |> shownR

/// The presentation as the loose DOM sees it: the old pipeline escapes `<` and `>` in attribute values and the new one writes them
/// as the spec does, so compare the trees the two strings parse to.
let private looseOf (html: string) : string =
    match Next.Html5.parseBody html with
    | Error e -> $"refused: {e.Message}"
    | Ok body -> Html5Tests.treeOf body

[<Fact>]
let ``M1 presentations equal the old pipeline's on the 1,479, P1's list apart`` () =
    let compared =
        compareWith
            foreignContextDifferences
            "M1 presentation"
            (fun _ -> true)
            (fun body c -> looseOf (presentationOld body c))
            (fun body c -> looseOf (presentationNew body c))
    Assert.True(compared >= 1315, $"{compared} inputs")

/// The presentation's bytes too, with the escaping of `<` and `>` in attribute values the one difference allowed.
[<Fact>]
let ``M1 presentations differ from the old pipeline's bytes only in attribute-value escaping`` () =
    let mutable compared = 0
    let mutable different = 0
    for (label, body, c) in all.Force() do
        if not (p1Differences.Contains label || foreignContextDifferences.Contains label) then
            compared <- compared + 1
            let o = presentationOld body c
            let n = presentationNew body c
            if o <> n then
                different <- different + 1
                // Same tree is M1 above; here the bytes may differ only where the old serializer wrote &lt; or &gt; in a value
                Assert.Equal(looseOf o, looseOf n)
    eprintfn "M1 bytes: %d compared, %d differ in bytes (equal as trees)" compared different

// --- A1 ------------------------------------------------------------------------------------------------------------------------

let private link (href: string) (text: string) = $"<a target=\"_blank\" href=\"{href}\">{text}</a>"

[<Fact>]
let ``A1 urls and addresses in text become links`` () =
    Assert.Equal(
        "<p>see " + link "http://example.com/a?b=1&amp;c=2" "http://example.com/a?b=1&amp;c=2" + " and " + link "mailto:me@example.com" "me@example.com" + "</p>",
        presentation "<p>see http://example.com/a?b=1&amp;c=2 and me@example.com</p>"
    )
    Assert.Equal("<p>" + link "http://www.a.com" "www.a.com" + "</p>", presentation "<p>www.a.com</p>")
    Assert.Equal("<p>" + link "HTTP://A.COM/x" "HTTP://A.COM/x" + "</p>", presentation "<p>HTTP://A.COM/x</p>")

[<Fact>]
let ``A1 urls inside pre and code are linked, as rails_autolink links them`` () =
    let html = presentation "<pre>run http://a.test/x now</pre><p><code>www.b.test</code></p>"
    Assert.Equal(
        "<pre>run " + link "http://a.test/x" "http://a.test/x" + " now</pre><p><code>" + link "http://www.b.test" "www.b.test" + "</code></p>",
        html
    )

[<Fact>]
let ``A1 nothing inside an a is linked`` () =
    let html = presentation "<p><a href=\"https://x.test\">see http://a.test and me@a.test <b>www.c.test</b></a> http://d.test</p>"
    Assert.Equal(2, countOf html "<a ")
    Assert.True(contains html "see http://a.test and me@a.test <b>www.c.test</b></a>", html)
    Assert.True(contains html (link "http://d.test" "http://d.test"), html)

[<Fact>]
let ``A1 trailing punctuation and brackets stay outside the link as in rails_autolink`` () =
    Assert.Equal("<p>(" + link "http://a.test/x" "http://a.test/x" + ")</p>", presentation "<p>(http://a.test/x)</p>")
    Assert.Equal("<p>" + link "http://a.test/(x)" "http://a.test/(x)" + ".</p>", presentation "<p>http://a.test/(x).</p>")
    Assert.Equal("<p>" + link "http://a.test/x" "http://a.test/x" + ",!?</p>", presentation "<p>http://a.test/x,!?</p>")
    Assert.Equal("<p>&lt;" + link "http://a.test/x" "http://a.test/x" + "&gt;</p>", presentation "<p>&lt;http://a.test/x&gt;</p>")

[<Fact>]
let ``A1 a url may hold an ampersand, a non-breaking space and a quote mark`` () =
    // `&nbsp;` is part of the escaped form, so the URL runs on through it; the href holds the character
    let html = presentation "<p>http://a.test/x&amp;y\u00A0z 'q'</p>"
    let a = firstElement html "a"
    Assert.Equal("http://a.test/x&y\u00A0z", a.GetAttribute "href")
    Assert.Equal("http://a.test/x&y\u00A0z", a.TextContent)

[<Fact>]
let ``A1 addresses need a local part, an at sign and a dotted domain`` () =
    Assert.Equal("<p>a@b and @b.test and a@b.</p>", presentation "<p>a@b and @b.test and a@b.</p>")
    Assert.Equal("<p>" + link "mailto:a.b%2Bc@d-e.test" "a.b+c@d-e.test" + "</p>", presentation "<p>a.b+c@d-e.test</p>")
    Assert.Equal("<p>x " + link "mailto:y@z.test" "y@z.test" + "</p>", presentation "<p>x y@z.test</p>")

[<Fact>]
let ``A1 the sanitizer runs first, so a forbidden element is gone and its text still links`` () =
    let html = presentation "<p><script>alert(1)</script>http://a.test<style>p{}</style></p>"
    Assert.False(contains html "<script", html)
    Assert.True(contains html (link "http://a.test" "http://a.test"), html)

// --- A2 ------------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``A2 a url after a greater-than sign in an attribute cannot break out of it`` () =
    let body = "<p title=\"x> http://evil.test/ <img src=x onerror=alert(1)>\">hi</p>"
    let html = presentation body
    Assert.Equal<string list>([ "p"; "p[title]" ], parsedMarkup html)
    let p = firstElement html "p"
    Assert.Equal("x> http://evil.test/ <img src=x onerror=alert(1)>", p.GetAttribute "title")
    Assert.Equal("hi", p.TextContent)

[<Fact>]
let ``A2 an address after a greater-than sign in an attribute cannot break out of it`` () =
    let html = presentation "<p><abbr title=\"x> me@evil.test <img src=x onerror=alert(1)>\">hi</abbr></p>"
    Assert.Equal<string list>([ "p"; "abbr"; "abbr[title]" ], parsedMarkup html)
    Assert.Equal("x> me@evil.test <img src=x onerror=alert(1)>", (firstElement html "abbr").GetAttribute "title")

[<Fact>]
let ``A2 a title holding a quote, a tag and a url keeps its value`` () =
    let value = "\"><img src=x onerror=alert(1)> http://evil.test/ me@evil.test"
    let body = "<p><span title=\"" + value.Replace("&", "&amp;").Replace("\"", "&quot;") + "\">hi</span></p>"
    let html = presentation body
    Assert.Equal<string list>([ "p"; "span"; "span[title]" ], parsedMarkup html)
    Assert.Equal(value, (firstElement html "span").GetAttribute "title")

[<Fact>]
let ``A2 text that looks like markup is text, and a link text cannot carry markup`` () =
    let html = presentation "<p>&lt;a href=&quot;http://x.test&quot;&gt;http://y.test/&lt;img src=x onerror=alert(1)&gt;</p>"
    // The `<img` is in an attribute value and in text, never a tag
    let markup = parsedMarkup html
    Assert.False(markup |> List.exists (fun m -> m = "img" || m.Contains "onerror"), html)

// --- A3 ------------------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``A3 linking adds no parse`` () =
    let bodies =
        [ "<p>see http://example.com/a and me@example.com</p><pre>www.x.test</pre>"
          String.replicate 50 "<p>http://a.test/x <b>y@z.test</b></p>"
          "<p>nothing to link here</p>" ]
    for body in bodies do
        let sanitizeOnly () =
            let before = Next.Html5.parseCount ()
            Next.Sanitizer.sanitize body SafeList.autoLink |> ignore
            Next.Html5.parseCount () - before
        let linked () =
            let before = Next.Html5.parseCount ()
            Next.Autolink.autoLink body SafeList.autoLink |> ignore
            Next.Html5.parseCount () - before
        let a = sanitizeOnly ()
        let b = linked ()
        Assert.Equal(1L, a)
        Assert.Equal(a, b)

[<Fact>]
let ``A3 linking a seed body costs the parses of its render and no more`` () =
    let body = "<p>see http://example.com/a and me@example.com</p>"
    let renderOnly () =
        let before = Next.Html5.parseCount ()
        Next.Content.load body ctx
        |> Result.bind (fun c -> Next.Filters.apply c ctx)
        |> Result.bind (fun c -> Next.Content.toRenderedHtmlWithLayout c ctx)
        |> ignore
        Next.Html5.parseCount () - before
    let presented () =
        let before = Next.Html5.parseCount ()
        Next.ActionText.messagePresentation body ctx |> ignore
        Next.Html5.parseCount () - before
    // The one parse the sanitizer needs is the linker's; nothing else
    Assert.Equal(renderOnly () + 1L, presented ())

// --- The segments --------------------------------------------------------------------------------------------------------------

[<Fact>]
let ``linkText cuts an escaped text into decoded text and links`` () =
    let segments (s: string) = okP (Next.Autolink.linkText s)
    Assert.Equal<Next.Segment list>([ Next.Plain "a & b" ], segments "a &amp; b")
    Assert.Equal<Next.Segment list>(
        [ Next.Plain "x "; Next.Linked("http://a.test/?q=1&b", "http://a.test/?q=1&b"); Next.Plain ", y" ],
        segments "x http://a.test/?q=1&amp;b, y"
    )
    Assert.Equal<Next.Segment list>(
        [ Next.Linked("mailto:a@b.test", "a@b.test"); Next.Plain " < " ; Next.Linked("http://www.c.test", "www.c.test") ],
        segments "a@b.test &lt; www.c.test"
    )

[<Fact>]
let ``a lone surrogate or NUL in a link goes through the sanitizer as the old linker did`` () =
    let html = presentation "<p>http://a.test/x\u0000y</p>"
    Assert.True(contains html "http://a.test/xy", html)

// --- Linear time (the bound, 400 ms CPU, is also run in NextContentTests' wide-bodies test) ------------------------------------

[<Fact>]
let ``wide bodies link every url and address within the bound`` () =
    let cases =
        [ "9,900 urls, one per paragraph", String.concat "" [ for i in 1..9_900 -> $"<p>http://a.test/{i}</p>" ], "<a target=\"_blank\" href=\"http://a.test/", 9_900
          "9,900 urls among text nodes", String.concat "" [ for i in 1..9_900 -> $"see http://a.test/{i}<br>" ], "<a target=\"_blank\" href=\"http://a.test/", 9_900
          "9,900 addresses, one per paragraph", String.concat "" [ for i in 1..9_900 -> $"<p>u{i}@a.test</p>" ], "<a target=\"_blank\" href=\"mailto:", 9_900
          "9,900 addresses among text nodes", String.concat "" [ for i in 1..9_900 -> $"mail u{i}@a.test<br>" ], "<a target=\"_blank\" href=\"mailto:", 9_900
          "one text node of 16,000 urls", "<p>" + String.concat " " [ for i in 1..16_000 -> $"http://a.test/{i}" ] + "</p>", "<a target=\"_blank\" href=\"http://a.test/", 16_000
          "one text node of 16,000 addresses", "<p>" + String.concat " " [ for i in 1..16_000 -> $"u{i}@a.test" ] + "</p>", "<a target=\"_blank\" href=\"mailto:", 16_000 ]
    presentation "<p>warm http://a.test u@a.test</p>" |> ignore
    let timings = ResizeArray<string>()
    let mutable slowest = TimeSpan.Zero
    for (what, body, needle, expected) in cases do
        presentation body |> ignore
        let started = CpuTimer()
        let html = presentation body
        let elapsed = started.Elapsed
        Assert.Equal(expected, countOf html needle)
        timings.Add $"{what}: {elapsed.TotalMilliseconds:F0} ms"
        if elapsed > slowest then slowest <- elapsed
    let limit = bound (TimeSpan.FromSeconds 2.0) (TimeSpan.FromMilliseconds 400.0)
    Assert.True(slowest < limit, String.Join("; ", timings))

