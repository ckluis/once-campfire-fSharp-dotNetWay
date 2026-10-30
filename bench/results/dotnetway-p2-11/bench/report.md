```
date: 2026-10-20T15:54:46-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:f2af394f4c97df8bf68d3a1701a7146ff4239da624097bfa3aa7c8b013336c38 2026-10-20T15:52:18.09098661-04:00 built from commit: af923953f08a34c839b3bdb796118d9f860579fc
repo HEAD: af92395 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 375 [372–449] |
| idle memory.current (MB) | 48.0 [48.0–48.0] |
| idle anon (MB) | 25.0 [24.0–25.0] |
| peak memory.current under load (MB) | 256 [256–292] |
| peak anon under load (MB) | 172 [171–172] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,545 [1,541–1,547] |
| room_show c=1 CPU µs/req | 745 [742–749] |
| room_show c=1 p50 ms | 0.61 [0.61–0.62] |
| room_show c=1 p99 ms | 2.19 [1.84–2.20] |
| room_show c=16 req/s | 5,231 [5,131–5,315] |
| room_show c=16 CPU µs/req | 658 [650–672] |
| room_show c=16 p50 ms | 2.45 [2.42–2.50] |
| room_show c=16 p99 ms | 8.16 [7.72–8.20] |
| messages_page c=1 req/s | 1,426 [1,414–1,440] |
| messages_page c=1 CPU µs/req | 802 [793–803] |
| messages_page c=1 p50 ms | 0.66 [0.66–0.67] |
| messages_page c=1 p99 ms | 2.83 [2.79–2.85] |
| messages_page c=16 req/s | 4,947 [4,710–4,977] |
| messages_page c=16 CPU µs/req | 699 [697–734] |
| messages_page c=16 p50 ms | 2.63 [2.62–2.76] |
| messages_page c=16 p99 ms | 8.24 [7.93–9.01] |
| sidebar c=1 req/s | 1,528 [1,519–1,537] |
| sidebar c=1 CPU µs/req | 750 [750–760] |
| sidebar c=1 p50 ms | 0.61 [0.61–0.62] |
| sidebar c=1 p99 ms | 0.94 [0.92–0.97] |
| sidebar c=16 req/s | 5,358 [5,217–5,469] |
| sidebar c=16 CPU µs/req | 647 [633–665] |
| sidebar c=16 p50 ms | 2.44 [2.40–2.53] |
| sidebar c=16 p99 ms | 8.28 [7.94–8.57] |
| search c=1 req/s | 1,351 [1,349–1,370] |
| search c=1 CPU µs/req | 838 [830–840] |
| search c=1 p50 ms | 0.70 [0.70–0.71] |
| search c=1 p99 ms | 2.84 [2.82–2.86] |
| search c=16 req/s | 4,757 [4,732–4,799] |
| search c=16 CPU µs/req | 740 [733–741] |
| search c=16 p50 ms | 2.78 [2.76–2.80] |
| search c=16 p99 ms | 8.36 [8.33–8.54] |
| post_message c=1 req/s | 875 [856–889] |
| post_message c=1 CPU µs/req | 1,977 [1,945–2,002] |
| post_message c=1 p50 ms | 1.00 [0.99–1.00] |
| post_message c=1 p99 ms | 4.46 [4.34–5.10] |
| post_message c=16 req/s | 1,734 [1,731–1,784] |
| post_message c=16 CPU µs/req | 1,780 [1,721–1,786] |
| post_message c=16 p50 ms | 9.08 [8.98–9.19] |
| post_message c=16 p99 ms | 17.4 [16.0–18.5] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.34 1.02 1.02 | 2.68 2.41 1.70 |
| theDotNetWay | 2 | 1.31 2.09 1.62 | 3.17 2.72 2.10 |
| theDotNetWay | 3 | 1.44 2.31 1.99 | 3.05 2.67 2.27 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.07 | 1.07 | 0/10 | 1.10 |
| theDotNetWay | 2 | 1.08 | 1.07 | 0/10 | 1.07 |
| theDotNetWay | 3 | 1.08 | 1.08 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 162,550 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 151,348 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 165,080 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 146,920 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 62,993 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 36.0 [30.0–80.0] | 3/3 | 0.99 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–38.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–52.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 42.0 [34.0–42.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–34.0] | 3/3 | 1.01 |
| theDotNetWay | search | 16 | 30.0 [30.0–80.1] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–60.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.2 [30.1–86.4] | 3/3 | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 25,501 | 25,501 | yes |
| theDotNetWay | 1 | run c=1 | 6,851 | 6,851 | yes |
| theDotNetWay | 1 | warm-up c=16 | 51,273 | 51,273 | yes |
| theDotNetWay | 1 | run c=16 | 13,879 | 13,879 | yes |
| theDotNetWay | 1 | **all** | 97,504 | 97,504 | yes |
| theDotNetWay | 2 | warm-up c=1 | 50,520 | 50,520 | yes |
| theDotNetWay | 2 | run c=1 | 7,114 | 7,114 | yes |
| theDotNetWay | 2 | warm-up c=16 | 137,756 | 137,756 | yes |
| theDotNetWay | 2 | run c=16 | 14,290 | 14,290 | yes |
| theDotNetWay | 2 | **all** | 209,680 | 209,680 | yes |
| theDotNetWay | 3 | warm-up c=1 | 25,859 | 25,859 | yes |
| theDotNetWay | 3 | run c=1 | 6,998 | 6,998 | yes |
| theDotNetWay | 3 | warm-up c=16 | 53,379 | 53,379 | yes |
| theDotNetWay | 3 | run c=16 | 13,861 | 13,861 | yes |
| theDotNetWay | 3 | **all** | 100,097 | 100,097 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 37,238 / 416,122 | 200 416,122 | 37,238 | – |
| messages_page | 200 25,427 / 383,844 | 200 383,844 | 25,427 | – |
| sidebar | 200 7,919 / 30,763 | 200 30,763 | 7,919 | – |
| search | 200 16,029 / 149,623 | 200 149,623 | 16,029 | – |
| post_message | – | – | 2,684 | – |
