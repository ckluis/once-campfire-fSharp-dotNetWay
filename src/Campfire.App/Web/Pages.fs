// plans/design-kit-auth.md section 4, unit 2.9 (Web/Pages.fs): pages and bodies on `HttpContext`.
//
// The layout's data from
// `Current` and the database, the `ViewContext` of this request (flash, URLs, referer), and the handlers that write a
// rendered page: the application layout, or turbo-rails' frame layout for a Turbo-Frame request. A page of cached fragments goes out in
// parts, already compressed.
namespace Campfire.App.Web

open System
open System.Text
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.ResponseCompression
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

    /// `remember_last_room_visited`: `cookies.permanent[:last_room] = @room.id`, only when it changes (Rails sets it on every room page).
    let rememberLastRoomVisited (http: HttpContext) (roomId: int64) : unit =
        let roomId = string roomId
        if http.Request.Cookies["last_room"] <> roomId then
            // `cookies.permanent`: 20 calendar years out on the harness's clock. `UseCookiePolicy` adds `secure`/`samesite`.
            let expires = Campfire.RailsCompat.Cookies.permanentExpiresAt (Data.now http)
            http.Response.Cookies.Append("last_room", roomId, CookieOptions(Path = "/", Expires = Nullable expires, SameSite = SameSiteMode.Lax))

    /// The base of the URLs in a render outside the request during it: `SetCurrentRequest`'s `default_url_options` carry only the
    /// host and protocol, so a request to `http://localhost:3999` broadcasts `http://localhost/...` links.
    let rendererBaseUrl (http: HttpContext) : string = $"{http.Request.Scheme}://{http.Request.Host.Host}"

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

    [<Literal>]
    let HtmlUtf8 = "text/html; charset=utf-8"

    [<Literal>]
    let JsonUtf8 = "application/json; charset=utf-8"

    [<Literal>]
    let TurboStreamUtf8 = "text/vnd.turbo-stream.html; charset=utf-8"

    /// What a rendered response carries as its body: one buffer, or a page of cached fragments in parts.
    type PageBody =
        | Bytes of ReadOnlyMemory<byte>
        | Parts of IPageParts

    /// The compressed form of a page of cached fragments, kept by the page's identity (its ETag digest, which covers every byte)
    /// and the encoding. A room page is ~400 KB of HTML that repeats from one request to the next, so compressing it afresh
    /// (and copying it through the compression middleware's stream) was the unit 2.8 shortfall against the 5x floor; the
    /// bytes themselves are the framework's provider's (`IResponseCompressionProvider`, gzip or Brotli at their fastest).
    module private PageCompression =
        let private gate = obj ()

        let private kept =
            Generations<string, byte[]>(16 * 1024 * 1024, (fun key bytes -> key.Length + bytes.Length + CacheCosts.EntryOverhead))

        /// The encoding and bytes of `parts` for a request that takes compression, or nothing.
        let compressed (http: HttpContext) (parts: IPageParts) : struct (string * byte[]) voption =
            match http.RequestServices.GetService<IResponseCompressionProvider>() with
            | null -> ValueNone
            | provider ->
                if provider.CheckRequestAcceptsCompression http && provider.ShouldCompressResponse http then
                    match provider.GetCompressionProvider http with
                    | null -> ValueNone
                    | encoder ->
                        let key = String.Concat(encoder.EncodingName, ":", parts.Etag())
                        match lock gate (fun () -> kept.Get(key, id)) with
                        | ValueSome bytes -> ValueSome(struct (encoder.EncodingName, bytes))
                        | ValueNone ->
                            let plain = System.Buffers.ArrayBufferWriter<byte>(parts.BodyLength)
                            parts.WritePlain plain
                            use output = new System.IO.MemoryStream()
                            (use stream = encoder.CreateStream output
                             stream.Write plain.WrittenSpan)
                            let bytes = output.ToArray()
                            lock gate (fun () -> kept.Insert(key, bytes))
                            ValueSome(struct (encoder.EncodingName, bytes))
                else
                    ValueNone

    /// Writes a rendered response: the status and headers, the length the host will send (known for a buffer and for parts),
    /// and the body, which a HEAD request drops (`Rack::Head`) while keeping its `Content-Length`. Compression is the
    /// framework's (`UseResponseCompression`); a page in parts has its compressed form kept and written already encoded.
    let private send (status: int) (contentType: string) (headers: (string * string) list) (body: PageBody) : HttpHandler =
        fun http ->
            task {
                let response = http.Response
                response.StatusCode <- status
                response.Headers.ContentType <- contentType
                for (name, value) in headers do
                    response.Headers.Append(name, value)
                response.ContentLength <- Nullable(int64 (match body with Bytes bytes -> bytes.Length | Parts parts -> parts.BodyLength))
                if Edge.headFromPost http then response.ContentLength <- Nullable()
                try
                    if not (HttpMethods.IsHead http.Request.Method) then
                        let compressed =
                            match body with
                            | Parts parts -> PageCompression.compressed http parts
                            | Bytes _ -> ValueNone
                        match compressed, body with
                        | ValueSome(struct (encoding, bytes)), _ ->
                            // Already encoded, so the compression middleware leaves it (and its `Vary`) alone.
                            response.Headers.ContentEncoding <- encoding
                            response.Headers.Append("Vary", "Accept-Encoding")
                            response.ContentLength <- Nullable(int64 bytes.Length)
                            do! response.Body.WriteAsync bytes
                        | ValueNone, Bytes bytes -> do! response.Body.WriteAsync bytes
                        | ValueNone, Parts parts ->
                            parts.WritePlain response.BodyWriter
                            let! _ = response.BodyWriter.FlushAsync().AsTask()
                            ()
                with
                // The client went away; the host has nothing more to say about it.
                | :? OperationCanceledException
                | :? System.IO.IOException -> ()
            }
            :> Task

    /// The `Vary: Accept` pair of a response whose format came from the `Accept` header.
    let private varyHeaders (http: HttpContext) : (string * string) list =
        if Formats.shouldApplyVary http then [ "Vary", "Accept" ] else []

    /// A body with an explicit content type (`render json:`, `render html:`, a manifest), as the handler that writes it.
    let renderAs (http: HttpContext) (status: int) (contentType: string) (body: ReadOnlyMemory<byte>) : HttpHandler =
        send status contentType (varyHeaders http) (Bytes body)

    let renderText (http: HttpContext) (status: int) (contentType: string) (body: string) : HttpHandler =
        renderAs http status contentType (ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes body))

    /// A Turbo Stream response (`render turbo_stream:` / `index.turbo_stream.erb`).
    let turboStream (http: HttpContext) (body: ReadOnlyMemory<byte>) : HttpHandler = renderAs http 200 TurboStreamUtf8 body

    /// `render html:`
    let html (http: HttpContext) (body: string) : HttpHandler = renderText http 200 HtmlUtf8 body

    /// `render json:`
    let json (http: HttpContext) (status: int) (value: Campfire.RailsCompat.Value) : HttpHandler =
        renderAs http status JsonUtf8 (ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(Campfire.RailsCompat.Json.generate value)))

    /// `set_paginated_headers` (after_action), for JSON requests: `X-Total-Count`, and a `Link` to the next page unless this is the last.
    let applyPageHeaders (http: HttpContext) (page: Pagination.Page) : unit =
        let json =
            match Formats.requested http with
            | Ok(first :: _) -> first = Format.Json
            | _ -> false
        if json then
            http.Response.Headers["x-total-count"] <- string page.RecordsCount
            if not page.IsLast then
                let url = Pagination.Url.withPage (requestUrl http) (string page.NextParam)
                http.Response.Headers["link"] <- $"<{url}>; rel=\"next\""

    /// A recorded page (its text and the cached fragments it holds) as a response in the HTML format, spliced into parts
    /// when it holds fragments big enough for a part.
    let private recordedAs (http: HttpContext) (status: int) (contentType: string) (extraHeaders: (string * string) list) (page: RecordedPage) : HttpHandler =
        let body =
            if page.Fragments.Length = 0 then
                Bytes(ReadOnlyMemory<byte> page.Text)
            else
                match FragmentGlue.splice page with
                | Ok parts -> Parts(parts :> IPageParts)
                | Result.Error whole -> Bytes whole
        send status contentType (varyHeaders http @ extraHeaders) body

    /// A page rendered in the application layout: plus the `Link` preload header `stylesheet_link_tag` adds.
    let private page (http: HttpContext) (status: int) (html: RecordedPage) : HttpHandler =
        let links = (ViewContextBuilder.stylesheetTags ()).PreloadLinks
        recordedAs http status HtmlUtf8 [ "link", Assets.appendPreloadLinks "" links ] html

    /// A page rendered in turbo-rails' frame layout (no stylesheets, so no `Link` header).
    let private frame (http: HttpContext) (status: int) (html: RecordedPage) : HttpHandler = recordedAs http status HtmlUtf8 [] html

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

    /// A content-only template: Rails wraps it in the application layout, or in turbo-rails' `layouts/turbo_rails/frame` when the
    /// request carries a `Turbo-Frame` header.
    let content (http: HttpContext) (status: int) (render: Out -> ViewContext -> unit) : Result<HttpHandler, Error> =
        pageOrFrame
            http
            status
            (fun ctx -> Render.page 0 (fun w -> Layouts.Application.render w ctx (Layouts.Application.create (Render.html (fun w -> render w ctx)))))
            (fun ctx -> Render.page 0 (fun w -> Templates.Layouts.TurboRails.Frame.render w (fun _ -> ()) (fun w -> render w ctx)))

    /// A content-only template in the application layout even for Turbo-Frame requests: a controller that declares its own
    /// `layout` (`MessagesController`) replaces turbo-rails' frame layout choice.
    let contentInApplicationLayout (http: HttpContext) (status: int) (render: Out -> ViewContext -> unit) : Result<HttpHandler, Error> =
        match findTemplate http Format.Html with
        | Result.Error e -> Result.Error e
        | Ok() ->
            pageInAnyFormat
                http
                status
                (fun ctx -> Render.page 0 (fun w -> Layouts.Application.render w ctx (Layouts.Application.create (Render.html (fun w -> render w ctx)))))

    /// A template rendered with `layout false` (or a turbo stream), no layout, labelled with the template's format.
    let bare (http: HttpContext) (status: int) (template: Format) (body: ViewContext -> RecordedPage) : Result<HttpHandler, Error> =
        match findTemplate http template with
        | Result.Error e -> Result.Error e
        | Ok() ->
            load http
            |> Result.map (fun layout ->
                let html = render layout http body
                recordedAs http status (Formats.mimeType template + "; charset=utf-8") [] html)

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
