```
date: 2026-10-27T07:32:30-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: LADDER_QUEUE=65536
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:fa4f7372b14c34820d1c79e9fd2a2fe284e6ad815396afdf5672ec5765114441 2026-10-27T06:49:16.291406625-04:00 built from commit: f181944a30303b559fe7e996e8fc657a422201b0
dotnetway build: MOUNTED /var/tmp/campfire-ladder over /opt/campfire (23eea9b (dirty files under src/: 2) at 2026-10-27T11:21:25Z)
repo HEAD: 23eea9b (dirty: 2 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 2; suites: http; http secs 8, concs 1 16, routes avatar static_css up, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 2. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 546 [491–602] |
| idle memory.current (MB) | 83.0 [83.0–83.0] |
| idle anon (MB) | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 186 [186–186] |
| peak anon under load (MB) | 142 [142–142] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| avatar c=1 req/s | 12,534 [12,241–12,826] |
| avatar c=1 CPU µs/req | 158 [155–160] |
| avatar c=1 p50 ms | 0.08 [0.08–0.08] |
| avatar c=1 p99 ms | 0.15 [0.12–0.17] |
| avatar c=16 req/s | 99,565 [96,557–102,573] |
| avatar c=16 CPU µs/req | 25.3 [24.6–26.0] |
| avatar c=16 p50 ms | 0.14 [0.13–0.14] |
| avatar c=16 p99 ms | 0.43 [0.38–0.48] |
| static_css c=1 req/s | 11,865 [11,638–12,091] |
| static_css c=1 CPU µs/req | 183 [182–183] |
| static_css c=1 p50 ms | 0.08 [0.08–0.08] |
| static_css c=1 p99 ms | 0.15 [0.13–0.17] |
| static_css c=16 req/s | 89,919 [85,600–94,238] |
| static_css c=16 CPU µs/req | 29.2 [28.5–29.9] |
| static_css c=16 p50 ms | 0.15 [0.14–0.16] |
| static_css c=16 p99 ms | 0.65 [0.56–0.73] |
| up c=1 req/s | 11,510 [11,286–11,733] |
| up c=1 CPU µs/req | 178 [176–180] |
| up c=1 p50 ms | 0.08 [0.08–0.08] |
| up c=1 p99 ms | 0.16 [0.14–0.18] |
| up c=16 req/s | 76,594 [72,802–80,387] |
| up c=16 CPU µs/req | 35.8 [34.9–36.7] |
| up c=16 p50 ms | 0.18 [0.17–0.18] |
| up c=16 p99 ms | 0.67 [0.58–0.76] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.37 2.60 1.97 | 4.70 3.79 2.61 |
| theDotNetWay | 2 | 1.42 2.96 2.41 | 5.12 4.10 3.00 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.07 | 1.06 | 0/6 | 1.09 |
| theDotNetWay | 2 | 1.05 | 1.06 | 0/6 | 1.09 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | avatar | 1,793,666 | 0 | 0 | 0 | 0 |
| theDotNetWay | static_css | 1,628,621 | 0 | 0 | 0 | 0 |
| theDotNetWay | up | 1,409,737 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | avatar | 1 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| theDotNetWay | avatar | 16 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| theDotNetWay | static_css | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | static_css | 16 | 30.0 [30.0–30.0] | 2/2 | 0.99 |
| theDotNetWay | up | 1 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| theDotNetWay | up | 16 | 30.0 [30.0–30.0] | 2/2 | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| avatar | 200 3,364 / 3,364 | 200 3,364 | 3,364 | – |
| static_css | 200 648 / 1,218 | 200 1,218 | 648 | – |
| up | 200 73 / 73 | 200 73 | 73 | – |
| post_message | – | – | – | – |
