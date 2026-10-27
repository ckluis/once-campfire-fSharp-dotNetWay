// plans/design-jobs.md J11 (unit 8.6): redaction at the logging boundary. A bot API path carries the bot's key (the credential of
// the bot API) and a webhook URL often holds its secret in the path; Rails logs both as sent, this port logs a keyed hash of them.
// `Microsoft.Extensions.Compliance.Redaction`'s HMAC redactor, keyed from `secret_key_base` through Rails' key generator: stable
// within an install (two lines can still be matched), different between installs. Only bot API paths and failure lines call it.
namespace Campfire.App

open System
open System.Runtime.CompilerServices
open Microsoft.Extensions.Compliance.Classification
open Microsoft.Extensions.Compliance.Redaction
open Microsoft.Extensions.DependencyInjection
open Campfire.Rails

module Logging =
    /// The class of what is redacted (the attributes that would name it are applied by C# generators, so `AddRedaction` is explicit).
    let Secret: DataClassification = DataClassification("campfire", "secret")

    /// Rails' key generator salt for the redaction key (`campfire log redaction`, 64 bytes).
    [<Literal>]
    let Salt = "campfire log redaction"

    let private classes = DataClassificationSet Secret

    /// Registers `IRedactorProvider` with the HMAC redactor for `Secret` under `key`, and the erasing one for anything else.
    let addWithKey (services: IServiceCollection) (key: byte[]) : unit =
        services.AddRedaction(fun builder ->
            builder.SetHmacRedactor((fun (options: HmacRedactorOptions) ->
                options.Key <- Convert.ToBase64String key
                options.KeyId <- 1), classes)
            |> ignore
            builder.SetRedactor<ErasingRedactor>(DataClassificationSet.FromDataClassification DataClassification.Unknown)
            |> ignore)
        |> ignore

    /// `addWithKey` keyed from the install's `secret_key_base`.
    let add (services: IServiceCollection) (secrets: Secrets) : unit =
        addWithKey services (secrets.KeyGenerator.GenerateKey(Salt, 64))

    /// The redactor for `Secret` of a provider (a process's redactors are built once).
    let redactor (provider: IRedactorProvider) : Redactor = provider.GetRedactor classes

    let private byKey = ConditionalWeakTable<Secrets, Redactor>()

    /// The redactor of an install, built once per `Secrets` (jobs have no request services to take it from).
    let forSecrets (secrets: Secrets) : Redactor =
        byKey.GetValue(
            secrets,
            fun secrets ->
                let services = ServiceCollection()
                add services secrets
                use provider = services.BuildServiceProvider()
                redactor (provider.GetRequiredService<IRedactorProvider>())
        )

    /// The stand-in where no redactor is registered (a test host without one): nothing of the value survives.
    let Erased = "[FILTERED]"

    let private redactWith (redactor: Redactor | null) (value: string) : string =
        match redactor with
        | null -> Erased
        | redactor -> redactor.Redact value

    /// The key segment of `/rooms/:room_id/:bot_key/messages...`, the only routes that carry one; ValueNone for any other path. The
    /// literals are matched in any letter case: a bot that spells them otherwise gets a 404, but its key is on that line too.
    let private botKeyRange (path: string) : struct (int * int) voption =
        if not (path.StartsWith("/rooms/", StringComparison.OrdinalIgnoreCase)) then
            ValueNone
        else
            // /rooms/<id>/<key>/messages[.fmt|/...]
            let idEnd = path.IndexOf('/', 7)
            if idEnd < 0 then ValueNone
            else
                let keyEnd = path.IndexOf('/', idEnd + 1)
                if keyEnd < 0 then ValueNone
                else
                    let rest = path.AsSpan(keyEnd + 1)
                    let isMessages =
                        rest.StartsWith("messages", StringComparison.OrdinalIgnoreCase)
                        && (rest.Length = 8 || rest[8] = '/' || rest[8] = '.')
                    if isMessages && keyEnd > idEnd + 1 then ValueSome(struct (idEnd + 1, keyEnd)) else ValueNone

    /// Whether `path` is a bot API path.
    let isBotPath (path: string) : bool = (botKeyRange path).IsSome

    /// `path` with a bot key replaced by its keyed hash (`<key id>:<base64 HMAC>`); other paths as they are.
    let redactBotPath (redactor: Redactor | null) (path: string) : string =
        match botKeyRange path with
        | ValueNone -> path
        | ValueSome(struct (start, stop)) -> String.Concat(path.AsSpan(0, start), (redactWith redactor (path.Substring(start, stop - start))).AsSpan(), path.AsSpan stop)

    /// `message` with every occurrence of `url` (as written, and as a JSON string, as Ruby's URI error quotes it) replaced by its keyed
    /// hash. Webhook URLs often hold the secret in their path, so none of it reaches a failure line.
    let redactUrl (redactor: Redactor | null) (url: string) (message: string) : string =
        if String.IsNullOrEmpty url then
            message
        else
            let hash = redactWith redactor url
            let quoted = Json.generate (Value.String url)
            message.Replace(quoted, Json.generate (Value.String hash)).Replace(url, hash)
