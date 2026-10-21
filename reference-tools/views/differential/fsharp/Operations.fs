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
open Campfire.Views.Helpers.Tag
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

let private form (args: JsonElement) : Forms.FormWith =
    let mutable form = Forms.formWith (str (get args "url"))
    opt (get args "model") |> Option.iter (fun m -> form <- form.Model m)
    opt (get args "method") |> Option.iter (fun m -> form <- form.Method m)
    opt (get args "id") |> Option.iter (fun m -> form <- form.Id m)
    opt (get args "class") |> Option.iter (fun m -> form <- form.Class m)
    for pair in arr (get args "data") do
        form <- form.Data(str (at pair 0), str (at pair 1))
    if bool (get args "auto_submit") then form <- form.AutoSubmit()
    if bool (get args "multipart") then form <- form.Multipart()
    form

let private field (form: Forms.FormWith) (w: Out) (field: JsonElement) : unit =
    let scoped =
        match opt (get field "fields_for") with
        | Some name -> form.FieldsFor name
        | None -> form
    let methodName = str (get field "method")
    let value = opt (get field "value")
    let options = attrs (get field "attrs")
    match str (get field "type") with
    | "text" -> scoped.TextField(w, methodName, value, options)
    | "email" -> scoped.EmailField(w, methodName, value, options)
    | "url" -> scoped.UrlField(w, methodName, value, options)
    | "password" -> scoped.PasswordField(w, methodName, options)
    | "hidden" -> scoped.HiddenField(w, methodName, value, options)
    | "file" -> scoped.FileField(w, methodName, options)
    | "text_area" -> scoped.TextArea(w, methodName, value, options)
    | "check_box" ->
        scoped.CheckBox(w, methodName, options, str (get field "checked_value"), str (get field "unchecked_value"), str (get field "current"))
    | other -> failwith $"field type {other}"

let private helper (name: string) (args: JsonElement) (ctx: JsonElement) (shared: JsonElement) : Answer =
    let view () = viewContext ctx shared
    let content = str (get args "content")
    match name with
    | "tag" ->
        let options = attrs (get args "attrs")
        let tag = str (get args "name")
        rendered (fun w ->
            match str (get args "kind") with
            | "content_tag" -> contentTag w tag options content
            | "content_tag_text" -> contentTagText w tag options content
            | "content_tag_block" -> contentTagBlock w tag options (fun w -> w.Raw content)
            | "builder_tag" -> builderTag w tag options
            | "legacy_tag" -> legacyTag w tag options
            | other -> failwith $"tag kind {other}")
    | "image_tag" -> rendered (fun w -> Assets.imageTag w (view ()) (str (get args "source")) (attrs (get args "attrs")))
    | "asset_path" -> text (Assets.assetPath (view ()) (str (get args "source")))
    | "link" ->
        let url, label = str (get args "url"), str (get args "text")
        let options = attrs (get args "attrs")
        rendered (fun w ->
            match str (get args "kind") with
            | "link_to" -> Links.linkTo w url options (fun w -> w.Raw content)
            | "link_to_text" -> Links.linkToText w label url options
            | "link_to_if" -> Links.linkToIf w (bool (get args "condition")) label url options
            | "mail_to" -> Links.mailTo w label
            | other -> failwith $"link kind {other}")
    | "form" ->
        let form = form args
        rendered (fun w ->
            if bool (get args "open") then
                form.Open w
            else
                form.Wrap(w, (fun inner -> for f in arr (get args "fields") do field form inner f)))
    | "button" ->
        let options = attrs (get args "attrs")
        let url = str (get args "url")
        rendered (fun w ->
            match str (get args "kind") with
            | "button_to" -> Forms.buttonTo w url options content
            | "button_to_block" -> Forms.buttonToBlock w url options (fun w -> w.Raw content)
            | "button_tag" -> Forms.buttonTag w options content
            | "hidden_field_tag" -> Forms.hiddenFieldTag w (str (get args "name")) (opt (get args "value")) options
            | "method_tag" -> Forms.methodTag w (str (get args "name"))
            | other -> failwith $"button kind {other}")
    | "application" ->
        match str (get args "name") with
        | "page_title_tag" -> text ((HtmlOutput.ofNode (Markup.Application.pageTitleTag (opt (get args "page_title"))) []).ToString())
        | "current_user_meta_tags" -> text ((HtmlOutput.ofNodes (Markup.Application.currentUserMetaTags (view ())) []).ToString())
        | "script_aware_action_cable_meta_tag" -> text ((HtmlOutput.ofNode (Markup.Application.scriptAwareActionCableMetaTag (view ())) []).ToString())
        | "custom_styles_tag" -> text ((HtmlOutput.ofNodes (Markup.Application.customStylesTag (view ())) []).ToString())
        | "body_classes" -> text (Markup.Application.bodyClasses (view ()) (opt (get args "body_class")))
        | "link_back" -> rendered (fun w -> Application.linkBack w (view ()))
        | "link_back_to" -> rendered (fun w -> Application.linkBackTo w (view ()) (str (get args "destination")))
        | "link_back_to_last_room_visited" -> rendered (fun w -> Application.linkBackToLastRoomVisited w (view ()))
        | "version_badge" -> rendered (fun w -> Application.versionBadge w (view ()))
        | "button_to_copy_to_clipboard" ->
            rendered (fun w -> Application.buttonToCopyToClipboard w (str (get args "url")) (fun w -> w.Raw content))
        | "link_to_zoom_qr_code" -> rendered (fun w -> Application.linkToZoomQrCode w (str (get args "url")) (fun w -> w.Raw content))
        | "web_share_session_button" ->
            rendered (fun w ->
                Application.webShareSessionButton w (str (get args "url")) (str (get args "title")) (str (get args "text")) (fun w ->
                    w.Raw content))
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
        | "avatar_tag" -> rendered (fun w -> UsersHelper.avatarTag w (view ()) avatar (attrs (get args "attrs")))
        | "avatar_background_color" -> text (UsersHelper.avatarBackgroundColor (int64Of (get args "id")))
        | "initials" -> text (UsersHelper.initials (str (get args "text")))
        | "initials_each" ->
            text (String.Join("\n", (str (get args "text")).EnumerateRunes() |> Seq.map (fun rune -> UsersHelper.initials (rune.ToString() + "x"))))
        | "user_title" -> text (UsersHelper.userTitle (str (get args "text")) (opt (get args "bio")))
        | "button_to_direct_room_with" -> rendered (fun w -> UsersHelper.buttonToDirectRoomWith w (view ()) (int64Of (get args "id")))
        | "curl_text_line" -> text (UsersHelper.curlTextLine (str (get args "text")))
        | "curl_upload_line" -> text (UsersHelper.curlUploadLine (str (get args "text")))
        | "account_logo_tag" -> rendered (fun w -> UsersHelper.accountLogoTag w (view ()) (opt (get args "style")))
        | "profile_form_submit_button" -> rendered (fun w -> UsersHelper.profileFormSubmitButton w (view ()))
        | "sidebar_turbo_frame_tag" ->
            rendered (fun w -> UsersHelper.sidebarTurboFrameTag w (opt (get args "src")) (fun w -> w.Raw(str (get args "content"))))
        | "user_filter_menu_tag" -> rendered (fun w -> UsersHelper.userFilterMenuTag w (fun w -> w.Raw(str (get args "content"))))
        | "user_filter_search_tag" -> rendered UsersHelper.userFilterSearchTag
        | other -> failwith $"users helper {other}"
    | "rooms" ->
        match str (get args "name") with
        | "link_to_room" ->
            rendered (fun w -> RoomsHelper.linkToRoom w (int64Of (get args "room_id")) (attrs (get args "attrs")) (fun w -> w.Raw content))
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
        | "translations_for" -> rendered (fun w -> Translations.translationsFor w (str (get args "key")))
        | "translation_button" -> rendered (fun w -> Translations.translationButton w (view ()) (str (get args "key")))
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
        | "turbo_stream_from" -> rendered (fun w -> Turbo.turboStreamFrom w (str (get args "name")))
        | "page_requires_reload" -> rendered Turbo.turboPageRequiresReloadTag
        | "dom_id" -> text (Turbo.domId (str (get args "model")) (int64Of (get args "id")) (opt (get args "prefix")))
        | "turbo_frame_tag" ->
            rendered (fun w -> Filters.turboFrameTag w (str (get args "id")) (attrs (get args "attrs")) (fun w -> w.Raw content))
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
        let c = Render.text (fun w -> w.Lit b.Bytes)
        { Out = a.ToString(); Text = Some c; Fragments = None }
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
