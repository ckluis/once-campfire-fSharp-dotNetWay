// plans/design-kit-auth.md section 3 (Web/Services.fs): what Phase 2 registers, and the pipeline.
//
// Unit 2.1 is the seam: the services later units use are resolvable from a request (the kit and the app state, the
// authentication schemes, ASP.NET Core session, the framework's CSRF protection), and nothing runs them yet, so a
// request behaves as before. Units 2.2-2.8 add the cookie scheme, the bot-key scheme, `UseSession`, the forwarded
// headers, compression and the rest.
namespace Campfire.App.Web

open System
open Falco
open Falco.Routing
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.DataProtection
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Microsoft.Extensions.DependencyInjection
open Campfire.App
open Campfire.Kit
open Campfire.RailsCompat

module Services =
    /// Cookie name of ASP.NET Core's session, which holds the flash and `return_to_after_authenticating` (K6): not
    /// Rails' `_campfire_session`, which stays untouched.
    [<Literal>]
    let FlashCookie = "_campfire_flash"

    /// K2: the one registered scheme must not become the default, or every request (assets too) authenticates.
    [<Literal>]
    let SuppressAutoDefaultScheme = "Microsoft.AspNetCore.Authentication.SuppressAutoDefaultScheme"

    /// The raw request path, still percent-encoded and without its query (`PATH_INFO`).
    let rawPath (http: HttpContext) : string =
        let target =
            match http.Features.Get<IHttpRequestFeature>() with
            | null -> http.Request.Path.Value
            | feature -> feature.RawTarget
        match target with
        | null
        | "" -> "/"
        | target ->
            let target = match target.IndexOf '?' with -1 -> target | q -> target.Substring(0, q)
            if target.StartsWith '/' then
                target
            else
                // Absolute form: `http://host/path`.
                match target.IndexOf("://", StringComparison.Ordinal) with
                | -1 -> target
                | at ->
                    match target.IndexOf('/', at + 3) with
                    | -1 -> "/"
                    | slash -> target.Substring slash

    /// Registers the app's services on the host (`Front.serveWithLines`'s `configureServices`, and the tests' hosts).
    let add (kit: Kit) (app: AppState) (services: IServiceCollection) : unit =
        AppContext.SetSwitch(SuppressAutoDefaultScheme, true)
        services.AddSingleton<Kit>(kit) |> ignore
        services.AddSingleton<Secrets>(kit.Secrets) |> ignore
        services.AddSingleton<AppState>(app) |> ignore
        // K6: session state lives in server memory, so its keys are ephemeral too.
        services.AddDataProtection().UseEphemeralDataProtectionProvider() |> ignore
        services.AddDistributedMemoryCache(fun options -> options.SizeLimit <- 64L * 1024L * 1024L) |> ignore
        services.AddSession(fun options ->
            options.Cookie.Name <- FlashCookie
            options.Cookie.HttpOnly <- true
            options.Cookie.SameSite <- SameSiteMode.Lax
            options.Cookie.IsEssential <- true
            options.IdleTimeout <- TimeSpan.FromHours 2.0)
        |> ignore
        // K10: cookies set through `Response.Cookies` (`last_room`, the session's own) are `secure` when the request is HTTPS,
        // never below `Lax`.
        services.Configure<CookiePolicyOptions>(fun (options: CookiePolicyOptions) ->
            options.Secure <- CookieSecurePolicy.SameAsRequest
            options.MinimumSameSitePolicy <- SameSiteMode.Lax)
        |> ignore
        // K1: the `session_token` cookie; K4: the bot key, a second scheme (no policy scheme, no default).
        services
            .AddAuthentication()
            .AddCookie(Schemes.Session, SessionCookie.configure)
            .AddScheme<AuthenticationSchemeOptions, BotKeyAuthenticationHandler>(Schemes.BotKey, ignore)
        |> ignore
        services
            .AddOptions<CookieAuthenticationOptions>(Schemes.Session)
            .Configure<Secrets, TimeProvider>(fun options secrets time ->
                options.TicketDataFormat <- RailsSignedTicketFormat(secrets, time)
                options.TimeProvider <- time
                options.Events.OnValidatePrincipal <- Func<CookieValidatePrincipalContext, System.Threading.Tasks.Task>(SessionCookie.validatePrincipal))
        |> ignore

    /// The app's branch of the host: the kit's pre-routing middleware, public files, `/cable`, then the route table
    /// (`dispatch`) as Falco's not-found handler, so ASP.NET Core's own routing never rewrites a path.
    let pipeline (kit: Kit) (publicFiles: Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task>) (cable: HttpHandler) (dispatch: Campfire.App.Web.Action) (builder: IApplicationBuilder) : unit =
        Adapter.useDeflater kit builder |> ignore
        builder.Use(Adapter.railsMiddleware kit) |> ignore
        builder.Use(publicFiles) |> ignore
        builder.UseCookiePolicy() |> ignore
        builder.UseSession() |> ignore
        builder.UseRouting() |> ignore
        builder.UseFalco [ any Campfire.Cable.Protocol.DefaultMountPath cable ] |> ignore
        builder.UseFalcoNotFound(Campfire.App.Web.Action.run dispatch) |> ignore
