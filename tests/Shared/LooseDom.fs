/// The loose DOM layer of the pixel harness (parity/capture/loose.ts and the masks of normalize.ts it uses) ported to F# over
/// AngleSharp, so the view tests judge a template by the rule the browser layer applies to the live page: the tree a reader of the
/// DOM sees, not the bytes the server sent (plans/design-views.md V11, W2). tests/Campfire.Views.Tests/loose/ holds what loose.ts
/// itself made of the Rails goldens and of 30 oracle answers (parity/bin/loose-golden); the W2 test requires this port to equal it
/// line for line, so a rule changed in loose.ts and not here fails.
///
/// The rules, in short: attributes are a set (order never matters, `class` is a set of tokens, `disabled` = `disabled=""`);
/// character references are decoded; comments and the forgery tokens Rails renders are dropped; a whitespace-only text node is
/// dropped unless it sits between two inline-level siblings (a space that renders) or inside pre/textarea/listing/plaintext/xmp;
/// other text has its whitespace collapsed and keeps a space at an edge only against an inline sibling; volatile values (tokens,
/// nonces, timestamps, asset digests) are masked. The output is one node per line, attributes sorted.
module Campfire.Tests.LooseDom

open System
open System.Collections.Generic
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open AngleSharp.Dom
open AngleSharp.Html.Dom
open AngleSharp.Html.Parser

type Options =
    { /// Epoch milliseconds of the seed's clock, which timestamps are decoded against.
      SeedTime: int64 option
      /// Servers run with --freeze: expiries are exact too.
      FrozenServerClock: bool }

let defaultOptions = { SeedTime = None; FrozenServerClock = false }

// ---- JavaScript semantics the port has to keep ---------------------------------------------------------------------------

/// JavaScript's whitespace (String.prototype.trim): not .NET's (it has U+0085 and not U+FEFF).
let private isJsSpace (c: char) : bool =
    match c with
    | '\t' | '\n' | '\u000B' | '\u000C' | '\r' | ' ' | ' ' | ' ' | ' ' | ' ' | ' ' | ' ' | '　' | '﻿' -> true
    | c -> c >= ' ' && c <= ' '

let private jsTrim (s: string) : string =
    let mutable a = 0
    let mutable b = s.Length
    while a < b && isJsSpace s[a] do a <- a + 1
    while b > a && isJsSpace s[b - 1] do b <- b - 1
    s.Substring(a, b - a)

let private isHtmlSpace (c: char) = c = '\t' || c = '\n' || c = '\u000C' || c = '\r' || c = ' '

/// `text.replace(/[\t\n\f\r ]+/g, " ").trim()`.
let collapse (text: string) : string =
    let out = StringBuilder(text.Length)
    let mutable inSpace = false
    for c in text do
        if isHtmlSpace c then
            if not inSpace then out.Append ' ' |> ignore
            inSpace <- true
        else
            out.Append c |> ignore
            inSpace <- false
    jsTrim (out.ToString())

/// `JSON.stringify` of a string.
let private jsQuote (s: string) : string =
    let out = StringBuilder(s.Length + 2)
    out.Append '"' |> ignore
    for i in 0 .. s.Length - 1 do
        let c = s[i]
        match c with
        | '"' -> out.Append "\\\"" |> ignore
        | '\\' -> out.Append "\\\\" |> ignore
        | '\b' -> out.Append "\\b" |> ignore
        | '\u000C' -> out.Append "\\f" |> ignore
        | '\n' -> out.Append "\\n" |> ignore
        | '\r' -> out.Append "\\r" |> ignore
        | '\t' -> out.Append "\\t" |> ignore
        | c when c < ' ' -> out.Append("\\u").Append((int c).ToString("x4")) |> ignore
        | c when Char.IsHighSurrogate c && not (i + 1 < s.Length && Char.IsLowSurrogate s[i + 1]) -> out.Append("\\u").Append((int c).ToString("x4")) |> ignore
        | c when Char.IsLowSurrogate c && not (i > 0 && Char.IsHighSurrogate s[i - 1]) -> out.Append("\\u").Append((int c).ToString("x4")) |> ignore
        | c -> out.Append c |> ignore
    out.Append('"').ToString()

let private jsNumber (raw: string) : string =
    let d = Double.Parse(raw, Globalization.CultureInfo.InvariantCulture)
    if d = Math.Floor d && Math.Abs d < 1e21 then d.ToString("F0", Globalization.CultureInfo.InvariantCulture)
    else d.ToString("R", Globalization.CultureInfo.InvariantCulture).Replace("E+", "e+").Replace("E-", "e-")

/// `JSON.stringify` of a parsed value (compact; a repeated key keeps its first place and its last value).
let rec private stringify (e: JsonElement) : string =
    match e.ValueKind with
    | JsonValueKind.String -> jsQuote (e.GetString() |> Option.ofObj |> Option.defaultValue "")
    | JsonValueKind.Number -> jsNumber (e.GetRawText())
    | JsonValueKind.True -> "true"
    | JsonValueKind.False -> "false"
    | JsonValueKind.Array -> "[" + String.Join(",", [ for x in e.EnumerateArray() -> stringify x ]) + "]"
    | JsonValueKind.Object ->
        let order = List<string>()
        let values = Dictionary<string, JsonElement>()
        for p in e.EnumerateObject() do
            if not (values.ContainsKey p.Name) then order.Add p.Name
            values[p.Name] <- p.Value
        "{" + String.Join(",", [ for k in order -> jsQuote k + ":" + stringify values[k] ]) + "}"
    | _ -> "null"

/// `JSON.parse`, or None when it throws.
let private parseJson (text: string) : JsonElement option =
    try
        use doc = JsonDocument.Parse text
        Some(doc.RootElement.Clone())
    with _ -> None

let normalizeJson (text: string) : string =
    match parseJson text with
    | Some json -> stringify json
    | None -> collapse text

/// `Buffer.from(text, "base64")` of text that matched `[A-Za-z0-9+/_-]+={0,2}`; None when it decodes to nothing.
let private decodeBase64 (text: string) : byte[] option =
    if not (Regex.IsMatch(text, @"^[A-Za-z0-9+/_-]+={0,2}\z")) then
        None
    else
        // Node's decoder, which keeps whole bytes and ignores bits left over (Convert.FromBase64String refuses them).
        let out = List<byte>()
        let mutable bits = 0
        let mutable count = 0
        for c in text.TrimEnd('=') do
            let v =
                match c with
                | c when c >= 'A' && c <= 'Z' -> int c - int 'A'
                | c when c >= 'a' && c <= 'z' -> int c - int 'a' + 26
                | c when c >= '0' && c <= '9' -> int c - int '0' + 52
                | '+' | '-' -> 62
                | _ -> 63
            bits <- (bits <<< 6) ||| v
            count <- count + 6
            if count >= 8 then
                count <- count - 8
                out.Add(byte ((bits >>> count) &&& 0xFF))
                bits <- bits &&& ((1 <<< count) - 1)
        if out.Count = 0 then None else Some(out.ToArray())

let private safeDecodeURIComponent (text: string) : string = Uri.UnescapeDataString text

// ---- masking (normalize.ts) -------------------------------------------------------------------------------------------------

let private jsSpaceClass = @"\t\n\u000B\u000C\r    -     　﻿"

let private blobUrl = Regex(@"blob:[a-z]+://[^/" + jsSpaceClass + @"""]+/[0-9a-f-]{36}")
let private qrCode = Regex(@"/qr_code/([A-Za-z0-9_=-]{8,})")
let private assetDigest = Regex(@"/assets/([^" + jsSpaceClass + @"""'()?#]+?)-([0-9a-f]{7,64})(\.[A-Za-z0-9.]+)")
let private signedToken =
    Regex(@"(?<![A-Za-z0-9+%_-])((?:[A-Za-z0-9+/_-]|%2[BbFf]|%3[Dd])+={0,2}(?:%3[Dd]){0,2})--([0-9a-f]{40,128})(?![0-9a-f])")
let private encryptedMessage = Regex(@"(?<![A-Za-z0-9+/])[A-Za-z0-9+/]{16,}={0,2}--[A-Za-z0-9+/]{12,}={0,2}--[A-Za-z0-9+/]{16,}={0,2}(?![A-Za-z0-9+/])")
let private isoTime =
    Regex(@"(?<![A-Za-z0-9_])[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?:Z|[+-][0-9]{2}:?[0-9]{2})(?![A-Za-z0-9_])")
let private gid = Regex(@"gid://[^/" + jsSpaceClass + @"]+/([A-Za-z:]+)/([^" + jsSpaceClass + @"?""\u0000-\u001F]+)")

let private formatDelta (ms: int64) : string =
    let sign = if ms < 0L then "-" else "+"
    let abs = Math.Abs ms
    let seconds = float abs / 1000.0
    let shown = if abs % 1000L = 0L then seconds.ToString("F0", Globalization.CultureInfo.InvariantCulture) else seconds.ToString("F3", Globalization.CultureInfo.InvariantCulture)
    $"{sign}{shown}s"

/// `Date.parse` of an ISO time, in epoch milliseconds.
let private dateParse (iso: string) : int64 option =
    let fixedOffset = Regex.Replace(iso, @"([+-][0-9]{2})([0-9]{2})\z", "$1:$2")
    match DateTimeOffset.TryParse(fixedOffset, Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.AssumeUniversal) with
    | true, t -> Some(t.ToUnixTimeMilliseconds())
    | _ -> None

let private relativeIso (iso: string) (options: Options) : string =
    match dateParse iso, options.SeedTime with
    | Some t, Some seed -> $"«t{formatDelta (t - seed)}»"
    | _ -> iso

let private describeExpiry (exp: string) (options: Options) : string =
    if options.FrozenServerClock then "exp" + relativeIso exp options else "expires"

let private describeGid (text: string) : string option =
    let m = gid.Match text
    if m.Success then Some($"{m.Groups[1].Value}#{Uri.UnescapeDataString m.Groups[2].Value}") else None

let private describeGidParam (part: string) : string =
    let decoded = decodeBase64 part |> Option.map (fun b -> Encoding.UTF8.GetString b)
    match decoded |> Option.bind (fun d -> if d <> "" then describeGid d else None) with
    | Some g -> g
    | None -> part

let private jsString (e: JsonElement) = e.ValueKind = JsonValueKind.String

let private describeSignedMessage (encoded: string) (options: Options) : string option =
    match decodeBase64 (safeDecodeURIComponent encoded) with
    | None -> None
    | Some data ->
        let text = Encoding.Latin1.GetString data
        let json = parseJson text
        let envelope =
            match json with
            | Some j when j.ValueKind = JsonValueKind.Object ->
                match j.TryGetProperty "_rails" with
                | true, e when e.ValueKind <> JsonValueKind.Null && e.ValueKind <> JsonValueKind.False -> Some e
                | _ -> None
            | _ -> None
        match envelope with
        | Some envelope ->
            let prop (name: string) =
                match envelope.ValueKind, (if envelope.ValueKind = JsonValueKind.Object then envelope.TryGetProperty name else (false, Unchecked.defaultof<JsonElement>)) with
                | JsonValueKind.Object, (true, v) -> Some v
                | _ -> None
            // payload: a string, or any JSON value, as a string for the template
            let payloadText, payloadString =
                match prop "data" with
                | Some d -> (if jsString d then d.GetString() |> Option.ofObj |> Option.defaultValue "" else stringify d), (if jsString d then d.GetString() else null)
                | None ->
                    match prop "message" with
                    | Some m when jsString m ->
                        let raw = m.GetString() |> Option.ofObj |> Option.defaultValue ""
                        let inner = decodeBase64 raw |> Option.map (fun b -> Encoding.UTF8.GetString b)
                        match inner with
                        | Some i ->
                            match parseJson i with
                            | Some v when v.ValueKind <> JsonValueKind.Null -> (if jsString v then v.GetString() |> Option.ofObj |> Option.defaultValue "" else stringify v), (if jsString v then v.GetString() else null)
                            | _ -> i, i
                        | None -> raw, raw
                    | Some m -> stringify m, null
                    | None -> "undefined", null
            let purpose =
                match prop "pur" with
                | Some p when p.ValueKind <> JsonValueKind.Null -> (if jsString p then p.GetString() |> Option.ofObj |> Option.defaultValue "" else stringify p)
                | _ -> "default"
            let exp =
                match prop "exp" with
                | Some e when jsString e && e.GetString() <> "" -> ":" + describeExpiry (e.GetString() |> Option.ofObj |> Option.defaultValue "") options
                | Some e when e.ValueKind = JsonValueKind.Number -> ":" + describeExpiry (jsNumber (e.GetRawText())) options
                | _ -> ""
            let g = match payloadString with | null -> None | ps -> describeGid ps
            match g with
            | Some g ->
                let kind = if purpose = "default" then "" else purpose + ":"
                Some $"«sgid:{kind}{g}{exp}»"
            | None -> Some $"«signed_id:{purpose}:{payloadText}{exp}»"
        | None ->
            match describeGid text with
            | Some g -> Some $"«sgid:{g}»"
            | None ->
                match json with
                | Some j when jsString j ->
                    let s = j.GetString() |> Option.ofObj |> Option.defaultValue ""
                    Some("«stream:" + String.Join(":", s.Split(':') |> Array.map describeGidParam) + "»")
                | Some j -> Some $"«signed:{stringify j}»"
                | None -> if text.StartsWith "\u0004\u0008" then Some "«signed:marshal»" else None

let private describeSignedToken (token: string) (options: Options) : string =
    let encoded = token.Substring(0, token.IndexOf "--")
    let starts = [ yield 0; for m in Regex.Matches(encoded, "/") -> m.Index + 1 ]
    let found =
        starts
        |> List.tryPick (fun start ->
            describeSignedMessage (encoded.Substring start) options |> Option.map (fun d -> encoded.Substring(0, start) + d))
    match found with
    | Some d -> d
    | None -> encoded.Substring(0, List.last starts) + "«signed»"

let rec maskText (text: string) (options: Options) : string =
    let t = qrCode.Replace(text, MatchEvaluator(fun m ->
                let encoded = m.Groups[1].Value
                let decoded = decodeBase64 encoded |> Option.map (fun b -> Encoding.UTF8.GetString b) |> Option.defaultValue encoded
                $"/qr_code/«qr:{maskText decoded options}»"))
    let t = isoTime.Replace(t, MatchEvaluator(fun m -> relativeIso m.Value options))
    let t = signedToken.Replace(t, MatchEvaluator(fun m -> describeSignedToken m.Value options))
    let t = encryptedMessage.Replace(t, "«encrypted»")
    let t = blobUrl.Replace(t, "blob:«object-url»")
    assetDigest.Replace(t, MatchEvaluator(fun m -> $"/assets/{m.Groups[1].Value}-«digest»{m.Groups[3].Value}"))

let private relativeEpoch (name: string) (value: string) (options: Options) : string option =
    match options.SeedTime with
    | Some seed when Regex.IsMatch(value, @"^[0-9]{10}([0-9]{3})?\z") ->
        let ms = if value.Length = 13 then Int64.Parse value else Int64.Parse value * 1000L
        let plausible = Math.Abs(ms - seed) < 20L * 365L * 86_400_000L
        let timeish = value.Length = 13 || Regex.IsMatch(name, "(_at|-at|time|date|timestamp|sort|number|expires)", RegexOptions.IgnoreCase)
        let unit = if value.Length = 13 then "ms" else "s"
        if plausible && timeish then Some $"«epoch{unit}{formatDelta (ms - seed)}»" else None
    | _ -> None

type private Attribute = { Name: string; Value: string }

let private attrOf (attrs: Attribute list) (name: string) : string option =
    attrs |> List.tryFind (fun a -> a.Name = name) |> Option.map (fun a -> a.Value)

let private maskAttr (tag: string) (name: string) (value: string) (attrs: Attribute list) (options: Options) : string =
    if name = "nonce" || (tag = "meta" && attrOf attrs "name" = Some "csp-nonce" && name = "content") then "«nonce»"
    else
        match relativeEpoch name value options with
        | Some e -> e
        | None -> maskText value options

let private isForgeryToken (tag: string) (attrs: Attribute list) : bool =
    let name = attrOf attrs "name"
    (tag = "input" && name = Some "authenticity_token") || (tag = "meta" && (name = Some "csrf-token" || name = Some "csrf-param"))

/// A message's "Copy link" button: the absolute URL Rails builds from the request's host becomes the path.
let private relativeCopyLink (attrs: Attribute list) (attr: Attribute) : Attribute =
    let title = attrOf attrs "title"
    let m = Regex.Match(attr.Value, @"^https?://[^/]+(/.*)$", RegexOptions.Singleline)
    if title <> Some "Copy link" || attr.Name <> "data-copy-to-clipboard-content-value" || not m.Success then attr
    else { Name = "data-copy-to-clipboard-url-value"; Value = m.Groups[1].Value }

// ---- the loose tree (loose.ts) ------------------------------------------------------------------------------------------------

let private rawText = HashSet<string>([ "pre"; "textarea"; "listing"; "plaintext"; "xmp" ])
let private voidElements = HashSet<string>([ "area"; "base"; "br"; "col"; "embed"; "hr"; "img"; "input"; "link"; "meta"; "source"; "track"; "wbr" ])
let private inlineElements =
    HashSet<string>(
        [ "a"; "abbr"; "acronym"; "b"; "bdi"; "bdo"; "big"; "br"; "button"; "canvas"; "cite"; "code"; "data"; "del"; "dfn"; "em"; "font"; "i"
          "iframe"; "img"; "input"; "ins"; "kbd"; "label"; "mark"; "meter"; "object"; "output"; "picture"; "progress"; "q"; "ruby"; "s"; "samp"
          "select"; "slot"; "small"; "span"; "strike"; "strong"; "sub"; "sup"; "svg"; "textarea"; "time"; "tt"; "u"; "var"; "video"; "audio"; "wbr" ]
    )
let private boxless = HashSet<string>([ "script"; "style"; "template"; "link"; "meta"; "base"; "title"; "noscript" ])

let private tagOf (n: INode) : string option =
    match n with
    | :? IElement as e -> Some e.LocalName
    | _ -> None

let private attrsOf (n: INode) : Attribute list =
    match n with
    | :? IElement as e -> [ for a in e.Attributes -> { Name = a.Name; Value = a.Value } ]
    | _ -> []

let private isText (n: INode) = n.NodeType = NodeType.Text
let private isInlineNode (n: INode) =
    isText n || (match tagOf n with Some t -> inlineElements.Contains t || t.Contains '-' | None -> false)

let private isBoxless (n: INode) =
    match tagOf n with
    | Some t -> boxless.Contains t || (t = "input" && (defaultArg (attrOf (attrsOf n) "type") "").ToLowerInvariant() = "hidden")
    | None -> false

let private isBlankText (n: INode) = isText n && collapse n.TextContent = ""

let rec private walk (node: INode) (depth: int) (out: List<string>) (options: Options) (raw: bool) : unit =
    let indent = String.replicate depth "  "
    let tag = (tagOf node).Value
    let nodeAttrs = attrsOf node
    if not (isForgeryToken tag nodeAttrs) then
        let seen = HashSet<string>()
        let attrs = List<string * string>()
        for a in nodeAttrs |> List.map (relativeCopyLink nodeAttrs) do
            if seen.Add a.Name then
                let value = maskAttr tag a.Name a.Value nodeAttrs options
                let value =
                    if a.Name = "class" then
                        value.Split([| '\t'; '\n'; '\u000C'; '\r'; ' ' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.distinct
                        |> Array.sortWith (fun x y -> String.CompareOrdinal(x, y))
                        |> String.concat " "
                    else value
                attrs.Add((a.Name, value))
        let sorted = attrs |> Seq.sortWith (fun (a, _) (b, _) -> String.CompareOrdinal(a, b)) |> List.ofSeq
        let shown = sorted |> List.map (fun (n, v) -> if v = "" then $" {n}" else $" {n}={jsQuote v}") |> String.concat ""
        out.Add $"{indent}<{tag}{shown}>"
        let children: INode list =
            match node with
            | :? IHtmlTemplateElement as t -> [ for c in t.Content.ChildNodes -> c ]
            | n -> [ for c in n.ChildNodes -> c ]
        if tag = "script" || tag = "style" then
            let text = children |> List.filter isText |> List.map (fun c -> c.TextContent) |> String.concat ""
            let body =
                if tag = "script" && Regex.IsMatch(defaultArg (attrOf nodeAttrs "type") "", "importmap|json") then normalizeJson text
                else collapse text
            if body <> "" then out.Add $"{indent}  {maskText body options}"
        else
            let childRaw = raw || rawText.Contains tag
            let kids =
                children
                |> List.filter (fun c ->
                    c.NodeType <> NodeType.Comment
                    && c.NodeType <> NodeType.DocumentType
                    && not (match tagOf c with Some t -> isForgeryToken t (attrsOf c) | None -> false))
                |> Array.ofList
            let prevDrawn (i: int) : INode option =
                let mutable j = i - 1
                let mutable found = None
                while found.IsNone && j >= 0 do
                    if not (isBoxless kids[j]) then found <- Some kids[j]
                    j <- j - 1
                found
            let nextDrawn (i: int) (skipBlank: bool) : INode option =
                let mutable j = i + 1
                let mutable found = None
                while found.IsNone && j < kids.Length do
                    if not (isBoxless kids[j]) && not (skipBlank && isBlankText kids[j]) then found <- Some kids[j]
                    j <- j + 1
                found
            kids
            |> Array.iteri (fun i child ->
                if not (isText child) then
                    if child.NodeType = NodeType.Element then walk child (depth + 1) out options childRaw
                elif childRaw then
                    let v = child.TextContent
                    if v <> "" then out.Add $"{indent}  {jsQuote (maskText v options)}"
                else
                    let value = child.TextContent
                    let prev = prevDrawn i
                    let text = collapse value
                    if text = "" then
                        if not (value = "" || (prev.IsSome && isBlankText prev.Value)) then
                            let next = nextDrawn i true
                            if prev.IsSome && isInlineNode prev.Value && next.IsSome && isInlineNode next.Value then out.Add $"{indent}  \" \""
                    else
                        let next = nextDrawn i false
                        let lead = if isHtmlSpace value[0] && prev.IsSome && isInlineNode prev.Value && not (isBlankText prev.Value) then " " else ""
                        let trail = if isHtmlSpace value[value.Length - 1] && next.IsSome && isInlineNode next.Value && not (isBlankText next.Value) then " " else ""
                        out.Add $"{indent}  {jsQuote (maskText (lead + text + trail) options)}")
        if not (voidElements.Contains tag) then out.Add $"{indent}</{tag}>"

let private parser = HtmlParser(HtmlParserOptions(IsScripting = true))

/// loose.ts `looseDom`: the page's `body.outerHTML` as the loose tree, one node per line.
let ofBody (options: Options) (bodyOuterHtml: string) : string =
    let document = parser.ParseDocument($"<!DOCTYPE html><html><head></head>{bodyOuterHtml}</html>")
    let lines = List<string>()
    match document.Body with
    | null -> ()
    | body -> walk body 0 lines options false
    String.Join("\n", lines) + "\n"

let private isPage (markup: string) = Regex.IsMatch(markup, @"^\s*(<!doctype|<html[\s>])", RegexOptions.IgnoreCase)

/// The loose tree of a rendered page or fragment. A page (a doctype or an `<html>` first) is parsed whole and its body goes to
/// `ofBody`, as the live layer hands it the browser's `body.outerHTML`; anything else is a fragment, put in a body.
let normalizeWith (options: Options) (markup: string) : string =
    if isPage markup then
        let document = parser.ParseDocument markup
        match document.Body with
        | null -> ofBody options "<body></body>"
        | body -> ofBody options body.OuterHtml
    else
        ofBody options $"<body>{markup}</body>"

let normalize (markup: string) : string = normalizeWith defaultOptions markup

/// A readable report of the first difference between two loose trees, or None.
let diff (expected: string) (actual: string) : string option =
    if expected = actual then None
    else
        let e = expected.Split('\n')
        let a = actual.Split('\n')
        let first = Seq.zip e a |> Seq.tryFindIndex (fun (x, y) -> x <> y) |> Option.defaultValue (min e.Length a.Length)
        let report = StringBuilder()
        report.AppendLine $"first difference at line {first + 1} (expected {e.Length} lines, got {a.Length})" |> ignore
        for index in max 0 (first - 4) .. first + 5 do
            let x = if index < e.Length then e[index] else "<end>"
            let y = if index < a.Length then a[index] else "<end>"
            let marker = if x = y then " " else "!"
            report.AppendLine $"{marker} expected: {x}" |> ignore
            report.AppendLine $"{marker} actual:   {y}" |> ignore
        Some(report.ToString())
