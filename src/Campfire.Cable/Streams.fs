// Phase 3 (plans/design-cable.md C6): the stream registry (broadcasting -> subscribers) and the connection index.
//
// `StreamSubscriber` is the design's `Subscriber` (named so in 3.2, beside the old transport's type of that name).
namespace Campfire.Cable

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Collections.Immutable
open System.Threading

/// What the registries need of a connection.
type internal IOutbound =
    /// false: the queue is full, the connection has been aborted (it is gone from the caller's point of view).
    abstract TryWrite: ReadOnlyMemory<byte> -> bool
    abstract Close: DisconnectReason option * reconnect: bool -> unit

type internal StreamSubscriber =
    { Outbound: IOutbound
      Identifier: string
      IdentifierJson: byte[] }

module private Sorted =
    /// Index after the last element whose identifier is <= the new one: equal identifiers stay in arrival order.
    let insert (items: ImmutableArray<StreamSubscriber>) (s: StreamSubscriber) =
        let mutable at = items.Length
        while at > 0 && String.CompareOrdinal(items[at - 1].Identifier, s.Identifier) > 0 do
            at <- at - 1
        items.Insert(at, s)

    let remove (items: ImmutableArray<StreamSubscriber>) (s: StreamSubscriber) =
        let mutable at = -1
        let mutable i = 0
        while at < 0 && i < items.Length do
            if obj.ReferenceEquals(items[i].Outbound, s.Outbound) && String.Equals(items[i].Identifier, s.Identifier) then at <- i
            i <- i + 1
        if at < 0 then items else items.RemoveAt at

[<Sealed>]
type internal StreamRegistry() =
    let streams = ConcurrentDictionary<string, ImmutableArray<StreamSubscriber>>(StringComparer.Ordinal)
    let mutable framesBuilt = 0L

    member _.Add(broadcasting: string, subscriber: StreamSubscriber) : unit =
        let mutable fin = false
        while not fin do
            match streams.TryGetValue broadcasting with
            | true, current -> fin <- streams.TryUpdate(broadcasting, Sorted.insert current subscriber, current)
            | _ -> fin <- streams.TryAdd(broadcasting, ImmutableArray.Create subscriber)

    member _.Remove(broadcasting: string, subscriber: StreamSubscriber) : unit =
        let mutable fin = false
        while not fin do
            match streams.TryGetValue broadcasting with
            | true, current ->
                let next = Sorted.remove current subscriber
                if next.Length = current.Length then fin <- true
                elif next.IsEmpty then fin <- streams.TryRemove(KeyValuePair(broadcasting, current))
                else fin <- streams.TryUpdate(broadcasting, next, current)
            | _ -> fin <- true

    /// One frame per distinct identifier, shared by every subscriber of it. Returns the subscribers written.
    member _.Broadcast(broadcasting: string, payload: Payload) : int =
        match streams.TryGetValue broadcasting with
        | true, subscribers ->
            let mutable reached = 0
            let mutable frame = ReadOnlyMemory<byte>.Empty
            let mutable frameFor: string | null = null
            for s in subscribers do
                if isNull frameFor || not (String.Equals(frameFor, s.Identifier)) then
                    frame <- Frames.message s.IdentifierJson payload
                    frameFor <- s.Identifier
                    Interlocked.Increment(&framesBuilt) |> ignore
                if s.Outbound.TryWrite frame then reached <- reached + 1
            reached
        | _ -> 0

    /// Broadcastings with subscribers (tests).
    member _.Count: int = streams.Count

    /// Frames built by `Broadcast` so far (tests: a stream with no subscribers builds none).
    member _.FramesBuilt: int64 = Volatile.Read(&framesBuilt)

/// Every open connection, and by identifier (remote disconnects, C11).
[<Sealed>]
type internal ConnectionIndex() =
    let all = ConcurrentDictionary<IOutbound, string>(HashIdentity.Reference)
    let byIdentifier = ConcurrentDictionary<string, ImmutableArray<IOutbound>>(StringComparer.Ordinal)

    member _.Add(outbound: IOutbound, identifier: string) : unit =
        all[outbound] <- identifier
        let mutable fin = false
        while not fin do
            match byIdentifier.TryGetValue identifier with
            | true, current -> fin <- byIdentifier.TryUpdate(identifier, current.Add outbound, current)
            | _ -> fin <- byIdentifier.TryAdd(identifier, ImmutableArray.Create outbound)

    member _.Remove(outbound: IOutbound, identifier: string) : unit =
        all.TryRemove outbound |> ignore
        let mutable fin = false
        while not fin do
            match byIdentifier.TryGetValue identifier with
            | true, current ->
                let at = current.IndexOf(outbound, HashIdentity.Reference)
                if at < 0 then fin <- true
                else
                    let next = current.RemoveAt at
                    if next.IsEmpty then fin <- byIdentifier.TryRemove(KeyValuePair(identifier, current))
                    else fin <- byIdentifier.TryUpdate(identifier, next, current)
            | _ -> fin <- true

    member _.All: seq<IOutbound> = all.Keys :> seq<IOutbound>

    member _.ByIdentifier(identifier: string) : ImmutableArray<IOutbound> =
        match byIdentifier.TryGetValue identifier with
        | true, v -> v
        | _ -> ImmutableArray<IOutbound>.Empty

    member _.Count: int = all.Count
