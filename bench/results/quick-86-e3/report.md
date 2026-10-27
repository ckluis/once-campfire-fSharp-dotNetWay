```
date: 2026-10-27T07:55:53-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: LADDER_FIELDS=none
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
| cold start: docker run → /up 200 (ms) | 638 [604–673] |
| idle memory.current (MB) | 84.0 [84.0–84.0] |
| idle anon (MB) | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 186 [186–186] |
| peak anon under load (MB) | 142 [142–142] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| avatar c=1 req/s | 11,427 [10,982–11,873] |
| avatar c=1 CPU µs/req | 169 [166–172] |
| avatar c=1 p50 ms | 0.08 [0.08–0.09] |
| avatar c=1 p99 ms | 0.20 [0.18–0.21] |
| avatar c=16 req/s | 90,590 [88,950–92,230] |
| avatar c=16 CPU µs/req | 27.4 [26.9–27.8] |
| avatar c=16 p50 ms | 0.15 [0.15–0.15] |
| avatar c=16 p99 ms | 0.57 [0.51–0.63] |
| static_css c=1 req/s | 11,084 [10,890–11,278] |
| static_css c=1 CPU µs/req | 192 [189–195] |
| static_css c=1 p50 ms | 0.09 [0.09–0.09] |
| static_css c=1 p99 ms | 0.18 [0.17–0.19] |
| static_css c=16 req/s | 84,194 [80,568–87,821] |
| static_css c=16 CPU µs/req | 30.6 [29.7–31.5] |
| static_css c=16 p50 ms | 0.16 [0.15–0.17] |
| static_css c=16 p99 ms | 0.73 [0.60–0.87] |
| up c=1 req/s | 10,601 [9,855–11,346] |
| up c=1 CPU µs/req | 191 [183–198] |
| up c=1 p50 ms | 0.09 [0.09–0.09] |
| up c=1 p99 ms | 0.21 [0.15–0.27] |
| up c=16 req/s | 71,041 [66,523–75,560] |
| up c=16 CPU µs/req | 37.9 [35.6–40.2] |
| up c=16 p50 ms | 0.19 [0.18–0.20] |
| up c=16 p99 ms | 0.78 [0.63–0.92] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.34 3.08 3.10 | 3.98 3.48 3.26 |
| theDotNetWay | 2 | 1.42 2.81 3.04 | 4.64 3.71 3.35 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.09 | 1.10 | 1/6 **noisy** | 1.11 |
| theDotNetWay | 2 | 1.09 | 1.05 | 1/6 **noisy** | 1.11 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | avatar | 1,632,397 | 0 | 0 | 0 | 0 |
| theDotNetWay | static_css | 1,524,573 | 0 | 0 | 0 | 0 |
| theDotNetWay | up | 1,306,365 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | avatar | 1 | 39.0 [30.0–48.0] | 2/2 | 0.99 |
| theDotNetWay | avatar | 16 | 41.0 [30.0–52.0] | 2/2 | 0.99 |
| theDotNetWay | static_css | 1 | 59.1 [30.0–88.3] | 2/2 | 0.99 |
| theDotNetWay | static_css | 16 | 30.0 [30.0–30.0] | 2/2 | 0.99 |
| theDotNetWay | up | 1 | 37.0 [30.0–44.0] | 2/2 | 0.97 |
| theDotNetWay | up | 16 | 54.0 [30.0–78.0] | 2/2 | 0.98 |

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
