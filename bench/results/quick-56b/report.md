```
date: 2026-10-22T08:14:55-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:control-8abdb6b sha256:d869de00fd7da1d9a75231696fd79c8e72e66897ecb413fa4d6515dcdc850d77 2026-10-22T07:26:54.47152608-04:00 built from commit: 8abdb6bb567e0ad5ccd5e61aa2577a40e4424b62
dotnetway image: campfire-dotnetway:app sha256:f1802b75f196c1ca135c9d3988f65e2f7a2d065cdd9f7a800fb5dd85f5798414 2026-10-22T08:06:07.019395791-04:00 built from commit: 5d9b6b15cf02305197200fc81b3bb17252699b82
repo HEAD: 5d9b6b1 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 389 [339–420] | 371 [362–376] |
| idle memory.current (MB) | 48.0 [48.0–51.0] | 48.0 [48.0–48.0] |
| idle anon (MB) | 25.0 [25.0–25.0] | 25.0 [25.0–25.0] |
| peak memory.current under load (MB) | 273 [272–274] | 271 [270–271] |
| peak anon under load (MB) | 176 [175–178] | 175 [174–178] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,496 [2,483–2,507] | 2,468 [2,463–2,512] |
| room_show c=1 CPU µs/req | 496 [494–500] | 502 [494–508] |
| room_show c=1 p50 ms | 0.38 [0.38–0.38] | 0.38 [0.38–0.38] |
| room_show c=1 p99 ms | 0.61 [0.58–0.62] | 0.61 [0.61–0.62] |
| room_show c=16 req/s | 7,088 [6,776–7,381] | 7,373 [7,320–7,426] |
| room_show c=16 CPU µs/req | 458 [436–477] | 438 [436–441] |
| room_show c=16 p50 ms | 1.72 [1.62–1.77] | 1.63 [1.62–1.64] |
| room_show c=16 p99 ms | 6.88 [6.83–7.42] | 6.76 [6.76–6.77] |
| messages_page c=1 req/s | 2,273 [2,256–2,290] | 2,284 [2,273–2,301] |
| messages_page c=1 CPU µs/req | 534 [533–537] | 533 [530–537] |
| messages_page c=1 p50 ms | 0.41 [0.41–0.42] | 0.41 [0.41–0.41] |
| messages_page c=1 p99 ms | 0.57 [0.57–0.68] | 0.56 [0.56–0.56] |
| messages_page c=16 req/s | 7,276 [7,019–7,340] | 7,277 [7,274–7,318] |
| messages_page c=16 CPU µs/req | 455 [450–478] | 456 [453–458] |
| messages_page c=16 p50 ms | 1.70 [1.67–1.78] | 1.69 [1.68–1.70] |
| messages_page c=16 p99 ms | 7.19 [7.16–7.24] | 7.21 [7.16–7.23] |
| search c=1 req/s | 2,099 [2,094–2,115] | 2,101 [2,077–2,112] |
| search c=1 CPU µs/req | 571 [567–572] | 571 [568–578] |
| search c=1 p50 ms | 0.45 [0.45–0.45] | 0.45 [0.45–0.46] |
| search c=1 p99 ms | 0.66 [0.65–0.69] | 0.65 [0.65–0.69] |
| search c=16 req/s | 7,033 [6,636–7,056] | 6,965 [6,899–7,036] |
| search c=16 CPU µs/req | 480 [478–513] | 484 [478–488] |
| search c=16 p50 ms | 1.79 [1.78–1.92] | 1.81 [1.79–1.82] |
| search c=16 p99 ms | 6.63 [6.62–6.83] | 6.75 [6.59–6.79] |
| post_message c=1 req/s | 1,131 [1,110–1,161] | 1,100 [1,092–1,107] |
| post_message c=1 CPU µs/req | 1,664 [1,617–1,683] | 1,674 [1,664–1,690] |
| post_message c=1 p50 ms | 0.76 [0.74–0.76] | 0.79 [0.78–0.79] |
| post_message c=1 p99 ms | 3.92 [3.92–4.72] | 3.95 [3.95–3.98] |
| post_message c=16 req/s | 2,048 [1,992–2,066] | 2,020 [2,014–2,024] |
| post_message c=16 CPU µs/req | 1,455 [1,448–1,524] | 1,487 [1,483–1,503] |
| post_message c=16 p50 ms | 6.80 [6.56–7.05] | 7.03 [6.91–7.10] |
| post_message c=16 p99 ms | 14.3 [14.3–14.4] | 14.6 [14.4–15.3] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.02 2.05 1.33 | 3.03 2.52 1.74 |
| F# | 2 | 1.31 1.98 1.90 | 2.64 2.41 2.12 |
| F# | 3 | 1.33 2.09 2.02 | 2.25 2.17 2.07 |
| theDotNetWay | 1 | 1.47 2.18 1.66 | 2.49 2.49 1.95 |
| theDotNetWay | 2 | 1.40 2.21 1.88 | 2.25 2.20 1.96 |
| theDotNetWay | 3 | 1.30 1.95 2.00 | 2.56 2.30 2.13 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.07 | 1.08 | 0/8 | 1.08 |
| F# | 2 | 1.07 | 1.04 | 0/8 | 1.08 |
| F# | 3 | 1.07 | 1.04 | 0/8 | 1.09 |
| theDotNetWay | 1 | 1.06 | 1.05 | 0/8 | 1.07 |
| theDotNetWay | 2 | 1.07 | 1.04 | 0/8 | 1.08 |
| theDotNetWay | 3 | 1.05 | 1.06 | 0/8 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 229,916 | 0 | 0 | 0 | 0 |
| F# | messages_page | 227,652 | 0 | 0 | 0 | 0 |
| F# | search | 216,294 | 0 | 0 | 0 | 0 |
| F# | post_message | 76,112 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 236,534 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 229,896 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 217,569 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 74,907 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | room_show | 16 | 30.0 [30.0–90.0] | 2/3 **←** | 0.99 |
| F# | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| F# | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 32,379 | 32,379 | yes |
| F# | 1 | run c=1 | 8,879 | 8,879 | yes |
| F# | 1 | warm-up c=16 | 58,765 | 58,765 | yes |
| F# | 1 | run c=16 | 15,959 | 15,959 | yes |
| F# | 1 | **all** | 115,982 | 115,982 | yes |
| F# | 2 | warm-up c=1 | 33,626 | 33,626 | yes |
| F# | 2 | run c=1 | 9,290 | 9,290 | yes |
| F# | 2 | warm-up c=16 | 62,321 | 62,321 | yes |
| F# | 2 | run c=16 | 16,542 | 16,542 | yes |
| F# | 2 | **all** | 121,779 | 121,779 | yes |
| F# | 3 | warm-up c=1 | 33,309 | 33,309 | yes |
| F# | 3 | run c=1 | 9,049 | 9,049 | yes |
| F# | 3 | warm-up c=16 | 61,893 | 61,893 | yes |
| F# | 3 | run c=16 | 16,393 | 16,393 | yes |
| F# | 3 | **all** | 120,644 | 120,644 | yes |
| theDotNetWay | 1 | warm-up c=1 | 31,615 | 31,615 | yes |
| theDotNetWay | 1 | run c=1 | 8,738 | 8,738 | yes |
| theDotNetWay | 1 | warm-up c=16 | 60,457 | 60,457 | yes |
| theDotNetWay | 1 | run c=16 | 16,140 | 16,140 | yes |
| theDotNetWay | 1 | **all** | 116,950 | 116,950 | yes |
| theDotNetWay | 2 | warm-up c=1 | 32,124 | 32,124 | yes |
| theDotNetWay | 2 | run c=1 | 8,854 | 8,854 | yes |
| theDotNetWay | 2 | warm-up c=16 | 61,008 | 61,008 | yes |
| theDotNetWay | 2 | run c=16 | 16,202 | 16,202 | yes |
| theDotNetWay | 2 | **all** | 118,188 | 118,188 | yes |
| theDotNetWay | 3 | warm-up c=1 | 31,891 | 31,891 | yes |
| theDotNetWay | 3 | run c=1 | 8,798 | 8,798 | yes |
| theDotNetWay | 3 | warm-up c=16 | 60,536 | 60,536 | yes |
| theDotNetWay | 3 | run c=16 | 16,175 | 16,175 | yes |
| theDotNetWay | 3 | **all** | 117,400 | 117,400 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 36,115 / 376,570 | 200 36,115 / 376,570 | 200 376,570 | 200 376,570 | 36,115 | 36,115 | – |
| messages_page | 200 23,443 / 345,884 | 200 23,443 / 345,884 | 200 345,884 | 200 345,884 | 23,443 | 23,443 | – |
| search | 200 14,836 / 136,633 | 200 14,836 / 136,633 | 200 136,633 | 200 136,633 | 14,836 | 14,836 | – |
| post_message | – | – | – | – | 2,487 | 2,487 | – |
