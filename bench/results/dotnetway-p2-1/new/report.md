```
date: 2026-10-19T21:42:58-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:3e6e12e278334d66232a7e87613eb23fb7e13aef89f5de474e9f28198dd6b17c 2026-10-19T21:38:50.577660715-04:00 built from commit: 54254bbce3847e48d50c1e5c4f0508acfecc6e69
repo HEAD: 54254bb (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 1; suites: http; http secs 8, concs 1 16, routes post_message sidebar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 1. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 367 |
| idle memory.current (MB) | 47.0 |
| idle anon (MB) | 24.0 |
| peak memory.current under load (MB) | 298 |
| peak anon under load (MB) | 223 |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| sidebar c=1 req/s | 1,762 |
| sidebar c=1 CPU µs/req | 662 |
| sidebar c=1 p50 ms | 0.53 |
| sidebar c=1 p99 ms | 2.05 |
| sidebar c=16 req/s | 5,800 |
| sidebar c=16 CPU µs/req | 590 |
| sidebar c=16 p50 ms | 2.24 |
| sidebar c=16 p99 ms | 7.82 |
| post_message c=1 req/s | 935 |
| post_message c=1 CPU µs/req | 1,875 |
| post_message c=1 p50 ms | 0.93 |
| post_message c=1 p99 ms | 4.90 |
| post_message c=16 req/s | 1,853 |
| post_message c=16 CPU µs/req | 1,659 |
| post_message c=16 p50 ms | 7.91 |
| post_message c=16 p99 ms | 15.8 |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.33 2.36 2.03 | 2.86 2.51 2.14 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.03 | 1.04 | 0/4 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | sidebar | 60,521 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 22,312 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | sidebar | 1 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 | 1/1 | 0.98 |
| theDotNetWay | post_message | 1 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 | 1/1 | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 27,275 | 27,275 | yes |
| theDotNetWay | 1 | run c=1 | 7,477 | 7,477 | yes |
| theDotNetWay | 1 | warm-up c=16 | 54,977 | 54,977 | yes |
| theDotNetWay | 1 | run c=16 | 14,835 | 14,835 | yes |
| theDotNetWay | 1 | **all** | 104,564 | 104,564 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| post_message | – | – | 1,992 | – |
