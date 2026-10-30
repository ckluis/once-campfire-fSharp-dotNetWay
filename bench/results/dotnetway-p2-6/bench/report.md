```
date: 2026-10-20T09:54:31-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:a071f6c84ce5981e1dbe6b1a6d7ea127871930e473f79c0e991011eca33e0a03 2026-10-20T09:45:07.579770403-04:00 built from commit: ce87a52118d2a41d4735d5a4e158d7181bea8e1d
repo HEAD: ce87a52 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 354 [341–362] |
| idle memory.current (MB) | 48.0 [48.0–48.0] |
| idle anon (MB) | 25.0 [24.0–25.0] |
| peak memory.current under load (MB) | 310 [305–318] |
| peak anon under load (MB) | 230 [226–230] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,649 [1,624–1,655] |
| room_show c=1 CPU µs/req | 704 [701–706] |
| room_show c=1 p50 ms | 0.58 [0.58–0.59] |
| room_show c=1 p99 ms | 1.54 [1.00–2.09] |
| room_show c=16 req/s | 5,484 [5,483–5,511] |
| room_show c=16 CPU µs/req | 630 [627–632] |
| room_show c=16 p50 ms | 2.39 [2.38–2.40] |
| room_show c=16 p99 ms | 7.43 [7.38–7.50] |
| messages_page c=1 req/s | 1,495 [1,489–1,497] |
| messages_page c=1 CPU µs/req | 768 [767–770] |
| messages_page c=1 p50 ms | 0.64 [0.64–0.64] |
| messages_page c=1 p99 ms | 2.62 [2.58–2.62] |
| messages_page c=16 req/s | 5,167 [5,099–5,173] |
| messages_page c=16 CPU µs/req | 672 [671–682] |
| messages_page c=16 p50 ms | 2.60 [2.60–2.65] |
| messages_page c=16 p99 ms | 7.54 [7.40–7.54] |
| sidebar c=1 req/s | 1,666 [1,664–1,674] |
| sidebar c=1 CPU µs/req | 698 [697–700] |
| sidebar c=1 p50 ms | 0.57 [0.57–0.57] |
| sidebar c=1 p99 ms | 0.87 [0.79–1.06] |
| sidebar c=16 req/s | 5,850 [5,812–5,879] |
| sidebar c=16 CPU µs/req | 590 [589–594] |
| sidebar c=16 p50 ms | 2.26 [2.25–2.27] |
| sidebar c=16 p99 ms | 7.32 [7.27–7.40] |
| search c=1 req/s | 1,400 [1,398–1,412] |
| search c=1 CPU µs/req | 811 [808–814] |
| search c=1 p50 ms | 0.68 [0.68–0.69] |
| search c=1 p99 ms | 2.68 [2.65–2.68] |
| search c=16 req/s | 5,015 [4,994–5,045] |
| search c=16 CPU µs/req | 702 [699–705] |
| search c=16 p50 ms | 2.76 [2.74–2.77] |
| search c=16 p99 ms | 7.59 [7.46–7.71] |
| post_message c=1 req/s | 909 [901–915] |
| post_message c=1 CPU µs/req | 1,924 [1,922–1,925] |
| post_message c=1 p50 ms | 0.97 [0.97–0.98] |
| post_message c=1 p99 ms | 4.19 [4.16–4.73] |
| post_message c=16 req/s | 1,836 [1,666–1,848] |
| post_message c=16 CPU µs/req | 1,672 [1,662–1,866] |
| post_message c=16 p50 ms | 8.23 [8.16–9.28] |
| post_message c=16 p99 ms | 16.2 [15.1–18.1] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.11 1.70 1.36 | 2.58 2.26 1.74 |
| theDotNetWay | 2 | 1.49 2.02 1.68 | 2.72 2.47 2.00 |
| theDotNetWay | 3 | 1.32 2.14 1.91 | 2.72 2.47 2.14 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.05 | 1.07 | 0/10 | 1.09 |
| theDotNetWay | 2 | 1.05 | 1.06 | 0/10 | 1.07 |
| theDotNetWay | 3 | 1.08 | 1.06 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 171,282 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 159,385 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 180,384 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 154,145 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 64,658 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 26,172 | 26,172 | yes |
| theDotNetWay | 1 | run c=1 | 7,211 | 7,211 | yes |
| theDotNetWay | 1 | warm-up c=16 | 54,480 | 54,480 | yes |
| theDotNetWay | 1 | run c=16 | 14,695 | 14,695 | yes |
| theDotNetWay | 1 | **all** | 102,558 | 102,558 | yes |
| theDotNetWay | 2 | warm-up c=1 | 26,808 | 26,808 | yes |
| theDotNetWay | 2 | run c=1 | 7,324 | 7,324 | yes |
| theDotNetWay | 2 | warm-up c=16 | 55,747 | 55,747 | yes |
| theDotNetWay | 2 | run c=16 | 14,806 | 14,806 | yes |
| theDotNetWay | 2 | **all** | 104,685 | 104,685 | yes |
| theDotNetWay | 3 | warm-up c=1 | 26,644 | 26,644 | yes |
| theDotNetWay | 3 | run c=1 | 7,274 | 7,274 | yes |
| theDotNetWay | 3 | warm-up c=16 | 55,778 | 55,778 | yes |
| theDotNetWay | 3 | run c=16 | 13,348 | 13,348 | yes |
| theDotNetWay | 3 | **all** | 103,044 | 103,044 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 24,080 / 416,122 | 200 416,122 | 24,080 | – |
| messages_page | 200 15,967 / 383,844 | 200 383,844 | 15,967 | – |
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| search | 200 9,713 / 149,623 | 200 149,623 | 9,713 | – |
| post_message | – | – | 1,991 | – |
