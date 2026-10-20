```
date: 2026-10-20T18:47:59-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:1ae1604eeb2190d02e45c12b07883d8cd3bd3055bb5617eb4482611f156ac312 2026-10-20T18:47:28.191575457-04:00 built from commit: 09bf227a3825a2820006dfcacf51e792fea89da9
repo HEAD: 09bf227 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 417 [413–447] |
| idle memory.current (MB) | 48.0 [48.0–48.0] |
| idle anon (MB) | 25.0 [25.0–25.0] |
| peak memory.current under load (MB) | 253 [252–257] |
| peak anon under load (MB) | 176 [173–177] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,706 [1,697–1,718] |
| room_show c=1 CPU µs/req | 684 [678–687] |
| room_show c=1 p50 ms | 0.56 [0.56–0.57] |
| room_show c=1 p99 ms | 1.00 [0.87–1.01] |
| room_show c=16 req/s | 5,545 [5,527–5,552] |
| room_show c=16 CPU µs/req | 626 [624–628] |
| room_show c=16 p50 ms | 2.33 [2.33–2.35] |
| room_show c=16 p99 ms | 7.47 [7.37–7.47] |
| messages_page c=1 req/s | 1,538 [1,537–1,550] |
| messages_page c=1 CPU µs/req | 745 [741–746] |
| messages_page c=1 p50 ms | 0.62 [0.62–0.62] |
| messages_page c=1 p99 ms | 2.55 [2.54–2.57] |
| messages_page c=16 req/s | 5,243 [5,210–5,256] |
| messages_page c=16 CPU µs/req | 666 [664–668] |
| messages_page c=16 p50 ms | 2.52 [2.51–2.52] |
| messages_page c=16 p99 ms | 7.50 [7.42–7.68] |
| sidebar c=1 req/s | 1,645 [1,630–1,648] |
| sidebar c=1 CPU µs/req | 708 [705–713] |
| sidebar c=1 p50 ms | 0.58 [0.58–0.58] |
| sidebar c=1 p99 ms | 0.84 [0.82–0.90] |
| sidebar c=16 req/s | 5,712 [5,695–5,719] |
| sidebar c=16 CPU µs/req | 607 [607–610] |
| sidebar c=16 p50 ms | 2.32 [2.31–2.33] |
| sidebar c=16 p99 ms | 7.46 [7.43–7.54] |
| search c=1 req/s | 1,455 [1,453–1,458] |
| search c=1 CPU µs/req | 782 [782–784] |
| search c=1 p50 ms | 0.66 [0.66–0.66] |
| search c=1 p99 ms | 2.62 [2.56–2.62] |
| search c=16 req/s | 5,082 [5,050–5,120] |
| search c=16 CPU µs/req | 695 [690–696] |
| search c=16 p50 ms | 2.63 [2.62–2.64] |
| search c=16 p99 ms | 7.69 [7.54–7.71] |
| post_message c=1 req/s | 952 [932–957] |
| post_message c=1 CPU µs/req | 1,840 [1,835–1,845] |
| post_message c=1 p50 ms | 0.93 [0.92–0.93] |
| post_message c=1 p99 ms | 4.09 [4.07–5.19] |
| post_message c=16 req/s | 1,902 [1,886–1,915] |
| post_message c=16 CPU µs/req | 1,619 [1,619–1,638] |
| post_message c=16 p50 ms | 7.87 [7.70–8.52] |
| post_message c=16 p99 ms | 15.5 [14.8–15.7] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.34 3.26 2.77 | 2.63 2.67 2.64 |
| theDotNetWay | 2 | 1.32 2.31 2.52 | 3.10 2.59 2.54 |
| theDotNetWay | 3 | 1.25 2.14 2.39 | 2.56 2.33 2.38 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.05 | 1.04 | 0/10 | 1.08 |
| theDotNetWay | 2 | 1.06 | 1.07 | 0/10 | 1.09 |
| theDotNetWay | 3 | 1.04 | 1.04 | 0/10 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 174,039 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 162,706 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 176,439 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 156,986 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 68,382 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.2] | 3/3 | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 27,455 | 27,455 | yes |
| theDotNetWay | 1 | run c=1 | 7,455 | 7,455 | yes |
| theDotNetWay | 1 | warm-up c=16 | 55,785 | 55,785 | yes |
| theDotNetWay | 1 | run c=16 | 15,100 | 15,100 | yes |
| theDotNetWay | 1 | **all** | 105,795 | 105,795 | yes |
| theDotNetWay | 2 | warm-up c=1 | 28,042 | 28,042 | yes |
| theDotNetWay | 2 | run c=1 | 7,657 | 7,657 | yes |
| theDotNetWay | 2 | warm-up c=16 | 57,240 | 57,240 | yes |
| theDotNetWay | 2 | run c=16 | 15,331 | 15,331 | yes |
| theDotNetWay | 2 | **all** | 108,270 | 108,270 | yes |
| theDotNetWay | 3 | warm-up c=1 | 27,936 | 27,936 | yes |
| theDotNetWay | 3 | run c=1 | 7,615 | 7,615 | yes |
| theDotNetWay | 3 | warm-up c=16 | 57,779 | 57,779 | yes |
| theDotNetWay | 3 | run c=16 | 15,224 | 15,224 | yes |
| theDotNetWay | 3 | **all** | 108,554 | 108,554 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 37,238 / 416,122 | 200 416,122 | 37,238 | – |
| messages_page | 200 25,427 / 383,844 | 200 383,844 | 25,427 | – |
| sidebar | 200 7,919 / 30,763 | 200 30,763 | 7,919 | – |
| search | 200 16,029 / 149,623 | 200 149,623 | 16,029 | – |
| post_message | – | – | 2,684 | – |
