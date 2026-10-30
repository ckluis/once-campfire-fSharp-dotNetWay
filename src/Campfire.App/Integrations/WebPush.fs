// Port of rust/crates/campfire/src/integrations/web_push.rs
//
// Web Push: `WebPush::Notification` and `WebPush::Pool` (reference/lib/web_push), the gem request they make
// (web-push 3.1.0 with the `WebPush::PersistentRequest` patch in reference/config/initializers/web_push.rb), and
// `Room::MessagePusher#push` (`PushDeliveries.pushMessage`).
//
// Delivery resolves the endpoint on the worker, only for a permitted `https://...:443` push service, through the
// private network guard, and connects to that address with no proxy.
namespace Campfire.App.Integrations

open System
open System.Net
open System.Net.Http
open System.Security.Cryptography
open System.Threading
open System.Threading.Tasks
open Lib.Net.Http.WebPush
open Lib.Net.Http.WebPush.Authentication
open Campfire.Db
open Campfire.Rails
open Campfire.RichText

/// `Push::Subscription#notification(title:, body:, path:)`: built on the enqueuing side, with the badge counted
/// there. The endpoint is resolved only when it's delivered.
type Notification =
    { Title: string
      Body: string
      Path: string
      Badge: int64
      Subscription: Ef.PushSubscription }

/// What `WebPush.payload_send` raised, by the gem's exceptions (the library's `PushServiceClientException` maps through its status).
type DeliveryError =
    /// `WebPush::ExpiredSubscription` (410) and `WebPush::InvalidSubscription` (404): the push service doesn't
    /// know the subscription (any more).
    | SubscriptionGone of kind: string * host: string * status: int
    /// The gem's other `ResponseError`s: 401/403 (`Unauthorized`), 413, 429, 5xx, and any other non-2xx.
    | Response of kind: string * host: string * status: int
    /// `OpenSSL::PKey::EC::Point::Error`: the subscription's key isn't a P-256 point.
    | InvalidSubscriptionKey of string
    /// `ArgumentError`: blank or malformed keys, an oversized payload.
    | Argument of string
    /// Connection failures, TLS failures and timeouts.
    | Transport of TransportError

module DeliveryError =
    let message (error: DeliveryError) : string =
        match error with
        | SubscriptionGone(kind, host, status)
        | Response(kind, host, status) -> $"{kind}: host: {host}, status: {status}"
        | InvalidSubscriptionKey message
        | Argument message -> message
        | Transport error -> TransportError.message error

    /// Whether the subscription can never be delivered to, so the pool destroys it. `WebPush::Pool#deliver` also
    /// destroys it for a 410 and any `OpenSSL::OpenSSLError`, which includes TLS failures and a bad VAPID key;
    /// those say nothing about the subscription, and a 404 does (RFC 8030, section 7.3).
    let invalidatesSubscription (error: DeliveryError) : bool =
        match error with
        | SubscriptionGone _
        | InvalidSubscriptionKey _ -> true
        | _ -> false

    /// The Ruby exception class, for the pool's log line.
    let className (error: DeliveryError) : string =
        match error with
        | SubscriptionGone(kind, _, _)
        | Response(kind, _, _) -> kind
        | InvalidSubscriptionKey _ -> "OpenSSL::PKey::EC::Point::Error"
        | Argument _ -> "ArgumentError"
        | Transport(TransportError.Tls _) -> "OpenSSL::SSL::SSLError"
        | Transport TransportError.OpenTimeout -> "Net::OpenTimeout"
        | Transport TransportError.ReadTimeout -> "Net::ReadTimeout"
        | Transport _ -> "SystemCallError"

    /// The error as an exception, for the places that raise it.
    let toExn (error: DeliveryError) : exn = Exception($"{className error}: {message error}")

/// What a delivery runs on: the factory's "push" client, the resolver for the endpoint's host, and the clock (the JWT's `exp`, deadlines).
type PushEnv =
    { Http: IHttpClientFactory
      Resolver: IResolver
      Time: TimeProvider }

module WebPush =
    /// `WebPush::Request#default_options[:ttl]`: four weeks.
    [<Literal>]
    let private TtlSeconds = 2419200

    /// For a whole delivery, however the push service trickles its reply (the client's pipeline bounds the time to the response head).
    let private deliveryDeadline = TimeSpan.FromSeconds 30.0

    /// `WebPush::Request#expiration`
    let private vapidLifetimeSeconds = 12 * 60 * 60

    /// `Rails.application.routes.url_helpers.account_logo_path`
    [<Literal>]
    let private IconPath = "/account/logo"

    module Notification =
        /// `subscription.notification(**payload)`: the badge is `user.memberships.unread.count`.
        let build (db: Campfire.Db.Ef.CampfireDb) (subscription: Ef.PushSubscription) (payload: PushPayload) : Notification =
            { Title = payload.Title
              Body = payload.Body
              Path = payload.Path
              Badge = PushSubscription.badge db subscription
              Subscription = subscription }

        /// `encoded_message`: `JSON.generate` (plain JSON, no HTML escaping).
        let encodedMessage (notification: Notification) : string =
            Json.generate (
                Value.Object
                    [ "title", Value.String notification.Title
                      "options",
                      Value.Object
                          [ "body", Value.String notification.Body
                            "icon", Value.String IconPath
                            "data", Value.Object [ "path", Value.String notification.Path; "badge", Value.Int notification.Badge ] ] ]
            )

    /// `WebPush::Request#verify_response`, from the library's exception. The status line's reason phrase is not kept, so the gem's
    /// 400 "UnauthorizedRegistration" is a plain `ResponseError` here (logged either way, never invalidating).
    let private ofStatus (status: int) (host: string) : DeliveryError =
        let error kind = Response(kind, host, status)
        match status with
        | 410 -> SubscriptionGone("WebPush::ExpiredSubscription", host, status)
        | 404 -> SubscriptionGone("WebPush::InvalidSubscription", host, status)
        | 401
        | 403 -> error "WebPush::Unauthorized"
        | 413 -> error "WebPush::PayloadTooLarge"
        | 429 -> error "WebPush::TooManyRequests"
        | s when s >= 500 && s <= 599 -> error "WebPush::PushServiceError"
        | _ -> error "WebPush::ResponseError"

    /// What the library raised, by the gem's exception: its status for a push service's refusal, `ArgumentError` or the key error for
    /// what it could not encrypt, and the transport's classification for the rest.
    let private ofException (host: string) (e: exn) : DeliveryError =
        match e with
        | :? PushServiceClientException as refused -> ofStatus (int refused.StatusCode) host
        | :? CryptographicException as key -> InvalidSubscriptionKey key.Message
        | :? ArgumentException
        | :? FormatException
        // "The client P-256 public key or authentication secret is not available": a blank key.
        | :? InvalidOperationException -> Argument e.Message
        | e -> Transport(TransportError.classify e)

    /// `WebPush.payload_send(message:, endpoint:, endpoint_ip:, p256dh:, auth:, vapid:, urgency: "high")` on the "push" client. The library
    /// builds the request, so the pin travels in `Clients.withPin` and the client's handler copies it onto the request.
    let private payloadSend
        (env: PushEnv)
        (vapid: VapidConfig)
        (endpoint: string)
        (endpointIp: IPAddress)
        (subscription: Ef.PushSubscription)
        (message: string)
        : Task<Result<unit, DeliveryError>> =
        task {
            match Uri.TryCreate(endpoint, UriKind.Absolute) with
            | true, uri when not (isNull uri) ->
                let host = (nonNull uri).Host
                use deadline = new CancellationTokenSource(deliveryDeadline, env.Time)
                try
                    let target = Lib.Net.Http.WebPush.PushSubscription()
                    target.Endpoint <- endpoint
                    target.SetKey(PushEncryptionKeyName.P256DH, defaultArg (Option.ofObj subscription.P256dhKey) "")
                    target.SetKey(PushEncryptionKeyName.Auth, defaultArg (Option.ofObj subscription.AuthKey) "")
                    let push = Lib.Net.Http.WebPush.PushMessage(message)
                    push.Urgency <- PushMessageUrgency.High
                    push.TimeToLive <- Nullable TtlSeconds
                    use authentication = new VapidAuthentication(vapid.PublicKey, vapid.PrivateKey)
                    authentication.Subject <- vapid.Subject
                    authentication.Expiration <- vapidLifetimeSeconds
                    // `AutoRetryAfter` off: a 429 is raised, never sent again (Rails retries nothing).
                    let client = PushServiceClient(env.Http.CreateClient Clients.Push, AutoRetryAfter = false)
                    do!
                        Clients.withPin endpointIp (fun () ->
                            client.RequestPushMessageDeliveryAsync(target, push, authentication, VapidAuthenticationScheme.Vapid, deadline.Token))
                    return Ok()
                with e ->
                    return Error(if deadline.IsCancellationRequested then Transport TransportError.ReadTimeout else ofException host e)
            | _ -> return Error(Argument("bad URI(is not URI?): " + Json.generate (Value.String endpoint)))
        }

    /// `Push::Subscription#resolved_endpoint_ip`: a lookup that outlasts a delivery's deadline is no address.
    let private resolvedEndpointIp (env: PushEnv) (subscription: Ef.PushSubscription) : Task<IPAddress option> =
        task {
            match PushSubscription.resolvedEndpointIp Some subscription with
            | None -> return None
            | Some host ->
                use deadline = new CancellationTokenSource(deliveryDeadline, env.Time)
                try
                    match! Guard.resolve env.Resolver host deadline.Token with
                    | Ok ip -> return Some ip
                    | Error _ -> return None
                with :? OperationCanceledException when deadline.IsCancellationRequested ->
                    return None
        }

    /// `WebPush::Notification#deliver`: nothing happens when the endpoint isn't a permitted push service or doesn't
    /// resolve to a public address (`Ok false`); otherwise the push service accepted it (`Ok true`).
    let deliver (env: PushEnv) (vapid: VapidConfig) (notification: Notification) : Task<Result<bool, DeliveryError>> =
        task {
            match! resolvedEndpointIp env notification.Subscription with
            | None -> return Ok false
            | Some endpointIp ->
                let endpoint = defaultArg (Option.ofObj notification.Subscription.Endpoint) ""
                let! sent = payloadSend env vapid endpoint endpointIp notification.Subscription (Notification.encodedMessage notification)
                return sent |> Result.map (fun () -> true)
        }

    /// `Users::PushSubscriptions::TestNotificationsController#create`: a "Campfire Test" notification with a random
    /// body, delivered inline. `path` is `user_push_subscriptions_url` (a full URL); `badge` is the subscriber's
    /// unread count. Errors propagate, as in Rails.
    let deliverTestNotification
        (env: PushEnv)
        (vapid: VapidConfig)
        (subscription: Ef.PushSubscription)
        (badge: int64)
        (path: string)
        : Task<Result<unit, DeliveryError>> =
        task {
            let notification =
                { Title = "Campfire Test"
                  Body = Guid.NewGuid().ToString()
                  Path = path
                  Badge = badge
                  Subscription = subscription }
            let! delivered = deliver env vapid notification
            return delivered |> Result.map ignore
        }
