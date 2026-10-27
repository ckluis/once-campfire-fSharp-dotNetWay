```
date: 2026-10-27T18:01:56-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p9ctl sha256:bf48cbc93c6ac99a3ba68d98e9923838ff979b6bcfd491e3b1f4f4ab3a75b9a1 2026-10-27T15:06:02.651310211-04:00 built from commit: 7d58f54f9bced52bff995bea5898b306fb5987e7
dotnetway image: campfire-dotnetway:app sha256:1f16395e4d2b3c159318c05285b72950ecb314fde8faa873dbd804168a29bc31 2026-10-27T18:00:17.549899606-04:00 built from commit: dabac38
repo HEAD: dabac38 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes avatar static_css up room_show post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 581 [552–625] | 558 [477–621] |
| idle memory.current (MB) | 90.0 [89.0–93.0] | 90.0 [89.0–90.0] |
| idle anon (MB) | 62.0 [62.0–63.0] | 62.0 [62.0–63.0] |
| peak memory.current under load (MB) | 380 [380–385] | 381 [380–393] |
| peak anon under load (MB) | 286 [284–287] | 284 [283–284] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,039 [1,112–2,040] | 2,245 [2,128–2,261] |
| room_show c=1 CPU µs/req | 616 [595–1,004] | 561 [558–583] |
| room_show c=1 p50 ms | 0.47 [0.46–0.67] | 0.42 [0.42–0.44] |
| room_show c=1 p99 ms | 1.49 [0.69–2.94] | 0.61 [0.61–1.42] |
| room_show c=16 req/s | 6,930 [5,200–7,340] | 7,563 [6,972–7,621] |
| room_show c=16 CPU µs/req | 485 [458–641] | 446 [440–476] |
| room_show c=16 p50 ms | 1.79 [1.71–2.45] | 1.68 [1.66–1.77] |
| room_show c=16 p99 ms | 6.86 [6.41–9.43] | 5.98 [5.96–7.00] |
| avatar c=1 req/s | 9,998 [9,678–11,493] | 21,483 [19,927–21,830] |
| avatar c=1 CPU µs/req | 181 [177–188] | 108 [108–115] |
| avatar c=1 p50 ms | 0.10 [0.08–0.10] | 0.04 [0.04–0.05] |
| avatar c=1 p99 ms | 0.16 [0.16–0.18] | 0.09 [0.09–0.11] |
| avatar c=16 req/s | 86,814 [50,873–94,877] | 117,208 [108,991–119,764] |
| avatar c=16 CPU µs/req | 29.3 [26.8–46.7] | 22.8 [22.4–24.1] |
| avatar c=16 p50 ms | 0.16 [0.14–0.24] | 0.13 [0.12–0.14] |
| avatar c=16 p99 ms | 0.50 [0.46–1.49] | 0.27 [0.27–0.30] |
| static_css c=1 req/s | 7,408 [4,256–10,868] | 18,071 [17,179–18,338] |
| static_css c=1 CPU µs/req | 282 [207–366] | 135 [134–136] |
| static_css c=1 p50 ms | 0.13 [0.09–0.18] | 0.05 [0.05–0.05] |
| static_css c=1 p99 ms | 0.27 [0.18–1.15] | 0.10 [0.10–0.12] |
| static_css c=16 req/s | 64,196 [56,360–79,306] | 104,834 [99,318–105,556] |
| static_css c=16 CPU µs/req | 41.7 [33.0–48.3] | 27.3 [27.2–28.6] |
| static_css c=16 p50 ms | 0.20 [0.17–0.23] | 0.14 [0.14–0.15] |
| static_css c=16 p99 ms | 1.20 [0.87–1.29] | 0.31 [0.31–0.41] |
| up c=1 req/s | 9,121 [8,766–10,387] | 16,886 [16,827–16,898] |
| up c=1 CPU µs/req | 217 [200–218] | 131 [131–132] |
| up c=1 p50 ms | 0.10 [0.09–0.11] | 0.06 [0.06–0.06] |
| up c=1 p99 ms | 0.20 [0.19–0.20] | 0.09 [0.09–0.09] |
| up c=16 req/s | 72,554 [69,619–75,327] | 95,097 [94,699–95,834] |
| up c=16 CPU µs/req | 38.1 [35.3–38.4] | 30.6 [30.4–30.7] |
| up c=16 p50 ms | 0.18 [0.18–0.19] | 0.16 [0.16–0.16] |
| up c=16 p99 ms | 0.77 [0.61–0.84] | 0.34 [0.34–0.35] |
| post_message c=1 req/s | 954 [908–1,034] | 1,058 [1,054–1,064] |
| post_message c=1 CPU µs/req | 1,931 [1,831–2,043] | 1,810 [1,800–1,815] |
| post_message c=1 p50 ms | 0.89 [0.86–0.93] | 0.84 [0.83–0.84] |
| post_message c=1 p99 ms | 5.16 [4.16–5.47] | 4.12 [4.10–4.17] |
| post_message c=16 req/s | 1,988 [1,894–2,071] | 2,159 [2,109–2,162] |
| post_message c=16 CPU µs/req | 1,608 [1,566–1,641] | 1,507 [1,505–1,534] |
| post_message c=16 p50 ms | 6.67 [6.32–6.83] | 6.01 [5.99–6.12] |
| post_message c=16 p99 ms | 16.8 [14.9–35.1] | 14.6 [14.5–15.1] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 0.91 0.84 1.14 | 3.37 2.93 2.03 |
| F# | 2 | 1.43 2.56 2.53 | 3.77 3.61 3.11 |
| F# | 3 | 1.30 2.91 2.90 | 3.49 3.17 3.04 |
| theDotNetWay | 1 | 1.42 2.45 1.92 | 2.93 3.02 2.43 |
| theDotNetWay | 2 | 1.25 2.52 2.29 | 3.49 3.07 2.68 |
| theDotNetWay | 3 | 1.42 2.65 2.87 | 3.07 3.01 2.96 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.07 | 1.07 | 0/10 | 1.09 |
| F# | 2 | 1.06 | 1.04 | 3/10 **noisy** | 1.44 |
| F# | 3 | 1.07 | 1.05 | 3/10 **noisy** | 1.41 |
| theDotNetWay | 1 | 1.07 | 1.05 | 0/10 | 1.10 |
| theDotNetWay | 2 | 1.07 | 1.07 | 0/10 | 1.08 |
| theDotNetWay | 3 | 1.08 | 1.06 | 0/10 | 1.06 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 197,325 | 0 | 0 | 0 | 0 |
| F# | avatar | 2,110,038 | 0 | 0 | 0 | 0 |
| F# | static_css | 1,779,251 | 0 | 0 | 0 | 0 |
| F# | up | 1,966,295 | 0 | 0 | 0 | 0 |
| F# | post_message | 70,836 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 230,384 | 0 | 0 | 0 | 0 |
| theDotNetWay | avatar | 3,273,776 | 0 | 0 | 0 | 0 |
| theDotNetWay | static_css | 2,906,534 | 0 | 0 | 0 | 0 |
| theDotNetWay | up | 2,690,056 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 76,879 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 40.1 [30.0–72.0] | 3/3 | 1.00 |
| F# | room_show | 16 | 32.0 [30.0–90.1] | 2/3 **←** | 1.00 |
| F# | avatar | 1 | 76.0 [30.0–90.0] | 2/3 **←** | 1.00 |
| F# | avatar | 16 | 30.0 [30.0–90.0] | 2/3 **←** | 1.00 |
| F# | static_css | 1 | 44.0 [30.0–90.1] | 2/3 **←** | 1.01 |
| F# | static_css | 16 | 70.0 [30.0–90.4] | 2/3 **←** | 1.05 |
| F# | up | 1 | 90.0 [30.0–90.0] | 2/3 **←** | 1.01 |
| F# | up | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.2 [30.1–38.2] | 3/3 | 0.99 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | avatar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | avatar | 16 | 32.0 [30.0–52.0] | 3/3 | 0.98 |
| theDotNetWay | static_css | 1 | 30.0 [30.0–90.0] | 2/3 **←** | 1.00 |
| theDotNetWay | static_css | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | up | 1 | 90.0 [90.0–90.0] | 0/3 **←** | 1.00 |
| theDotNetWay | up | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–38.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–48.2] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 27,641 | 27,641 | yes |
| F# | 1 | run c=1 | 7,633 | 7,633 | yes |
| F# | 1 | warm-up c=16 | 58,065 | 58,065 | yes |
| F# | 1 | run c=16 | 15,919 | 15,919 | yes |
| F# | 1 | **all** | 109,258 | 109,258 | yes |
| F# | 2 | warm-up c=1 | 26,462 | 26,462 | yes |
| F# | 2 | run c=1 | 7,262 | 7,262 | yes |
| F# | 2 | warm-up c=16 | 71,486 | 71,486 | yes |
| F# | 2 | run c=16 | 15,169 | 15,169 | yes |
| F# | 2 | **all** | 120,379 | 120,379 | yes |
| F# | 3 | warm-up c=1 | 29,859 | 29,859 | yes |
| F# | 3 | run c=1 | 8,270 | 8,270 | yes |
| F# | 3 | warm-up c=16 | 61,634 | 61,634 | yes |
| F# | 3 | run c=16 | 16,583 | 16,583 | yes |
| F# | 3 | **all** | 116,346 | 116,346 | yes |
| theDotNetWay | 1 | warm-up c=1 | 30,560 | 30,560 | yes |
| theDotNetWay | 1 | run c=1 | 8,510 | 8,510 | yes |
| theDotNetWay | 1 | warm-up c=16 | 64,939 | 64,939 | yes |
| theDotNetWay | 1 | run c=16 | 17,285 | 17,285 | yes |
| theDotNetWay | 1 | **all** | 121,294 | 121,294 | yes |
| theDotNetWay | 2 | warm-up c=1 | 36,079 | 36,079 | yes |
| theDotNetWay | 2 | run c=1 | 8,465 | 8,465 | yes |
| theDotNetWay | 2 | warm-up c=16 | 89,926 | 89,926 | yes |
| theDotNetWay | 2 | run c=16 | 16,881 | 16,881 | yes |
| theDotNetWay | 2 | **all** | 151,351 | 151,351 | yes |
| theDotNetWay | 3 | warm-up c=1 | 30,612 | 30,612 | yes |
| theDotNetWay | 3 | run c=1 | 8,428 | 8,428 | yes |
| theDotNetWay | 3 | warm-up c=16 | 64,588 | 64,588 | yes |
| theDotNetWay | 3 | run c=16 | 17,310 | 17,310 | yes |
| theDotNetWay | 3 | **all** | 120,938 | 120,938 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 37,356 / 377,518 | 200 37,356 / 377,518 | 200 377,518 | 200 377,518 | 37,356 | 37,356 | – |
| avatar | 200 3,364 / 3,364 | 200 3,364 / 3,364 | 200 3,364 | 200 3,364 | 3,364 | 3,364 | – |
| static_css | 200 648 / 1,218 | 200 648 / 1,218 | 200 1,218 | 200 1,218 | 648 | 648 | – |
| up | 200 73 / 73 | 200 73 / 73 | 200 73 | 200 73 | 73 | 73 | – |
| post_message | – | – | – | – | 2,494 | 2,494 | – |
