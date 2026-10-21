// Phase 3 (plans/design-cable.md section 3): what the host needs to run the cable server: the heartbeat and the
// shutdown hook as hosted services, and the server itself in DI. `Registry.server` still builds the server at boot
// (3.5 moves its construction here).
module Campfire.App.Channels.CableServices

open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Campfire.Cable

/// The server, the heartbeat (one `ping` per beat for every connection) and `Restart` on `ApplicationStopping`.
let add (services: IServiceCollection) (cable: Cable) : unit =
    services.AddSingleton<Cable>(cable) |> ignore
    services.AddHostedService<CableHeartbeat<CableUser>>(fun _ -> new CableHeartbeat<CableUser>(cable)) |> ignore
    services.AddHostedService<CableShutdown<CableUser>>(fun provider ->
        CableShutdown<CableUser>(cable, provider.GetRequiredService<IHostApplicationLifetime>()))
    |> ignore
