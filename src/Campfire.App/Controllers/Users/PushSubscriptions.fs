// Port of rust/crates/campfire/src/controllers/users/push_subscriptions.rs
//
// `Users::PushSubscriptionsController` (reference/app/controllers/users/push_subscriptions_controller.rb):
// the signed-in user's Web Push subscriptions.
namespace Campfire.App.Controllers

open System
open System.Linq
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Campfire.Views
open Campfire.App
open Campfire.App.Integrations
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby
open Campfire.App.Web

module PushSubscriptions =
    module IndexPage = Campfire.Views.Templates.Users.PushSubscriptions.Index

    /// `params.require(:push_subscription).permit(:endpoint, :p256dh_key, :auth_key)`: the scalar members given, in the order
    /// permitted.
    let private pushSubscriptionParams (http: HttpContext) : Result<(string * string) list, Error> =
        Params.require http "push_subscription"
        |> Result.map (fun scope ->
            let permitted = Params.permit scope [ "endpoint"; "p256dh_key"; "auth_key" ]
            [ for key in [ "endpoint"; "p256dh_key"; "auth_key" ] do
                  match Map.tryFind key permitted with
                  | Some value -> yield key, value
                  | None -> () ])

    /// `Current.user.push_subscriptions.find_by(push_subscription_params)`: only the given keys are
    /// conditions (none at all finds the user's first subscription); nil is `IS NULL`.
    let private findBy (db: Ef.CampfireDb) (userId: int64) (parameters: (string * string) list) : Ef.PushSubscription option =
        let mutable query = db.PushSubscriptions.Where(fun p -> p.UserId = EF.Parameter userId)
        for (key, v) in parameters do
            query <-
                match key with
                | "endpoint" -> query.Where(fun p -> p.Endpoint = EF.Parameter v)
                | "p256dh_key" -> query.Where(fun p -> p.P256dhKey = EF.Parameter v)
                | "auth_key" -> query.Where(fun p -> p.AuthKey = EF.Parameter v)
                | other -> failwith $"push_subscription key {other} is not permitted"
        Ef.Data.tryFirst query

    /// `RestrictedHTTP::PrivateNetworkGuard.resolve(endpoint_uri.host)`, done ahead of the (synchronous)
    /// validation for the one host it asks about: a first pass records the host, then it's resolved.
    let private resolveEndpoint (time: System.TimeProvider) (subscription: Ef.PushSubscription) : Task<Map<string, string option>> =
        task {
            let mutable asked: string option = None
            PushSubscription.validate
                (fun host ->
                    asked <- Some host
                    None)
                subscription
            |> ignore
            match asked with
            | Some host ->
                let network = Network.system time
                let! resolved = Guard.resolve network.Resolver host
                let ip =
                    match resolved with
                    | Ok ip -> Some(ip.ToString())
                    | Error _ -> None
                return Map.ofList [ host, ip ]
            | None -> return Map.empty
        }

    let private resolver (resolved: Map<string, string option>) : string -> string option =
        fun host -> resolved |> Map.tryFind host |> Option.flatten

    /// `subscription.valid?`
    let private validate (time: System.TimeProvider) (subscription: Ef.PushSubscription) : Task<Errors> =
        task {
            let! resolved = resolveEndpoint time subscription
            return PushSubscription.validate (resolver resolved) subscription
        }

    let index: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                do! Formats.respondTo http [ Format.Html ] |> Result.map ignore
                let! (user: User) = Current.requireUser http
                let! subscriptions = Data.read http (fun db -> PushSubscription.forUser db user.Id)
                let pushSubscriptions = subscriptions |> List.map Accounts.pushSubscription
                return!
                    Pages.page http 200 (fun ctx -> IndexPage.rendered ctx pushSubscriptions)
            }

    let create: Web.Action =
        fun http ->
            taskResult {
                Params.wrap http "push_subscription" ValueNone
                do! Before.chain Before.Default http
                let! (user: User) = Current.requireUser http
                let userId = user.Id
                let! parameters = pushSubscriptionParams http

                let! existing = Data.read http (fun db -> findBy db userId parameters)
                match existing with
                // Existing endpoints must pass current validations
                | Some subscription ->
                    let! errors = validate (Data.app http).Time subscription
                    if Errors.isEmpty errors then
                        let id = subscription.Id
                        let! () =
                            Data.write http (fun tx ->
                                let now = tx.Now()
                                tx.Db.PushSubscriptions
                                    .Where(fun p -> p.Id = EF.Parameter id)
                                    .ExecuteUpdate(fun u -> u.SetProperty((fun p -> p.UpdatedAt), now) |> ignore)
                                |> ignore)
                        return Respond.head http 200
                    else
                        return Respond.head http 422
                | None ->
                    let value key = parameters |> List.tryFind (fun (k, _) -> k = key) |> Option.map snd |> Option.toObj
                    let userAgent =
                        match http.Request.Headers.UserAgent with
                        | values when values.Count = 0 -> null
                        | values -> values.ToString()
                    let subscription = PushSubscription.build userId (value "endpoint") (value "p256dh_key") (value "auth_key") userAgent
                    let! resolved = resolveEndpoint (Data.app http).Time subscription
                    let! result = Data.tryWrite http (fun tx -> PushSubscription.create tx subscription (resolver resolved)) |> Task.map Ok
                    match result with
                    | Ok _ -> return Respond.head http 200
                    | Result.Error(RecordInvalid _) -> return Respond.head http 422
                    | Result.Error error -> return! Result.Error(Internal(Exception(DbError.display error)))
            }

    /// `@push_subscriptions.destroy_by(id: params[:id])`
    let destroy: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (user: User) = Current.requireUser http
                let userId = user.Id
                match (match Params.value http "id" with null -> None | id -> Ruby.integerCast id) with
                | Some id ->
                    let! () =
                        Data.write http (fun tx ->
                            let found =
                                try
                                    Some(PushSubscription.find tx.Db id)
                                with DbException(RecordNotFound _) ->
                                    None
                            match found with
                            | Some subscription when subscription.UserId = userId -> PushSubscription.destroy tx subscription
                            | _ -> ())
                    ()
                | None -> ()
                return! Respond.redirectTo http (Respond.urlFor http (Routes.userPushSubscriptions ()))
            }
