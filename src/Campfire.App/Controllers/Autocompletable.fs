// Port of rust/crates/campfire/src/controllers/autocompletable.rs
//
// `Autocompletable::UsersController` (reference/app/controllers/autocompletable/users_controller.rb):
// mention and user-picker suggestions. A Web action (unit 2.11). (`Campfire.Views.Autocompletable` is a module too, hence the name.)
namespace Campfire.App.Controllers

open System
open FsToolkit.ErrorHandling
open Campfire.Views
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Ruby
open Campfire.App.Web

module AutocompletableUsers =
    /// `set_page_and_extract_portion_from find_autocompletable_users.with_attached_avatar.ordered, per_page: 20`
    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! user = Current.requireUser http

                // `params[:room_id].present? ? Current.user.rooms.find(params[:room_id]).users : User.all`
                let! roomId =
                    match Params.lookup http "room_id" with
                    | param when not (Params.isPresent param) -> Ok None
                    | Str id ->
                        match Ruby.integerCast id with
                        | None -> Result.Error NotFound
                        | Some id ->
                            match Data.read http (fun db -> Room.findForUser db user.Id id) with
                            | Result.Error e -> Result.Error e
                            | Ok None -> Result.Error NotFound
                            | Ok(Some room) -> Ok(Some room.Id)
                    | _ -> Result.Error NotFound
                // The rich text editor's mentions prompt filters with `filter`, the autocomplete inputs with `query`
                let query =
                    [ "filter"; "query" ]
                    |> List.map (Params.lookup http)
                    |> List.tryFind Params.isPresent
                    |> Option.bind (fun param ->
                        match param with
                        | Str s -> Some s
                        // A hash or array: `to_s` of it
                        | _ -> Some "")

                let! (users: User list) = Data.read http (fun db -> User.autocompletable db roomId query)
                let page = Pagination.Page.Create(Params.value http "page", int64 users.Length, [ 20L ])
                let secrets = (Data.app http).Secrets
                let users = page.Records users |> List.map (Accounts.mentionUser secrets)

                let! format = Formats.respondTo http [ Format.Html; Format.Json ]
                Pages.applyPageHeaders http page
                if format = Format.Json then
                    let body = Autocompletable.usersIndexJson users (Respond.urlFor http "")
                    return Pages.renderText http 200 "application/json; charset=utf-8" body
                else
                    // `render layout: false`: <lexxy-prompt-item> elements for the mentions prompt
                    let! layout = Pages.load http
                    let html =
                        Pages.render layout http (fun ctx ->
                            HtmlOutput.ofNodes (Campfire.Views.Templates.Autocompletable.Users.Index.render ctx users) [])
                    return Pages.renderOutput http 200 "text/html; charset=utf-8" html
            }
