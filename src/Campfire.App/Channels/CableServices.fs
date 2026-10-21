// Phase 3 (plans/design-cable.md section 3): what the host needs to run the cable server. The server is a DI singleton built from
// `IOptions<CableOptions>`, the host's scope factory, `TimeProvider` and `IHostApplicationLifetime` (which it hooks
// `ApplicationStopping` on, C11); the heartbeat is a hosted service; `Broadcasts` publishes through the `Publish` guard.
module Campfire.App.Channels.CableServices

open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Campfire.Cable
open Campfire.Cable.Turbo
open Campfire.RailsCompat

/// The channels under their Ruby class names. `RoomMessagesChannel` verifies signed stream names with `streams`, and
/// `Turbo::StreamsChannel` gets it with `RoomStreamsAreAuthorized` prepended.
let channels (secrets: Secrets) : ChannelRegistry<CableUser> =
    let streams = StreamsChannel.create secrets
    let stock = streams |> StreamsChannel.guardedBy RoomMessages.guardedStream
    ChannelRegistry.create
        [ "ApplicationCable::Channel", (fun () -> Channel.empty)
          "HeartbeatChannel", (fun () -> Channel.empty)
          "PresenceChannel", Presence.channel
          "ReadRoomsChannel", (fun () -> ReadRooms.channel)
          "RoomChannel", RoomChannel.channel
          "RoomMessagesChannel", (fun () -> RoomMessages.channel streams)
          "TypingNotificationsChannel", TypingNotifications.channel
          "UnreadRoomsChannel", (fun () -> UnreadRooms.channel)
          StreamsChannelName, (fun () -> StreamsChannel.channel stock) ]

/// Hands the host's provider to the `CableHandle` when the host starts.
[<Sealed>]
type CableLink(handle: CableHandle, provider: System.IServiceProvider) =
    interface IHostedService with
        member _.StartAsync(_: System.Threading.CancellationToken) : System.Threading.Tasks.Task =
            handle.Attach provider
            System.Threading.Tasks.Task.CompletedTask

        member _.StopAsync(_: System.Threading.CancellationToken) : System.Threading.Tasks.Task = System.Threading.Tasks.Task.CompletedTask

/// The server, its heartbeat, the broadcasts, and the link that lets what is built before the host reach them. Needs
/// `Secrets`, `TimeProvider`, `WriteGate`, `Env` and the scoped `CampfireDb` (`Data.addServices`) and the session scheme
/// (`Services.addAuthentication`) in the container.
let add (services: IServiceCollection) (handle: CableHandle) : unit =
    services.AddOptions<CableOptions>() |> ignore
    services.AddSingleton<Cable>(fun provider ->
        Cable(
            provider.GetRequiredService<IOptions<CableOptions>>(),
            channels (provider.GetRequiredService<Secrets>()),
            CableAuth.authenticate,
            CableAuth.recheck,
            Gid.connectionIdentifier,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<System.TimeProvider>(),
            provider.GetRequiredService<IHostApplicationLifetime>(),
            provider.GetRequiredService<ILoggerFactory>().CreateLogger "campfire.cable"
        ))
    |> ignore
    services.AddSingleton<Broadcasts>(fun provider -> Broadcasts(provider.GetRequiredService<Cable>())) |> ignore
    services.AddHostedService<CableHeartbeat<CableUser>>(fun provider -> new CableHeartbeat<CableUser>(provider.GetRequiredService<Cable>())) |> ignore
    services.AddHostedService<CableLink>(fun provider -> CableLink(handle, provider)) |> ignore
