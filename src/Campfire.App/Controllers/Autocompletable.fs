// Port of rust/crates/campfire/src/controllers/autocompletable.rs
//
// `Autocompletable::UsersController` (reference/app/controllers/autocompletable/users_controller.rb):
// mention and user-picker suggestions. (`Campfire.Views.Autocompletable` is a module too, hence the name.)
namespace Campfire.App.Controllers

open System
open System.Threading.Tasks
open Campfire.Views
open Campfire.Kit
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Ruby

module AutocompletableUsers =
    /// `set_page_and_extract_portion_from find_autocompletable_users.with_attached_avatar.ordered, per_page: 20`
    let index (c: Ctx) : Task<Result<Response, Error>> =
        act {
            do! Concerns.beforeActions c Before.Default
            let! user = Concerns.requireCurrentUser c

            // `params[:room_id].present? ? Current.user.rooms.find(params[:room_id]).users : User.all`
            let! roomId =
                task {
                    match c.Params.Get "room_id" |> ValueOption.filter (fun param -> param.IsPresent) with
                    | ValueNone -> return Ok None
                    | ValueSome param ->
                        match (match param.AsStr with null -> None | id -> Ruby.integerCast id) with
                        | None -> return Error NotFound
                        | Some id ->
                            match! c.Read(fun db -> Room.findForUser db user.Id id) with
                            | Error e -> return Error e
                            | Ok None -> return Error NotFound
                            | Ok(Some room) -> return Ok(Some room.Id)
                }
            // The rich text editor's mentions prompt filters with `filter`, the autocomplete inputs with `query`
            let query =
                [ c.Params.Get "filter"; c.Params.Get "query" ]
                |> List.choose (fun param ->
                    match param with
                    | ValueSome param when param.IsPresent -> Some param
                    | _ -> None)
                |> List.tryHead
                |> Option.bind (fun param -> Option.ofObj (param.ToS()))

            let! (users: User list) = c.Read(fun db -> User.autocompletable db roomId query)
            let page = Pagination.Page.Create(c.ParamStr "page", int64 users.Length, [ 20L ])
            let secrets = c.App.Secrets
            let users = page.Records users |> List.map (Accounts.mentionUser secrets)

            let! format = c.RespondTo [ Format.Html; Format.Json ]
            page.ApplyHeaders c
            if format = Format.Json then
                let body = Autocompletable.usersIndexJson users (c.UrlFor "")
                return c.RenderAs(Status.Ok, "application/json; charset=utf-8", body)
            else
                // `render layout: false`: <lexxy-prompt-item> elements for the mentions prompt
                let! layout = Layout.load c
                let html =
                    Layout.render layout c (fun ctx ->
                        Render.page 0 (fun w -> Campfire.Views.Templates.Autocompletable.Users.Index.render w ctx users))
                return c.Render(Status.Ok, Format.Html, ReadOnlyMemory<byte> html.Text)
        }
