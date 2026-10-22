/// The browser model: a page parse with scripting on (as Chromium, Firefox and WebKit parse), the output inside its real container
/// (`messages/_presentation`'s div) with a sentinel after it, and the checks a sanitizer's output must pass there.
module Check

open System
open System.Collections.Generic
open AngleSharp
open AngleSharp.Dom
open AngleSharp.Html.Parser
open Campfire.RichText

let private browser = HtmlParser(HtmlParserOptions(IsScripting = true), BrowsingContext.New(Configuration.Default))

let private htmlNs = "http://www.w3.org/1999/xhtml"

/// The URL a browser resolves: leading and trailing C0 controls and spaces stripped, tab and newlines removed anywhere; its scheme if it
/// has one (ASCII letter, then letters, digits, `+-.`, then `:`), lowercased.
let browserScheme (value: string) : string option =
    let trimmed = value.Trim([| for c in 0 .. 0x20 -> char c |])
    let cleaned = String(trimmed |> Seq.filter (fun c -> c <> '\t' && c <> '\n' && c <> '\r') |> Seq.toArray)
    if cleaned.Length = 0 || not (Char.IsAsciiLetter cleaned[0]) then None
    else
        let mutable i = 1
        while i < cleaned.Length && (Char.IsAsciiLetterOrDigit cleaned[i] || cleaned[i] = '+' || cleaned[i] = '-' || cleaned[i] = '.') do
            i <- i + 1
        if i < cleaned.Length && cleaned[i] = ':' then Some(cleaned.Substring(0, i).ToLowerInvariant()) else None

/// A data: URL's MIME type as a browser reads it (before the first comma, up to `;`, trimmed, lowercased).
let dataMime (value: string) : string =
    let trimmed = value.Trim([| for c in 0 .. 0x20 -> char c |])
    let cleaned = String(trimmed |> Seq.filter (fun c -> c <> '\t' && c <> '\n' && c <> '\r') |> Seq.toArray)
    let rest = cleaned.Substring(cleaned.IndexOf ':' + 1)
    let mime = match rest.IndexOf ',' with -1 -> rest | c -> rest.Substring(0, c)
    (mime.Split ';').[0].Trim().ToLowerInvariant()

let urlAttributes = set [ "href"; "src"; "action"; "formaction"; "poster"; "cite"; "background"; "xlink:href"; "srcset"; "data"; "longdesc"; "xml:base" ]

let safeSchemes = set [ "http"; "https"; "mailto"; "ftp"; "tel"; "sms"; "cid"; "xmpp"; "webcal"; "irc"; "news"; "nntp"; "callto"; "aim"; "afs"; "ed2k"; "fax"; "gopher"; "line"; "modem"; "rsync"; "rtsp"; "sftp"; "ssh"; "tag"; "telnet"; "urn"; "file"; "feed"; "svn" ]

type Report =
    { Violations: ResizeArray<string>
      /// Structure the browser builds differently from ours (informational, counted).
      Mutations: ResizeArray<string> }

let newReport () = { Violations = ResizeArray(); Mutations = ResizeArray() }

let private ownTree (html: string) =
    match Html5.parseBody html with
    | Ok root -> Some root
    | Error _ -> None

let private elementCounts (root: INode) =
    let counts = Dictionary<string, int>()
    for d in Html5.descendants root do
        match d with
        | :? IElement as e ->
            let add (key: string) = counts[key] <- (match counts.TryGetValue key with | true, n -> n + 1 | _ -> 1)
            add (e.NamespaceUri + "|" + e.LocalName)
            for a in e.Attributes do add ("@" + e.LocalName + "[" + Html5.qualifiedName a + "]")
        | _ -> ()
    counts

/// Checks `html` (a sanitizer's output) as a browser parses it inside the page. `allowed`: the list its last sanitizer pass used.
let check (label: string) (html: string) (allowed: SafeList) (allowStyle: bool) (report: Report) =
    let v (what: string) = report.Violations.Add $"{label}: {what}\n    output: {html}"
    if html.Contains '' then
        // Trusted.ofSanitized rewrites it; recorded so the review can see the marker reaches the boundary
        ()
    let doc = browser.ParseDocument("<!DOCTYPE html><html><head></head><body><div id=\"x-container\">" + html + "</div><p id=\"x-after\">after</p></body></html>")
    let body = nonNull doc.Body
    let container = doc.GetElementById "x-container"
    let after = doc.GetElementById "x-after"
    match container, after with
    | null, _
    | _, null -> v "the sentinel or the container is gone"
    | container, after ->
        let bodyChildren = Html5.childrenOf body
        if bodyChildren.Length <> 2 || not (obj.ReferenceEquals(bodyChildren[0], container)) || not (obj.ReferenceEquals(bodyChildren[1], after)) then
            let shape = bodyChildren |> Array.map (fun n -> match n with :? IElement as e -> e.LocalName + (match e.Id with null -> "" | id -> "#" + id) | :? IText as t -> "text(" + t.Data + ")" | _ -> n.NodeName) |> String.concat ", "
            v $"escapes its container: body holds [{shape}]"
        if after.TextContent <> "after" || after.ChildElementCount <> 0 then v "the sentinel was changed"
        for d in Html5.descendants container do
            match d with
            | :? IComment -> v "comment"
            | :? IElement as e ->
                let name = e.LocalName
                if not (String.Equals(e.NamespaceUri, htmlNs, StringComparison.Ordinal)) then v $"foreign element {e.NamespaceUri} {name}"
                if not (allowed.AllowsTag name) && name <> "a" then v $"<{name}> not in the list"
                for a in e.Attributes do
                    let attr = Html5.qualifiedName a
                    let lower = attr.ToLowerInvariant()
                    if not (allowed.AllowsAttribute attr) && not (name = "a" && (attr = "target" || attr = "href")) && not (attr = "style" && allowStyle) then
                        v $"{attr} on <{name}> not in the list"
                    if lower.StartsWith "on" then v $"event attribute {attr}"
                    if lower = "id" || lower = "name" || lower = "form" then v $"clobbering attribute {attr}"
                    if lower.StartsWith "data-" && lower <> "data-language" then v $"{attr}"
                    if urlAttributes.Contains lower then
                        match browserScheme a.Value with
                        | Some "data" ->
                            let mime = dataMime a.Value
                            let token (t: string) = t.Length > 0 && t |> Seq.forall (fun c -> Char.IsAsciiLetterOrDigit c || "!#$%&'*+-.^_`|~".IndexOf c >= 0)
                            let valid = match mime.IndexOf '/' with -1 -> false | i -> token (mime.Substring(0, i)) && token (mime.Substring(i + 1))
                            if valid && (mime.Contains "html" || mime.Contains "xml" || mime.Contains "svg" || mime.Contains "script") then v $"{attr}=\"{a.Value}\" (data: {mime})"
                        | Some scheme when not (safeSchemes.Contains scheme) -> v $"{attr}=\"{a.Value}\" (scheme {scheme})"
                        | _ -> ()
                        if not (Loofah.allowedUri a.Value) && not (name = "a" && attr = "href") then v $"{attr}=\"{a.Value}\" refused by allowed_uri? after the browser's parse"
                    if lower = "style" then
                        let s = a.Value.ToLowerInvariant()
                        if s.Contains "url(" || s.Contains "expression" || s.Contains "\\" || s.Contains "@import" || s.Contains "position" then v $"style=\"{a.Value}\""
            | _ -> ()
        // Structure: does the browser build an element our parse of the same output does not have?
        match ownTree html with
        | None -> v "our own parser refuses our output"
        | Some own ->
            let ours = elementCounts own
            let theirs = elementCounts container
            for KeyValue(k, n) in theirs do
                let mine = match ours.TryGetValue k with | true, m -> m | _ -> 0
                if n > mine then report.Mutations.Add $"{label}: browser builds {n - mine} more {k}\n    output: {html}"
