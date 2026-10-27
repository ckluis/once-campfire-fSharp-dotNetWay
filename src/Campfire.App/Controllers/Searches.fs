// Port of rust/crates/campfire/src/controllers/searches.rs
//
// `SearchesController` (reference/app/controllers/searches_controller.rb). `set_messages` runs
// before every action, so a query that FTS5 rejects fails `create` and `clear` too. Web actions (unit 2.11).
namespace Campfire.App.Controllers

open System
open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Searches
open Campfire.App.Web

module SearchesController =
    module IndexPage = Campfire.Views.Templates.Searches.Index

    /// `params[:q]`; anything but a string makes `gsub` raise.
    let private queryParam (http: Microsoft.AspNetCore.Http.HttpContext) : Result<string option, Error> =
        match Params.lookup http "q" with
        | Absent -> Ok None
        | Str q -> Ok(Some q)
        | Structured -> Result.Error(Internal(exn "undefined method 'gsub'"))

    /// `params[:q]&.gsub(/[^[:word:]]/, " ")`
    let query (q: string option) : string option = Campfire.App.Integrations.Search.sanitizeQuery q

    let private isPresent (value: string) : bool = not (value |> Seq.forall Char.IsWhiteSpace)

    /// The `query` `set_messages` searches for: none when it's blank.
    let private searchableQuery (q: string option) : string option = query q |> Option.filter isPresent

    /// `set_messages`: `Current.user.reachable_messages.search(query).last(100)` when there's a query.
    let private setMessages (http: Microsoft.AspNetCore.Http.HttpContext) (q: string option) : Result<Message list, Error> =
        result {
            match searchableQuery q with
            | None -> return []
            | Some query ->
                let! (user: User) = Current.requireUser http
                return! Data.read http (fun db -> Message.searchReachable db user.Id query)
        }

    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! q = queryParam http
                let! (user: User) = Current.requireUser http
                let query = searchableQuery q
                let lastRoom = Pages.lastRoomCookie http
                // `set_messages`, the recent searches and `last_room_visited` in one trip to the reader.
                let! messages, recentSearches, returnToRoomId =
                    Data.read http (fun db ->
                        let messages =
                            match query with
                            | Some query -> Message.searchReachable db user.Id query
                            | None -> []
                        let recent = Search.orderedForUser db user.Id |> List.map (fun search -> search.Query)
                        let returnTo =
                            Campfire.App.Current.lastRoomVisitedIn db user.Id lastRoom
                            |> Option.map (fun room -> room.Id)
                            |> Option.defaultValue 0L
                        messages, recent, returnTo)
                let! messages = Data.presentAsync http (fun presenter -> presenter.Messages messages)
                let index: IndexView =
                    { Query = query
                      Q = q
                      Messages = messages
                      RecentSearches = recentSearches
                      ReturnToRoomId = returnToRoomId }
                return! Pages.page http 200 (fun ctx -> IndexPage.rendered ctx index)
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! q = queryParam http
                let! (_: Message list) = setMessages http q
                let! (user: User) = Current.requireUser http
                let query = query q
                // Current.user.searches.record(query): a nil query violates `query`'s NOT NULL.
                let! recorded =
                    match query with
                    | Some query -> Ok query
                    | None -> Result.Error(Internal(exn "NOT NULL constraint failed: searches.query"))
                let! (_: Search) = Data.write http (fun tx -> Search.record tx user.Id recorded)
                let path =
                    match query with
                    | Some query -> searchPath query
                    | None -> Routes.searches ()
                return! Respond.redirectTo http (Respond.urlFor http path)
            }

    let clear: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! q = queryParam http
                let! (_: Message list) = setMessages http q
                let! (user: User) = Current.requireUser http
                do! Data.write http (fun tx -> Search.destroyAllForUser tx user.Id)
                return! Respond.redirectTo http (Respond.urlFor http (Routes.searches ()))
            }
