```
date: 2026-10-20T17:16:19-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:be40f80ae22741c8ed2ac316c72151038e0723803960d6536b21ece61626a5a5 2026-10-20T16:58:54.668450683-04:00 built from commit: 3227b1b2c2e47ff758cea61de81313e874805269
repo HEAD: 3227b1b (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 386 [377–403] |
| idle memory.current (MB) | 48.0 [48.0–48.0] |
| idle anon (MB) | 25.0 [24.0–25.0] |
| peak memory.current under load (MB) | 254 [251–254] |
| peak anon under load (MB) | 171 [171–172] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,553 [1,532–1,656] |
| room_show c=1 CPU µs/req | 745 [710–750] |
| room_show c=1 p50 ms | 0.61 [0.58–0.62] |
| room_show c=1 p99 ms | 2.05 [0.96–2.14] |
| room_show c=16 req/s | 5,210 [5,105–5,289] |
| room_show c=16 CPU µs/req | 663 [647–679] |
| room_show c=16 p50 ms | 2.48 [2.43–2.52] |
| room_show c=16 p99 ms | 8.13 [8.00–8.26] |
| messages_page c=1 req/s | 1,412 [1,405–1,470] |
| messages_page c=1 CPU µs/req | 808 [780–810] |
| messages_page c=1 p50 ms | 0.67 [0.65–0.67] |
| messages_page c=1 p99 ms | 2.81 [2.69–2.82] |
| messages_page c=16 req/s | 4,872 [4,824–5,041] |
| messages_page c=16 CPU µs/req | 714 [691–721] |
| messages_page c=16 p50 ms | 2.69 [2.61–2.71] |
| messages_page c=16 p99 ms | 8.28 [7.93–8.56] |
| sidebar c=1 req/s | 1,493 [1,493–1,565] |
| sidebar c=1 CPU µs/req | 766 [737–768] |
| sidebar c=1 p50 ms | 0.62 [0.60–0.62] |
| sidebar c=1 p99 ms | 1.16 [0.89–1.19] |
| sidebar c=16 req/s | 5,234 [4,900–5,527] |
| sidebar c=16 CPU µs/req | 663 [629–707] |
| sidebar c=16 p50 ms | 2.52 [2.39–2.67] |
| sidebar c=16 p99 ms | 8.26 [7.69–9.35] |
| search c=1 req/s | 1,332 [1,313–1,379] |
| search c=1 CPU µs/req | 851 [823–859] |
| search c=1 p50 ms | 0.71 [0.69–0.72] |
| search c=1 p99 ms | 2.89 [2.78–2.91] |
| search c=16 req/s | 4,784 [4,694–4,913] |
| search c=16 CPU µs/req | 733 [718–749] |
| search c=16 p50 ms | 2.79 [2.71–2.84] |
| search c=16 p99 ms | 8.21 [7.97–8.41] |
| post_message c=1 req/s | 897 [873–902] |
| post_message c=1 CPU µs/req | 1,930 [1,900–1,978] |
| post_message c=1 p50 ms | 0.98 [0.97–1.01] |
| post_message c=1 p99 ms | 4.34 [4.33–5.04] |
| post_message c=16 req/s | 1,820 [1,758–1,864] |
| post_message c=16 CPU µs/req | 1,707 [1,669–1,773] |
| post_message c=16 p50 ms | 8.39 [7.97–8.96] |
| post_message c=16 p99 ms | 15.7 [15.0–17.3] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.43 1.79 2.03 | 2.47 2.35 2.22 |
| theDotNetWay | 2 | 1.42 2.10 2.14 | 3.08 2.57 2.33 |
| theDotNetWay | 3 | 1.49 2.22 2.22 | 2.76 2.55 2.35 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.04 | 1.07 | 0/10 | 1.08 |
| theDotNetWay | 2 | 1.06 | 1.07 | 0/10 | 1.10 |
| theDotNetWay | 3 | 1.06 | 1.07 | 0/10 | 1.09 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 162,812 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 152,246 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 161,729 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 147,348 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 64,974 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–58.1] | 3/3 | 0.99 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–60.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 54.0 [30.0–90.1] | 2/3 **←** | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 26,539 | 26,539 | yes |
| theDotNetWay | 1 | run c=1 | 7,181 | 7,181 | yes |
| theDotNetWay | 1 | warm-up c=16 | 54,613 | 54,613 | yes |
| theDotNetWay | 1 | run c=16 | 14,931 | 14,931 | yes |
| theDotNetWay | 1 | **all** | 103,264 | 103,264 | yes |
| theDotNetWay | 2 | warm-up c=1 | 25,446 | 25,446 | yes |
| theDotNetWay | 2 | run c=1 | 6,987 | 6,987 | yes |
| theDotNetWay | 2 | warm-up c=16 | 53,090 | 53,090 | yes |
| theDotNetWay | 2 | run c=16 | 14,079 | 14,079 | yes |
| theDotNetWay | 2 | **all** | 99,602 | 99,602 | yes |
| theDotNetWay | 3 | warm-up c=1 | 26,363 | 26,363 | yes |
| theDotNetWay | 3 | run c=1 | 7,219 | 7,219 | yes |
| theDotNetWay | 3 | warm-up c=16 | 54,443 | 54,443 | yes |
| theDotNetWay | 3 | run c=16 | 14,577 | 14,577 | yes |
| theDotNetWay | 3 | **all** | 102,602 | 102,602 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 37,238 / 416,122 | 200 416,122 | 37,238 | – |
| messages_page | 200 25,427 / 383,844 | 200 383,844 | 25,427 | – |
| sidebar | 200 7,919 / 30,763 | 200 30,763 | 7,919 | – |
| search | 200 16,029 / 149,623 | 200 149,623 | 16,029 | – |
| post_message | – | – | 2,684 | – |
