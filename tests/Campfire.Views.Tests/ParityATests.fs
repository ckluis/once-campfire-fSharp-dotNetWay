// The cases of rust/crates/views/tests/parity_a.rs for the templates ported so far: DOM parity with golden
// renders from the reference app (`reference-tools/views/a/render.rb`, in rust/crates/views/tests/golden/a,
// read here and never written). Each case rebuilds, from facts.json, the view-model a controller would pass,
// renders it with the F# template, and compares normalized token streams.
module Campfire.Views.Tests.ParityATests

open System.Text.Json
open Xunit
open Campfire.Views
open Campfire.Tests
open Campfire.Views.Tests.Facts

module Inputs = Campfire.Views.Differential.Inputs

let private text (e: JsonElement) : string = nonNull (e.GetString())

let private named (caseName: string) (userName: string) : Users.UserSummary =
    Inputs.userSummary (user caseName userName)

let private mentionUserOf (caseName: string) (userName: string) : Users.MentionUser =
    { User = named caseName userName
      AttachableSgid = nonNull ((user caseName userName).GetProperty("attachable_sgid").GetString()) }

/// `User.administrator.first`, shown by `accounts/_help_contact`.
let private helpContact (caseName: string) : Accounts.HelpContact option =
    let david = user caseName "David"
    Some { Name = text (david.GetProperty "name"); EmailAddress = text (david.GetProperty "email_address") }

/// All users in case `name`, `User.ordered` (by lowercased name).
let private orderedUsers (name: string) : Users.UserSummary list =
    (case name).GetProperty("users").EnumerateObject()
    |> Seq.map (fun p -> Inputs.userSummary p.Value)
    |> Seq.sortBy (fun user -> Helpers.Application.toLowercase user.Name)
    |> List.ofSeq

let private accountFact (name: string) (key: string) : JsonElement = (case name).GetProperty("account").GetProperty key

let private assertParity (name: string) (ext: string) (rendered: string) =
    let expected = LooseDom.normalize (golden name ext)
    let actual = LooseDom.normalize rendered
    match LooseDom.diff expected actual with
    | Some report -> failwith $"{name}: loose DOM differs from the reference\n{report}"
    | None -> ()

[<Fact>]
let ``pwa partials`` () =
    for ua in facts.Value.GetProperty("user_agents").EnumerateObject() |> Seq.map (fun p -> p.Name) do
        for kind in [ "browser_settings"; "system_settings"; "install_instructions" ] do
            let name = $"{kind}_{ua}"
            let ctx = context name Request.none
            let nodes =
                match kind with
                | "browser_settings" -> Templates.Pwa._BrowserSettings.render ctx
                | "system_settings" -> [ Templates.Pwa._SystemSettings.render ctx ]
                | _ -> Templates.Pwa._InstallInstructions.render ctx
            assertParity name "html" ((HtmlOutput.ofNodes nodes []).ToString())

[<Fact>]
let ``users partials`` () =
    let name = "autocompletables_template"
    assertParity name "html" ((HtmlOutput.ofNode (Templates.Users.Autocompletables._Template.render (context name Request.none)) []).ToString())

    let name = "mention"
    assertParity name "html" ((HtmlOutput.ofNode (Templates.Users._Mention.render (context name Request.none) (mentionUser name "JZ")) []).ToString())

    let name = "ban_button_banned"
    let ctx = context name { Request.none with Partial = true }
    assertParity name "html" ((HtmlOutput.ofNode (Templates.Users._BanButton.render ctx (named name "Spam Ham")) []).ToString())

    for (name, roomName, unread) in [ "shared_room_unread", "HQ", true; "shared_room", "All Talk", false ] do
        let room = facts.Value.GetProperty("rooms").GetProperty roomName
        let view: Users.SidebarRoom =
            { Id = room.GetProperty("id").GetInt64()
              ParamKey = nonNull (room.GetProperty("param_key").GetString())
              Name = roomName
              Unread = unread }
        assertParity name "html" ((HtmlOutput.ofNode (Templates.Users.Sidebars.Rooms._Shared.render view) []).ToString())

    let name = "user_json"
    let jz = user name "JZ"
    let json =
        Campfire.RailsCompat.Json.encode (
            MessagesJson.userToValue
                { Id = jz.GetProperty("id").GetInt64()
                  Name = "JZ"
                  Role = text (jz.GetProperty "role")
                  AvatarUrl = text (facts.Value.GetProperty "base_url") + text (jz.GetProperty "avatar_path") }
        )
    Assert.Equal(golden name "json", json)

[<Fact>]
let ``welcome show`` () =
    // The first page to use the layout: its parity covers the layout and the lightbox.
    let name = "welcome"
    let ctx = context name Request.none
    let html = (Legacy.inLayout ctx false (Templates.Welcome.Show.rendered ctx ctx.CurrentUser.Value.Name)).ToString()
    assertParity name "html" html

[<Fact>]
let ``application layout wrapper matches extended pages`` () =
    // A page rendered through the wrapper with its parts equals the page calling the layout directly.
    let name = "welcome"
    let ctx = context name Request.none
    let page = Templates.Welcome.Show.rendered ctx ctx.CurrentUser.Value.Name
    let direct = (Legacy.inLayout ctx false page).ToString()
    let region (nodes: Falco.Markup.XmlNode list) = Render.html (fun w -> Legacy.write w (HtmlOutput.ofNodes nodes []))
    let wrapped =
        Render.text (fun w ->
            Layouts.Application.render
                w
                ctx
                { Layouts.Application.create (region page.Regions.Content) with
                    PageTitle = page.Regions.Title
                    BodyClass = page.Regions.BodyClass
                    Sidebar = region page.Regions.Sidebar })
    Assert.Equal(direct, wrapped)
    assertParity name "html" wrapped

[<Fact>]
let ``a flash notice and a flash alert render in the layout`` () =
    let name = "welcome"
    let welcome (request: Request) =
        let ctx = context name request
        (Legacy.inLayout ctx false (Templates.Welcome.Show.rendered ctx "X")).ToString()
    let notice = welcome { Request.none with FlashNotice = Some "Saved <ok>" }
    Assert.Contains("<span class=\"for-screen-reader\" role=\"alert\" aria-atomic=\"true\">Saved &lt;ok&gt;</span>", notice)
    Assert.DoesNotContain("--flash-background", notice)
    let alert = welcome { Request.none with FlashAlert = Some "No" }
    Assert.Contains("style=\"--flash-background: var(--color-negative)\"", alert)
    Assert.Contains("alert-", alert)

// The cases of parity_a.rs for the hot-path templates (unit 4.2): the sidebar and the mentions prompt.

let private sidebar (name: string) : Users.SidebarShow =
    let sidebar = (data name).GetProperty "sidebar"
    let me = userByEmail name (nonNull ((case name).GetProperty("as").GetString()))
    let meName = nonNull (me.GetProperty("name").GetString())
    let strings (e: JsonElement) = [ for n in e.EnumerateArray() -> text n ]
    { CurrentUser = Campfire.Views.Differential.Inputs.userSummary me
      RoomsStream = text (facts.Value.GetProperty("signed_streams").GetProperty "rooms")
      UserRoomsStream = text (facts.Value.GetProperty("signed_streams").GetProperty("user_rooms").GetProperty meName)
      DirectMemberships =
        [ for d in sidebar.GetProperty("direct").EnumerateArray() ->
              let roomId = d.GetProperty("room_id").GetInt64()
              let direct: Users.SidebarDirect =
                  { RoomId = roomId
                    Unread = d.GetProperty("unread").GetBoolean()
                    UpdatedAtEpoch = text (d.GetProperty "updated_at_epoch")
                    Members = strings (d.GetProperty "member_names") |> List.map (named name)
                    MembershipId = roomId
                    MembershipUpdatedAt = Campfire.RailsCompat.Timestamps.unixEpoch }
              ({ Html = HtmlOutput.toFragment (HtmlOutput.ofNode (Templates.Users.Sidebars.Rooms._Direct.render (context name Request.none) direct) []) }: Users.SidebarDirectItem) ]
      DirectPlaceholderUsers = strings (sidebar.GetProperty "placeholders") |> List.map (named name)
      OtherMemberships =
        [ for r in sidebar.GetProperty("shared").EnumerateArray() ->
              { Id = r.GetProperty("room_id").GetInt64()
                ParamKey = text (r.GetProperty "param_key")
                Name = text (r.GetProperty "name")
                Unread = r.GetProperty("unread").GetBoolean() } ]
      CanCreateRooms = sidebar.GetProperty("can_create_rooms").GetBoolean() }

[<Fact>]
let ``users sidebars show`` () =
    for name in [ "sidebar_david"; "sidebar_kevin" ] do
        let ctx = context name Request.none
        let page = sidebar name
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Users.Sidebars.Show.rendered ctx page)).ToString())
    let name = "sidebar_frame"
    let ctx = context name Request.none
    let page = sidebar name
    assertParity name "html" ((Legacy.inLayout ctx true (Templates.Users.Sidebars.Show.rendered ctx page)).ToString())

[<Fact>]
let ``autocompletable users`` () =
    let name = "autocompletable_users"
    let ctx = context name Request.none
    let users =
        [ for n in (data name).GetProperty("autocompletable").EnumerateArray() -> mentionUserOf name (nonNull (n.GetString())) ]
    assertParity name "html" ((HtmlOutput.ofNodes (Templates.Autocompletable.Users.Index.render ctx users) []).ToString())

    let name = "autocompletable_users_json"
    let users =
        [ for n in (data name).GetProperty("autocompletable").EnumerateArray() -> mentionUserOf name (nonNull (n.GetString())) ]
    let json = Autocompletable.usersIndexJson users (nonNull (facts.Value.GetProperty("base_url").GetString()))
    Assert.Equal(golden name "json", json)

// The cases of parity_a.rs for the remaining templates (unit 4.3): sessions, accounts, bots, custom styles, first run, join,
// a user's page, the profile, push subscriptions, the avatar SVG, the PWA manifest and the turbo stream of users.

[<Fact>]
let ``sessions new`` () =
    for (name, email, alert) in
        [ "sessions_new", None, None
          "sessions_new_email", Some "x@y.com", None
          "sessions_new_rejected", Some "david@37signals.com", Some "Too many requests or unauthorized."
          "sessions_new_with_logo", None, None ] do
        let ctx = context name { Request.none with FlashAlert = alert }
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Sessions.New.rendered ctx email (helpContact name))).ToString())

[<Fact>]
let ``sessions incompatible browser`` () =
    for name in [ "incompatible_browser"; "incompatible_browser_apple_messages" ] do
        let ctx = context name Request.none
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Sessions.IncompatibleBrowser.rendered ctx)).ToString())

[<Fact>]
let ``sessions transfer`` () =
    let name = "sessions_transfer"
    let ctx = context name Request.none
    let action = text ((case name).GetProperty "path")
    assertParity name "html" ((Legacy.inLayout ctx false (Templates.Sessions.Transfers.Show.rendered action)).ToString())

[<Fact>]
let ``accounts edit`` () =
    for (name, notice) in
        [ "account_edit_admin", None
          "account_edit_member", None
          "account_edit_notice", Some "✓"
          "account_edit_paginated", None
          "account_edit_with_logo", None
          "account_edit_with_logo_member", None ] do
        let ctx = context name { Request.none with FlashNotice = notice }
        // AccountsController#account_users
        let visible =
            orderedUsers name
            |> List.filter (fun user -> not user.Bot)
            |> List.filter (fun user -> if ctx.CanAdminister then user.Active || user.Banned else user.Active)
        let administrators, members = visible |> List.partition (fun user -> user.Administrator)
        let edit: Accounts.EditView =
            { AccountId = (accountFact name "id").GetInt64()
              JoinCode = text (accountFact name "join_code")
              RestrictRoomCreationToAdministrators = (accountFact name "restrict_room_creation_to_administrators").GetBoolean()
              Administrators = administrators
              Members = members
              NextPage = if name = "account_edit_paginated" then Some "2" else None }
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Accounts.Edit.rendered ctx edit)).ToString())

/// `bot.rooms.without_directs.ordered` for each active bot of case `name`.
let private bots (name: string) : Accounts.Bot list =
    let facts = facts.Value
    [ for user in orderedUsers name |> List.filter (fun user -> user.Bot && user.Active) do
          let botKey = text ((Facts.user name user.Name).GetProperty "bot_key")
          let rooms =
              [ for m in facts.GetProperty("memberships").EnumerateArray() do
                    if m.GetProperty("user_id").GetInt64() = user.Id then
                        for room in facts.GetProperty("rooms").EnumerateObject() do
                            if room.Value.GetProperty("id").GetInt64() = m.GetProperty("room_id").GetInt64()
                               && text (room.Value.GetProperty "type") <> "Rooms::Direct" then
                                { Id = room.Value.GetProperty("id").GetInt64(); Name = text (room.Value.GetProperty "name") }: Accounts.BotRoom ]
              |> List.sortBy (fun room -> Helpers.Application.toLowercase room.Name)
          { User = user; BotKey = botKey; Rooms = rooms }: Accounts.Bot ]

[<Fact>]
let ``accounts bots`` () =
    let name = "bots_index"
    let ctx = context name Request.none
    let list = bots name
    assertParity name "html" ((Legacy.inLayout ctx false (Templates.Accounts.Bots.Index.rendered ctx list)).ToString())

    let name = "bots_new"
    let ctx = context name Request.none
    assertParity name "html" ((Legacy.inLayout ctx false (Templates.Accounts.Bots.New.rendered ctx Accounts.BotForm.empty)).ToString())

    for (name, withAvatar) in [ "bots_edit", false; "bots_edit_with_avatar", true ] do
        let ctx = context name Request.none
        let bender = user name "Bender Bot"
        let form: Accounts.BotForm =
            { Name = Inputs.opt (bender.GetProperty "name")
              WebhookUrl = Inputs.opt (bender.GetProperty "webhook_url")
              AvatarAttachmentUrl = if withAvatar then Inputs.opt ((case name).GetProperty "avatar_url") else None }
        let botId = bender.GetProperty("id").GetInt64()
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Accounts.Bots.Edit.rendered ctx botId form)).ToString())

[<Fact>]
let ``accounts custom styles`` () =
    for name in [ "custom_styles_edit"; "custom_styles_layout" ] do
        let ctx = context name Request.none
        let styles = Inputs.opt (accountFact name "custom_styles")
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Accounts.CustomStyles.Edit.rendered ctx styles)).ToString())

[<Fact>]
let ``first runs show`` () =
    let name = "first_run"
    let ctx = context name Request.none
    assertParity name "html" ((Legacy.inLayout ctx false (Templates.FirstRuns.Show.rendered ctx)).ToString())

[<Fact>]
let ``users new`` () =
    let name = "users_new"
    let ctx = context name Request.none
    let joinCode = text (accountFact name "join_code")
    assertParity name "html" ((Legacy.inLayout ctx false (Templates.Users.New.rendered ctx joinCode (helpContact name))).ToString())

[<Fact>]
let ``users show`` () =
    for (name, shown) in
        [ "users_show_self", "David"
          "users_show_member_as_admin", "JZ"
          "users_show_member_as_member", "JZ"
          "users_show_bot", "Bender Bot"
          "users_show_deactivated", "Ex Employee"
          "users_show_banned", "Spam Ham" ] do
        let ctx = context name Request.none
        let transferId = text ((user name shown).GetProperty "transfer_id")
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Users.Show.rendered ctx (named name shown) transferId)).ToString())

let private profileMemberships (list: JsonElement) : Users.ProfileMembership list =
    [ for m in list.EnumerateArray() ->
          { RoomId = m.GetProperty("room_id").GetInt64()
            RoomParamKey = text (m.GetProperty "param_key")
            RoomDisplayName = text (m.GetProperty "display_name")
            Involvement = text (m.GetProperty "involvement")
            Direct = m.GetProperty("direct").GetBoolean() } ]

[<Fact>]
let ``users profiles show`` () =
    for ua in
        [ "chrome_mac"; "chrome_windows"; "safari_mac"; "safari_ios"; "chrome_android"; "firefox_mac"; "firefox_android"; "edge_windows"
          "kevin"; "with_avatar" ] do
        let name = $"profile_{ua}"
        let ctx = context name Request.none
        let me = userByEmail name (text ((case name).GetProperty "as"))
        let profile: Users.ProfileShow =
            { User = Inputs.userSummary me
              AvatarAttached = me.GetProperty("avatar_attached").GetBoolean()
              TransferId = text (me.GetProperty "transfer_id")
              SharedMemberships = profileMemberships ((data name).GetProperty("profile").GetProperty "shared")
              DirectMemberships = profileMemberships ((data name).GetProperty("profile").GetProperty "direct") }
        assertParity name "html" ((Legacy.inLayout ctx false (Templates.Users.Profiles.Show.rendered ctx profile)).ToString())

[<Fact>]
let ``users push subscriptions`` () =
    let name = "push_subscriptions"
    let ctx = context name Request.none
    let subscriptions: Users.PushSubscription list =
        [ for ps in (data name).GetProperty("push_subscriptions").EnumerateArray() ->
              { Id = ps.GetProperty("id").GetInt64()
                Endpoint = text (ps.GetProperty "endpoint")
                Browser = text (ps.GetProperty "browser")
                Version = text (ps.GetProperty "version")
                Platform = text (ps.GetProperty "platform") } ]
    assertParity name "html" ((Legacy.inLayout ctx false (Templates.Users.PushSubscriptions.Index.rendered ctx subscriptions)).ToString())

[<Fact>]
let ``users avatars show`` () =
    for (name, shown) in [ "avatar_david", "David"; "avatar_three_initials", "Anna Bea Cole" ] do
        let user = user name shown
        let svg =
            (HtmlOutput.ofNode (Templates.Users.Avatars.Show.render (user.GetProperty("id").GetInt64()) (text (user.GetProperty "initials"))) [])
                .ToString()
        // Falco writes the same elements without the ERB's indentation: loose-equal, not byte-equal (class A).
        assertParity name "svg" svg

[<Fact>]
let ``pwa manifest and service worker`` () =
    let name = "manifest"
    let assets = facts.Value.GetProperty "assets"
    let assetPath (logical: string) = text (assets.GetProperty logical)
    let json =
        System.Text.Encoding.UTF8.GetString(
            Templates.Pwa.Manifest.render
                (Inputs.opt (accountFact name "name"))
                (text (accountFact name "logo_path_small"))
                (text (accountFact name "logo_path"))
                (text (facts.Value.GetProperty "base_url"))
                assetPath
        )
    // Rails HTML-escapes the values into the JSON (README, Known differences); the manifest is compared as JSON (indentation is class A).
    let expected = (golden name "json").Replace("&amp;", "&")
    use expectedDocument = JsonDocument.Parse expected
    use actualDocument = JsonDocument.Parse json
    Assert.True(
        Campfire.Views.Tests.Facts.jsonEqual expectedDocument.RootElement actualDocument.RootElement,
        $"the manifest differs from Rails' as JSON:\n{json}")
    Assert.Equal(golden "service_worker" "js", Templates.Pwa.ServiceWorker.js)
    Assert.Equal(golden "service_worker" "js", System.Text.Encoding.UTF8.GetString Templates.Pwa.ServiceWorker.bytes)

[<Fact>]
let ``pwa manifest is valid json whatever the account is called`` () =
    let assetPath (logical: string) = $"/assets/{logical}"
    let name = "Back\\slash \"quoted\" <b>&amp;</b>"
    let json =
        System.Text.Encoding.UTF8.GetString(
            Templates.Pwa.Manifest.render (Some name) "/account/logo?size=small&v=1" "/account/logo?v=1" "http://campfire.test" assetPath
        )
    use manifest = JsonDocument.Parse json
    Assert.Equal(name, text (manifest.RootElement.GetProperty "name"))
    let icons = manifest.RootElement.GetProperty "icons"
    Assert.Equal("/account/logo?size=small&v=1", text (icons[0].GetProperty "src"))

[<Fact>]
let ``accounts users index turbo stream`` () =
    let name = "account_users_page_2"
    let ctx = context name Request.none
    let users = [ for n in (case name).GetProperty("page_users").EnumerateArray() -> named name (text n) ]
    assertParity name "turbo_stream.html" ((Templates.Accounts.Users.IndexTurboStream.output ctx users None).ToString())
