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
let private compare (what: string) (keep: string -> bool) (oldOf: string -> RenderContext -> string) (newOf: string -> RenderContext -> string) =
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
                if not (p1Differences.Contains label) then
                    let clip (s: string) = if s.Length > 400 then s.Substring(0, 400) + "..." else s
                    report.AppendLine($"{label}\n--- old\n{clip o}\n--- new\n{clip n}") |> ignore
    let unlisted = differing |> Seq.filter (fun l -> not (p1Differences.Contains l)) |> List.ofSeq
    let names = String.Join("; ", unlisted)
    Assert.True(unlisted.IsEmpty, $"{what}: {unlisted.Length} unlisted differences: {names}\n{report}")
    eprintfn "%s: %d compared; %d differ, all on P1's list: %s" what compared differing.Count (String.Join(", ", differing))
    compared

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
        Assert.True(fst newLoad < 200.0 && fst newText < 200.0, "a load or a plain text of a seed body costs over 200 us")
