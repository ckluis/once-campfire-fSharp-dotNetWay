// Unit 5.2 of plans/design-richtext.md: the AngleSharp sanitizer (`Next.Sanitizer`) against the old arena walker (S1) and the
// `WebUtility.HtmlDecode` trial for `allowed_uri?` (S3). S2 (the Rails vectors) runs in SanitizerTests on both sanitizers.
module Campfire.RichText.Tests.NextSanitizerTests

open System
open System.Collections.Generic
open System.Net
open System.Text
open Xunit
open Campfire.RichText
open Campfire.RichText.Tests.Html5Tests
open Campfire.RichText.Tests.Support

let private lists =
    [ "defaults", SafeList.defaults; "actionText", SafeList.actionText; "contentFilter", SafeList.contentFilter; "autoLink", SafeList.autoLink ]

let private shown (r: Result<string, ParseError>) : string =
    match r with
    | Ok html -> html
    | Error e -> $"refused: {e.Message}"

[<Fact>]
let ``S1 the new sanitizer equals the old on the 1,479 inputs for all four lists, P1's list apart`` () =
    let bodies, presentations, seeds = inputs.Force()
    let all = bodies @ presentations @ seeds
    let differing = SortedDictionary<string, ResizeArray<string>>()
    let report = StringBuilder()
    let mutable compared = 0
    for (listName, list) in lists do
        for (label, html) in all do
            compared <- compared + 1
            let o = shown (Sanitizer.sanitize html list)
            let n = shown (Next.Sanitizer.sanitize html list)
            if o <> n then
                if not (differing.ContainsKey label) then differing[label] <- ResizeArray()
                differing[label].Add listName
                if not (p1Differences.Contains label) then
                    report.AppendLine($"{listName} {label}\n--- old\n{o}\n--- new\n{n}") |> ignore
    let unlisted = differing.Keys |> Seq.filter (fun l -> not (p1Differences.Contains l)) |> List.ofSeq
    Assert.Equal(4 * all.Length, compared)
    let names = String.Join("; ", unlisted)
    Assert.True(unlisted.IsEmpty, $"{unlisted.Length} unlisted: {names}\n{report}")
    // Not every name in P1's list need differ once sanitized (a difference can be unwrapped away), but the walker must not hide
    // a real one: report how many did
    eprintfn "S1: %d inputs x 4 lists = %d compared; %d differ, all on P1's list: %s" all.Length compared differing.Count (String.Join(", ", differing.Keys))

// --- S3 ------------------------------------------------------------------------------------------------------------------------

let private uriAttributes = set [ "action"; "cite"; "href"; "longdesc"; "poster"; "preload"; "src"; "xlink:href"; "xml:base" ]

/// Hostile and ordinary values beyond the corpus: the entity forms `CGI.unescapeHTML` and Loofah decode differently from the
/// framework, each alone and as the scheme of a `javascript:` URL.
let private hardening =
    let entities =
        [ "&#65;"; "&#X41;"; "&#00000000065;"; "&#0000000000000000000000065;"; "&#x000000041;"; "&#x0000000000000000000041;"
          "&#00000000000000000000000000000000106;avascript"; "&#0;"; "&#65535;"; "&#1114110;"; "&#1114111;"; "&#x10FFFF;"
          "&#x110000;"; "&#99999999999999999999;"; "&#18446744073709551615;"; "&#x10000000000000041;"; "&#;"; "&#x;"; "&#65"
          "&#0x41;"; "&amp;#65;"; "&amp;#0000000000038;#106;avascript:alert(1)"; "&#106"; "&#x6a"; "&#106avascript:x"
          "&amp;"; "&amp"; "&lt;"; "&gt;"; "&quot;"; "&apos;"; "&nbsp;"; "&Tab;"; "&NewLine;"; "&colon;"; "&Colon;"; "&sol;"
          "&AMP;"; "&Amp;"; "&amp;amp;"; "&ampx;"; "&#58"; "&#x3a"; "&#xD800;"; "&#55357;"; "&copy;"; "&eacute;" ]
    let schemes = [ "javascript"; "jav&#97;script"; "java&Tab;script"; "java&NewLine;script"; "data"; "vbscript"; "mailto"; "https" ]
    let separators = [ ":"; "&colon;"; "&#58;"; "&#x3a;"; "&amp;colon;"; "&amp;#58;"; "&#x3A"; "&#58"; "%3a"; "&#37;3a"; "&amp;#37;3a" ]
    [ yield! entities
      for e in entities do
          yield "javascript:" + e + "alert(1)"
          yield "javascript" + e + "alert(1)"
          yield e + "avascript:alert(1)"
      for s in schemes do
          for sep in separators do
              yield s + sep + "alert(1)"
              yield s + sep + "text/html,x"
              yield s + sep + "image/png;base64,xx"
      yield! [ "/rooms/1"; "https://example.com"; "data:image/png;base64,xx"; "data:text/html,xx"; ""; " "; "  javascript:alert(1)"; "#top"; "?q=1"
               "http://example.com/a?b=1&amp;c=2"; "http://example.com/a?b=1&c=2"; "mailto:me@example.com"; "x&#x00;y"; "&#x0;javascript:x" ] ]

/// Every URI attribute value of the corpus's bodies, Rails' presentations and the seed's bodies, in any namespace.
let private corpusValues () : string list =
    let bodies, presentations, seeds = inputs.Force()
    [ for (_, html) in bodies @ presentations @ seeds do
          match Next.Html5.parseBody html with
          | Error _ -> ()
          | Ok body ->
              for node in Next.Html5.descendants body do
                  match node with
                  | :? AngleSharp.Dom.IElement as e ->
                      for a in e.Attributes do
                          if uriAttributes.Contains(Next.Html5.qualifiedName a) then yield a.Value
                  | _ -> () ]

[<Fact>]
let ``S3 URI verdicts: Loofah.allowedUri is the old code, and the WebUtility.HtmlDecode trial is counted`` () =
    let fromCorpus = corpusValues ()
    let values = (fromCorpus @ hardening) |> List.distinct
    let mutable oldVsLoofah = 0
    let decodeDiffers = ResizeArray<string>()
    let verdictDiffers = ResizeArray<string>()
    for v in values do
        if Sanitizer.allowedUri v <> Loofah.allowedUri v then oldVsLoofah <- oldVsLoofah + 1
        if Loofah.cgiUnescapeHtml v <> WebUtility.HtmlDecode v then decodeDiffers.Add v
        if Loofah.allowedUri v <> Loofah.allowedUriUsing WebUtility.HtmlDecode v then verdictDiffers.Add v
    eprintfn "S3: %d URI attribute values from the corpus (%d distinct) + %d hardening inputs = %d distinct; old vs Loofah verdicts differing: %d; HtmlDecode output differs on %d, verdict on %d: %s"
        fromCorpus.Length (List.distinct fromCorpus).Length hardening.Length values.Length oldVsLoofah decodeDiffers.Count verdictDiffers.Count
        (String.Join(" | ", verdictDiffers |> Seq.truncate 12))
    Assert.Equal(0, oldVsLoofah)
    Assert.True(fromCorpus.Length > 100, $"only {fromCorpus.Length} corpus URI values")
    // The trial's outcome is recorded in plans/dotnetway.md: HtmlDecode decodes every named entity (`&nbsp;avascript:`), where
    // CGI.unescapeHTML leaves all but five, so the verdicts differ and `Loofah.cgiUnescapeHtml` stays. If this fails, the
    // trial would now pass and the replacement could be made.
    Assert.True(verdictDiffers.Count > 0, "HtmlDecode now gives every verdict CGI.unescapeHTML does")

/// Unit 5.1's finding (d): AngleSharp still expands `<isindex>` as the 2008 standard did, where Gumbo (and browsers) keep an
/// unknown element, which every list unwraps to nothing. The expansion is recorded as a class B difference (known-differences in
/// 5.6), pinned here so a change in AngleSharp shows: the form, label and input go by the lists, the sentence and the `hr`s stay.
[<Fact>]
let ``isindex expands as in the 2008 standard: recorded difference`` () =
    Assert.Equal("", okP (Sanitizer.sanitize "<isindex>" SafeList.defaults))
    Assert.Equal(
        "<hr>This is a searchable index. Enter search keywords: <hr>",
        okP (Next.Sanitizer.sanitize "<isindex>" SafeList.defaults)
    )
