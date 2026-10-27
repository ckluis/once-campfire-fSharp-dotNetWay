```
date: 2026-10-27T06:58:53-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p8ctl sha256:b4ccd7e5b4d6616956a2ffe4659158b14558ffe63304073e402791035ec7805b 2026-10-27T04:58:07.292450478-04:00 built from commit: 6da5ef7ca37f4a07b9f25af708e2da299bfca1db
dotnetway image: campfire-dotnetway:app sha256:fa4f7372b14c34820d1c79e9fd2a2fe284e6ad815396afdf5672ec5765114441 2026-10-27T06:49:16.291406625-04:00 built from commit: f181944a30303b559fe7e996e8fc657a422201b0
repo HEAD: f181944 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 482 [466–640] | 488 [473–548] |
| idle memory.current (MB) | 83.0 [83.0–83.0] | 84.0 [84.0–84.0] |
| idle anon (MB) | 57.0 [57.0–57.0] | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 373 [371–378] | 382 [381–382] |
| peak anon under load (MB) | 283 [282–284] | 286 [286–287] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| post_message c=1 req/s | 1,087 [984–1,095] | 1,086 [1,082–1,098] |
| post_message c=1 CPU µs/req | 1,691 [1,688–1,827] | 1,768 [1,745–1,779] |
| post_message c=1 p50 ms | 0.82 [0.81–0.86] | 0.82 [0.80–0.82] |
| post_message c=1 p99 ms | 4.10 [4.08–5.08] | 4.13 [4.00–4.16] |
| post_message c=16 req/s | 2,180 [1,952–2,181] | 2,133 [2,132–2,177] |
| post_message c=16 CPU µs/req | 1,483 [1,480–1,677] | 1,503 [1,484–1,506] |
| post_message c=16 p50 ms | 5.89 [5.88–6.76] | 6.01 [5.88–6.02] |
| post_message c=16 p99 ms | 14.5 [14.5–17.6] | 15.0 [14.6–15.2] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 0.24 0.89 0.73 | 2.48 1.40 0.92 |
| F# | 2 | 1.41 1.52 1.10 | 2.33 1.76 1.22 |
| F# | 3 | 1.38 1.58 1.18 | 2.81 1.90 1.32 |
| theDotNetWay | 1 | 1.33 1.24 0.89 | 2.67 1.64 1.06 |
| theDotNetWay | 2 | 1.30 1.42 1.01 | 2.44 1.70 1.14 |
| theDotNetWay | 3 | 1.34 1.64 1.26 | 2.54 1.90 1.39 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.08 | 1.08 | 0/2 | 1.08 |
| F# | 2 | 1.07 | 1.07 | 0/2 | 1.07 |
| F# | 3 | 1.05 | 1.06 | 0/2 | 1.08 |
| theDotNetWay | 1 | 1.06 | 1.05 | 0/2 | 1.06 |
| theDotNetWay | 2 | 1.04 | 1.06 | 0/2 | 1.06 |
| theDotNetWay | 3 | 1.06 | 1.05 | 0/2 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | post_message | 75,873 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 77,699 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| F# | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 21,838 | 21,838 | yes |
| F# | 1 | run c=1 | 7,872 | 7,872 | yes |
| F# | 1 | warm-up c=16 | 56,539 | 56,539 | yes |
| F# | 1 | run c=16 | 15,629 | 15,629 | yes |
| F# | 1 | **all** | 101,878 | 101,878 | yes |
| F# | 2 | warm-up c=1 | 24,686 | 24,686 | yes |
| F# | 2 | run c=1 | 8,698 | 8,698 | yes |
| F# | 2 | warm-up c=16 | 65,492 | 65,492 | yes |
| F# | 2 | run c=16 | 17,462 | 17,462 | yes |
| F# | 2 | **all** | 116,338 | 116,338 | yes |
| F# | 3 | warm-up c=1 | 24,540 | 24,540 | yes |
| F# | 3 | run c=1 | 8,760 | 8,760 | yes |
| F# | 3 | warm-up c=16 | 65,404 | 65,404 | yes |
| F# | 3 | run c=16 | 17,452 | 17,452 | yes |
| F# | 3 | **all** | 116,156 | 116,156 | yes |
| theDotNetWay | 1 | warm-up c=1 | 24,494 | 24,494 | yes |
| theDotNetWay | 1 | run c=1 | 8,784 | 8,784 | yes |
| theDotNetWay | 1 | warm-up c=16 | 65,278 | 65,278 | yes |
| theDotNetWay | 1 | run c=16 | 17,428 | 17,428 | yes |
| theDotNetWay | 1 | **all** | 115,984 | 115,984 | yes |
| theDotNetWay | 2 | warm-up c=1 | 24,391 | 24,391 | yes |
| theDotNetWay | 2 | run c=1 | 8,686 | 8,686 | yes |
| theDotNetWay | 2 | warm-up c=16 | 64,906 | 64,906 | yes |
| theDotNetWay | 2 | run c=16 | 17,067 | 17,067 | yes |
| theDotNetWay | 2 | **all** | 115,050 | 115,050 | yes |
| theDotNetWay | 3 | warm-up c=1 | 24,241 | 24,241 | yes |
| theDotNetWay | 3 | run c=1 | 8,656 | 8,656 | yes |
| theDotNetWay | 3 | warm-up c=16 | 64,585 | 64,585 | yes |
| theDotNetWay | 3 | run c=16 | 17,078 | 17,078 | yes |
| theDotNetWay | 3 | **all** | 114,560 | 114,560 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| post_message | – | – | – | – | 2,494 | 2,494 | – |
