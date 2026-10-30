```
date: 2026-10-27T17:13:30-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p9ctl sha256:bf48cbc93c6ac99a3ba68d98e9923838ff979b6bcfd491e3b1f4f4ab3a75b9a1 2026-10-27T15:06:02.651310211-04:00 built from commit: 7d58f54f9bced52bff995bea5898b306fb5987e7
dotnetway image: campfire-dotnetway:app sha256:92fea55c622aa4648f83cc59bca18392a80a01d61a02e6ea0ae92fb573e3bfd2 2026-10-27T17:07:48.132149952-04:00 built from commit: 5b534b36aeb39b9a8d3d9b547959806211735352
repo HEAD: 5b534b3 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes avatar room_show, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 470 [461–722] | 521 [463–550] |
| idle memory.current (MB) | 89.0 [89.0–177.0] | 90.0 [90.0–90.0] |
| idle anon (MB) | 62.0 [62.0–62.0] | 62.0 [62.0–62.0] |
| peak memory.current under load (MB) | 190 [190–287] | 190 [190–190] |
| peak anon under load (MB) | 142 [142–142] | 142 [142–142] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,266 [2,264–2,277] | 2,236 [2,185–2,263] |
| room_show c=1 CPU µs/req | 555 [549–559] | 560 [551–570] |
| room_show c=1 p50 ms | 0.42 [0.42–0.42] | 0.42 [0.42–0.44] |
| room_show c=1 p99 ms | 0.61 [0.61–0.62] | 0.61 [0.60–0.69] |
| room_show c=16 req/s | 7,570 [7,504–7,616] | 7,501 [7,288–7,502] |
| room_show c=16 CPU µs/req | 447 [443–452] | 451 [451–466] |
| room_show c=16 p50 ms | 1.67 [1.65–1.69] | 1.69 [1.68–1.75] |
| room_show c=16 p99 ms | 6.04 [5.99–6.16] | 6.07 [6.01–6.13] |
| avatar c=1 req/s | 12,799 [12,242–13,013] | 12,654 [12,061–12,732] |
| avatar c=1 CPU µs/req | 162 [160–172] | 160 [160–170] |
| avatar c=1 p50 ms | 0.08 [0.07–0.08] | 0.08 [0.08–0.08] |
| avatar c=1 p99 ms | 0.13 [0.12–0.13] | 0.13 [0.13–0.13] |
| avatar c=16 req/s | 106,033 [104,114–107,137] | 104,500 [102,019–107,847] |
| avatar c=16 CPU µs/req | 24.6 [24.5–24.7] | 24.9 [24.7–25.3] |
| avatar c=16 p50 ms | 0.13 [0.12–0.13] | 0.13 [0.12–0.13] |
| avatar c=16 p99 ms | 0.39 [0.38–0.39] | 0.39 [0.39–0.41] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.35 1.91 1.63 | 4.46 2.79 1.99 |
| F# | 2 | 1.25 2.41 2.20 | 4.98 3.19 2.51 |
| F# | 3 | 1.28 2.41 2.29 | 4.45 3.07 2.54 |
| theDotNetWay | 1 | 1.35 2.18 1.84 | 6.04 3.46 2.35 |
| theDotNetWay | 2 | 1.32 2.53 2.12 | 4.08 3.08 2.39 |
| theDotNetWay | 3 | 1.35 2.40 2.34 | 3.99 2.83 2.49 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.07 | 1.07 | 0/4 | 1.07 |
| F# | 2 | 1.07 | 1.05 | 0/4 | 1.09 |
| F# | 3 | 1.05 | 1.05 | 0/4 | 1.07 |
| theDotNetWay | 1 | 1.07 | 1.07 | 0/4 | 1.07 |
| theDotNetWay | 2 | 1.06 | 1.07 | 0/4 | 1.07 |
| theDotNetWay | 3 | 1.07 | 1.06 | 0/4 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 236,033 | 0 | 0 | 0 | 0 |
| F# | avatar | 2,842,858 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 231,840 | 0 | 0 | 0 | 0 |
| theDotNetWay | avatar | 2,814,633 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–34.0] | 3/3 | 1.00 |
| F# | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | avatar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | avatar | 16 | 30.0 [30.0–30.0] | 3/3 | 0.98 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | avatar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | avatar | 16 | 30.0 [30.0–30.0] | 3/3 | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 37,356 / 377,518 | 200 37,356 / 377,518 | 200 377,518 | 200 377,518 | 37,356 | 37,356 | – |
| avatar | 200 3,364 / 3,364 | 200 3,364 / 3,364 | 200 3,364 | 200 3,364 | 3,364 | 3,364 | – |
| post_message | – | – | – | – | – | – | – |
