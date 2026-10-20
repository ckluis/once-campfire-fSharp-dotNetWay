// Port of rust/crates/campfire/src/controllers/first_runs.rs
//
// `FirstRunsController` (reference/app/controllers/first_runs_controller.rb): set up the account and its first
// administrator. Web actions (unit 2.9).
namespace Campfire.App.Controllers

open System.IO
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Views
open Campfire.App.Web

module FirstRuns =
    module ShowPage = Campfire.Views.Templates.FirstRuns.Show

    let private showSize = RenderSize()

    /// `redirect_to root_url if Account.any?`
    let private preventRepeats (http: HttpContext) : Result<unit, Error> =
        match Data.read http (fun db -> Account.count db > 0L) with
        | Result.Error e -> Result.Error e
        | Ok false -> Ok()
        | Ok true ->
            match Respond.redirectTo http (Respond.urlFor http (Routes.root ())) with
            | Ok handler -> Action.halt handler
            | Result.Error e -> Result.Error e

    /// `allow_unauthenticated_access`, `before_action :prevent_repeats`
    let show: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                do! preventRepeats http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                return! Pages.framedPage http 200 showSize (fun w ctx -> ShowPage.render w ctx) ShowPage.head (fun w ctx -> ShowPage.content w ctx)
            }

    /// What `user.avatar = params[:avatar]` gets: an uploaded file is `Create` (spooled to a temp file the caller deletes),
    /// `""` or nil is `Delete`, a key that was not given is `Unchanged`, and any other string is not a file.
    let private avatarAssignment (http: HttpContext) (user: Scope) : Assignment<Upload> * Campfire.Kit.UploadedFile list =
        let file = if http.Request.HasFormContentType then http.Request.Form.Files.GetFile "user[avatar]" else null
        match file with
        | null ->
            match user.Get "avatar" with
            | Absent -> Unchanged, []
            | Str "" -> Delete, []
            | Str _
            | Structured -> Invalid, []
        | file ->
            let path = Campfire.Kit.UploadedFile.NewTempPath()
            use target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)
            use source = file.OpenReadStream()
            source.CopyTo target
            target.Dispose()
            let headers = System.String.Join("\r\n", [ for h in file.Headers -> $"{h.Key}: {h.Value}" ])
            let uploaded = new Campfire.Kit.UploadedFile(file.FileName, file.ContentType, headers, file.Length, path)
            Create { File = uploaded; Filename = uploaded.OriginalFilename; ContentType = Option.ofObj uploaded.ContentType }, [ uploaded ]

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain (Before.allowUnauthenticatedAccess Before.Default) http
                do! preventRepeats http

                // params.require(:user).permit(:name, :avatar, :email_address, :password)
                let! required = Params.require http "user"
                let user = Params.permit required [ "name"; "email_address"; "password" ]
                let name = Map.tryFind "name" user
                let emailAddress = defaultArg (Map.tryFind "email_address" user) ""
                let password = defaultArg (Map.tryFind "password" user) ""
                let avatar, spooled = avatarAssignment http required
                try
                    // users.name is NOT NULL: Rails raises ActiveRecord::NotNullViolation.
                    let! name =
                        match name with
                        | Some name -> Ok name
                        | None -> Result.Error(Internal(exn "NOT NULL constraint failed: users.name"))
                    let app = Data.app http
                    let! avatar = Assignment.stage app avatar |> Task.map (Result.mapError Legacy)
                    let! passwordDigest =
                        task {
                            try
                                let! digest = PasswordDigest.hash password app.Env.BcryptCost
                                return Ok digest
                            with e ->
                                return Result.Error(Internal e)
                        }

                    let! result =
                        Data.tryWrite http (fun tx ->
                            let administrator = FirstRun.create tx name emailAddress passwordDigest
                            administrator, AttachmentWrites.assign tx (Record.user administrator.Id) "avatar" avatar)
                        |> Task.map Ok

                    let root = Respond.urlFor http (Routes.root ())
                    match result with
                    | Ok(administrator, pending) ->
                        AttachmentWrites.analyzeLater app pending
                        let! (_: Session) = Authentication.startNewSessionFor http administrator
                        return! Respond.redirectTo http root
                    // rescue ActiveRecord::RecordNotUnique
                    | Result.Error error when DbError.isRecordNotUnique error -> return! Respond.redirectTo http root
                    | Result.Error error -> return! Result.Error(Internal(System.Exception(DbError.display error)))
                finally
                    for file in spooled do
                        file.Delete()
            }
