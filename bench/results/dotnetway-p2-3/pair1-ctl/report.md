```
date: 2026-10-20T07:37:55-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:e4a13d16b435c7806ab4fef0833d5aa804262637afe3854f65b2a8b3f676eb60 2026-10-20T07:16:54.918332267-04:00 built from commit: 3042339dd2dad1aca96e07162de9b6b7661fa6c4
dotnetway build: MOUNTED /var/tmp/mounted-ctl over /opt/campfire (3042339 (dirty files under src/: 5) at 2026-10-20T11:30:20Z)
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
| cold start: docker run → /up 200 (ms) | 576 [511–640] |
| idle memory.current (MB) | 47.0 [47.0–47.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 292 [291–292] |
| peak anon under load (MB) | 222 [217–227] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| sidebar c=1 req/s | 1,492 [1,490–1,493] |
| sidebar c=1 CPU µs/req | 753 [750–756] |
| sidebar c=1 p50 ms | 0.64 [0.64–0.64] |
| sidebar c=1 p99 ms | 1.08 [1.05–1.10] |
| sidebar c=16 req/s | 4,432 [4,343–4,521] |
| sidebar c=16 CPU µs/req | 777 [763–790] |
| sidebar c=16 p50 ms | 3.00 [2.92–3.08] |
| sidebar c=16 p99 ms | 9.81 [9.57–10.05] |
| post_message c=1 req/s | 819 [815–823] |
| post_message c=1 CPU µs/req | 2,117 [2,111–2,122] |
| post_message c=1 p50 ms | 1.07 [1.06–1.07] |
| post_message c=1 p99 ms | 4.85 [4.82–4.87] |
| post_message c=16 req/s | 1,408 [1,393–1,423] |
| post_message c=16 CPU µs/req | 2,224 [2,199–2,250] |
| post_message c=16 p50 ms | 10.9 [10.8–11.1] |
| post_message c=16 p99 ms | 19.9 [19.5–20.3] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.37 1.90 1.64 | 2.62 2.14 1.77 |
| theDotNetWay | 2 | 1.31 1.86 1.69 | 2.90 2.40 1.94 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.08 | 1.09 | 1/4 **noisy** | 1.10 |
| theDotNetWay | 2 | 1.09 | 1.07 | 0/4 | 1.10 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | sidebar | 94,808 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 35,669 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | sidebar | 16 | 45.0 [30.0–60.0] | 2/2 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 2/2 | 0.98 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 24,020 | 24,020 | yes |
| theDotNetWay | 1 | run c=1 | 6,519 | 6,519 | yes |
| theDotNetWay | 1 | warm-up c=16 | 43,104 | 43,104 | yes |
| theDotNetWay | 1 | run c=16 | 11,406 | 11,406 | yes |
| theDotNetWay | 1 | **all** | 85,049 | 85,049 | yes |
| theDotNetWay | 2 | warm-up c=1 | 23,891 | 23,891 | yes |
| theDotNetWay | 2 | run c=1 | 6,584 | 6,584 | yes |
| theDotNetWay | 2 | warm-up c=16 | 43,018 | 43,018 | yes |
| theDotNetWay | 2 | run c=16 | 11,160 | 11,160 | yes |
| theDotNetWay | 2 | **all** | 84,653 | 84,653 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| post_message | – | – | 1,991 | – |
