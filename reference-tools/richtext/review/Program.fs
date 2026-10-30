// The Phase 5 security review's fuzz harness (plans/design-richtext.md section 11), kept so the review can be re-run.
//
//   dotnet run -c Release -- 250000 3          fuzz: corpus bodies, Rails' presentations, fixed hostile cases and N generated bodies
//                                              (seed) through every rich text entry point; each output checked in the browser model
//   dotnet run -c Release -- min SEED INDEX     shrink the input at INDEX of that run to the shortest that still throws in parseBody
//   dotnet run -c Release -- emit N FILE        write fixed + N generated cases as a corpus input file for generate.rb (see run.sh)
//   dotnet run -c Release -- compare BIG.json   ours against Rails' presentations on that corpus: survivors and browser-model checks
//
// Results land in the build output directory (violations.txt, mutations.txt, exceptions.txt, survivors.txt, differ.txt).
module Program

open System
open System.Collections.Generic
open System.Diagnostics
open System.IO
open System.Text.Json
open Campfire.RichText

/// The repository: the first directory above the build output that holds AGENTS.md.
let repo =
    let mutable dir = DirectoryInfo(AppContext.BaseDirectory)
    while not (isNull dir) && not (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) do
        dir <- dir.Parent
    if isNull dir then failwith "run from a build inside the repository" else dir.FullName

let hostileUser =
    { Id = 9001L
      Name = "<img src=x onerror=alert(1)>\"'&</span>"
      Title = "\"><img src=x onerror=alert(1)> – <script>alert(1)</script>"
      AttachableSgid = "sg\"id><img src=x onerror=alert(1)>"
      UserPath = "/users/9001"
      AvatarPath = "/users/9001/avatar?v=\"><img src=x onerror=alert(1)>" }

type HostileResolver(inner: IAttachableResolver) =
    interface IAttachableResolver with
        member _.LocateSigned sgid = if sgid = "hostile" then SignedLookup.User hostileUser else inner.LocateSigned sgid
        member _.FindGid gid = if gid = "gid://campfire/User/9001" then GidLookup.User hostileUser else inner.FindGid gid

let corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(repo, "tests/Campfire.RichText.Tests/corpus/expected.json")))
let resolver = HostileResolver(Campfire.RichText.Tests.CorpusResolver.resolverOf corpus.RootElement) :> IAttachableResolver
let ctx: RenderContext = { Resolver = resolver; RequestHost = Some "once.campfire.test" }

let corpusBodies = [| for c in corpus.RootElement.GetProperty("cases").EnumerateArray() -> c.GetProperty("body").GetString() |> nonNull |]
let railsPresentations =
    [| for c in corpus.RootElement.GetProperty("cases").EnumerateArray() do
           match c.GetProperty("presentation").TryGetProperty "ok" with
           | true, v when v.ValueKind = JsonValueKind.String -> yield v.GetString() |> nonNull
           | _ -> () |]

let exceptions = ResizeArray<string>()
let slow = ResizeArray<string>()
let report = Check.newReport ()
let mutable outputs = 0

let guarded (label: string) (input: string) (f: unit -> unit) =
    let sw = Stopwatch.StartNew()
    try
        f ()
    with e ->
        exceptions.Add $"{label}: {e.GetType().Name}: {e.Message}\n    input: {input}"
    if sw.ElapsedMilliseconds > 250L then slow.Add $"{label}: {sw.ElapsedMilliseconds} ms (len {input.Length})"

let checkOut label (html: string) list style =
    outputs <- outputs + 1
    Check.check label html list style report

let stripAndSanitize (html: string) =
    match Html5.parseBody html with
    | Error _ -> None
    | Ok root ->
        let text = String.Concat [ for n in Html5.descendants root do match n with :? AngleSharp.Dom.IText as t -> yield t.Data | _ -> () ]
        let sb = Text.StringBuilder()
        Html5.escapeText text sb
        match Sanitizer.sanitize (sb.ToString()) SafeList.defaults with
        | Ok s -> Some s
        | Error _ -> None

let runOne (i: int) (input: string) =
    let l (what: string) = $"#{i} {what}"
    guarded (l "presentMessage") input (fun () ->
        match ActionText.presentMessage input ctx with
        | Presentation.Html html -> checkOut (l "presentation") html SafeList.autoLink false
        | _ -> ())
    guarded (l "body.html") input (fun () ->
        match Content.load input ctx |> Result.bind (fun c -> Content.toRenderedHtmlWithLayout c ctx) with
        | Ok html -> checkOut (l "body.html") html SafeList.actionText true
        | Error _ -> ())
    for (name, list, style) in [ "defaults", SafeList.defaults, false; "actionText", SafeList.actionText, true; "contentFilter", SafeList.contentFilter, true; "autoLink", SafeList.autoLink, false ] do
        guarded (l ("sanitize " + name)) input (fun () ->
            match Sanitizer.sanitize input list with
            | Ok html -> checkOut (l ("sanitize " + name)) html list style
            | Error _ -> ())
    guarded (l "stored then presented") input (fun () ->
        match Content.load input ctx with
        | Ok c ->
            let stored = Content.toHtml c
            match ActionText.presentMessage stored ctx with
            | Presentation.Html html -> checkOut (l "stored presentation") html SafeList.autoLink false
            | _ -> ()
        | Error _ -> ())
    guarded (l "plain text") input (fun () -> ActionText.toPlainText input ctx |> ignore)
    guarded (l "mentions") input (fun () -> ActionText.mentionedUsers input ctx |> ignore)
    guarded (l "editable") input (fun () -> ActionText.editableValue input ctx |> ignore)
    guarded (l "unfurl strip_tags+sanitize") input (fun () ->
        match stripAndSanitize input with
        | Some s -> checkOut (l "unfurl text") s (SafeList([||], [||])) false
        | None -> ())

let throws (input: string) =
    try
        Html5.parseBody input |> ignore
        None
    with e -> Some e

/// Delta debugging: the shortest input (by removing chunks) that still throws.
let minimize (input: string) =
    let mutable s = input
    let mutable chunk = s.Length / 2
    while chunk >= 1 do
        let mutable i = 0
        let mutable progressed = false
        while i < s.Length do
            let candidate = s.Remove(i, Math.Min(chunk, s.Length - i))
            if (throws candidate).IsSome then
                s <- candidate
                progressed <- true
            else
                i <- i + chunk
        if not progressed then chunk <- chunk / 2
    s

let inputsFor (count: int) (seed: int) =
    let rng = Random(seed)
    Array.concat [ corpusBodies; railsPresentations; Gen.fixedCases; [| for _ in 1 .. count -> Gen.next rng |] ]

let minMain (argv: string[]) =
    let index = int argv[2]
    let input = (inputsFor index (int argv[1]))[index]
    match throws input with
    | None -> printfn "does not throw"
    | Some e ->
        printfn "%O" e
        let m = minimize input
        printfn "MIN %A" m
    0

let fuzzMain (argv: string[]) =
    let count = if argv.Length > 0 then int argv[0] else 20000
    let seed = if argv.Length > 1 then int argv[1] else 7
    let inputs = inputsFor count seed
    let sw = Stopwatch.StartNew()
    inputs |> Array.iteri runOne
    let distinct (xs: ResizeArray<string>) = xs |> Seq.map (fun s -> s.Substring(s.IndexOf ' ' + 1)) |> Seq.countBy (fun s -> (s.Split '\n').[0]) |> Seq.sortByDescending snd
    printfn "inputs %d, outputs checked %d, %.1f s" inputs.Length outputs sw.Elapsed.TotalSeconds
    printfn "violations %d, mutations %d, exceptions %d, slow %d" report.Violations.Count report.Mutations.Count exceptions.Count slow.Count
    printfn "--- violations by kind"
    for (k, n) in distinct report.Violations |> Seq.truncate 40 do printfn "%6d %s" n k
    printfn "--- mutations by kind"
    for (k, n) in distinct report.Mutations |> Seq.truncate 40 do printfn "%6d %s" n k
    printfn "--- exceptions by kind"
    for (k, n) in distinct exceptions |> Seq.truncate 40 do printfn "%6d %s" n k
    let dump (name: string) (xs: ResizeArray<string>) = File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, name), xs |> Seq.truncate 3000)
    dump "violations.txt" report.Violations
    dump "mutations.txt" report.Mutations
    dump "exceptions.txt" exceptions
    dump "slow.txt" slow
    0

/// Writes a Rails corpus input file (JSON is YAML) of the fixed hostile cases and `count` generated ones.
let emitMain (argv: string[]) =
    let count = int argv[1]
    let path = argv[2]
    let rng = Random(11)
    let noLoneSurrogates (b: string) =
        let sb = Text.StringBuilder()
        let mutable i = 0
        while i < b.Length do
            let c = b[i]
            if Char.IsHighSurrogate c && i + 1 < b.Length && Char.IsLowSurrogate b[i + 1] then
                sb.Append(c).Append(b[i + 1]) |> ignore
                i <- i + 2
            else
                sb.Append(if Char.IsSurrogate c then '?' else c) |> ignore
                i <- i + 1
        sb.ToString()
    let bodies = Array.append Gen.fixedCases [| for _ in 1 .. count -> Gen.next rng |] |> Array.map noLoneSurrogates
    let cases =
        bodies
        |> Array.mapi (fun i b -> {| name = $"review {i}"; body = b.Replace("sgid=\"hostile\"", "sgid=\"{{sgid:david}}\"").Replace("\\\"sgid\\\":\\\"hostile\\\"", "\\\"sgid\\\":\\\"{{sgid:david}}\\\"") |})
    File.WriteAllText(path, JsonSerializer.Serialize({| cases = cases |}))
    printfn "wrote %d cases" cases.Length
    0

/// Elements (with namespaces), attribute names and URI values in `ours` that `rails` lacks (N1).
let survivors (ours: string) (rails: string) =
    let facts (html: string) =
        let e = HashSet<string>()
        let a = HashSet<string>()
        let u = HashSet<string>()
        match Html5.parseBody html with
        | Ok root ->
            for n in Html5.descendants root do
                match n with
                | :? AngleSharp.Dom.IElement as el ->
                    e.Add(el.NamespaceUri + "|" + el.LocalName) |> ignore
                    for at in el.Attributes do
                        let name = Html5.qualifiedName at
                        a.Add name |> ignore
                        if Check.urlAttributes.Contains name then u.Add(name + "=" + at.Value) |> ignore
                | _ -> ()
        | Error _ -> ()
        e, a, u
    let (oe, oa, ou) = facts ours
    let (re, ra, ru) = facts rails
    [ for x in oe do if not (re.Contains x) then yield "element " + x
      for x in oa do if not (ra.Contains x) then yield "attribute " + x
      for x in ou do if not (ru.Contains x) then yield "URI " + x ]

let compareMain (argv: string[]) =
    use doc = JsonDocument.Parse(File.ReadAllText argv[1])
    let resolver = Campfire.RichText.Tests.CorpusResolver.resolverOf doc.RootElement
    let ctx: RenderContext = { Resolver = resolver; RequestHost = Some "once.campfire.test" }
    let mutable cases = 0
    let mutable equal = 0
    let mutable railsRaised = 0
    let survivorsFound = ResizeArray<string>()
    let railsReport = Check.newReport ()
    let differ = ResizeArray<string>()
    for c in doc.RootElement.GetProperty("cases").EnumerateArray() do
        cases <- cases + 1
        let name = c.GetProperty("name").GetString() |> nonNull
        let body = c.GetProperty("body").GetString() |> nonNull
        let rails =
            match c.GetProperty("presentation").TryGetProperty "ok" with
            | true, v when v.ValueKind = JsonValueKind.String -> Some(v.GetString() |> nonNull)
            | _ -> None
        let ours = try (match ActionText.presentMessage body ctx with Presentation.Html h -> Some h | _ -> None) with e -> Some("EXCEPTION " + e.GetType().Name)
        match rails, ours with
        | Some r, Some o ->
            Check.check (name + " ours") o SafeList.autoLink false report
            Check.check (name + " rails") r SafeList.autoLink false railsReport
            if r = o then equal <- equal + 1
            else
                differ.Add $"{name}\n  body:  {body}\n  rails: {r}\n  ours:  {o}"
                for s in survivors o r do survivorsFound.Add $"{name}: {s}\n  body:  {body}\n  rails: {r}\n  ours:  {o}"
        | None, _ -> railsRaised <- railsRaised + 1
        | Some r, None -> differ.Add $"{name}: ours unrenderable; rails {r}"
    printfn "cases %d, byte-equal %d, differ %d, rails raised %d" cases equal differ.Count railsRaised
    printfn "survivors (in ours, not in Rails): %d" survivorsFound.Count
    for (k, n) in survivorsFound |> Seq.countBy (fun s -> (s.Split '\n').[0].Substring((s.Split '\n').[0].IndexOf ':' + 2)) |> Seq.sortByDescending snd |> Seq.truncate 40 do printfn "%6d %s" n k
    printfn "browser-model violations: ours %d, rails %d" report.Violations.Count railsReport.Violations.Count
    for (k, n) in report.Violations |> Seq.countBy (fun s -> (s.Split '\n').[0].Substring((s.Split '\n').[0].IndexOf(" ours: ") + 1)) |> Seq.sortByDescending snd |> Seq.truncate 20 do printfn "  ours %6d %s" n k
    for (k, n) in railsReport.Violations |> Seq.countBy (fun s -> let l = (s.Split '\n').[0] in l.Substring(l.IndexOf(" rails: ") + 1)) |> Seq.sortByDescending snd |> Seq.truncate 20 do printfn "  rails %6d %s" n k
    File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "survivors.txt"), survivorsFound)
    File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "differ.txt"), differ)
    File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "cmp-violations.txt"), Seq.append report.Violations railsReport.Violations)
    0

[<EntryPoint>]
let main argv =
    match argv with
    | [||] -> fuzzMain argv
    | _ when argv[0] = "min" -> minMain argv
    | _ when argv[0] = "emit" -> emitMain argv
    | _ when argv[0] = "compare" -> compareMain argv
    | _ -> fuzzMain argv
