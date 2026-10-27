// Port of rust/crates/campfire/src/integrations/web_push/tests.rs, on Lib.Net.Http.WebPush (plans/design-jobs.md J7; WP1-WP8)
module Campfire.App.Tests.WebPushTests

open System
open System.Collections.Generic
open System.Net
open System.Security.Cryptography
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Campfire.App
open Campfire.App.Integrations
open Campfire.App.Tests.IntegrationsSupport
open Campfire.App.Tests.Support
open Campfire.Db
open Campfire.Tests

/// `DnsTestHelper::WEB_PUSH_PUBLIC_TEST_IP`
[<Literal>]
let private PublicIp = "142.250.185.206"

/// parity/.env.reference
[<Literal>]
let private VapidPublicKey = "BEYXTBB5_jNhNzXDmx5KEU55Vbbd-u--Lk9rM5OFQvUkPIBwZJ9QzAq0zdEzFw6yTV8cTriz_qYBVicY02_VxTQ="

[<Literal>]
let private VapidPrivateKey = "qfXLHghuG1rSHZUVo9SscNRI-0EIHRbIrfeGCqbAwak="

/// `WebPush::Notification#vapid_identification`, which the gem's vectors were made with.
[<Literal>]
let private ReferenceSubject = "mailto:support@37signals.com"

let private decode64 (s: string) : byte[] =
    match WebPushEncoding.decode64 s with
    | Ok bytes -> bytes
    | Error error -> failwith (EncryptionError.message error)

let private encode64 (bytes: byte[]) : string = WebPushEncoding.encode64Nopad bytes

let private expected () : JsonElement =
    let text = IO.File.ReadAllText(IO.Path.Combine(AppContext.BaseDirectory, "testdata", "web_push_expected.json"))
    (JsonDocument.Parse text).RootElement.Clone()

let private vapid () : VapidConfig =
    match VapidConfig.Create(ReferenceSubject, VapidPublicKey, VapidPrivateKey) with
    | Ok vapid -> vapid
    | Error error -> failwith (VapidError.message error)

/// A `PushDeliveries` started as the host starts it, and stopped as the host stops it (deliveries get a second to finish,
/// then the invalidations drain).
let private startPool (net: PushEnv) (handler: int64 -> Task<Result<unit, string>>) : Task<PushDeliveries> =
    task {
        let pool = new PushDeliveries(net, vapid (), handler, NullLogger.Instance)
        do! (pool :> IHostedService).StartAsync CancellationToken.None
        return pool
    }

let private stopPool (pool: PushDeliveries) : Task =
    task {
        use grace = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
        do! (pool :> IHostedService).StopAsync grace.Token
        pool.Dispose()
    }

let private p256 = ECCurve.NamedCurves.nistP256

/// A P-256 key pair from its scalar and its public point (SEC1 uncompressed).
let private keyOf (scalar: byte[]) (publicPoint: byte[]) : ECDiffieHellman =
    P256.keyOf scalar (ECPoint(X = publicPoint[1..32], Y = publicPoint[33..64]))

let private hkdf (salt: byte[]) (ikm: byte[]) (info: byte[]) (length: int) : byte[] =
    HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, length, salt, info)

/// What a user agent does with the message (RFC 8291 section 3.4): for the tests.
let private decrypt (body: byte[]) (receiver: ECDiffieHellman) (auth: byte[]) : (uint32 * byte[]) option =
    try
        let salt = body[..15]
        let recordSize = (uint32 body[16] <<< 24) ||| (uint32 body[17] <<< 16) ||| (uint32 body[18] <<< 8) ||| uint32 body[19]
        let idLength = int body[20]
        let serverPublicBytes = body[21 .. 20 + idLength]
        let ciphertext = body[21 + idLength ..]
        use serverKey =
            ECDiffieHellman.Create(
                ECParameters(Curve = p256, Q = ECPoint(X = serverPublicBytes[1..32], Y = serverPublicBytes[33..64]))
            )
        let shared = receiver.DeriveRawSecretAgreement serverKey.PublicKey
        let info = Array.concat [ Encoding.ASCII.GetBytes "WebPush: info\000"; P256.uncompressed receiver; serverPublicBytes ]
        let prk = hkdf auth shared info 32
        let key = hkdf salt prk (Encoding.ASCII.GetBytes "Content-Encoding: aes128gcm\000") 16
        let nonce = hkdf salt prk (Encoding.ASCII.GetBytes "Content-Encoding: nonce\000") 12
        use cipher = new AesGcm(key, 16)
        let text = ciphertext[.. ciphertext.Length - 17]
        let tag = ciphertext[ciphertext.Length - 16 ..]
        let plaintext = Array.zeroCreate<byte> text.Length
        cipher.Decrypt(nonce, text, tag, plaintext)
        Some(recordSize, plaintext)
    with _ ->
        None

/// RFC 8188: the last record ends with the delimiter 0x02 (any padding zeros sit between the message and it).
let private openMessage (plaintext: byte[]) : string =
    Assert.True(plaintext.Length >= 1 && plaintext[plaintext.Length - 1] = 2uy, "aes128gcm delimiter")
    Encoding.UTF8.GetString plaintext[.. plaintext.Length - 2]

/// A user agent's key and auth secret.
type private Receiver() =
    let key = ECDiffieHellman.Create p256
    let auth = RandomNumberGenerator.GetBytes 16

    member _.Key = key
    member _.Auth = auth

    member _.Subscription(id: int64, endpoint: string) : Ef.PushSubscription =
        let p256dh = encode64 (P256.uncompressed key)
        { PushSubscription.build 1L endpoint p256dh (encode64 auth) null with Id = id }

    member _.Open(body: byte[]) : string =
        match decrypt body key auth with
        | Some(_, plaintext) -> openMessage plaintext
        | None -> failwith "decrypts"

// --- tests.rs --------------------------------------------------------------------------------------------------

type private PushService =
    { Server: FakeServer
      Resolver: FakeResolver
      Dialer: MappingDialer
      Provider: ServiceProvider
      Env: PushEnv }

let private pushServiceWith (trusted: bool) (route: Route) : PushService =
    let routes =
        [ "/fcm/send/abc"; "/fcm/send/123"; "/fcm/send/456"; "/fcm/send/567"; "/fcm/send/789" ]
        |> List.map (fun path -> { route with Path = path })
    let server = FakeServer.StartTls routes
    let resolver = FakeResolver([ "fcm.googleapis.com", [ PublicIp ] ])
    let dialer = MappingDialer(HashSet [ IPAddress.Parse PublicIp ], server.Addr)
    let provider, env = pushEnv resolver dialer trusted
    { Server = server
      Resolver = resolver
      Dialer = dialer
      Provider = provider
      Env = env }

let private pushService (status: int) (reason: string) : PushService =
    pushServiceWith true { Route.create "POST" "*" "" status with Reason = reason }

let private notification (subscription: Ef.PushSubscription) : Notification =
    { Title = "t"
      Body = "b"
      Path = "/"
      Badge = 0L
      Subscription = subscription }

[<Fact>]
let ``encodes the message like json generate`` () =
    let notification =
        { Title = "Designers <&> \"quotes\" é 😀"
          Body = "Kevin: line\nbreak\ttab \u2028 \u001f / \\ "
          Path = "/rooms/1"
          Badge = 3L
          Subscription = PushSubscription.build 1L null null null null }
    Assert.Equal(str ((expected ()).GetProperty "message"), WebPush.Notification.encodedMessage notification)

/// WP1: the vector's receiver key decrypts what the library sent to the message the gem encrypts, and the longest payload fits.
[<Fact>]
let ``the payload decrypts to the message`` () =
    task {
        let service = pushService 201 "Created"
        use _server = service.Server
        let expected = expected ()
        let receiver = keyOf (decode64 (str (expected.GetProperty "receiver_private_key"))) (decode64 (str (expected.GetProperty "p256dh")))
        let auth = decode64 (str (expected.GetProperty "auth"))
        let subscription =
            { PushSubscription.build 1L "https://fcm.googleapis.com/fcm/send/abc" (str (expected.GetProperty "p256dh")) (str (expected.GetProperty "auth")) null with
                Id = 1L }
        let notification =
            { Title = "Designers <&> \"quotes\" é 😀"
              Body = "Kevin: line\nbreak\ttab \u2028 \u001f / \\ "
              Path = "/rooms/1"
              Badge = 3L
              Subscription = subscription }
        let! delivered = WebPush.deliver service.Env (vapid ()) notification
        Assert.Equal(Ok true, delivered |> Result.mapError (fun _ -> ()))
        match decrypt (List.head (service.Server.Received())).Body receiver auth with
        | Some(recordSize, plaintext) ->
            Assert.Equal(4096u, recordSize)
            Assert.Equal<byte[]>(Array.append (Encoding.UTF8.GetBytes(str (expected.GetProperty "message"))) [| 2uy |], plaintext)
        | None -> failwith "the user agent couldn't decrypt it"
    }

[<Fact>]
let ``the longest payload fits a push message`` () =
    task {
        let service = pushService 201 "Created"
        use _server = service.Server
        let receiver = Receiver()
        let notification =
            { Title = String.replicate (PushSubscription.MaxPayloadTitleBytes / 2) "\""
              Body = String.replicate (PushSubscription.MaxPayloadBodyBytes / 6) "\u0001"
              Path = $"/rooms/{Int64.MinValue}"
              Badge = Int64.MinValue
              Subscription = receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc") }
        let! delivered = WebPush.deliver service.Env (vapid ()) notification
        Assert.Equal(Ok true, delivered |> Result.mapError DeliveryError.message |> Result.mapError (fun _ -> ()))
        Assert.Equal(WebPush.Notification.encodedMessage notification, receiver.Open (List.head (service.Server.Received())).Body)
    }

/// WP2, and the pinned address: the request goes to the address the delivery resolved, with no second lookup.
[<Fact>]
let ``delivers to the pinned address with the vapid headers`` () =
    task {
        let service = pushService 201 "Created"
        use _server = service.Server
        let receiver = Receiver()
        let notification = notification (receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc"))
        let before = DateTimeOffset.UtcNow.ToUnixTimeSeconds()

        let! delivered = WebPush.deliver service.Env (vapid ()) notification
        Assert.Equal(Ok true, delivered |> Result.mapError DeliveryError.message |> Result.mapError (fun _ -> ()))

        Assert.Equal<string list>([ "fcm.googleapis.com" ], service.Resolver.Lookups)
        Assert.Equal<IPEndPoint list>([ IPEndPoint(IPAddress.Parse PublicIp, 443) ], service.Dialer.Dialed)
        let received = service.Server.Received()
        Assert.Equal(1, received.Length)
        let request = received[0]
        Assert.Equal(("POST", "/fcm/send/abc"), (request.Method, request.Target))
        Assert.Equal(Some "fcm.googleapis.com", request.Header "Host")
        Assert.Equal(Some "aes128gcm", request.Header "Content-Encoding")
        Assert.Equal(Some "2419200", request.Header "TTL")
        Assert.Equal(Some "high", request.Header "Urgency")
        Assert.Equal(Some "application/octet-stream", request.Header "Content-Type")
        Assert.Equal(Some(string request.Body.Length), request.Header "Content-Length")

        let authorization = (request.Header "Authorization").Value
        let expected = expected ()
        Assert.StartsWith("vapid t=", authorization)
        let t, k = (let rest = authorization.Substring 8 in let at = rest.IndexOf ", k=" in rest.Substring(0, at), rest.Substring(at + 4))
        Assert.Equal(str (expected.GetProperty "authorization_k"), k)
        let segments = t.Split '.'
        Assert.Equal(str (expected.GetProperty "jwt_header_segment"), segments[0])
        use claims = JsonDocument.Parse(Encoding.UTF8.GetString(decode64 segments[1]))
        Assert.Equal("https://fcm.googleapis.com", str (claims.RootElement.GetProperty "aud"))
        Assert.Equal(ReferenceSubject, str (claims.RootElement.GetProperty "sub"))
        // `exp` is twelve hours out (the library reads its own clock).
        let exp = claims.RootElement.GetProperty("exp").GetInt64()
        Assert.InRange(exp, before + 12L * 3600L - 5L, before + 12L * 3600L + 120L)
        let publicKey = decode64 VapidPublicKey
        use key = ECDsa.Create(ECParameters(Curve = p256, Q = ECPoint(X = publicKey[1..32], Y = publicKey[33..64])))
        Assert.True(
            key.VerifyData(
                Encoding.ASCII.GetBytes($"{segments[0]}.{segments[1]}"),
                decode64 segments[2],
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation
            )
        )
        Assert.Equal(WebPush.Notification.encodedMessage notification, receiver.Open request.Body)
    }

[<Fact>]
let ``signs with the configured subject`` () =
    task {
        let service = pushService 201 "Created"
        use _server = service.Server
        let vapid =
            match VapidConfig.Create("mailto:ops@example.com", VapidPublicKey, VapidPrivateKey) with
            | Ok vapid -> vapid
            | Error error -> failwith (VapidError.message error)
        let! _ = WebPush.deliver service.Env vapid (notification ((Receiver()).Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")))
        let authorization = ((List.head (service.Server.Received())).Header "Authorization").Value
        let jwt = (authorization.Substring(8).Split ',')[0]
        use claims = JsonDocument.Parse(Encoding.UTF8.GetString(decode64 (jwt.Split '.').[1]))
        Assert.Equal("mailto:ops@example.com", str (claims.RootElement.GetProperty "sub"))
    }

[<Fact>]
let ``rejects bad vapid keys up front`` () =
    use other = ECDiffieHellman.Create p256
    let otherPublicKey = encode64 (P256.uncompressed other)
    let keys (publicKey: string) (privateKey: string) = VapidConfig.Create(ReferenceSubject, publicKey, privateKey) |> Result.map ignore
    Assert.Equal(Error InvalidPublicKey, keys "" VapidPrivateKey)
    Assert.Equal(Error InvalidPublicKey, keys "dGVzdF9rZXk" VapidPrivateKey)
    Assert.Equal(Error InvalidPrivateKey, keys VapidPublicKey "not base64!")
    Assert.Equal(Error InvalidPrivateKey, keys VapidPublicKey (encode64 (Array.create 32 0xffuy)))
    Assert.Equal(Error Mismatched, keys otherPublicKey VapidPrivateKey)
    Assert.Equal(Ok(), keys (VapidPublicKey.TrimEnd '=') (VapidPrivateKey.TrimEnd '='))

let private config (publicKey: string option) (privateKey: string option) : CampfireOptions =
    let vars = dict [ "SECRET_KEY_BASE_DUMMY", "1" ]
    match CampfireOptions.fromLookup (fun name -> match vars.TryGetValue name with | true, v -> v | _ -> null) with
    | Ok config ->
        { config with
            VapidPublicKey = publicKey
            VapidPrivateKey = privateKey
            VapidSubject = ReferenceSubject }
    | Error message -> failwith message

/// WP6: invalid or missing keys turn push off (`None`), and nothing throws.
[<Fact>]
let ``web push is off without a valid key pair`` () =
    task {
        use t = new TestDb()
        let service = pushService 201 "Created"
        use _server = service.Server
        let pool publicKey privateKey = IntegrationJobs.webPushPool (config publicKey privateKey) t.Factory t.Gate t.Env service.Env NullLogger.Instance
        Assert.True((pool None (Some VapidPrivateKey)).IsNone)
        Assert.True((pool (Some "dGVzdF9rZXk") (Some VapidPrivateKey)).IsNone)
        let on = pool (Some VapidPublicKey) (Some VapidPrivateKey)
        Assert.True on.IsSome
        on.Value.Dispose()
        Assert.Equal(Error Missing, VapidConfig.FromConfig(config (Some VapidPublicKey) None) |> Result.map ignore)
    }

/// WP5
[<Fact>]
let ``skips endpoints it may not deliver to`` () =
    task {
        let service = pushService 201 "Created"
        use _server = service.Server
        service.Resolver.Set("updates.push.services.mozilla.com", [ [ IPAddress.Parse "10.0.0.5" ] ])
        let receiver = Receiver()
        for endpoint in
            [ "https://updates.push.services.mozilla.com/wpush/v2/x" // resolves privately
              "https://web.push.apple.com/QaBC123" // doesn't resolve
              "https://attacker.example.com/collect"
              "https://fcm.googleapis.com:22/fcm/send/abc"
              "http://fcm.googleapis.com/fcm/send/abc"
              "https://evilfcm.googleapis.com.attacker.example/webhook" ] do
            let! delivered = WebPush.deliver service.Env (vapid ()) (notification (receiver.Subscription(1L, endpoint)))
            match delivered with
            | Ok false -> ()
            | other -> failwith $"{endpoint}: {other |> Result.mapError DeliveryError.message}"
        Assert.True(service.Server.Received().IsEmpty)
        Assert.Empty(service.Dialer.Dialed)
        Assert.Equal<string list>([ "updates.push.services.mozilla.com"; "web.push.apple.com" ], service.Resolver.Lookups)
    }

/// WP3 and WP4
[<Fact>]
let ``raises what the gem raises`` () =
    task {
        let receiver = Receiver()
        let subscription = receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")
        for (status, reason, kind, invalidates) in
            [ 410, "Gone", "WebPush::ExpiredSubscription", true
              404, "Not Found", "WebPush::InvalidSubscription", true
              401, "Unauthorized", "WebPush::Unauthorized", false
              403, "Forbidden", "WebPush::Unauthorized", false
              400, "Bad Request", "WebPush::ResponseError", false
              413, "Payload Too Large", "WebPush::PayloadTooLarge", false
              429, "Too Many Requests", "WebPush::TooManyRequests", false
              500, "Internal Server Error", "WebPush::PushServiceError", false
              503, "Service Unavailable", "WebPush::PushServiceError", false
              302, "Found", "WebPush::ResponseError", false ] do
            let service = pushService status reason
            use _server = service.Server
            match! WebPush.deliver service.Env (vapid ()) (notification subscription) with
            | Error error -> Assert.Equal((kind, invalidates), (DeliveryError.className error, DeliveryError.invalidatesSubscription error))
            | Ok delivered -> failwith $"{status} {reason}: {delivered}"
            Assert.Equal(1, service.Server.Received().Length)
    }

/// WP4: a 429 that says when to retry is still sent once.
[<Fact>]
let ``a 429 with retry after is sent once`` () =
    task {
        let service = pushServiceWith true (Route.create "POST" "*" "" 429 |> Route.header "Retry-After" "1")
        use _server = service.Server
        let receiver = Receiver()
        match! WebPush.deliver service.Env (vapid ()) (notification (receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc"))) with
        | Error error -> Assert.Equal("WebPush::TooManyRequests", DeliveryError.className error)
        | Ok delivered -> failwith $"{delivered}"
        do! Task.Delay 1500
        Assert.Equal(1, service.Server.Received().Length)
    }

[<Fact>]
let ``only the subscriptions own faults invalidate it`` () =
    task {
        // A key that isn't a point on the curve (as in the fixtures) can never be delivered to.
        let service = pushService 201 "Created"
        use _server = service.Server
        let badKey =
            PushSubscription.build 1L "https://fcm.googleapis.com/fcm/send/abc" "dGVzdF9rZXk" "dGVzdF9hdXRo" null
        match! WebPush.deliver service.Env (vapid ()) (notification badKey) with
        | Error error ->
            Assert.Equal(("OpenSSL::PKey::EC::Point::Error", true), (DeliveryError.className error, DeliveryError.invalidatesSubscription error))
        | Ok delivered -> failwith $"{delivered}"

        // A certificate that doesn't verify may be our fault (an empty CA store, a skewed clock).
        let untrusted = pushServiceWith false (Route.create "POST" "*" "" 201)
        use _untrusted = untrusted.Server
        let receiver = Receiver()
        match! WebPush.deliver untrusted.Env (vapid ()) (notification (receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc"))) with
        | Error error ->
            Assert.Equal(("OpenSSL::SSL::SSLError", false), (DeliveryError.className error, DeliveryError.invalidatesSubscription error))
        | Ok delivered -> failwith $"{delivered}"

        let blank = PushSubscription.build 1L "https://fcm.googleapis.com/fcm/send/abc" "" "dGVzdF9hdXRo" null
        match! WebPush.deliver service.Env (vapid ()) (notification blank) with
        | Error error -> Assert.Equal(("ArgumentError", false), (DeliveryError.className error, DeliveryError.invalidatesSubscription error))
        | Ok delivered -> failwith $"{delivered}"
    }

/// WP7 (the delivery half; the redirect is the controller's)
[<Fact>]
let ``test notification`` () =
    task {
        let service = pushService 201 "Created"
        use _server = service.Server
        let receiver = Receiver()
        let subscription = receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")
        match! WebPush.deliverTestNotification service.Env (vapid ()) subscription 4L "http://example.com/users/me/push_subscriptions" with
        | Ok() -> ()
        | Error error -> failwith (DeliveryError.message error)
        Assert.Equal(1, service.Server.Received().Length)

        use message = JsonDocument.Parse(receiver.Open (List.head (service.Server.Received())).Body)
        let root = message.RootElement
        Assert.Equal("Campfire Test", str (root.GetProperty "title"))
        let options = root.GetProperty "options"
        Assert.True(Guid.TryParse(str (options.GetProperty "body")) |> fst)
        Assert.Equal("/account/logo", str (options.GetProperty "icon"))
        let data = options.GetProperty "data"
        Assert.Equal("http://example.com/users/me/push_subscriptions", str (data.GetProperty "path"))
        Assert.Equal(4L, data.GetProperty("badge").GetInt64())

        let expired = pushService 410 "Gone"
        use _expired = expired.Server
        let! result = WebPush.deliverTestNotification expired.Env (vapid ()) subscription 4L "/"
        Assert.True result.IsError
    }

/// `test/models/room/push_test.rb`: "deliver new message ..." and "destroys invalid subscriptions".
[<Fact>]
let ``pushes messages and destroys expired subscriptions`` () =
    task {
        use t = new TestDb()
        let labels = [ "david_chrome"; "jason_chrome"; "jz_chrome"; "kevin_chrome" ]
        let receivers = [ for label in labels -> TestDb.Id label, Receiver() ]
        t.Sql(fun conn ->
            for (id, receiver) in receivers do
                RawSql.run
                    conn
                    "UPDATE push_subscriptions SET p256dh_key = ?, auth_key = ? WHERE id = ?"
                    [ box (encode64 (P256.uncompressed receiver.Key)); box (encode64 receiver.Auth); box id ]
            RawSql.run conn "UPDATE memberships SET involvement = 'invisible' WHERE id = ?" [ box (TestDb.Id "kevin_designers") ])

        let create () =
            task {
                let attributes: NewMessage =
                    { RoomId = TestDb.Id "designers"
                      CreatorId = TestDb.Id "david"
                      ClientMessageId = Some "earth"
                      Body = Some "Hey @kevin"
                      AttachmentBlobId = None }
                use context = t.Factory.CreateDbContext()
                match! Campfire.Db.Ef.Writes.run t.Gate t.Env context (fun tx -> Message.create tx attributes) with
                | Ok message -> return message
                | Error error -> return failwith (DbError.display error)
            }

        // Delivered: nothing is destroyed.
        let ok = pushService 201 "Created"
        use _ok = ok.Server
        let destroyed = List<int64>()
        let! pool = startPool ok.Env (fun id -> lock destroyed (fun () -> destroyed.Add id); Task.FromResult(Ok()))
        let! message = create ()
        use pushContext = t.Factory.CreateDbContext()
        let payload =
            match Ef.Reads.run pushContext (fun ctx -> PushDeliveries.pushMessage pool ctx t.Env.RichText message (RailsTime.now t.Env.Time)) with
            | Ok payload -> payload
            | Error error -> failwith (DbError.display error)
        Assert.Equal("Designers", payload.Title)
        Assert.Equal("David: Hey @kevin", payload.Body)
        do! stopPool pool
        let received = ok.Server.Received()
        Assert.Equal(2, received.Length)
        let suffixes = dict [ "david_chrome", "123"; "jason_chrome", "567"; "jz_chrome", "456"; "kevin_chrome", "789" ]
        for request in received do
            let _, receiver =
                receivers |> List.find (fun (id, _) -> request.Target.EndsWith $"/fcm/send/{suffixes[labels |> List.find (fun l -> TestDb.Id l = id)]}")
            use opened = JsonDocument.Parse(receiver.Open request.Body)
            Assert.Equal("Designers", str (opened.RootElement.GetProperty "title"))
            Assert.Equal(payload.Path, str (opened.RootElement.GetProperty("options").GetProperty("data").GetProperty "path"))
        Assert.True(destroyed.Count = 0)

        // Expired: both subscriptions are destroyed by the handler, in the pool's worker thread.
        let gone = pushService 410 "Gone"
        use _gone = gone.Server
        let handler (id: int64) : Task<Result<unit, string>> =
            task {
                use destroyContext = t.Factory.CreateDbContext()
                match! Ef.Writes.run t.Gate t.Env destroyContext (fun tx -> PushSubscription.destroy tx (PushSubscription.find tx.Db id)) with
                | Ok()
                | Error(RecordNotFound _) -> return Ok()
                | Error error -> return Error(DbError.display error)
            }
        let! pool = startPool gone.Env handler
        let countPush () =
            use ctx = t.Factory.CreateDbContext()
            Ef.Reads.run ctx PushSubscription.count
        let before = countPush ()
        let! message = create ()
        use context = t.Factory.CreateDbContext()
        Ef.Reads.run context (fun ctx -> PushDeliveries.pushMessage pool ctx t.Env.RichText message (RailsTime.now t.Env.Time)) |> ignore
        do! stopPool pool
        let after = countPush ()
        let count (result: Result<int64, DbError>) = match result with Ok n -> n | Error error -> failwith (DbError.display error)
        Assert.Equal(count before - 2L, count after)
    }

[<Fact>]
let ``the pool keeps subscriptions it failed to reach`` () =
    task {
        let service = pushService 201 "Created"
        use _server = service.Server
        let untrusted = pushServiceWith false (Route.create "POST" "*" "" 201)
        use _untrusted = untrusted.Server
        let destroyed = List<int64>()
        let! pool = startPool untrusted.Env (fun id -> lock destroyed (fun () -> destroyed.Add id); Task.FromResult(Ok()))
        pool.DeliverLater(notification ((Receiver()).Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")))
        do! stopPool pool
        Assert.True(destroyed.Count = 0)
    }

/// Answers a lookup only once let go, with nothing: so deliveries wait in the pool, and finish without a network once
/// they are released. (Rust's test holds its deliveries back by not yielding on a single-threaded runtime.)
type private GatedResolver() =
    let gate = TaskCompletionSource()
    member _.Release() = gate.TrySetResult() |> ignore

    interface IResolver with
        member _.Lookup(_host: string, _cancellation: CancellationToken) : Task<Result<IPAddress list, exn>> =
            task {
                do! gate.Task
                return Error(exn "released")
            }

[<Fact>]
let ``the pool drops deliveries past its queue`` () =
    task {
        let resolver = GatedResolver()
        let _, net = pushEnv resolver (TcpDialer()) false
        let! pool = startPool net (fun _ -> Task.FromResult(Ok()))
        let receiver = Receiver()
        let subscription = receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")
        // 50 readers each take a delivery and wait on the gated lookup ...
        for _ in 1 .. 50 do
            pool.DeliverLater(notification subscription)
        while pool.Running < 50 do
            do! Task.Delay 5
        Assert.Equal(50, pool.Running)
        // ... 10,000 more wait in the queue, and the rest are dropped.
        for _ in 1 .. (10000 + 5) do
            pool.DeliverLater(notification subscription)
        Assert.Equal(10050, pool.Pending)
        Assert.Equal(50, pool.Running)
        resolver.Release()
        do! stopPool pool
    }

[<Fact>]
let ``CH1 one queued notification wakes one reader`` () =
    task {
        let resolver = GatedResolver()
        resolver.Release()
        let _, net = pushEnv resolver (TcpDialer()) false
        let! pool = startPool net (fun _ -> Task.FromResult(Ok()))
        let subscription = (Receiver()).Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")
        // With 50 readers waiting, 1,000 notifications a millisecond apart: the old `WaitToReadAsync` loop woke all 50 readers
        // for each one. Each notification wakes at most 2 readers here (in fact one).
        for _ in 1 .. 1000 do
            pool.DeliverLater(notification subscription)
            do! Task.Delay 1
        while pool.Pending > 0 do
            do! Task.Delay 5
        Assert.InRange(pool.Wakeups, 1000, 2000)
        do! stopPool pool
    }

[<Fact>]
let ``CH2 a closed queue is delivered whole and every reader ends once`` () =
    task {
        let resolver = GatedResolver()
        let _, net = pushEnv resolver (TcpDialer()) false
        let! pool = startPool net (fun _ -> Task.FromResult(Ok()))
        let subscription = (Receiver()).Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")
        for _ in 1 .. 50 do
            pool.DeliverLater(notification subscription)
        while pool.Running < 50 do
            do! Task.Delay 5
        for _ in 1 .. 10000 do
            pool.DeliverLater(notification subscription)
        Assert.Equal(10050, pool.Pending)
        resolver.Release()
        do! stopPool pool
        Assert.Equal(10050, pool.Wakeups)
        Assert.Equal(0, pool.Pending)
        Assert.True((nonNull (pool :> BackgroundService).ExecuteTask).IsCompletedSuccessfully)
    }

/// A stop that comes right after the start still delivers what was queued and destroys what is gone: `StartAsync` runs all of
/// `ExecuteAsync` on the thread pool, so the stop may run before the readers exist.
[<Fact>]
let ``a stop right after the start still delivers and invalidates`` () =
    task {
        let gone = pushService 410 "Gone"
        use _gone = gone.Server
        let receiver = Receiver()
        for round in 1 .. 20 do
            let destroyed = List<int64>()
            let pool = new PushDeliveries(gone.Env, vapid (), (fun id -> lock destroyed (fun () -> destroyed.Add id); Task.FromResult(Ok())), NullLogger.Instance)
            pool.DeliverLater(notification (receiver.Subscription(int64 round, "https://fcm.googleapis.com/fcm/send/abc")))
            do! (pool :> IHostedService).StartAsync CancellationToken.None
            do! stopPool pool
            Assert.True((destroyed.Count = 1), $"round {round}: {destroyed.Count} destroyed")
        Assert.Equal(20, gone.Server.Received().Length)
    }

type private PanickingResolver() =
    interface IResolver with
        member _.Lookup(host: string, _cancellation: CancellationToken) : Task<Result<IPAddress list, exn>> = failwith $"resolving {host} panicked"

/// A delivery that panics still gives its place in the queue back, so panics can't fill it up.
[<Fact>]
let ``a panicking delivery frees its slot`` () =
    task {
        let _, net = pushEnv (PanickingResolver()) (TcpDialer()) false
        let! pool = startPool net (fun _ -> Task.FromResult(Ok()))
        pool.DeliverLater(notification ((Receiver()).Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")))
        do! stopPool pool
        Assert.Equal(0, pool.Pending)
    }

/// RD4 (unit 8.6): a push failure's log line names the host and never the endpoint's path (which holds the subscription's token).
[<Fact>]
let ``RD4 a push failure logs the host and not the endpoint's path`` () =
    task {
        let logs = new CapturingLoggers()
        let receiver = Receiver()
        let subscription = receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc")
        // A push service that refuses, and one that cannot be reached (the library's own message may carry the request URI).
        let refusing = pushService 500 "Internal Server Error"
        use _refusing = refusing.Server
        let _, env =
            pushEnv (FakeResolver([ "fcm.googleapis.com", [ PublicIp ] ])) (MappingDialer(HashSet [ IPAddress.Parse PublicIp ], IPEndPoint(IPAddress.Loopback, 1))) true
        for net in [ refusing.Env; env ] do
            let pool = new PushDeliveries(net, vapid (), (fun _ -> Task.FromResult(Ok())), logs.Logger "campfire.web_push")
            do! (pool :> IHostedService).StartAsync CancellationToken.None
            pool.DeliverLater(notification subscription)
            do! stopPool pool
        let text = logs.Text
        Assert.Contains("Error in WebPush::Pool.deliver", text)
        Assert.Contains("fcm.googleapis.com", text)
        Assert.DoesNotContain("/fcm/send/abc", text)
        Assert.DoesNotContain("abc", text.Replace("WebPush::Pool", ""))
    }

/// SR-J5 (Phase 8 security review): the library reads a push service's error body whole; the "push" client caps that buffer, so a
/// gigabyte gzip bomb in a 500 neither inflates nor holds the delivery, and the answer is still the gem's `PushServiceError`.
[<Fact>]
let ``SR-J5 a push service's error body is capped`` () =
    task {
        let service = pushServiceWith true (Route.create "POST" "*" "" 500 |> Route.header "Content-Encoding" "gzip" |> Route.body (gzipBomb 1024))
        use _server = service.Server
        let receiver = Receiver()
        let started = DateTime.UtcNow
        match! WebPush.deliver service.Env (vapid ()) (notification (receiver.Subscription(1L, "https://fcm.googleapis.com/fcm/send/abc"))) with
        | Error error -> Assert.False(DeliveryError.invalidatesSubscription error, DeliveryError.message error)
        | Ok delivered -> failwith $"{delivered}"
        Assert.True((DateTime.UtcNow - started) < TimeSpan.FromSeconds 10.0, $"{DateTime.UtcNow - started}")
        Assert.Equal(1, service.Server.Received().Length)
    }
