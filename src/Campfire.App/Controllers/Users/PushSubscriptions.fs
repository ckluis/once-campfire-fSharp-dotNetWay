// Port of rust/crates/campfire/src/controllers/users/push_subscriptions.rs
//
// `Users::PushSubscriptionsController` (reference/app/controllers/users/push_subscriptions_controller.rb):
// the signed-in user's Web Push subscriptions.
namespace Campfire.App.Controllers

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Campfire.Views
open Campfire.Kit
open Campfire.App
open Campfire.App.Integrations
open Campfire.App.Presenters
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby

module PushSubscriptions =
    module IndexPage = Campfire.Views.Templates.Users.PushSubscriptions.Index

    let private indexSize = RenderSize()

    /// `params.require(:push_subscription).permit(:endpoint, :p256dh_key, :auth_key)`
    let private pushSubscriptionParams (c: Ctx) : Result<Campfire.Kit.ParamMap, Error> =
        c.Params.Require "push_subscription"
        |> Result.map (fun param -> param.Permit(Params.permitKeys [ "endpoint"; "p256dh_key"; "auth_key" ]))

    /// `Current.user.push_subscriptions.find_by(push_subscription_params)`: only the given keys are
    /// conditions (none at all finds the user's first subscription); nil is `IS NULL`.
    let private findBy (db: Ef.CampfireDb) (userId: int64) (parameters: Campfire.Kit.ParamMap) : Ef.PushSubscription option =
        let mutable query = db.PushSubscriptions.Where(fun p -> p.UserId = EF.Parameter userId)
        for struct (key, param) in parameters.Iter do
            let value: string | null = param.ToS()
            query <-
                match key, value with
                | "endpoint", null -> query.Where(fun p -> p.Endpoint = null)
                | "endpoint", v -> query.Where(fun p -> p.Endpoint = EF.Parameter v)
                | "p256dh_key", null -> query.Where(fun p -> p.P256dhKey = null)
                | "p256dh_key", v -> query.Where(fun p -> p.P256dhKey = EF.Parameter v)
                | "auth_key", null -> query.Where(fun p -> p.AuthKey = null)
                | "auth_key", v -> query.Where(fun p -> p.AuthKey = EF.Parameter v)
                | other, _ -> failwith $"push_subscription key {other} is not permitted"
        Ef.Data.tryFirst query

    /// `RestrictedHTTP::PrivateNetworkGuard.resolve(endpoint_uri.host)`, done ahead of the (synchronous)
    /// validation for the one host it asks about: a first pass records the host, then it's resolved.
    let private resolveEndpoint (subscription: Ef.PushSubscription) : Task<Map<string, string option>> =
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
                let network = Network.system ()
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
    let private validate (subscription: Ef.PushSubscription) : Task<Errors> =
        task {
            let! resolved = resolveEndpoint subscription
            return PushSubscription.validate (resolver resolved) subscription
        }

    let index (c: Ctx) : Task<Result<Response, Error>> =
        act {
            do! Concerns.beforeActions c Before.Default
            do! c.RespondTo [ Format.Html ] |> Result.map ignore
            let! (user: User) = Concerns.requireCurrentUser c
            let! subscriptions = c.Read(fun db -> PushSubscription.forUser db user.Id)
            let pushSubscriptions = subscriptions |> List.map Accounts.pushSubscription
            return!
                Page.framedPage
                    c
                    Status.Ok
                    indexSize
                    (fun w ctx -> IndexPage.render w ctx pushSubscriptions)
                    IndexPage.head
                    (fun w ctx -> IndexPage.content w ctx pushSubscriptions)
        }

    let create (c: Ctx) : Task<Result<Response, Error>> =
        act {
            c.WrapParameters("push_subscription", ValueNone)
            do! Concerns.beforeActions c Before.Default
            let! (user: User) = Concerns.requireCurrentUser c
            let userId = user.Id
            let! parameters = pushSubscriptionParams c

            let! existing = c.Read(fun db -> findBy db userId parameters)
            match existing with
            // Existing endpoints must pass current validations
            | Some subscription ->
                let! errors = validate subscription
                if Errors.isEmpty errors then
                    let id = subscription.Id
                    let! () =
                        c.Write(fun tx ->
                            let now = tx.Now()
                            tx.Db.PushSubscriptions
                                .Where(fun p -> p.Id = EF.Parameter id)
                                .ExecuteUpdate(fun u -> u.SetProperty((fun p -> p.UpdatedAt), now) |> ignore)
                            |> ignore)
                    return c.Head Status.Ok
                else
                    return c.Head Status.UnprocessableEntity
            | None ->
                let value key = Accounts.paramToS parameters key
                let subscription =
                    PushSubscription.build userId (Option.toObj (value "endpoint")) (Option.toObj (value "p256dh_key")) (Option.toObj (value "auth_key")) c.Request.UserAgent
                let! resolved = resolveEndpoint subscription
                let! result = c.TryWrite(fun tx -> PushSubscription.create tx subscription (resolver resolved))
                match result with
                | Ok _ -> return c.Head Status.Ok
                | Error(RecordInvalid _) -> return c.Head Status.UnprocessableEntity
                | Error error -> return! Error(Internal(Exception(DbError.display error)))
        }

    /// `@push_subscriptions.destroy_by(id: params[:id])`
    let destroy (c: Ctx) : Task<Result<Response, Error>> =
        act {
            do! Concerns.beforeActions c Before.Default
            let! (user: User) = Concerns.requireCurrentUser c
            let userId = user.Id
            match (match c.ParamStr "id" with null -> None | id -> Ruby.integerCast id) with
            | Some id ->
                let! () =
                    c.Write(fun tx ->
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
            return! c.RedirectTo(c.UrlFor(Routes.userPushSubscriptions ()))
        }
