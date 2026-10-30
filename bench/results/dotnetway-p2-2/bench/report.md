```
date: 2026-10-19T22:11:08-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:bb99ef5c4b832e88b76a9ccd6047e3da32f69f189e88574632f3a3a6947a99b1 2026-10-19T21:56:23.024167934-04:00 built from commit: 2038298fa7306e1472956ca375f634279d0e43c7
repo HEAD: 2038298 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 339 [330–432] |
| idle memory.current (MB) | 47.0 [47.0–47.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 308 [307–309] |
| peak anon under load (MB) | 228 [227–230] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 2,033 [2,016–2,047] |
| room_show c=1 CPU µs/req | 592 [591–593] |
| room_show c=1 p50 ms | 0.47 [0.46–0.47] |
| room_show c=1 p99 ms | 0.68 [0.68–0.78] |
| room_show c=16 req/s | 6,531 [6,292–6,587] |
| room_show c=16 CPU µs/req | 519 [517–538] |
| room_show c=16 p50 ms | 1.94 [1.93–2.00] |
| room_show c=16 p99 ms | 7.17 [7.03–7.70] |
| post_message c=1 req/s | 950 [944–963] |
| post_message c=1 CPU µs/req | 1,861 [1,858–1,877] |
| post_message c=1 p50 ms | 0.92 [0.92–0.93] |
| post_message c=1 p99 ms | 4.10 [4.05–4.88] |
| post_message c=16 req/s | 1,881 [1,779–1,897] |
| post_message c=16 CPU µs/req | 1,632 [1,615–1,743] |
| post_message c=16 p50 ms | 7.58 [7.58–8.46] |
| post_message c=16 p99 ms | 15.3 [14.6–15.7] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.31 1.74 1.59 | 2.82 2.09 1.73 |
| theDotNetWay | 2 | 1.34 1.80 1.65 | 2.93 2.09 1.77 |
| theDotNetWay | 3 | 1.42 1.81 1.69 | 2.74 2.14 1.83 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.04 | 1.05 | 0/4 | 1.08 |
| theDotNetWay | 2 | 1.06 | 1.05 | 0/4 | 1.08 |
| theDotNetWay | 3 | 1.07 | 1.06 | 0/4 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 204,082 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 67,365 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 27,752 | 27,752 | yes |
| theDotNetWay | 1 | run c=1 | 7,550 | 7,550 | yes |
| theDotNetWay | 1 | warm-up c=16 | 55,852 | 55,852 | yes |
| theDotNetWay | 1 | run c=16 | 15,062 | 15,062 | yes |
| theDotNetWay | 1 | **all** | 106,216 | 106,216 | yes |
| theDotNetWay | 2 | warm-up c=1 | 28,093 | 28,093 | yes |
| theDotNetWay | 2 | run c=1 | 7,598 | 7,598 | yes |
| theDotNetWay | 2 | warm-up c=16 | 51,418 | 51,418 | yes |
| theDotNetWay | 2 | run c=16 | 14,257 | 14,257 | yes |
| theDotNetWay | 2 | **all** | 101,366 | 101,366 | yes |
| theDotNetWay | 3 | warm-up c=1 | 28,347 | 28,347 | yes |
| theDotNetWay | 3 | run c=1 | 7,706 | 7,706 | yes |
| theDotNetWay | 3 | warm-up c=16 | 56,784 | 56,784 | yes |
| theDotNetWay | 3 | run c=16 | 15,192 | 15,192 | yes |
| theDotNetWay | 3 | **all** | 108,029 | 108,029 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 24,080 / 416,122 | 200 416,122 | 24,080 | – |
| post_message | – | – | 1,991 | – |
