```
date: 2026-10-26T15:22:58-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p6ctl sha256:4e33c8f9212bfe29a094ea573b018a507f666a3172bb3f0c1b9d087a903e1189 2026-10-26T08:50:46.642832624-04:00 built from commit: 309ab42
dotnetway image: campfire-dotnetway:app sha256:89ef9d56dae9856bb2345ef6f2dd1338c88b3bcbff07dc27c695eb86db59200a 2026-10-26T12:59:37.344065286-04:00 built from commit: 414713f059dec669e9b469ca72692540b442f056
repo HEAD: 868730e (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message avatar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 476 [467–634] | 513 [511–540] |
| idle memory.current (MB) | 71.0 [70.0–71.0] | 83.0 [83.0–84.0] |
| idle anon (MB) | 45.0 [45.0–45.0] | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 300 [296–338] | 377 [375–383] |
| peak anon under load (MB) | 203 [203–242] | 285 [284–287] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| avatar c=1 req/s | 25,293 [18,883–25,351] | 12,836 [12,830–12,960] |
| avatar c=1 CPU µs/req | 88.0 [87.6–101.8] | 158 [156–159] |
| avatar c=1 p50 ms | 0.04 [0.04–0.04] | 0.08 [0.07–0.08] |
| avatar c=1 p99 ms | 0.09 [0.08–0.12] | 0.13 [0.13–0.13] |
| avatar c=16 req/s | 124,155 [122,049–127,940] | 105,125 [102,919–106,238] |
| avatar c=16 CPU µs/req | 19.4 [19.4–19.9] | 24.9 [24.7–25.0] |
| avatar c=16 p50 ms | 0.12 [0.12–0.12] | 0.13 [0.13–0.13] |
| avatar c=16 p99 ms | 0.28 [0.28–0.28] | 0.39 [0.38–0.39] |
| post_message c=1 req/s | 1,113 [1,076–1,118] | 1,059 [1,018–1,071] |
| post_message c=1 CPU µs/req | 1,654 [1,641–1,683] | 1,750 [1,728–1,817] |
| post_message c=1 p50 ms | 0.78 [0.77–0.78] | 0.84 [0.83–0.88] |
| post_message c=1 p99 ms | 3.92 [3.88–4.73] | 4.19 [4.18–4.21] |
| post_message c=16 req/s | 2,028 [1,963–2,037] | 2,139 [1,990–2,140] |
| post_message c=16 CPU µs/req | 1,480 [1,477–1,534] | 1,515 [1,515–1,650] |
| post_message c=16 p50 ms | 6.94 [6.86–7.72] | 6.06 [6.05–6.63] |
| post_message c=16 p99 ms | 14.6 [14.3–16.1] | 15.0 [14.7–15.7] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 0.54 2.23 2.59 | 2.96 2.64 2.69 |
| F# | 2 | 1.39 2.25 2.50 | 3.35 2.77 2.65 |
| F# | 3 | 1.42 2.32 2.50 | 2.86 2.56 2.56 |
| theDotNetWay | 1 | 1.26 2.21 2.54 | 3.02 2.73 2.69 |
| theDotNetWay | 2 | 1.28 2.28 2.54 | 3.27 2.69 2.65 |
| theDotNetWay | 3 | 1.36 2.19 2.43 | 3.47 2.81 2.63 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.08 | 1.08 | 0/4 | 1.08 |
| F# | 2 | 1.07 | 1.09 | 0/4 | 1.09 |
| F# | 3 | 1.04 | 1.06 | 0/4 | 1.07 |
| theDotNetWay | 1 | 1.04 | 1.07 | 0/4 | 1.07 |
| theDotNetWay | 2 | 1.04 | 1.09 | 0/4 | 1.07 |
| theDotNetWay | 3 | 1.06 | 1.04 | 0/4 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | avatar | 3,549,639 | 0 | 0 | 0 | 0 |
| F# | post_message | 74,725 | 0 | 0 | 0 | 0 |
| theDotNetWay | avatar | 2,823,421 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 75,376 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | avatar | 1 | 30.0 [30.0–42.0] | 3/3 | 1.01 |
| F# | avatar | 16 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| F# | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.01 |
| theDotNetWay | avatar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | avatar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 29,535 | 29,535 | yes |
| F# | 1 | run c=1 | 8,606 | 8,606 | yes |
| F# | 1 | warm-up c=16 | 57,633 | 57,633 | yes |
| F# | 1 | run c=16 | 15,715 | 15,715 | yes |
| F# | 1 | **all** | 111,489 | 111,489 | yes |
| F# | 2 | warm-up c=1 | 30,789 | 30,789 | yes |
| F# | 2 | run c=1 | 8,904 | 8,904 | yes |
| F# | 2 | warm-up c=16 | 61,192 | 61,192 | yes |
| F# | 2 | run c=16 | 16,317 | 16,317 | yes |
| F# | 2 | **all** | 117,202 | 117,202 | yes |
| F# | 3 | warm-up c=1 | 30,490 | 30,490 | yes |
| F# | 3 | run c=1 | 8,944 | 8,944 | yes |
| F# | 3 | warm-up c=16 | 60,924 | 60,924 | yes |
| F# | 3 | run c=16 | 16,239 | 16,239 | yes |
| F# | 3 | **all** | 116,597 | 116,597 | yes |
| theDotNetWay | 1 | warm-up c=1 | 28,093 | 28,093 | yes |
| theDotNetWay | 1 | run c=1 | 8,141 | 8,141 | yes |
| theDotNetWay | 1 | warm-up c=16 | 61,116 | 61,116 | yes |
| theDotNetWay | 1 | run c=16 | 15,927 | 15,927 | yes |
| theDotNetWay | 1 | **all** | 113,277 | 113,277 | yes |
| theDotNetWay | 2 | warm-up c=1 | 29,773 | 29,773 | yes |
| theDotNetWay | 2 | run c=1 | 8,566 | 8,566 | yes |
| theDotNetWay | 2 | warm-up c=16 | 64,566 | 64,566 | yes |
| theDotNetWay | 2 | run c=16 | 17,126 | 17,126 | yes |
| theDotNetWay | 2 | **all** | 120,031 | 120,031 | yes |
| theDotNetWay | 3 | warm-up c=1 | 29,639 | 29,639 | yes |
| theDotNetWay | 3 | run c=1 | 8,472 | 8,472 | yes |
| theDotNetWay | 3 | warm-up c=16 | 64,495 | 64,495 | yes |
| theDotNetWay | 3 | run c=16 | 17,144 | 17,144 | yes |
| theDotNetWay | 3 | **all** | 119,750 | 119,750 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| avatar | 200 3,364 / 3,364 | 200 3,364 / 3,364 | 200 3,364 | 200 3,364 | 3,364 | 3,364 | – |
| post_message | – | – | – | – | 2,494 | 2,494 | – |
