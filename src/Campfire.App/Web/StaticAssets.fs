// plans/design-assets.md section 3, unit 6.3 (Web/StaticAssets.fs): assets on `MapStaticAssets`.
//
// The host options (A8), the asset routing pass at `ActionDispatch::Static`'s place (A7), the process-wide table of logical path to
// fingerprinted route read from the endpoints' descriptors (A4), and the startup validation that every descriptor's file is in the
// host's web root (a host that cannot see it would serve every asset as 200 with no body).
namespace Campfire.App.Web

open System
open System.Collections.Frozen
open System.Collections.Generic
open System.IO
open System.Linq
open System.Threading
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.StaticAssets
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders

/// `Propshaft::MissingAssetError`: `asset_path` of a logical path the build does not have.
exception MissingAsset of logicalPath: string

module StaticAssets =
    /// The endpoint manifest the build writes beside the assembly.
    let ManifestPath: string =
        Path.Combine(AppContext.BaseDirectory, "campfire.staticwebassets.endpoints.json")

    /// The host's creation options: the web root and the application name can only be set at creation (A8). The content root is
    /// the working directory, as before.
    let hostOptions (args: string[]) : WebApplicationOptions =
        WebApplicationOptions(
            Args = args,
            ApplicationName = "campfire",
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        )

    /// What makes a build output (tests, `dotnet run`) serve its static web assets; a no-op in a publish.
    let configureHost (builder: WebApplicationBuilder) : unit = builder.WebHost.UseStaticWebAssets() |> ignore

    /// What one load of the manifest makes: the table and the three head strings (A5, A6), all computed once.
    type private Loaded =
        { Routes: FrozenDictionary<string, string>
          /// Every route an asset endpoint owns (precompressed variants and public files too), matched without regard to case as routing does.
          AssetRoutes: HashSet<string>
          Stylesheets: string
          Preload: string
          Importmap: string }

    let mutable private loaded: Loaded | null = null

    /// The pattern of the catch-all route a build output (not a publish) adds for files that exist in the web root.
    let private devFallback = "{**path:file}"

    let private assetsPrefix = "assets/"

    let private property (descriptor: StaticAssetDescriptor) (name: string) : string | null =
        descriptor.Properties
        |> Seq.tryFind (fun p -> p.Name = name)
        |> Option.map (fun p -> p.Value)
        |> Option.toObj

    let private isPlain (descriptor: StaticAssetDescriptor) : bool =
        descriptor.Selectors.Count = 0
        && not (descriptor.ResponseHeaders |> Seq.exists (fun h -> h.Name.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase)))

    /// The asset endpoints' descriptors.
    let private descriptorsOf (endpoints: IEndpointRouteBuilder) : StaticAssetDescriptor list =
        [ for source in endpoints.DataSources do
              for endpoint in source.Endpoints do
                  match endpoint.Metadata.GetMetadata<StaticAssetDescriptor>() with
                  | null -> ()
                  | descriptor -> descriptor ]

    /// Logical path to route for every fingerprinted, uncompressed, unselected descriptor under `assets/`.
    let private tableOf (descriptors: StaticAssetDescriptor list) : FrozenDictionary<string, string> =
        [ for d in descriptors do
              if isPlain d && not (isNull (property d "fingerprint")) then
                  match property d "label" with
                  | null -> ()
                  | label when label.StartsWith(assetsPrefix, StringComparison.Ordinal) ->
                      label.Substring assetsPrefix.Length, "/" + d.Route.TrimStart '/'
                  | _ -> () ]
        |> dict
        |> fun d -> d.ToFrozenDictionary()

    /// Rails' MAX_HEADER_SIZE for the preload `Link` header (ActionView::Helpers::AssetTagHelper).
    let private maxLinkHeaderSize = 1_000

    /// `stylesheet_link_tag :all, "data-turbo-track": "reload"`: every `.css` logical path, sorted ordinally (Propshaft's
    /// `all_stylesheets_paths`), and the preload `Link` header Rails adds while rendering it (`send_preload_links_header`: the first
    /// link whose bytes with the header's so far pass 1,000 ends it, the joining comma not counted).
    let private stylesheets (routes: FrozenDictionary<string, string>) : string * string =
        let hrefs =
            routes.Keys |> Seq.filter (fun l -> l.EndsWith(".css", StringComparison.Ordinal)) |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b))
            |> Seq.map (fun l -> routes[l]) |> List.ofSeq
        let tags =
            hrefs |> List.map (fun href -> $"<link rel=\"stylesheet\" href=\"{Campfire.Rails.Ruby.Erb.htmlEscape href}\" data-turbo-track=\"reload\" />")
        let header = System.Text.StringBuilder()
        let mutable size = 0
        let mutable full = false
        for href in hrefs do
            let link = $"<{href}>; rel=preload; as=style; nopush"
            let linkSize = System.Text.Encoding.UTF8.GetByteCount link
            if not full && size + linkSize > maxLinkHeaderSize then full <- true
            if not full then
                if size > 0 then
                    header.Append ',' |> ignore
                    size <- size + 1
                header.Append link |> ignore
                size <- size + linkSize
        String.concat "\n" tags, header.ToString()

    /// config/importmap.rb, the App's embedded resource (reference/config/importmap.rb).
    let private importmapRb () : string =
        use stream = typeof<Pin>.Assembly.GetManifestResourceStream "importmap.rb" |> nonNull
        use reader = new StreamReader(stream)
        reader.ReadToEnd()

    let private routeSet (descriptors: StaticAssetDescriptor list) : HashSet<string> =
        HashSet<string>((descriptors |> List.map (fun d -> "/" + d.Route.TrimStart '/')), StringComparer.OrdinalIgnoreCase)

    let private load (descriptors: StaticAssetDescriptor list) : Loaded =
        let routes = tableOf descriptors
        let stylesheetTags, preload = stylesheets routes
        let pins = Importmap.pins (importmapRb ()) routes.Keys
        { Routes = routes
          AssetRoutes = routeSet descriptors
          Stylesheets = stylesheetTags
          Preload = preload
          Importmap = Importmap.tags pins (fun l -> match routes.TryGetValue l with | true, r -> ValueSome r | _ -> ValueNone) }

    /// Every descriptor's file must be in the web root, else the host serves empty bodies (A4).
    let private validate (env: IWebHostEnvironment) (descriptors: StaticAssetDescriptor list) : unit =
        let missing =
            descriptors
            |> List.filter (fun d -> not (env.WebRootFileProvider.GetFileInfo(d.AssetPath).Exists))
        if not missing.IsEmpty then
            let first = missing |> List.truncate 3 |> List.map (fun d -> d.AssetPath) |> String.concat ", "
            invalidOp
                $"static assets: {missing.Length} of {descriptors.Length} asset files are not in the web root '{env.WebRootPath}' (e.g. {first}); \
the host must be created with StaticAssets.hostOptions and StaticAssets.configureHost (UseStaticWebAssets), under the application name 'campfire'"

    /// The asset routing pass at `ActionDispatch::Static`'s place (A7): `UseRouting`, the marker for the request headers Rails'
    /// `Static` ran ahead of, `UseEndpoints` with `MapStaticAssets`. Loads and validates the table the first time (and again
    /// per host, to the same value).
    let map (builder: IApplicationBuilder) : unit =
        let env = builder.ApplicationServices.GetRequiredService<IWebHostEnvironment>()
        // Every route the asset endpoints own; filled by `UseEndpoints` below, before the first request.
        let mutable routes: HashSet<string> = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        builder.UseRouting() |> ignore
        builder.Use(
            Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task>(fun http next ->
                match http.GetEndpoint() with
                | null -> ()
                | endpoint ->
                    match endpoint.Metadata.GetMetadata<StaticAssetDescriptor>() with
                    | null ->
                        // A build output (not a publish) adds a catch-all fallback for existing files: it matches every path and
                        // answers 405 or 404 for the ones that are not assets. Only a method mismatch on an asset's own route
                        // (the 405 of class B) may end the request here; anything else belongs to the app.
                        if not (routes.Contains(http.Request.Path.Value |> Option.ofObj |> Option.defaultValue "")) then
                            http.SetEndpoint null
                    | _ -> (Edge.state http).SkipRequestHeaders <- true
                next.Invoke http)
        )
        |> ignore
        builder.UseEndpoints(fun endpoints ->
            endpoints.MapStaticAssets ManifestPath |> ignore
            let descriptors = descriptorsOf endpoints
            validate env descriptors
            let state = load descriptors
            routes <- state.AssetRoutes
            Volatile.Write(&loaded, state))
        |> ignore

    /// For the app's own routing pass (after its `UseRouting`): both passes share one set of endpoints, so the asset endpoints and, in
    /// a build output, the catch-all fallback for existing files are candidates there too. Only the app's own routes (`/cable`) may
    /// run; a request that matched anything else goes on to the route table as it does in a publish.
    let appEndpointsOnly (builder: IApplicationBuilder) : unit =
        builder.Use(
            Func<HttpContext, RequestDelegate, System.Threading.Tasks.Task>(fun http next ->
                match http.GetEndpoint() with
                | null -> ()
                | :? RouteEndpoint as route when route.RoutePattern.RawText <> devFallback && isNull (route.Metadata.GetMetadata<StaticAssetDescriptor>()) -> ()
                | _ -> http.SetEndpoint null
                next.Invoke http)
        )
        |> ignore

    /// Whether the path is one of the asset endpoints' routes (a fingerprinted asset or a public file): a lookup in the loaded table.
    /// False before `map` has run, when no route is known yet.
    let isAssetRoute (path: PathString) : bool =
        match Volatile.Read(&loaded), path.Value with
        | null, _ | _, null -> false
        | state, value -> state.AssetRoutes.Contains value

    let private current () : Loaded =
        match Volatile.Read(&loaded) with
        | null -> invalidOp "static assets are not mapped"
        | state -> state

    /// "/assets/composer.zroil2fb6p.css"; raises `MissingAsset` for a logical path the build does not have.
    let path (logicalPath: string) : string =
        match (current ()).Routes.TryGetValue logicalPath with
        | true, route -> route
        | _ -> raise (MissingAsset logicalPath)

    /// Every logical path in the table.
    let logicalPaths () : string list = (current ()).Routes.Keys |> List.ofSeq

    /// `stylesheet_link_tag :all, "data-turbo-track": "reload"`, made once at load (A5).
    let stylesheetTags () : string = (current ()).Stylesheets

    /// The preload `Link` header value, made once at load (A5).
    let preloadLinks () : string = (current ()).Preload

    /// `javascript_importmap_tags`, made once at load (A6).
    let importmapTags () : string = (current ()).Importmap

    /// A file of the web root (error pages, `send_file` of an image asset): "404.html", "assets/default-bot-avatar.svg".
    let webRootFile (env: IWebHostEnvironment) (relativePath: string) : IFileInfo =
        env.WebRootFileProvider.GetFileInfo relativePath
