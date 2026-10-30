```
date: 2026-10-19T21:07:31-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:2a77212a51436d91accd9b5c8566529618a4d46068464067404bfce49e7c4264 2026-10-19T19:07:37.175154758-04:00 built from commit: 6ef24dab8b9623f78cf880ca1f884209c2607a12
dotnetway build: MOUNTED /var/tmp/campfire-mounted-p2-0-ctl over /opt/campfire (8d5cd20 (dirty files under src/: 5) at 2026-10-20T01:07:30Z)
repo HEAD: 8d5cd20 (dirty: 400 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 349 [339–359] |
| idle memory.current (MB) | 45.0 [45.0–46.0] |
| idle anon (MB) | 23.0 [23.0–23.0] |
| peak memory.current under load (MB) | 294 [284–326] |
| peak anon under load (MB) | 223 [220–227] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| post_message c=1 req/s | 791 [773–924] |
| post_message c=1 CPU µs/req | 2,177 [1,906–2,235] |
| post_message c=1 p50 ms | 1.10 [0.94–1.14] |
| post_message c=1 p99 ms | 4.88 [4.25–4.93] |
| post_message c=16 req/s | 1,442 [1,412–1,470] |
| post_message c=16 CPU µs/req | 2,177 [2,143–2,240] |
| post_message c=16 p50 ms | 10.6 [10.5–10.8] |
| post_message c=16 p99 ms | 19.4 [19.3–19.8] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.37 1.75 1.58 | 2.92 2.20 1.77 |
| theDotNetWay | 2 | 1.42 1.91 1.69 | 2.30 2.06 1.77 |
| theDotNetWay | 3 | 1.36 1.84 1.70 | 2.65 2.09 1.80 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.08 | 1.09 | 0/2 | 1.09 |
| theDotNetWay | 2 | 1.07 | 1.07 | 0/2 | 1.09 |
| theDotNetWay | 3 | 1.07 | 1.08 | 0/2 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | post_message | 54,552 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | post_message | 1 | 30.0 [30.0–60.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–90.5] | 2/3 **←** | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 32,102 | 32,102 | yes |
| theDotNetWay | 1 | run c=1 | 7,395 | 7,395 | yes |
| theDotNetWay | 1 | warm-up c=16 | 128,705 | 128,705 | yes |
| theDotNetWay | 1 | run c=16 | 11,772 | 11,772 | yes |
| theDotNetWay | 1 | **all** | 179,974 | 179,974 | yes |
| theDotNetWay | 2 | warm-up c=1 | 19,698 | 19,698 | yes |
| theDotNetWay | 2 | run c=1 | 6,331 | 6,331 | yes |
| theDotNetWay | 2 | warm-up c=16 | 43,238 | 43,238 | yes |
| theDotNetWay | 2 | run c=16 | 11,555 | 11,555 | yes |
| theDotNetWay | 2 | **all** | 80,822 | 80,822 | yes |
| theDotNetWay | 3 | warm-up c=1 | 19,292 | 19,292 | yes |
| theDotNetWay | 3 | run c=1 | 6,184 | 6,184 | yes |
| theDotNetWay | 3 | warm-up c=16 | 42,383 | 42,383 | yes |
| theDotNetWay | 3 | run c=16 | 11,315 | 11,315 | yes |
| theDotNetWay | 3 | **all** | 79,174 | 79,174 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| post_message | – | – | 1,991 | – |
