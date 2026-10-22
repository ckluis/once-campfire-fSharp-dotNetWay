#r "/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/tests/Campfire.RichText.Tests/bin/Release/net11.0/AngleSharp.dll"
#r "/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/tests/Campfire.RichText.Tests/bin/Release/net11.0/Campfire.Ruby.dll"
#r "/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/tests/Campfire.RichText.Tests/bin/Release/net11.0/Campfire.RailsCompat.dll"
#r "/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/tests/Campfire.RichText.Tests/bin/Release/net11.0/Campfire.RichText.dll"
open System
open System.Diagnostics
open Campfire.RichText
type NoRecords() =
    interface IAttachableResolver with
        member _.LocateSigned _ = SignedLookup.Invalid
        member _.FindGid _ = GidLookup.NotFound
let ctx : RenderContext = { Resolver = NoRecords(); RequestHost = None }
let rep (n: int) (s: string) = String.replicate n s
let shapes : (string * string) list = [
  "F1 base", "<p><select><xmp></xmp><select>x"
  "F1 in li", "<ul><li><select><xmp></xmp><select>x"
  "F1 variant textarea", "<p><select><textarea></textarea><select>x"
  "F1 variant input", "<p><select><input><select>x"
  "F1 variant keygen", "<p><select><keygen><select>x"
  "F1 variant plaintext", "<p><select><plaintext>x"
  "select in table in select", "<select><table><select><xmp>x</xmp><select>y"
  "select nested 5000", rep 5000 "<select><xmp></xmp>"
  "p select xmp 3000", rep 3000 "<p><select><xmp></xmp><select>x"
  "option hr 9000", "<select>" + rep 9000 "<option><hr>"
  "formatting distinct + p", (String.Join("", [for i in 1..300 -> sprintf "<b a%d>" i])) + rep 30 "<p>x"
  "formatting reopen in table", "<table>" + (String.Join("", [for i in 1..300 -> sprintf "<b a%d>" i])) + rep 30 "<td>x"
  "a nested 3000 + /a", rep 3000 "<a href=x>" + rep 3000 "</a>"
  "adoption div 300", rep 300 "<b><div>" + rep 300 "</b>"
  "foreignObject 3000", rep 3000 "<svg><foreignObject>"
  "math annotation", rep 3000 "<math><annotation-xml encoding=text/html>"
  "table caption 3000", rep 3000 "<table><caption>"
  "frameset+text", "<frameset>" + rep 9000 "<frame>"
  "noscript", rep 9000 "<noscript><p>"
  "urls 16000", rep 16000 "http://a.b/c "
  "attachment flood", rep 3000 "<action-text-attachment sgid=x content-type=\"text/html\" content=\"&lt;b&gt;x&lt;/b&gt;\"></action-text-attachment>"
  "nested attachment content", (let mutable s = "<b>x</b>" in (for _ in 1..10 do s <- sprintf "<action-text-attachment content-type=\"text/html\" content=\"%s\"></action-text-attachment>" (Net.WebUtility.HtmlEncode s)); s)
  "isindex", "<isindex prompt=\"<img src=x onerror=alert(1)>\">"
  "javascript href", "<a href=\"jav&#x09;ascript:alert(1)\">x</a>"
  "svg a", "<svg><a href=javascript:alert(1)>x</a></svg>"
  "noscript mxss", "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\">"
]
for (name, body) in shapes do
    let sw = Stopwatch.StartNew()
    let res =
        try
            match ActionText.messagePresentation body ctx with
            | Ok html -> sprintf "ok %d bytes: %s" html.Length (if html.Length < 160 then html else html.Substring(0,160))
            | Error e -> sprintf "error %s" e.Message
        with e -> sprintf "EXCEPTION %s %s" (e.GetType().Name) e.Message
    let pt = try (match ActionText.toPlainText body ctx with Ok t -> sprintf "pt %d" t.Length | Error e -> "pt err " + e.Message) with e -> "PT EXCEPTION " + e.GetType().Name
    let ed = try (match ActionText.editableValue body ctx with Ok _ -> "ed ok" | Error e -> "ed err " + e.Message) with e -> "ED EXCEPTION " + e.GetType().Name
    printfn "%-28s %6.0f ms  %s | %s | %s" name sw.Elapsed.TotalMilliseconds (res.Replace("\n"," ")) pt ed
