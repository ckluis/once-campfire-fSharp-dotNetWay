```
date: 2026-10-19T14:44:41-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:9899bd7892853ac7da4e3d2e7b084b49f4679d65f2b9174eeba5e4b7bc8b828e 2026-10-19T14:44:23.370092392-04:00 built from commit: 45a9bd2d196bf2b583b47a55895ff684f122ac65
repo HEAD: 45a9bd2 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 1; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 1. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 429 |
| idle memory.current (MB) | 48.0 |
| idle anon (MB) | 25.0 |
| peak memory.current under load (MB) | 309 |
| peak anon under load (MB) | 224 |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,789 |
| room_show c=1 CPU µs/req | 650 |
| room_show c=1 p50 ms | 0.51 |
| room_show c=1 p99 ms | 1.34 |
| room_show c=16 req/s | 6,343 |
| room_show c=16 CPU µs/req | 532 |
| room_show c=16 p50 ms | 1.99 |
| room_show c=16 p99 ms | 7.30 |
| messages_page c=1 req/s | 1,502 |
| messages_page c=1 CPU µs/req | 762 |
| messages_page c=1 p50 ms | 0.63 |
| messages_page c=1 p99 ms | 2.73 |
| messages_page c=16 req/s | 5,196 |
| messages_page c=16 CPU µs/req | 665 |
| messages_page c=16 p50 ms | 2.53 |
| messages_page c=16 p99 ms | 7.72 |
| sidebar c=1 req/s | 1,180 |
| sidebar c=1 CPU µs/req | 942 |
| sidebar c=1 p50 ms | 0.80 |
| sidebar c=1 p99 ms | 2.94 |
| sidebar c=16 req/s | 4,075 |
| sidebar c=16 CPU µs/req | 850 |
| sidebar c=16 p50 ms | 3.46 |
| sidebar c=16 p99 ms | 9.37 |
| search c=1 req/s | 1,388 |
| search c=1 CPU µs/req | 815 |
| search c=1 p50 ms | 0.68 |
| search c=1 p99 ms | 2.83 |
| search c=16 req/s | 5,036 |
| search c=16 CPU µs/req | 692 |
| search c=16 p50 ms | 2.69 |
| search c=16 p99 ms | 7.80 |
| post_message c=1 req/s | 913 |
| post_message c=1 CPU µs/req | 1,900 |
| post_message c=1 p50 ms | 0.95 |
| post_message c=1 p99 ms | 4.93 |
| post_message c=16 req/s | 1,807 |
| post_message c=16 CPU µs/req | 1,672 |
| post_message c=16 p50 ms | 8.47 |
| post_message c=16 p99 ms | 17.2 |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.39 1.31 1.50 | 2.48 2.21 1.86 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.07 | 1.07 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 65,091 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 53,594 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 42,060 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 51,406 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 21,779 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 | 1/1 | 0.97 |
| theDotNetWay | room_show | 16 | 30.0 | 1/1 | 1.04 |
| theDotNetWay | messages_page | 1 | 34.0 | 1/1 | 1.02 |
| theDotNetWay | messages_page | 16 | 48.0 | 1/1 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 | 1/1 | 1.01 |
| theDotNetWay | sidebar | 16 | 32.0 | 1/1 | 1.00 |
| theDotNetWay | search | 1 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | search | 16 | 48.1 | 1/1 | 1.01 |
| theDotNetWay | post_message | 1 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | post_message | 16 | 30.2 | 1/1 | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 26,398 | 26,398 | yes |
| theDotNetWay | 1 | run c=1 | 7,306 | 7,306 | yes |
| theDotNetWay | 1 | warm-up c=16 | 54,343 | 54,343 | yes |
| theDotNetWay | 1 | run c=16 | 14,473 | 14,473 | yes |
| theDotNetWay | 1 | **all** | 102,520 | 102,520 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 24,080 / 416,122 | 200 416,122 | 24,080 | – |
| messages_page | 200 15,967 / 383,844 | 200 383,844 | 15,967 | – |
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| search | 200 9,713 / 149,623 | 200 149,623 | 9,713 | – |
| post_message | – | – | 1,991 | – |
