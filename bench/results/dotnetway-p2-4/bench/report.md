```
date: 2026-10-20T08:28:49-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:a9c66b097254262f788774c40a9fd4f51db5bedb45983b0c3d5bbf5b289b7963 2026-10-20T08:22:26.535656347-04:00 built from commit: 80e20428260c1185035a96f18e2628c4a9a4b6d5
repo HEAD: 80e2042 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message sidebar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 404 [368–533] |
| idle memory.current (MB) | 47.0 [47.0–47.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 312 [306–336] |
| peak anon under load (MB) | 229 [224–229] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| sidebar c=1 req/s | 1,727 [1,711–1,775] |
| sidebar c=1 CPU µs/req | 677 [661–678] |
| sidebar c=1 p50 ms | 0.55 [0.54–0.56] |
| sidebar c=1 p99 ms | 0.82 [0.80–0.82] |
| sidebar c=16 req/s | 5,835 [5,814–5,979] |
| sidebar c=16 CPU µs/req | 588 [579–593] |
| sidebar c=16 p50 ms | 2.24 [2.22–2.26] |
| sidebar c=16 p99 ms | 7.39 [7.12–7.41] |
| post_message c=1 req/s | 910 [877–917] |
| post_message c=1 CPU µs/req | 1,929 [1,899–1,967] |
| post_message c=1 p50 ms | 0.98 [0.95–0.98] |
| post_message c=1 p99 ms | 4.86 [4.20–4.97] |
| post_message c=16 req/s | 1,813 [1,801–1,874] |
| post_message c=16 CPU µs/req | 1,702 [1,647–1,706] |
| post_message c=16 p50 ms | 8.10 [7.78–8.46] |
| post_message c=16 p99 ms | 15.4 [15.2–16.1] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.40 1.96 1.71 | 2.90 2.26 1.86 |
| theDotNetWay | 2 | 1.41 1.96 1.77 | 2.63 2.31 1.96 |
| theDotNetWay | 3 | 1.28 2.00 1.87 | 2.33 2.13 1.94 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.06 | 1.06 | 0/4 | 1.07 |
| theDotNetWay | 2 | 1.07 | 1.05 | 0/4 | 1.07 |
| theDotNetWay | 3 | 1.07 | 1.06 | 0/4 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | sidebar | 182,774 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 65,576 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–64.0] | 3/3 | 1.01 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–42.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 16 | 32.2 [30.1–62.3] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 27,114 | 27,114 | yes |
| theDotNetWay | 1 | run c=1 | 7,333 | 7,333 | yes |
| theDotNetWay | 1 | warm-up c=16 | 58,577 | 58,577 | yes |
| theDotNetWay | 1 | run c=16 | 15,000 | 15,000 | yes |
| theDotNetWay | 1 | **all** | 108,024 | 108,024 | yes |
| theDotNetWay | 2 | warm-up c=1 | 36,284 | 36,284 | yes |
| theDotNetWay | 2 | run c=1 | 7,016 | 7,016 | yes |
| theDotNetWay | 2 | warm-up c=16 | 102,928 | 102,928 | yes |
| theDotNetWay | 2 | run c=16 | 14,432 | 14,432 | yes |
| theDotNetWay | 2 | **all** | 160,660 | 160,660 | yes |
| theDotNetWay | 3 | warm-up c=1 | 26,674 | 26,674 | yes |
| theDotNetWay | 3 | run c=1 | 7,279 | 7,279 | yes |
| theDotNetWay | 3 | warm-up c=16 | 54,441 | 54,441 | yes |
| theDotNetWay | 3 | run c=16 | 14,516 | 14,516 | yes |
| theDotNetWay | 3 | **all** | 102,910 | 102,910 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| post_message | – | – | 1,991 | – |
