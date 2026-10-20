```
date: 2026-10-20T14:30:39-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-fsharp:app sha256:cccad24be9a176d72520c350823efbc82a76c219318fb2a7bc132e0b05bd38c2 2026-10-10T17:20:26.217039707-04:00 built from commit: 
repo HEAD: dee7ec5 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp, reps 1; suites: http; http secs 8, concs 1 16, routes sidebar post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 1. Cells: median [min–max].

### Startup and memory

| Metric | F# |
|---|---|
| cold start: docker run → /up 200 (ms) | 348 |
| idle memory.current (MB) | 86.0 |
| idle anon (MB) | 15.0 |
| peak memory.current under load (MB) | 484 |
| peak anon under load (MB) | 212 |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# |
|---|---|
| sidebar c=1 req/s | 7,792 |
| sidebar c=1 CPU µs/req | 183 |
| sidebar c=1 p50 ms | 0.12 |
| sidebar c=1 p99 ms | 0.18 |
| sidebar c=16 req/s | 37,363 |
| sidebar c=16 CPU µs/req | 96.4 |
| sidebar c=16 p50 ms | 0.40 |
| sidebar c=16 p99 ms | 1.06 |
| post_message c=1 req/s | 3,034 |
| post_message c=1 CPU µs/req | 633 |
| post_message c=1 p50 ms | 0.31 |
| post_message c=1 p99 ms | 0.55 |
| post_message c=16 req/s | 8,805 |
| post_message c=16 CPU µs/req | 360 |
| post_message c=16 p50 ms | 1.57 |
| post_message c=16 p99 ms | 6.07 |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.36 2.14 2.03 | 4.46 2.79 2.27 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.07 | 1.05 | 0/4 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | sidebar | 361,257 | 0 | 0 | 0 | 0 |
| F# | post_message | 94,726 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | sidebar | 1 | 30.0 | 1/1 | 1.00 |
| F# | sidebar | 16 | 30.0 | 1/1 | 1.00 |
| F# | post_message | 1 | 30.0 | 1/1 | 1.00 |
| F# | post_message | 16 | 30.0 | 1/1 | 1.02 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 90,083 | 90,083 | yes |
| F# | 1 | run c=1 | 24,270 | 24,270 | yes |
| F# | 1 | warm-up c=16 | 259,751 | 259,751 | yes |
| F# | 1 | run c=16 | 70,456 | 70,456 | yes |
| F# | 1 | **all** | 444,560 | 444,560 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | F# identity | F# c=16 avg | F# vs Rust |
|---|---|---|---|---|
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| post_message | – | – | 1,993 | – |
