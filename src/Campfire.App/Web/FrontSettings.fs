// plans/design-front.md F14, section 3: the front server's settings: Thruster's variables with their names (the THRUSTER_ prefix, Go's
// parsing), trimmed to what the host still reads. Phase 8 moves this record to IOptions with the rest of the configuration.
namespace Campfire.App.Web

open System
open System.Net

type FrontSettings =
    { HttpPort: int
      HttpsPort: int
      TargetPort: int
      TargetBind: IPAddress
      /// `TLS_DOMAIN`, comma-separated. Setting it turns TLS on.
      TlsDomains: string list
      AcmeDirectory: string
      /// `EAB_KID` and `EAB_HMAC_KEY` (unpadded base64url), when both are set.
      ExternalAccount: (string * string) voption
      StoragePath: string
      /// `HTTP_IDLE_TIMEOUT` and `HTTP_READ_TIMEOUT`; `TimeSpan.MaxValue` is no timeout (zero, negative, or 49 days and more).
      IdleTimeout: TimeSpan
      ReadTimeout: TimeSpan
      ForwardHeaders: bool
      /// 0 = none.
      MaxRequestBody: int64
      CacheSize: int64
      MaxCacheItemSize: int64
      LogRequests: bool }

    member this.HasTls = not this.TlsDomains.IsEmpty

module FrontSettings =
    [<Literal>]
    let private MB = 1048576L

    /// Let's Encrypt's production directory (`acme.LetsEncryptURL`).
    [<Literal>]
    let LetsEncryptUrl = "https://acme-v02.api.letsencrypt.org/directory"

    /// Go's `strconv.ParseBool`.
    let private parseBool (value: string) : bool voption =
        match value with
        | "1" | "t" | "T" | "TRUE" | "true" | "True" -> ValueSome true
        | "0" | "f" | "F" | "FALSE" | "false" | "False" -> ValueSome false
        | _ -> ValueNone

    /// `strconv.Atoi`: an optional sign and decimal digits, nothing else.
    let private parseInt (value: string) : int64 voption =
        match Int64.TryParse(value, Globalization.NumberStyles.AllowLeadingSign, Globalization.CultureInfo.InvariantCulture) with
        | true, n -> ValueSome n
        | _ -> ValueNone

    /// Rust's `IpAddr::from_str` (the old port's reader, kept): four decimal octets without leading zeros, or an IPv6 address.
    let private parseIp (value: string) : IPAddress voption =
        if value.Contains ':' then
            if value.Contains '%' || value.Contains '[' then
                ValueNone
            else
                match IPAddress.TryParse value with
                | true, ip ->
                    match ip with
                    | null -> ValueNone
                    | ip -> if ip.AddressFamily = Sockets.AddressFamily.InterNetworkV6 then ValueSome ip else ValueNone
                | _ -> ValueNone
        else
            let octets = value.Split '.'
            let valid (octet: string) =
                octet.Length >= 1
                && octet.Length <= 3
                && Seq.forall Char.IsAsciiDigit octet
                && (octet.Length = 1 || octet[0] <> '0')
                && int octet <= 255
            if octets.Length = 4 && Array.forall valid octets then ValueSome(IPAddress(octets |> Array.map byte)) else ValueNone

    /// 49 days is as long as `CancellationTokenSource.CancelAfter` and Kestrel's timers can wait: a timeout of that or more, or of
    /// zero or less (Go: no timeout), is no timeout: `TimeSpan.MaxValue`, Kestrel's own "disabled".
    let private maxTimeoutSeconds = 49L * 24L * 3600L

    /// Thruster's `NewConfig` (`internal/config.go`), trimmed to what the host still reads: every setting is `THRUSTER_<NAME>`, then
    /// `<NAME>`; a value that does not parse is the default. `H2C_ENABLED`, `GZIP_COMPRESSION_*`, `HTTP_WRITE_TIMEOUT` and `DEBUG`
    /// are not read (F14). Over any lookup, so tests pass a map.
    let fromLookup (get: string -> string | null) : FrontSettings =
        let find (key: string) : string voption =
            match get ("THRUSTER_" + key) with
            | null ->
                match get key with
                | null -> ValueNone
                | v -> ValueSome v
            | v -> ValueSome v
        let text (key: string) (fallback: string) = find key |> ValueOption.defaultValue fallback
        let integer (key: string) (fallback: int64) = find key |> ValueOption.bind parseInt |> ValueOption.defaultValue fallback
        let port (key: string) (fallback: int) =
            let n = integer key (int64 fallback)
            if n >= 0L && n <= 65535L then int n else fallback
        let timeout (key: string) (fallback: int64) =
            let s = integer key fallback
            if s <= 0L || s >= maxTimeoutSeconds then TimeSpan.MaxValue else TimeSpan.FromSeconds(float s)
        let boolean (key: string) (fallback: bool) = find key |> ValueOption.bind parseBool |> ValueOption.defaultValue fallback
        let tlsDomains =
            match find "TLS_DOMAIN" with
            | ValueSome v -> v.Split ',' |> Array.map (fun d -> d.Trim()) |> Array.filter (fun d -> d <> "") |> List.ofArray
            | ValueNone -> []
        let external =
            match text "EAB_KID" "", text "EAB_HMAC_KEY" "" with
            | "", _
            | _, "" -> ValueNone
            | kid, key -> ValueSome(kid, key)
        { HttpPort = port "HTTP_PORT" 80
          HttpsPort = port "HTTPS_PORT" 443
          TargetPort = port "TARGET_PORT" 3000
          TargetBind = find "TARGET_BIND" |> ValueOption.bind parseIp |> ValueOption.defaultValue IPAddress.Loopback
          TlsDomains = tlsDomains
          AcmeDirectory = text "ACME_DIRECTORY" LetsEncryptUrl
          ExternalAccount = external
          StoragePath = text "STORAGE_PATH" "./storage/thruster"
          IdleTimeout = timeout "HTTP_IDLE_TIMEOUT" 60L
          ReadTimeout = timeout "HTTP_READ_TIMEOUT" 30L
          ForwardHeaders = boolean "FORWARD_HEADERS" tlsDomains.IsEmpty
          MaxRequestBody = integer "MAX_REQUEST_BODY" 0L
          CacheSize = integer "CACHE_SIZE" (64L * MB)
          MaxCacheItemSize = integer "MAX_CACHE_ITEM_SIZE" MB
          LogRequests = boolean "LOG_REQUESTS" true }

    let fromEnv () : FrontSettings = fromLookup Environment.GetEnvironmentVariable
