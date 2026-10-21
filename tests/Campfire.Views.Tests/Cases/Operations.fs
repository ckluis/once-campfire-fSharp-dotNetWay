/// The F# counterpart of `src/ops.rs`: renders each case with `Campfire.Views`. A branch of `run` here
/// is the branch of the same name there; the arguments are described there.
module Campfire.Views.Differential.Operations

open System
open System.Text
open System.Text.Json
open Campfire.RailsCompat
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Helpers
open Falco.Markup
open Campfire.Views.Users
open Campfire.Views.Differential.Inputs
open Campfire.Views.Differential.Answer

/// A recorded page's fragment: `<li id="n">xxxx</li>`.
let private fragmentOf (n: int) (size: int) : CachedFragment = CachedFragment($"<li id=\"{n}\">{String('x', size)}</li>")

let private strings (list: JsonElement) : string list = arr list |> List.map str

let private paramOf (v: JsonElement) : Url.Param =
    match v.ValueKind with
    | JsonValueKind.Array -> Url.Many(arr v |> List.map str)
    | _ -> Url.One(str v)

let private html (node: XmlNode) : Answer = text ((HtmlOutput.ofNode node []).ToString())

let private helper (name: string) (args: JsonElement) (ctx: JsonElement) (shared: JsonElement) : Answer =
    let view () = viewContext ctx shared
    let content = str (get args "content")
    match name with
    | "asset_path" -> text (Assets.assetPath (view ()) (str (get args "source")))
    | "link" ->
        match str (get args "kind") with
        | "mail_to" -> html (Markup.Links.mailTo (str (get args "text")))
        | other -> failwith $"link kind {other}"
    | "application" ->
        match str (get args "name") with
        | "page_title_tag" -> text ((HtmlOutput.ofNode (Markup.Application.pageTitleTag (opt (get args "page_title"))) []).ToString())
        | "current_user_meta_tags" -> text ((HtmlOutput.ofNodes (Markup.Application.currentUserMetaTags (view ())) []).ToString())
        | "script_aware_action_cable_meta_tag" -> text ((HtmlOutput.ofNode (Markup.Application.scriptAwareActionCableMetaTag (view ())) []).ToString())
        | "custom_styles_tag" -> text ((HtmlOutput.ofNodes (Markup.Application.customStylesTag (view ())) []).ToString())
        | "body_classes" -> text (Markup.Application.bodyClasses (view ()) (opt (get args "body_class")))
        | "link_back" -> html (Markup.Application.linkBack (view ()))
        | "link_back_to" -> html (Markup.Application.linkBackTo (view ()) (str (get args "destination")))
        | "link_back_to_last_room_visited" -> html (Markup.Application.linkBackToLastRoomVisited (view ()))
        | "version_badge" -> html (Markup.Application.versionBadge (view ()))
        | "button_to_copy_to_clipboard" ->
            html (Markup.Application.buttonToCopyToClipboard (str (get args "url")) [ Text.raw content ])
        | "link_to_zoom_qr_code" -> html (Markup.Application.linkToZoomQrCode (str (get args "url")) [ Text.raw content ])
        | "web_share_session_button" ->
            html (Markup.Application.webShareSessionButton (str (get args "url")) (str (get args "title")) (str (get args "text")) [ Text.raw content ])
        | "truncate" -> text (Application.truncate (str (get args "text")) (intOf (get args "length")) (str (get args "omission")))
        | "capitalize" -> text (Application.capitalize (str (get args "text")))
        | "to_lowercase" -> text (Application.toLowercase (str (get args "text")))
        | "capitalize_each" ->
            text (String.Join("", (str (get args "text")).EnumerateRunes() |> Seq.map (fun rune -> Application.capitalize (rune.ToString()))))
        | "to_sentence" -> text (Application.toSentence (strings (get args "items")) (str (get args "connector")))
        | other -> failwith $"application helper {other}"
    | "users" ->
        let avatar: UsersHelper.AvatarUser =
            { Id = int64Of (get args "id")
              Title = str (get args "title")
              AvatarPath = str (get args "avatar_path") }
        match str (get args "name") with
        | "avatar_tag" -> html (Markup.Users.avatarTag (view ()) avatar (attrs (get args "attrs")))
        | "avatar_background_color" -> text (UsersHelper.avatarBackgroundColor (int64Of (get args "id")))
        | "initials" -> text (UsersHelper.initials (str (get args "text")))
        | "initials_each" ->
            text (String.Join("\n", (str (get args "text")).EnumerateRunes() |> Seq.map (fun rune -> UsersHelper.initials (rune.ToString() + "x"))))
        | "user_title" -> text (UsersHelper.userTitle (str (get args "text")) (opt (get args "bio")))
        | "button_to_direct_room_with" -> html (Markup.Users.buttonToDirectRoomWith (view ()) (int64Of (get args "id")))
        | "curl_text_line" -> text (UsersHelper.curlTextLine (str (get args "text")))
        | "curl_upload_line" -> text (UsersHelper.curlUploadLine (str (get args "text")))
        | "account_logo_tag" -> html (Markup.Users.accountLogoTag (view ()) (opt (get args "style")))
        | "profile_form_submit_button" -> html (Markup.Users.profileFormSubmitButton (view ()))
        | "sidebar_turbo_frame_tag" ->
            html (Markup.Users.sidebarFrameWith (opt (get args "src")) [ Text.raw (str (get args "content")) ])
        | "user_filter_menu_tag" -> html (Markup.Users.userFilterMenu [ Text.raw (str (get args "content")) ])
        | "user_filter_search_tag" -> html Markup.Users.userFilterSearch
        | other -> failwith $"users helper {other}"
    | "rooms" ->
        match str (get args "name") with
        | "humanize_involvement" -> text (RoomsHelper.humanizeInvolvement (str (get args "involvement")))
        | "next_involvement" -> text (RoomsHelper.nextInvolvement (bool (get args "direct")) (str (get args "involvement")))
        | "button_to_change_involvement" ->
            let room: RoomsHelper.InvolvementRoom =
                { Id = int64Of (get args "room_id")
                  ParamKey = str (get args "param_key")
                  Direct = bool (get args "direct") }
            text ((HtmlOutput.ofNode (Markup.Rooms.buttonToChangeInvolvement (view ()) room (str (get args "involvement"))) []).ToString())
        | other -> failwith $"rooms helper {other}"
    | "translations" ->
        match str (get args "kind") with
        | "translations_for" -> html (Markup.Translations.translationsFor (str (get args "key")))
        | "translation_button" -> html (Markup.Translations.translationButton (view ()) (str (get args "key")))
        | other -> failwith $"translations helper {other}"
    | "url" ->
        match str (get args "kind") with
        | "with_query" ->
            let parameters = arr (get args "params") |> List.map (fun pair -> str (at pair 0), paramOf (at pair 1))
            text (Url.withQuery (str (get args "path")) parameters)
        | "directs" -> text (Url.roomsDirectsWithUsers (arr (get args "user_ids") |> List.map int64Of))
        | "cgi_escape" -> text (Url.cgiEscape (str (get args "text")))
        | other -> failwith $"url helper {other}"
    | "turbo" ->
        match str (get args "kind") with
        | "turbo_stream_from" -> html (Markup.Turbo.streamFrom (str (get args "name")))
        | "dom_id" -> text (Turbo.domId (str (get args "model")) (int64Of (get args "id")) (opt (get args "prefix")))
        | other -> failwith $"turbo helper {other}"
    | other -> failwith $"unknown helper {other}"

let private messageOp (name: string) (args: JsonElement) (ctx: JsonElement) (shared: JsonElement) : Answer =
    match name with
    | "epoch_ms" -> text (string (MessagesSupport.epochMs (timestamp (get args "at"))))
    | "iso8601" -> text (MessagesSupport.iso8601 (timestamp (get args "at")))
    | "json_time" -> text (MessagesSupport.jsonTime (timestamp (get args "at")))
    | "ruby_number" ->
        let number =
            match get args "int" with
            | v when v.ValueKind = JsonValueKind.Number -> MessagesSupport.RubyNumber.Int(v.GetInt64())
            | _ -> MessagesSupport.RubyNumber.Float(BitConverter.Int64BitsToDouble(Convert.ToInt64(str (get args "float_bits"), 16)))
        text ((if bool (get args "half") then number.Half else number).ToString())
    | "presentation" ->
        let message = messageView (get args "message")
        text ((HtmlOutput.ofNode (Markup.Messages.presentation (viewContext ctx shared) message) []).ToString())
    | "json_by_bots_index" -> text (MessagesJson.byBotsIndex (arr (get args "input") |> List.map messageJson))
    | "json_by_bots_show" -> text (MessagesJson.byBotsShow (messageJson (get args "input")))
    | "json_boosts_by_bots_show" -> text (MessagesJson.boostsByBotsShow (boostJson (get args "input")))
    | other -> failwith $"unknown messages op {other}"

let run (case: JsonElement) (shared: JsonElement) : Answer =
    let op = str (get case "op")
    let args = get case "args"
    let ctx = get case "ctx"
    let ctxOf () = viewContext ctx shared
    match Pages.tryRun op args ctx shared with
    | Some answer -> answer
    | None ->
    match Rest.tryRun op args ctx shared with
    | Some answer -> answer
    | None ->
    match op with
    | "layouts/application_wrapper" ->
        // The layout around parts that are HTML already (what `content_for` gave the ERB): each part is a trusted node.
        let part key = [ Falco.Markup.Text.raw (str (get args key)) ]
        let regions: Layouts.Regions =
            { Title = opt (get args "page_title")
              BodyClass = opt (get args "body_class")
              Head = part "head"
              Nav = part "nav"
              Content = part "content"
              Footer = part "footer"
              Sidebar = part "sidebar" }
        text ((HtmlOutput.ofDocument (Layouts.application (ctxOf ()) regions) []).ToString())
    | "layouts/_lightbox" -> text ((HtmlOutput.ofNode (Templates.Layouts._Lightbox.render (ctxOf ())) []).ToString())
    | "layouts/turbo_rails/frame" ->
        let regions =
            { Layouts.Regions.content [ Falco.Markup.Text.raw (str (get args "content")) ] with
                Head = [ Falco.Markup.Text.raw (str (get args "head")) ] }
        text ((HtmlOutput.ofNode (Layouts.frame regions) []).ToString())
    | "recorded/page" ->
        // A list of cached fragments in a page, framed or not: the fragments stay fragments, and the text is the answer.
        let items = arr (get args "sizes") |> List.mapi (fun n size -> fragmentOf n (intOf size))
        let unhanded = (fragmentOf 99 (intOf (get args "unhanded"))).ToString()
        let list =
            [ yield Falco.Markup.Text.raw "<ul>"
              for item in items do
                  yield Falco.Markup.Text.raw "\n  "
                  yield Markup.Html.fragment item
              yield Falco.Markup.Text.raw "\n</ul>"
              yield Falco.Markup.Text.raw unhanded ]
        let page =
            if bool (get args "frame") then
                Layouts.page (ctxOf ()) true (Layouts.ViewPage.content list items)
            else
                HtmlOutput.ofNodes list items
        let whole = page.ToString()
        if page.Fragments.Length <> items.Length then failwith "a page lost fragments"
        text whole
    | "accounts/_help_contact" ->
        text ((HtmlOutput.ofNodes (Templates.Accounts._HelpContact.render (ctxOf ()) (helpContact (get args "help_contact"))) []).ToString())
    | "accounts/_invite" -> text ((HtmlOutput.ofNode (Templates.Accounts._Invite.render (ctxOf ()) (str (get args "join_code"))) []).ToString())
    | "pwa/_install_instructions" -> text ((HtmlOutput.ofNodes (Templates.Pwa._InstallInstructions.render (ctxOf ())) []).ToString())
    | "pwa/_browser_settings" -> text ((HtmlOutput.ofNodes (Templates.Pwa._BrowserSettings.render (ctxOf ())) []).ToString())
    | "pwa/_system_settings" -> text ((HtmlOutput.ofNode (Templates.Pwa._SystemSettings.render (ctxOf ())) []).ToString())
    | "users/_mention" ->
        let user: MentionUser =
            { User = userSummary (get args "user")
              AttachableSgid = str (get args "attachable_sgid") }
        text ((HtmlOutput.ofNode (Templates.Users._Mention.render (ctxOf ()) user) []).ToString())
    | "users/autocompletables/_template" -> text ((HtmlOutput.ofNode (Templates.Users.Autocompletables._Template.render (ctxOf ())) []).ToString())
    | "users/sidebars/rooms/_direct" ->
        text ((HtmlOutput.ofNode (Templates.Users.Sidebars.Rooms._Direct.render (ctxOf ()) (sidebarDirect (get args "membership"))) []).ToString())
    | "users/sidebars/rooms/_shared" ->
        let room = get args "room"
        let room: SidebarRoom =
            { Id = int64Of (get room "id")
              ParamKey = str (get room "param_key")
              Name = str (get room "name")
              Unread = bool (get room "unread") }
        text ((HtmlOutput.ofNode (Templates.Users.Sidebars.Rooms._Shared.render room) []).ToString())
    | "users/sidebars/rooms/_direct_placeholder" ->
        text ((HtmlOutput.ofNode (Templates.Users.Sidebars.Rooms._DirectPlaceholder.render (ctxOf ()) (userSummary (get args "user"))) []).ToString())
    | "users/direct_room" ->
        let ctx = ctxOf ()
        let first, second = sidebarDirect (get args "membership"), sidebarDirect (get args "second")
        let cache = freshHybrid ()
        let a = await (UsersCached.directRoom cache ctx first)
        let b = await (UsersCached.directRoom cache ctx second)
        if not (obj.ReferenceEquals(a, b)) then failwith "the second render did not reuse the cached fragment"
        let c = b.ToString()
        { Body = a.ToString(); Text = Some c; Fragments = None }
    | "welcome/show" ->
        let ctx = ctxOf ()
        Pages.viewPageAnswer args ctx (Templates.Welcome.Show.rendered ctx (str (get args "current_user_name")))
    | _ when op.StartsWith "helpers/" -> helper (op.Substring "helpers/".Length) args ctx shared
    | _ when op.StartsWith "messages/" -> messageOp (op.Substring "messages/".Length) args ctx shared
    | "autocompletable/users_index_json" ->
        let users =
            arr (get args "users")
            |> List.map (fun u ->
                { User = userSummary (get u "user")
                  AttachableSgid = str (get u "attachable_sgid") }: MentionUser)
        text (Autocompletable.usersIndexJson users (str (get args "base_url")))
    | "searches/search_path" -> text (Searches.searchPath (str (get args "query")))
    | "rooms/room_display_name" ->
        text (Rooms.roomDisplayName (opt (get args "name")) (bool (get args "direct")) (strings (get args "others")) (opt (get args "for_user")))
    | "rooms/button_to_delete_room" ->
        text ((HtmlOutput.ofNode (Markup.Rooms.buttonToDeleteRoom (ctxOf ()) (int64Of (get args "room_id")) (str (get args "display_name"))) []).ToString())
    | "rooms/mention_prompt_src" -> text (Rooms.mentionPromptSrc (int64Of (get args "room_id")))
    | other -> failwith $"unknown op {other}"
