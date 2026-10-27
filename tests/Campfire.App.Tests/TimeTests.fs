// plans/design-jobs.md J10 and section 5 (Time, unit 8.1): TM1-TM5. TM5 (the heartbeat under the harness clock) is
// `CableHostTests`' ``the heartbeat beats under the frozen harness clock with the frozen time``.
module Campfire.App.Tests.TimeTests

open System
open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Time.Testing
open Xunit
open Campfire.App.Platform
open Campfire.Db
open Campfire.Tests
open Campfire.App.Tests.Support

[<Fact>]
let ``TM1 HarnessClock.fromValue: unset and blank are the system clock, a timestamp freezes, nonsense names the variable`` () =
    Assert.Same(TimeProvider.System, (HarnessClock.fromValue null |> Result.defaultWith failwith))
    Assert.Same(TimeProvider.System, (HarnessClock.fromValue "" |> Result.defaultWith failwith))
    Assert.Same(TimeProvider.System, (HarnessClock.fromValue "   " |> Result.defaultWith failwith))
    let frozen = HarnessClock.fromValue "2026-03-02T16:00:00Z" |> Result.defaultWith failwith
    Assert.Equal(DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero), frozen.GetUtcNow())
    Thread.Sleep 20
    Assert.Equal(DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero), frozen.GetUtcNow())
    match HarnessClock.fromValue "nonsense" with
    | Error message -> Assert.Contains("CAMPFIRE_FROZEN_TIME", message)
    | Ok _ -> failwith "nonsense must be an error"

[<Fact>]
let ``TM2 a PeriodicTimer on the frozen provider ticks`` () =
    task {
        let frozen = FrozenTimeProvider(DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero)) :> TimeProvider
        use timer = new PeriodicTimer(TimeSpan.FromMilliseconds 20.0, frozen)
        use cancel = new CancellationTokenSource(TimeSpan.FromSeconds 2.0)
        let mutable ticks = 0
        while ticks < 3 && not cancel.IsCancellationRequested do
            let! ticked = timer.WaitForNextTickAsync cancel.Token |> fun t -> t.AsTask()
            if ticked then ticks <- ticks + 1
        Assert.True(ticks >= 3, $"{ticks} ticks")
    }

[<Fact>]
let ``TM3 a message takes created_at from the provider to the microsecond, and an edit takes updated_at from it`` () =
    task {
        let start = DateTimeOffset(2026, 3, 4, 10, 11, 12, 123, TimeSpan.Zero).AddTicks(4560L)
        let time = FakeTimeProvider(start)
        use! test = bootSeededFrozen time "default"
        let write f =
            task {
                use context = test.App.DbFactory.CreateDbContext()
                match! Ef.Writes.run test.App.Gate test.App.Env context f with
                | Ok value -> return value
                | Error error -> return failwith (DbError.display error)
            }
        let room, david = int64 (test.Label "rooms.designers"), int64 (test.Label "users.david")
        let! message =
            write (fun tx ->
                Message.create tx { NewMessage.create room david with ClientMessageId = Some "tm3"; Body = Some "hello" })
        let expected = RailsTime.truncate start.UtcDateTime
        Assert.Equal(expected, message.CreatedAt)
        Assert.Equal(expected, message.UpdatedAt)
        time.Advance(TimeSpan.FromSeconds 1.0)
        let! edited = write (fun tx -> Message.updateBody tx message "edited")
        Assert.Equal(expected, edited.CreatedAt)
        Assert.Equal(expected.AddSeconds 1.0, edited.UpdatedAt)
    }

[<Fact>]
let ``TM4 the session cookie's and the transfer link's expiry follow the provider`` () =
    task {
        let start = DateTimeOffset(2026, 3, 2, 16, 0, 0, TimeSpan.Zero)
        let time = FakeTimeProvider(start)
        use! test = bootSeededFrozen time "default"
        let admin = browser test "198.51.100.40"
        let! _ = admin.Get "/session/new"
        let! signedIn = admin.Form("post", "/session", [ "email_address", test.Label "emails.david"; "password", "secret123456" ])
        Assert.Equal(302, signedIn.Status)
        let header = signedIn.SetCookies |> List.find (fun c -> c.StartsWith "session_token=")
        let expires =
            header.ToLowerInvariant().Split(';') |> Array.map (fun a -> a.Trim()) |> Array.find (fun a -> a.StartsWith "expires=")
        let expires = DateTimeOffset.Parse(expires.Substring "expires=".Length, Globalization.CultureInfo.InvariantCulture)
        Assert.InRange(expires, start.AddYears(20).AddSeconds(-1.0), start.AddYears(20).AddSeconds(1.0))

        let kevin = test.Label "users.kevin"
        let! page = admin.Get $"/users/{kevin}"
        Assert.Equal(200, page.Status)
        let marker = "/session/transfers/"
        let transferId = page.Text.Substring(page.Text.IndexOf marker + marker.Length).Split('"')[0]
        let path = $"/session/transfers/{transferId}"
        let phone = browser test "198.51.100.41"
        time.Advance(TimeSpan.FromHours 4.0 - TimeSpan.FromSeconds 1.0)
        let! valid = phone.Form("put", path, [])
        Assert.Equal(302, valid.Status)
        let late = browser test "198.51.100.42"
        time.Advance(TimeSpan.FromSeconds 2.0)
        let! expired = late.Form("put", path, [])
        Assert.Equal(400, expired.Status)
    }
