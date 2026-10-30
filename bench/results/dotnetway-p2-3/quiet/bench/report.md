```
date: 2026-10-20T08:03:52-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:e4a13d16b435c7806ab4fef0833d5aa804262637afe3854f65b2a8b3f676eb60 2026-10-20T07:16:54.918332267-04:00 built from commit: 3042339dd2dad1aca96e07162de9b6b7661fa6c4
repo HEAD: 556bf77 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message sidebar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 365 [357–561] |
| idle memory.current (MB) | 47.0 [47.0–48.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 304 [303–316] |
| peak anon under load (MB) | 227 [226–232] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| sidebar c=1 req/s | 1,752 [1,749–1,757] |
| sidebar c=1 CPU µs/req | 668 [665–669] |
| sidebar c=1 p50 ms | 0.54 [0.54–0.55] |
| sidebar c=1 p99 ms | 0.81 [0.79–0.81] |
| sidebar c=16 req/s | 6,050 [6,009–6,076] |
| sidebar c=16 CPU µs/req | 573 [568–576] |
| sidebar c=16 p50 ms | 2.18 [2.17–2.19] |
| sidebar c=16 p99 ms | 7.19 [7.10–7.24] |
| post_message c=1 req/s | 933 [915–944] |
| post_message c=1 CPU µs/req | 1,890 [1,880–1,894] |
| post_message c=1 p50 ms | 0.95 [0.94–0.95] |
| post_message c=1 p99 ms | 4.15 [4.15–5.12] |
| post_message c=16 req/s | 1,876 [1,866–1,906] |
| post_message c=16 CPU µs/req | 1,644 [1,623–1,649] |
| post_message c=16 p50 ms | 7.91 [7.88–8.12] |
| post_message c=16 p99 ms | 14.8 [14.6–15.3] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 0.25 1.16 1.62 | 2.31 1.64 1.72 |
| theDotNetWay | 2 | 1.37 1.47 1.66 | 2.89 1.92 1.78 |
| theDotNetWay | 3 | 1.44 1.66 1.70 | 2.73 2.05 1.84 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.06 | 1.08 | 0/4 | 1.08 |
| theDotNetWay | 2 | 1.07 | 1.06 | 0/4 | 1.07 |
| theDotNetWay | 3 | 1.06 | 1.06 | 0/4 | 1.09 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | sidebar | 187,186 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 67,543 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.2 [30.1–34.2] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 27,022 | 27,022 | yes |
| theDotNetWay | 1 | run c=1 | 7,321 | 7,321 | yes |
| theDotNetWay | 1 | warm-up c=16 | 62,289 | 62,289 | yes |
| theDotNetWay | 1 | run c=16 | 14,936 | 14,936 | yes |
| theDotNetWay | 1 | **all** | 111,568 | 111,568 | yes |
| theDotNetWay | 2 | warm-up c=1 | 27,767 | 27,767 | yes |
| theDotNetWay | 2 | run c=1 | 7,549 | 7,549 | yes |
| theDotNetWay | 2 | warm-up c=16 | 56,993 | 56,993 | yes |
| theDotNetWay | 2 | run c=16 | 15,256 | 15,256 | yes |
| theDotNetWay | 2 | **all** | 107,565 | 107,565 | yes |
| theDotNetWay | 3 | warm-up c=1 | 27,387 | 27,387 | yes |
| theDotNetWay | 3 | run c=1 | 7,466 | 7,466 | yes |
| theDotNetWay | 3 | warm-up c=16 | 56,549 | 56,549 | yes |
| theDotNetWay | 3 | run c=16 | 15,015 | 15,015 | yes |
| theDotNetWay | 3 | **all** | 106,417 | 106,417 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| post_message | – | – | 1,991 | – |
