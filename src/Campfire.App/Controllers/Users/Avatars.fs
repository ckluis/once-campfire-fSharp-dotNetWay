// Port of rust/crates/campfire/src/controllers/users/avatars.rs
//
// `Users::AvatarsController` (reference/app/controllers/users/avatars_controller.rb): a user's
// avatar by its signed avatar token (Web actions, unit 2.10): the uploaded image's `:square` variant, the default bot
// avatar, or an SVG of their initials.
namespace Campfire.App.Controllers

open System
open System.IO
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.Views
open Campfire.Assets
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Storage
open Campfire.App.Web

module Avatars =
    /// `ActionView::Digestor.digest(name: "users/avatars/show", ...)`: the SHA256 (truncated) of
    /// `show.svg.erb`'s source plus "-" (it renders nothing else). `EtagWithTemplateDigest` adds it
    /// whenever the template can be found for the request's formats.
    [<Literal>]
    let private TemplateDigest = "d500db55e2a67222018ef0156839c3c9"

    /// `expires_in 30.minutes, public: true, stale_while_revalidate: 1.week`
    [<Literal>]
    let private MaxAge = 1800UL

    [<Literal>]
    let private StaleWhileRevalidate = 604_800UL

    /// `User.from_avatar_token(params[:user_id])`: a bad signature is `head :not_found`
    /// (`rescue_from ActiveSupport::MessageVerifier::InvalidSignature`); a valid one for a missing
    /// user is `ActiveRecord::RecordNotFound`.
    let private fromAvatarToken (http: HttpContext) : Result<User, Web.Error> =
        let token = match Params.value http "user_id" with null -> "" | token -> token
        match Accounts.userIdFromAvatarToken (Data.app http).Secrets token (Data.now http) with
        | None -> Action.halt (Respond.head http 404)
        | Some userId ->
            match Data.read http (fun db -> User.findById db userId) with
            | Result.Error e -> Result.Error e
            | Ok(Some user) -> Ok user
            | Ok None -> Result.Error NotFound

    /// Whether `lookup_context.find_all("show", ["users/avatars", ...])` finds `show.svg.erb` for the
    /// request's formats: `*/*` or svg, or no registered format at all (`Accept: image/*` parses to
    /// none, and an empty `formats=` falls back to every format).
    let private templateFound (http: HttpContext) : bool =
        match Formats.requested http with
        | Result.Error _ -> false
        | Ok formats -> formats.IsEmpty || formats |> List.exists (fun format -> format = Any || format = Svg)

    /// `avatar.variant(:square).processed if avatar.variable?` (`resize_to_limit: [512, 512], format: :webp`).
    let private avatarVariant (http: HttpContext) (user: User) : Task<Result<Campfire.Storage.Blob option, Web.Error>> =
        AttachmentWrites.processedVariant (Data.app http) (Record.user user.Id) "avatar" (Variation.resizeToLimit 512L 512L (Some "webp"))

    let private assetDirectory = lazy (Directory.CreateTempSubdirectory "campfire-assets-")

    let private assetLock = obj ()

    /// A file under `app/assets/images` (embedded by `Campfire.Assets`) on disk, for `send_file`: it's
    /// written once per process to a private directory under its own name, so the response carries
    /// the same filename and, like Rails' file bodies, gets no `Rack::ETag` digest. The lock makes the
    /// first write race-free: concurrent first requests would otherwise see a half-written file.
    let assetFile (logicalPath: string) : Result<string, Web.Error> =
        lock assetLock (fun () ->
            let path = Path.Combine(assetDirectory.Value.FullName, logicalPath)
            if File.Exists path then
                Ok path
            else
                let url = Assets.assetPath logicalPath
                match Assets.serve (Serve.StaticRequest.create "GET" url) with
                | None -> Result.Error(Internal(exn $"missing asset {logicalPath}"))
                | Some response ->
                    let parent = Path.GetDirectoryName path |> nonNull
                    Directory.CreateDirectory parent |> ignore
                    let partial = Path.Combine(parent, $".{Guid.NewGuid():N}.partial")
                    File.WriteAllBytes(partial, response.Body.ToArray())
                    File.Move(partial, path, true)
                    Ok path)

    /// `send_file Rails.root.join("app/assets/images/default-bot-avatar.svg"), content_type: "image/svg+xml", disposition: :inline`
    let private renderDefaultBot () : Result<Falco.HttpHandler, Web.Error> =
        match assetFile "default-bot-avatar.svg" with
        | Ok path -> Respond.sendFile path (SendOptions.Inline "image/svg+xml")
        | Result.Error e -> Result.Error e

    /// `render formats: :svg` (`users/avatars/show.svg.erb`).
    let private renderInitials (http: HttpContext) (user: User) : Falco.HttpHandler =
        let svg = Render.plain (fun w -> Campfire.Views.Templates.Users.Avatars.Show.render w user.Id (User.initials user))
        // `Vary: Accept` like any render when the format came from a non-browser `Accept` (e.g.
        // `image/*`); a browser's image `Accept` ends in `*/*`, so it usually doesn't apply.
        Pages.renderAs http 200 "image/svg+xml; charset=utf-8" (ReadOnlyMemory<byte> svg)

    let show: Web.Action =
        fun http ->
            taskResult {
                Native.useLiveResponse http // `include ActiveStorage::Streaming`
                do! Before.chain Before.Default http
                let! (user: User) = fromAvatarToken http

                let freshness =
                    { Freshness.Default with
                        Etag = FragmentCache.cacheKeyWithVersion "users" user.Id (DateTimeOffset(user.UpdatedAt))
                        Template = if templateFound http then TemplateDigest else null }
                match Respond.freshWhen http freshness with
                | ValueSome notModified -> return notModified
                | ValueNone ->
                    Respond.expiresIn http MaxAge { ExpiresIn.Default with Public = true; StaleWhileRevalidate = ValueSome StaleWhileRevalidate }
                    let! (variant: Campfire.Storage.Blob option) = avatarVariant http user
                    match variant with
                    | Some variant ->
                        let path = DiskService.pathFor (Data.app http).Storage.Service variant.Key
                        return! Respond.sendFile path (SendOptions.Inline "image/webp")
                    | None -> if User.isBot user then return! renderDefaultBot () else return renderInitials http user
            }

    /// `Current.user.avatar.destroy`, then back to the profile.
    let destroy: Web.Action =
        fun http ->
            taskResult {
                Native.useLiveResponse http // `include ActiveStorage::Streaming`
                do! Before.chain Before.Default http
                let! (user: User) = Current.requireUser http
                let! (_: bool) = Data.write http (fun tx -> AttachmentWrites.destroy tx (Record.user user.Id) "avatar")
                return! Respond.redirectTo http (Respond.urlFor http (Campfire.Routes.Routes.userProfile ()))
            }
