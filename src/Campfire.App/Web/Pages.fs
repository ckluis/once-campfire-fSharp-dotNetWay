// plans/design-kit-auth.md section 4, unit 2.9 (Web/Pages.fs): pages and bodies on `HttpContext`.
//
// The layout's data from
// `Current` and the database, the `ViewContext` of this request (flash, URLs, referer), and the handlers that write a
// rendered page: the application layout, or turbo-rails' frame layout for a Turbo-Frame request. A page of cached fragments goes out in
// parts (HtmlOutput), already compressed and kept.
namespace Campfire.App.Web

open System
open System.Text
open System.Threading.Tasks
open Falco
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Caching.Hybrid
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
        let referer = http.Request.Headers.Referer
        let ctx: ViewContext =
            { CurrentUser = layout.CurrentUser
              Account = layout.Account
              FlashNotice = option flash.Notice
              FlashAlert = option flash.Alert
              Platform = layout.Platform
              VapidPublicKey = layout.VapidPublicKey
              AssetPath = Assets.assetPath
              ImportmapTags = ViewContextBuilder.importmapTrusted ()
              StylesheetTags = ViewContextBuilder.stylesheetTrusted ()
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

    /// What a rendered response carries as its body: one buffer, or a page in parts (text parts with cached fragments between).
    type PageBody =
        | Bytes of ReadOnlyMemory<byte>
        | Output of HtmlOutput

    /// The encoder for a request that takes compression, or nothing: the framework's provider (gzip or Brotli at their fastest).
    let private encoderFor (http: HttpContext) : ICompressionProvider voption =
        match http.RequestServices.GetService<IResponseCompressionProvider>() with
        | null -> ValueNone
        | provider ->
            if provider.CheckRequestAcceptsCompression http && provider.ShouldCompressResponse http then
                match provider.GetCompressionProvider http with
                | null -> ValueNone
                | encoder -> ValueSome encoder
            else
                ValueNone

    /// `page` compressed with `encoder`.
    let private compress (encoder: ICompressionProvider) (page: HtmlOutput) : byte[] =
        let plain = System.Buffers.ArrayBufferWriter<byte>(page.Length)
        page.WriteTo plain
        use output = new System.IO.MemoryStream()
        (use stream = encoder.CreateStream output
         stream.Write plain.WrittenSpan)
        output.ToArray()

    /// Writes a rendered response: the status and headers, the length the host will send (known for a buffer and for parts),
    /// and the body, which a HEAD request drops (`Rack::Head`) while keeping its `Content-Length`. Compression is the
    /// framework's (`UseResponseCompression`); a page with cached fragments has its compressed form kept (V7, by the SHA-256
    /// of its parts and fragments) and written already encoded.
    let private send (status: int) (contentType: string) (headers: (string * string) list) (body: PageBody) : HttpHandler =
        fun http ->
            task {
                let response = http.Response
                response.StatusCode <- status
                response.Headers.ContentType <- contentType
                for (name, value) in headers do
                    response.Headers.Append(name, value)
                response.ContentLength <- Nullable(int64 (match body with Bytes bytes -> bytes.Length | Output page -> page.Length))
                if Edge.headFromPost http then response.ContentLength <- Nullable()
                try
                    if not (HttpMethods.IsHead http.Request.Method) then
                        match body with
                        | Bytes bytes -> do! response.Body.WriteAsync bytes
                        // A page with no fragment is one buffer: written whole through the body stream, as every body of one piece is
                        // (`BodyWriter` over the compression middleware's stream copies it once more).
                        | Output page when page.Fragments.Length = 0 && page.Parts.Length = 1 -> do! response.Body.WriteAsync page.Parts[0]
                        | Output page ->
                            let encoder = if page.Fragments.Length = 0 then ValueNone else encoderFor http
                            match encoder with
                            | ValueSome encoder ->
                                let cache = http.RequestServices.GetRequiredService<HybridCache>()
                                let! kept = Fragments.compressedPage cache encoder.EncodingName page (compress encoder)
                                // Already encoded, so the compression middleware leaves it (and its `Vary`) alone.
                                response.Headers.ContentEncoding <- kept.Encoding
                                response.Headers.Append("Vary", "Accept-Encoding")
                                response.ContentLength <- Nullable(int64 kept.Bytes.Length)
                                do! response.Body.WriteAsync kept.Bytes
                            | ValueNone ->
                                page.WriteTo response.BodyWriter
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

    /// A rendered page with an explicit content type (a partial, a turbo stream), as the handler that writes it.
    let renderOutput (http: HttpContext) (status: int) (contentType: string) (page: HtmlOutput) : HttpHandler =
        send status contentType (varyHeaders http) (Output page)

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

    /// A rendered page (its text parts and the cached fragments between them) as a response.
    let private output (http: HttpContext) (status: int) (contentType: string) (extraHeaders: (string * string) list) (page: HtmlOutput) : HttpHandler =
        send status contentType (varyHeaders http @ extraHeaders) (Output page)

    /// A page rendered in the application layout: plus the `Link` preload header `stylesheet_link_tag` adds.
    let private sendPage (http: HttpContext) (status: int) (html: HtmlOutput) : HttpHandler =
        let links = (ViewContextBuilder.stylesheetTags ()).PreloadLinks
        output http status HtmlUtf8 [ "link", Assets.appendPreloadLinks "" links ] html

    /// A page rendered in turbo-rails' frame layout (no stylesheets, so no `Link` header).
    let private sendFrame (http: HttpContext) (status: int) (html: HtmlOutput) : HttpHandler = output http status HtmlUtf8 [] html

    /// The implicit render's template lookup (`default_render`): an action whose only template is `<action>.html.erb` can't
    /// answer a request that doesn't accept HTML, which is `ActionController::UnknownFormat` (406).
    let findTemplate (http: HttpContext) (template: Format) : Result<unit, Error> = Formats.respondTo http [ template ] |> Result.map ignore

    /// A page in the application layout whatever the request (a controller that declares its own `layout`, `MessagesController`),
    /// without the implicit render's template lookup: an explicit `render template:` answers HTML whatever the request's format.
    let applicationPageInAnyFormat (http: HttpContext) (status: int) (view: ViewContext -> Layouts.ViewPage) : Result<HttpHandler, Error> =
        load http |> Result.map (fun layout -> sendPage http status (render layout http (fun ctx -> Layouts.page ctx false (view ctx))))

    /// A page, or for a Turbo-Frame request its `head` and `content` in turbo-rails' frame layout
    /// (`layout -> { "turbo_rails/frame" if turbo_frame_request? }`), without the template lookup.
    let pageInAnyFormat (http: HttpContext) (status: int) (view: ViewContext -> Layouts.ViewPage) : Result<HttpHandler, Error> =
        load http
        |> Result.map (fun layout ->
            if Formats.isTurboFrame http then
                sendFrame http status (render layout http (fun ctx -> Layouts.page ctx true (view ctx)))
            else
                sendPage http status (render layout http (fun ctx -> Layouts.page ctx false (view ctx))))

    /// A Falco page (`Layouts.ViewPage`) in the application layout, or for a Turbo-Frame request its `head` and `content` in
    /// turbo-rails' frame layout, after the implicit render's template lookup.
    let page (http: HttpContext) (status: int) (view: ViewContext -> Layouts.ViewPage) : Result<HttpHandler, Error> =
        match findTemplate http Format.Html with
        | Result.Error e -> Result.Error e
        | Ok() -> pageInAnyFormat http status view

    /// A Falco page in the application layout even for Turbo-Frame requests: a controller that declares its own `layout`
    /// (`MessagesController`) replaces turbo-rails' frame layout choice.
    let pageInApplicationLayout (http: HttpContext) (status: int) (view: ViewContext -> Layouts.ViewPage) : Result<HttpHandler, Error> =
        match findTemplate http Format.Html with
        | Result.Error e -> Result.Error e
        | Ok() -> applicationPageInAnyFormat http status view

    /// A template rendered with `layout false` (or a turbo stream), no layout, labelled with the template's format.
    let bare (http: HttpContext) (status: int) (template: Format) (body: ViewContext -> HtmlOutput) : Result<HttpHandler, Error> =
        match findTemplate http template with
        | Result.Error e -> Result.Error e
        | Ok() ->
            load http
            |> Result.map (fun layout ->
                let html = render layout http body
                output http status (Formats.mimeType template + "; charset=utf-8") [] html)

    // --- AllowBrowser ---------------------------------------------------------------------------------

    /// `render template: "sessions/incompatible_browser"` (200). Rendered from a before-action, so it's HTML whatever the
    /// request format. The layout is the controller's: turbo-rails' frame layout for a Turbo-Frame request, except in
    /// controllers that declare their own layout (`MessagesController` and its `Messages::ByBotsController`).
    let incompatibleBrowser (http: HttpContext) : Result<HttpHandler, Error> =
        let endpoint = endpoint http
        let ownLayout = endpoint.StartsWith("messages#", StringComparison.Ordinal) || endpoint.StartsWith("messages/by_bots#", StringComparison.Ordinal)
        if ownLayout then
            applicationPageInAnyFormat http 200 IncompatibleBrowser.rendered
        else
            pageInAnyFormat http 200 IncompatibleBrowser.rendered
