// Phase 3 (plans/design-cable.md section 3, unit 3.3): the channel API: JsonElement params, exceptions as Rails' rescued errors,
// Payload broadcasts, Services, and the channel registry.
module Campfire.Cable.Tests.ChannelTests

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Xunit
open Campfire.Rails
open Campfire.Cable
open Campfire.Cable.Tests.Support

/// Collects every formatted log line.
type private Lines() =
    member val Items = List<string>()

    interface ILogger with
        member _.BeginScope(_: 'S) : IDisposable | null = null
        member _.IsEnabled(_: LogLevel) = true

        member this.Log(_: LogLevel, _: EventId, state: 'S, ex: exn | null, formatter: Func<'S, exn | null, string>) =
            lock this.Items (fun () -> this.Items.Add(formatter.Invoke(state, ex)))

let private lines (log: Lines) : string list = lock log.Items (fun () -> List.ofSeq log.Items)

let private withChannel (registered: Lines -> ServerBuilder<User> -> ServerBuilder<User>) (body: CableServer<User> -> Client -> Lines -> Task) : Task =
    task {
        let log = Lines()
        let server = Server.builder (testOptions ()) cookieAuth identify |> registered log |> ServerBuilder.withLogger log |> ServerBuilder.build
        let! app, address = host server
        try
            let authority = (Uri address).Authority
            match! Client.Open(authority, [ "Origin", $"http://{authority}"; "Cookie", "session_token=1" ]) with
            | Choice2Of2 response -> failwith $"{response.Status}"
            | Choice1Of2 client ->
                use _ = client
                let! welcome = client.NextText()
                Assert.Equal("""{"type":"welcome"}""", welcome)
                do! (body server client log).WaitAsync(TimeSpan.FromSeconds 20.0)
        finally
            app.DisposeAsync().AsTask().Wait()
    }

let private ident (entries: (string * Value) list) : string = Json.generate (Value.Object entries)

let private confirmed (identifier: string) : string =
    $"""{{"identifier":{Json.generate (Value.String identifier)},"type":"confirm_subscription"}}"""

[<Fact>]
let ``C1 params are the decoded identifier and Param reads one value`` () =
    let seen = List<string>()
    withChannel
        (fun _ b ->
            b
            |> ServerBuilder.channel "SeenChannel" (fun () ->
                { Channel.empty with
                    Subscribed =
                        fun sub ->
                            lock seen (fun () ->
                                seen.Add(sub.Params.GetRawText())
                                seen.Add(
                                    match sub.Param "room_id" with
                                    | ValueSome v -> string v.ValueKind
                                    | ValueNone -> "none"
                                )
                                seen.Add(
                                    match sub.Param "absent" with
                                    | ValueSome _ -> "some"
                                    | ValueNone -> "none"
                                ))
                            Task.CompletedTask }))
        (fun _ client _ ->
            task {
                let id = ident [ "channel", Value.String "SeenChannel"; "room_id", Value.Int 7L ]
                do! client.Subscribe id
                let! text = client.NextText()
                Assert.Equal(confirmed id, text)
                Assert.Equal<string list>([ id; "Number"; "none" ], lock seen (fun () -> List.ofSeq seen))
            })

[<Fact>]
let ``C2 Services is a scope of the server's provider (3.5) and Broadcaster is the server`` () =
    let provider = ServiceCollection().AddSingleton<string>("marker").BuildServiceProvider()
    let outcome = TaskCompletionSource<bool * bool>()
    task {
        let mutable theServer: CableServer<User> option = None
        let build =
            Server.builder (testOptions ()) cookieAuth identify
            |> ServerBuilder.withServices provider
            |> ServerBuilder.channel "SeenChannel" (fun () ->
                { Channel.empty with
                    Subscribed =
                        fun sub ->
                            outcome.SetResult(not (obj.ReferenceEquals(sub.Services, provider)) && sub.Services.GetRequiredService<string>() = "marker", obj.ReferenceEquals(sub.Broadcaster, theServer.Value))
                            Task.CompletedTask })
            |> ServerBuilder.build
        theServer <- Some build
        let! app, address = host build
        try
            let authority = (Uri address).Authority
            match! Client.Open(authority, [ "Origin", $"http://{authority}"; "Cookie", "session_token=1" ]) with
            | Choice2Of2 response -> failwith $"{response.Status}"
            | Choice1Of2 client ->
                use _ = client
                let! _ = client.NextText()
                do! client.Subscribe(ident [ "channel", Value.String "SeenChannel" ])
                let! services, broadcaster = outcome.Task.WaitAsync(TimeSpan.FromSeconds 10.0)
                Assert.True services
                Assert.True broadcaster
        finally
            app.DisposeAsync().AsTask().Wait()
    }

[<Fact>]
let ``C3 an exception in subscribed or perform is logged and sends nothing`` () =
    withChannel
        (fun _ b ->
            b
            |> ServerBuilder.channel "BrokenChannel" (fun () ->
                { Channel.empty with
                    Subscribed = fun _ -> failwith "boom in subscribed"
                    Perform =
                        fun action _ _ ->
                            task {
                                do! Task.Yield()
                                if action = "explode" then failwith "boom in perform"
                                return true
                            } })
            |> ServerBuilder.channel "HeartbeatChannel" (fun () -> Channel.empty))
        (fun _ client log ->
            task {
                let broken = ident [ "channel", Value.String "BrokenChannel" ]
                do! client.Subscribe broken
                // Neither confirmed nor rejected; the connection carries on.
                let heartbeat = ident [ "channel", Value.String "HeartbeatChannel" ]
                do! client.Subscribe heartbeat
                let! text = client.NextText()
                Assert.Equal(confirmed heartbeat, text)
                do! client.Perform(broken, Value.Object [ "action", Value.String "explode" ])
                do! client.Perform(broken, Value.Object [ "action", Value.String "fine" ])
                do! client.Subscribe heartbeat // a repeat is ignored without a reply
                do! client.AssertSilent()
                let logged = lines log
                Assert.Contains(logged, fun l -> l.Contains "boom in subscribed")
                Assert.Contains(logged, fun l -> l.Contains "boom in perform")
                Assert.Equal(1, logged |> List.filter (fun l -> l.Contains "boom in perform") |> List.length)
            })

[<Fact>]
let ``C4 perform gets the action, receive when it is null or blank, and the data element`` () =
    let seen = List<string>()
    withChannel
        (fun _ b ->
            b
            |> ServerBuilder.channel "SeenChannel" (fun () ->
                { Channel.empty with
                    Perform =
                        fun action data _ ->
                            lock seen (fun () -> seen.Add($"{action} {data.GetRawText()}"))
                            Task.FromResult true }))
        (fun _ client log ->
            task {
                let id = ident [ "channel", Value.String "SeenChannel" ]
                do! client.Subscribe id
                let! _ = client.NextText()
                do! client.Perform(id, Value.Object [ "action", Value.String "go"; "n", Value.Int 1L ])
                do! client.Perform(id, Value.Object [ "action", Value.Null ])
                do! client.Perform(id, Value.Object [ "action", Value.String "  " ])
                do! client.Perform(id, Value.Object [ "text", Value.String "hi" ])
                // A non-string action is invalid; a non-object payload is invalid data. Neither reaches the channel.
                do! client.Perform(id, Value.Object [ "action", Value.Int 3L ])
                do! client.SendText(Json.generate (Value.Object [ "command", Value.String "message"; "identifier", Value.String id; "data", Value.String "[1]" ]))
                do! client.AssertSilent()
                Assert.Equal<string list>(
                    [ """go {"action":"go","n":1}"""
                      """receive {"action":null}"""
                      """receive {"action":"  "}"""
                      """receive {"text":"hi"}""" ],
                    lock seen (fun () -> List.ofSeq seen)
                )
                let logged = lines log
                Assert.Contains(logged, fun l -> l.Contains "invalid action")
                Assert.Contains(logged, fun l -> l.Contains "invalid data")
            })

[<Fact>]
let ``C5 the registry resolves a class name, with or without the leading colons`` () =
    let registry = ChannelRegistry.create [ "RoomChannel", (fun () -> Channel.empty<int>); "Turbo::StreamsChannel", (fun () -> Channel.empty<int>) ]
    let name (found: struct (string * (unit -> Channel<int>)) voption) =
        match found with
        | ValueSome(struct (n, _)) -> Some n
        | ValueNone -> None
    Assert.Equal(Some "RoomChannel", name (ChannelRegistry.resolve registry "RoomChannel"))
    Assert.Equal(Some "RoomChannel", name (ChannelRegistry.resolve registry "::RoomChannel"))
    Assert.Equal(Some "Turbo::StreamsChannel", name (ChannelRegistry.resolve registry "::Turbo::StreamsChannel"))
    Assert.Equal(None, name (ChannelRegistry.resolve registry "Nope"))
    Assert.Equal(None, name (ChannelRegistry.resolve registry ""))
