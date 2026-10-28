```
date: 2026-10-27T20:10:36-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p9ctl sha256:bf48cbc93c6ac99a3ba68d98e9923838ff979b6bcfd491e3b1f4f4ab3a75b9a1 2026-10-27T15:06:02.651310211-04:00 built from commit: 7d58f54f9bced52bff995bea5898b306fb5987e7
dotnetway image: campfire-dotnetway:app sha256:a7e4bd51d3f72e24f3cb0ae7b01ad2b1c2f8357905e1d31695b36ffa6c8f73e8 2026-10-27T19:18:27.415914824-04:00 built from commit: 979d360b202c02e910b219b6f2ff9da44ed5813e
repo HEAD: 979d360 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 5; suites: http; http secs 8, concs 1 16, routes post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 5, dotnetway 5. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 467 [465–672] | 457 [439–467] |
| idle memory.current (MB) | 89.0 [89.0–178.0] | 89.0 [89.0–90.0] |
| idle anon (MB) | 62.0 [62.0–62.0] | 62.0 [62.0–62.0] |
| peak memory.current under load (MB) | 377 [375–472] | 376 [373–379] |
| peak anon under load (MB) | 285 [282–285] | 282 [280–286] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| post_message c=1 req/s | 1,094 [1,087–1,136] | 1,122 [1,095–1,152] |
| post_message c=1 CPU µs/req | 1,750 [1,720–1,776] | 1,646 [1,615–1,690] |
| post_message c=1 p50 ms | 0.81 [0.78–0.82] | 0.79 [0.77–0.81] |
| post_message c=1 p99 ms | 4.01 [3.94–4.07] | 3.98 [3.90–4.08] |
| post_message c=16 req/s | 2,166 [2,146–2,200] | 2,198 [2,122–2,221] |
| post_message c=16 CPU µs/req | 1,489 [1,460–1,508] | 1,468 [1,451–1,534] |
| post_message c=16 p50 ms | 5.93 [5.81–6.02] | 5.82 [5.71–6.11] |
| post_message c=16 p99 ms | 14.7 [14.3–14.8] | 14.3 [14.2–14.7] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.37 1.78 1.83 | 3.19 2.23 1.99 |
| F# | 2 | 1.34 1.77 1.85 | 3.32 2.18 1.98 |
| F# | 3 | 1.39 1.84 1.87 | 2.58 2.06 1.95 |
| F# | 4 | 1.31 1.89 1.91 | 2.54 2.08 1.97 |
| F# | 5 | 1.46 1.86 1.90 | 2.84 2.16 2.00 |
| theDotNetWay | 1 | 1.31 1.87 1.88 | 2.62 2.09 1.95 |
| theDotNetWay | 2 | 1.27 1.81 1.86 | 2.26 1.97 1.92 |
| theDotNetWay | 3 | 1.49 1.85 1.88 | 2.65 2.10 1.96 |
| theDotNetWay | 4 | 1.33 1.83 1.87 | 3.19 2.25 2.02 |
| theDotNetWay | 5 | 1.42 1.88 1.91 | 2.88 2.19 2.01 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.06 | 1.07 | 0/2 | 1.05 |
| F# | 2 | 1.06 | 1.04 | 0/2 | 1.08 |
| F# | 3 | 1.07 | 1.07 | 0/2 | 1.06 |
| F# | 4 | 1.05 | 1.05 | 0/2 | 1.06 |
| F# | 5 | 1.05 | 1.08 | 0/2 | 1.06 |
| theDotNetWay | 1 | 1.06 | 1.03 | 0/2 | 1.07 |
| theDotNetWay | 2 | 1.05 | 1.05 | 0/2 | 1.07 |
| theDotNetWay | 3 | 1.06 | 1.05 | 0/2 | 1.06 |
| theDotNetWay | 4 | 1.05 | 1.06 | 0/2 | 1.07 |
| theDotNetWay | 5 | 1.07 | 1.07 | 0/2 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | post_message | 131,340 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 132,542 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | post_message | 1 | 30.0 [30.0–30.0] | 5/5 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–30.1] | 5/5 | 0.99 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 5/5 | 0.99 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 5/5 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 24,482 | 24,482 | yes |
| F# | 1 | run c=1 | 8,698 | 8,698 | yes |
| F# | 1 | warm-up c=16 | 65,094 | 65,094 | yes |
| F# | 1 | run c=16 | 17,177 | 17,177 | yes |
| F# | 1 | **all** | 115,451 | 115,451 | yes |
| F# | 2 | warm-up c=1 | 24,633 | 24,633 | yes |
| F# | 2 | run c=1 | 8,716 | 8,716 | yes |
| F# | 2 | warm-up c=16 | 65,242 | 65,242 | yes |
| F# | 2 | run c=16 | 17,316 | 17,316 | yes |
| F# | 2 | **all** | 115,907 | 115,907 | yes |
| F# | 3 | warm-up c=1 | 25,598 | 25,598 | yes |
| F# | 3 | run c=1 | 9,088 | 9,088 | yes |
| F# | 3 | warm-up c=16 | 66,855 | 66,855 | yes |
| F# | 3 | run c=16 | 17,555 | 17,555 | yes |
| F# | 3 | **all** | 119,096 | 119,096 | yes |
| F# | 4 | warm-up c=1 | 25,051 | 25,051 | yes |
| F# | 4 | run c=1 | 9,072 | 9,072 | yes |
| F# | 4 | warm-up c=16 | 66,309 | 66,309 | yes |
| F# | 4 | run c=16 | 17,614 | 17,614 | yes |
| F# | 4 | **all** | 118,046 | 118,046 | yes |
| F# | 5 | warm-up c=1 | 24,262 | 24,262 | yes |
| F# | 5 | run c=1 | 8,750 | 8,750 | yes |
| F# | 5 | warm-up c=16 | 65,429 | 65,429 | yes |
| F# | 5 | run c=16 | 17,354 | 17,354 | yes |
| F# | 5 | **all** | 115,795 | 115,795 | yes |
| theDotNetWay | 1 | warm-up c=1 | 25,057 | 25,057 | yes |
| theDotNetWay | 1 | run c=1 | 8,969 | 8,969 | yes |
| theDotNetWay | 1 | warm-up c=16 | 65,957 | 65,957 | yes |
| theDotNetWay | 1 | run c=16 | 17,540 | 17,540 | yes |
| theDotNetWay | 1 | **all** | 117,523 | 117,523 | yes |
| theDotNetWay | 2 | warm-up c=1 | 25,359 | 25,359 | yes |
| theDotNetWay | 2 | run c=1 | 9,003 | 9,003 | yes |
| theDotNetWay | 2 | warm-up c=16 | 66,415 | 66,415 | yes |
| theDotNetWay | 2 | run c=16 | 17,691 | 17,691 | yes |
| theDotNetWay | 2 | **all** | 118,468 | 118,468 | yes |
| theDotNetWay | 3 | warm-up c=1 | 24,995 | 24,995 | yes |
| theDotNetWay | 3 | run c=1 | 8,979 | 8,979 | yes |
| theDotNetWay | 3 | warm-up c=16 | 66,021 | 66,021 | yes |
| theDotNetWay | 3 | run c=16 | 17,593 | 17,593 | yes |
| theDotNetWay | 3 | **all** | 117,588 | 117,588 | yes |
| theDotNetWay | 4 | warm-up c=1 | 25,016 | 25,016 | yes |
| theDotNetWay | 4 | run c=1 | 8,764 | 8,764 | yes |
| theDotNetWay | 4 | warm-up c=16 | 63,860 | 63,860 | yes |
| theDotNetWay | 4 | run c=16 | 16,998 | 16,998 | yes |
| theDotNetWay | 4 | **all** | 114,638 | 114,638 | yes |
| theDotNetWay | 5 | warm-up c=1 | 25,380 | 25,380 | yes |
| theDotNetWay | 5 | run c=1 | 9,215 | 9,215 | yes |
| theDotNetWay | 5 | warm-up c=16 | 66,220 | 66,220 | yes |
| theDotNetWay | 5 | run c=16 | 17,790 | 17,790 | yes |
| theDotNetWay | 5 | **all** | 118,605 | 118,605 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| post_message | – | – | – | – | 2,494 | 2,494 | – |
