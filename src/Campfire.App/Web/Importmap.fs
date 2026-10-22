// plans/design-assets.md section 3, unit 6.4: importmap-rails 2.2.2's `pin` and `pin_all_from` over the asset table, and the tags.
//
// Reference: importmap-rails lib/importmap/map.rb and helpers. `pin_all_from` lists the table's logical paths under its prefix
// (every `pin_all_from` of reference/config/importmap.rb names a load-path root's subdirectory equal to its `under:`), so the
// build's directories are never read at run time; T4 pins the result to Rails' map.
namespace Campfire.App.Web

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open Microsoft.AspNetCore.Components
open Campfire.Ruby

type Pin = { Name: string; Path: string; Preload: bool }

module Importmap =
    type private Value =
        | Str of string
        | Bool of bool

    // `"string"`/`'string'`, then `, key: "string"|true|false` options; a trailing `# comment` ends the line.
    let private quoted = Regex("""^(["'])(.*?)\1\s*""", RegexOptions.CultureInvariant)
    let private option = Regex("""^,\s*(\w+):\s*(?:(["'])(.*?)\2|(true|false))\s*""", RegexOptions.CultureInvariant)

    let private parse (line: string) : (string * string * (string * Value) list) option =
        let line = line.Trim()
        if line = "" || line.StartsWith '#' then None else
        let space = line.IndexOfAny [| ' '; '\t' |]
        let command, rest = if space < 0 then line, "" else line.Substring(0, space), line.Substring(space + 1).TrimStart()
        let first = quoted.Match rest
        if not first.Success then failwith $"importmap.rb: expected a string in {line}"
        let mutable rest = rest.Substring first.Length
        let options = ResizeArray<string * Value>()
        let mutable more = true
        while more && rest <> "" && not (rest.StartsWith '#') do
            let m = option.Match rest
            if not m.Success then failwith $"importmap.rb: can't parse {rest} in {line}"
            options.Add(m.Groups[1].Value, (if m.Groups[4].Success then Bool(m.Groups[4].Value = "true") else Str m.Groups[3].Value))
            rest <- rest.Substring m.Length
        match command with
        | "pin" | "pin_all_from" -> Some(command, first.Groups[2].Value, List.ofSeq options)
        | other -> failwith $"importmap.rb: unsupported statement {other}"

    /// `[under, filename.chomp(extname).remove(/(?:\/|^)index$/).presence].compact.join("/")`
    let private moduleName (under: string option) (filename: string) : string =
        let stem = filename.Substring(0, filename.Length - (if filename.EndsWith ".jsm" then 4 else 3))
        let stem = if stem = "index" then "" elif stem.EndsWith "/index" then stem.Substring(0, stem.Length - 6) else stem
        match under, stem with
        | Some under, "" -> under
        | Some under, stem -> $"{under}/{stem}"
        | None, stem -> stem

    /// Importmap::Map#expanded_packages_and_directories: pins in insertion order, then every directory expanded; a later entry for an
    /// existing name replaces it in place (a Ruby Hash). A pin whose file is not in `logicalPaths` is skipped (Propshaft's rescued
    /// `MissingAssetError`). An unsupported statement or option raises.
    let pins (importmapRb: string) (logicalPaths: string seq) : Pin list =
        let table = HashSet<string>(logicalPaths, StringComparer.Ordinal)
        let packages = ResizeArray<Pin>()
        let directories = ResizeArray<string * string option * string * bool>()
        let insert (pin: Pin) =
            match packages.FindIndex(fun p -> p.Name = pin.Name) with
            | -1 -> packages.Add pin
            | i -> packages[i] <- pin
        for line in importmapRb.Split '\n' do
            match parse line with
            | None -> ()
            | Some(command, first, options) ->
                let mutable target, under, preload = None, None, true
                for key, value in options do
                    match key, value with
                    | "to", Str v -> target <- Some v
                    | "under", Str v -> under <- Some v
                    | "preload", Bool v -> preload <- v
                    | _ -> failwith $"importmap.rb: unsupported option {key} in {line.Trim()}"
                if command = "pin" then
                    insert { Name = first; Path = defaultArg target $"{first}.js"; Preload = preload }
                else
                    let prefix = target |> Option.orElse under |> Option.defaultWith (fun () -> failwith $"importmap.rb: pin_all_from {first} needs under: or to:")
                    // `@directories[dir] = ...`: a Ruby Hash, so a second `pin_all_from` of a directory keeps the first one's place.
                    let entry = (first, under, prefix, preload)
                    match directories.FindIndex(fun (d, _, _, _) -> d = first) with
                    | -1 -> directories.Add entry
                    | i -> directories[i] <- entry
        for _, under, prefix, preload in directories do
            let prefix = prefix + "/"
            // `Dir[...].sort`: Ruby sorts the file names as strings, byte by byte ("a-b.js" before "a/b.js", "a.js" before "a/b.js").
            for logical in table |> Seq.filter (fun l -> l.StartsWith(prefix, StringComparison.Ordinal) && (l.EndsWith ".js" || l.EndsWith ".jsm")) |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b)) do
                insert { Name = moduleName under (logical.Substring prefix.Length); Path = logical; Preload = preload }
        packages |> Seq.filter (fun p -> table.Contains p.Path) |> List.ofSeq

    /// `javascript_importmap_tags`: the import map (`ImportMapDefinition`'s JSON) in its script element, one modulepreload link per
    /// preloaded pin's distinct URL, then the module script.
    let tags (pins: Pin list) (route: string -> string voption) : string =
        let routed = [ for p in pins do match route p.Path with ValueSome url -> p, url | ValueNone -> () ]
        let imports = Dictionary<string, string>()
        for p, url in routed do imports[p.Name] <- url
        let map = ImportMapDefinition(imports, null, null)
        let preloads =
            routed |> List.filter (fun (p, _) -> p.Preload) |> List.map snd |> List.distinct
            |> List.map (fun url -> $"<link rel=\"modulepreload\" href=\"{Erb.htmlEscape url}\">")
        String.concat "\n" [ $"<script type=\"importmap\" data-turbo-track=\"reload\">{map}</script>"; yield! preloads; "<script type=\"module\">import \"application\"</script>" ]
