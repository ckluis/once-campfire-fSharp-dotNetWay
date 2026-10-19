```
date: 2026-10-19T14:15:38-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-fsharp:app sha256:cccad24be9a176d72520c350823efbc82a76c219318fb2a7bc132e0b05bd38c2 2026-10-10T17:20:26.217039707-04:00 built from commit: 
dotnetway image: campfire-dotnetway:app sha256:0d0214249ffbb4922151f8b166ceb8aad91fc6df29b79efe019f60c8f20434e3 2026-10-19T14:15:06.331382491-04:00 built from commit: 9a953797bf0bb87bac76acfc3a1c2a2090f160a9
repo HEAD: 9a95379 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 1; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 1, dotnetway 1. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 400 | 409 |
| idle memory.current (MB) | 86.0 | 47.0 |
| idle anon (MB) | 15.0 | 25.0 |
| peak memory.current under load (MB) | 482 | 303 |
| peak anon under load (MB) | 219 | 232 |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 7,644 | 1,288 |
| room_show c=1 CPU µs/req | 206 | 870 |
| room_show c=1 p50 ms | 0.12 | 0.75 |
| room_show c=1 p99 ms | 0.20 | 2.14 |
| room_show c=16 req/s | 35,850 | 4,405 |
| room_show c=16 CPU µs/req | 98.8 | 794 |
| room_show c=16 p50 ms | 0.41 | 3.13 |
| room_show c=16 p99 ms | 1.18 | 8.19 |
| messages_page c=1 req/s | 8,360 | 1,246 |
| messages_page c=1 CPU µs/req | 193 | 900 |
| messages_page c=1 p50 ms | 0.12 | 0.77 |
| messages_page c=1 p99 ms | 0.16 | 2.71 |
| messages_page c=16 req/s | 38,138 | 4,379 |
| messages_page c=16 CPU µs/req | 93.3 | 798 |
| messages_page c=16 p50 ms | 0.39 | 3.15 |
| messages_page c=16 p99 ms | 1.11 | 8.27 |
| sidebar c=1 req/s | 7,732 | 1,075 |
| sidebar c=1 CPU µs/req | 184 | 1,027 |
| sidebar c=1 p50 ms | 0.12 | 0.89 |
| sidebar c=1 p99 ms | 0.18 | 2.98 |
| sidebar c=16 req/s | 34,488 | 3,773 |
| sidebar c=16 CPU µs/req | 105 | 929 |
| sidebar c=16 p50 ms | 0.43 | 3.88 |
| sidebar c=16 p99 ms | 1.17 | 9.14 |
| search c=1 req/s | 6,165 | 1,287 |
| search c=1 CPU µs/req | 300 | 873 |
| search c=1 p50 ms | 0.16 | 0.74 |
| search c=1 p99 ms | 0.20 | 2.85 |
| search c=16 req/s | 33,616 | 4,612 |
| search c=16 CPU µs/req | 109 | 761 |
| search c=16 p50 ms | 0.45 | 3.00 |
| search c=16 p99 ms | 1.32 | 8.06 |
| post_message c=1 req/s | 2,960 | 544 |
| post_message c=1 CPU µs/req | 624 | 2,865 |
| post_message c=1 p50 ms | 0.31 | 1.70 |
| post_message c=1 p99 ms | 0.62 | 4.90 |
| post_message c=16 req/s | 8,541 | 693 |
| post_message c=16 CPU µs/req | 360 | 4,114 |
| post_message c=16 p50 ms | 1.58 | 14.1 |
| post_message c=16 p99 ms | 6.40 | 208 |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.25 1.07 1.46 | 3.74 2.97 2.20 |
| theDotNetWay | 1 | 1.29 2.40 2.06 | 2.59 2.47 2.18 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.08 | 1.07 | 2/10 **noisy** | 1.12 |
| theDotNetWay | 1 | 1.05 | 1.07 | 1/10 **noisy** | 1.14 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 347,982 | 0 | 0 | 0 | 0 |
| F# | messages_page | 372,010 | 0 | 0 | 0 | 0 |
| F# | sidebar | 337,787 | 0 | 0 | 0 | 0 |
| F# | search | 318,275 | 0 | 0 | 0 | 0 |
| F# | post_message | 92,019 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 45,552 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 45,013 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 38,794 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 47,202 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 9,919 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 | 1/1 | 0.99 |
| F# | room_show | 16 | 30.0 | 1/1 | 1.05 |
| F# | messages_page | 1 | 30.0 | 1/1 | 1.00 |
| F# | messages_page | 16 | 30.0 | 1/1 | 1.00 |
| F# | sidebar | 1 | 30.0 | 1/1 | 1.00 |
| F# | sidebar | 16 | 48.0 | 1/1 | 0.99 |
| F# | search | 1 | 30.0 | 1/1 | 1.01 |
| F# | search | 16 | 30.0 | 1/1 | 1.01 |
| F# | post_message | 1 | 30.0 | 1/1 | 0.99 |
| F# | post_message | 16 | 30.0 | 1/1 | 0.99 |
| theDotNetWay | room_show | 1 | 30.0 | 1/1 | 1.01 |
| theDotNetWay | room_show | 16 | 30.0 | 1/1 | 1.01 |
| theDotNetWay | messages_page | 1 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | search | 1 | 30.0 | 1/1 | 1.01 |
| theDotNetWay | search | 16 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 | 1/1 | 1.00 |
| theDotNetWay | post_message | 16 | 30.2 | 1/1 | 0.59 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 88,713 | 88,713 | yes |
| F# | 1 | run c=1 | 23,683 | 23,683 | yes |
| F# | 1 | warm-up c=16 | 233,830 | 233,830 | yes |
| F# | 1 | run c=16 | 68,336 | 68,336 | yes |
| F# | 1 | **all** | 414,562 | 414,562 | yes |
| theDotNetWay | 1 | warm-up c=1 | 15,990 | 15,990 | yes |
| theDotNetWay | 1 | run c=1 | 4,351 | 4,351 | yes |
| theDotNetWay | 1 | warm-up c=16 | 35,335 | 35,335 | yes |
| theDotNetWay | 1 | run c=16 | 5,568 | 5,568 | yes |
| theDotNetWay | 1 | **all** | 61,244 | 61,244 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 24,080 / 416,122 | 200 24,080 / 416,122 | 200 416,122 | 200 416,122 | 24,080 | 24,080 | – |
| messages_page | 200 15,967 / 383,844 | 200 15,967 / 383,844 | 200 383,844 | 200 383,844 | 15,967 | 15,967 | – |
| sidebar | 200 5,910 / 30,763 | 200 5,910 / 30,763 | 200 30,763 | 200 30,763 | 5,910 | 5,910 | – |
| search | 200 9,713 / 149,623 | 200 9,713 / 149,623 | 200 149,623 | 200 149,623 | 9,713 | 9,713 | – |
| post_message | – | – | – | – | 1,993 | 1,990 | – |
