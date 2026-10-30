// Port of rust/crates/views/tests/support/facts.rs
/// Loads `rust/crates/views/tests/golden/a/facts.json` (written by reference-tools/views/a/render.rb
/// from the reference app; read here, never written) and builds the `ViewContext` a case was rendered
/// with.
module Campfire.Views.Tests.Facts

open System
open System.IO
open System.Text.Json
open Campfire.Tests
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Differential.Inputs

let goldenDir = Repo.path "rust/crates/views/tests/golden/a"

let facts: Lazy<JsonElement> =
    lazy (JsonDocument.Parse(File.ReadAllText(Path.Combine(goldenDir, "facts.json"))).RootElement)

let golden (name: string) (ext: string) : string = File.ReadAllText(Path.Combine(goldenDir, $"{name}.{ext}"))

let private str (e: JsonElement) : string option = opt e

let case (name: string) : JsonElement = facts.Value.GetProperty("cases").GetProperty name

let platform (label: string) : Platform =
    let p = facts.Value.GetProperty("platforms").GetProperty label
    let flag (name: string) = p.GetProperty(name).GetBoolean()
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
      Browser = defaultArg (str (p.GetProperty "browser")) ""
      OperatingSystem = defaultArg (str (p.GetProperty "operating_system")) "" }

/// Per-case request facts that aren't in facts.json.
type Request =
    { /// Rendered by `ApplicationController.renderer`, outside a request.
      Partial: bool
      FlashNotice: string option
      FlashAlert: string option }

module Request =
    let none = { Partial = false; FlashNotice = None; FlashAlert = None }

/// The facts for the user with this email as of case `caseName`.
let userByEmail (caseName: string) (email: string) : JsonElement =
    (case caseName).GetProperty("users").EnumerateObject()
    |> Seq.map (fun p -> p.Value)
    |> Seq.find (fun user -> str (user.GetProperty "email_address") = Some email)

/// The facts for the user named `name` as of case `caseName`.
let user (caseName: string) (name: string) : JsonElement = (case caseName).GetProperty("users").GetProperty name

/// The `ViewContext` the reference app had for case `name`.
let context (name: string) (request: Request) : ViewContext =
    let facts = facts.Value
    let case = case name
    let assets =
        facts.GetProperty("assets").EnumerateObject() |> Seq.map (fun p -> p.Name, nonNull (p.Value.GetString())) |> dict
    let assetPath (logical: string) =
        match assets.TryGetValue logical with
        | true, path -> path
        | _ -> failwith $"unknown asset {logical}"
    let current = str (case.GetProperty "as") |> Option.map (userByEmail name)
    let currentUser =
        current
        |> Option.map (fun user ->
            let role = nonNull (user.GetProperty("role").GetString())
            { Id = user.GetProperty("id").GetInt64()
              Name = nonNull (user.GetProperty("name").GetString())
              Administrator = role = "administrator"
              Bot = role = "bot"
              AvatarUrl = nonNull (user.GetProperty("avatar_path").GetString()) })
    let account = case.GetProperty "account"
    let baseUrl = nonNull (facts.GetProperty("base_url").GetString())
    { CurrentUser = currentUser
      Account =
        { Name = defaultArg (str (get account "name")) ""
          LogoUrl = defaultArg (str (get account "logo_path")) "/account/logo"
          HasLogo = (get account "has_logo").ValueKind = JsonValueKind.True }
      FlashNotice = request.FlashNotice
      FlashAlert = request.FlashAlert
      Platform = platform (nonNull (case.GetProperty("ua").GetString()))
      VapidPublicKey = str (facts.GetProperty "vapid_public_key")
      AssetPath = assetPath
      ImportmapTags = Html.Trusted.ofAssetTags (nonNull (facts.GetProperty("importmap_tags").GetString()))
      StylesheetTags = Html.Trusted.ofAssetTags (nonNull (facts.GetProperty("stylesheet_tags").GetString()))
      CustomStyles = str (get account "custom_styles")
      CableUrl = "/cable"
      BaseUrl = baseUrl
      RequestUrl = baseUrl + nonNull (case.GetProperty("path").GetString())
      Referrer = str (get case "referrer")
      LastRoomVisitedId =
        current
        |> Option.bind (fun user ->
            let id = get user "original_room_id"
            if id.ValueKind = JsonValueKind.Number then Some(id.GetInt64()) else None)
      AppVersion = nonNull (facts.GetProperty("app_version").GetString()) }

/// A page's facts: `case["data"]`.
let data (name: string) : JsonElement = (case name).GetProperty "data"

let mentionUser (caseName: string) (userName: string) : Users.MentionUser =
    let u = user caseName userName
    let text (key: string) = str (u.GetProperty key)
    { User =
        { Id = u.GetProperty("id").GetInt64()
          Name = nonNull (u.GetProperty("name").GetString())
          Bio = text "bio"
          EmailAddress = text "email_address"
          Role =
            (match text "role" with
             | Some "administrator" -> Users.Administrator
             | Some "bot" -> Users.Bot
             | _ -> Users.Member)
          Status =
            (match text "status" with
             | Some "deactivated" -> Users.Deactivated
             | Some "banned" -> Users.Banned
             | _ -> Users.Active)
          AvatarPath = nonNull (u.GetProperty("avatar_path").GetString()) }
      AttachableSgid = nonNull (u.GetProperty("attachable_sgid").GetString()) }

/// Two JSON values are equal: the same kinds and values, objects with the same properties in the same order (the manifest's key
/// order is the port's), numbers by their text.
let rec jsonEqual (a: JsonElement) (b: JsonElement) : bool =
    a.ValueKind = b.ValueKind
    && (match a.ValueKind with
        | JsonValueKind.Object ->
            let left, right = [ for p in a.EnumerateObject() -> p ], [ for p in b.EnumerateObject() -> p ]
            left.Length = right.Length && List.forall2 (fun (x: JsonProperty) (y: JsonProperty) -> x.Name = y.Name && jsonEqual x.Value y.Value) left right
        | JsonValueKind.Array ->
            let left, right = [ for e in a.EnumerateArray() -> e ], [ for e in b.EnumerateArray() -> e ]
            left.Length = right.Length && List.forall2 jsonEqual left right
        | JsonValueKind.String -> a.GetString() = b.GetString()
        | _ -> a.GetRawText() = b.GetRawText())
