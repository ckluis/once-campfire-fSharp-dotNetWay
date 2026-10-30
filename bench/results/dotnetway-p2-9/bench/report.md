```
date: 2026-10-20T14:08:19-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:0351727787eda91a77ee71f6c967a3d424791cd6855d4026678cc02bec39dbf6 2026-10-20T14:02:02.548280941-04:00 built from commit: dee7ec50d4caeed5111aa43685d6a14c4d32f741
repo HEAD: dee7ec5 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 374 [360–383] |
| idle memory.current (MB) | 48.0 [48.0–48.0] |
| idle anon (MB) | 25.0 [24.0–25.0] |
| peak memory.current under load (MB) | 258 [249–259] |
| peak anon under load (MB) | 173 [171–173] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,647 [1,585–1,684] |
| room_show c=1 CPU µs/req | 701 [690–726] |
| room_show c=1 p50 ms | 0.58 [0.57–0.61] |
| room_show c=1 p99 ms | 1.50 [0.93–1.80] |
| room_show c=16 req/s | 5,413 [4,946–5,462] |
| room_show c=16 CPU µs/req | 637 [632–709] |
| room_show c=16 p50 ms | 2.38 [2.36–2.67] |
| room_show c=16 p99 ms | 7.65 [7.56–7.90] |
| messages_page c=1 req/s | 1,489 [1,445–1,524] |
| messages_page c=1 CPU µs/req | 766 [754–790] |
| messages_page c=1 p50 ms | 0.64 [0.63–0.66] |
| messages_page c=1 p99 ms | 2.73 [2.67–2.78] |
| messages_page c=16 req/s | 5,099 [4,626–5,132] |
| messages_page c=16 CPU µs/req | 679 [674–758] |
| messages_page c=16 p50 ms | 2.58 [2.55–2.88] |
| messages_page c=16 p99 ms | 7.72 [7.69–8.24] |
| sidebar c=1 req/s | 1,556 [1,501–1,597] |
| sidebar c=1 CPU µs/req | 736 [724–766] |
| sidebar c=1 p50 ms | 0.61 [0.59–0.63] |
| sidebar c=1 p99 ms | 0.88 [0.83–0.90] |
| sidebar c=16 req/s | 5,469 [5,039–5,492] |
| sidebar c=16 CPU µs/req | 635 [631–697] |
| sidebar c=16 p50 ms | 2.42 [2.40–2.67] |
| sidebar c=16 p99 ms | 7.82 [7.74–8.26] |
| search c=1 req/s | 1,392 [1,367–1,421] |
| search c=1 CPU µs/req | 814 [801–830] |
| search c=1 p50 ms | 0.69 [0.67–0.70] |
| search c=1 p99 ms | 2.78 [2.77–2.81] |
| search c=16 req/s | 4,917 [4,700–4,983] |
| search c=16 CPU µs/req | 715 [703–753] |
| search c=16 p50 ms | 2.71 [2.66–2.86] |
| search c=16 p99 ms | 7.83 [7.79–8.02] |
| post_message c=1 req/s | 916 [904–930] |
| post_message c=1 CPU µs/req | 1,869 [1,864–1,925] |
| post_message c=1 p50 ms | 0.95 [0.95–0.98] |
| post_message c=1 p99 ms | 4.25 [4.19–4.95] |
| post_message c=16 req/s | 1,854 [1,814–1,878] |
| post_message c=16 CPU µs/req | 1,654 [1,651–1,719] |
| post_message c=16 p50 ms | 8.15 [8.04–8.18] |
| post_message c=16 p99 ms | 15.2 [14.9–16.7] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.39 1.69 1.31 | 2.70 2.41 1.79 |
| theDotNetWay | 2 | 1.35 2.09 1.71 | 2.90 2.40 1.96 |
| theDotNetWay | 3 | 1.41 2.08 1.87 | 2.81 2.48 2.13 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.08 | 1.05 | 0/10 | 1.08 |
| theDotNetWay | 2 | 1.07 | 1.07 | 0/10 | 1.07 |
| theDotNetWay | 3 | 1.07 | 1.04 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 165,923 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 154,575 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 165,311 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 150,284 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 66,403 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–32.2] | 3/3 | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 26,810 | 26,810 | yes |
| theDotNetWay | 1 | run c=1 | 7,332 | 7,332 | yes |
| theDotNetWay | 1 | warm-up c=16 | 58,316 | 58,316 | yes |
| theDotNetWay | 1 | run c=16 | 14,847 | 14,847 | yes |
| theDotNetWay | 1 | **all** | 107,305 | 107,305 | yes |
| theDotNetWay | 2 | warm-up c=1 | 26,570 | 26,570 | yes |
| theDotNetWay | 2 | run c=1 | 7,234 | 7,234 | yes |
| theDotNetWay | 2 | warm-up c=16 | 54,240 | 54,240 | yes |
| theDotNetWay | 2 | run c=16 | 14,519 | 14,519 | yes |
| theDotNetWay | 2 | **all** | 102,563 | 102,563 | yes |
| theDotNetWay | 3 | warm-up c=1 | 27,364 | 27,364 | yes |
| theDotNetWay | 3 | run c=1 | 7,440 | 7,440 | yes |
| theDotNetWay | 3 | warm-up c=16 | 56,153 | 56,153 | yes |
| theDotNetWay | 3 | run c=16 | 15,031 | 15,031 | yes |
| theDotNetWay | 3 | **all** | 105,988 | 105,988 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 37,238 / 416,122 | 200 416,122 | 37,238 | – |
| messages_page | 200 25,427 / 383,844 | 200 383,844 | 25,427 | – |
| sidebar | 200 7,919 / 30,763 | 200 30,763 | 7,919 | – |
| search | 200 16,029 / 149,623 | 200 149,623 | 16,029 | – |
| post_message | – | – | 2,684 | – |
