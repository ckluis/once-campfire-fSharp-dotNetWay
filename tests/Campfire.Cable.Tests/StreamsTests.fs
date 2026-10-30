/// R1-R6: the stream registry and connection index over a fake outbound (plans/design-cable.md section 5).
module Campfire.Cable.Tests.StreamsTests

open System
open System.Collections.Concurrent
open System.Diagnostics
open System.Threading
open System.Threading.Tasks
open Xunit
open Campfire.Cable

[<Sealed>]
type private FakeOutbound(accepts: bool) =
    let frames = ConcurrentQueue<ReadOnlyMemory<byte>>()
    let mutable writes = 0
    member _.Frames = frames.ToArray()
    member _.Writes = Volatile.Read(&writes)
    interface IOutbound with
        member _.TryWrite frame =
            Interlocked.Increment(&writes) |> ignore
            if accepts then frames.Enqueue frame
            accepts
        member _.Close(_, _) = ()

let private sub (o: FakeOutbound) (identifier: string) : StreamSubscriber =
    { Outbound = o :> IOutbound; Identifier = identifier; IdentifierJson = Frames.identifierJson identifier }

let private payload = Payload.ofValue {| n = 1 |}

let private sameInstance (a: ReadOnlyMemory<byte>) (b: ReadOnlyMemory<byte>) = a.Equals b

[<Fact>]
let ``R1 same identifier shares one frame instance, two identifiers get two frames`` () =
    let registry = StreamRegistry()
    let a, b, c = FakeOutbound true, FakeOutbound true, FakeOutbound true
    registry.Add("room", sub a "id-1")
    registry.Add("room", sub b "id-1")
    registry.Add("room", sub c "id-2")
    Assert.Equal(3, registry.Broadcast("room", payload))
    Assert.True(sameInstance a.Frames[0] b.Frames[0])
    Assert.False(sameInstance a.Frames[0] c.Frames[0])
    Assert.Equal(2L, registry.FramesBuilt)

[<Fact>]
let ``R1 subscribers are kept sorted by identifier, ordinal`` () =
    let registry = StreamRegistry()
    let order = ConcurrentQueue<string>()
    let outbound (name: string) =
        { new IOutbound with
            member _.TryWrite _ = order.Enqueue name; true
            member _.Close(_, _) = () }
    for name in [ "b"; "C"; "a"; "B" ] do
        registry.Add("s", { Outbound = outbound name; Identifier = name; IdentifierJson = Frames.identifierJson name })
    registry.Broadcast("s", payload) |> ignore
    Assert.Equal<string[]>([| "B"; "C"; "a"; "b" |], order.ToArray())

[<Fact>]
let ``R2 removing the last subscriber removes the broadcasting`` () =
    let registry = StreamRegistry()
    let a = FakeOutbound true
    let s = sub a "i"
    registry.Add("room", s)
    Assert.Equal(1, registry.Count)
    registry.Remove("room", s)
    Assert.Equal(0, registry.Count)
    registry.Remove("room", s)
    Assert.Equal(0, registry.Broadcast("room", payload))

[<Fact>]
let ``R2 concurrent add, remove and broadcast lose no subscriber`` () =
    let registry = StreamRegistry()
    let keeper = FakeOutbound true
    registry.Add("room", sub keeper "keep")
    let outbounds = Array.init 8 (fun _ -> FakeOutbound true)
    let tasks =
        outbounds
        |> Array.mapi (fun t o ->
            Task.Factory.StartNew(
                (fun () ->
                    let s = sub o $"t{t}"
                    for i in 1..625 do
                        registry.Add("room", s)
                        registry.Broadcast("room", payload) |> ignore
                        registry.Remove("room", s)
                        if i % 2 = 0 then registry.Add("other", s); registry.Remove("other", s)),
                TaskCreationOptions.LongRunning
            ))
    Task.WaitAll tasks
    Assert.Equal(1, registry.Count)
    Assert.Equal(1, registry.Broadcast("room", payload))
    Assert.True(keeper.Writes >= 8 * 625)

[<Fact>]
let ``R3 broadcast counts the subscribers written and a refusing one does not stop the rest`` () =
    let registry = StreamRegistry()
    let ok1, full, ok2 = FakeOutbound true, FakeOutbound false, FakeOutbound true
    registry.Add("room", sub ok1 "a")
    registry.Add("room", sub full "b")
    registry.Add("room", sub ok2 "c")
    Assert.Equal(2, registry.Broadcast("room", payload))
    Assert.Equal(1, ok1.Frames.Length)
    Assert.Equal(1, ok2.Frames.Length)
    Assert.Equal(1, full.Writes)

[<Fact>]
let ``R4 ConnectionIndex by identifier after adds and removes`` () =
    let index = ConnectionIndex()
    let a, b, c = FakeOutbound true :> IOutbound, FakeOutbound true :> IOutbound, FakeOutbound true :> IOutbound
    index.Add(a, "u1")
    index.Add(b, "u1")
    index.Add(c, "u2")
    Assert.Equal(3, Seq.length index.All)
    Assert.Equal(2, index.ByIdentifier("u1").Length)
    Assert.Equal(1, index.ByIdentifier("u2").Length)
    index.Remove(a, "u1")
    Assert.Equal(1, index.ByIdentifier("u1").Length)
    Assert.True(obj.ReferenceEquals(b, index.ByIdentifier("u1")[0]))
    index.Remove(b, "u1")
    index.Remove(c, "u2")
    Assert.True(index.ByIdentifier("u1").IsEmpty)
    Assert.Equal(0, Seq.length index.All)

[<Fact>]
let ``R5 a broadcast to a stream with no subscribers is 0 and builds no frame`` () =
    let registry = StreamRegistry()
    Assert.Equal(0, registry.Broadcast("nobody", payload))
    Assert.Equal(0L, registry.FramesBuilt)

[<Fact>]
let ``R6 ten thousand writes to a full outbound cost under 10 ms`` () =
    let registry = StreamRegistry()
    let full = FakeOutbound false
    registry.Add("room", sub full "a")
    registry.Broadcast("room", payload) |> ignore // warm
    // Best of five: the claim is "never waits", and the suite runs other tests in parallel on a busy host.
    let mutable best = TimeSpan.MaxValue
    let mutable reached = 0
    for _ in 1..5 do
        let watch = Stopwatch.StartNew()
        for _ in 1..10000 do
            reached <- reached + registry.Broadcast("room", payload)
        watch.Stop()
        if watch.Elapsed < best then best <- watch.Elapsed
    Assert.Equal(0, reached)
    Assert.True(best < TimeSpan.FromMilliseconds 10.0, $"best of 5: {best.TotalMilliseconds} ms")
