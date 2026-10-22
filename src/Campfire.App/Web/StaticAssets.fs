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

    let mutable private loaded: FrozenDictionary<string, string> | null = null

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
            routes <- HashSet<string>((descriptors |> List.map (fun d -> "/" + d.Route.TrimStart '/')), StringComparer.OrdinalIgnoreCase)
            Volatile.Write(&loaded, tableOf descriptors))
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

    /// "/assets/composer.zroil2fb6p.css"; raises `MissingAsset` for a logical path the build does not have.
    let path (logicalPath: string) : string =
        match Volatile.Read(&loaded) with
        | null -> invalidOp "static assets are not mapped"
        | table ->
            match table.TryGetValue logicalPath with
            | true, route -> route
            | _ -> raise (MissingAsset logicalPath)

    /// A file of the web root (error pages, `send_file` of an image asset): "404.html", "assets/default-bot-avatar.svg".
    let webRootFile (env: IWebHostEnvironment) (relativePath: string) : IFileInfo =
        env.WebRootFileProvider.GetFileInfo relativePath
