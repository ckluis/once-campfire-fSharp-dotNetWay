```
date: 2026-10-26T16:03:48-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p7pre sha256:8d56675d7aaebb0b084eaf0c00285cfbc4fa989bde359508b238ef02015e5baf 2026-10-26T12:58:55.33352898-04:00 built from commit: 0328698
dotnetway image: campfire-dotnetway:app sha256:80e1e11ad98c409558636b5b05a46af928795689c0a2fd7927b0a3cd85f24b5e 2026-10-26T15:57:32.412484684-04:00 built from commit: 0099ef0b4064a56d1107be4841372f6b5f13cd2f
repo HEAD: 0099ef0 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message avatar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 974 [805–1,296] | 950 [679–995] |
| idle memory.current (MB) | 75.0 [75.0–153.0] | 83.0 [83.0–83.0] |
| idle anon (MB) | 49.0 [49.0–49.0] | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 408 [403–463] | 409 [394–420] |
| peak anon under load (MB) | 281 [278–283] | 286 [283–289] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,045 [1,540–2,132] | 1,877 [1,713–2,149] |
| room_show c=1 CPU µs/req | 599 [586–747] | 644 [575–678] |
| room_show c=1 p50 ms | 0.47 [0.44–0.58] | 0.49 [0.44–0.51] |
| room_show c=1 p99 ms | 1.01 [0.73–1.83] | 1.48 [0.65–2.58] |
| room_show c=16 req/s | 6,768 [6,088–7,091] | 6,896 [6,385–7,210] |
| room_show c=16 CPU µs/req | 486 [470–540] | 476 [462–524] |
| room_show c=16 p50 ms | 1.85 [1.78–2.08] | 1.82 [1.75–1.99] |
| room_show c=16 p99 ms | 7.01 [6.47–7.75] | 6.78 [6.43–7.20] |
| messages_page c=1 req/s | 1,869 [1,762–1,933] | 1,933 [1,844–1,970] |
| messages_page c=1 CPU µs/req | 644 [629–676] | 627 [615–651] |
| messages_page c=1 p50 ms | 0.51 [0.48–0.51] | 0.49 [0.48–0.52] |
| messages_page c=1 p99 ms | 1.04 [0.84–2.07] | 0.81 [0.79–1.02] |
| messages_page c=16 req/s | 6,467 [5,470–6,980] | 6,813 [6,360–6,842] |
| messages_page c=16 CPU µs/req | 521 [484–600] | 503 [496–531] |
| messages_page c=16 p50 ms | 1.92 [1.82–2.28] | 1.85 [1.84–2.00] |
| messages_page c=16 p99 ms | 7.76 [6.97–9.67] | 7.26 [7.18–7.58] |
| sidebar c=1 req/s | 1,906 [1,862–1,935] | 2,055 [1,895–2,097] |
| sidebar c=1 CPU µs/req | 634 [626–650] | 595 [587–637] |
| sidebar c=1 p50 ms | 0.48 [0.48–0.49] | 0.46 [0.45–0.50] |
| sidebar c=1 p99 ms | 0.93 [0.87–2.13] | 0.89 [0.68–0.98] |
| sidebar c=16 req/s | 6,954 [5,642–7,141] | 6,433 [4,872–6,903] |
| sidebar c=16 CPU µs/req | 484 [466–593] | 521 [482–659] |
| sidebar c=16 p50 ms | 1.82 [1.76–2.15] | 1.96 [1.82–1.98] |
| sidebar c=16 p99 ms | 7.05 [6.80–10.10] | 7.69 [7.15–20.67] |
| search c=1 req/s | 1,699 [1,555–1,747] | 1,819 [1,553–1,837] |
| search c=1 CPU µs/req | 699 [682–754] | 658 [655–753] |
| search c=1 p50 ms | 0.56 [0.55–0.57] | 0.52 [0.52–0.59] |
| search c=1 p99 ms | 1.13 [0.90–2.52] | 0.82 [0.79–2.24] |
| search c=16 req/s | 6,192 [5,767–6,354] | 6,748 [6,479–6,788] |
| search c=16 CPU µs/req | 555 [541–606] | 506 [505–536] |
| search c=16 p50 ms | 2.08 [2.04–2.19] | 1.92 [1.91–2.02] |
| search c=16 p99 ms | 7.28 [7.04–9.04] | 6.61 [6.43–6.67] |
| avatar c=1 req/s | 12,006 [11,510–12,031] | 12,057 [11,649–12,585] |
| avatar c=1 CPU µs/req | 167 [166–171] | 163 [160–170] |
| avatar c=1 p50 ms | 0.08 [0.08–0.08] | 0.08 [0.08–0.08] |
| avatar c=1 p99 ms | 0.15 [0.15–0.16] | 0.16 [0.14–0.16] |
| avatar c=16 req/s | 84,993 [83,852–88,034] | 89,541 [84,950–96,282] |
| avatar c=16 CPU µs/req | 29.7 [28.6–30.3] | 27.8 [26.4–28.9] |
| avatar c=16 p50 ms | 0.16 [0.15–0.16] | 0.15 [0.14–0.16] |
| avatar c=16 p99 ms | 0.53 [0.49–0.54] | 0.47 [0.47–0.61] |
| post_message c=1 req/s | 866 [642–931] | 1,001 [910–1,006] |
| post_message c=1 CPU µs/req | 1,996 [1,867–2,674] | 1,793 [1,782–1,955] |
| post_message c=1 p50 ms | 0.91 [0.91–1.22] | 0.86 [0.85–0.93] |
| post_message c=1 p99 ms | 5.25 [5.16–7.04] | 5.50 [5.25–5.65] |
| post_message c=16 req/s | 1,578 [1,428–1,845] | 1,837 [806–2,055] |
| post_message c=16 CPU µs/req | 2,062 [1,735–2,273] | 1,747 [1,560–3,744] |
| post_message c=16 p50 ms | 8.86 [7.36–10.12] | 7.50 [6.32–12.01] |
| post_message c=16 p99 ms | 23.6 [17.7–30.6] | 18.2 [16.3–272.6] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 0.47 1.63 1.99 | 3.22 3.29 2.70 |
| F# | 2 | 1.40 2.62 2.77 | 3.49 3.19 2.95 |
| F# | 3 | 0.45 1.94 2.51 | 3.91 3.44 2.95 |
| theDotNetWay | 1 | 1.36 2.75 2.55 | 4.02 3.62 3.02 |
| theDotNetWay | 2 | 1.44 2.93 2.81 | 3.31 3.14 2.94 |
| theDotNetWay | 3 | 1.28 2.72 2.74 | 3.00 3.28 3.02 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.07 | 1.08 | 1/12 **noisy** | 1.11 |
| F# | 2 | 1.06 | 1.10 | 0/12 | 1.10 |
| F# | 3 | 1.09 | 1.09 | 2/12 **noisy** | 1.14 |
| theDotNetWay | 1 | 1.06 | 1.09 | 1/12 **noisy** | 1.12 |
| theDotNetWay | 2 | 1.10 | 1.10 | 1/12 **noisy** | 1.10 |
| theDotNetWay | 3 | 1.08 | 1.06 | 1/12 **noisy** | 1.11 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 205,396 | 0 | 0 | 0 | 0 |
| F# | messages_page | 195,892 | 0 | 0 | 0 | 0 |
| F# | sidebar | 203,569 | 0 | 0 | 0 | 0 |
| F# | search | 186,542 | 0 | 0 | 0 | 0 |
| F# | avatar | 2,339,591 | 0 | 0 | 0 | 0 |
| F# | post_message | 58,381 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 209,913 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 206,130 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 194,079 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 201,850 | 0 | 0 | 0 | 0 |
| theDotNetWay | avatar | 2,456,691 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 60,948 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–90.0] | 2/3 **←** | 0.99 |
| F# | room_show | 16 | 40.0 [30.0–82.1] | 3/3 | 0.97 |
| F# | messages_page | 1 | 66.0 [30.0–90.0] | 2/3 **←** | 1.03 |
| F# | messages_page | 16 | 70.1 [36.0–78.2] | 3/3 | 0.95 |
| F# | sidebar | 1 | 56.0 [46.0–86.3] | 3/3 | 0.99 |
| F# | sidebar | 16 | 36.0 [30.0–90.1] | 2/3 **←** | 1.02 |
| F# | search | 1 | 54.1 [30.0–84.0] | 3/3 | 0.97 |
| F# | search | 16 | 46.0 [44.0–90.0] | 2/3 **←** | 0.99 |
| F# | avatar | 1 | 32.0 [32.0–56.0] | 3/3 | 1.01 |
| F# | avatar | 16 | 30.0 [30.0–38.0] | 3/3 | 0.98 |
| F# | post_message | 1 | 30.0 [30.0–90.1] | 2/3 **←** | 0.99 |
| F# | post_message | 16 | 72.3 [44.3–90.5] | 2/3 **←** | 0.96 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–34.0] | 3/3 | 0.98 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–50.0] | 3/3 | 1.01 |
| theDotNetWay | messages_page | 1 | 42.0 [30.0–52.0] | 3/3 | 1.01 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–36.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | sidebar | 16 | 88.1 [38.0–90.0] | 2/3 **←** | 1.00 |
| theDotNetWay | search | 1 | 34.0 [32.0–40.0] | 3/3 | 0.99 |
| theDotNetWay | search | 16 | 32.0 [30.0–76.0] | 3/3 | 1.02 |
| theDotNetWay | avatar | 1 | 30.0 [30.0–40.0] | 3/3 | 0.99 |
| theDotNetWay | avatar | 16 | 90.0 [30.0–90.0] | 1/3 **←** | 1.00 |
| theDotNetWay | post_message | 1 | 44.0 [32.0–90.0] | 2/3 **←** | 1.01 |
| theDotNetWay | post_message | 16 | 90.5 [36.2–90.5] | 1/3 **←** | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 28,404 | 28,404 | yes |
| F# | 1 | run c=1 | 6,931 | 6,931 | yes |
| F# | 1 | warm-up c=16 | 71,255 | 71,255 | yes |
| F# | 1 | run c=16 | 12,640 | 12,640 | yes |
| F# | 1 | **all** | 119,230 | 119,230 | yes |
| F# | 2 | warm-up c=1 | 51,089 | 51,089 | yes |
| F# | 2 | run c=1 | 5,139 | 5,139 | yes |
| F# | 2 | warm-up c=16 | 136,932 | 136,932 | yes |
| F# | 2 | run c=16 | 11,446 | 11,446 | yes |
| F# | 2 | **all** | 204,606 | 204,606 | yes |
| F# | 3 | warm-up c=1 | 26,921 | 26,921 | yes |
| F# | 3 | run c=1 | 7,446 | 7,446 | yes |
| F# | 3 | warm-up c=16 | 128,504 | 128,504 | yes |
| F# | 3 | run c=16 | 14,779 | 14,779 | yes |
| F# | 3 | **all** | 177,650 | 177,650 | yes |
| theDotNetWay | 1 | warm-up c=1 | 40,322 | 40,322 | yes |
| theDotNetWay | 1 | run c=1 | 8,007 | 8,007 | yes |
| theDotNetWay | 1 | warm-up c=16 | 157,085 | 157,085 | yes |
| theDotNetWay | 1 | run c=16 | 16,452 | 16,452 | yes |
| theDotNetWay | 1 | **all** | 221,866 | 221,866 | yes |
| theDotNetWay | 2 | warm-up c=1 | 31,181 | 31,181 | yes |
| theDotNetWay | 2 | run c=1 | 8,047 | 8,047 | yes |
| theDotNetWay | 2 | warm-up c=16 | 150,942 | 150,942 | yes |
| theDotNetWay | 2 | run c=16 | 6,456 | 6,456 | yes |
| theDotNetWay | 2 | **all** | 196,626 | 196,626 | yes |
| theDotNetWay | 3 | warm-up c=1 | 71,199 | 71,199 | yes |
| theDotNetWay | 3 | run c=1 | 7,277 | 7,277 | yes |
| theDotNetWay | 3 | warm-up c=16 | 64,855 | 64,855 | yes |
| theDotNetWay | 3 | run c=16 | 14,709 | 14,709 | yes |
| theDotNetWay | 3 | **all** | 158,040 | 158,040 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 37,356 / 377,518 | 200 37,356 / 377,518 | 200 377,518 | 200 377,518 | 37,356 | 37,356 | – |
| messages_page | 200 24,267 / 346,364 | 200 24,267 / 346,364 | 200 346,364 | 200 346,364 | 24,267 | 24,267 | – |
| sidebar | 200 7,979 / 30,144 | 200 7,979 / 30,144 | 200 30,144 | 200 30,144 | 7,979 | 7,979 | – |
| search | 200 15,053 / 137,229 | 200 15,053 / 137,229 | 200 137,229 | 200 137,229 | 15,053 | 15,053 | – |
| avatar | 200 3,364 / 3,364 | 200 3,364 / 3,364 | 200 3,364 | 200 3,364 | 3,364 | 3,364 | – |
| post_message | – | – | – | – | 2,494 | 2,494 | – |
