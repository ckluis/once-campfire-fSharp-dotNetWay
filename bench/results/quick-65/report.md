```
date: 2026-10-22T15:34:50-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p5ctl sha256:3b424112aecf6835e2936f7c2f97c8e7fff7b0833959397fc8dd4b85d8a8c202 2026-10-22T13:29:30.533632367-04:00 built from commit: 853604f1
dotnetway image: campfire-dotnetway:app sha256:d566d2e0e7241c3bd80ff7dd5adf964e5bca7b8e94ed4f4c7ff8f4c2fd11a71d 2026-10-22T15:09:33.956966317-04:00 built from commit: fab4bcf2dde2caf36a30e949ae4cd45d64d755fc
repo HEAD: e2ca957 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show post_message static_css, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 383 [374–591] | 463 [454–476] |
| idle memory.current (MB) | 49.0 [48.0–134.0] | 71.0 [71.0–71.0] |
| idle anon (MB) | 25.0 [25.0–25.0] | 45.0 [45.0–45.0] |
| peak memory.current under load (MB) | 275 [273–368] | 298 [297–301] |
| peak anon under load (MB) | 177 [176–179] | 200 [199–201] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,495 [2,492–2,521] | 2,466 [2,318–2,494] |
| room_show c=1 CPU µs/req | 496 [494–499] | 504 [499–529] |
| room_show c=1 p50 ms | 0.38 [0.38–0.38] | 0.39 [0.38–0.41] |
| room_show c=1 p99 ms | 0.60 [0.60–0.61] | 0.62 [0.60–0.64] |
| room_show c=16 req/s | 7,384 [6,876–7,456] | 7,338 [6,504–7,409] |
| room_show c=16 CPU µs/req | 434 [432–469] | 439 [437–509] |
| room_show c=16 p50 ms | 1.63 [1.61–1.75] | 1.64 [1.62–1.92] |
| room_show c=16 p99 ms | 6.84 [6.79–7.31] | 6.78 [6.75–7.17] |
| static_css c=1 req/s | 17,247 [17,212–18,571] | 16,776 [16,392–16,865] |
| static_css c=1 CPU µs/req | 128 [118–128] | 138 [136–140] |
| static_css c=1 p50 ms | 0.06 [0.05–0.06] | 0.06 [0.06–0.06] |
| static_css c=1 p99 ms | 0.09 [0.09–0.10] | 0.11 [0.11–0.12] |
| static_css c=16 req/s | 101,258 [99,193–102,794] | 94,999 [94,673–96,324] |
| static_css c=16 CPU µs/req | 27.9 [27.7–29.0] | 30.5 [29.8–30.6] |
| static_css c=16 p50 ms | 0.14 [0.14–0.14] | 0.15 [0.14–0.15] |
| static_css c=16 p99 ms | 0.36 [0.35–0.37] | 0.46 [0.44–0.46] |
| post_message c=1 req/s | 1,107 [1,075–1,121] | 1,104 [1,076–1,114] |
| post_message c=1 CPU µs/req | 1,657 [1,642–1,668] | 1,664 [1,663–1,711] |
| post_message c=1 p50 ms | 0.78 [0.77–0.78] | 0.78 [0.78–0.81] |
| post_message c=1 p99 ms | 3.96 [3.94–4.66] | 3.97 [3.94–4.00] |
| post_message c=16 req/s | 2,029 [2,027–2,038] | 2,015 [1,950–2,032] |
| post_message c=16 CPU µs/req | 1,475 [1,474–1,476] | 1,491 [1,483–1,554] |
| post_message c=16 p50 ms | 7.26 [6.71–7.38] | 7.43 [6.94–7.70] |
| post_message c=16 p99 ms | 14.8 [14.3–15.0] | 14.4 [14.4–15.6] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.41 1.56 1.88 | 2.92 2.41 2.15 |
| F# | 2 | 1.36 2.24 2.20 | 2.67 2.49 2.31 |
| F# | 3 | 1.42 2.19 2.21 | 2.72 2.52 2.34 |
| theDotNetWay | 1 | 1.39 2.06 2.04 | 2.84 2.55 2.25 |
| theDotNetWay | 2 | 1.42 2.21 2.15 | 2.80 2.59 2.31 |
| theDotNetWay | 3 | 1.32 2.18 2.23 | 2.80 2.42 2.30 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.06 | 1.07 | 0/6 | 1.09 |
| F# | 2 | 1.06 | 1.07 | 0/6 | 1.07 |
| F# | 3 | 1.04 | 1.06 | 0/6 | 1.07 |
| theDotNetWay | 1 | 1.08 | 1.06 | 0/6 | 1.08 |
| theDotNetWay | 2 | 1.04 | 1.05 | 0/6 | 1.07 |
| theDotNetWay | 3 | 1.05 | 1.05 | 0/6 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 233,810 | 0 | 0 | 0 | 0 |
| F# | static_css | 2,850,329 | 0 | 0 | 0 | 0 |
| F# | post_message | 75,202 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 228,261 | 0 | 0 | 0 | 0 |
| theDotNetWay | static_css | 2,688,346 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 74,356 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| F# | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | static_css | 1 | 30.0 [30.0–46.0] | 3/3 | 1.00 |
| F# | static_css | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 1 | 30.0 [30.0–38.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–32.2] | 3/3 | 0.99 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | static_css | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | static_css | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 42.0 [30.0–46.0] | 3/3 | 1.01 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 30,466 | 30,466 | yes |
| F# | 1 | run c=1 | 8,600 | 8,600 | yes |
| F# | 1 | warm-up c=16 | 61,065 | 61,065 | yes |
| F# | 1 | run c=16 | 16,244 | 16,244 | yes |
| F# | 1 | **all** | 116,375 | 116,375 | yes |
| F# | 2 | warm-up c=1 | 40,829 | 40,829 | yes |
| F# | 2 | run c=1 | 8,968 | 8,968 | yes |
| F# | 2 | warm-up c=16 | 61,811 | 61,811 | yes |
| F# | 2 | run c=16 | 16,224 | 16,224 | yes |
| F# | 2 | **all** | 127,832 | 127,832 | yes |
| F# | 3 | warm-up c=1 | 32,062 | 32,062 | yes |
| F# | 3 | run c=1 | 8,854 | 8,854 | yes |
| F# | 3 | warm-up c=16 | 61,352 | 61,352 | yes |
| F# | 3 | run c=16 | 16,312 | 16,312 | yes |
| F# | 3 | **all** | 118,580 | 118,580 | yes |
| theDotNetWay | 1 | warm-up c=1 | 48,941 | 48,941 | yes |
| theDotNetWay | 1 | run c=1 | 8,830 | 8,830 | yes |
| theDotNetWay | 1 | warm-up c=16 | 60,407 | 60,407 | yes |
| theDotNetWay | 1 | run c=16 | 16,128 | 16,128 | yes |
| theDotNetWay | 1 | **all** | 134,306 | 134,306 | yes |
| theDotNetWay | 2 | warm-up c=1 | 32,185 | 32,185 | yes |
| theDotNetWay | 2 | run c=1 | 8,917 | 8,917 | yes |
| theDotNetWay | 2 | warm-up c=16 | 60,812 | 60,812 | yes |
| theDotNetWay | 2 | run c=16 | 16,271 | 16,271 | yes |
| theDotNetWay | 2 | **all** | 118,185 | 118,185 | yes |
| theDotNetWay | 3 | warm-up c=1 | 42,905 | 42,905 | yes |
| theDotNetWay | 3 | run c=1 | 8,604 | 8,604 | yes |
| theDotNetWay | 3 | warm-up c=16 | 59,276 | 59,276 | yes |
| theDotNetWay | 3 | run c=16 | 15,606 | 15,606 | yes |
| theDotNetWay | 3 | **all** | 126,391 | 126,391 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 36,115 / 376,570 | 200 37,356 / 377,518 | 200 376,570 | 200 377,518 | 36,115 | 37,356 | – |
| static_css | 200 650 / 1,218 | 200 648 / 1,218 | 200 1,218 | 200 1,218 | 650 | 648 | – |
| post_message | – | – | – | – | 2,487 | 2,494 | – |
