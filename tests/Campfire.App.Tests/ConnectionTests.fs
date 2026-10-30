/// Design: plans/design-perf.md section 1 P3, unit 10.2. One physical SQLite open per request (`Web.Data.db` opens the request's
/// context connection once, on first use), and a Cable connection's scope holds none while the socket idles.
module Campfire.App.Tests.ConnectionTests

open System
open System.Collections.Concurrent
open System.Data.Common
open System.Net.WebSockets
open System.Threading
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Diagnostics
open Xunit
open Campfire.App.Tests.Support
open Campfire.Db

/// EF's `ConnectionOpened` and `ConnectionClosed` (fired only for a physical open and close of the `DbConnection`), by context.
type private Opens() =
    inherit DbConnectionInterceptor()
    let opened = ConcurrentQueue<Guid>()
    let mutable outstanding = 0
    member _.Opened = opened.ToArray()
    member _.Outstanding = Volatile.Read(&outstanding)
    member _.Total = opened.Count

    static member private Id(data: ConnectionEventData) : Guid =
        match data.Context with
        | null -> Guid.Empty
        | context -> context.ContextId.InstanceId

    override _.ConnectionOpened(_, data) =
        opened.Enqueue(Opens.Id data)
        Interlocked.Increment(&outstanding) |> ignore

    override _.ConnectionOpenedAsync(_, data, _) =
        opened.Enqueue(Opens.Id data)
        Interlocked.Increment(&outstanding) |> ignore
        Task.CompletedTask

    override _.ConnectionClosed(_, _) = Interlocked.Decrement(&outstanding) |> ignore

    override _.ConnectionClosedAsync(_, _) =
        Interlocked.Decrement(&outstanding) |> ignore
        Task.CompletedTask

    member this.Attach(builder: DbContextOptionsBuilder<Ef.CampfireDb>) : unit = builder.AddInterceptors(this) |> ignore

let private bootCounting (opens: Opens) = bootSeededWith opens.Attach "default"

/// DC1: every page of the hot paths opens one physical connection for the request (before P3, one per statement).
[<Fact>]
let ``DC1 a request opens one physical connection on the four pages and a post`` () =
    task {
        let opens = Opens()
        use! test = bootCounting opens
        let b = anonymous test
        do! b.SignIn(test.Label "emails.david")
        let room = ALL_TALK
        let! _ = b.Get $"/rooms/{room}"
        // The sign-in and the first page may start background work (jobs): let it settle before counting.
        do! Task.Delay 800
        let measure (name: string) (send: unit -> Task<Reply>) =
            task {
                let from = opens.Total
                let! reply = send ()
                Assert.True(reply.Status >= 200 && reply.Status < 400, $"{name} answered {reply.Status}")
                let mine = opens.Opened |> Array.skip from
                // The request's own context opens once; whatever a post queued runs on contexts of its own, after the reply.
                // `countBy` keeps first-seen order: the request's context opens before anything the post queued.
                let byContext = mine |> Array.countBy id
                return (name, byContext |> Array.map snd)
            }
        let! pages =
            task {
                let! a = measure "room page" (fun () -> b.Get $"/rooms/{room}")
                let! m = measure "messages page" (fun () -> b.Get $"/rooms/{room}/messages")
                let! s = measure "sidebar" (fun () -> b.Get "/users/me/sidebar")
                let! q = measure "search" (fun () -> b.Get "/searches?q=coffee")
                return [ a; m; s; q ]
            }
        for (name, counts) in pages do
            let shown = String.Join(",", counts)
            Assert.True((counts = [| 1 |]), $"{name}: opens per context {shown}, expected exactly one context opened once")
        let! (name: string, counts: int[]) =
            measure "post a message" (fun () ->
                b.Write(((request "POST" $"/rooms/{room}/messages").With("accept", "text/vnd.turbo-stream.html, text/html")).Form(formBody [ "message[body]", "dc1" ])))
        Assert.True(counts.Length > 0 && counts[0] = 1, $"{name}: the request's context opened {(if counts.Length > 0 then counts[0] else 0)} times")
    }

/// DC2: idle WebSockets hold no open connection (a Cable scope lives as long as its socket, so the request's connection is
/// opened lazily, never at the scope's creation).
[<Fact>]
let ``DC2 idle websockets hold no open connection`` () =
    task {
        let opens = Opens()
        use! test = bootCounting opens
        let connect () =
            task {
                let ws = new ClientWebSocket()
                ws.Options.AddSubProtocol "actioncable-v1-json"
                ws.Options.SetRequestHeader("Origin", $"http://127.0.0.1:{test.Port}")
                ws.Options.SetRequestHeader("Cookie", davidCookie ())
                do! ws.ConnectAsync(Uri $"ws://127.0.0.1:{test.Port}/cable", CancellationToken.None)
                return ws
            }
        let before = opens.Outstanding
        let! sockets = Task.WhenAll [| connect (); connect (); connect (); connect () |]
        do! Task.Delay 1000
        Assert.Equal(4, test.App.Cable.ConnectionCount)
        Assert.True(opens.Outstanding <= before, $"{opens.Outstanding - before} connections held open by 4 idle sockets")
        for ws in sockets do
            do! ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None)
            ws.Dispose()
    }
