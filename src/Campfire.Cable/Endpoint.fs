// Phase 3 (plans/design-cable.md C1, C5, C9): the `/cable` endpoint (`ActionCable::Server::Base#call`). Anything that
// isn't a WebSocket upgrade from an allowed origin gets Rails' 404 "Page not found" before any upgrade.
module Campfire.Cable.Endpoint

open System
open System.Net.WebSockets
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<Literal>]
let MountPath = "/cable"

/// The first protocol in the client's list that Action Cable supports (websocket-driver walks the client's list).
let private negotiate (requested: Collections.Generic.IList<string>) : string | null =
    let mutable picked: string | null = null
    let mutable i = 0
    while isNull picked && i < requested.Count do
        if Array.contains requested[i] Frames.Protocols then picked <- requested[i]
        i <- i + 1
    picked

/// `Connection::Base#respond_to_invalid_request`.
let private pageNotFound (ctx: HttpContext) : Task =
    let body = "Page not found"
    ctx.Response.StatusCode <- StatusCodes.Status404NotFound
    ctx.Response.ContentType <- "text/plain; charset=utf-8"
    ctx.Response.ContentLength <- int64 body.Length
    ctx.Response.WriteAsync body

/// Serves one request to `/cable`: upgrades a WebSocket handshake and runs the connection until the socket closes;
/// anything else is a 404. The connection is authenticated before the upgrade, while the request's headers are good.
let handle (server: CableServer<'U>) (ctx: HttpContext) : Task =
    task {
        if ctx.WebSockets.IsWebSocketRequest && server.AllowRequestOrigin ctx then
            let! user = server.Authenticate ctx
            let accept = WebSocketAcceptContext(SubProtocol = negotiate ctx.WebSockets.WebSocketRequestedProtocols, DangerousEnableCompression = false, KeepAliveInterval = TimeSpan.Zero)
            use! socket = ctx.WebSockets.AcceptWebSocketAsync accept
            let connection = CableConnection<'U>(socket, ctx, ValueOption.ofOption user, server.Core)
            do! connection.RunAsync()
        else
            do! pageNotFound ctx
    }
