// Port of rust/crates/views/tests/messages_support/mod.rs
/// Shared by the views-B golden tests (`messages_*`, `rooms_*`, `searches_*`): loads a golden from
/// `rust/crates/views/tests/golden/b` (written by `reference-tools/views/b/run.sh`; read here, never written), builds
/// the `ViewContext` the reference request had, and compares our HTML with the reference's through the loose DOM
/// (`Campfire.Tests.LooseDom`, the harness's own rule). The reference's side is stored as the canonical token stream that
/// `reference-tools/views/b/canonical.rb` produces; it is rebuilt into HTML here (`rebuild`) and goes through the same
/// comparator as our output.
module Campfire.Views.Tests.GoldenB

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open AngleSharp
open AngleSharp.Dom
open AngleSharp.Html.Parser
open Campfire.Tests
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Differential.Inputs

let private goldenDir = Repo.path "rust/crates/views/tests/golden/b"

/// Where a difference's loose trees are written (never into `rust/`, which the Rust test wrote them in).
let private diffDir = Repo.path "target/views-b-diff"

// ---- the reference's expected side, rebuilt as HTML --------------------------------------------------------------------

let private escapeText (text: string) : string = text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")

/// The HTML of a canonical token stream: start and end tags as they are (attribute values are already escaped), text tokens
/// (`#text`: decoded, whitespace collapsed) escaped again, except under script and style, whose text is raw.
let rebuild (tokens: string seq) : string =
    let html = StringBuilder()
    let mutable raw = false
    for token in tokens do
        if token.StartsWith '#' then
            let text = token.Substring 1
            html.Append(if raw then text else escapeText text) |> ignore
        else
            html.Append token |> ignore
            if token = "<script>" || token.StartsWith "<script " || token = "<style>" || token.StartsWith "<style " then raw <- true
            elif token = "</script>" || token = "</style>" then raw <- false
    html.ToString()

// ---- regions of a page ----------------------------------------------------------------------------------------------------

let private parser = HtmlParser(HtmlParserOptions(IsScripting = true))

let private html (nodes: INode seq) : string = nodes |> Seq.map (fun n -> n.ToHtml()) |> String.concat ""

/// The parts of a page its template fills: the title, the head block (everything in head after the importmap's module script,
/// which the test layout puts there as a marker), nav, main content, footer, sidebar. A turbo-frame request's minimal layout has
/// no main: its body is the one region.
let private regionsOf (markup: string) (names: string list) : (string * string) list =
    let document = parser.ParseDocument markup
    match document.QuerySelector "main#main-content" with
    | null -> [ "body", (match document.Body with | null -> "" | body -> body.InnerHtml) ]
    | main ->
        let head = nonNull document.Head
        let first (selector: string) = document.QuerySelector selector
        names
        |> List.map (fun name ->
            let region =
                match name with
                | "title" -> match first "title" with | null -> "" | e -> e.InnerHtml
                | "head" ->
                    let kids = head.ChildNodes |> Seq.toList
                    let marker = kids |> List.tryFindIndexBack (fun n -> match n with :? IElement as e -> e.LocalName = "script" && e.GetAttribute "type" = "module" | _ -> false)
                    match marker with
                    | Some i -> html (List.skip (i + 1) kids)
                    | None -> ""
                | "nav" -> match first "nav#nav" with | null -> "" | e -> e.InnerHtml
                | "main" ->
                    let kids = main.ChildNodes |> Seq.toList
                    let footer = kids |> List.tryFindIndex (fun n -> match n with :? IElement as e -> e.Id = "footer" | _ -> false)
                    html (match footer with Some i -> List.take i kids | None -> kids)
                | "footer" -> match first "footer#footer" with | null -> "" | e -> e.InnerHtml
                | "sidebar" -> match first "aside#sidebar" with | null -> "" | e -> e.InnerHtml
                | other -> failwith other
            name, region)

let private regionNames = [ "title"; "head"; "nav"; "main"; "footer"; "sidebar" ]

let private assertSame (label: string) (expected: string) (actual: string) : unit =
    match LooseDom.diff expected actual with
    | None -> ()
    | Some report ->
        let file = label.Replace(' ', '_').Replace('[', '_').Replace(']', '_')
        Directory.CreateDirectory diffDir |> ignore
        File.WriteAllText(Path.Combine(diffDir, $"{file}.expected"), expected)
        File.WriteAllText(Path.Combine(diffDir, $"{file}.actual"), actual)
        failwith $"{label}: loose DOM differs from the reference\n{report}  full trees in target/views-b-diff/{file}.*"

// ---- a golden ----------------------------------------------------------------------------------------------------

type Golden =
    { Name: string
      Kind: string
      Json: JsonElement
      /// Overrides the context's `base_url` (the rest of the context is the golden's).
      BaseUrl: string option }

let golden (name: string) : Golden =
    let path = Path.Combine(goldenDir, $"{name}.json")
    let json = JsonDocument.Parse(File.ReadAllText path).RootElement
    { Name = name
      Kind = str (get json "kind")
      Json = json
      BaseUrl = None }

let private expectedTokens (g: Golden) : string list =
    [ for t in (get g.Json "expected").EnumerateArray() -> nonNull (t.GetString()) ]

type Golden with
    member g.Input: JsonElement = get g.Json "input"

    member g.InputAt(key: string) : JsonElement = get g.Input key

    /// The `ViewContext` the reference request had.
    member g.Context: ViewContext =
        let context = get g.Json "context"
        let assets =
            [ for p in (get context "assets").EnumerateObject() -> p.Name, nonNull (p.Value.GetString()) ] |> dict
        let assetPath (logical: string) =
            match assets.TryGetValue logical with
            | true, path -> path
            | _ -> failwith $"{g.Name}: unknown asset {logical}"
        let user = get context "current_user"
        let p = get context "platform"
        let flag key = bool (get p key)
        { CurrentUser =
            (if user.ValueKind = JsonValueKind.Object then
                 Some
                     { Id = int64Of (get user "id")
                       Name = str (get user "name")
                       Administrator = bool (get user "administrator")
                       Bot = bool (get user "bot")
                       AvatarUrl = str (get user "avatar_url") }
             else
                 None)
          Account =
            { Name = str (get (get context "account") "name")
              LogoUrl = str (get (get context "account") "logo_url")
              HasLogo = bool (get (get context "account") "has_logo") }
          FlashNotice = None
          FlashAlert = None
          Platform =
            { Ios = flag "ios"
              Android = flag "android"
              Mac = flag "mac"
              Windows = flag "windows"
              Chrome = flag "chrome"
              Firefox = flag "firefox"
              Safari = flag "safari"
              Edge = flag "edge"
              Mobile = flag "mobile"
              Desktop = flag "desktop"
              AppleMessages = flag "apple_messages"
              Browser = str (get p "browser")
              OperatingSystem = str (get p "operating_system") }
          VapidPublicKey = None
          AssetPath = assetPath
          // Marks where the layout ends and the page's head block starts (see `headBlock`).
          ImportmapTags = Html.Trusted.ofAssetTags "<script type=\"module\">import \"application\"</script>"
          StylesheetTags = Html.Trusted.ofAssetTags ""
          CustomStyles = None
          CableUrl = "/cable"
          BaseUrl = defaultArg g.BaseUrl (str (get context "base_url"))
          RequestUrl = ""
          Referrer = None
          LastRoomVisitedId =
            (match get context "last_room_visited_id" with
             | v when v.ValueKind = JsonValueKind.Number -> Some(v.GetInt64())
             | _ -> None)
          AppVersion = "0" }

    /// Asserts DOM parity for a template rendered without a layout against a reference page: the page's main content
    /// (or a frame layout's body) is compared with the whole render.
    member g.AssertContent(actualHtml: string) : unit =
        let expected = rebuild (expectedTokens g)
        let expected =
            if g.Kind = "page" then
                match regionsOf expected [ "main" ] with
                | (_, region) :: _ -> region
                | [] -> ""
            else
                expected
        assertSame g.Name (LooseDom.normalize expected) (LooseDom.normalize actualHtml)

    /// Asserts DOM parity. Pages compare the regions the page fills (title, the head block, nav, main content, footer,
    /// sidebar); fragments compare everything.
    member g.AssertDom(actualHtml: string) : unit =
        let expected = rebuild (expectedTokens g)
        if g.Kind = "page" then
            let regions = regionsOf expected regionNames
            if regions.IsEmpty then failwith $"{g.Name}: no regions in reference"
            let actualRegions = regionsOf actualHtml (regions |> List.map fst)
            for (name, expectedRegion), (_, actualRegion) in List.zip regions actualRegions do
                assertSame $"{g.Name} [{name}]" (LooseDom.normalize expectedRegion) (LooseDom.normalize actualRegion)
        else
            assertSame g.Name (LooseDom.normalize expected) (LooseDom.normalize actualHtml)
