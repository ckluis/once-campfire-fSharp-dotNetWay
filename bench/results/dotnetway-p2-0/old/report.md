```
date: 2026-10-19T20:52:05-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:2a77212a51436d91accd9b5c8566529618a4d46068464067404bfce49e7c4264 2026-10-19T19:07:37.175154758-04:00 built from commit: 6ef24dab8b9623f78cf880ca1f884209c2607a12
repo HEAD: 8d5cd20 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 342 [310–384] |
| idle memory.current (MB) | 45.0 [45.0–46.0] |
| idle anon (MB) | 23.0 [23.0–23.0] |
| peak memory.current under load (MB) | 302 [298–307] |
| peak anon under load (MB) | 227 [225–227] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| post_message c=1 req/s | 979 [973–985] |
| post_message c=1 CPU µs/req | 1,829 [1,807–1,835] |
| post_message c=1 p50 ms | 0.90 [0.89–0.91] |
| post_message c=1 p99 ms | 3.98 [3.96–4.73] |
| post_message c=16 req/s | 1,882 [1,871–1,920] |
| post_message c=16 CPU µs/req | 1,641 [1,596–1,644] |
| post_message c=16 p50 ms | 8.10 [7.51–8.60] |
| post_message c=16 p99 ms | 15.0 [14.6–15.9] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.44 0.57 1.08 | 2.42 1.05 1.20 |
| theDotNetWay | 2 | 1.51 0.97 1.17 | 2.35 1.37 1.29 |
| theDotNetWay | 3 | 1.36 1.24 1.25 | 2.55 1.60 1.37 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.07 | 1.05 | 0/2 | 1.08 |
| theDotNetWay | 2 | 1.06 | 1.06 | 0/2 | 1.07 |
| theDotNetWay | 3 | 1.05 | 1.04 | 0/2 | 1.06 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | post_message | 68,918 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 24,664 | 24,664 | yes |
| theDotNetWay | 1 | run c=1 | 7,787 | 7,787 | yes |
| theDotNetWay | 1 | warm-up c=16 | 56,819 | 56,819 | yes |
| theDotNetWay | 1 | run c=16 | 15,374 | 15,374 | yes |
| theDotNetWay | 1 | **all** | 104,644 | 104,644 | yes |
| theDotNetWay | 2 | warm-up c=1 | 24,566 | 24,566 | yes |
| theDotNetWay | 2 | run c=1 | 7,830 | 7,830 | yes |
| theDotNetWay | 2 | warm-up c=16 | 56,616 | 56,616 | yes |
| theDotNetWay | 2 | run c=16 | 14,979 | 14,979 | yes |
| theDotNetWay | 2 | **all** | 103,991 | 103,991 | yes |
| theDotNetWay | 3 | warm-up c=1 | 24,787 | 24,787 | yes |
| theDotNetWay | 3 | run c=1 | 7,879 | 7,879 | yes |
| theDotNetWay | 3 | warm-up c=16 | 57,628 | 57,628 | yes |
| theDotNetWay | 3 | run c=16 | 15,069 | 15,069 | yes |
| theDotNetWay | 3 | **all** | 105,363 | 105,363 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| post_message | – | – | 1,992 | – |
