```
date: 2026-10-20T07:45:28-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:e4a13d16b435c7806ab4fef0833d5aa804262637afe3854f65b2a8b3f676eb60 2026-10-20T07:16:54.918332267-04:00 built from commit: 3042339dd2dad1aca96e07162de9b6b7661fa6c4
dotnetway build: MOUNTED /var/tmp/mounted-new over /opt/campfire (3042339 (dirty files under src/: 0) at 2026-10-20T11:29:45Z)
repo HEAD: 3042339 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 2; suites: http; http secs 8, concs 1 16, routes post_message sidebar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 2. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 468 [468–468] |
| idle memory.current (MB) | 47.0 [47.0–47.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 293 [290–296] |
| peak anon under load (MB) | 216 [215–217] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| sidebar c=1 req/s | 1,486 [1,462–1,510] |
| sidebar c=1 CPU µs/req | 754 [741–766] |
| sidebar c=1 p50 ms | 0.64 [0.63–0.66] |
| sidebar c=1 p99 ms | 1.05 [1.01–1.09] |
| sidebar c=16 req/s | 4,430 [4,406–4,455] |
| sidebar c=16 CPU µs/req | 777 [771–784] |
| sidebar c=16 p50 ms | 2.99 [2.98–3.01] |
| sidebar c=16 p99 ms | 9.61 [9.53–9.70] |
| post_message c=1 req/s | 819 [813–826] |
| post_message c=1 CPU µs/req | 2,109 [2,088–2,130] |
| post_message c=1 p50 ms | 1.07 [1.06–1.08] |
| post_message c=1 p99 ms | 4.77 [4.77–4.77] |
| post_message c=16 req/s | 1,438 [1,433–1,442] |
| post_message c=16 CPU µs/req | 2,182 [2,175–2,189] |
| post_message c=16 p50 ms | 10.6 [10.6–10.6] |
| post_message c=16 p99 ms | 19.5 [19.2–19.8] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.24 2.02 1.84 | 3.01 2.32 1.97 |
| theDotNetWay | 2 | 1.08 1.88 1.84 | 2.70 2.14 1.94 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.10 | 1.10 | 1/4 **noisy** | 1.12 |
| theDotNetWay | 2 | 1.09 | 1.08 | 0/4 | 1.10 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | sidebar | 94,699 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 36,143 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | post_message | 16 | 30.2 [30.2–30.2] | 2/2 | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 23,580 | 23,580 | yes |
| theDotNetWay | 1 | run c=1 | 6,501 | 6,501 | yes |
| theDotNetWay | 1 | warm-up c=16 | 42,932 | 42,932 | yes |
| theDotNetWay | 1 | run c=16 | 11,549 | 11,549 | yes |
| theDotNetWay | 1 | **all** | 84,562 | 84,562 | yes |
| theDotNetWay | 2 | warm-up c=1 | 23,973 | 23,973 | yes |
| theDotNetWay | 2 | run c=1 | 6,607 | 6,607 | yes |
| theDotNetWay | 2 | warm-up c=16 | 42,959 | 42,959 | yes |
| theDotNetWay | 2 | run c=16 | 11,486 | 11,486 | yes |
| theDotNetWay | 2 | **all** | 85,025 | 85,025 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| post_message | – | – | 1,991 | – |
