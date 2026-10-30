// The endpoints bench/front measures, behind the app's own front server (Web.Host on Kestrel, Web.OutputCaching, the
// app's response compression). bench/front/rust/src/main.rs is the same on the Rust front server.
//
//   FrontBench HTTP_PORT TARGET_PORT
//
//   /hello      a tiny page
//   /page       a 100 KB page: identity, or gzip per request with Accept-Encoding (no front cache holds it)
//   /users/1/avatar  an OutputCache hit (the app's avatar family): 20 KB, identity, gzip or zstd per Accept-Encoding
//   POST /rooms/1/messages  a form post answered with a redirect
module FrontBench

open System
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open Campfire.App.Web

let private page =
    let one = "<div class=\"message\" data-message-id=\"42\"><p>Hello there, see you at 10:30</p></div>\n"
    Encoding.UTF8.GetBytes(String.replicate 1200 one)

let private asset = Encoding.UTF8.GetBytes(String.replicate 800 "body { color: #123456; }\n")

let private hello = Encoding.UTF8.GetBytes "hello"

let private send (contentType: string) (body: byte[]) (http: HttpContext) : Task =
    http.Response.ContentType <- contentType
    http.Response.ContentLength <- int64 body.Length
    http.Response.Body.WriteAsync(ReadOnlyMemory<byte> body).AsTask()

let private endpoints (http: HttpContext) : Task =
    match http.Request.Method, http.Request.Path.Value with
    | "GET", "/hello" -> send "text/html; charset=utf-8" hello http
    | "GET", "/page" -> send "text/html; charset=utf-8" page http
    | "GET", "/users/1/avatar" ->
        http.Response.Headers.CacheControl <- Microsoft.Extensions.Primitives.StringValues "public, max-age=1800"
        send "text/css" asset http
    | "POST", "/rooms/1/messages" ->
        task {
            let! form = http.Request.ReadFormAsync()
            let length = form["message[body]"].ToString().Length
            http.Response.StatusCode <- 302
            http.Response.Headers.Location <- Microsoft.Extensions.Primitives.StringValues("/rooms/1?m=" + string length)
            http.Response.ContentLength <- 0L
        }
        :> Task
    | _ ->
        http.Response.StatusCode <- 404
        Task.CompletedTask

[<EntryPoint>]
let main argv =
    let http = if argv.Length > 0 then argv[0] else "3001"
    let target = if argv.Length > 1 then argv[1] else "3101"
    let settings =
        FrontSettings.fromLookup (fun name ->
            match name with
            | "HTTP_PORT" -> http
            | "TARGET_PORT" -> target
            | "LOG_REQUESTS" -> "false"
            | _ -> null)
    let configureServices (services: IServiceCollection) =
        services.AddResponseCompression(Compression.configure) |> ignore
        services.AddOutputCache(OutputCaching.configure settings.CacheSize settings.MaxCacheItemSize) |> ignore
    let pipeline (app: IApplicationBuilder) =
        app.UseOutputCache().UseResponseCompression().Run(RequestDelegate endpoints)
    let app = Host.build settings NullLoggerFactory.Instance configureServices pipeline
    app.RunAsync().GetAwaiter().GetResult()
    0
