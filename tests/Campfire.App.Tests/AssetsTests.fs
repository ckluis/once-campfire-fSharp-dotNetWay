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
    // The target leaves dotfiles out of the overrides too (none exist), but not compressed files.
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
    let manifest = JsonDocument.Parse(File.ReadAllText(Repo.path "tests/Campfire.App.Tests/Assets/reference/manifest.json")).RootElement
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

/// Security review (design-assets.md section 11, F3): every SVG and HTML file the asset endpoints serve is opened in the app's own
/// origin when a browser navigates to it, with no CSP and no nosniff (Rails' `Static` sends neither), so none may carry script. Read
/// from the sources (overrides first, then the load path), so a re-vendored or added file is caught before it is staged.
[<Fact>]
let ``no SVG or HTML among the assets carries script`` () =
    let active = System.Text.RegularExpressions.Regex(@"<script|<foreignObject|javascript:|<iframe|<embed|<object|\son[a-z]+\s*=", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
    let markup = sources () |> Map.filter (fun logical _ -> logical.EndsWith ".svg" || logical.EndsWith ".html" || logical.EndsWith ".htm" || logical.EndsWith ".xml")
    Assert.True(markup.Count > 50, $"only {markup.Count} SVG/HTML assets found")
    for KeyValue(logical, source) in markup do
        let m = active.Match(File.ReadAllText source)
        Assert.False(m.Success, $"{logical} ({source}) carries active content: {m.Value}")

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
let ``nothing outside the asset table is served: encoded dots and backslashes, the build output, the web root by name`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        for url in
            [ "/assets/..%2Fcampfire.dll"; "/assets/%2e%2e/campfire.dll"; "/assets/%2e%2e%2fcampfire.dll"; "/assets/..%5Ccampfire.dll"
              "/assets/..%5C..%5Cappsettings.json"; "/campfire.dll"; "/campfire.staticwebassets.endpoints.json"
              "/campfire.staticwebassets.runtime.json"; "/wwwroot/robots.txt"; "/wwwroot/assets/composer.css"; "/assets/.keep"
              "/assets/%2e%2e/%2e%2e/reference/config/importmap.rb"
              // Security review (design-assets.md section 11): double encoding, overlong UTF-8, NUL, mixed dots, doubled slashes.
              "/assets/%252e%252e/campfire.dll"; "/assets/%c0%ae%c0%ae/campfire.dll"; "/assets/composer.css%00"; "/assets/.%2e/campfire.dll"
              "/assets//..//campfire.dll"; "/assets/..;/campfire.dll"; "/ASSETS/NOPE.CSS"; "/.env"; "/.git/config" ] do
            let! response = test.Send(Campfire.App.Tests.Support.get url)
            Assert.True((response.Status = 404 || response.Status = 400), $"{url}: {response.Status}")
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

// --- Unit 6.4 (design section 5, T1-T6): the table, the tags and the import map against Rails' own output ----------------------

open System
open System.Security.Cryptography
open System.Text.RegularExpressions
open Campfire.App.Web

let private fixture (name: string) : string =
    File.ReadAllText(Repo.path $"tests/Campfire.App.Tests/Assets/reference/{name}")

/// Rails' manifest: logical path to digested path, in the file's order.
let private railsManifest () : (string * string) list =
    [ for p in (JsonDocument.Parse(fixture "manifest.json")).RootElement.EnumerateObject() ->
          p.Name, nonNull (p.Value.GetProperty("digested_path").GetString()) ]

/// Digested path (no leading slash) to logical path.
let private railsLogical () : Map<string, string> =
    railsManifest () |> List.map (fun (logical, digested) -> digested, logical) |> Map.ofList

/// The A15 mask: Propshaft's `-<hex digest>` and the SDK's `.<10 [0-9a-z]>` fingerprint become one placeholder.
let private assetDigest =
    Regex(@"/assets/([^\s""'()?#]+?)(?:-([0-9a-f]{7,64})|\.([0-9a-z]{10}))(\.[A-Za-z0-9.]+)")

let private mask (text: string) : string =
    assetDigest.Replace(text, MatchEvaluator(fun m -> $"/assets/{m.Groups[1].Value}-«digest»{m.Groups[4].Value}"))

/// The logical path an `/assets/...` URL names in Rails' output.
let private railsPathOf (url: string) : string = (railsLogical ())[url.Substring "/assets/".Length]

let private hrefs (text: string) (rel: string) : string list =
    [ for m in Regex.Matches(text, $"<link rel=\"{rel}\" href=\"([^\"]+)\"") -> m.Groups[1].Value ]

/// The A12 list: Rails' compiled files that differ from the staged sources, by logical path.
let private a12 =
    [ "controllers/copy_to_clipboard_controller.js"; "lib/autocomplete/base_autocomplete_handler.js"; "models/file_uploader.js"
      "avatars.css"; "composer.css"; "lexxy.css"; "lexxy-content.css"; "lexxy-editor.css"
      "lexxy.js"; "stimulus.min.js"; "turbo.min.js" ]
    |> List.sort

let private booted () = Campfire.App.Tests.Support.bootSeeded "default"

[<Fact>]
let ``T1 the table's logical paths are the staged set, every route has the fingerprint form, path raises MissingAsset`` () =
    task {
        use! _test = booted ()
        let table = StaticAssets.logicalPaths () |> List.sort
        Assert.Equal<string list>(files stagedRoot, table)
        for logical in table do
            Assert.Matches(@"^/assets/.+\.[0-9a-z]{10}\.[^./]+$", StaticAssets.path logical)
        let missing = Assert.Throws<MissingAsset>(fun () -> StaticAssets.path "nope.css" |> ignore)
        Assert.Equal("nope.css", missing.logicalPath)
    }

[<Fact>]
let ``T2 the bytes equal Rails' compiled files except exactly A12's 11`` () =
    task {
        use! _test = booted ()
        let logical = railsLogical ()
        let compiled = (JsonDocument.Parse(fixture "compiled_sha256.json")).RootElement.EnumerateObject() |> List.ofSeq
        Assert.Equal(314, compiled.Length)
        let differs =
            [ for p in compiled do
                  let name = logical[p.Name]
                  // lexxy's .gz and .br are in Rails' manifest and are not served under their own names.
                  if not (name.StartsWith "lexxy" && (name.EndsWith ".gz" || name.EndsWith ".br")) then
                      let ours = Convert.ToHexStringLower(SHA256.HashData(stagedBytes name))
                      if ours <> nonNull (p.Value.GetString()) then name ]
            |> List.sort
        Assert.Equal<string list>(a12, differs)
    }

[<Fact>]
let ``T3 stylesheetTags are Rails' 31 stylesheets in Rails' order, equal line for line after the mask`` () =
    task {
        use! _test = booted ()
        let rails = fixture "stylesheet_link_tag_all.html"
        let ours = StaticAssets.stylesheetTags ()
        let railsHrefs, ourHrefs = hrefs rails "stylesheet", hrefs ours "stylesheet"
        Assert.Equal(31, railsHrefs.Length)
        let reverse = StaticAssets.logicalPaths () |> List.map (fun l -> StaticAssets.path l, l) |> Map.ofList
        Assert.Equal<string list>(railsHrefs |> List.map railsPathOf, ourHrefs |> List.map (fun h -> reverse[h]))
        let lines (t: string) = (mask t).Split '\n' |> List.ofArray
        Assert.Equal<string list>(lines rails, lines ours)
    }

[<Fact>]
let ``T4 importmapTags have Rails' 89 pins and modulepreloads in Rails' order and targets, the same text after the mask`` () =
    task {
        use! _test = booted ()
        let rails = fixture "javascript_importmap_tags.html"
        let ours = StaticAssets.importmapTags ()
        let pinsOf (text: string) : (string * string) list =
            let json = Regex.Match(text, @"<script type=""importmap""[^>]*>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value
            [ for p in (JsonDocument.Parse json).RootElement.GetProperty("imports").EnumerateObject() -> p.Name, nonNull (p.Value.GetString()) ]
        let railsPins, ourPins = pinsOf rails, pinsOf ours
        Assert.Equal(89, railsPins.Length)
        let reverse = StaticAssets.logicalPaths () |> List.map (fun l -> StaticAssets.path l, l) |> Map.ofList
        Assert.Equal<(string * string) list>(
            railsPins |> List.map (fun (n, u) -> n, railsPathOf u),
            ourPins |> List.map (fun (n, u) -> n, reverse[u]))
        let railsPreload, ourPreload = hrefs rails "modulepreload", hrefs ours "modulepreload"
        Assert.Equal(89, railsPreload.Length)
        Assert.Equal<string list>(railsPreload |> List.map railsPathOf, ourPreload |> List.map (fun u -> reverse[u]))
        Assert.EndsWith("\n<script type=\"module\">import \"application\"</script>", ours)
        Assert.Equal(mask rails, mask ours)
    }

[<Fact>]
let ``T5 preloadLinks name Rails' first stylesheets, fit 1,000 bytes and stop at the first link that would not`` () =
    task {
        use! _test = booted ()
        let header = StaticAssets.preloadLinks ()
        let railsLinks = Regex.Matches(fixture "link_header.txt", "<([^>]+)>") |> Seq.map (fun m -> m.Groups[1].Value) |> List.ofSeq
        let links = Regex.Matches(header, "<([^>]+)>") |> Seq.map (fun m -> m.Groups[1].Value) |> List.ofSeq
        let reverse = StaticAssets.logicalPaths () |> List.map (fun l -> StaticAssets.path l, l) |> Map.ofList
        let ourLogical = links |> List.map (fun h -> reverse[h])
        Assert.Equal<string list>(railsLinks |> List.map railsPathOf |> List.truncate ourLogical.Length, ourLogical)
        Assert.True(ourLogical.Length >= 15, $"{ourLogical.Length} links")
        let size = Text.Encoding.UTF8.GetByteCount header
        // Rails' `send_preload_links_header`: `break if header.bytesize + link.bytesize > 1000` before the comma is added, so the
        // header is a prefix of the stylesheets, at most 1,001 bytes, and the next stylesheet is the first that does not fit.
        let all = hrefs (StaticAssets.stylesheetTags ()) "stylesheet"
        Assert.Equal<string list>(all |> List.truncate links.Length, links)
        Assert.True(size <= 1001, $"{size} bytes")
        let next = all[links.Length]
        Assert.True(size + Text.Encoding.UTF8.GetByteCount $"<{next}>; rel=preload; as=style; nopush" > 1000, $"{next} would still fit")
    }

let private pinsFrom (rb: string) (paths: string list) =
    Importmap.pins rb paths |> List.map (fun p -> p.Name, p.Path, p.Preload)

[<Fact>]
let ``T6 Importmap.pins on small sources`` () =
    // A later pin of a name replaces it in place.
    Assert.Equal<(string * string * bool) list>(
        [ "a", "b.js", true; "c", "c.js", true ],
        pinsFrom "pin \"a\"\npin \"c\"\npin \"a\", to: \"b.js\"  # again\n" [ "a.js"; "b.js"; "c.js" ])
    // index.js gives the directory's name; modules sort as Ruby's `Dir[...].sort` sorts strings (byte order: '-' and '.' before
    // '/'); .jsm counts; other files do not.
    Assert.Equal<(string * string * bool) list>(
        [ "ctl/a-b", "ctl/a-b.jsm", true; "ctl/a", "ctl/a.js", true; "ctl/a/b", "ctl/a/b.js", true; "ctl", "ctl/index.js", true; "ctl/z", "ctl/z.js", true ],
        pinsFrom "pin_all_from \"app/javascript/ctl\", under: \"ctl\"" [ "ctl/z.js"; "ctl/a/b.js"; "ctl/a-b.jsm"; "ctl/a.js"; "ctl/index.js"; "ctl/x.css"; "other/q.js" ])
    // `to:` without `under:` names the modules without a prefix (Ruby's `[nil, name].compact`); a second `pin_all_from` of one
    // directory keeps the first one's place.
    Assert.Equal<(string * string * bool) list>(
        [ "x", "lib/x.js", true; "c/y", "c/y.js", false ],
        pinsFrom "pin_all_from \"app/javascript/lib\", to: \"lib\", preload: false\npin_all_from \"app/javascript/c\", under: \"c\", preload: false\npin_all_from \"app/javascript/lib\", to: \"lib\"" [ "lib/x.js"; "c/y.js" ])
    // preload: false keeps a pin out of the modulepreloads.
    let pins = Importmap.pins "pin \"a\", preload: false\npin \"b\"" [ "a.js"; "b.js" ]
    let tags = Importmap.tags pins (fun l -> ValueSome $"/assets/{l}")
    Assert.Contains("<link rel=\"modulepreload\" href=\"/assets/b.js\">", tags)
    Assert.DoesNotContain("modulepreload\" href=\"/assets/a.js", tags)
    Assert.Contains("\"a\": \"/assets/a.js\"", tags)
    // A pin to a missing file is skipped.
    Assert.Equal<(string * string * bool) list>([ "b", "b.js", true ], pinsFrom "pin \"a\"\npin \"b\"" [ "b.js" ])
    // An unsupported statement or option raises.
    Assert.ThrowsAny<exn>(fun () -> pinsFrom "pin_all_from_x \"a\"" [] |> ignore) |> ignore
    Assert.ThrowsAny<exn>(fun () -> pinsFrom "pin \"a\", integrity: true" [ "a.js" ] |> ignore) |> ignore
    Assert.ThrowsAny<exn>(fun () -> pinsFrom "pin \"a\", to: foo" [ "a.js" ] |> ignore) |> ignore

// --- Unit 6.5 (design section 5, H7-H8): the pages' asset URLs are the table's fingerprinted ones ---------------------------

let private fingerprinted = System.Text.RegularExpressions.Regex(@"/assets/((?:[A-Za-z0-9_\-/]+?))\.[0-9a-z]{10}(\.[a-z0-9]+)")

let private logicalNames (text: string) : string list =
    [ for m in fingerprinted.Matches text -> m.Groups[1].Value + m.Groups[2].Value ] |> List.distinct |> List.sort

[<Fact>]
let ``H7 the web app manifest's icons, screenshots and shortcuts are fingerprinted URLs of the logical paths Rails names`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let! manifest = test.Send(Campfire.App.Tests.Support.get "/webmanifest.json")
        Assert.Equal(200, manifest.Status)
        let text = manifest.Text
        // Every asset URL in the manifest is fingerprinted (none is a plain `/assets/<logical>`).
        let all = System.Text.RegularExpressions.Regex.Matches(text, @"/assets/[^""]+") |> Seq.map (fun m -> m.Value) |> List.ofSeq
        Assert.NotEmpty all
        for url in all do
            Assert.True(fingerprinted.IsMatch url, $"not fingerprinted: {url}")
        // Each is the table's own route for that logical path, as Rails names the manifest's images.
        let names = logicalNames text
        for name in names do
            Assert.Contains(Campfire.App.Web.StaticAssets.path name, text.Replace("\\/", "/"))
        Assert.Equal<string list>([ "add.svg"; "person.svg"; "screenshots/android-chat.png"; "screenshots/android-dark-mode.png"; "screenshots/android-sidebar.png" ], names)
    }

[<Fact>]
let ``H8 a message with a sound renders the fingerprinted mp3 and image URLs`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let bot = Campfire.App.Tests.Support.anonymous test
        let! created = bot.Send((Campfire.App.Tests.Support.request "POST" $"/rooms/{Campfire.App.Tests.Support.ALL_TALK}/{Campfire.App.Tests.Support.BENDER_KEY}/messages").WithBody "/play yeah")
        Assert.Equal(201, created.Status)
        let b = Campfire.App.Tests.Support.browser test "198.51.100.9"
        do! b.SignIn "david@37signals.com"
        let! page = b.Get $"/rooms/{Campfire.App.Tests.Support.ALL_TALK}"
        Assert.Equal(200, page.Status)
        let mp3 = Campfire.App.Web.StaticAssets.path "yeah.mp3"
        let image = Campfire.App.Web.StaticAssets.path "sounds/yeah.webp"
        Assert.Contains(mp3, page.Text)
        Assert.Contains(image, page.Text)
        Assert.DoesNotContain("/assets/yeah.mp3", page.Text)
    }

// --- Unit 6.6 (design section 5, H5-H6): public files on the asset endpoints ---------------------------------------------

let private publicBytes (name: string) : byte[] = File.ReadAllBytes(Repo.path $"reference/public/{name}")

[<Fact>]
let ``H5 the public files are served with Rails' bytes, a 404 the app renders has 404.html's bytes, and /404 is the app's 404 (class B)`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        for name in [ "404.html"; "422.html"; "500.html"; "502.html"; "robots.txt" ] do
            let! response = test.Send(Campfire.App.Tests.Support.get $"/{name}")
            Assert.Equal(200, response.Status)
            Assert.True(response.Body = publicBytes name, $"/{name}: bytes differ from reference/public/{name}")
        // The app's own 404 (an unknown path, and the extensionless /404 that Rails' Static answered with the file and 200) is the page.
        for url in [ "/nope"; "/404"; "/422"; "/500"; "/502" ] do
            let! response = test.Send(Campfire.App.Tests.Support.get url)
            Assert.Equal(404, response.Status)
            Assert.True(response.Body = publicBytes "404.html", $"{url}: not 404.html's bytes")
            Assert.True(response.Header("x-request-id").IsSome, $"{url}: the app's own 404, not an endpoint's")
    }

[<Fact>]
let ``H6 a bot without an avatar gets default-bot-avatar.svg's bytes as image/svg+xml`` () =
    task {
        use! test = Campfire.App.Tests.Support.bootSeeded "default"
        let b = Campfire.App.Tests.Support.browser test "198.51.100.9"
        do! b.SignIn "david@37signals.com"
        let token = Campfire.App.Presenters.Common.avatarToken test.Booted.App.Secrets Campfire.App.Tests.Support.BENDER
        let! response = b.Get $"/users/{token}/avatar"
        Assert.Equal(200, response.Status)
        Assert.Equal(Some "image/svg+xml", response.ContentType)
        Assert.True(response.Body = stagedBytes "default-bot-avatar.svg", "bytes differ from the staged default-bot-avatar.svg")
        // The stock logo (`send_stock_icon`) is read the same way.
        let! logo = b.Get "/account/logo"
        Assert.Equal(200, logo.Status)
        Assert.Equal(Some "image/png", logo.ContentType)
        Assert.True(response.Body.Length > 0)
    }
