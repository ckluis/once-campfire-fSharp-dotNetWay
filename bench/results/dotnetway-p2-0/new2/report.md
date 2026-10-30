```
date: 2026-10-19T21:15:44-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:2a77212a51436d91accd9b5c8566529618a4d46068464067404bfce49e7c4264 2026-10-19T19:07:37.175154758-04:00 built from commit: 6ef24dab8b9623f78cf880ca1f884209c2607a12
dotnetway build: MOUNTED /var/tmp/campfire-mounted-p2-0 over /opt/campfire (8d5cd20 (dirty files under src/: 0) at 2026-10-20T00:52:05Z)
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
| cold start: docker run → /up 200 (ms) | 382 [350–559] |
| idle memory.current (MB) | 45.0 [45.0–45.0] |
| idle anon (MB) | 23.0 [23.0–23.0] |
| peak memory.current under load (MB) | 304 [299–314] |
| peak anon under load (MB) | 225 [223–230] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| post_message c=1 req/s | 963 [806–968] |
| post_message c=1 CPU µs/req | 1,844 [1,839–2,172] |
| post_message c=1 p50 ms | 0.92 [0.91–0.99] |
| post_message c=1 p99 ms | 4.03 [3.99–4.57] |
| post_message c=16 req/s | 1,875 [1,796–1,893] |
| post_message c=16 CPU µs/req | 1,653 [1,623–1,717] |
| post_message c=16 p50 ms | 8.47 [8.04–8.98] |
| post_message c=16 p99 ms | 16.2 [15.1–17.6] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.11 1.74 1.70 | 2.81 2.16 1.86 |
| theDotNetWay | 2 | 1.40 1.87 1.78 | 2.23 2.05 1.86 |
| theDotNetWay | 3 | 1.29 1.84 1.79 | 2.56 2.05 1.87 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.10 | 1.07 | 1/2 **noisy** | 1.11 |
| theDotNetWay | 2 | 1.04 | 1.05 | 0/2 | 1.09 |
| theDotNetWay | 3 | 1.06 | 1.03 | 0/2 | 1.06 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | post_message | 66,443 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | post_message | 1 | 30.0 [30.0–84.1] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 58.5 [30.1–72.5] | 3/3 | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 42,762 | 42,762 | yes |
| theDotNetWay | 1 | run c=1 | 6,448 | 6,448 | yes |
| theDotNetWay | 1 | warm-up c=16 | 97,787 | 97,787 | yes |
| theDotNetWay | 1 | run c=16 | 14,377 | 14,377 | yes |
| theDotNetWay | 1 | **all** | 161,374 | 161,374 | yes |
| theDotNetWay | 2 | warm-up c=1 | 22,979 | 22,979 | yes |
| theDotNetWay | 2 | run c=1 | 7,702 | 7,702 | yes |
| theDotNetWay | 2 | warm-up c=16 | 87,760 | 87,760 | yes |
| theDotNetWay | 2 | run c=16 | 15,159 | 15,159 | yes |
| theDotNetWay | 2 | **all** | 133,600 | 133,600 | yes |
| theDotNetWay | 3 | warm-up c=1 | 24,588 | 24,588 | yes |
| theDotNetWay | 3 | run c=1 | 7,743 | 7,743 | yes |
| theDotNetWay | 3 | warm-up c=16 | 56,802 | 56,802 | yes |
| theDotNetWay | 3 | run c=16 | 15,014 | 15,014 | yes |
| theDotNetWay | 3 | **all** | 104,147 | 104,147 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| post_message | – | – | 1,991 | – |
