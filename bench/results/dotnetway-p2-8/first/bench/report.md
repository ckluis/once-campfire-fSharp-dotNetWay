```
date: 2026-10-20T12:24:17-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:7777de1bf77845a7ac2be5b893abdffe7985497eb4200233e1706bb1e256db27 2026-10-20T11:59:06.296142201-04:00 built from commit: 55b4e675aef0acc2eeaf19957fe083b504b552d6
repo HEAD: 55b4e67 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 397 [363–499] |
| idle memory.current (MB) | 48.0 [47.0–48.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 256 [253–272] |
| peak anon under load (MB) | 176 [172–180] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 957 [949–992] |
| room_show c=1 CPU µs/req | 1,832 [1,792–1,851] |
| room_show c=1 p50 ms | 1.00 [0.97–1.01] |
| room_show c=1 p99 ms | 2.60 [2.52–2.66] |
| room_show c=16 req/s | 3,633 [3,595–3,739] |
| room_show c=16 CPU µs/req | 975 [971–991] |
| room_show c=16 p50 ms | 3.73 [3.73–3.76] |
| room_show c=16 p99 ms | 10.4 [9.5–10.6] |
| messages_page c=1 req/s | 969 [967–979] |
| messages_page c=1 CPU µs/req | 1,652 [1,639–1,660] |
| messages_page c=1 p50 ms | 0.99 [0.97–0.99] |
| messages_page c=1 p99 ms | 3.23 [2.75–3.29] |
| messages_page c=16 req/s | 3,691 [3,658–3,822] |
| messages_page c=16 CPU µs/req | 960 [950–976] |
| messages_page c=16 p50 ms | 3.69 [3.66–3.70] |
| messages_page c=16 p99 ms | 10.2 [9.4–10.3] |
| sidebar c=1 req/s | 1,488 [1,485–1,496] |
| sidebar c=1 CPU µs/req | 768 [767–768] |
| sidebar c=1 p50 ms | 0.63 [0.63–0.63] |
| sidebar c=1 p99 ms | 1.07 [0.99–1.10] |
| sidebar c=16 req/s | 5,219 [5,187–5,294] |
| sidebar c=16 CPU µs/req | 665 [664–668] |
| sidebar c=16 p50 ms | 2.53 [2.53–2.55] |
| sidebar c=16 p99 ms | 8.29 [7.98–8.36] |
| search c=1 req/s | 1,070 [1,060–1,080] |
| search c=1 CPU µs/req | 1,286 [1,286–1,297] |
| search c=1 p50 ms | 0.89 [0.89–0.91] |
| search c=1 p99 ms | 3.10 [2.62–3.13] |
| search c=16 req/s | 3,911 [3,144–4,156] |
| search c=16 CPU µs/req | 904 [859–1,148] |
| search c=16 p50 ms | 3.30 [3.24–4.29] |
| search c=16 p99 ms | 11.7 [8.8–15.4] |
| post_message c=1 req/s | 872 [736–897] |
| post_message c=1 CPU µs/req | 1,951 [1,936–2,334] |
| post_message c=1 p50 ms | 0.99 [0.98–1.08] |
| post_message c=1 p99 ms | 5.15 [4.32–5.37] |
| post_message c=16 req/s | 1,753 [1,753–1,787] |
| post_message c=16 CPU µs/req | 1,761 [1,732–1,770] |
| post_message c=16 p50 ms | 9.07 [8.64–9.10] |
| post_message c=16 p99 ms | 17.3 [15.9–17.4] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 0.84 1.87 1.79 | 2.99 2.64 2.20 |
| theDotNetWay | 2 | 1.27 2.21 2.08 | 2.98 2.60 2.32 |
| theDotNetWay | 3 | 1.41 2.23 2.20 | 3.19 2.89 2.55 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.07 | 1.09 | 1/10 **noisy** | 1.15 |
| theDotNetWay | 2 | 1.09 | 1.08 | 1/10 **noisy** | 1.10 |
| theDotNetWay | 3 | 1.07 | 1.06 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 110,976 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 112,713 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 161,408 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 115,412 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 62,432 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 58.1 [30.0–90.2] | 2/3 **←** | 0.99 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–46.0] | 3/3 | 0.99 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–52.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–46.1] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–60.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 40.0 [30.0–44.0] | 3/3 | 0.95 |
| theDotNetWay | post_message | 1 | 32.0 [30.0–62.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–58.4] | 3/3 | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 24,530 | 24,530 | yes |
| theDotNetWay | 1 | run c=1 | 6,978 | 6,978 | yes |
| theDotNetWay | 1 | warm-up c=16 | 51,595 | 51,595 | yes |
| theDotNetWay | 1 | run c=16 | 14,034 | 14,034 | yes |
| theDotNetWay | 1 | **all** | 97,137 | 97,137 | yes |
| theDotNetWay | 2 | warm-up c=1 | 47,617 | 47,617 | yes |
| theDotNetWay | 2 | run c=1 | 5,889 | 5,889 | yes |
| theDotNetWay | 2 | warm-up c=16 | 82,175 | 82,175 | yes |
| theDotNetWay | 2 | run c=16 | 14,037 | 14,037 | yes |
| theDotNetWay | 2 | **all** | 149,718 | 149,718 | yes |
| theDotNetWay | 3 | warm-up c=1 | 26,320 | 26,320 | yes |
| theDotNetWay | 3 | run c=1 | 7,178 | 7,178 | yes |
| theDotNetWay | 3 | warm-up c=16 | 53,652 | 53,652 | yes |
| theDotNetWay | 3 | run c=16 | 14,316 | 14,316 | yes |
| theDotNetWay | 3 | **all** | 101,466 | 101,466 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 36,812 / 416,122 | 200 416,122 | 36,812 | – |
| messages_page | 200 24,252 / 383,844 | 200 383,844 | 24,252 | – |
| sidebar | 200 7,919 / 30,763 | 200 30,763 | 7,919 | – |
| search | 200 15,950 / 149,623 | 200 149,623 | 15,950 | – |
| post_message | – | – | 2,684 | – |
