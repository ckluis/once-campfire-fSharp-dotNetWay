// W2 (plans/design-views.md section 5): tests/Shared/LooseDom.fs, the F# port of parity/capture/loose.ts over AngleSharp, equals what
// loose.ts itself made of every Rails golden page and of the stored oracle answers (24 since 4.11 dropped the writer helpers' cases; stored in tests/Campfire.Views.Tests/loose/ by
// parity/bin/loose-golden), line for line; plus the rules of loose.ts's own self-test (parity/capture/test/loose.test.ts).
module Campfire.Views.Tests.LooseDomTests

open System
open System.IO
open System.Text.Json
open Xunit
open Campfire.Tests

let private looseDir = Repo.path "tests/Campfire.Views.Tests/loose"

/// The options loose-golden.ts used: the harness's seed clock.
let private options = { LooseDom.defaultOptions with SeedTime = Some(DateTimeOffset.Parse("2026-03-02T16:00:00Z").ToUnixTimeMilliseconds()) }

let private check (label: string) (stored: string) (markup: string) =
    match LooseDom.diff stored (LooseDom.normalizeWith options markup) with
    | Some report -> failwith $"{label}: the port differs from loose.ts\n{report}"
    | None -> ()

[<Fact>]
let ``loose dom of every rails golden page equals loose.ts`` () =
    let stored = Directory.GetFiles(Path.Combine(looseDir, "a"), "*.txt") |> Array.sort
    Assert.True(stored.Length >= 80, $"only {stored.Length} stored golden pages")
    let htmlGoldens = Directory.GetFiles(Facts.goldenDir, "*.html").Length
    Assert.Equal(htmlGoldens, stored.Length)
    for file in stored do
        let name = Path.GetFileNameWithoutExtension file
        check name (File.ReadAllText file) (File.ReadAllText(Path.Combine(Facts.goldenDir, name + ".html")))

[<Fact>]
let ``loose dom of the stored oracle answers equals loose.ts`` () =
    let picked = JsonDocument.Parse(File.ReadAllText(Path.Combine(looseDir, "oracle", "index.json"))).RootElement
    Assert.Equal(Directory.GetFiles(Path.Combine(looseDir, "oracle"), "*.txt").Length, picked.GetArrayLength())
    let lines = File.ReadAllLines(Repo.path "tests/Campfire.Views.Tests/oracle.jsonl")
    let mutable n = 0
    for pick in picked.EnumerateArray() do
        n <- n + 1
        let row = JsonDocument.Parse(lines[pick.GetProperty("line").GetInt32() - 1]).RootElement
        let case = row.GetProperty "case"
        let lowercase =
            match case.TryGetProperty "args" with
            | true, a when a.ValueKind = JsonValueKind.Object ->
                match a.TryGetProperty "name" with
                | true, name -> name.GetString() = "to_lowercase"
                | _ -> false
            | _ -> false
        let op = nonNull (case.GetProperty("op").GetString()) + (if lowercase then ":to_lowercase" else "")
        Assert.Equal(nonNull (pick.GetProperty("op").GetString()), op)
        let answer = row.GetProperty("rust").GetProperty("out").GetString() |> Option.ofObj |> Option.defaultValue ""
        check $"oracle {n} ({op})" (File.ReadAllText(Path.Combine(looseDir, "oracle", (n.ToString("00") + ".txt")))) answer

// ---- the rules (loose.test.ts) -----------------------------------------------------------------------------------------------

let private loose (body: string) = LooseDom.normalizeWith options $"<body>{body}</body>"
let private same (a: string) (b: string) = loose a = loose b

[<Fact>]
let ``a lost or changed hook is a difference`` () =
    let rails = """<div id="m" class="message" data-controller="messages" data-message-id="7" data-action="click->messages#open">hi</div>"""
    let ours = """<div id="m" class="message" data-controller="messages" data-action="click->messages#open">hi</div>"""
    Assert.False(same rails ours)
    Assert.Contains("data-message-id=\"7\"", loose rails)
    let b = """<turbo-frame id="f" target="_top"><button data-action="a#b" aria-label="Send" role="button">x</button></turbo-frame>"""
    for changed in [ b.Replace(" id=\"f\"", ""); b.Replace("target=\"_top\"", "target=\"_self\""); b.Replace(" data-action=\"a#b\"", " data-action=\"a#c\"")
                     b.Replace(" aria-label=\"Send\"", ""); b.Replace(" role=\"button\"", "") ] do
        Assert.False(same b changed, changed)

[<Fact>]
let ``byte level differences are not differences`` () =
    Assert.True(same """<a href="/x" id="a" data-turbo="false" class="btn">go</a>""" """<a class="btn" data-turbo="false" id="a" href="/x">go</a>""")
    Assert.True(same """<div class="b a  c"></div>""" """<div class="c a b"></div>""")
    Assert.True(same """<p title="a &amp; b">Tom &amp; Jerry &#8212; &quot;x&quot;</p><br>""" """<p title='a & b'>Tom & Jerry — "x"</p><br/><!-- c -->""")
    Assert.True(same "<input disabled name=q>" """<input name="q" disabled="">""")
    Assert.True(same """<p title="é 😀">é</p>""" """<p title="&#233; &#128512;">&#233;</p>""")

[<Fact>]
let ``whitespace that renders is a difference and whitespace that does not is not`` () =
    // between inline-level siblings (text, a, span, a custom element) a space renders
    Assert.False(same "<a>x</a> <a>y</a>" "<a>x</a><a>y</a>")
    Assert.False(same "<turbo-frame></turbo-frame> <turbo-frame></turbo-frame>" "<turbo-frame></turbo-frame><turbo-frame></turbo-frame>")
    Assert.False(same "<span>a</span> Bender" "<span>a</span>Bender")
    // between blocks it does not
    Assert.True(same "<div>a</div>\n  <div>b</div>" "<div>a</div><div>b</div>")
    // inside pre and textarea every character renders
    Assert.False(same "<pre>a  b</pre>" "<pre>a b</pre>")
    Assert.False(same "<textarea>\n\nx</textarea>" "<textarea>\nx</textarea>")

[<Fact>]
let ``children of a custom element stay inside it`` () =
    // T2: a self-closed custom element is not void in HTML and swallows its siblings, so the trees differ
    Assert.False(same "<turbo-frame id=\"a\"></turbo-frame><p>x</p>" "<turbo-frame id=\"a\"/><p>x</p>")
    Assert.Contains("  <p>", loose "<turbo-frame id=\"a\"/><p>x</p>")

[<Fact>]
let ``forgery tokens are left out and volatile values masked`` () =
    Assert.True(same """<form><input type="hidden" name="authenticity_token" value="abc">x</form>""" "<form>x</form>")
    Assert.True(same """<script nonce="a">1</script>""" """<script nonce="b">1</script>""")
    Assert.Contains("/assets/app-«digest».js", loose """<script src="/assets/app-0123456789abcdef.js"></script>""")
