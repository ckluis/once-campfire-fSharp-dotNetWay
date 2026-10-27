// Port of rust/crates/campfire/src/config.rs
//
// Environment configuration (plans/design-jobs.md J13: `IOptions<CampfireOptions>`, filled from the host's configuration, checked by
// `CampfireOptionsValidator` with `ValidateOnStart()`): the env vars the reference reads in production, plus a few
// `CAMPFIRE_*` knobs for things Rails gets from its directory layout.
//
// Reference sources:
// - `SECRET_KEY_BASE`: Rails' `secret_key_base` (required in production; `SECRET_KEY_BASE_DUMMY`
//   makes a throwaway one, as Rails does for asset precompilation).
// - `VAPID_PUBLIC_KEY`, `VAPID_PRIVATE_KEY`: `config/initializers/vapid.rb`. Checked at boot:
//   when either is missing or they aren't a matching P-256 key pair, Web Push is off (logged).
// - `VAPID_SUBJECT`: the contact push services see in the VAPID JWT's `sub` (a `mailto:` or
//   `https:` URL). The reference hardcodes `mailto:support@37signals.com`; this defaults to
//   `https://` and the first `TLS_DOMAIN`, or the project's URL without one.
// - `DISABLE_SSL`: `config/environments/production.rb` (`assume_ssl`/`force_ssl` unless present).
// - `APP_VERSION`, `GIT_REVISION`: `config/initializers/version.rb` (`X-Version`, `X-Rev`).
// - `RAILS_ENV`: names the database file (`storage/db/<env>.sqlite3`, `config/database.yml`).
// - `RAILS_MAX_THREADS`: not read (EF Core's pooled contexts and Microsoft.Data.Sqlite's connection pool size themselves).
// - `JOB_CONCURRENCY`: Resque worker count (`config/puma.rb`), used for job concurrency.
// - `RAILS_LOG_LEVEL`: `config/environments/production.rb` log level.
// - Thruster's (`TLS_DOMAIN`, `HTTP_PORT`, `HTTP_*_TIMEOUT`, `TARGET_PORT`, ...): read by
//   `Web/FrontSettings.fs`, which does Thruster's job in this binary.
// - Not applicable: `REDIS_URL` and `WEB_CONCURRENCY` (no Redis, one process), `PORT` (Puma's;
//   the app listens on Thruster's `TARGET_PORT`), and `SENTRY_DSN` and `SKIP_TELEMETRY` (the app
//   sends no telemetry).
// - `CAMPFIRE_FRAGMENT_CACHE_MB`: the fragment store's limit in megabytes (default 32). The
//   reference caches fragments in Redis (`redis_cache_store`) with no `maxmemory`; this store is
//   in the process, so it's bounded like Rails' `MemoryStore` (default `size` 32 MB), evicting the
//   least recently used fragments. See `Campfire.Views.Fragments`.
// - `CAMPFIRE_FROZEN_TIME` (`Platform/HarnessClock.fs`): pins the clock, for parity runs; a bad value is a validation failure.
// - `CAMPFIRE_LOG`: a `Microsoft.Extensions.Logging` level (`debug`, `info`, ...) that replaces the
//   one `RAILS_LOG_LEVEL` gives (Rust's `CAMPFIRE_LOG` is a tracing filter; see Program.fs).
//
// Storage paths mirror `Rails.root.join("storage")`: the database under `db/`, blobs under
// `files/` (`config/storage.yml`), backups under `backups/` (`script/admin/prepare-backup`).
namespace Campfire.App

open System
open System.IO
open System.Security.Cryptography
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open Campfire.App.Platform
open Campfire.Views

type StoragePaths =
    {
        /// `storage/db/<env>.sqlite3`
        Database: string
        /// The `local` Disk service root, `storage/files`.
        Files: string
        /// `storage/backups`
        Backups: string
    }

module StoragePaths =
    let create (root: string) (environment: string) : StoragePaths =
        { Database = Path.Combine(root, "db", $"{environment}.sqlite3")
          Files = Path.Combine(root, "files")
          Backups = Path.Combine(root, "backups") }

    /// `config/initializers/storage_paths.rb`: `storage/{db,files}` exist after boot.
    let createDirs (paths: StoragePaths) : unit =
        match Path.GetDirectoryName paths.Database with
        | null
        | "" -> ()
        | dbDir -> Directory.CreateDirectory dbDir |> ignore
        Directory.CreateDirectory paths.Files |> ignore

    /// Where `prepare-backup` writes the snapshot: `storage/backups/<database file name>`.
    let backupFile (paths: StoragePaths) : string =
        let name =
            match Path.GetFileName paths.Database with
            | "" -> "production.sqlite3"
            | name -> name
        Path.Combine(paths.Backups, name)

/// The install's settings (`IOptions<CampfireOptions>`). Mutable fields so the options machinery can fill it; `Problem` is the first
/// thing wrong with the configuration (what `CampfireOptionsValidator` reports), which a value that does not fit its field (a
/// non-number) cannot say any other way. Printed (`string`, `%A`, a log template), it shows whether a secret is set, never the secret.
[<CLIMutable; StructuredFormatDisplay("{Display}")>]
type CampfireOptions =
    { mutable SecretKeyBase: string
      mutable VapidPublicKey: string option
      mutable VapidPrivateKey: string option
      /// `VAPID_SUBJECT`, or a default (see the module docs).
      mutable VapidSubject: string
      /// `DISABLE_SSL` present: no `assume_ssl`, no `force_ssl`.
      mutable DisableSsl: bool
      /// `Rails.application.config.app_version`
      mutable AppVersion: string
      /// `Rails.application.config.git_revision`
      mutable GitRevision: string option
      mutable Environment: string
      mutable Storage: StoragePaths
      mutable JobConcurrency: int
      mutable LogLevel: string
      /// The fragment store's limit in bytes (`CAMPFIRE_FRAGMENT_CACHE_MB`).
      mutable FragmentBytes: int64
      /// `CAMPFIRE_FROZEN_TIME`, as set.
      mutable FrozenTime: string option
      mutable Problem: string option }

    /// The fields, with `SECRET_KEY_BASE` and `VAPID_PRIVATE_KEY` as `[FILTERED]` when set.
    member this.Display: string =
        let filtered (value: string option) = value |> Option.map (fun _ -> "[FILTERED]")
        sprintf
            "%A"
            {| SecretKeyBase = (if String.IsNullOrEmpty this.SecretKeyBase then "" else "[FILTERED]")
               VapidPublicKey = this.VapidPublicKey
               VapidPrivateKey = filtered this.VapidPrivateKey
               VapidSubject = this.VapidSubject
               DisableSsl = this.DisableSsl
               AppVersion = this.AppVersion
               GitRevision = this.GitRevision
               Environment = this.Environment
               Storage = this.Storage
               JobConcurrency = this.JobConcurrency
               LogLevel = this.LogLevel
               FragmentBytes = this.FragmentBytes
               FrozenTime = this.FrozenTime
               Problem = this.Problem |}

    override this.ToString() : string = this.Display

module CampfireOptions =
    /// The install's own HTTPS URL when it has a TLS domain; the project's otherwise.
    let private defaultVapidSubject (tlsDomains: string option) : string =
        let domain =
            tlsDomains
            |> Option.bind (fun domains -> domains.Split ',' |> Array.map (fun d -> d.Trim()) |> Array.tryFind (fun d -> d <> ""))
        match domain with
        | Some domain -> $"https://{domain}"
        | None -> "https://github.com/ckluis/once-campfire-fsharp"

    /// One throwaway secret per process (`SECRET_KEY_BASE_DUMMY`), so that every read of the configuration agrees on it.
    let private dummySecret: Lazy<string> = lazy (Convert.ToHexStringLower(RandomNumberGenerator.GetBytes 64))

    /// Rust's `usize::from_str`: an optional `+` and decimal digits.
    let private parseNumber (value: string) : int64 option =
        let digits = if value.StartsWith('+') then value.Substring 1 else value
        if digits.Length > 0 && Seq.forall Char.IsAsciiDigit digits then
            match Int64.TryParse digits with
            | true, n -> Some n
            | _ -> None
        else
            None

    /// Rust's `{value:?}` for a string: quoted, with quotes, backslashes and control characters escaped.
    let private debugString (value: string) : string =
        let out = Text.StringBuilder("\"")
        for c in value do
            match c with
            | '"' -> out.Append "\\\"" |> ignore
            | '\\' -> out.Append "\\\\" |> ignore
            | '\n' -> out.Append "\\n" |> ignore
            | '\r' -> out.Append "\\r" |> ignore
            | '\t' -> out.Append "\\t" |> ignore
            | c when c < ' ' || c = '\127' -> out.Append($"\\u{{{int c:x}}}") |> ignore
            | c -> out.Append c |> ignore
        out.Append('"').ToString()

    /// A record to fill (what `AddOptions` makes through the `CLIMutable` constructor).
    let empty () : CampfireOptions =
        { SecretKeyBase = ""
          VapidPublicKey = None
          VapidPrivateKey = None
          VapidSubject = ""
          DisableSsl = false
          AppVersion = "0"
          GitRevision = None
          Environment = "production"
          Storage = StoragePaths.create "storage" "production"
          JobConcurrency = 2
          LogLevel = "info"
          FragmentBytes = 0L
          FrozenTime = None
          Problem = None }

    /// Fills `options` from any variable lookup (the host's configuration; tests pass a map). A problem is recorded, not thrown: the
    /// first one, in the order Rails' boot meets them.
    let configure (get: string -> string | null) (options: CampfireOptions) : unit =
        let present (name: string) : string option =
            match get name with
            | null -> None
            | value -> if String.IsNullOrWhiteSpace value then None else Some value
        let mutable problem: string option = None
        let fail (message: string) = if problem.IsNone then problem <- Some message
        let number (name: string) (fallback: int64) : int64 =
            match present name with
            | Some value ->
                match parseNumber (value.Trim()) with
                | Some n -> n
                | None ->
                    fail $"{name}={debugString value} is not a number"
                    fallback
            | None -> fallback
        options.SecretKeyBase <-
            match present "SECRET_KEY_BASE" with
            | Some secret -> secret
            | None when (present "SECRET_KEY_BASE_DUMMY").IsSome -> dummySecret.Value
            | None ->
                fail "Missing `secret_key_base` for 'production' environment, set SECRET_KEY_BASE"
                ""
        let environment = defaultArg (present "RAILS_ENV") "production"
        let storageRoot = defaultArg (present "CAMPFIRE_STORAGE_PATH") "storage"
        let defaults = StoragePaths.create storageRoot environment
        let concurrency = number "JOB_CONCURRENCY" 2L
        let megabytes = number "CAMPFIRE_FRAGMENT_CACHE_MB" (int64 (Fragments.DefaultMaxBytes >>> 20))
        options.FrozenTime <- present HarnessClock.Variable
        match HarnessClock.fromValue (get HarnessClock.Variable) with
        | Error message -> fail message
        | Ok _ -> ()
        options.VapidPublicKey <- present "VAPID_PUBLIC_KEY"
        options.VapidPrivateKey <- present "VAPID_PRIVATE_KEY"
        options.VapidSubject <-
            (match present "VAPID_SUBJECT" with
             | Some subject -> subject
             | None -> defaultVapidSubject (present "TLS_DOMAIN"))
        options.DisableSsl <- (present "DISABLE_SSL").IsSome
        options.AppVersion <-
            (match present "APP_VERSION" |> Option.orElse (present "GIT_REVISION") with
             | Some version -> version
             | None -> "0")
        options.GitRevision <- (match get "GIT_REVISION" with null -> None | revision -> Some revision)
        options.Environment <- environment
        options.Storage <-
            { Database = defaultArg (present "CAMPFIRE_DATABASE_PATH") defaults.Database
              Files = defaultArg (present "CAMPFIRE_FILES_PATH") defaults.Files
              Backups = defaultArg (present "CAMPFIRE_BACKUPS_PATH") defaults.Backups }
        options.JobConcurrency <- int (max concurrency 1L)
        options.LogLevel <- defaultArg (present "RAILS_LOG_LEVEL") "info"
        options.FragmentBytes <- megabytes * (1L <<< 20)
        options.Problem <- problem

    /// The options over a lookup, whatever is wrong with them (`Problem` says).
    let read (get: string -> string | null) : CampfireOptions =
        let options = empty ()
        configure get options
        options

    /// The options over a lookup, or what is wrong with them.
    let fromLookup (get: string -> string | null) : Result<CampfireOptions, string> =
        let options = read get
        match options.Problem with
        | Some message -> Error message
        | None -> Ok options

    let fromEnv () : Result<CampfireOptions, string> = fromLookup Environment.GetEnvironmentVariable

    /// The host's configuration as the variable lookup.
    let ofConfiguration (configuration: IConfiguration) : string -> string | null = fun key -> configuration[key]

/// J13: a problem found while filling the options fails validation with its message (Rails' own words).
[<Sealed>]
type CampfireOptionsValidator() =
    interface IValidateOptions<CampfireOptions> with
        member _.Validate(_: string | null, options: CampfireOptions) : ValidateOptionsResult =
            match options.Problem with
            | Some message -> ValidateOptionsResult.Fail message
            | None -> ValidateOptionsResult.Success

module CampfireOptionsRegistration =
    /// `IOptions<CampfireOptions>` (one instance, made on first use), filled from the host's `IConfiguration`, validated, and checked
    /// when the host starts; `CampfireOptions` itself is its value.
    let add (services: IServiceCollection) : unit =
        services
            .AddOptions<CampfireOptions>()
            .Configure<IConfiguration>(fun options configuration -> CampfireOptions.configure (CampfireOptions.ofConfiguration configuration) options)
            .ValidateOnStart()
        |> ignore
        services.AddSingleton<IValidateOptions<CampfireOptions>, CampfireOptionsValidator>() |> ignore
        services.AddSingleton<CampfireOptions>(fun sp -> sp.GetRequiredService<IOptions<CampfireOptions>>().Value) |> ignore
