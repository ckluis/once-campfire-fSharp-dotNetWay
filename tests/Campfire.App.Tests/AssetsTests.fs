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
