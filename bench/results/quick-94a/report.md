```
date: 2026-10-27T19:49:21-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p8ctl sha256:9331c8d49e17964fb0e5418b9029af31b7b76faaf03209792d02df4af6e10777 2026-10-27T19:17:45.913376821-04:00 built from commit: 6da5ef7ca37f4a07b9f25af708e2da299bfca1db
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
| cold start: docker run → /up 200 (ms) | 482 [463–698] | 475 [461–498] |
| idle memory.current (MB) | 83.0 [83.0–154.0] | 90.0 [89.0–90.0] |
| idle anon (MB) | 57.0 [57.0–57.0] | 62.0 [62.0–62.0] |
| peak memory.current under load (MB) | 378 [376–454] | 377 [375–378] |
| peak anon under load (MB) | 284 [284–285] | 283 [282–286] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| post_message c=1 req/s | 1,120 [1,090–1,132] | 1,107 [1,095–1,124] |
| post_message c=1 CPU µs/req | 1,673 [1,653–1,692] | 1,665 [1,652–1,680] |
| post_message c=1 p50 ms | 0.79 [0.78–0.80] | 0.80 [0.79–0.81] |
| post_message c=1 p99 ms | 4.01 [3.99–4.72] | 4.04 [3.97–4.08] |
| post_message c=16 req/s | 2,179 [2,160–2,189] | 2,199 [2,187–2,213] |
| post_message c=16 CPU µs/req | 1,474 [1,470–1,481] | 1,468 [1,464–1,487] |
| post_message c=16 p50 ms | 5.89 [5.84–5.95] | 5.84 [5.78–5.91] |
| post_message c=16 p99 ms | 14.7 [14.3–14.9] | 14.2 [14.2–14.5] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.34 1.88 1.85 | 2.58 2.12 1.93 |
| F# | 2 | 1.30 1.91 1.91 | 2.75 2.15 1.99 |
| F# | 3 | 1.38 1.86 1.90 | 2.42 2.01 1.94 |
| F# | 4 | 1.28 1.71 1.82 | 2.60 1.97 1.90 |
| F# | 5 | 1.27 1.71 1.82 | 2.82 2.05 1.92 |
| theDotNetWay | 1 | 1.30 1.84 1.84 | 2.99 2.25 1.99 |
| theDotNetWay | 2 | 1.45 1.95 1.90 | 3.05 2.29 2.02 |
| theDotNetWay | 3 | 1.40 1.80 1.87 | 1.99 1.85 1.88 |
| theDotNetWay | 4 | 1.39 1.72 1.84 | 2.54 1.97 1.91 |
| theDotNetWay | 5 | 1.33 1.76 1.83 | 2.73 2.05 1.92 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.05 | 1.05 | 0/2 | 1.07 |
| F# | 2 | 1.06 | 1.04 | 0/2 | 1.09 |
| F# | 3 | 1.04 | 1.06 | 0/2 | 1.08 |
| F# | 4 | 1.07 | 1.08 | 0/2 | 1.09 |
| F# | 5 | 1.02 | 1.05 | 0/2 | 1.08 |
| theDotNetWay | 1 | 1.04 | 1.04 | 0/2 | 1.07 |
| theDotNetWay | 2 | 1.05 | 1.08 | 0/2 | 1.07 |
| theDotNetWay | 3 | 1.07 | 1.08 | 0/2 | 1.07 |
| theDotNetWay | 4 | 1.05 | 1.07 | 0/2 | 1.05 |
| theDotNetWay | 5 | 1.06 | 1.07 | 0/2 | 1.06 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | post_message | 131,781 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 132,403 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | post_message | 1 | 30.0 [30.0–30.0] | 5/5 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–30.1] | 5/5 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 5/5 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 5/5 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 24,591 | 24,591 | yes |
| F# | 1 | run c=1 | 8,718 | 8,718 | yes |
| F# | 1 | warm-up c=16 | 65,041 | 65,041 | yes |
| F# | 1 | run c=16 | 17,423 | 17,423 | yes |
| F# | 1 | **all** | 115,773 | 115,773 | yes |
| F# | 2 | warm-up c=1 | 24,992 | 24,992 | yes |
| F# | 2 | run c=1 | 9,053 | 9,053 | yes |
| F# | 2 | warm-up c=16 | 66,090 | 66,090 | yes |
| F# | 2 | run c=16 | 17,295 | 17,295 | yes |
| F# | 2 | **all** | 117,430 | 117,430 | yes |
| F# | 3 | warm-up c=1 | 25,029 | 25,029 | yes |
| F# | 3 | run c=1 | 8,964 | 8,964 | yes |
| F# | 3 | warm-up c=16 | 65,541 | 65,541 | yes |
| F# | 3 | run c=16 | 17,446 | 17,446 | yes |
| F# | 3 | **all** | 116,980 | 116,980 | yes |
| F# | 4 | warm-up c=1 | 24,884 | 24,884 | yes |
| F# | 4 | run c=1 | 8,981 | 8,981 | yes |
| F# | 4 | warm-up c=16 | 65,936 | 65,936 | yes |
| F# | 4 | run c=16 | 17,467 | 17,467 | yes |
| F# | 4 | **all** | 117,268 | 117,268 | yes |
| F# | 5 | warm-up c=1 | 24,590 | 24,590 | yes |
| F# | 5 | run c=1 | 8,901 | 8,901 | yes |
| F# | 5 | warm-up c=16 | 65,928 | 65,928 | yes |
| F# | 5 | run c=16 | 17,533 | 17,533 | yes |
| F# | 5 | **all** | 116,952 | 116,952 | yes |
| theDotNetWay | 1 | warm-up c=1 | 25,168 | 25,168 | yes |
| theDotNetWay | 1 | run c=1 | 8,954 | 8,954 | yes |
| theDotNetWay | 1 | warm-up c=16 | 66,492 | 66,492 | yes |
| theDotNetWay | 1 | run c=16 | 17,669 | 17,669 | yes |
| theDotNetWay | 1 | **all** | 118,283 | 118,283 | yes |
| theDotNetWay | 2 | warm-up c=1 | 24,541 | 24,541 | yes |
| theDotNetWay | 2 | run c=1 | 8,761 | 8,761 | yes |
| theDotNetWay | 2 | warm-up c=16 | 66,037 | 66,037 | yes |
| theDotNetWay | 2 | run c=16 | 17,516 | 17,516 | yes |
| theDotNetWay | 2 | **all** | 116,855 | 116,855 | yes |
| theDotNetWay | 3 | warm-up c=1 | 24,771 | 24,771 | yes |
| theDotNetWay | 3 | run c=1 | 8,994 | 8,994 | yes |
| theDotNetWay | 3 | warm-up c=16 | 66,546 | 66,546 | yes |
| theDotNetWay | 3 | run c=16 | 17,723 | 17,723 | yes |
| theDotNetWay | 3 | **all** | 118,034 | 118,034 | yes |
| theDotNetWay | 4 | warm-up c=1 | 24,483 | 24,483 | yes |
| theDotNetWay | 4 | run c=1 | 8,806 | 8,806 | yes |
| theDotNetWay | 4 | warm-up c=16 | 66,474 | 66,474 | yes |
| theDotNetWay | 4 | run c=16 | 17,611 | 17,611 | yes |
| theDotNetWay | 4 | **all** | 117,374 | 117,374 | yes |
| theDotNetWay | 5 | warm-up c=1 | 24,709 | 24,709 | yes |
| theDotNetWay | 5 | run c=1 | 8,859 | 8,859 | yes |
| theDotNetWay | 5 | warm-up c=16 | 65,998 | 65,998 | yes |
| theDotNetWay | 5 | run c=16 | 17,510 | 17,510 | yes |
| theDotNetWay | 5 | **all** | 117,076 | 117,076 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| post_message | – | – | – | – | 2,494 | 2,494 | – |
