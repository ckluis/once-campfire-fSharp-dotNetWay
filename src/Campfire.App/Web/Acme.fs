// plans/design-front.md F3-F6 (unit 7.3): ACME on Certes. The certificate cache stays autocert's `DirCache` layout (so an install that
// ran Thruster keeps its certificates and account), the schedule is a pure function, one renewal pass is one testable call, and the
// order happens after `ApplicationStarted` (the HTTP listener must answer the CA) instead of on the first handshake. Not yet wired into
// the production host (7.4).
namespace Campfire.App.Web

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Net.Http
open System.Net.Security
open System.Security.Cryptography
open System.Security.Cryptography.X509Certificates
open System.Text
open System.Threading
open System.Threading.Tasks
open Certes
open Certes.Acme
open Certes.Acme.Resource
open Microsoft.AspNetCore.Server.Kestrel.Https
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

/// A certificate ready for handshakes.
type LoadedCertificate =
    { Options: SslServerAuthenticationOptions
      NotBefore: DateTimeOffset
      NotAfter: DateTimeOffset }

module ServerNames =
    /// `idna.Lookup.ToASCII` (STD3 rules: an underscore or any other non-LDH ASCII character is refused): a lowercased ASCII (punycode) name, no trailing dot; an IP address or a non-host is refused.
    let private toAscii (host: string) : string voption =
        if host = "" || host.StartsWith '[' then
            ValueNone
        else
            match System.Net.IPAddress.TryParse host with
            | true, _ -> ValueNone
            | _ ->
                try
                    let ascii = IdnMapping(UseStd3AsciiRules = true).GetAscii(host).ToLowerInvariant()
                    if ascii = "" || ascii.IndexOfAny [| ' '; '/'; '\\'; '@'; '#'; '?'; ':' |] >= 0 then ValueNone else ValueSome ascii
                with :? ArgumentException ->
                    ValueNone

    /// autocert's server name checks and normalization (`acme/autocert.(*Manager).GetCertificate`).
    let normalize (serverName: string | null) : Result<string, string> =
        let name = match serverName with null -> "" | n -> n
        if name = "" then
            Error "acme/autocert: missing server name"
        elif not (name.Trim('.').Contains '.') then
            Error "acme/autocert: server name component count invalid"
        elif name.IndexOfAny [| '/'; '\\' |] >= 0 then
            Error "acme/autocert: server name contains invalid character"
        else
            match toAscii (name.TrimEnd '.') with
            | ValueSome domain -> Ok domain
            | ValueNone -> Error "acme/autocert: invalid server name"

module DirCache =
    [<Literal>]
    let AccountKeyFile = "acme_account+key"

    let private privateMode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite

    /// `DirCache.Put`: the directory 0700, the file 0600, written to a temporary file in it and renamed over the name.
    let private put (storage: string) (name: string) (text: string) : unit =
        if OperatingSystem.IsWindows() then
            Directory.CreateDirectory storage |> ignore
        else
            Directory.CreateDirectory(storage, privateMode ||| UnixFileMode.UserExecute) |> ignore
        let temporary = Path.Combine(storage, $"{name}.tmp{Guid.NewGuid():N}")
        let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None)
        if not (OperatingSystem.IsWindows()) then options.UnixCreateMode <- privateMode
        try
            (
                use file = new FileStream(temporary, options)
                file.Write(Encoding.ASCII.GetBytes text)
                file.Flush true
            )
            File.Move(temporary, Path.Combine(storage, name), true)
        finally
            // A failed write or rename leaves no stray copy of a private key behind (a no-op once the rename succeeded).
            if File.Exists temporary then File.Delete temporary

    /// The key as autocert writes it (`x509.MarshalECPrivateKey`): SEC1 "EC PRIVATE KEY" with the curve named, which Go needs to read it back.
    let private keyPem (key: ECDsa) : string = (key.ExportECPrivateKeyPem()).Replace("\r\n", "\n") + "\n"

    /// A cache file: the private key, then the chain's certificates in the order given.
    let write (storage: string) (domain: string) (key: ECDsa) (chainPem: string) : unit =
        let text = StringBuilder(keyPem key)
        let chain = X509Certificate2Collection()
        chain.ImportFromPem chainPem
        for certificate in chain do
            text.Append(certificate.ExportCertificatePem()).Append('\n') |> ignore
            certificate.Dispose()
        put storage domain (text.ToString())

    /// Reads and checks a cache file (autocert's `validCert`): `CreateFromPem` refuses a key that does not match the leaf, the leaf covers
    /// the name (`MatchesHostname`, wildcards included) and is current at `now`. The rest of the file's certificates are the chain.
    let read (storage: string) (domain: string) (now: DateTimeOffset) : Result<LoadedCertificate, string> =
        let path = Path.Combine(storage, domain)
        if not (File.Exists path) then
            Error $"no cached certificate for {domain}"
        else
            try
                let text = File.ReadAllText path
                let leaf = X509Certificate2.CreateFromPem(text, text)
                let notBefore, notAfter = DateTimeOffset leaf.NotBefore, DateTimeOffset leaf.NotAfter
                if now < notBefore then
                    Error "certificate is not yet valid"
                elif now >= notAfter then
                    Error "certificate is expired"
                elif not (leaf.MatchesHostname(domain, true, false)) then
                    Error $"certificate is not valid for {domain}"
                else
                    let chain = X509Certificate2Collection()
                    chain.ImportFromPem text
                    // The leaf is the first certificate; the others are the intermediates sent after it.
                    let intermediates = X509Certificate2Collection()
                    for i in 1 .. chain.Count - 1 do
                        intermediates.Add chain[i] |> ignore
                    let context = SslStreamCertificateContext.Create(leaf, intermediates, false)
                    let options = SslServerAuthenticationOptions()
                    options.ServerCertificateContext <- context
                    // Protocols and cipher suites stay the platform's defaults (Kestrel's); no client certificates, so never renegotiate
                    // (HTTP/2 forbids it, RFC 9113 9.2.1).
                    options.AllowRenegotiation <- false
                    options.ApplicationProtocols <- List<SslApplicationProtocol>([ SslApplicationProtocol.Http2; SslApplicationProtocol.Http11 ])
                    Ok { Options = options; NotBefore = notBefore; NotAfter = notAfter }
            with e ->
                Error $"certificate: {e.Message}"

    /// `acme_account+key`: the account's key as SEC1 PEM, made once (P-256) if the file is absent.
    let accountKeyPem (storage: string) : string =
        let path = Path.Combine(storage, AccountKeyFile)
        if File.Exists path then
            File.ReadAllText path
        else
            use key = ECDsa.Create ECCurve.NamedCurves.nistP256
            let text = keyPem key
            put storage AccountKeyFile text
            text

module Renewal =
    let private renewBefore = TimeSpan.FromDays 30.0
    /// autocert's `renewJitter`.
    let JitterWindow = TimeSpan.FromHours 1.0

    /// When a certificate is due: `NotAfter - min(30 days, lifetime / 3) - jitter`. A fixed 30 days would order forever for a CA that
    /// issues short-lived certificates (Pebble's six days).
    let due (certificate: LoadedCertificate) (jitter: TimeSpan) : DateTimeOffset =
        let lifetime = certificate.NotAfter - certificate.NotBefore
        certificate.NotAfter - (let third = lifetime / 3.0 in if third < renewBefore then third else renewBefore) - jitter

    /// A failed order is retried 30 to 60 minutes later (`jitter01` in [0, 1)).
    let retryAfterFailure (jitter01: float) : TimeSpan =
        TimeSpan.FromMinutes 30.0 + TimeSpan.FromMinutes(30.0 * jitter01)

/// The names this host serves, their certificates once issued, and the HTTP-01 answers the CA is waiting for.
[<Sealed>]
type CertificateStore(domains: string list) =
    let names =
        domains
        |> List.choose (fun d -> match ServerNames.normalize d with Ok n -> Some n | Error _ -> None)
        |> List.distinct
    let certificates = ConcurrentDictionary<string, LoadedCertificate>()
    let first =
        names |> List.map (fun n -> n, TaskCompletionSource<LoadedCertificate>(TaskCreationOptions.RunContinuationsAsynchronously)) |> dict
    let challenges = ConcurrentDictionary<string, string>()

    member _.Domains: string list = names

    member _.Get(domain: string) : LoadedCertificate voption =
        match certificates.TryGetValue domain with
        | true, c -> ValueSome c
        | _ -> ValueNone

    /// Replaces the domain's certificate and completes its first-certificate task.
    member _.Set(domain: string, certificate: LoadedCertificate) : unit =
        certificates[domain] <- certificate
        match first.TryGetValue domain with
        | true, source -> source.TrySetResult certificate |> ignore
        | _ -> ()

    /// A `TLS_DOMAIN` name's certificate, waiting for the first one.
    member _.WaitFor(domain: string, cancellation: CancellationToken) : Task<LoadedCertificate> =
        match first.TryGetValue domain with
        | true, source -> source.Task.WaitAsync cancellation
        | _ -> Task.FromException<LoadedCertificate>(InvalidOperationException $"acme/autocert: host \"{domain}\" not configured in HostWhitelist")

    /// The handshake callback (F3): the stored options for the client's name, the first certificate awaited, and a failure (the
    /// handshake is refused) for any other name.
    member this.HandshakeOptions(context: TlsHandshakeCallbackContext) : ValueTask<SslServerAuthenticationOptions> =
        match ServerNames.normalize context.ClientHelloInfo.ServerName with
        | Error reason -> ValueTask.FromException<SslServerAuthenticationOptions>(InvalidOperationException reason)
        | Ok domain ->
            match this.Get domain with
            | ValueSome certificate -> ValueTask<SslServerAuthenticationOptions>(certificate.Options)
            | ValueNone ->
                let waiting = this.WaitFor(domain, context.CancellationToken)
                ValueTask<SslServerAuthenticationOptions>(task { let! certificate = waiting in return certificate.Options })

    member _.AddChallenge(token: string, keyAuthorization: string) : unit = challenges[token] <- keyAuthorization
    member _.RemoveChallenge(token: string) : unit = challenges.TryRemove token |> ignore

    member _.Challenge(token: string) : string | null =
        match challenges.TryGetValue token with
        | true, answer -> answer
        | _ -> null

module AcmeEndpoints =
    [<Literal>]
    let Prefix = "/.well-known/acme-challenge/"

    /// `/.well-known/acme-challenge/{token}`: the key authorization as text/plain, 404 for an unknown token or any other path.
    let challenge (store: CertificateStore) : RequestDelegate =
        RequestDelegate(fun http ->
            let path = http.Request.Path.Value |> Option.ofObj |> Option.defaultValue ""
            let answer =
                if path.StartsWith(Prefix, StringComparison.Ordinal) then
                    let token = path.Substring Prefix.Length
                    if token = "" || token.Contains '/' then null else store.Challenge token
                else
                    null
            match answer with
            | null ->
                http.Response.StatusCode <- 404
                Task.CompletedTask
            | answer ->
                let body = Encoding.ASCII.GetBytes answer
                http.Response.ContentType <- "text/plain"
                http.Response.ContentLength <- int64 body.Length
                http.Response.Body.WriteAsync(ReadOnlyMemory<byte> body).AsTask())

[<Sealed>]
type AcmeService
    (
        settings: FrontSettings,
        store: CertificateStore,
        http: IHttpClientFactory,
        lifetime: IHostApplicationLifetime,
        time: TimeProvider,
        logger: ILogger<AcmeService>
    ) =
    inherit BackgroundService()

    let domains = store.Domains
    /// Each domain's renewal jitter (0 to 1 hour), chosen once per certificate so the due time does not move between passes.
    let jitters = Dictionary<string, TimeSpan>()
    let mutable context: AcmeContext | null = null

    let jitterFor (domain: string) : TimeSpan =
        match jitters.TryGetValue domain with
        | true, j -> j
        | _ ->
            let j = TimeSpan.FromTicks(int64 (Random.Shared.NextDouble() * float Renewal.JitterWindow.Ticks))
            jitters[domain] <- j
            j

    let issueTimeout = TimeSpan.FromSeconds 120.0

    /// The account context, made once: the account key from `acme_account+key`, the account registered (or found) and the external
    /// account binding when both variables are set. Left unset if registration fails, so the next pass tries again.
    let accountContext (cancellation: CancellationToken) : Task<AcmeContext> =
        task {
            match context with
            | null ->
                let key = KeyFactory.FromPem(DirCache.accountKeyPem settings.StoragePath)
                let client = http.CreateClient "acme"
                let acme = AcmeContext(Uri settings.AcmeDirectory, key, AcmeHttpClient(Uri settings.AcmeDirectory, client))
                cancellation.ThrowIfCancellationRequested()
                match settings.ExternalAccount with
                | ValueSome(kid, hmac) ->
                    let! _ = acme.NewAccount([||], true, kid, hmac, "HS256")
                    ()
                | ValueNone ->
                    let! _ = acme.NewAccount([||], true)
                    ()
                context <- acme
                return acme
            | acme -> return acme
        }

    /// Polls `order` until it is one of `wanted` (Invalid fails), backing off from 250 ms, for at most `issueTimeout` in all.
    let awaitOrder (order: IOrderContext) (wanted: OrderStatus list) (cancellation: CancellationToken) : Task<unit> =
        task {
            let mutable delay = TimeSpan.FromMilliseconds 250.0
            let mutable waited = TimeSpan.Zero
            let mutable finished = false
            while not finished do
                let! current = order.Resource()
                let status = if current.Status.HasValue then current.Status.Value else OrderStatus.Pending
                if List.contains status wanted then
                    finished <- true
                elif status = OrderStatus.Invalid then
                    failwith "the order became invalid"
                elif waited >= issueTimeout then
                    failwith $"the order was still {status} after {int issueTimeout.TotalSeconds} s"
                else
                    do! Task.Delay(delay, time, cancellation)
                    waited <- waited + delay
                    delay <- TimeSpan.FromMilliseconds(min 2000.0 (delay.TotalMilliseconds * 2.0))
        }

    /// One order for `domain`: account, order, the HTTP-01 challenge answered from the store, a P-256 key and CSR made by .NET,
    /// finalize, download, write the cache file, read it back with the checks of a cached file.
    let orderCertificate (domain: string) (cancellation: CancellationToken) : Task<LoadedCertificate> =
        task {
            let! acme = accountContext cancellation
            let! order = acme.NewOrder [| domain |]
            let tokens = List<string>()
            try
                let! authorizations = order.Authorizations()
                for authorization in authorizations do
                    let! challenge = authorization.Http()
                    store.AddChallenge(challenge.Token, challenge.KeyAuthz)
                    tokens.Add challenge.Token
                    let! _ = challenge.Validate()
                    ()
                do! awaitOrder order [ OrderStatus.Ready; OrderStatus.Valid ] cancellation
            finally
                for token in tokens do
                    store.RemoveChallenge token
            use key = ECDsa.Create ECCurve.NamedCurves.nistP256
            let request = CertificateRequest($"CN={domain}", key, HashAlgorithmName.SHA256)
            let names = SubjectAlternativeNameBuilder()
            names.AddDnsName domain
            request.CertificateExtensions.Add(names.Build false)
            let! _ = order.Finalize(request.CreateSigningRequest())
            do! awaitOrder order [ OrderStatus.Valid ] cancellation
            let! chain = order.Download null
            DirCache.write settings.StoragePath domain key (chain.ToPem null)
            // Read at the time of issue, not of the pass: a CA that does not back-date NotBefore (Pebble) issued it after the pass began.
            match DirCache.read settings.StoragePath domain (time.GetUtcNow()) with
            | Ok certificate -> return certificate
            | Error reason -> return failwith $"the issued certificate is not usable: {reason}"
        }

    /// At start: each `TLS_DOMAIN` name's cached certificate, when it is usable at `now`.
    member _.LoadCached(now: DateTimeOffset) : unit =
        for domain in domains do
            match DirCache.read settings.StoragePath domain now with
            | Ok certificate ->
                store.Set(domain, certificate)
                logger.LogInformation("Loaded the cached certificate for {Domain}, valid until {NotAfter}", domain, certificate.NotAfter)
            | Error reason -> logger.LogInformation("No usable cached certificate for {Domain}: {Reason}", domain, reason)

    /// One pass: orders each domain that has no certificate or is due at `now`, one at a time. Returns when to look again (the earliest
    /// due time; a failed order is retried 30 to 60 minutes later). Exceptions are caught per domain and logged with the domain.
    member _.RenewDueAsync(now: DateTimeOffset, cancellation: CancellationToken) : Task<DateTimeOffset> =
        task {
            let mutable next = now + TimeSpan.FromDays 7.0
            for domain in domains do
                let due =
                    match store.Get domain with
                    | ValueSome certificate -> Renewal.due certificate (jitterFor domain)
                    | ValueNone -> now
                let mutable look = due
                if due <= now then
                    try
                        let! certificate = orderCertificate domain cancellation
                        store.Set(domain, certificate)
                        jitters.Remove domain |> ignore
                        look <- Renewal.due certificate (jitterFor domain)
                        logger.LogInformation("Obtained a certificate for {Domain}, valid until {NotAfter}", domain, certificate.NotAfter)
                    with
                    | :? OperationCanceledException as stopped when cancellation.IsCancellationRequested ->
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(stopped).Throw()
                    | e ->
                        look <- now + Renewal.retryAfterFailure (Random.Shared.NextDouble())
                        logger.LogError(e, "Could not obtain a certificate for {Domain}: {Reason}; retrying at {Retry}", domain, e.Message, look)
                if look < next then next <- look
            return next
        }

    override this.ExecuteAsync(stoppingToken: CancellationToken) : Task =
        task {
            if not domains.IsEmpty then
                this.LoadCached(time.GetUtcNow())
                // The CA validates over HTTP: order only once the listener answers.
                let started = TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
                use _registration = lifetime.ApplicationStarted.Register(fun () -> started.TrySetResult() |> ignore)
                do! started.Task.WaitAsync stoppingToken
                while not stoppingToken.IsCancellationRequested do
                    let now = time.GetUtcNow()
                    let! next = this.RenewDueAsync(now, stoppingToken)
                    let wait = (let left = next - time.GetUtcNow() in if left < TimeSpan.Zero then TimeSpan.Zero elif left > TimeSpan.FromDays 7.0 then TimeSpan.FromDays 7.0 else left)
                    do! Task.Delay(wait, time, stoppingToken)
        }
