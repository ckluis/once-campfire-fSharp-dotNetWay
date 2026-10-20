```
date: 2026-10-20T09:01:46-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:061d5e8535afb8033b656ee484fd897b33736fdde064caa7bfbc4018a56e3645 2026-10-20T08:57:39.546113269-04:00 built from commit: 72ed71d6412ce5f473eea619027ed35ec4110700
repo HEAD: 72ed71d (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 414 [402–448] |
| idle memory.current (MB) | 47.0 [47.0–48.0] |
| idle anon (MB) | 24.0 [24.0–24.0] |
| peak memory.current under load (MB) | 308 [306–309] |
| peak anon under load (MB) | 234 [231–234] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,612 [1,589–1,628] |
| room_show c=1 CPU µs/req | 719 [713–722] |
| room_show c=1 p50 ms | 0.59 [0.59–0.60] |
| room_show c=1 p99 ms | 1.42 [1.27–1.67] |
| room_show c=16 req/s | 5,393 [4,919–5,428] |
| room_show c=16 CPU µs/req | 640 [635–702] |
| room_show c=16 p50 ms | 2.43 [2.41–2.67] |
| room_show c=16 p99 ms | 7.73 [7.59–8.62] |
| messages_page c=1 req/s | 1,466 [1,458–1,472] |
| messages_page c=1 CPU µs/req | 778 [778–782] |
| messages_page c=1 p50 ms | 0.65 [0.65–0.65] |
| messages_page c=1 p99 ms | 2.73 [2.68–2.73] |
| messages_page c=16 req/s | 5,130 [5,076–5,141] |
| messages_page c=16 CPU µs/req | 675 [674–681] |
| messages_page c=16 p50 ms | 2.59 [2.58–2.62] |
| messages_page c=16 p99 ms | 7.58 [7.51–7.74] |
| sidebar c=1 req/s | 1,641 [1,640–1,656] |
| sidebar c=1 CPU µs/req | 706 [702–708] |
| sidebar c=1 p50 ms | 0.58 [0.57–0.58] |
| sidebar c=1 p99 ms | 0.80 [0.78–0.85] |
| sidebar c=16 req/s | 5,734 [5,693–5,782] |
| sidebar c=16 CPU µs/req | 599 [595–600] |
| sidebar c=16 p50 ms | 2.29 [2.26–2.29] |
| sidebar c=16 p99 ms | 7.54 [7.53–7.72] |
| search c=1 req/s | 1,382 [1,378–1,383] |
| search c=1 CPU µs/req | 821 [819–824] |
| search c=1 p50 ms | 0.69 [0.69–0.69] |
| search c=1 p99 ms | 2.78 [2.76–2.81] |
| search c=16 req/s | 4,955 [4,935–4,988] |
| search c=16 CPU µs/req | 709 [699–709] |
| search c=16 p50 ms | 2.75 [2.73–2.76] |
| search c=16 p99 ms | 7.78 [7.71–7.90] |
| post_message c=1 req/s | 891 [886–905] |
| post_message c=1 CPU µs/req | 1,935 [1,932–1,952] |
| post_message c=1 p50 ms | 0.98 [0.98–0.99] |
| post_message c=1 p99 ms | 4.94 [4.26–5.02] |
| post_message c=16 req/s | 1,822 [1,677–1,839] |
| post_message c=16 CPU µs/req | 1,686 [1,671–1,862] |
| post_message c=16 p50 ms | 8.27 [7.65–9.17] |
| post_message c=16 p99 ms | 15.9 [15.5–19.3] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.20 1.43 1.13 | 2.32 2.22 1.62 |
| theDotNetWay | 2 | 1.37 1.99 1.57 | 3.36 2.65 2.00 |
| theDotNetWay | 3 | 1.26 2.16 1.87 | 2.97 2.59 2.16 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.05 | 1.06 | 0/10 | 1.07 |
| theDotNetWay | 2 | 1.07 | 1.09 | 0/10 | 1.08 |
| theDotNetWay | 3 | 1.08 | 1.06 | 0/10 | 1.10 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 164,599 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 157,979 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 177,208 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 152,196 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 64,202 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–32.2] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 25,884 | 25,884 | yes |
| theDotNetWay | 1 | run c=1 | 7,088 | 7,088 | yes |
| theDotNetWay | 1 | warm-up c=16 | 57,356 | 57,356 | yes |
| theDotNetWay | 1 | run c=16 | 14,584 | 14,584 | yes |
| theDotNetWay | 1 | **all** | 104,912 | 104,912 | yes |
| theDotNetWay | 2 | warm-up c=1 | 26,612 | 26,612 | yes |
| theDotNetWay | 2 | run c=1 | 7,238 | 7,238 | yes |
| theDotNetWay | 2 | warm-up c=16 | 55,170 | 55,170 | yes |
| theDotNetWay | 2 | run c=16 | 13,432 | 13,432 | yes |
| theDotNetWay | 2 | **all** | 102,452 | 102,452 | yes |
| theDotNetWay | 3 | warm-up c=1 | 26,148 | 26,148 | yes |
| theDotNetWay | 3 | run c=1 | 7,128 | 7,128 | yes |
| theDotNetWay | 3 | warm-up c=16 | 54,077 | 54,077 | yes |
| theDotNetWay | 3 | run c=16 | 14,732 | 14,732 | yes |
| theDotNetWay | 3 | **all** | 102,085 | 102,085 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 24,080 / 416,122 | 200 416,122 | 24,080 | – |
| messages_page | 200 15,967 / 383,844 | 200 383,844 | 15,967 | – |
| sidebar | 200 5,910 / 30,763 | 200 30,763 | 5,910 | – |
| search | 200 9,713 / 149,623 | 200 149,623 | 9,713 | – |
| post_message | – | – | 1,991 | – |
