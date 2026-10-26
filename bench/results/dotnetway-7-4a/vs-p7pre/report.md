```
date: 2026-10-26T13:05:58-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p7pre sha256:8d56675d7aaebb0b084eaf0c00285cfbc4fa989bde359508b238ef02015e5baf 2026-10-26T12:58:55.33352898-04:00 built from commit: 0328698
dotnetway image: campfire-dotnetway:app sha256:89ef9d56dae9856bb2345ef6f2dd1338c88b3bcbff07dc27c695eb86db59200a 2026-10-26T12:59:37.344065286-04:00 built from commit: 414713f059dec669e9b469ca72692540b442f056
repo HEAD: 414713f (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message avatar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 515 [501–550] | 524 [518–531] |
| idle memory.current (MB) | 75.0 [75.0–75.0] | 83.0 [83.0–83.0] |
| idle anon (MB) | 49.0 [49.0–49.0] | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 374 [373–376] | 381 [379–404] |
| peak anon under load (MB) | 279 [278–281] | 286 [283–287] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,168 [2,090–2,180] | 2,192 [2,179–2,203] |
| room_show c=1 CPU µs/req | 574 [572–590] | 569 [565–572] |
| room_show c=1 p50 ms | 0.44 [0.44–0.46] | 0.43 [0.43–0.44] |
| room_show c=1 p99 ms | 0.66 [0.66–0.67] | 0.61 [0.61–0.63] |
| room_show c=16 req/s | 7,152 [7,128–7,491] | 7,481 [7,436–7,504] |
| room_show c=16 CPU µs/req | 476 [453–478] | 450 [448–452] |
| room_show c=16 p50 ms | 1.78 [1.68–1.78] | 1.69 [1.68–1.70] |
| room_show c=16 p99 ms | 6.24 [6.04–6.25] | 6.08 [6.04–6.16] |
| messages_page c=1 req/s | 2,020 [1,920–2,034] | 2,036 [2,036–2,050] |
| messages_page c=1 CPU µs/req | 606 [603–630] | 603 [599–604] |
| messages_page c=1 p50 ms | 0.47 [0.47–0.50] | 0.47 [0.47–0.47] |
| messages_page c=1 p99 ms | 0.67 [0.63–0.76] | 0.63 [0.62–0.63] |
| messages_page c=16 req/s | 6,888 [6,820–7,294] | 7,331 [7,282–7,416] |
| messages_page c=16 CPU µs/req | 501 [470–508] | 468 [465–471] |
| messages_page c=16 p50 ms | 1.85 [1.74–1.88] | 1.73 [1.72–1.75] |
| messages_page c=16 p99 ms | 6.83 [6.56–6.85] | 6.54 [6.36–6.62] |
| sidebar c=1 req/s | 2,095 [2,053–2,124] | 2,133 [2,132–2,141] |
| sidebar c=1 CPU µs/req | 588 [582–597] | 580 [579–581] |
| sidebar c=1 p50 ms | 0.46 [0.45–0.47] | 0.45 [0.45–0.45] |
| sidebar c=1 p99 ms | 0.65 [0.65–0.68] | 0.64 [0.63–0.65] |
| sidebar c=16 req/s | 7,276 [7,223–7,616] | 7,583 [7,460–7,727] |
| sidebar c=16 CPU µs/req | 471 [448–475] | 448 [442–456] |
| sidebar c=16 p50 ms | 1.75 [1.66–1.77] | 1.67 [1.64–1.70] |
| sidebar c=16 p99 ms | 6.50 [6.28–6.55] | 6.29 [6.16–6.46] |
| search c=1 req/s | 1,824 [1,804–1,876] | 1,879 [1,842–1,889] |
| search c=1 CPU µs/req | 659 [645–665] | 644 [642–656] |
| search c=1 p50 ms | 0.53 [0.51–0.53] | 0.51 [0.51–0.52] |
| search c=1 p99 ms | 0.71 [0.69–0.73] | 0.70 [0.69–1.29] |
| search c=16 req/s | 6,686 [6,675–7,091] | 7,101 [5,777–7,108] |
| search c=16 CPU µs/req | 528 [494–530] | 494 [492–602] |
| search c=16 p50 ms | 1.97 [1.84–1.98] | 1.84 [1.84–2.23] |
| search c=16 p99 ms | 6.16 [5.97–6.27] | 6.06 [5.92–8.47] |
| avatar c=1 req/s | 12,468 [12,359–12,505] | 12,907 [11,720–13,069] |
| avatar c=1 CPU µs/req | 164 [164–164] | 157 [157–166] |
| avatar c=1 p50 ms | 0.08 [0.08–0.08] | 0.07 [0.07–0.08] |
| avatar c=1 p99 ms | 0.13 [0.13–0.13] | 0.13 [0.12–0.17] |
| avatar c=16 req/s | 94,251 [92,588–97,833] | 98,255 [85,917–104,071] |
| avatar c=16 CPU µs/req | 27.8 [27.3–28.0] | 26.7 [24.8–29.1] |
| avatar c=16 p50 ms | 0.14 [0.13–0.14] | 0.14 [0.13–0.16] |
| avatar c=16 p99 ms | 0.47 [0.47–0.48] | 0.42 [0.39–0.56] |
| post_message c=1 req/s | 1,006 [973–1,050] | 1,025 [942–1,054] |
| post_message c=1 CPU µs/req | 1,806 [1,749–1,812] | 1,770 [1,749–1,892] |
| post_message c=1 p50 ms | 0.86 [0.84–0.88] | 0.84 [0.84–0.91] |
| post_message c=1 p99 ms | 4.22 [4.16–5.00] | 4.90 [4.25–5.45] |
| post_message c=16 req/s | 2,021 [2,018–2,132] | 2,118 [1,975–2,123] |
| post_message c=16 CPU µs/req | 1,615 [1,520–1,622] | 1,530 [1,528–1,648] |
| post_message c=16 p50 ms | 6.44 [6.08–6.46] | 6.09 [6.08–6.86] |
| post_message c=16 p99 ms | 15.2 [14.7–15.5] | 15.0 [14.7–15.8] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 0.37 1.00 0.74 | 3.32 2.63 1.63 |
| F# | 2 | 1.34 2.80 2.60 | 3.35 3.15 2.83 |
| F# | 3 | 1.42 2.64 2.67 | 3.89 3.47 3.02 |
| theDotNetWay | 1 | 1.40 2.20 1.54 | 3.32 3.51 2.47 |
| theDotNetWay | 2 | 1.40 2.93 2.33 | 3.58 3.45 2.78 |
| theDotNetWay | 3 | 1.39 2.80 2.82 | 3.83 3.41 3.05 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.05 | 1.06 | 0/12 | 1.08 |
| F# | 2 | 1.09 | 1.05 | 0/12 | 1.07 |
| F# | 3 | 1.06 | 1.06 | 0/12 | 1.08 |
| theDotNetWay | 1 | 1.07 | 1.09 | 0/12 | 1.08 |
| theDotNetWay | 2 | 1.08 | 1.07 | 1/12 **noisy** | 1.10 |
| theDotNetWay | 3 | 1.07 | 1.08 | 2/12 **noisy** | 1.24 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 225,715 | 0 | 0 | 0 | 0 |
| F# | messages_page | 215,840 | 0 | 0 | 0 | 0 |
| F# | sidebar | 227,134 | 0 | 0 | 0 | 0 |
| F# | search | 207,688 | 0 | 0 | 0 | 0 |
| F# | avatar | 2,576,146 | 0 | 0 | 0 | 0 |
| F# | post_message | 73,633 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 232,012 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 225,246 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 233,467 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 204,824 | 0 | 0 | 0 | 0 |
| theDotNetWay | avatar | 2,607,713 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 73,926 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| F# | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | search | 1 | 30.0 [30.0–32.0] | 3/3 | 1.01 |
| F# | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | avatar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | avatar | 16 | 30.0 [30.0–32.0] | 3/3 | 0.99 |
| F# | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 30.0 [30.0–42.0] | 3/3 | 1.00 |
| theDotNetWay | avatar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | avatar | 16 | 34.0 [30.0–90.0] | 2/3 **←** | 1.01 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–46.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–64.4] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 28,373 | 28,373 | yes |
| F# | 1 | run c=1 | 7,781 | 7,781 | yes |
| F# | 1 | warm-up c=16 | 60,002 | 60,002 | yes |
| F# | 1 | run c=16 | 16,177 | 16,177 | yes |
| F# | 1 | **all** | 112,333 | 112,333 | yes |
| F# | 2 | warm-up c=1 | 30,065 | 30,065 | yes |
| F# | 2 | run c=1 | 8,397 | 8,397 | yes |
| F# | 2 | warm-up c=16 | 64,508 | 64,508 | yes |
| F# | 2 | run c=16 | 17,069 | 17,069 | yes |
| F# | 2 | **all** | 120,039 | 120,039 | yes |
| F# | 3 | warm-up c=1 | 29,069 | 29,069 | yes |
| F# | 3 | run c=1 | 8,048 | 8,048 | yes |
| F# | 3 | warm-up c=16 | 60,039 | 60,039 | yes |
| F# | 3 | run c=16 | 16,161 | 16,161 | yes |
| F# | 3 | **all** | 113,317 | 113,317 | yes |
| theDotNetWay | 1 | warm-up c=1 | 30,590 | 30,590 | yes |
| theDotNetWay | 1 | run c=1 | 8,431 | 8,431 | yes |
| theDotNetWay | 1 | warm-up c=16 | 64,267 | 64,267 | yes |
| theDotNetWay | 1 | run c=16 | 16,996 | 16,996 | yes |
| theDotNetWay | 1 | **all** | 120,284 | 120,284 | yes |
| theDotNetWay | 2 | warm-up c=1 | 29,222 | 29,222 | yes |
| theDotNetWay | 2 | run c=1 | 8,199 | 8,199 | yes |
| theDotNetWay | 2 | warm-up c=16 | 63,190 | 63,190 | yes |
| theDotNetWay | 2 | run c=16 | 16,956 | 16,956 | yes |
| theDotNetWay | 2 | **all** | 117,567 | 117,567 | yes |
| theDotNetWay | 3 | warm-up c=1 | 39,605 | 39,605 | yes |
| theDotNetWay | 3 | run c=1 | 7,535 | 7,535 | yes |
| theDotNetWay | 3 | warm-up c=16 | 115,040 | 115,040 | yes |
| theDotNetWay | 3 | run c=16 | 15,809 | 15,809 | yes |
| theDotNetWay | 3 | **all** | 177,989 | 177,989 | yes |

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
