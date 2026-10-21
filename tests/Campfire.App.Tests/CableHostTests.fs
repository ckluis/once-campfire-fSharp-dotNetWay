// Phase 3, unit 3.4 (plans/design-cable.md P12 and risks 8-9): the cable server inside the app's real pipeline and host,
// not a host of its own: `UseWebSockets` ahead of routing, the session cookie, and `ApplicationStopping` reaching `Restart`.
module Campfire.App.Tests.CableHostTests

open System
open System.Diagnostics
open System.Net.WebSockets
open System.Text
open System.Threading
open Xunit
open Campfire.App.Tests.Support

let private next (ws: ClientWebSocket) : string * WebSocketMessageType =
    use limit = new CancellationTokenSource(TimeSpan.FromSeconds 10.0)
    let buffer = Array.zeroCreate<byte> 4096
    let r = ws.ReceiveAsync(ArraySegment<byte>(buffer), limit.Token).GetAwaiter().GetResult()
    Encoding.UTF8.GetString(buffer, 0, r.Count), r.MessageType

[<Fact>]
let ``P12 stopping the app's host closes the cable connections with server_restart`` () =
    task {
        use! test = bootSeeded "default"
        let connect () =
            task {
                let ws = new ClientWebSocket()
                ws.Options.AddSubProtocol "actioncable-v1-json"
                ws.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{test.Port}")
                ws.Options.SetRequestHeader("Cookie", davidCookie ())
                do! ws.ConnectAsync(Uri $"ws://127.0.0.1:{test.Port}/cable", CancellationToken.None)
                return ws
            }
        let! sockets = System.Threading.Tasks.Task.WhenAll [| connect (); connect (); connect () |]
        for ws in sockets do
            Assert.Equal("""{"type":"welcome"}""", fst (next ws))
        Assert.Equal(3, test.App.Cable.ConnectionCount)
        let started = Stopwatch.StartNew()
        let stopping = test.Host.StopAsync()
        for ws in sockets do
            // Pings may come first on a slow host; the disconnect is what must arrive.
            let mutable frame, kind = next ws
            while kind = WebSocketMessageType.Text && frame.Contains "\"type\":\"ping\"" do
                let f, k = next ws
                frame <- f
                kind <- k
            Assert.Equal("""{"type":"disconnect","reason":"server_restart","reconnect":true}""", frame)
            let _, closed = next ws
            Assert.Equal(WebSocketMessageType.Close, closed)
            Assert.Equal(Nullable WebSocketCloseStatus.NormalClosure, ws.CloseStatus)
            do! ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
        do! stopping
        Assert.True(started.Elapsed < TimeSpan.FromSeconds 5.0, $"{started.Elapsed}")
        Assert.Equal(0, test.App.Cable.ConnectionCount)
        Assert.Equal(0, test.App.Cable.StreamCount)
        for ws in sockets do
            ws.Dispose()
    }
