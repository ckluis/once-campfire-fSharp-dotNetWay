// Phase 3 (plans/design-cable.md section 3): the settings the Cable server reads, bound from configuration in 3.4.
namespace Campfire.Cable

open System

/// `config.action_cable.*` plus the transport's limits (C5, C6, C7, C8, C17).
[<AllowNullLiteral>]
type CableOptions() =
    member val DisableRequestForgeryProtection = false with get, set
    member val AllowedRequestOrigins: string[] = [||] with get, set
    /// Frames queued per connection before it counts as a slow consumer and is aborted (C6).
    member val QueueCapacity = 256 with get, set
    member val SendTimeout = TimeSpan.FromSeconds 30.0 with get, set
    member val CloseTimeout = TimeSpan.FromSeconds 5.0 with get, set
    member val BeatInterval = TimeSpan.FromSeconds 3.0 with get, set
    member val MaxSubscriptions = 64 with get, set
    member val MaxIdentifierBytes = 4096 with get, set
    member val MaxMessageBytes = 1048576 with get, set
