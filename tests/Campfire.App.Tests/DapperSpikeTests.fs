/// Spike (bench/results/spike-dapper): are the Dapper reads the same as EF's, and what do they cost?
/// The equivalence test always runs. The measurements run only with CAMPFIRE_SPIKE=1 and write to CAMPFIRE_SPIKE_OUT.
module Campfire.App.Tests.DapperSpikeTests

open System
open System.Diagnostics
open System.IO
open System.Linq
open System.Text.RegularExpressions
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Xunit
open Campfire.App.Tests.Support
open Campfire.Db
open Campfire.Db.Ef

let private seed (test: Test) = test.App.DbFactory.CreateDbContext()

/// Every row of the default seed read by EF (plain LINQ) equals the Dapper read of it, field for field (records compare structurally).
[<Fact>]
let ``dapper reads return the same records as EF for every row of the default seed`` () =
    task {
        use! test = bootSeeded "default"
        use db = seed test
        let users = db.Users.ToList()
        let rooms = db.Rooms.ToList()
        let memberships = db.Memberships.ToList()
        let sessions = db.Sessions.ToList()
        let messages = db.Messages.ToList()
        Assert.True(users.Count > 3 && rooms.Count > 3 && memberships.Count > 3 && messages.Count > 100)
        for u in users do
            Assert.Equal(Some u, FastReads.userById db u.Id)
        for r in rooms do
            Assert.Equal(Some r, FastReads.roomById db r.Id)
        Assert.Equal(Some(rooms.OrderBy(fun r -> r.CreatedAt).First()), FastReads.originalRoom db)
        for m in memberships do
            Assert.Equal(Some m, FastReads.membershipByRoomAndUser db m.RoomId m.UserId)
            Assert.Equal(Some(rooms.First(fun r -> r.Id = m.RoomId)), FastReads.roomForUser db m.UserId m.RoomId)
        Assert.Equal(None, FastReads.roomForUser db DAVID DIRECT_KEVIN_BENDER)
        for s in sessions do
            Assert.Equal(Some s, FastReads.sessionByToken db s.Token)
        Assert.Equal(None, FastReads.sessionByToken db "nope")
        for m in messages do
            Assert.Equal(Some m, FastReads.messageById db m.Id)
        for room in rooms do
            let inRoom = messages.Where(fun m -> m.RoomId = room.Id).OrderByDescending(fun m -> m.CreatedAt).ToList()
            Assert.Equal<Message list>(inRoom.Take(40).Reverse() |> List.ofSeq, FastReads.lastPage db room.Id)
            for m in inRoom do
                let before = inRoom.Where(fun x -> x.CreatedAt < m.CreatedAt).Take(40).Reverse() |> List.ofSeq
                Assert.Equal<Message list>(before, FastReads.pageBefore db room.Id m.CreatedAt)
        Assert.Equal(Some(db.Accounts.OrderBy(fun a -> a.Id).First()), FastReads.accountFirst db)
        // Blob: every attachment's record, through both paths.
        for a in db.Attachments.ToList() do
            let blob = db.Blobs.First(fun b -> b.Id = a.BlobId)
            Assert.Equal(Some blob, FastReads.attachedBlob db a.RecordType a.RecordId a.Name)
        // Nullable times and the involvement nullable survived (some seed rows have them set, some null).
        Assert.Contains(memberships, fun m -> m.UnreadAt.HasValue)
        Assert.Contains(memberships, fun m -> not m.UnreadAt.HasValue)
    }

// ---------------------------------------------------------------------------------------------------------------------

let private spikeOn = Environment.GetEnvironmentVariable "CAMPFIRE_SPIKE" = "1"
let private out (line: string) =
    Console.WriteLine line
    match Environment.GetEnvironmentVariable "CAMPFIRE_SPIKE_OUT" with
    | null | "" -> ()
    | path -> File.AppendAllText(path, line + "\n")

let private loadavg () =
    use p = nonNull (Process.Start(ProcessStartInfo("sysctl", "-n vm.loadavg", RedirectStandardOutput = true)))
    let s = p.StandardOutput.ReadToEnd().Trim()
    p.WaitForExit()
    s

let private name (dapper: bool) = if dapper then "dapper" else "ef"

let private percentile (sorted: float[]) (p: float) = sorted[min (sorted.Length - 1) (int (p * float sorted.Length))]

/// The room page's read set as the request runs it (from the command log, warm fragment cache): session, user, room (the
/// membership join), the original room, the last page, the account (twice), the room again, the account logo.
let private roomPageReads (test: Test) (token: string) (accountId: int64) =
    use db = test.App.DbFactory.CreateDbContext()
    let s = Session.findByToken db token
    let u = User.findById db s.Value.UserId
    let r = Room.findForUser db DAVID ALL_TALK
    let o = Room.original db
    let m = Message.lastPage db ALL_TALK
    let a = Account.first db
    let r2 = Room.findForUser db DAVID ALL_TALK
    let a2 = Account.first db
    let b = Campfire.Storage.Blob.attached db "Account" accountId "logo"
    box (u, r, o, m, a, r2, a2, b)

/// The messages page's read set: session, user, membership, room, the pivot message, the page before it, the room join,
/// the account, the logo.
let private messagesPageReads (test: Test) (token: string) (accountId: int64) (pivot: int64) =
    use db = test.App.DbFactory.CreateDbContext()
    let s = Session.findByToken db token
    let u = User.findById db s.Value.UserId
    let ms = Membership.findByRoomAndUser db ALL_TALK DAVID
    let r = Room.findById db ALL_TALK
    let m = Message.findById db pivot
    let page = Message.pageBefore db ALL_TALK m.Value
    let r2 = Room.findForUser db DAVID ALL_TALK
    let a = Account.first db
    let b = Campfire.Storage.Blob.attached db "Account" accountId "logo"
    box (u, ms, r, page, r2, a, b)

let private timeLoop (iterations: int) (f: unit -> obj | null) =
    for _ in 1..200 do f () |> ignore
    GC.Collect()
    let before = GC.GetAllocatedBytesForCurrentThread()
    let sw = Stopwatch.StartNew()
    for _ in 1..iterations do f () |> ignore
    sw.Stop()
    let bytes = GC.GetAllocatedBytesForCurrentThread() - before
    sw.Elapsed.TotalMicroseconds / float iterations, float bytes / float iterations

[<Fact>]
let ``measure a: the read sets alone`` () =
    task {
        if spikeOn then
            use! test = bootSeeded "default"
            let token, accountId =
                test.Sql(fun c ->
                    use cmd = c.CreateCommand()
                    cmd.CommandText <- "SELECT token FROM sessions WHERE user_id = " + string DAVID + " LIMIT 1"
                    let token = string (nonNull (cmd.ExecuteScalar()))
                    cmd.CommandText <- "SELECT id FROM accounts LIMIT 1"
                    token, Convert.ToInt64(nonNull (cmd.ExecuteScalar())))
            use db = seed test
            let pivot = (Message.lastPage db ALL_TALK).[20].Id
            out $"## a. read sets alone, in process, single thread, default seed, warm. load {loadavg ()}"
            let sets =
                [ "room page (9 statements)", (fun () -> roomPageReads test token accountId)
                  "messages page (9 statements)", (fun () -> messagesPageReads test token accountId pivot) ]
            for (label, f) in sets do
                for round in 1..6 do
                    let results =
                        [ for dapper in [ false; true ] ->
                              FastReads.enabled <- dapper
                              let us, bytes = timeLoop 20000 f
                              dapper, us, bytes ]
                    for (dapper, us, bytes) in results do
                        out $"a | {label} | round {round} | {name dapper} | {us:F1} us/page | {bytes:F0} B/page | load {loadavg ()}"
            FastReads.enabled <- false
    }

let private mask (body: string) =
    let body = Regex.Replace(body, "(name=\"csrf-token\" content=\")[^\"]*", "$1X")
    let body = Regex.Replace(body, "(name=\"authenticity_token\" value=\")[^\"]*", "$1X")
    body

[<Fact>]
let ``measure b: the pages through the pipeline, and byte identity`` () =
    task {
        if spikeOn then
            use! test = bootSeeded "default"
            let b = david test
            let allTalk = Message.lastPage (seed test) ALL_TALK
            let pivot = allTalk.[20]
            let paths =
                [ "room page", $"/rooms/{ALL_TALK}"
                  "messages page", $"/rooms/{ALL_TALK}/messages?before={pivot.Id}" ]
            // Byte identity: EF, Dapper, EF, Dapper (the first of each warms the fragment cache).
            for (label, path) in paths do
                let bodies = ResizeArray<string>()
                for dapper in [ false; true; false; true ] do
                    FastReads.enabled <- dapper
                    let! r = b.Get path
                    Assert.Equal(200, r.Status)
                    bodies.Add(mask r.Text)
                let same = bodies |> Seq.forall (fun x -> x = bodies[0])
                out $"b | identity | {label} | {bodies[0].Length} chars | all four bodies equal after masking csrf: {same}"
                Assert.True(same, label)
            // Sequential, interleaved.
            for (label, path) in paths do
                for round in 1..6 do
                    // Odd rounds run EF first, even rounds Dapper first (A/B, B/A, ...): position in the pair cancels.
                    for dapper in (if round % 2 = 1 then [ false; true ] else [ true; false ]) do
                        FastReads.enabled <- dapper
                        for _ in 1..300 do
                            let! _ = b.Get path
                            ()
                        let n = 2000
                        let times = Array.zeroCreate<float> n
                        let cpu0 = Process.GetCurrentProcess().TotalProcessorTime
                        let alloc0 = GC.GetTotalAllocatedBytes false
                        for i in 0 .. n - 1 do
                            let t = Stopwatch.GetTimestamp()
                            let! _ = b.Get path
                            times[i] <- Stopwatch.GetElapsedTime(t).TotalMicroseconds
                        let cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMicroseconds / float n
                        let alloc = float (GC.GetTotalAllocatedBytes false - alloc0) / float n
                        Array.sortInPlace times
                        out $"b | seq c=1 | {label} | round {round} | {name dapper} | mean {Array.average times:F1} us | p50 {percentile times 0.5:F1} us | cpu {cpu:F1} us/req | alloc {alloc:F0} B/req | load {loadavg ()}"
            FastReads.enabled <- false
    }

[<Fact>]
let ``measure c: sixteen concurrent readers`` () =
    task {
        if spikeOn then
            use! test = bootSeeded "default"
            let browsers = [ for _ in 1..16 -> david test ]
            let paths = [ "room page", $"/rooms/{ALL_TALK}" ]
            for (label, path) in paths do
                for dapper in [ false; true ] do
                    FastReads.enabled <- dapper
                    for br in browsers do
                        let! _ = br.Get path
                        ()
                for round in 1..6 do
                    for dapper in (if round % 2 = 1 then [ false; true ] else [ true; false ]) do
                        FastReads.enabled <- dapper
                        let count = ref 0
                        let stop = DateTime.UtcNow.AddSeconds 3.0
                        let cpu0 = Process.GetCurrentProcess().TotalProcessorTime
                        let sw = Stopwatch.StartNew()
                        let worker (br: Browser) =
                            Task.Run(fun () ->
                                task {
                                    while DateTime.UtcNow < stop do
                                        let! r = br.Get path
                                        if r.Status = 200 then Threading.Interlocked.Increment count |> ignore
                                } :> Task)
                        do! Task.WhenAll(browsers |> List.map worker)
                        let secs = sw.Elapsed.TotalSeconds
                        let cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMicroseconds / float count.Value
                        out $"c | c=16 | {label} | round {round} | {name dapper} | {float count.Value / secs:F0} req/s | cpu {cpu:F1} us/req | load {loadavg ()}"
            FastReads.enabled <- false
    }
