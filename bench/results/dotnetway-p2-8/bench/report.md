```
date: 2026-10-20T13:01:49-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway image: campfire-dotnetway:app sha256:373802184f99e501000ef031a381f844234d48f7af7f8d5dee907c473acfd43f 2026-10-20T13:01:08.507145159-04:00 built from commit: 9d4efe25c4287a87a29ee8fc9697894bc6b10878
repo HEAD: 9d4efe2 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | theDotNetWay |
|---|---|
| cold start: docker run → /up 200 (ms) | 437 [397–457] |
| idle memory.current (MB) | 48.0 [47.0–48.0] |
| idle anon (MB) | 25.0 [24.0–25.0] |
| peak memory.current under load (MB) | 257 [256–260] |
| peak anon under load (MB) | 174 [172–175] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | theDotNetWay |
|---|---|
| room_show c=1 req/s | 1,654 [1,636–1,678] |
| room_show c=1 CPU µs/req | 704 [693–714] |
| room_show c=1 p50 ms | 0.58 [0.57–0.59] |
| room_show c=1 p99 ms | 0.98 [0.86–1.09] |
| room_show c=16 req/s | 5,492 [5,411–5,532] |
| room_show c=16 CPU µs/req | 629 [625–640] |
| room_show c=16 p50 ms | 2.36 [2.34–2.38] |
| room_show c=16 p99 ms | 7.39 [7.36–7.72] |
| messages_page c=1 req/s | 1,495 [1,482–1,509] |
| messages_page c=1 CPU µs/req | 767 [759–773] |
| messages_page c=1 p50 ms | 0.64 [0.63–0.64] |
| messages_page c=1 p99 ms | 2.68 [2.67–2.70] |
| messages_page c=16 req/s | 5,160 [5,106–5,188] |
| messages_page c=16 CPU µs/req | 670 [669–680] |
| messages_page c=16 p50 ms | 2.53 [2.53–2.57] |
| messages_page c=16 p99 ms | 7.59 [7.56–7.67] |
| sidebar c=1 req/s | 1,564 [1,540–1,573] |
| sidebar c=1 CPU µs/req | 737 [736–749] |
| sidebar c=1 p50 ms | 0.60 [0.60–0.61] |
| sidebar c=1 p99 ms | 0.88 [0.84–1.00] |
| sidebar c=16 req/s | 5,544 [5,237–5,553] |
| sidebar c=16 CPU µs/req | 627 [624–659] |
| sidebar c=16 p50 ms | 2.39 [2.38–2.50] |
| sidebar c=16 p99 ms | 7.74 [7.71–8.18] |
| search c=1 req/s | 1,361 [1,324–1,423] |
| search c=1 CPU µs/req | 832 [797–856] |
| search c=1 p50 ms | 0.70 [0.67–0.71] |
| search c=1 p99 ms | 2.81 [2.78–2.87] |
| search c=16 req/s | 4,802 [4,720–5,019] |
| search c=16 CPU µs/req | 731 [699–744] |
| search c=16 p50 ms | 2.75 [2.64–2.81] |
| search c=16 p99 ms | 8.38 [7.74–8.43] |
| post_message c=1 req/s | 913 [899–915] |
| post_message c=1 CPU µs/req | 1,906 [1,862–1,942] |
| post_message c=1 p50 ms | 0.97 [0.94–0.98] |
| post_message c=1 p99 ms | 4.36 [4.21–5.20] |
| post_message c=16 req/s | 1,832 [1,810–1,872] |
| post_message c=16 CPU µs/req | 1,683 [1,634–1,705] |
| post_message c=16 p50 ms | 8.67 [7.97–8.81] |
| post_message c=16 p99 ms | 16.3 [15.5–17.0] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| theDotNetWay | 1 | 1.31 3.50 2.99 | 2.92 2.80 2.82 |
| theDotNetWay | 2 | 1.41 2.42 2.69 | 2.95 2.52 2.59 |
| theDotNetWay | 3 | 1.43 2.18 2.47 | 2.66 2.41 2.44 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| theDotNetWay | 1 | 1.07 | 1.05 | 0/10 | 1.07 |
| theDotNetWay | 2 | 1.08 | 1.06 | 0/10 | 1.08 |
| theDotNetWay | 3 | 1.04 | 1.08 | 0/10 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| theDotNetWay | room_show | 171,283 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 159,546 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 168,128 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 149,237 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 65,972 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–38.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–36.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| theDotNetWay | 1 | warm-up c=1 | 27,167 | 27,167 | yes |
| theDotNetWay | 1 | run c=1 | 7,323 | 7,323 | yes |
| theDotNetWay | 1 | warm-up c=16 | 54,650 | 54,650 | yes |
| theDotNetWay | 1 | run c=16 | 14,998 | 14,998 | yes |
| theDotNetWay | 1 | **all** | 104,138 | 104,138 | yes |
| theDotNetWay | 2 | warm-up c=1 | 29,047 | 29,047 | yes |
| theDotNetWay | 2 | run c=1 | 7,191 | 7,191 | yes |
| theDotNetWay | 2 | warm-up c=16 | 54,800 | 54,800 | yes |
| theDotNetWay | 2 | run c=16 | 14,665 | 14,665 | yes |
| theDotNetWay | 2 | **all** | 105,703 | 105,703 | yes |
| theDotNetWay | 3 | warm-up c=1 | 26,963 | 26,963 | yes |
| theDotNetWay | 3 | run c=1 | 7,305 | 7,305 | yes |
| theDotNetWay | 3 | warm-up c=16 | 54,577 | 54,577 | yes |
| theDotNetWay | 3 | run c=16 | 14,490 | 14,490 | yes |
| theDotNetWay | 3 | **all** | 103,335 | 103,335 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | theDotNetWay gzip wire / decoded | theDotNetWay identity | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|
| room_show | 200 37,238 / 416,122 | 200 416,122 | 37,238 | – |
| messages_page | 200 25,427 / 383,844 | 200 383,844 | 25,427 | – |
| sidebar | 200 7,919 / 30,763 | 200 30,763 | 7,919 | – |
| search | 200 16,029 / 149,623 | 200 149,623 | 16,029 | – |
| post_message | – | – | 2,685 | – |
