/// Design: plans/design-data-ef.md section 5, H1-H3 (unit 1.10), under the 2026-10-19 LINQ-first revision.
///
/// The five workloads of `bench/quick` (room page, messages page, sidebar, search, post a message) are requested through the
/// app's whole pipeline over the `default` seed, with EF's query-compilation counter and command log attached to the app's own
/// context options (`Support.bootSeededWith`).
module Campfire.App.Tests.HotPathTests

open System
open System.Collections.Concurrent
open System.IO
open System.Threading
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Diagnostics
open Microsoft.Extensions.Logging
open Xunit
open Xunit.Sdk
open Campfire.App.Tests.Support
open Campfire.Db

type private Probe() =
    let mutable compilations = 0
    let commands = ConcurrentQueue<string>()
    member _.Compilations = Volatile.Read(&compilations)
    member _.Commands = commands.ToArray()
    /// A Dapper statement, in the shape of EF's "Executed DbCommand" entry (the log line is the text after a newline).
    member _.AddRead(sql: string) = commands.Enqueue("Executed DbCommand (Dapper)\n" + sql)

    member this.Attach(builder: DbContextOptionsBuilder<Ef.CampfireDb>) : unit =
        builder.LogTo(
            Action<string>(fun message ->
                if message.Contains "Executed DbCommand" then commands.Enqueue message
                else Interlocked.Increment(&compilations) |> ignore),
            [| CoreEventId.QueryCompilationStarting
               RelationalEventId.CommandExecuted |],
            LogLevel.Debug
        )
        |> ignore

/// Boots the app with EF's command log attached, and lists Dapper's hybrid reads (Ef/FastReads.fs) in the same log.
let private boot (probe: Probe) =
    task {
        let! test = bootSeededWith probe.Attach "default"
        use db = test.App.DbFactory.CreateDbContext()
        Ef.FastReads.logTo (db.Database.GetDbConnection().DataSource) (Action<string>(fun sql -> probe.AddRead sql))
        return test
    }

let private messageIds (test: Test) (roomId: int64) : int64 list =
    use context = test.App.DbFactory.CreateDbContext()
    Message.forRoom context roomId |> List.sortBy (fun m -> m.CreatedAt, m.Id) |> List.map (fun m -> m.Id)

let private signedIn (test: Test) (who: string) : Task<Browser> =
    task {
        let b = anonymous test
        do! b.SignIn(test.Label $"emails.{who}")
        return b
    }

/// One user's pass over the five workloads; each returns its workload name, status and the statements it ran.
let private pass (probe: Probe) (b: Browser) (room: int64) (before: int64) (query: string) (body: string) =
    task {
        let results = ResizeArray<string * int * string[]>()
        let run (name: string) (send: unit -> Task<Reply>) =
            task {
                let from = probe.Commands.Length
                let! reply = send ()
                results.Add((name, reply.Status, probe.Commands |> Array.skip from))
            }
        do! run "room page" (fun () -> b.Get $"/rooms/{room}")
        do! run "messages page" (fun () -> b.Get $"/rooms/{room}/messages?before={before}")
        do! run "sidebar" (fun () -> b.Get "/users/me/sidebar")
        do! run "search" (fun () -> b.Get $"/searches?q={query}")
        do!
            run "post a message" (fun () ->
                b.Write(((request "POST" $"/rooms/{room}/messages").With("accept", "text/vnd.turbo-stream.html, text/html")).Form(formBody [ "message[body]", body ])))
        return List.ofSeq results
    }

let private firstLine (command: string) : string =
    let at = command.IndexOf '\n'
    (if at < 0 then command else command.Substring(at + 1)).Replace("\r", "").Replace("\n", " ").Trim()

/// H1: after one round, a round with other users, rooms, pages, queries and bodies compiles no query.
[<Fact>]
let ``H1 steady state compiles no query on the five workloads`` () =
    task {
        let probe = Probe()
        use! test = boot probe
        // Two sessions per user: the second round's session tokens are values the first round never saw.
        let! david = signedIn test "david"
        let! jason = signedIn test "jason"
        let! david2 = signedIn test "david"
        let! jason2 = signedIn test "jason"
        let allTalk = messageIds test ALL_TALK
        let direct = messageIds test DIRECT_DAVID_JASON
        Assert.True(allTalk.Length > 40 && direct.Length > 2, $"seed: {allTalk.Length} and {direct.Length} messages")

        let! first1 = pass probe david ALL_TALK allTalk[30] "coffee" "first round, david"
        let! first2 = pass probe jason DIRECT_DAVID_JASON direct[2] "hello" "first round, jason"
        for (name, status, _) in first1 @ first2 do
            Assert.True(status >= 200 && status < 400, $"{name} answered {status}")
        // The jobs the posts queued (broadcasts, push) run on their own threads: let them compile what they need.
        do! Task.Delay 1500
        let warm = probe.Compilations
        Assert.True((warm > 0), "the probe saw no compilation in the first round: it is not attached")

        let! second1 = pass probe jason2 ALL_TALK allTalk[20] "meeting" "second round, jason"
        let! second2 = pass probe david2 DIRECT_DAVID_JASON direct[1] "world" "second round, david"
        do! Task.Delay 1500
        for (name, status, _) in second1 @ second2 do
            Assert.True(status >= 200 && status < 400, $"{name} answered {status}")
        let compiled = probe.Compilations - warm
        Assert.True((compiled = 0), $"{compiled} query compilations in the second round (a captured local in a LINQ query?)")
    }

/// H2 (revised): the command log of each workload is recorded (`CAMPFIRE_HOTPATH_LOG` names a file) and sane: every workload
/// ran statements, and no statement carries a user-supplied value (the post body, the search text) as a literal.
[<Fact>]
let ``H2 each workload's statements are listed and carry no literal user value`` () =
    task {
        let probe = Probe()
        use! test = boot probe
        let! david = signedIn test "david"
        let allTalk = messageIds test ALL_TALK
        let! _warm = pass probe david ALL_TALK allTalk[30] "coffee" "warm up"
        let! results = pass probe david ALL_TALK allTalk[20] "meeting" "literal-marker-body"
        let log = Text.StringBuilder()
        for (name, status, commands) in results do
            Assert.True(status >= 200 && status < 400, $"{name} answered {status}")
            Assert.True(commands.Length > 0, $"{name} ran no statements")
            log.AppendLine $"## {name}: {commands.Length} statements" |> ignore
            for c in commands do
                log.AppendLine $"- {firstLine c}" |> ignore
                Assert.DoesNotContain("literal-marker-body", c)
        match Environment.GetEnvironmentVariable "CAMPFIRE_HOTPATH_LOG" with
        | null | "" -> ()
        | path -> File.WriteAllText(path, log.ToString())
    }

/// H3: 16 concurrent posts for 3 s: every 2xx has its row; posts/s is printed (the floor's early warning is 1,650 on the gate's
/// host; the number is recorded, not asserted: the test runs beside every other suite, through the harness's raw sockets; `bench/quick` decides the floor).
[<Fact>]
let ``H3 sixteen concurrent posts all land`` () =
    task {
        let probe = Probe()
        use! test = boot probe
        let workers = [ for _ in 1..16 -> david test ]
        let before = (messageIds test ALL_TALK).Length
        let deadline = DateTime.UtcNow.AddSeconds 3.0
        let started = Diagnostics.Stopwatch.StartNew()
        let ok = ref 0
        let bad = ref 0
        let worker (i: int) (b: Browser) =
            Task.Run(fun () ->
                task {
                    let mutable n = 0
                    while DateTime.UtcNow < deadline do
                        n <- n + 1
                        let! reply =
                            b.Write(
                                ((request "POST" $"/rooms/{ALL_TALK}/messages").With("accept", "text/vnd.turbo-stream.html, text/html"))
                                    .Form(formBody [ "message[body]", $"h3 {i} {n}" ])
                            )
                        if reply.Status >= 200 && reply.Status < 300 then Interlocked.Increment ok |> ignore
                        else Interlocked.Increment bad |> ignore
                } :> Task)
        do! Task.WhenAll(workers |> List.mapi worker)
        let seconds = started.Elapsed.TotalSeconds
        let after = (messageIds test ALL_TALK).Length
        let perSecond = float ok.Value / seconds
        let line = $"H3: {ok.Value} posts in {seconds:F1} s = {perSecond:F0} posts/s ({bad.Value} not 2xx)"
        Console.WriteLine line
        match Environment.GetEnvironmentVariable "CAMPFIRE_HOTPATH_LOG" with
        | null | "" -> ()
        | path -> File.WriteAllText(path + ".h3.txt", line + "\n")
        Assert.True(ok.Value > 0)
        Assert.Equal(0, bad.Value)
        Assert.Equal(before + ok.Value, after)
    }
