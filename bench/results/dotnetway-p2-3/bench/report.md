```
date: 2026-10-20T07:17:30-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:e4a13d16b435c7806ab4fef0833d5aa804262637afe3854f65b2a8b3f676eb60 2026-10-20T07:16:54.918332267-04:00 built from commit: 3042339dd2dad1aca96e07162de9b6b7661fa6c4
repo HEAD: 3042339 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message sidebar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 480 [441–480] |
| idle memory.current (MB) | 47.0 [47.0–47.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 294 [291–296] |
| peak anon under load (MB) | 224 [223–231] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| sidebar c=1 req/s | 1,478 [1,473–1,479] |
| sidebar c=1 CPU µs/req | 760 [759–762] |
| sidebar c=1 p50 ms | 0.65 [0.65–0.65] |
| sidebar c=1 p99 ms | 1.08 [1.05–1.12] |
| sidebar c=16 req/s | 4,451 [4,439–4,475] |
| sidebar c=16 CPU µs/req | 774 [769–779] |
| sidebar c=16 p50 ms | 2.99 [2.97–3.00] |
| sidebar c=16 p99 ms | 9.65 [9.53–9.68] |
| post_message c=1 req/s | 806 [790–819] |
| post_message c=1 CPU µs/req | 2,141 [2,114–2,155] |
| post_message c=1 p50 ms | 1.08 [1.07–1.09] |
| post_message c=1 p99 ms | 4.88 [4.74–5.49] |
| post_message c=16 req/s | 1,422 [1,398–1,434] |
| post_message c=16 CPU µs/req | 2,203 [2,181–2,246] |
| post_message c=16 p50 ms | 10.8 [10.5–11.0] |
| post_message c=16 p99 ms | 19.6 [18.9–21.1] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.36 1.40 0.75 | 2.66 1.87 1.04 |
| theDotNetWay | 2 | 1.09 1.55 0.98 | 2.73 1.97 1.23 |
| theDotNetWay | 3 | 1.36 1.71 1.17 | 2.51 1.98 1.36 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.10 | 1.11 | 2/4 **noisy** | 1.10 |
| theDotNetWay | 2 | 1.09 | 1.11 | 1/4 **noisy** | 1.11 |
| theDotNetWay | 3 | 1.08 | 1.08 | 1/4 **noisy** | 1.10 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | sidebar | 142,386 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 53,390 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 16 | 30.2 [30.1–32.2] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 23,016 | 23,016 | yes |
| theDotNetWay | 1 | run c=1 | 6,317 | 6,317 | yes |
| theDotNetWay | 1 | warm-up c=16 | 43,462 | 43,462 | yes |
| theDotNetWay | 1 | run c=16 | 11,198 | 11,198 | yes |
| theDotNetWay | 1 | **all** | 83,993 | 83,993 | yes |
| theDotNetWay | 2 | warm-up c=1 | 23,720 | 23,720 | yes |
| theDotNetWay | 2 | run c=1 | 6,449 | 6,449 | yes |
| theDotNetWay | 2 | warm-up c=16 | 42,915 | 42,915 | yes |
| theDotNetWay | 2 | run c=16 | 11,391 | 11,391 | yes |
| theDotNetWay | 2 | **all** | 84,475 | 84,475 | yes |
| theDotNetWay | 3 | warm-up c=1 | 23,984 | 23,984 | yes |
| theDotNetWay | 3 | run c=1 | 6,550 | 6,550 | yes |
| theDotNetWay | 3 | warm-up c=16 | 43,282 | 43,282 | yes |
| theDotNetWay | 3 | run c=16 | 11,485 | 11,485 | yes |
| theDotNetWay | 3 | **all** | 85,301 | 85,301 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| post_message | – | – | 1,991 | – |
