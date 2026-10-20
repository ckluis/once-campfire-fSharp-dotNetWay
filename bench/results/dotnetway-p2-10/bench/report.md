```
date: 2026-10-20T15:09:26-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:608d9ae74150c1c35c9e518c1d68dd2be8c3afcd29962cf05f21bcdc80501835 2026-10-20T15:04:50.470943748-04:00 built from commit: 47aac9a365f94da1b000a3a4b46c3ef21a41c49e
repo HEAD: 47aac9a (dirty: 2 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 363 [339–368] |
| idle memory.current (MB) | 48.0 [47.0–48.0] |
| idle anon (MB) | 25.0 [25.0–25.0] |
| peak memory.current under load (MB) | 255 [253–257] |
| peak anon under load (MB) | 173 [171–175] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,642 [1,608–1,674] |
| room_show c=1 CPU µs/req | 709 [693–724] |
| room_show c=1 p50 ms | 0.58 [0.57–0.60] |
| room_show c=1 p99 ms | 1.07 [0.88–1.19] |
| room_show c=16 req/s | 5,387 [5,166–5,444] |
| room_show c=16 CPU µs/req | 640 [635–675] |
| room_show c=16 p50 ms | 2.41 [2.38–2.54] |
| room_show c=16 p99 ms | 7.70 [7.42–7.75] |
| messages_page c=1 req/s | 1,491 [1,430–1,528] |
| messages_page c=1 CPU µs/req | 770 [755–799] |
| messages_page c=1 p50 ms | 0.64 [0.62–0.67] |
| messages_page c=1 p99 ms | 2.62 [2.58–2.68] |
| messages_page c=16 req/s | 5,107 [4,846–5,144] |
| messages_page c=16 CPU µs/req | 679 [674–721] |
| messages_page c=16 p50 ms | 2.56 [2.56–2.74] |
| messages_page c=16 p99 ms | 7.88 [7.69–7.97] |
| sidebar c=1 req/s | 1,588 [1,471–1,597] |
| sidebar c=1 CPU µs/req | 730 [729–779] |
| sidebar c=1 p50 ms | 0.59 [0.59–0.64] |
| sidebar c=1 p99 ms | 0.88 [0.85–1.36] |
| sidebar c=16 req/s | 5,579 [4,716–5,616] |
| sidebar c=16 CPU µs/req | 619 [618–736] |
| sidebar c=16 p50 ms | 2.38 [2.36–2.80] |
| sidebar c=16 p99 ms | 7.59 [7.51–9.21] |
| search c=1 req/s | 1,401 [1,366–1,408] |
| search c=1 CPU µs/req | 814 [809–829] |
| search c=1 p50 ms | 0.68 [0.68–0.70] |
| search c=1 p99 ms | 2.69 [2.68–2.75] |
| search c=16 req/s | 4,920 [4,682–4,970] |
| search c=16 CPU µs/req | 709 [706–756] |
| search c=16 p50 ms | 2.71 [2.68–2.88] |
| search c=16 p99 ms | 7.90 [7.72–8.29] |
| post_message c=1 req/s | 905 [864–908] |
| post_message c=1 CPU µs/req | 1,927 [1,903–2,023] |
| post_message c=1 p50 ms | 0.96 [0.96–0.98] |
| post_message c=1 p99 ms | 4.32 [4.17–5.00] |
| post_message c=16 req/s | 1,838 [1,806–1,870] |
| post_message c=16 CPU µs/req | 1,653 [1,652–1,724] |
| post_message c=16 p50 ms | 8.28 [7.95–8.29] |
| post_message c=16 p99 ms | 15.5 [15.3–17.0] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.39 1.83 1.20 | 2.28 2.28 1.66 |
| theDotNetWay | 2 | 1.32 2.04 1.60 | 2.68 2.36 1.89 |
| theDotNetWay | 3 | 1.34 2.04 1.80 | 2.66 2.38 2.04 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.05 | 1.05 | 0/10 | 1.07 |
| theDotNetWay | 2 | 1.05 | 1.07 | 0/10 | 1.08 |
| theDotNetWay | 3 | 1.07 | 1.07 | 0/10 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 167,414 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 156,391 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 164,573 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 150,015 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 65,570 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–54.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.2] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 26,700 | 26,700 | yes |
| theDotNetWay | 1 | run c=1 | 7,242 | 7,242 | yes |
| theDotNetWay | 1 | warm-up c=16 | 54,800 | 54,800 | yes |
| theDotNetWay | 1 | run c=16 | 14,714 | 14,714 | yes |
| theDotNetWay | 1 | **all** | 103,456 | 103,456 | yes |
| theDotNetWay | 2 | warm-up c=1 | 27,188 | 27,188 | yes |
| theDotNetWay | 2 | run c=1 | 6,911 | 6,911 | yes |
| theDotNetWay | 2 | warm-up c=16 | 56,160 | 56,160 | yes |
| theDotNetWay | 2 | run c=16 | 14,973 | 14,973 | yes |
| theDotNetWay | 2 | **all** | 105,232 | 105,232 | yes |
| theDotNetWay | 3 | warm-up c=1 | 26,523 | 26,523 | yes |
| theDotNetWay | 3 | run c=1 | 7,260 | 7,260 | yes |
| theDotNetWay | 3 | warm-up c=16 | 54,316 | 54,316 | yes |
| theDotNetWay | 3 | run c=16 | 14,470 | 14,470 | yes |
| theDotNetWay | 3 | **all** | 102,569 | 102,569 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 37,238 / 416,122 | 200 416,122 | 37,238 | – |
| messages_page | 200 25,427 / 383,844 | 200 383,844 | 25,427 | – |
| sidebar | 200 7,919 / 30,763 | 200 30,763 | 7,919 | – |
| search | 200 16,029 / 149,623 | 200 149,623 | 16,029 | – |
| post_message | – | – | 2,684 | – |
