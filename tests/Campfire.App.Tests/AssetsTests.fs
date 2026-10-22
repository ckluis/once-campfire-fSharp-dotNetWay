// Unit 6.2 (plans/design-assets.md section 5, S1): the staged web root.
module Campfire.App.Tests.AssetsTests

open System.IO
open System.Text.Json
open Xunit
open Campfire.Tests

let private assetsDir = Repo.path "src/Campfire.App/Assets"

let private files (dir: string) : string list =
    if not (Directory.Exists dir) then [] else
    Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
    |> Seq.map (fun f -> Path.GetRelativePath(dir, f).Replace('\\', '/'))
    |> Seq.sort
    |> List.ofSeq

/// The source of each logical path as Propshaft finds it: the overrides first, then the load path in order.
let private sources () : Map<string, string> =
    let loadPath =
        File.ReadAllLines(Path.Combine(assetsDir, "vendor/LOAD_PATH"))
        |> Array.filter (fun (l: string) -> l.Trim() <> "")
        |> Array.map (fun (l: string) ->
            match l.Split(':', 2) with
            | [| "reference"; dir |] -> Repo.path $"reference/{dir}"
            | [| "vendor"; dir |] -> Path.Combine(assetsDir, "vendor", dir)
            | _ -> failwith $"LOAD_PATH: bad line {l}")
    let skip (rel: string) : bool =
        Path.GetFileName(rel).StartsWith "." || rel.EndsWith ".gz" || rel.EndsWith ".br"
    let add (dir: string) (acc: Map<string, string>) : Map<string, string> =
        files dir
        |> List.filter (skip >> not)
        |> List.fold (fun (m: Map<string, string>) (rel: string) -> if m.ContainsKey rel then m else m.Add(rel, Path.Combine(dir, rel))) acc
    // Overrides are not filtered for dotfiles or compressed files by the target either (it only filters the load path).
    let overrides =
        files (Path.Combine(assetsDir, "overrides"))
        |> List.map (fun rel -> rel, Path.Combine(assetsDir, "overrides", rel))
        |> Map.ofList
    Array.fold (fun acc dir -> add dir acc) overrides loadPath

[<Fact>]
let ``S1 the staged web root holds Rails' manifest minus the lexxy compressed files plus install-edge.svg, with the sources' bytes`` () =
    let webRoot = Path.Combine(Repo.path "src/Campfire.App", "wwwroot/assets")
    Assert.True(Directory.Exists webRoot, "wwwroot/assets is missing: dotnet build stages it")
    let staged = files webRoot
    let manifest = JsonDocument.Parse(File.ReadAllText(Repo.path "tests/Campfire.Assets.Tests/reference/manifest.json")).RootElement
    let expected =
        [ for p in manifest.EnumerateObject() do
              let name = p.Name
              if not (name.StartsWith "lexxy" && (name.EndsWith ".gz" || name.EndsWith ".br")) then name
          "install-edge.svg" ]
        |> List.sort
    Assert.Equal<string list>(expected, staged)
    let sources = sources ()
    Assert.Equal<string list>(expected, sources |> Map.keys |> List.ofSeq |> List.sort)
    for logical in staged do
        Assert.True(
            File.ReadAllBytes(Path.Combine(webRoot, logical)) = File.ReadAllBytes sources[logical],
            $"{logical}: staged bytes differ from {sources[logical]}")

// --- Unit 6.3 (design section 5, H1-H4): the asset endpoints behind the old public files --------------------------------

let private stagedRoot = Path.Combine(Repo.path "src/Campfire.App", "wwwroot/assets")

let private route (logical: string) : string = Campfire.App.Web.StaticAssets.path logical

let private stagedBytes (logical: string) : byte[] = File.ReadAllBytes(Path.Combine(stagedRoot, logical))

let private gunzip (bytes: byte[]) : byte[] =
    use input = new MemoryStream(bytes)
    use gz = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress)
    use output = new MemoryStream()
    gz.CopyTo output
    output.ToArray()

[<Fact>]
let ``H1 every table route answers 200 with the source's bytes, gzip with Accept-Encoding, HEAD without a body`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let logicals = files stagedRoot
        Assert.True(logicals.Length > 300, $"{logicals.Length} staged assets")
        let mutable gzipped = 0
        for logical in logicals do
            let url = route logical
            Assert.Matches(@"^/assets/.+\.[0-9a-z]{10}\.[^.]+$", url)
            let! plain = test.Send(Campfire.App.Tests.Support.get url)
            Assert.True((plain.Status = 200), $"{url}: {plain.Status}")
            Assert.True((plain.Body = stagedBytes logical), $"{url}: body differs from the source")
            Assert.True(plain.Header("content-encoding").IsNone, $"{url}: identity request got an encoding")
            let! encoded = test.Send((Campfire.App.Tests.Support.get url).With("accept-encoding", "gzip"))
            Assert.Equal(200, encoded.Status)
            match encoded.Header "content-encoding" with
            | Some "gzip" ->
                gzipped <- gzipped + 1
                Assert.True((gunzip encoded.Body = stagedBytes logical), $"{url}: gzip body differs from the source")
            | _ -> Assert.True((encoded.Body = stagedBytes logical), $"{url}: unencoded body differs")
        Assert.True(gzipped > 100, $"{gzipped} gzip variants")
        let sample = route "composer.css"
        let! head = test.Send(Campfire.App.Tests.Support.request "HEAD" sample)
        Assert.Equal(200, head.Status)
        Assert.Empty head.Body
    }

[<Fact>]
let ``H2 the ETag of a fingerprinted URL gives 304, and Range on a sound gives 206 with 10 bytes`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let url = route "composer.css"
        let! first = test.Send(Campfire.App.Tests.Support.get url)
        let etag = (first.Header "etag").Value
        let! again = test.Send((Campfire.App.Tests.Support.get url).With("if-none-match", etag))
        Assert.Equal(304, again.Status)
        Assert.Empty again.Body
        let sound = files stagedRoot |> List.find (fun f -> f.EndsWith ".mp3")
        let! ranged = test.Send((Campfire.App.Tests.Support.get (route sound)).With("range", "bytes=0-9"))
        Assert.Equal(206, ranged.Status)
        Assert.Equal<byte[]>((stagedBytes sound)[0..9], ranged.Body)
    }

[<Fact>]
let ``H3 an asset response has no x-request-id and no x-runtime, a page response has both`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let! asset = test.Send(Campfire.App.Tests.Support.get (route "composer.css"))
        Assert.Equal(200, asset.Status)
        Assert.True(asset.Header("x-request-id").IsNone)
        Assert.True(asset.Header("x-runtime").IsNone)
        let! page = test.Send(Campfire.App.Tests.Support.get "/session/new")
        Assert.Equal(200, page.Status)
        Assert.True(page.Header("x-request-id").IsSome)
        Assert.True(page.Header("x-runtime").IsSome)
    }

[<Fact>]
let ``H4 an unknown asset is the app's 404 and POST to an asset URL is 405 (class B)`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let! missing = test.Send(Campfire.App.Tests.Support.get "/assets/nope.css")
        Assert.Equal(404, missing.Status)
        Assert.True(missing.Header("x-request-id").IsSome, "the app's own 404, not an endpoint's")
        let! post = test.Send(Campfire.App.Tests.Support.request "POST" (route "composer.css"))
        Assert.Equal(405, post.Status)
    }

[<Fact>]
let ``a host built without configureHost fails at boot, naming the cause`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let! failure =
            Assert.ThrowsAnyAsync<exn>(fun () ->
                Campfire.App.Tests.Support.startPipelineOn
                    (Campfire.App.Web.StaticAssets.hostOptions [||])
                    ignore
                    test.Booted.ConfigureServices
                    test.Booted.Pipeline
                :> System.Threading.Tasks.Task)
        Assert.Contains("UseStaticWebAssets", failure.Message)
        Assert.Contains("not in the web root", failure.Message)
    }
