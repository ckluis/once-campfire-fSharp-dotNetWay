// Port of rust/crates/campfire/src/controllers/users/push_subscriptions/test_notifications.rs
//
// `Users::PushSubscriptions::TestNotificationsController`
// (reference/app/controllers/users/push_subscriptions/test_notifications_controller.rb). A Web action (unit 2.10).
namespace Campfire.App.Controllers

open FsToolkit.ErrorHandling
open Campfire.App
open Campfire.App.Integrations
open Campfire.Db
open Campfire.Routes
open Campfire.Ruby
open Campfire.App.Web

module TestNotifications =
    /// `@push_subscription.notification(title: "Campfire Test", body: Random.uuid, path: user_push_subscriptions_url).deliver`
    let create: Web.Action =
        fun http ->
            taskResult {
                do! Before.chain Before.Default http
                let! (user: User) = Current.requireUser http
                let userId = user.Id
                let! id =
                    match (match Params.value http "push_subscription_id" with null -> None | id -> Ruby.integerCast id) with
                    | Some id -> Ok id
                    | None -> Result.Error NotFound
                let! subscription =
                    Data.read http (fun db ->
                        // `Current.user.push_subscriptions.find(params[:push_subscription_id])`
                        let subscription = PushSubscription.find db id
                        if subscription.UserId <> userId then Err.fail (RecordNotFound "Push::Subscription")
                        subscription)
                let! badge = Data.read http (fun db -> Membership.unreadCount db userId)

                let location = Respond.urlFor http (Routes.userPushSubscriptions ())
                let! pool =
                    match (Data.app http).WebPush with
                    | Some pool -> Ok pool
                    | None -> Result.Error(Internal(exn "Web Push is off (no valid VAPID keys)"))
                let! delivered = WebPush.deliverTestNotification (Network.system (Data.app http).Time) pool.Vapid subscription badge location |> Task.map Ok
                match delivered with
                | Result.Error e -> return! Result.Error(Internal(DeliveryError.toExn e))
                | Ok() -> return! Respond.redirectTo http location
            }
