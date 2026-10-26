// plans/design-front.md F14, section 3: the front server's settings. Unit 7.3 needs only the record (the ACME service reads the
// domains, directory, external account and storage); unit 7.4 adds `fromLookup`/`fromEnv` (the THRUSTER_ prefix, Go's parsing).
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
      IdleTimeout: TimeSpan
      ReadTimeout: TimeSpan
      ForwardHeaders: bool
      /// 0 = none.
      MaxRequestBody: int64
      CacheSize: int64
      MaxCacheItemSize: int64
      LogRequests: bool }

    member this.HasTls = not this.TlsDomains.IsEmpty
