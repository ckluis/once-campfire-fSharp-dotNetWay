/// The F# counterpart of `src/pages.rs`: the hot-path templates (the room page, messages and their partials, the
/// sidebar, search, the room forms and the mentions prompt). A branch of `tryRun` here is the branch of the same
/// name there, and the arguments are described there.
module Campfire.Views.Differential.Pages

open System
open System.Text
open System.Text.Json
open Campfire.Views
open Campfire.Views.Messages
open Campfire.Views.Users
open Campfire.Views.Differential.Answer
open Campfire.Views.Differential.Inputs

let private known =
    set
        [ "messages/_message"; "messages/message_cached"; "messages/_actions"; "messages/_presentation"; "messages/_unrenderable"
          "messages/_template"; "messages/index"; "messages/show"; "messages/edit"; "messages/create_turbo_stream"
          "messages/destroy_turbo_stream"; "messages/room_not_found"; "messages/boosts/_boost"; "messages/boosts/_boosts"
          "messages/boosts/index"; "messages/boosts/new"; "rooms/show"; "rooms/involvements/show"; "rooms/refreshes/show"
          "rooms/opens/new"; "rooms/opens/edit"; "rooms/closeds/new"; "rooms/closeds/edit"; "rooms/directs/new"; "rooms/directs/edit"
          "rooms/layouts/_form"; "searches/index"; "autocompletable/users/index"; "autocompletable/users/_prompt_item"
          "users/sidebars/show" ]

/// `mode`: "mixed" fetches every second message through the cache (a hit gives the one instance); the others are rendered
/// here. The pages must come out the same either way.
let private items (ctx: ViewContext) (args: JsonElement) (values: JsonElement list) : MessageItem list =
    let mixed = str (get args "mode") = "mixed"
    let cache = if mixed then freshHybrid () else Unchecked.defaultof<_>
    values |> List.mapi (fun n v -> if mixed && n % 2 = 1 then cachedItem cache ctx (messageView v) else messageItem ctx v)

/// A Falco template (a partial or a page without a layout): rendered cold and then warm, the same bytes. A page in parts answers its
/// whole text; the writer's fragment offsets are not Falco's (the bytes differ: class A), so none are given.
let nodeAnswer (render: unit -> HtmlOutput) : Answer =
    let cold = (render ()).ToString()
    let warm = (render ()).ToString()
    if cold <> warm then failwith "a warm render is not the cold one"
    text warm

let private recordedAnswer (page: HtmlOutput) : Answer =
    { Out = page.ToString()
      Text = Some(recordedText page)
      Fragments = Some(recordedOffsets page) }

/// A page: rendered cold, then warm (the same bytes), plain and recorded (the same bytes), and in the Turbo-Frame
/// layout when `frame` is set.
let pageAnswer (args: JsonElement) (render: Out -> unit) (head: Out -> unit) (content: Out -> unit) : Answer =
    let frame = bool (get args "frame")
    let once () =
        let plain = Render.text render
        let recorded = if frame then Layouts.frame (RenderSize()) head content else Render.page 0 render
        plain, recorded
    let coldPlain, cold = once ()
    let plain, recorded = once ()
    if cold.ToString() <> recorded.ToString() || coldPlain <> plain then failwith "a warm render is not the cold one"
    if not frame && recorded.ToString() <> plain then failwith "a recorded page is not the plain render"
    recordedAnswer recorded

/// A Falco page: its regions in the writer's layouts (`Legacy.inLayout`, until unit 4.10), cold and then warm, the same bytes, and
/// in the Turbo-Frame layout when `frame` is set. The page's text is the answer (the writer's fragment offsets are not Falco's).
let viewPageAnswer (args: JsonElement) (ctx: ViewContext) (page: Layouts.ViewPage) : Answer =
    let frame = bool (get args "frame")
    let cold = (Legacy.inLayout ctx frame page).ToString()
    let warm = (Legacy.inLayout ctx frame page).ToString()
    if cold <> warm then failwith "a warm render is not the cold one"
    text warm

/// A body that is not markup (the manifest): written twice, the same bytes.
let nodeBytesAnswer (render: unit -> byte[]) : Answer =
    let cold = Encoding.UTF8.GetString(render ())
    let warm = Encoding.UTF8.GetString(render ())
    if cold <> warm then failwith "a warm render is not the cold one"
    text warm

/// A template that is not a page: rendered cold and then warm.
let fragmentAnswer (render: Out -> unit) : Answer =
    let cold = Render.text render
    let warm = Render.text render
    if cold <> warm then failwith "a warm render is not the cold one"
    text warm

/// A list of messages as a recorded page, as a template that isn't a page is served.
let private recordedFragmentAnswer (render: Out -> unit) : Answer =
    let recorded = Render.page 0 render
    let plain = Render.text render
    if recorded.ToString() <> plain then failwith "a recorded page is not the plain render"
    { recordedAnswer recorded with Out = plain }

let private run (op: string) (args: JsonElement) (ctx: ViewContext) : Answer =
    let message () = messageView (get args "message")
    match op with
    | "messages/_message" ->
        let message = message ()
        let boosts = renderedBoosts ctx message
        let placed = if message.IsUnrenderable then [||] else boosts
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Messages._Message.render ctx message boosts) placed)
    | "messages/message_cached" ->
        let first, second = messageView (get args "message"), messageView (get args "second")
        let cache = freshHybrid ()
        let a = await (MessagesCached.message cache ctx first)
        let b = await (MessagesCached.message cache ctx second)
        if not (obj.ReferenceEquals(a, b)) then failwith "the second render did not reuse the cached fragment"
        let c = (Templates.Messages.Show.output b).ToString()
        let d = b.ToString()
        if c <> d then failwith "the copied fragment and the placed one differ"
        { Out = a.ToString(); Text = Some c; Fragments = None }
    | "messages/_actions" ->
        let message = message ()
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Messages._Actions.render ctx message) [])
    | "messages/_presentation" ->
        let message = message ()
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Messages._Presentation.render ctx message) [])
    | "messages/_unrenderable" -> nodeAnswer (fun () -> HtmlOutput.ofNode Templates.Messages._Unrenderable.render [])
    | "messages/_template" ->
        let user = userView (get args "user")
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Messages._Template.render ctx user) [])
    | "messages/index" ->
        let messages = items ctx args (arr (get args "messages"))
        nodeAnswer (fun () -> Templates.Messages.Index.output messages)
    | "messages/show" ->
        let fragment = renderedMessage ctx (message ())
        nodeAnswer (fun () -> Templates.Messages.Show.output fragment)
    | "messages/edit" ->
        let edit = editView (get args "edit")
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Messages.Edit.render ctx edit) [])
    | "messages/create_turbo_stream" ->
        let message = message ()
        let kind = roomKind (get args "room_kind")
        let item = if bool (get args "fragment") then cachedItem (freshHybrid ()) ctx message else itemOf message (renderedMessage ctx message)
        nodeAnswer (fun () -> Templates.Messages.CreateTurboStream.output item kind)
    | "messages/destroy_turbo_stream" ->
        let message = message ()
        nodeAnswer (fun () -> Templates.Messages.DestroyTurboStream.output message)
    | "messages/room_not_found" -> nodeAnswer (fun () -> HtmlOutput.ofNode Templates.Messages.RoomNotFound.render [])
    | "messages/boosts/_boost" ->
        let boost = boostView (get args "boost")
        let cache = freshHybrid ()
        let first = await (BoostsCached.boost cache ctx boost)
        let second = await (BoostsCached.boost cache ctx boost)
        if not (obj.ReferenceEquals(first, second)) then failwith "the second render did not reuse the cached fragment"
        let partial = (HtmlOutput.ofNodes (Templates.Messages.Boosts._Boost.render ctx boost) []).ToString()
        if partial <> first.ToString() then failwith "the cached boost is not the partial"
        { Out = partial; Text = Some(first.ToString()); Fragments = None }
    | "messages/boosts/_boosts" ->
        let message = message ()
        let boosts = renderedBoosts ctx message
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Messages.Boosts._Boosts.render ctx message boosts) boosts)
    | "messages/boosts/index" ->
        let message = message ()
        let boosts = renderedBoosts ctx message
        nodeAnswer (fun () -> Templates.Messages.Boosts.Index.output ctx message boosts)
    | "messages/boosts/new" ->
        let message = message ()
        let user = userView (get args "user")
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Messages.Boosts.New.render ctx message user) [])
    | "rooms/show" ->
        let show = showView ctx (get args "show")
        let show = { show with Messages = items ctx args (arr (get (get args "show") "messages")) }
        viewPageAnswer args ctx (Templates.Rooms.ShowPage.rendered ctx show)
    | "rooms/involvements/show" ->
        let involvement = involvementView (get args "involvement")
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Rooms.Involvements.Show.render ctx involvement) [])
    | "rooms/refreshes/show" ->
        let refresh = refreshView ctx (get args "refresh")
        let refresh =
            { refresh with
                NewMessages = items ctx args (arr (get (get args "refresh") "new_messages"))
                UpdatedMessages = items ctx args (arr (get (get args "refresh") "updated_messages")) }
        nodeAnswer (fun () -> Templates.Rooms.Refreshes.ShowTurboStream.output refresh)
    | "rooms/opens/new" ->
        let form = openFormView (get args "form")
        viewPageAnswer args ctx (Templates.Rooms.Opens.New.rendered ctx form)
    | "rooms/opens/edit" ->
        let form = openFormView (get args "form")
        viewPageAnswer args ctx (Templates.Rooms.Opens.Edit.rendered ctx form)
    | "rooms/closeds/new" ->
        let form = closedFormView (get args "form")
        viewPageAnswer args ctx (Templates.Rooms.Closeds.New.rendered ctx form)
    | "rooms/closeds/edit" ->
        let form = closedFormView (get args "form")
        viewPageAnswer args ctx (Templates.Rooms.Closeds.Edit.rendered ctx form)
    | "rooms/directs/new" ->
        viewPageAnswer args ctx (Templates.Rooms.Directs.New.rendered ctx)
    | "rooms/directs/edit" ->
        let edit = directEditView (get args "edit")
        viewPageAnswer args ctx (Templates.Rooms.Directs.Edit.rendered ctx edit)
    | "rooms/layouts/_form" ->
        let room = formRoom (get args "room")
        let kind = roomKind (get args "kind")
        let content = Markup.Html.trusted (Markup.Html.Trusted.ofSanitized (str (get args "content")))
        nodeAnswer (fun () ->
            HtmlOutput.ofNode (Templates.Rooms.Layouts._Form.render ctx room (bool (get args "can_administer")) kind [ content ]) [])
    | "searches/index" ->
        let index = searchesIndexView ctx (get args "index")
        let index = { index with Messages = items ctx args (arr (get (get args "index") "messages")) }
        viewPageAnswer args ctx (Templates.Searches.Index.rendered ctx index)
    | "autocompletable/users/index" ->
        let users = arr (get args "users") |> List.map mentionUser
        nodeAnswer (fun () -> HtmlOutput.ofNodes (Templates.Autocompletable.Users.Index.render ctx users) [])
    | "autocompletable/users/_prompt_item" ->
        let user = mentionUser (get args "user")
        nodeAnswer (fun () -> HtmlOutput.ofNode (Templates.Autocompletable.Users._PromptItem.render ctx user) [])
    | "users/sidebars/show" ->
        let mixedDirect = str (get args "mode") = "mixed"
        let cache = if mixedDirect then freshHybrid () else Unchecked.defaultof<_>
        let sidebar: SidebarShow =
            { CurrentUser = userSummary (get args "current_user")
              RoomsStream = str (get args "rooms_stream")
              UserRoomsStream = str (get args "user_rooms_stream")
              DirectMemberships =
                arr (get args "direct")
                |> List.mapi (fun n v ->
                    let membership = sidebarDirect v
                    if mixedDirect && n % 2 = 1 then
                        { Html = await (UsersCached.directRoom cache ctx membership) }
                    else
                        { Html = HtmlOutput.toFragment (HtmlOutput.ofNode (Templates.Users.Sidebars.Rooms._Direct.render ctx membership) []) })
              DirectPlaceholderUsers = arr (get args "placeholders") |> List.map userSummary
              OtherMemberships =
                arr (get args "shared")
                |> List.map (fun r ->
                    { Id = int64Of (get r "id")
                      ParamKey = str (get r "param_key")
                      Name = str (get r "name")
                      Unread = bool (get r "unread") })
              CanCreateRooms = bool (get args "can_create_rooms") }
        viewPageAnswer args ctx (Templates.Users.Sidebars.Show.rendered ctx sidebar)
    | _ -> failwith $"unknown pages op {op}"

let tryRun (op: string) (args: JsonElement) (ctx: JsonElement) (shared: JsonElement) : Answer option =
    if known.Contains op then
        Some(run op args (viewContext ctx shared))
    else
        None
