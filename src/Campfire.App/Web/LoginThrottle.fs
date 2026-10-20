// plans/design-kit-auth.md K15 (unit 2.9): `rate_limit to: 10, within: 3.minutes, only: :create` on `SessionsController`,
// with the framework's limiter.
//
// Rails counts hits per `request.remote_ip` in a cache entry that expires `within` after the first hit; that is a fixed
// window per client, which `System.Threading.RateLimiting` has. The limiter is a singleton (one process holds every
// count, as one Redis would) and its windows run on wall-clock time, not the frozen harness clock: a recorded
// difference (README Known differences). It is acquired inside the action, after the before-action chain, which is where
// Rails' `rate_limit` callback runs, so CSRF and `allow_browser` answer first.
namespace Campfire.App.Web

open System
open System.Threading.RateLimiting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module LoginThrottle =
    /// `to: 10`
    [<Literal>]
    let Limit = 10

    /// `within: 3.minutes`
    let Window = TimeSpan.FromMinutes 3.0

    /// A fixed window per remote IP; an idle partition is dropped by the limiter.
    let create () : PartitionedRateLimiter<string> =
        PartitionedRateLimiter.Create<string, string>(fun ip ->
            RateLimitPartition.GetFixedWindowLimiter(
                ip,
                fun _ ->
                    FixedWindowRateLimiterOptions(PermitLimit = Limit, Window = Window, QueueLimit = 0, AutoReplenishment = true)
            ))

    /// Count a hit for `ip`: false once it has made more than `Limit` within the window.
    let hit (http: HttpContext) (ip: string) : bool =
        let limiter = http.RequestServices.GetRequiredService<PartitionedRateLimiter<string>>()
        use lease = limiter.AttemptAcquire ip
        lease.IsAcquired
