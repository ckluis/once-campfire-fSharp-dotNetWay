// Unit 5.3-5.4 of plans/design-richtext.md: attachments, load, rendering, plain text and mentions on AngleSharp (`Content`, `PlainText`,
// `ActionText`, `Filters`). Their equality with the pre-AngleSharp pipeline (M1, C2, C3 on the 1,479 inputs) was the migration differential and
// ended with unit 5.7; the standing oracle is Rails (`CorpusTests`: C1, plain text, editor values, mentions, N1, N2) and the frozen
// parser trees (`Html5Tests`: P1). What stays here: C4, nesting, the attribute lookups, the cost of a seed body and the linear-time bound.
module Campfire.RichText.Tests.ContentTests

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open Xunit
open Campfire.RichText
open Campfire.RichText.Tests.Html5Tests
open Campfire.RichText.Tests.Support
open Campfire.RichText.Tests.CorpusResolver
open Campfire.Tests

let str (e: JsonElement) : string = e.GetString() |> nonNull

/// Every SGID is a verified one for a user who is named after it, so the seed's mentions resolve (they are signed by the seed's key,
/// which the tests do not have): both pipelines see the same lookups.
type private AllSgids() =
    interface IAttachableResolver with
        member _.LocateSigned sgid =
            let id = int64 (abs (sgid.GetHashCode() % 1000)) + 1L
            SignedLookup.User
                { Id = id
                  Name = $"User {id}"
                  Title = ""
                  AttachableSgid = sgid
                  UserPath = $"/users/{id}"
                  AvatarPath = $"/users/{id}/avatar" }

        member _.FindGid _ = GidLookup.NotFound

/// (label, body, context) for the 1,479: the corpus's cases with their own host, Rails' presentations and the seed's bodies.
let all =
    lazy
        (let bodies, presentations, seeds = inputs.Force()
         use corpus = JsonDocument.Parse(File.ReadAllText(Repo.path "tests/Campfire.RichText.Tests/corpus/expected.json"))
         let resolver = resolverOf corpus.RootElement
         let hosts = Dictionary<string, string option>()
         for c in corpus.RootElement.GetProperty("cases").EnumerateArray() do
             hosts["corpus:" + str (c.GetProperty "name")] <-
                 (match c.TryGetProperty "host" with
                  | true, h when h.ValueKind = JsonValueKind.String -> Some(str h)
                  | _ -> None)
         let seedResolver = AllSgids() :> IAttachableResolver
         [ for (label, body) in bodies -> label, body, render resolver hosts[label]
           for (label, body) in presentations -> label, body, render resolver None
           for (label, body) in seeds -> label, body, render seedResolver None ])

let shownR (r: Result<string, RenderError>) : string =
    match r with
    | Ok s -> s
    | Error e -> $"error: {e.Message}"

let private mentionsOf (r: Result<MentionUser list, RenderError>) : string =
    match r with
    | Ok users -> String.Join(",", users |> List.map (fun u -> $"{u.Id}:{u.Name}"))
    | Error e -> $"error: {e.Message}"

let private canonicalNew (body: string) (ctx: RenderContext) =
    Content.load body ctx |> Result.map Content.toHtml |> shownR

let private layoutOf (body: string) (ctx: RenderContext) =
    Content.load body ctx |> Result.bind (fun c -> Content.toRenderedHtmlWithLayout c ctx) |> shownR

let private canonicalOf (body: string) (ctx: RenderContext) =
    Content.load body ctx |> Result.map Content.toHtml |> shownR

/// C4: a mention whose user is gone leaves the editor (Rails raises on it), and the rest of the body stays.
[<Fact>]
let ``C4 a deleted user's mention leaves the editor`` () =
    let sgid = "eyJfcmFpbHMiOnsiZGF0YSI6ImdpZDovL2NhbXBmaXJlL1VzZXIvNj9leHBpcmVzX2luIiwicHVyIjoiYXR0YWNoYWJsZSJ9fQ==--fc4f83a239475557295b8e2f5ff55482bebc9bfe"
    let body =
        $"<p>hi <action-text-attachment sgid=\"{sgid}\" content-type=\"application/vnd.campfire.mention\"></action-text-attachment> there</p>"
    let gone =
        { new IAttachableResolver with
            member _.LocateSigned _ = SignedLookup.MissingRecord "User"
            member _.FindGid _ = GidLookup.NotFound }
    let ctx = render gone None
    match ActionText.editableValue body ctx with
    | Ok(Some v) ->
        Assert.DoesNotContain("action-text-attachment", v)
        Assert.Contains("hi", v)
        Assert.Contains("there", v)
    | other -> failwithf "unexpected: %A" other

/// Nested content attachments render 8 deep and no deeper, (a content attachment's content is rendered inside the next, as in Action Text).
[<Fact>]
let ``Nested content attachments render alike, to the same depth`` () =
    let rec nest (n: int) : string =
        if n = 0 then "<p>core</p>"
        else
            let inner = nest (n - 1)
            let escaped = System.Net.WebUtility.HtmlEncode inner
            $"<action-text-attachment content-type=\"text/html\" content=\"{escaped}\"></action-text-attachment>"
    let ctx = render (AllSgids() :> IAttachableResolver) None
    for depth in [ 1; 3; 8; 9; 12 ] do
        let body = nest depth
        let rendered = layoutOf body ctx
        // 8 content attachments deep still render; the 9th and below are empty
        let withoutAttributes = System.Text.RegularExpressions.Regex.Replace(rendered, "=\"[^\"]*\"", "")
        Assert.Equal(depth <= 8, withoutAttributes.Contains "<p>core</p>")

/// The attachment nodes of every body that has any: the resolution rules read their attributes, and every attachment of the corpus
/// resolves to something (a record, a missing one or a plain file), none raises.
[<Fact>]
let ``Attachables resolves every attachment of the corpus through the attributes of its node`` () =
    let mutable nodes = 0
    for (_, body, ctx) in all.Force() do
        match Html5.parseBody body with
        | Ok root ->
            for n in Content.attachmentNodes root do
                nodes <- nodes + 1
                Attachables.attachmentFrom (Html5.attrsOf n) ctx |> ignore
                Attachables.opengraphEmbedFrom (Html5.attrsOf n) ctx |> ignore
        | Error _ -> ()
    Assert.True(nodes > 100, $"only {nodes} attachment nodes")

/// Risk 10 / S6: what a canonical load and a plain text cost per seed body (a stopwatch, reported; the bound is generous, the floor is
/// judged by `bench/quick`).
[<Fact>]
let ``S6 cost per seed body: canonical load with toHtml and plain text`` () =
    let seeds = all.Force() |> List.filter (fun (l, _, _) -> l.StartsWith "seed:")
    if not seeds.IsEmpty then
        let measure (f: string -> RenderContext -> string) : float * float =
            let rounds = 30
            for (_, b, c) in seeds do f b c |> ignore
            let sw = Diagnostics.Stopwatch.StartNew()
            let a0 = GC.GetAllocatedBytesForCurrentThread()
            for _ in 1..rounds do
                for (_, b, c) in seeds do f b c |> ignore
            let n = float (rounds * seeds.Length)
            sw.Elapsed.TotalMilliseconds * 1000.0 / n, float (GC.GetAllocatedBytesForCurrentThread() - a0) / n
        let report (name: string) (us: float, bytes: float) = eprintfn "S6 %s: %.1f us, %.1f KB per body" name us (bytes / 1024.0)
        let load = measure canonicalOf
        let text = measure (fun b c -> ActionText.toPlainText b c |> shownR)
        let render = measure layoutOf
        report "load+toHtml" load
        report "plain text" text
        report "render" render
        Assert.True(fst load < 200.0 && fst text < 200.0, "a load or a plain text of a seed body costs over 200 us")

/// Checkpoint after 5.4: AngleSharp finds a node's siblings, and a child it removes or inserts before, by scanning its parent's list,
/// so a walk by NextSibling/PreviousSibling, a removal from the back or an insertion per child is quadratic in a wide node. Before
/// the fix these took 0.4-2.7 s (a caption in a td 1.5 s through ParseFragment); every one is a body under
/// the parse caps.
[<Fact>]
let ``wide bodies cost linear time through load, plain text, render and filters`` () =
    let ctx = render (NoRecords()) (Some "once.campfire.test")
    let caption = Net.WebUtility.HtmlEncode(String.replicate 9_500 "<b>x</b>")
    let content = Net.WebUtility.HtmlEncode(String.replicate 9_500 "x<br>")
    let captioned = $"<action-text-attachment content-type=\"image/png\" url=\"https://x.test/y.png\" caption=\"{caption}\"></action-text-attachment>"
    let bodies =
        [ "9,900 x<br>", String.replicate 9_900 "x<br>"
          "19,000 comments", String.replicate 19_000 "x<!---->"
          "an unknown element around 9,900 x<br>", "<foo>" + String.replicate 9_900 "x<br>" + "</foo>"
          "a caption of 9,500 <b>", $"<action-text-attachment content-type=\"image/png\" url=\"https://x.test/y.png\" caption=\"{caption}\"></action-text-attachment>"
          "a content attachment of 9,500 x<br>", $"<action-text-attachment content-type=\"text/html\" content=\"{content}\"></action-text-attachment>"
          // A table cell, a caption and a form are contexts the body parse does not stand in for
          "that caption in a td", $"<table><tr><td>{captioned}</td></tr></table>"
          "that caption in a table caption", $"<table><caption>{captioned}</caption></table>"
          "that caption in a form", $"<form>{captioned}</form>"
          // Unit 5.5: the linker's wide cases (400 ms CPU each, with the presentation step below)
          "9,900 urls, one per paragraph", String.concat "" [ for i in 1..9_900 -> $"<p>http://a.test/{i}</p>" ]
          "9,900 urls among 9,900 text nodes", String.concat "" [ for i in 1..9_900 -> $"see http://a.test/{i}<br>" ]
          "9,900 e-mail addresses, one per paragraph", String.concat "" [ for i in 1..9_900 -> $"<p>u{i}@a.test</p>" ]
          "9,900 e-mail addresses among 9,900 text nodes", String.concat "" [ for i in 1..9_900 -> $"mail u{i}@a.test<br>" ]
          "one text node of 16,000 urls", "<p>" + String.concat " " [ for i in 1..16_000 -> $"http://a.test/{i}" ] + "</p>"
          "one text node of 16,000 e-mail addresses", "<p>" + String.concat " " [ for i in 1..16_000 -> $"u{i}@a.test" ] + "</p>" ]
    let steps: (string * (string -> unit)) list =
        [ "plain text", (fun b -> ActionText.toPlainText b ctx |> ignore)
          "layout", (fun b -> Content.load b ctx |> Result.bind (fun c -> Content.toRenderedHtmlWithLayout c ctx) |> ignore)
          "presentation", (fun b -> ActionText.messagePresentation b ctx |> ignore)
          "filters", (fun b -> Content.load b ctx |> Result.bind (fun c -> Filters.apply c ctx) |> Result.map Content.toHtml |> ignore) ]
    for (label, body) in bodies do
        for (step, run) in steps do
            run body
            let started = CpuTimer()
            run body
            let elapsed = started.Elapsed
            Assert.True(elapsed < bound (TimeSpan.FromSeconds 2.0) (TimeSpan.FromMilliseconds 400.0), $"{step} of {label} took {elapsed}")
