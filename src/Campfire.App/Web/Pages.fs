// plans/design-kit-auth.md section 4, unit 2.9 (Web/Pages.fs): pages and bodies on `HttpContext`.
//
// What `Presenters/ViewContext.fs` and `Presenters/Page.fs` do with a `Ctx`, for a Web action: the layout's data from
// `Current` and the database, the `ViewContext` of this request (flash, URLs, referer), and the handlers that write a
// rendered page: the application layout, or turbo-rails' frame layout for a Turbo-Frame request. A page is written by the
// kit's writer (`Adapter.write`), so a page of cached fragments still goes out in parts, already compressed.
namespace Campfire.App.Web

open System
open System.Text
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Campfire.App
open Campfire.App.Presenters
open Campfire.Assets
open Campfire.Db
open Campfire.Ruby
open Campfire.Views
open Campfire.Views.Templates.Sessions

module Pages =
    /// Where the route table leaves the matched row's `controller#action` (the layout choice of `before_action`s reads it).
    [<Literal>]
    let EndpointKey = "campfire.web.endpoint"

    let endpoint (http: HttpContext) : string =
        match http.Items.TryGetValue EndpointKey with
        | true, (:? string as endpoint) -> endpoint
        | _ -> ""

    /// `platform` (`helper_method`): the one `allow_browser` kept, or one made from the user agent.
    let platform (http: HttpContext) : ApplicationPlatform =
        let current = Current.get http
        match current.Platform with
        | Some platform -> platform
        | None ->
            let agent = http.Request.Headers.UserAgent
            let platform = ApplicationPlatform.create (if agent.Count = 0 then null else agent.ToString())
            current.Platform <- Some platform
            platform

    /// The `last_room` cookie, as `find_by(id: cookies[:last_room])` casts it (like an integer column).
    let lastRoomCookie (http: HttpContext) : int64 option =
        match http.Request.Cookies["last_room"] with
        | null -> None
        | value -> Ruby.integerCast value

    /// `last_room_visited`: the `last_room` cookie's room if the user is in it, else `Current.user.rooms.original`.
    let lastRoomVisited (http: HttpContext) : Result<Room option, Error> =
        match Current.user http with
        | None -> Ok None
        | Some user ->
            let lastRoom = lastRoomCookie http
            Data.read http (fun db -> Campfire.App.Current.lastRoomVisitedIn db user.Id lastRoom)

    // --- the layout -----------------------------------------------------------------------------------

    /// `Current.account`, `Current.user`, `last_room_visited` and the platform. With no account yet (first run) the
    /// account summary is blank: the pages that reference the account raise in Rails then, the others don't read it.
    let load (http: HttpContext) : Result<Presenters.Layout, Error> =
        let app = Data.app http
        let secrets = app.Secrets
        let user = Current.user http
        let userId = user |> Option.map (fun user -> user.Id)
        let lastRoom = lastRoomCookie http
        Data.read http (fun db ->
            let lastRoomVisitedId =
                match userId with
                | Some userId -> Campfire.App.Current.lastRoomVisitedIn db userId lastRoom |> Option.map (fun room -> room.Id)
                | None -> None
            let account = Account.first db
            let hasLogo =
                match account with
                | Some account -> (Attachments.attachedBlob db "Account" account.Id "logo").IsSome
                | None -> false
            account, hasLogo, lastRoomVisitedId)
        |> Result.map (fun (account, hasLogo, lastRoomVisitedId) ->
            { CurrentUser = user |> Option.map (ViewContextBuilder.currentUser secrets)
              Account = ViewContextBuilder.accountSummary account hasLogo
              CustomStyles = account |> Option.bind (fun account -> Option.ofObj account.CustomStyles)
              Platform = ApplicationPlatform.toView (platform http)
              LastRoomVisitedId = lastRoomVisitedId
              VapidPublicKey = app.VapidPublicKey
              AppVersion = app.Config.AppVersion })

    let private option (value: string | null) : string option =
        match value with
        | null -> None
        | value -> Some value

    /// `request.url`
    let requestUrl (http: HttpContext) : string =
        let query =
            match http.Request.QueryString.Value with
            | null
            | "?" -> ""
            | query -> query
        Respond.urlFor http (Respond.rawPath http + query)

    /// Renders with a `ViewContext` for this request. The flash is read (and so swept at the end of the request) the way
    /// the layout's `flash[:notice]` / `flash[:alert]` read it.
    let render (layout: Presenters.Layout) (http: HttpContext) (render: ViewContext -> 'T) : 'T =
        let flash = Respond.flash http
        let stylesheets = ViewContextBuilder.stylesheetTags ()
        let referer = http.Request.Headers.Referer
        let ctx: ViewContext =
            { CurrentUser = layout.CurrentUser
              Account = layout.Account
              FlashNotice = option flash.Notice
              FlashAlert = option flash.Alert
              Platform = layout.Platform
              VapidPublicKey = layout.VapidPublicKey
              AssetPath = Assets.assetPath
              ImportmapTags = Assets.javascriptImportmapTags ()
              StylesheetTags = stylesheets.Html
              CustomStyles = layout.CustomStyles
              CableUrl = "/cable"
              BaseUrl = Respond.urlFor http ""
              RequestUrl = requestUrl http
              Referrer = (if referer.Count = 0 then None else Some(referer.ToString()))
              LastRoomVisitedId = layout.LastRoomVisitedId
              AppVersion = layout.AppVersion }
        render ctx

    // --- writing -------------------------------------------------------------------------------------

    /// `request.should_apply_vary_header?`: the format came from the `Accept` header, not a format parameter.
    let private needsVary (http: HttpContext) : bool =
        let headers = http.Request.Headers
        let header (name: string) : string | null =
            match headers[name] with
            | values when values.Count = 0 -> null
            | values -> values.ToString()
        let input: Campfire.Kit.Format.NegotiationInput =
            { FormatParam = Params.value http "format"
              Accept = header "Accept"
              ContentType = header "Content-Type"
              Path = http.Request.Path.Value |> nonNull
              Xhr = String.Equals(header "X-Requested-With", "xmlhttprequest", StringComparison.OrdinalIgnoreCase) }
        Campfire.Kit.Format.shouldApplyVaryHeader input

    /// The handler that writes a kit response with the kit's writer.
    let write (response: Campfire.Kit.Response) : HttpHandler =
        fun http ->
            let kit = http.RequestServices.GetRequiredService<Campfire.Kit.Kit>()
            Campfire.Kit.Adapter.write kit http response (HttpMethods.IsHead http.Request.Method)

    let private withVary (http: HttpContext) (response: Campfire.Kit.Response) : Campfire.Kit.Response =
        if needsVary http then response.Header(Campfire.Kit.Hdr.Vary, "Accept") else response

    /// A body with an explicit content type (`render json:`, `render html:`, a manifest), as the handler that writes it.
    let renderAs (http: HttpContext) (status: int) (contentType: string) (body: ReadOnlyMemory<byte>) : HttpHandler =
        let response = Campfire.Kit.Response(status).ContentType contentType
        response.Body <- Campfire.Kit.Body.Bytes body
        write (withVary http response)

    let renderText (http: HttpContext) (status: int) (contentType: string) (body: string) : HttpHandler =
        renderAs http status contentType (ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes body))

    /// `render html:`
    let html (http: HttpContext) (body: string) : HttpHandler =
        renderText http 200 Campfire.Kit.Response.HtmlUtf8 body

    /// `render json:`
    let json (http: HttpContext) (status: int) (value: Campfire.RailsCompat.Value) : HttpHandler =
        renderAs http status Campfire.Kit.Response.JsonUtf8 (ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(Campfire.RailsCompat.Json.generate value)))

    /// A recorded page (its text and the cached fragments it holds) as a response in the HTML format, spliced into parts
    /// when it holds fragments big enough for a part.
    let private recorded (http: HttpContext) (status: int) (page: RecordedPage) : Campfire.Kit.Response =
        let response = Campfire.Kit.Response(status).ContentType $"{Campfire.Kit.Format.Html.String}; charset=utf-8"
        if page.Fragments.Length = 0 then
            response.Body <- Campfire.Kit.Body.Bytes(ReadOnlyMemory<byte> page.Text)
        else
            match FragmentGlue.splice page with
            | Ok parts -> response.Body <- Campfire.Kit.Body.Parts parts
            | Result.Error whole -> response.Body <- Campfire.Kit.Body.Bytes whole
        withVary http response

    /// A page rendered in the application layout: plus the `Link` preload header `stylesheet_link_tag` adds.
    let private page (http: HttpContext) (status: int) (html: RecordedPage) : HttpHandler =
        let links = (ViewContextBuilder.stylesheetTags ()).PreloadLinks
        let response = recorded http status html
        write (response.Header("link", Assets.appendPreloadLinks "" links))

    /// A page rendered in turbo-rails' frame layout (no stylesheets, so no `Link` header).
    let private frame (http: HttpContext) (status: int) (html: RecordedPage) : HttpHandler = write (recorded http status html)

    /// Renders a page in the application layout without the implicit render's template lookup: an explicit
    /// `render template:` answers HTML whatever the request's format.
    let pageInAnyFormat (http: HttpContext) (status: int) (full: ViewContext -> RecordedPage) : Result<HttpHandler, Error> =
        load http |> Result.map (fun layout -> page http status (render layout http full))

    /// A page, or for a Turbo-Frame request its `head` and `content` in turbo-rails' frame layout
    /// (`layout -> { "turbo_rails/frame" if turbo_frame_request? }`), without the template lookup.
    let pageOrFrameInAnyFormat
        (http: HttpContext)
        (status: int)
        (full: ViewContext -> RecordedPage)
        (frameLayout: ViewContext -> RecordedPage)
        : Result<HttpHandler, Error> =
        load http
        |> Result.map (fun layout ->
            if Formats.isTurboFrame http then
                frame http status (render layout http frameLayout)
            else
                page http status (render layout http full))

    /// The implicit render's template lookup (`default_render`): an action whose only template is `<action>.html.erb` can't
    /// answer a request that doesn't accept HTML, which is `ActionController::UnknownFormat` (406).
    let findTemplate (http: HttpContext) (template: Format) : Result<unit, Error> = Formats.respondTo http [ template ] |> Result.map ignore

    /// `pageOrFrameInAnyFormat` after the implicit render's template lookup.
    let pageOrFrame (http: HttpContext) (status: int) (full: ViewContext -> RecordedPage) (frameLayout: ViewContext -> RecordedPage) =
        match findTemplate http Format.Html with
        | Result.Error e -> Result.Error e
        | Ok() -> pageOrFrameInAnyFormat http status full frameLayout

    /// A template that extends `layouts/application` itself (`blocks = ["head", "content"]`): the full page, or for a
    /// Turbo-Frame request its `head` and `content` in turbo-rails' frame layout. `size` is the page's own `RenderSize`.
    let framedPage
        (http: HttpContext)
        (status: int)
        (size: RenderSize)
        (render: Out -> ViewContext -> unit)
        (head: Out -> unit)
        (content: Out -> ViewContext -> unit)
        : Result<HttpHandler, Error> =
        pageOrFrame http status (fun ctx -> size.Render(fun w -> render w ctx)) (fun ctx -> Layouts.frame size head (fun w -> content w ctx))

    // --- AllowBrowser ---------------------------------------------------------------------------------

    /// What `IncompatibleBrowser.render` writes with the Turbo-Frame layout's help, sized from its last render.
    let private incompatibleBrowserSize = RenderSize()

    /// `render template: "sessions/incompatible_browser"` (200). Rendered from a before-action, so it's HTML whatever the
    /// request format. The layout is the controller's: turbo-rails' frame layout for a Turbo-Frame request, except in
    /// controllers that declare their own layout (`MessagesController` and its `Messages::ByBotsController`).
    let incompatibleBrowser (http: HttpContext) : Result<HttpHandler, Error> =
        let endpoint = endpoint http
        let ownLayout = endpoint.StartsWith("messages#", StringComparison.Ordinal) || endpoint.StartsWith("messages/by_bots#", StringComparison.Ordinal)
        if ownLayout then
            pageInAnyFormat http 200 (fun ctx -> incompatibleBrowserSize.Render(fun w -> IncompatibleBrowser.render w ctx))
        else
            pageOrFrameInAnyFormat
                http
                200
                (fun ctx -> incompatibleBrowserSize.Render(fun w -> IncompatibleBrowser.render w ctx))
                (fun ctx -> Layouts.frame incompatibleBrowserSize IncompatibleBrowser.head (fun w -> IncompatibleBrowser.content w ctx))
