```
date: 2026-10-20T10:57:52-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:bee877ebe1b57f9d86c46b804a0d4f1bfc39c36d2f9efd59106423dd7d03b581 2026-10-20T10:48:48.506024614-04:00 built from commit: 8339b4dfcfbc1ebca2bdcfec26e05fde4477ab3a
repo HEAD: 8339b4d (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 387 [377–393] |
| idle memory.current (MB) | 48.0 [47.0–48.0] |
| idle anon (MB) | 24.0 [24.0–25.0] |
| peak memory.current under load (MB) | 312 [308–325] |
| peak anon under load (MB) | 225 [225–231] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,575 [1,527–1,652] |
| room_show c=1 CPU µs/req | 731 [703–754] |
| room_show c=1 p50 ms | 0.61 [0.58–0.62] |
| room_show c=1 p99 ms | 2.15 [1.01–2.16] |
| room_show c=16 req/s | 5,206 [4,978–5,474] |
| room_show c=16 CPU µs/req | 664 [633–698] |
| room_show c=16 p50 ms | 2.52 [2.40–2.66] |
| room_show c=16 p99 ms | 7.96 [7.43–8.12] |
| messages_page c=1 req/s | 1,407 [1,395–1,506] |
| messages_page c=1 CPU µs/req | 806 [763–816] |
| messages_page c=1 p50 ms | 0.67 [0.63–0.68] |
| messages_page c=1 p99 ms | 2.75 [2.56–2.78] |
| messages_page c=16 req/s | 4,798 [4,618–5,127] |
| messages_page c=16 CPU µs/req | 722 [678–754] |
| messages_page c=16 p50 ms | 2.77 [2.62–2.90] |
| messages_page c=16 p99 ms | 8.62 [7.55–8.75] |
| sidebar c=1 req/s | 1,585 [1,544–1,661] |
| sidebar c=1 CPU µs/req | 728 [702–747] |
| sidebar c=1 p50 ms | 0.59 [0.57–0.61] |
| sidebar c=1 p99 ms | 0.94 [0.84–0.96] |
| sidebar c=16 req/s | 5,078 [4,886–5,348] |
| sidebar c=16 CPU µs/req | 686 [643–704] |
| sidebar c=16 p50 ms | 2.51 [2.40–2.60] |
| sidebar c=16 p99 ms | 8.99 [8.68–12.38] |
| search c=1 req/s | 1,298 [1,290–1,324] |
| search c=1 CPU µs/req | 869 [852–871] |
| search c=1 p50 ms | 0.72 [0.72–0.73] |
| search c=1 p99 ms | 2.88 [2.73–2.90] |
| search c=16 req/s | 4,767 [4,494–4,852] |
| search c=16 CPU µs/req | 738 [723–788] |
| search c=16 p50 ms | 2.86 [2.78–3.07] |
| search c=16 p99 ms | 8.28 [8.15–8.58] |
| post_message c=1 req/s | 852 [845–865] |
| post_message c=1 CPU µs/req | 2,016 [2,010–2,044] |
| post_message c=1 p50 ms | 1.02 [1.02–1.04] |
| post_message c=1 p99 ms | 4.38 [4.36–5.24] |
| post_message c=16 req/s | 1,774 [1,594–1,804] |
| post_message c=16 CPU µs/req | 1,749 [1,712–1,962] |
| post_message c=16 p50 ms | 8.71 [8.19–9.88] |
| post_message c=16 p99 ms | 16.4 [15.7–19.2] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.03 1.65 1.23 | 2.56 2.28 1.68 |
| theDotNetWay | 2 | 1.33 1.99 1.61 | 2.77 2.42 1.93 |
| theDotNetWay | 3 | 1.39 2.10 1.84 | 2.76 2.49 2.13 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.06 | 1.07 | 0/10 | 1.09 |
| theDotNetWay | 2 | 1.05 | 1.06 | 0/10 | 1.08 |
| theDotNetWay | 3 | 1.07 | 1.08 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 163,338 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 150,866 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 160,951 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 144,255 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 61,919 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–54.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 16 | 44.0 [30.0–50.3] | 3/3 | 0.95 |
| theDotNetWay | search | 1 | 32.0 [30.0–36.0] | 3/3 | 0.99 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 36.2 [30.2–56.3] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 25,140 | 25,140 | yes |
| theDotNetWay | 1 | run c=1 | 6,758 | 6,758 | yes |
| theDotNetWay | 1 | warm-up c=16 | 62,616 | 62,616 | yes |
| theDotNetWay | 1 | run c=16 | 14,439 | 14,439 | yes |
| theDotNetWay | 1 | **all** | 108,953 | 108,953 | yes |
| theDotNetWay | 2 | warm-up c=1 | 25,532 | 25,532 | yes |
| theDotNetWay | 2 | run c=1 | 6,920 | 6,920 | yes |
| theDotNetWay | 2 | warm-up c=16 | 52,975 | 52,975 | yes |
| theDotNetWay | 2 | run c=16 | 14,209 | 14,209 | yes |
| theDotNetWay | 2 | **all** | 99,636 | 99,636 | yes |
| theDotNetWay | 3 | warm-up c=1 | 23,962 | 23,962 | yes |
| theDotNetWay | 3 | run c=1 | 6,819 | 6,819 | yes |
| theDotNetWay | 3 | warm-up c=16 | 89,318 | 89,318 | yes |
| theDotNetWay | 3 | run c=16 | 12,774 | 12,774 | yes |
| theDotNetWay | 3 | **all** | 132,873 | 132,873 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 24,080 / 416,122 | 200 416,122 | 24,080 | – |
| messages_page | 200 15,967 / 383,844 | 200 383,844 | 15,967 | – |
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| search | 200 9,713 / 149,623 | 200 149,623 | 9,713 | – |
| post_message | – | – | 1,991 | – |
