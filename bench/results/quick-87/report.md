```
date: 2026-10-27T10:28:11-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p8ctl sha256:b4ccd7e5b4d6616956a2ffe4659158b14558ffe63304073e402791035ec7805b 2026-10-27T04:58:07.292450478-04:00 built from commit: 6da5ef7ca37f4a07b9f25af708e2da299bfca1db
dotnetway image: campfire-dotnetway:app sha256:506fdf32d8409f88d14d0d8a00af829e313d7d133675ac61199003605be20978 2026-10-27T10:23:00.238497058-04:00 built from commit: 7477bfa97b81eae356646f9a069e8340c13ec4a3
repo HEAD: 7477bfa (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 501 [482–529] | 518 [481–529] |
| idle memory.current (MB) | 83.0 [83.0–85.0] | 90.0 [87.0–90.0] |
| idle anon (MB) | 57.0 [57.0–57.0] | 62.0 [60.0–62.0] |
| peak memory.current under load (MB) | 384 [378–395] | 377 [375–377] |
| peak anon under load (MB) | 286 [283–287] | 279 [279–282] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,219 [2,211–2,244] | 2,235 [2,182–2,259] |
| room_show c=1 CPU µs/req | 564 [557–567] | 559 [558–567] |
| room_show c=1 p50 ms | 0.43 [0.42–0.43] | 0.42 [0.42–0.44] |
| room_show c=1 p99 ms | 0.70 [0.61–0.75] | 0.60 [0.60–0.65] |
| room_show c=16 req/s | 7,213 [7,112–7,300] | 7,241 [6,958–7,267] |
| room_show c=16 CPU µs/req | 456 [452–463] | 455 [453–460] |
| room_show c=16 p50 ms | 1.68 [1.66–1.70] | 1.68 [1.67–1.74] |
| room_show c=16 p99 ms | 6.85 [6.70–6.91] | 6.73 [6.62–7.01] |
| messages_page c=1 req/s | 2,093 [2,050–2,125] | 2,046 [1,951–2,102] |
| messages_page c=1 CPU µs/req | 592 [580–601] | 599 [589–626] |
| messages_page c=1 p50 ms | 0.45 [0.43–0.46] | 0.46 [0.45–0.49] |
| messages_page c=1 p99 ms | 0.62 [0.62–0.65] | 0.60 [0.58–0.63] |
| messages_page c=16 req/s | 7,070 [2,928–7,141] | 7,121 [7,024–7,140] |
| messages_page c=16 CPU µs/req | 475 [473–1,106] | 471 [470–474] |
| messages_page c=16 p50 ms | 1.75 [1.73–4.24] | 1.73 [1.72–1.74] |
| messages_page c=16 p99 ms | 7.21 [7.06–20.40] | 7.20 [7.12–7.33] |
| sidebar c=1 req/s | 2,139 [1,238–2,178] | 2,169 [2,143–2,178] |
| sidebar c=1 CPU µs/req | 580 [573–920] | 572 [572–577] |
| sidebar c=1 p50 ms | 0.44 [0.43–0.67] | 0.43 [0.43–0.44] |
| sidebar c=1 p99 ms | 0.86 [0.62–2.15] | 0.61 [0.58–0.62] |
| sidebar c=16 req/s | 7,176 [4,048–7,436] | 7,353 [7,321–7,430] |
| sidebar c=16 CPU µs/req | 456 [446–792] | 450 [447–454] |
| sidebar c=16 p50 ms | 1.70 [1.65–3.00] | 1.66 [1.65–1.67] |
| sidebar c=16 p99 ms | 7.27 [6.93–16.91] | 7.04 [6.91–7.06] |
| search c=1 req/s | 1,880 [959–1,919] | 1,906 [1,860–1,916] |
| search c=1 CPU µs/req | 645 [636–1,130] | 638 [632–649] |
| search c=1 p50 ms | 0.51 [0.50–0.75] | 0.50 [0.49–0.51] |
| search c=1 p99 ms | 0.69 [0.65–3.64] | 0.68 [0.66–0.69] |
| search c=16 req/s | 6,761 [3,573–6,956] | 6,906 [6,823–6,909] |
| search c=16 CPU µs/req | 500 [492–916] | 495 [495–501] |
| search c=16 p50 ms | 1.87 [1.82–3.50] | 1.83 [1.83–1.85] |
| search c=16 p99 ms | 6.81 [6.46–16.29] | 6.66 [6.49–6.68] |
| post_message c=1 req/s | 1,071 [646–1,085] | 1,067 [1,059–1,069] |
| post_message c=1 CPU µs/req | 1,728 [1,720–2,734] | 1,795 [1,789–1,811] |
| post_message c=1 p50 ms | 0.83 [0.82–1.18] | 0.83 [0.83–0.84] |
| post_message c=1 p99 ms | 4.16 [4.07–7.33] | 4.15 [4.12–4.17] |
| post_message c=16 req/s | 1,982 [1,734–2,126] | 2,139 [2,121–2,155] |
| post_message c=16 CPU µs/req | 1,518 [1,505–1,867] | 1,507 [1,498–1,508] |
| post_message c=16 p50 ms | 6.14 [5.97–7.94] | 6.03 [5.93–6.03] |
| post_message c=16 p99 ms | 17.6 [15.1–26.6] | 14.7 [14.6–15.6] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 0.81 1.95 2.17 | 3.57 2.82 2.50 |
| F# | 2 | 1.31 2.27 2.36 | 3.23 2.75 2.51 |
| F# | 3 | 1.37 2.30 2.37 | 3.49 2.78 2.53 |
| theDotNetWay | 1 | 1.46 2.36 2.36 | 2.96 2.64 2.47 |
| theDotNetWay | 2 | 1.43 2.28 2.35 | 2.69 2.62 2.48 |
| theDotNetWay | 3 | 1.43 2.32 2.39 | 2.86 2.57 2.46 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.06 | 1.08 | 6/10 **noisy** | 1.48 |
| F# | 2 | 1.08 | 1.07 | 0/10 | 1.09 |
| F# | 3 | 1.07 | 1.07 | 0/10 | 1.08 |
| theDotNetWay | 1 | 1.04 | 1.04 | 0/10 | 1.08 |
| theDotNetWay | 2 | 1.06 | 1.06 | 0/10 | 1.09 |
| theDotNetWay | 3 | 1.06 | 1.07 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 226,476 | 0 | 0 | 0 | 0 |
| F# | messages_page | 187,304 | 0 | 0 | 0 | 0 |
| F# | sidebar | 193,758 | 0 | 0 | 0 | 0 |
| F# | search | 176,437 | 0 | 0 | 0 | 0 |
| F# | post_message | 69,581 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 225,191 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 219,113 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 228,817 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 210,594 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 76,930 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | messages_page | 16 | 30.0 [30.0–90.4] | 2/3 **←** | 1.01 |
| F# | sidebar | 1 | 30.0 [30.0–44.0] | 3/3 | 1.01 |
| F# | sidebar | 16 | 38.0 [30.0–54.1] | 3/3 | 0.99 |
| F# | search | 1 | 30.0 [30.0–90.0] | 2/3 **←** | 1.01 |
| F# | search | 16 | 30.0 [30.0–32.1] | 3/3 | 1.02 |
| F# | post_message | 1 | 30.0 [30.0–34.0] | 3/3 | 1.01 |
| F# | post_message | 16 | 30.1 [30.1–86.6] | 3/3 | 0.95 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–40.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.02 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 18,125 | 18,125 | yes |
| F# | 1 | run c=1 | 5,170 | 5,170 | yes |
| F# | 1 | warm-up c=16 | 109,917 | 109,917 | yes |
| F# | 1 | run c=16 | 13,883 | 13,883 | yes |
| F# | 1 | **all** | 147,095 | 147,095 | yes |
| F# | 2 | warm-up c=1 | 30,909 | 30,909 | yes |
| F# | 2 | run c=1 | 8,566 | 8,566 | yes |
| F# | 2 | warm-up c=16 | 64,600 | 64,600 | yes |
| F# | 2 | run c=16 | 16,263 | 16,263 | yes |
| F# | 2 | **all** | 120,338 | 120,338 | yes |
| F# | 3 | warm-up c=1 | 31,167 | 31,167 | yes |
| F# | 3 | run c=1 | 8,681 | 8,681 | yes |
| F# | 3 | warm-up c=16 | 64,960 | 64,960 | yes |
| F# | 3 | run c=16 | 17,018 | 17,018 | yes |
| F# | 3 | **all** | 121,826 | 121,826 | yes |
| theDotNetWay | 1 | warm-up c=1 | 30,814 | 30,814 | yes |
| theDotNetWay | 1 | run c=1 | 8,533 | 8,533 | yes |
| theDotNetWay | 1 | warm-up c=16 | 64,862 | 64,862 | yes |
| theDotNetWay | 1 | run c=16 | 17,258 | 17,258 | yes |
| theDotNetWay | 1 | **all** | 121,467 | 121,467 | yes |
| theDotNetWay | 2 | warm-up c=1 | 30,876 | 30,876 | yes |
| theDotNetWay | 2 | run c=1 | 8,550 | 8,550 | yes |
| theDotNetWay | 2 | warm-up c=16 | 65,122 | 65,122 | yes |
| theDotNetWay | 2 | run c=16 | 17,130 | 17,130 | yes |
| theDotNetWay | 2 | **all** | 121,678 | 121,678 | yes |
| theDotNetWay | 3 | warm-up c=1 | 30,793 | 30,793 | yes |
| theDotNetWay | 3 | run c=1 | 8,477 | 8,477 | yes |
| theDotNetWay | 3 | warm-up c=16 | 64,347 | 64,347 | yes |
| theDotNetWay | 3 | run c=16 | 16,982 | 16,982 | yes |
| theDotNetWay | 3 | **all** | 120,599 | 120,599 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 37,356 / 377,518 | 200 37,356 / 377,518 | 200 377,518 | 200 377,518 | 37,356 | 37,356 | – |
| messages_page | 200 24,267 / 346,364 | 200 24,267 / 346,364 | 200 346,364 | 200 346,364 | 24,267 | 24,267 | – |
| sidebar | 200 7,979 / 30,144 | 200 7,979 / 30,144 | 200 30,144 | 200 30,144 | 7,979 | 7,979 | – |
| search | 200 15,053 / 137,229 | 200 15,053 / 137,229 | 200 137,229 | 200 137,229 | 15,053 | 15,053 | – |
| post_message | – | – | – | – | 2,494 | 2,494 | – |
