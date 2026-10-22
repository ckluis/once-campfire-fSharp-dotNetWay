// Unit 5.3 of plans/design-richtext.md: attachments, load, plain text and mentions on AngleSharp (`Next.Content`, `Next.PlainText`,
// `Next.ActionText`) against the old pipeline: M1 (the migration differential for canonical bodies, plain text and mentions on
// the 1,479 inputs), C2 (canonical bodies, what posting stores) and C3 (the seed's plain text and mentions), and `Attachables`'
// attribute-lookup entry points on both DOMs.
module Campfire.RichText.Tests.NextContentTests

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

let private str (e: JsonElement) : string = e.GetString() |> nonNull

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
let private all =
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

let private shownR (r: Result<string, RenderError>) : string =
    match r with
    | Ok s -> s
    | Error e -> $"error: {e.Message}"

let private mentionsOf (r: Result<MentionUser list, RenderError>) : string =
    match r with
    | Ok users -> String.Join(",", users |> List.map (fun u -> $"{u.Id}:{u.Name}"))
    | Error e -> $"error: {e.Message}"

let private canonicalOld (body: string) (ctx: RenderContext) =
    Content.load body ctx |> Result.map Content.toHtml |> shownR

let private canonicalNew (body: string) (ctx: RenderContext) =
    Next.Content.load body ctx |> Result.map Next.Content.toHtml |> shownR

/// Compares `oldOf` with `newOf` on the inputs `keep` selects; the labels that differ must be P1's list, and every name on the list
/// that has no difference left is reported (a body already equal after the pipeline is no failure).
let private compareWith (extra: Set<string>) (what: string) (keep: string -> bool) (oldOf: string -> RenderContext -> string) (newOf: string -> RenderContext -> string) =
    let differing = ResizeArray<string>()
    let report = StringBuilder()
    let mutable compared = 0
    for (label, body, ctx) in all.Force() do
        if keep label then
            compared <- compared + 1
            let o = oldOf body ctx
            let n = newOf body ctx
            if o <> n then
                differing.Add label
                if not (p1Differences.Contains label || extra.Contains label) then
                    // The window around the first byte that differs
                    let at = Seq.zip o n |> Seq.tryFindIndex (fun (a, b) -> a <> b) |> Option.defaultValue (min o.Length n.Length)
                    let clip (s: string) =
                        let from = max 0 (at - 150)
                        s.Substring(from, min 400 (s.Length - from))
                    report.AppendLine($"{label} (first difference at {at} of {o.Length}/{n.Length})\n--- old\n{clip o}\n--- new\n{clip n}") |> ignore
    let unlisted = differing |> Seq.filter (fun l -> not (p1Differences.Contains l || extra.Contains l)) |> List.ofSeq
    let names = String.Join("; ", unlisted)
    Assert.True(unlisted.IsEmpty, $"{what}: {unlisted.Length} unlisted differences: {names}\n{report}")
    eprintfn "%s: %d compared; %d differ, all on P1's list: %s" what compared differing.Count (String.Join(", ", differing))
    compared

let private compare what keep oldOf newOf = compareWith Set.empty what keep oldOf newOf

/// Recorded with 5.4 (class B): an attachment inside a foreign element (here an svg `option`) is replaced by markup parsed in that
/// element's context. Gumbo breaks the partial's HTML out of the svg; AngleSharp's fragment algorithm cannot, and `parseIn` stands a
/// `div` in for the context, which is equal when the partial's first tag is a breakout tag (a mention's span) and not when it is
/// `action-text-attachment` itself (it stays open here and holds the rendered partial; Gumbo leaves it empty before the partial).
/// The sanitizer drops svg and math with their contents, so the difference only shows where a body's own structure left the
/// attachment out of the foreign element.
let private foreignContextDifferences = set [ "corpus:fuzz 398" ]

[<Fact>]
let ``M1 canonical bodies (bytes) equal the old pipeline's on the 1,479, P1's list apart`` () =
    let compared = compare "M1 canonical" (fun _ -> true) canonicalOld canonicalNew
    Assert.True(compared >= 1315, $"{compared} inputs")

[<Fact>]
let ``M1 plain text equals the old pipeline's on the 1,479, P1's list apart`` () =
    compare
        "M1 plain text"
        (fun _ -> true)
        (fun body ctx -> ActionText.toPlainText body ctx |> shownR)
        (fun body ctx -> Next.ActionText.toPlainText body ctx |> shownR)
    |> ignore

[<Fact>]
let ``M1 mentions equal the old pipeline's on the 1,479, P1's list apart`` () =
    compare
        "M1 mentions"
        (fun _ -> true)
        (fun body ctx -> ActionText.mentionedUsers body ctx |> mentionsOf)
        (fun body ctx -> Next.ActionText.mentionedUsers body ctx |> mentionsOf)
    |> ignore

[<Fact>]
let ``C2 the corpus's canonical bodies are byte-equal to the old pipeline's, P1's list apart`` () =
    let compared = compare "C2" (fun l -> l.StartsWith "corpus:") canonicalOld canonicalNew
    Assert.Equal(658, compared)

[<Fact>]
let ``C3 the seed's plain text and mentions equal the old pipeline's`` () =
    let seeds = (let _, _, s = inputs.Force() in s)
    if seeds.IsEmpty then
        Assert.False(Environment.GetEnvironmentVariable "CAMPFIRE_REQUIRE_SEED" = "1", "the default seed is required")
        eprintfn "C3 skipped: parity/.seed/default is not built"
    else
        Assert.Equal(164, seeds.Length)
        let isSeed (l: string) = l.StartsWith "seed:"
        compare "C3 plain text" isSeed (fun b c -> ActionText.toPlainText b c |> shownR) (fun b c -> Next.ActionText.toPlainText b c |> shownR) |> ignore
        compare "C3 mentions" isSeed (fun b c -> ActionText.mentionedUsers b c |> mentionsOf) (fun b c -> Next.ActionText.mentionedUsers b c |> mentionsOf) |> ignore
        // The resolver makes every sgid a user, so the seed's mentions are really compared
        let withMentions =
            all.Force()
            |> List.filter (fun (l, _, _) -> isSeed l)
            |> List.filter (fun (_, b, c) -> match Next.ActionText.mentionedUsers b c with Ok(_ :: _) -> true | _ -> false)
        eprintfn "C3: %d of %d seed bodies mention someone" withMentions.Length seeds.Length

let private filteredOld (body: string) (ctx: RenderContext) =
    Content.load body ctx
    |> Result.bind (fun c -> Filters.apply c ctx)
    |> Result.map Content.toHtml
    |> shownR

let private filteredNew (body: string) (ctx: RenderContext) =
    Next.Content.load body ctx
    |> Result.bind (fun c -> Next.Filters.apply c ctx)
    |> Result.map Next.Content.toHtml
    |> shownR

let private layoutOld (body: string) (ctx: RenderContext) =
    Content.load body ctx |> Result.bind (fun c -> Content.toRenderedHtmlWithLayout c ctx) |> shownR

let private layoutNew (body: string) (ctx: RenderContext) =
    Next.Content.load body ctx |> Result.bind (fun c -> Next.Content.toRenderedHtmlWithLayout c ctx) |> shownR

let private editableOld (body: string) (ctx: RenderContext) =
    match ActionText.editableValue body ctx with
    | Ok(Some v) -> v
    | Ok None -> "<none>"
    | Error e -> $"error: {e.Message}"

let private editableNew (body: string) (ctx: RenderContext) =
    match Next.ActionText.editableValue body ctx with
    | Ok(Some v) -> v
    | Ok None -> "<none>"
    | Error e -> $"error: {e.Message}"

[<Fact>]
let ``M1 toRenderedHtmlWithLayout (the bot API body.html) equals the old pipeline's on the 1,479, P1's list apart`` () =
    compareWith foreignContextDifferences "M1 layout" (fun _ -> true) layoutOld layoutNew |> ignore

[<Fact>]
let ``M1 filtered content equals the old pipeline's on the 1,479, P1's list apart`` () =
    compare "M1 filters" (fun _ -> true) filteredOld filteredNew |> ignore

[<Fact>]
let ``M1 editor values equal the old pipeline's on the 1,479, P1's list apart`` () =
    compare "M1 editor" (fun _ -> true) editableOld editableNew |> ignore

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
    match Next.ActionText.editableValue body ctx with
    | Ok(Some v) ->
        Assert.DoesNotContain("action-text-attachment", v)
        Assert.Contains("hi", v)
        Assert.Contains("there", v)
    | other -> failwithf "unexpected: %A" other
    Assert.Equal(editableOld body ctx, editableNew body ctx)

/// Nested content attachments render 8 deep and no deeper, as in the old pipeline.
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
        let rendered = layoutNew body ctx
        Assert.Equal(layoutOld body ctx, rendered)
        // 8 content attachments deep still render; the 9th and below are empty
        let withoutAttributes = System.Text.RegularExpressions.Regex.Replace(rendered, "=\"[^\"]*\"", "")
        Assert.Equal(depth <= 8, withoutAttributes.Contains "<p>core</p>")

/// The attachment nodes of every body that has any: the resolution rules read the same attributes through either DOM.
[<Fact>]
let ``Attachables resolves every attachment of the corpus alike through either DOM's attributes`` () =
    let mutable nodes = 0
    for (label, body, ctx) in all.Force() do
        if not (p1Differences.Contains label) then
            let dom = Dom()
            match dom.ParseFragment body, Next.Html5.parseBody body with
            | Ok root, Ok newRoot ->
                let oldNodes = Content.attachmentNodes dom root
                let newNodes = Next.Content.attachmentNodes newRoot
                Assert.True((oldNodes.Length = newNodes.Length), label)
                for (o, n) in List.zip oldNodes newNodes do
                    nodes <- nodes + 1
                    let fromOld = Attachables.attachmentFrom (fun name -> dom.Attr(o, name)) ctx
                    let fromNew = Attachables.attachmentFrom (Next.Html5.attrsOf n) ctx
                    Assert.True((fromOld = fromNew), $"{label}: {fromOld} vs {fromNew}")
                    let embedOld = Attachables.opengraphEmbedFrom (fun name -> dom.Attr(o, name)) ctx
                    let embedNew = Attachables.opengraphEmbedFrom (Next.Html5.attrsOf n) ctx
                    Assert.True((embedOld = embedNew), label)
            | Error _, Error _ -> ()
            | o, n -> failwithf "%s: parsed on one side only (%A / %A)" label (Result.isOk o) (Result.isOk n)
    Assert.True(nodes > 100, $"only {nodes} attachment nodes")

/// Risk 10 / S6: what a canonical load and a plain text cost per seed body, old and new (a stopwatch, reported; the bound is
/// generous, the floor is judged by `bench/quick` in 5.6).
[<Fact>]
let ``S6 cost per seed body: canonical load with toHtml and plain text, old against new`` () =
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
        let oldLoad = measure canonicalOld
        let newLoad = measure canonicalNew
        let oldText = measure (fun b c -> ActionText.toPlainText b c |> shownR)
        let newText = measure (fun b c -> Next.ActionText.toPlainText b c |> shownR)
        report "load+toHtml old" oldLoad
        report "load+toHtml new" newLoad
        report "plain text old" oldText
        report "plain text new" newText
        let renderOf (f: string -> RenderContext -> string) = measure f
        let oldRender = renderOf layoutOld
        let newRender = renderOf layoutNew
        report "render old" oldRender
        report "render new" newRender
        Assert.True(fst newLoad < 200.0 && fst newText < 200.0, "a load or a plain text of a seed body costs over 200 us")
