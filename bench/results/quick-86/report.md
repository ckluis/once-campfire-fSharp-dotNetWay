```
date: 2026-10-27T09:35:06-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p8ctl sha256:b4ccd7e5b4d6616956a2ffe4659158b14558ffe63304073e402791035ec7805b 2026-10-27T04:58:07.292450478-04:00 built from commit: 6da5ef7ca37f4a07b9f25af708e2da299bfca1db
dotnetway image: campfire-dotnetway:app sha256:b56e95f1389d938f97eecbc7be49f8e9cf3edfb099e82f95ad799c54a60d737a 2026-10-27T09:34:24.37834661-04:00 built from commit: 23eea9b854db0cdc389e140e07413635924facb5
repo HEAD: 23eea9b (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 2; suites: http; http secs 8, concs 1 16, routes avatar static_css up room_show post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 2, dotnetway 2. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 982 [551–1,413] | 621 [567–675] |
| idle memory.current (MB) | 83.0 [83.0–83.0] | 84.0 [84.0–84.0] |
| idle anon (MB) | 57.0 [57.0–57.0] | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 382 [379–384] | 382 [378–387] |
| peak anon under load (MB) | 285 [285–285] | 286 [286–287] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,026 [1,826–2,226] | 2,085 [2,066–2,104] |
| room_show c=1 CPU µs/req | 601 [558–643] | 588 [584–592] |
| room_show c=1 p50 ms | 0.47 [0.43–0.51] | 0.45 [0.44–0.45] |
| room_show c=1 p99 ms | 1.33 [0.62–2.04] | 1.42 [1.41–1.42] |
| room_show c=16 req/s | 6,615 [5,758–7,471] | 6,945 [6,931–6,960] |
| room_show c=16 CPU µs/req | 513 [451–575] | 483 [482–485] |
| room_show c=16 p50 ms | 1.92 [1.67–2.17] | 1.77 [1.77–1.78] |
| room_show c=16 p99 ms | 7.35 [6.10–8.60] | 7.05 [6.98–7.13] |
| avatar c=1 req/s | 11,211 [9,411–13,012] | 12,896 [12,519–13,272] |
| avatar c=1 CPU µs/req | 169 [154–183] | 155 [153–157] |
| avatar c=1 p50 ms | 0.09 [0.07–0.10] | 0.07 [0.07–0.08] |
| avatar c=1 p99 ms | 0.17 [0.12–0.21] | 0.14 [0.12–0.16] |
| avatar c=16 req/s | 94,024 [81,727–106,320] | 100,350 [95,817–104,884] |
| avatar c=16 CPU µs/req | 27.8 [24.8–30.8] | 25.3 [24.6–26.0] |
| avatar c=16 p50 ms | 0.15 [0.13–0.17] | 0.14 [0.13–0.14] |
| avatar c=16 p99 ms | 0.48 [0.38–0.57] | 0.44 [0.39–0.48] |
| static_css c=1 req/s | 10,682 [9,293–12,072] | 11,868 [11,484–12,252] |
| static_css c=1 CPU µs/req | 201 [188–213] | 186 [184–189] |
| static_css c=1 p50 ms | 0.09 [0.08–0.10] | 0.08 [0.08–0.08] |
| static_css c=1 p99 ms | 0.18 [0.14–0.23] | 0.15 [0.13–0.17] |
| static_css c=16 req/s | 81,564 [73,668–89,460] | 85,971 [81,635–90,307] |
| static_css c=16 CPU µs/req | 33.0 [29.7–36.2] | 30.9 [30.0–31.7] |
| static_css c=16 p50 ms | 0.16 [0.15–0.18] | 0.16 [0.15–0.16] |
| static_css c=16 p99 ms | 0.80 [0.60–0.99] | 0.71 [0.60–0.82] |
| up c=1 req/s | 10,116 [8,316–11,916] | 11,540 [11,088–11,993] |
| up c=1 CPU µs/req | 200 [178–222] | 179 [174–183] |
| up c=1 p50 ms | 0.10 [0.08–0.11] | 0.08 [0.08–0.09] |
| up c=1 p99 ms | 0.20 [0.13–0.26] | 0.16 [0.13–0.18] |
| up c=16 req/s | 75,736 [72,219–79,252] | 76,280 [72,171–80,388] |
| up c=16 CPU µs/req | 36.1 [35.2–37.0] | 35.8 [34.8–36.8] |
| up c=16 p50 ms | 0.18 [0.17–0.18] | 0.18 [0.17–0.18] |
| up c=16 p99 ms | 0.69 [0.61–0.77] | 0.67 [0.58–0.76] |
| post_message c=1 req/s | 1,019 [954–1,084] | 1,029 [988–1,070] |
| post_message c=1 CPU µs/req | 1,778 [1,721–1,834] | 1,836 [1,786–1,886] |
| post_message c=1 p50 ms | 0.84 [0.82–0.86] | 0.85 [0.83–0.87] |
| post_message c=1 p99 ms | 4.76 [4.13–5.39] | 4.32 [4.21–4.43] |
| post_message c=16 req/s | 2,071 [1,990–2,152] | 2,081 [2,000–2,162] |
| post_message c=16 CPU µs/req | 1,556 [1,495–1,617] | 1,559 [1,500–1,618] |
| post_message c=16 p50 ms | 6.21 [5.96–6.47] | 6.20 [5.95–6.45] |
| post_message c=16 p99 ms | 16.8 [15.1–18.4] | 15.7 [14.8–16.7] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 0.80 1.52 2.00 | 2.94 3.06 2.59 |
| F# | 2 | 1.33 2.94 2.93 | 3.73 3.67 3.28 |
| theDotNetWay | 1 | 1.43 2.65 2.47 | 3.06 3.42 2.92 |
| theDotNetWay | 2 | 1.39 2.91 2.77 | 3.71 3.64 3.14 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.10 | 1.07 | 4/10 **noisy** | 1.11 |
| F# | 2 | 1.09 | 1.06 | 0/10 | 1.07 |
| theDotNetWay | 1 | 1.08 | 1.06 | 0/10 | 1.09 |
| theDotNetWay | 2 | 1.07 | 1.04 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 138,279 | 0 | 0 | 0 | 0 |
| F# | avatar | 1,683,835 | 0 | 0 | 0 | 0 |
| F# | static_css | 1,476,013 | 0 | 0 | 0 | 0 |
| F# | up | 1,373,685 | 0 | 0 | 0 | 0 |
| F# | post_message | 49,484 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 144,511 | 0 | 0 | 0 | 0 |
| theDotNetWay | avatar | 1,812,020 | 0 | 0 | 0 | 0 |
| theDotNetWay | static_css | 1,565,510 | 0 | 0 | 0 | 0 |
| theDotNetWay | up | 1,405,202 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 49,805 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| F# | room_show | 16 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| F# | avatar | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| F# | avatar | 16 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| F# | static_css | 1 | 31.0 [30.0–32.0] | 2/2 | 1.00 |
| F# | static_css | 16 | 46.0 [34.0–58.0] | 2/2 | 1.00 |
| F# | up | 1 | 31.0 [30.0–32.0] | 2/2 | 0.99 |
| F# | up | 16 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| F# | post_message | 1 | 30.0 [30.0–30.0] | 2/2 | 0.98 |
| F# | post_message | 16 | 30.1 [30.1–30.1] | 2/2 | 1.00 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | avatar | 1 | 30.0 [30.0–30.0] | 2/2 | 1.01 |
| theDotNetWay | avatar | 16 | 30.0 [30.0–30.0] | 2/2 | 0.99 |
| theDotNetWay | static_css | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | static_css | 16 | 35.0 [30.0–40.0] | 2/2 | 1.00 |
| theDotNetWay | up | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | up | 16 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 2/2 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 2/2 | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 27,957 | 27,957 | yes |
| F# | 1 | run c=1 | 7,630 | 7,630 | yes |
| F# | 1 | warm-up c=16 | 58,776 | 58,776 | yes |
| F# | 1 | run c=16 | 15,936 | 15,936 | yes |
| F# | 1 | **all** | 110,299 | 110,299 | yes |
| F# | 2 | warm-up c=1 | 31,430 | 31,430 | yes |
| F# | 2 | run c=1 | 8,671 | 8,671 | yes |
| F# | 2 | warm-up c=16 | 65,243 | 65,243 | yes |
| F# | 2 | run c=16 | 17,247 | 17,247 | yes |
| F# | 2 | **all** | 122,591 | 122,591 | yes |
| theDotNetWay | 1 | warm-up c=1 | 28,425 | 28,425 | yes |
| theDotNetWay | 1 | run c=1 | 7,908 | 7,908 | yes |
| theDotNetWay | 1 | warm-up c=16 | 59,849 | 59,849 | yes |
| theDotNetWay | 1 | run c=16 | 16,022 | 16,022 | yes |
| theDotNetWay | 1 | **all** | 112,204 | 112,204 | yes |
| theDotNetWay | 2 | warm-up c=1 | 30,850 | 30,850 | yes |
| theDotNetWay | 2 | run c=1 | 8,561 | 8,561 | yes |
| theDotNetWay | 2 | warm-up c=16 | 64,734 | 64,734 | yes |
| theDotNetWay | 2 | run c=16 | 17,314 | 17,314 | yes |
| theDotNetWay | 2 | **all** | 121,459 | 121,459 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 37,356 / 377,518 | 200 37,356 / 377,518 | 200 377,518 | 200 377,518 | 37,356 | 37,356 | – |
| avatar | 200 3,364 / 3,364 | 200 3,364 / 3,364 | 200 3,364 | 200 3,364 | 3,364 | 3,364 | – |
| static_css | 200 648 / 1,218 | 200 648 / 1,218 | 200 1,218 | 200 1,218 | 648 | 648 | – |
| up | 200 73 / 73 | 200 73 / 73 | 200 73 | 200 73 | 73 | 73 | – |
| post_message | – | – | – | – | 2,494 | 2,494 | – |
