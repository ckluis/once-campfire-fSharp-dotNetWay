// Port of rust/crates/richtext/tests/corpus.rs
//
// Differential test against the Rails pipeline: corpus/expected.json is produced by
// reference-tools/richtext/run.sh in the campfire-reference image. Every output is also checked
// against security properties that don't depend on the oracle.
module Campfire.RichText.Tests.CorpusTests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open Xunit
open Campfire.RichText
open Campfire.RichText.Unicode
open Campfire.RichText.Tests.Support
open Campfire.RichText.Tests.CorpusResolver
open Campfire.Tests

let private str (e: JsonElement) : string = e.GetString() |> nonNull

/// RICHTEXT_CORPUS points at a larger, uncommitted corpus (see reference-tools/richtext/run.sh)
let private corpusPath, private usesCommittedCorpus =
    match Environment.GetEnvironmentVariable "RICHTEXT_CORPUS" with
    | null
    | "" -> Repo.path "tests/Campfire.RichText.Tests/corpus/expected.json", true
    | path -> path, false

let private load () : JsonDocument =
    if not (File.Exists corpusPath) then failwith "run reference-tools/richtext/run.sh to generate the corpus"
    JsonDocument.Parse(File.ReadAllText corpusPath)

/// A DOM normalization for comparing outputs: AngleSharp's tree, whitespace-only text dropped and the rest collapsed, attributes sorted.
let private normalizedDom (html: string) : string =
    match Html5.parseBody html with
    | Error _ -> $"unparseable: {html}"
    | Ok root ->
        let out = StringBuilder()
        let rec walk (node: AngleSharp.Dom.INode) =
            for child in Html5.childrenOf node do
                match child with
                | :? AngleSharp.Dom.IText as text ->
                    let collapsed = text.Data.Split([| ' '; '\t'; '\n'; '\r'; '\u000c'; '\u000b' |], StringSplitOptions.RemoveEmptyEntries) |> String.concat " "
                    if collapsed.Length > 0 then out.Append collapsed |> ignore
                | :? AngleSharp.Dom.IElement as element ->
                    let attrs = [ for a in element.Attributes -> $"{Html5.qualifiedName a}={a.Value}" ] |> List.sort
                    out.Append($"<{element.LocalName} {attrs}>") |> ignore
                    walk element
                    out.Append($"</{element.LocalName}>") |> ignore
                | _ -> ()
        walk root
        out.ToString()

// --- Deliberate differences ----------------------------------------------------------------------

type private ScanState =
    | Text
    | Tag
    | Value

/// Rails' presentation as the port renders it on purpose (see "Known differences" in README.md):
/// `<` and `>` are escaped in attribute values, and the links rails_autolink inserted inside an
/// attribute value (its stored XSS) are left as the text they replaced; `name` attributes are dropped.
let private withPortDivergences (rails: string) : string =
    let insertedLink = "<a target=\"_blank\" href=\""
    let nameAttr = " name=\""
    let out = StringBuilder(rails.Length)
    let mutable state = Text
    let mutable pos = 0
    let startsWith (literal: string) = pos + literal.Length <= rails.Length && String.CompareOrdinal(rails, pos, literal, 0, literal.Length) = 0
    while pos < rails.Length do
        let c = rails[pos]
        if state = Value && startsWith insertedLink then
            let textStart = rails.IndexOf("\">", pos, StringComparison.Ordinal) + 2
            let textEnd = rails.IndexOf("</a>", textStart, StringComparison.Ordinal)
            out.Append(rails.Substring(textStart, textEnd - textStart).Replace(">", "&gt;")) |> ignore
            pos <- textEnd + 4
        elif state = Tag && startsWith nameAttr then
            let valueEnd = rails.IndexOf('"', pos + nameAttr.Length)
            pos <- valueEnd + 1
        else
            match state, c with
            | Text, '<' -> state <- Tag
            | Tag, '>' -> state <- Text
            | Tag, '"' -> state <- Value
            | Value, '"' -> state <- Tag
            | _ -> ()
            match state, c with
            | Value, '<' -> out.Append "&lt;" |> ignore
            | Value, '>' -> out.Append "&gt;" |> ignore
            | _ -> out.Append c |> ignore
            pos <- pos + 1
    out.ToString()

[<Fact>]
let ``port_divergences_apply_to_attribute_values_only`` () =
    Assert.Equal(
        "<p title=\"a&gt;b http://x.test/\">c > <a target=\"_blank\" href=\"http://y.test/\">y</a></p>",
        withPortDivergences
            "<p title=\"a>b <a target=\"_blank\" href=\"http://x.test/\">http://x.test/</a>\">c > <a target=\"_blank\" href=\"http://y.test/\">y</a></p>"
    )
    Assert.Equal("<a title=\"name=\">n</a>", withPortDivergences "<a name=\"x y\" title=\"name=\">n</a>")

// --- Security assertions -------------------------------------------------------------------------

let private dangerousElements =
    set
        [ "script"; "style"; "iframe"; "frame"; "frameset"; "object"; "embed"; "applet"; "base"; "meta"; "link"; "form"; "input"; "button"
          "textarea"; "select"; "svg"; "math"; "template"; "noscript"; "xmp"; "plaintext"; "noembed" ]

let private urlAttributes =
    set [ "href"; "src"; "action"; "formaction"; "poster"; "cite"; "background"; "xlink:href"; "srcset"; "data" ]

let private isControl (c: char) = Char.IsControl c

let private dangerousUrl (value: string) : bool =
    // What a browser would see: control characters and whitespace ignored, case folded
    let cleaned = Chars.toLowercase (String(value |> Seq.filter (fun c -> not (isControl c) && not (Chars.isWhitespace c)) |> Seq.toArray))
    if cleaned.StartsWith("javascript:", StringComparison.Ordinal) || cleaned.StartsWith("vbscript:", StringComparison.Ordinal) || cleaned.StartsWith("livescript:", StringComparison.Ordinal) then
        true
    elif cleaned.StartsWith("data:", StringComparison.Ordinal) then
        // A data: URL is only dangerous as markup or script; an unparseable media type is text/plain
        let rest = cleaned.Substring 5
        let mediatype = (((rest.Split ',').[0]).Split ';').[0]
        let token (s: string) = s.Length > 0 && s |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || "!#$%&'*+-.^_`|~".IndexOf c >= 0)
        let valid =
            match mediatype.IndexOf '/' with
            | -1 -> false
            | slash -> token (mediatype.Substring(0, slash)) && token (mediatype.Substring(slash + 1))
        valid && [ "html"; "xml"; "svg"; "script"; "ecmascript" ] |> List.exists (fun kind -> mediatype.Contains(kind, StringComparison.Ordinal))
    else
        false

/// Parses rendered output the way a browser would and returns the security violations in it.
let private securityViolationsWith (allowed: SafeList) (html: string) (allowStyle: bool) : string list =
    match Html5.parseBody html with
    | Error _ -> [ "unparseable output" ]
    | Ok root ->
        let violations = ResizeArray<string>()
        for node in Html5.descendants root do
            match node with
            | :? AngleSharp.Dom.IElement as element ->
                let name = element.LocalName
                if dangerousElements.Contains name then violations.Add $"<{name}> element"
                // Nothing auto_link inserts may escape its own sanitizer's allowlist
                if not (allowed.AllowsTag name) then violations.Add $"<{name}> not in the allowlist"
                for a in element.Attributes do
                    let attr = Html5.qualifiedName a
                    let value = a.Value
                    if not (allowed.AllowsAttribute attr) && attr <> "target" && not (attr = "style" && allowStyle) then
                        violations.Add $"{attr} on <{name}> not in the allowlist"
                    let lower = Chars.toLowercase attr
                    if lower.StartsWith("on", StringComparison.Ordinal) then violations.Add $"{attr} attribute on <{name}>"
                    if urlAttributes.Contains lower && dangerousUrl value then violations.Add $"{attr}=\"{value}\" on <{name}>"
                    if lower = "style" && not allowStyle then violations.Add $"style on <{name}>"
                    if lower.StartsWith("data-", StringComparison.Ordinal) && lower <> "data-language" then violations.Add $"{attr} on <{name}>"
            | _ -> ()
        List.ofSeq violations

let private securityViolations (html: string) (allowStyle: bool) : string list =
    securityViolationsWith SafeList.autoLink html allowStyle

/// The browser model (security review, plans/design-richtext.md section 11): Html5.parseBody is the pipeline's own parser, with
/// scripting off and as a body, so an output it reads one way and a browser another (mutation XSS) would pass the assertions above.
/// This parses the output as a page does: scripting on, inside `messages/_presentation`'s container, with a sentinel after it.
let private browser =
    AngleSharp.Html.Parser.HtmlParser(AngleSharp.Html.Parser.HtmlParserOptions(IsScripting = true), AngleSharp.BrowsingContext.New(AngleSharp.Configuration.Default))

let private browserViolationsWith (allowed: SafeList) (html: string) (allowStyle: bool) : string list =
    let page = browser.ParseDocument("<!DOCTYPE html><body><div id=\"x-container\">" + html + "</div><p id=\"x-after\">after</p></body>")
    let body = nonNull page.Body
    match page.GetElementById "x-container", page.GetElementById "x-after" with
    | null, _
    | _, null -> [ "the container or the sentinel is gone" ]
    | container, after ->
        let violations = ResizeArray<string>()
        let top = Html5.childrenOf body
        if top.Length <> 2 || not (obj.ReferenceEquals(top[0], container)) || not (obj.ReferenceEquals(top[1], after)) || after.TextContent <> "after" then
            violations.Add "escapes its container"
        // The same assertions, on what the browser built inside the container
        let inner = Html5.innerHtml container
        for v in securityViolationsWith allowed inner allowStyle do
            violations.Add $"as a browser parses it: {v}"
        for node in Html5.descendants container do
            match node with
            | :? AngleSharp.Dom.IComment -> violations.Add "as a browser parses it: comment"
            | :? AngleSharp.Dom.IElement as element when not (String.Equals(element.NamespaceUri, "http://www.w3.org/1999/xhtml", StringComparison.Ordinal)) ->
                violations.Add $"as a browser parses it: foreign <{element.LocalName}>"
            | _ -> ()
        List.ofSeq violations

let private browserViolations (html: string) : string list = browserViolationsWith SafeList.autoLink html false

// --- The comparison ------------------------------------------------------------------------------

/// `corpus/known-differences.json`: for each kind of answer, the corpus cases whose new answer differs from Rails' (loosely compared,
/// after the port's divergences), each with why. The comparison fails both ways.
let private knownDifferences (kind: string) : IDictionary<string, string> =
    use doc = JsonDocument.Parse(File.ReadAllText(Repo.path "tests/Campfire.RichText.Tests/corpus/known-differences.json"))
    let out = SortedDictionary<string, string>()
    match doc.RootElement.TryGetProperty kind with
    | true, entries ->
        for e in entries.EnumerateObject() do
            out[e.Name] <- e.Value.GetString() |> nonNull
    | _ -> ()
    out :> IDictionary<string, string>

type private Tally() =
    member val Exact = 0 with get, set
    member val DomEqual = 0 with get, set
    member val Mismatched = ResizeArray<string>()

    member this.Record(label: string, expected: string, actual: string) =
        if expected = actual then
            this.Exact <- this.Exact + 1
        elif normalizedDom expected = normalizedDom actual then
            this.DomEqual <- this.DomEqual + 1
            this.Mismatched.Add $"{label} (DOM-equal)\n  expected: {expected}\n  actual:   {actual}"
        else
            this.Mismatched.Add $"{label}\n  expected: {expected}\n  actual:   {actual}"

/// `outcome_str`: Error for a recorded exception, else the recorded string (None for nil).
let private outcome (v: JsonElement) : Result<string option, string> =
    match v.TryGetProperty "error" with
    | true, e -> Error(str e)
    | _ ->
        match (v.GetProperty "ok").ValueKind with
        | JsonValueKind.String -> Ok(Some(str (v.GetProperty "ok")))
        | _ -> Ok None

let private showOption (o: string option) : string =
    match o with
    | Some s -> $"Some({s})"
    | None -> "None"

let private tallyOf (tallies: IDictionary<string, Tally>) (kind: string) : Tally =
    match tallies.TryGetValue kind with
    | true, t -> t
    | _ ->
        let t = Tally()
        tallies[kind] <- t
        t

[<Fact>]
let ``corpus_matches_rails`` () =
    use corpus = load ()
    let json = corpus.RootElement
    let resolver = resolverOf json
    let tallies = SortedDictionary<string, Tally>()
    let security = ResizeArray<string>()
    let cases = json.GetProperty("cases").EnumerateArray() |> Seq.toArray

    for case in cases do
        let name = str (case.GetProperty "name")
        let body = str (case.GetProperty "body")
        let ctx =
            render
                resolver
                (match (case.TryGetProperty "host") with
                 | true, h when h.ValueKind = JsonValueKind.String -> Some(str h)
                 | _ -> None)

        // presentation: C1 and N2 below

        // plain text
        let actual = ActionText.toPlainText body ctx
        let label = $"[plain_text] {name}"
        match outcome (case.GetProperty "plain_text"), actual with
        | Ok e, Ok a -> (tallyOf tallies "plain_text").Record(label, defaultArg e "", a)
        | Error _, Error _ -> (tallyOf tallies "plain_text").Exact <- (tallyOf tallies "plain_text").Exact + 1
        | e, a -> (tallyOf tallies "plain_text").Record(label, $"{e}", $"{a}")

        // editable value
        let actual = ActionText.editableValue body ctx
        let label = $"[editable] {name}"
        // Deliberate: missing attachables leave the editor, where Rails raises.
        let missingInEditor =
            match (case.GetProperty "editable").TryGetProperty "message" with
            | true, m when m.ValueKind = JsonValueKind.String -> (str m).Contains("MissingAttachable", StringComparison.Ordinal)
            | _ -> false
        match outcome (case.GetProperty "editable"), actual with
        | Error _, Ok _ when missingInEditor -> (tallyOf tallies "editable").Exact <- (tallyOf tallies "editable").Exact + 1
        | Ok e, Ok a -> (tallyOf tallies "editable").Record(label, showOption e, showOption a)
        | Error _, Error _ -> (tallyOf tallies "editable").Exact <- (tallyOf tallies "editable").Exact + 1
        | e, a -> (tallyOf tallies "editable").Record(label, $"{e}", $"{a}")

        // mentioned users
        let actual = ActionText.mentionedUsers body ctx |> Result.map (List.map (fun u -> u.Id))
        let expected =
            match (case.GetProperty "mentioned").TryGetProperty "ok" with
            | true, ids -> Ok [ for i in ids.EnumerateArray() -> i.GetInt64() ]
            | _ -> Error()
        let label = $"[mentioned] {name}"
        match expected, actual with
        | Ok e, Ok a -> (tallyOf tallies "mentioned").Record(label, $"{e}", $"{a}")
        | Error _, Error _ -> (tallyOf tallies "mentioned").Exact <- (tallyOf tallies "mentioned").Exact + 1
        | _ -> (tallyOf tallies "mentioned").Record(label, $"{expected}", $"{actual}")

    // opengraph URL checks
    let webUrls = json.GetProperty("web_urls").EnumerateArray() |> Seq.toArray
    for url in webUrls do
        let value = str (url.GetProperty "value")
        let host = str (url.GetProperty "host")
        let actual = Attachables.webUrl (ValueSome value) host
        let label = $"[web_url] {value}"
        match outcome (url.GetProperty "result"), actual with
        | Ok e, Ok a -> (tallyOf tallies "web_url").Record(label, showOption e, showOption a)
        | Error _, Error _ -> (tallyOf tallies "web_url").Exact <- (tallyOf tallies "web_url").Exact + 1
        | e, a -> (tallyOf tallies "web_url").Record(label, $"{e}", $"{a}")

    let report = StringBuilder()
    let mutable failed = false
    for KeyValue(kind, tally) in tallies do
        let total = tally.Exact + tally.Mismatched.Count
        report.AppendLine $"{kind}: {total} cases, {tally.Exact} byte-identical, {tally.DomEqual} DOM-equal only, {tally.Mismatched.Count - tally.DomEqual} different" |> ignore
        // known-differences.json: a listed case may differ, and a listed case that no longer does fails
        let listed = knownDifferences kind
        let nameOf (m: string) =
            let first = m.Split('\n')[0]
            let first = first.Substring(first.IndexOf(']') + 2)
            if first.EndsWith " (DOM-equal)" then first.Substring(0, first.Length - " (DOM-equal)".Length) else first
        let differing = tally.Mismatched |> Seq.map nameOf |> Set.ofSeq
        for m in tally.Mismatched do
            if not (listed.ContainsKey(nameOf m)) then
                report.AppendLine $"  MISMATCH {m}" |> ignore
                failed <- true
        for name in listed.Keys do
            if usesCommittedCorpus && not (differing.Contains name) then
                report.AppendLine $"  STALE known difference {kind}: {name}" |> ignore
                failed <- true
    report.AppendLine $"security: {cases.Length} outputs checked, {security.Count} violations" |> ignore
    for v in security do
        report.AppendLine $"  VIOLATION {v}" |> ignore

    // Every case of the committed corpus runs through every check: a skipped one fails here.
    if usesCommittedCorpus then
        Assert.Equal(658, cases.Length)
        Assert.Equal(54, webUrls.Length)
        for kind in [ "plain_text"; "editable"; "mentioned" ] do
            Assert.Equal(658, (tallyOf tallies kind).Exact + (tallyOf tallies kind).Mismatched.Count)
        Assert.Equal(54, (tallyOf tallies "web_url").Exact + (tallyOf tallies "web_url").Mismatched.Count)
    Assert.True(security.Count = 0, $"security assertions failed\n{report}")
    Assert.False(failed, $"differences from the Rails pipeline\n{report}")

/// The oracle's own outputs must pass the security assertions too: two implementations can agree
/// on something unsafe. (They do: rails_autolink breaks out of attribute values, which is why the
/// port diverges there, so the assertions run on the output as the port means to render it.)
[<Fact>]
let ``rails_outputs_pass_security_assertions`` () =
    use corpus = load ()
    let violations = ResizeArray<string>()
    let mutable checkedOutputs = 0
    for case in corpus.RootElement.GetProperty("cases").EnumerateArray() do
        match outcome (case.GetProperty "presentation") with
        | Ok(Some html) ->
            checkedOutputs <- checkedOutputs + 1
            for v in securityViolations (withPortDivergences html) false do
                let name = str (case.GetProperty "name")
                violations.Add $"{name}: {v}"
        | _ -> ()
    Assert.True(checkedOutputs > 0)
    Assert.True(violations.Count = 0, String.Join("\n", violations))

/// The security gate has to catch what it's for, or passing it means nothing.
[<Fact>]
let ``security_assertions_catch_planted_defects`` () =
    for (html, what) in
        [ "<script>alert(1)</script>", "script element"
          "<p onclick=\"x()\">p</p>", "event handler"
          "<a href=\"java\tscript:alert(1)\">x</a>", "javascript URL"
          "<a href=\" JAVASCRIPT:alert(1)\">x</a>", "javascript URL"
          "<img src=\"data:text/html,<script>\">", "data URL"
          "<span style=\"color: red\">x</span>", "style attribute"
          "<svg><a xlink:href=\"javascript:1\">x</a></svg>", "svg"
          "<span data-controller=\"x\">x</span>", "data attribute"
          "<p title=\"a\" _blank\"=\"\">x</p>", "attribute outside the allowlist"
          "<details>x</details>", "element outside the allowlist" ] do
        Assert.True(not (List.isEmpty (securityViolations html false)), $"missed {what} in {html}")
    Assert.True(List.isEmpty (securityViolations "<p><a href=\"https://example.com\">x</a><img src=\"/a.png\"></p>" false))
    // What only the browser model sees: markup that leaves the message's container, and structure a browser rebuilds
    for (html, what) in
        [ "<p>x</p></div><p>outside</p>", "an end tag that closes the container"
          "<table><div><table><tr><td>x</td></tr></table></div></table><b>y</b>", "a div inside a table that closes the container"
          "<p title=\"</noscript><img src=x onerror=alert(1)>\"><noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript></p>", "a noscript a browser reads as text" ] do
        Assert.True(not (List.isEmpty (browserViolations html)), $"the browser model missed {what} in {html}")
    Assert.True(List.isEmpty (browserViolations "<p><a href=\"https://example.com\">x</a><img src=\"/a.png\"></p>"))


// --- Unit 5.5: the new presentation (C1, N1, N2) ---------------------------------------------------------------------------------

/// What Loofah strips must not survive: every element name, attribute name and URI attribute value of `ours` also occurs in `rails`.
let private strippedSurvivors (ours: string) (rails: string) : string list =
    let facts (html: string) =
        let elements = HashSet<string>()
        let attributes = HashSet<string>()
        let uris = HashSet<string>()
        match Html5.parseBody html with
        | Error _ -> ()
        | Ok root ->
            for node in Html5.descendants root do
                match node with
                | :? AngleSharp.Dom.IElement as element ->
                    elements.Add($"{element.NamespaceUri}|{element.LocalName}") |> ignore
                    for a in element.Attributes do
                        let name = Html5.qualifiedName a
                        attributes.Add name |> ignore
                        if urlAttributes.Contains(Chars.toLowercase name) then uris.Add $"{name}={a.Value}" |> ignore
                | _ -> ()
        elements, attributes, uris
    let (oe, oa, ou) = facts ours
    let (re, ra, ru) = facts rails
    [ for e in oe do if not (re.Contains e) then yield $"element {e}"
      for a in oa do if not (ra.Contains a) then yield $"attribute {a}"
      for u in ou do if not (ru.Contains u) then yield $"URI {u}" ]

/// Rails' presentation of a case as the port renders it on purpose, or None for an unrenderable one.
let private railsPresentation (case: JsonElement) : Presentation =
    match outcome (case.GetProperty "presentation") with
    | Ok html -> Presentation.Html(withPortDivergences (defaultArg html ""))
    | Error _ -> Presentation.Unrenderable

let private isMissingPartial (case: JsonElement) : bool =
    match case.TryGetProperty "presentation_raised_message" with
    | true, m when m.ValueKind = JsonValueKind.String -> (str m).Contains("to_missing_attachable_partial_path", StringComparison.Ordinal)
    | _ -> false

[<Fact>]
let ``C1 the corpus's presentations equal Rails' through known-differences.json, both ways`` () =
    use corpus = load ()
    let json = corpus.RootElement
    let resolver = resolverOf json
    let listed = knownDifferences "presentation"
    let differing = SortedDictionary<string, string>()
    let report = StringBuilder()
    let mutable exact = 0
    let mutable loose = 0
    let cases = json.GetProperty("cases").EnumerateArray() |> Seq.toArray
    for case in cases do
        let name = str (case.GetProperty "name")
        let body = str (case.GetProperty "body")
        let host = match case.TryGetProperty "host" with | true, h when h.ValueKind = JsonValueKind.String -> Some(str h) | _ -> None
        let ctx = render resolver host
        let actual = ActionText.presentMessage body ctx
        match railsPresentation case, actual with
        | Presentation.Html _, Presentation.Html a when isMissingPartial case ->
            // Deliberate: a deleted user's mention renders as ☒ where Rails raises
            Assert.True(a.Contains '☒', $"{name}: {a}")
            exact <- exact + 1
        | Presentation.Html e, Presentation.Html a ->
            if e = a then exact <- exact + 1
            elif normalizedDom e = normalizedDom a then loose <- loose + 1
            else
                differing[name] <- "differs"
                report.AppendLine $"{name}\n  rails: {e}\n  ours:  {a}" |> ignore
        | Presentation.Unrenderable, Presentation.Unrenderable -> exact <- exact + 1
        | e, a ->
            differing[name] <- "differs"
            report.AppendLine $"{name}\n  rails: {e}\n  ours:  {a}" |> ignore
    let unlisted = differing.Keys |> Seq.filter (fun n -> not (listed.ContainsKey n)) |> List.ofSeq
    let stale = listed.Keys |> Seq.filter (fun n -> not (differing.ContainsKey n)) |> List.ofSeq
    eprintfn "C1: %d cases, %d byte-equal, %d loose-equal, %d known differences" cases.Length exact loose differing.Count
    let unlistedNames = String.Join("; ", unlisted)
    let staleNames = String.Join("; ", stale)
    Assert.True(unlisted.IsEmpty, $"{unlisted.Length} unlisted differences: {unlistedNames}\n{report}")
    Assert.True(stale.IsEmpty, $"listed cases that no longer differ: {staleNames}")
    Assert.Equal(658, cases.Length)

[<Fact>]
let ``N1 nothing Loofah strips survives in the new presentations`` () =
    use corpus = load ()
    let json = corpus.RootElement
    let resolver = resolverOf json
    let violations = ResizeArray<string>()
    let mutable checkedCases = 0
    let known = knownDifferences "presentation"
    for case in json.GetProperty("cases").EnumerateArray() do
        let name = str (case.GetProperty "name")
        let body = str (case.GetProperty "body")
        let host = match case.TryGetProperty "host" with | true, h when h.ValueKind = JsonValueKind.String -> Some(str h) | _ -> None
        match railsPresentation case, ActionText.presentMessage body (render resolver host) with
        | Presentation.Html rails, Presentation.Html ours when not (isMissingPartial case) && not (known.ContainsKey name) ->
            checkedCases <- checkedCases + 1
            for v in strippedSurvivors ours rails do
                violations.Add $"{name}: {v}"
        | _ -> ()
    Assert.True(checkedCases > 600, $"{checkedCases} presentations compared")
    Assert.True(violations.Count = 0, String.Join("\n", violations))

[<Fact>]
let ``N1 catches planted survivors`` () =
    let rails = "<p><a href=\"https://example.com\" target=\"_blank\">x</a></p>"
    Assert.True(List.isEmpty (strippedSurvivors rails rails))
    for (ours, what) in
        [ "<p><script>x</script></p>", "an element"
          "<p onclick=\"x()\"><a href=\"https://example.com\" target=\"_blank\">x</a></p>", "an event attribute"
          "<p><a href=\"javascript:alert(1)\" target=\"_blank\">x</a></p>", "a javascript URL"
          "<p><a href=\"https://example.com/other\" target=\"_blank\">x</a></p>", "a URI Rails does not have"
          "<p><svg><a href=\"https://example.com\" target=\"_blank\">x</a></svg></p>", "a foreign element" ] do
        Assert.True(not (List.isEmpty (strippedSurvivors ours rails)), $"missed {what} in {ours}")

[<Fact>]
let ``N2 the security assertions hold for every new presentation and body.html`` () =
    use corpus = load ()
    let json = corpus.RootElement
    let resolver = resolverOf json
    let violations = ResizeArray<string>()
    let mutable checkedOutputs = 0
    for case in json.GetProperty("cases").EnumerateArray() do
        let name = str (case.GetProperty "name")
        let body = str (case.GetProperty "body")
        let host = match case.TryGetProperty "host" with | true, h when h.ValueKind = JsonValueKind.String -> Some(str h) | _ -> None
        let ctx = render resolver host
        match ActionText.presentMessage body ctx with
        | Presentation.Html html ->
            checkedOutputs <- checkedOutputs + 1
            for v in securityViolations html false @ browserViolations html do
                violations.Add $"{name}: {v}"
        | _ -> ()
        // The bot API's body.html: the content partial inside the layout, sanitized with Action Text's list
        match Content.load body ctx |> Result.bind (fun c -> Content.toRenderedHtmlWithLayout c ctx) with
        | Ok html ->
            checkedOutputs <- checkedOutputs + 1
            // Campfire's Action Text list allows `embed` (the unfurl's), and the depth-limit cases are refused
            for v in securityViolationsWith SafeList.actionText html true do
                if v <> "<embed> element" && v <> "unparseable output" then violations.Add $"{name} (body.html): {v}"
        | Error _ -> ()
    Assert.True(checkedOutputs > 1000, $"{checkedOutputs} outputs")
    Assert.True(violations.Count = 0, String.Join("\n", violations))

/// S7 (unit 5.6b): a seeded fuzz of the new presentation, including autolink. Hostile fragments are spliced into the corpus bodies and
/// into each other; every output is parsed again, by the pipeline's parser and by the browser model (scripting on, inside its
/// container: since the security review), and must stay inside its container and hold no
/// disallowed element or attribute, no foreign element, no comment, no event attribute and no dangerous URL. The larger run of 151,087
/// outputs belongs to the checkpoint's out-of-repo harness; this one keeps the oracle in the build.
[<Fact>]
let ``S7 fuzzed bodies present with no disallowed markup`` () =
    use corpus = load ()
    let json = corpus.RootElement
    let resolver = resolverOf json
    let bodies = [| for case in json.GetProperty("cases").EnumerateArray() -> str (case.GetProperty "body") |]
    let fragments =
        [| "<script>alert(1)</script>"; "<img src=x onerror=alert(1)>"; "<a href=\"javascript:alert(1)\">x</a>"; "<a href=\" jav&#x09;ascript:x\">x</a>"
           "<svg><a xlink:href=\"javascript:x\"><text>x</text></a></svg>"; "<math><mtext><table><mglyph><style><img src=x onerror=x>"
           "<!-- c --><!--[if x]>y<![endif]-->"; "<![CDATA[<img src=x onerror=x>]]>"; "<p title=\"a>b http://example.com/x\">t</p>"
           "<p title='\"onmouseover=\"x'>http://example.com/\"onmouseover=\"x</p>"; "http://example.com/<b>"; "www.example.com/\"><script>x()</script>"
           "me@example.com\"onclick=\"x"; "<a href=\"data:text/html,<script>x</script>\">d</a>"; "<iframe src=\"//evil\"></iframe>"
           "<form action=x><button formaction=y>z</button></form>"; "<select><option><b>x</b></option></select>"; "<textarea></textarea><img src=x onerror=x>"
           "<noscript><p title=\"</noscript><img src=x onerror=x>\">"; "<style>@import 'x'</style>"; "<table><caption><b>x</caption></table>"
           "<isindex>"; "&lt;script&gt;"; "<div style=\"background:url(javascript:x)\">s</div>"; "<a href=\"vbscript:x\">v</a>"
           "<action-text-attachment sgid=\"bad\" content-type=\"text/html\" content=\"<script>x</script>\"></action-text-attachment>"
           "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"javascript:x\" href=\"javascript:x\" content=\"<img src=x onerror=x>\"></action-text-attachment>"
           "https://example.com/(a)>"; "\u0000"; "<plaintext>"; "<xmp><img src=x onerror=x></xmp>"; "<template><script>x</script></template>"
           // The security review's (plans/design-richtext.md section 11): a body AngleSharp's tree builder throws on, structure a browser
           // rebuilds, and the classic mutation shapes
           "<p><select><xmp></xmp><select>x"; "<table><template><div><table><tr><td>x</td></tr></table></div></template></table>"
           "<b><table><tr><td></b>x</td></tr></table>"; "<li><figure><li>x</li></figure></li>"; "<a><template><a>x</a></template></a>"
           "<svg></p><style><a id=\"</style><img src=1 onerror=alert(1)>\">"; "<math><mtext><table><mglyph><style><!--</style><img title=\"--&gt;&lt;img src=1 onerror=alert(1)&gt;\">"
           "<form><math><mtext></form><form><mglyph><style></math><img src onerror=alert(1)>"; "<p title=\"<!--\">a</p><p title=\"--><img src=x onerror=x>\">b</p>"
           "<math><annotation-xml encoding=\"text/html\"><style><img src=x onerror=x></style></annotation-xml></math>"
           "<a title=\"&lt;/noscript&gt;&lt;img src=x onerror=x&gt;\">x</a>"; "<a href=\"javascript&colon;x\">c</a><a href=\"&#14;javascript:x\">d</a>"
           "<action-text-attachment content-type=\"image/png\" url=\"javascript://%0aalert(1)\" caption=\"<img src=x onerror=x>\"></action-text-attachment>"
           "<action-text-attachment content-type=\"text/html\" content=\"&lt;p&gt;&lt;select&gt;&lt;xmp&gt;&lt;/xmp&gt;&lt;select&gt;x\"></action-text-attachment>" |]
    let rng = Random(56)
    let violations = ResizeArray<string>()
    let mutable outputs = 0
    for i in 0 .. 19999 do
        let mutable body = bodies[rng.Next bodies.Length]
        for _ in 0 .. rng.Next(1, 4) do
            let at = rng.Next(body.Length + 1)
            body <- body.Insert(at, fragments[rng.Next fragments.Length])
        let ctx = render resolver None
        match ActionText.presentMessage body ctx with
        | Presentation.Html html ->
            outputs <- outputs + 1
            for v in securityViolations html false @ browserViolations html do
                violations.Add $"{i}: {v}"
            match Html5.parseBody html with
            | Ok root ->
                for node in Html5.descendants root do
                    if node :? AngleSharp.Dom.IComment then violations.Add $"{i}: comment"
            | Error _ -> ()
        | _ -> ()
    Assert.True(outputs > 15000, $"{outputs} outputs")
    Assert.True(violations.Count = 0, String.Join("\n", violations |> Seq.truncate 30))
