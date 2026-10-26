```
date: 2026-10-26T14:02:47-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p6ctl sha256:4e33c8f9212bfe29a094ea573b018a507f666a3172bb3f0c1b9d087a903e1189 2026-10-26T08:50:46.642832624-04:00 built from commit: 309ab42
dotnetway image: campfire-dotnetway:app sha256:89ef9d56dae9856bb2345ef6f2dd1338c88b3bcbff07dc27c695eb86db59200a 2026-10-26T12:59:37.344065286-04:00 built from commit: 414713f059dec669e9b469ca72692540b442f056
repo HEAD: 868730e (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message avatar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 660 [530–736] | 674 [556–873] |
| idle memory.current (MB) | 71.0 [71.0–161.0] | 83.0 [83.0–83.0] |
| idle anon (MB) | 45.0 [45.0–45.0] | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 317 [300–402] | 388 [377–402] |
| peak anon under load (MB) | 204 [203–204] | 287 [283–290] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,298 [2,276–2,349] | 1,838 [1,016–2,004] |
| room_show c=1 CPU µs/req | 531 [523–536] | 646 [609–1,089] |
| room_show c=1 p50 ms | 0.41 [0.40–0.41] | 0.49 [0.48–0.74] |
| room_show c=1 p99 ms | 0.76 [0.72–0.77] | 2.37 [0.76–2.99] |
| room_show c=16 req/s | 6,503 [5,491–7,424] | 5,988 [3,656–7,080] |
| room_show c=16 CPU µs/req | 499 [446–599] | 534 [474–907] |
| room_show c=16 p50 ms | 1.91 [1.67–2.33] | 1.90 [1.78–3.55] |
| room_show c=16 p99 ms | 7.54 [6.44–9.04] | 8.74 [6.46–13.51] |
| messages_page c=1 req/s | 2,115 [2,108–2,186] | 1,964 [1,074–1,991] |
| messages_page c=1 CPU µs/req | 567 [552–569] | 619 [612–1,035] |
| messages_page c=1 p50 ms | 0.43 [0.43–0.44] | 0.49 [0.48–0.69] |
| messages_page c=1 p99 ms | 0.98 [0.65–1.12] | 0.73 [0.67–2.67] |
| messages_page c=16 req/s | 7,062 [6,195–7,231] | 6,140 [5,237–6,736] |
| messages_page c=16 CPU µs/req | 474 [466–542] | 548 [499–612] |
| messages_page c=16 p50 ms | 1.78 [1.73–1.99] | 1.99 [1.87–2.08] |
| messages_page c=16 p99 ms | 7.11 [7.07–8.93] | 7.99 [7.34–14.49] |
| sidebar c=1 req/s | 2,308 [2,126–2,376] | 2,051 [1,764–2,086] |
| sidebar c=1 CPU µs/req | 528 [515–560] | 600 [590–659] |
| sidebar c=1 p50 ms | 0.40 [0.40–0.44] | 0.46 [0.46–0.54] |
| sidebar c=1 p99 ms | 0.67 [0.63–0.74] | 0.77 [0.77–0.83] |
| sidebar c=16 req/s | 7,292 [4,070–7,396] | 6,832 [6,371–7,244] |
| sidebar c=16 CPU µs/req | 460 [451–834] | 487 [468–528] |
| sidebar c=16 p50 ms | 1.72 [1.68–3.22] | 1.85 [1.74–2.01] |
| sidebar c=16 p99 ms | 6.98 [6.82–12.21] | 7.24 [6.69–7.55] |
| search c=1 req/s | 1,985 [1,096–2,058] | 1,715 [1,581–1,842] |
| search c=1 CPU µs/req | 599 [580–965] | 694 [656–723] |
| search c=1 p50 ms | 0.47 [0.46–0.70] | 0.55 [0.52–0.61] |
| search c=1 p99 ms | 0.74 [0.69–2.98] | 0.83 [0.74–1.42] |
| search c=16 req/s | 6,829 [3,274–7,135] | 6,463 [5,932–6,694] |
| search c=16 CPU µs/req | 503 [484–1,028] | 539 [513–568] |
| search c=16 p50 ms | 1.90 [1.81–3.97] | 1.98 [1.95–2.21] |
| search c=16 p99 ms | 6.55 [6.23–16.30] | 7.07 [6.44–7.56] |
| avatar c=1 req/s | 22,340 [13,966–23,194] | 12,152 [10,249–12,396] |
| avatar c=1 CPU µs/req | 95.9 [95.2–121.2] | 165 [162–178] |
| avatar c=1 p50 ms | 0.04 [0.04–0.07] | 0.08 [0.08–0.09] |
| avatar c=1 p99 ms | 0.11 [0.09–0.14] | 0.15 [0.14–0.17] |
| avatar c=16 req/s | 131,987 [121,783–132,466] | 98,971 [91,157–99,934] |
| avatar c=16 CPU µs/req | 19.8 [19.6–20.2] | 25.4 [25.1–27.5] |
| avatar c=16 p50 ms | 0.10 [0.09–0.12] | 0.14 [0.14–0.15] |
| avatar c=16 p99 ms | 0.32 [0.30–0.47] | 0.42 [0.41–0.54] |
| post_message c=1 req/s | 1,023 [1,014–1,039] | 966 [905–1,003] |
| post_message c=1 CPU µs/req | 1,742 [1,726–1,750] | 1,856 [1,801–1,946] |
| post_message c=1 p50 ms | 0.82 [0.81–0.82] | 0.89 [0.86–0.94] |
| post_message c=1 p99 ms | 4.79 [4.71–4.94] | 5.02 [4.93–5.58] |
| post_message c=16 req/s | 1,394 [1,164–1,452] | 1,855 [1,098–2,028] |
| post_message c=16 CPU µs/req | 2,182 [2,122–2,503] | 1,607 [1,593–2,909] |
| post_message c=16 p50 ms | 10.4 [9.7–11.9] | 7.70 [6.47–13.81] |
| post_message c=16 p99 ms | 36.5 [18.4–38.5] | 16.3 [16.1–33.3] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.32 2.75 2.84 | 3.13 3.02 2.88 |
| F# | 2 | 1.45 2.78 2.88 | 3.27 3.19 2.93 |
| F# | 3 | 1.11 2.55 2.72 | 2.84 2.81 2.68 |
| theDotNetWay | 1 | 1.28 2.52 2.72 | 3.50 3.40 3.01 |
| theDotNetWay | 2 | 1.26 2.75 2.81 | 4.07 3.44 3.08 |
| theDotNetWay | 3 | 1.42 2.43 2.56 | 3.14 3.37 2.97 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.07 | 1.08 | 0/12 | 1.10 |
| F# | 2 | 1.05 | 1.06 | 1/12 **noisy** | 1.11 |
| F# | 3 | 1.09 | 1.15 | 4/12 **noisy** | 1.27 |
| theDotNetWay | 1 | 1.07 | 1.08 | 2/12 **noisy** | 1.11 |
| theDotNetWay | 2 | 1.08 | 1.06 | 1/12 **noisy** | 1.10 |
| theDotNetWay | 3 | 1.09 | 1.18 | 4/12 **noisy** | 1.27 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 210,772 | 0 | 0 | 0 | 0 |
| F# | messages_page | 215,232 | 0 | 0 | 0 | 0 |
| F# | sidebar | 204,594 | 0 | 0 | 0 | 0 |
| F# | search | 179,050 | 0 | 0 | 0 | 0 |
| F# | avatar | 3,566,075 | 0 | 0 | 0 | 0 |
| F# | post_message | 56,743 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 172,684 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 185,184 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 210,811 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 193,879 | 0 | 0 | 0 | 0 |
| theDotNetWay | avatar | 2,599,027 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 62,877 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–36.0] | 3/3 | 1.00 |
| F# | room_show | 16 | 44.0 [38.0–58.0] | 3/3 | 1.01 |
| F# | messages_page | 1 | 50.0 [32.0–90.3] | 2/3 **←** | 0.99 |
| F# | messages_page | 16 | 88.0 [76.4–90.1] | 2/3 **←** | 1.01 |
| F# | sidebar | 1 | 42.0 [30.0–90.0] | 2/3 **←** | 1.00 |
| F# | sidebar | 16 | 30.0 [30.0–54.1] | 3/3 | 0.99 |
| F# | search | 1 | 32.0 [30.0–84.0] | 3/3 | 1.00 |
| F# | search | 16 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| F# | avatar | 1 | 90.0 [34.0–90.2] | 1/3 **←** | 1.02 |
| F# | avatar | 16 | 60.0 [30.0–90.0] | 2/3 **←** | 0.99 |
| F# | post_message | 1 | 34.0 [30.0–46.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–90.6] | 2/3 **←** | 0.75 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| theDotNetWay | room_show | 16 | 44.0 [30.0–90.1] | 2/3 **←** | 1.00 |
| theDotNetWay | messages_page | 1 | 58.0 [42.1–90.0] | 2/3 **←** | 1.02 |
| theDotNetWay | messages_page | 16 | 90.0 [40.0–90.2] | 1/3 **←** | 1.03 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–56.1] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 42.0 [30.0–52.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–42.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 38.0 [30.0–90.0] | 2/3 **←** | 0.99 |
| theDotNetWay | avatar | 1 | 42.0 [30.0–48.0] | 3/3 | 0.99 |
| theDotNetWay | avatar | 16 | 42.0 [30.0–62.0] | 3/3 | 1.01 |
| theDotNetWay | post_message | 1 | 38.0 [30.0–64.0] | 3/3 | 0.98 |
| theDotNetWay | post_message | 16 | 70.7 [30.1–91.0] | 2/3 **←** | 1.01 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 41,825 | 41,825 | yes |
| F# | 1 | run c=1 | 8,111 | 8,111 | yes |
| F# | 1 | warm-up c=16 | 55,195 | 55,195 | yes |
| F# | 1 | run c=16 | 11,173 | 11,173 | yes |
| F# | 1 | **all** | 116,304 | 116,304 | yes |
| F# | 2 | warm-up c=1 | 28,955 | 28,955 | yes |
| F# | 2 | run c=1 | 8,316 | 8,316 | yes |
| F# | 2 | warm-up c=16 | 57,761 | 57,761 | yes |
| F# | 2 | run c=16 | 11,631 | 11,631 | yes |
| F# | 2 | **all** | 106,663 | 106,663 | yes |
| F# | 3 | warm-up c=1 | 27,303 | 27,303 | yes |
| F# | 3 | run c=1 | 8,184 | 8,184 | yes |
| F# | 3 | warm-up c=16 | 122,720 | 122,720 | yes |
| F# | 3 | run c=16 | 9,328 | 9,328 | yes |
| F# | 3 | **all** | 167,535 | 167,535 | yes |
| theDotNetWay | 1 | warm-up c=1 | 29,305 | 29,305 | yes |
| theDotNetWay | 1 | run c=1 | 8,022 | 8,022 | yes |
| theDotNetWay | 1 | warm-up c=16 | 61,496 | 61,496 | yes |
| theDotNetWay | 1 | run c=16 | 16,233 | 16,233 | yes |
| theDotNetWay | 1 | **all** | 115,056 | 115,056 | yes |
| theDotNetWay | 2 | warm-up c=1 | 55,748 | 55,748 | yes |
| theDotNetWay | 2 | run c=1 | 7,241 | 7,241 | yes |
| theDotNetWay | 2 | warm-up c=16 | 93,300 | 93,300 | yes |
| theDotNetWay | 2 | run c=16 | 14,852 | 14,852 | yes |
| theDotNetWay | 2 | **all** | 171,141 | 171,141 | yes |
| theDotNetWay | 3 | warm-up c=1 | 37,640 | 37,640 | yes |
| theDotNetWay | 3 | run c=1 | 7,729 | 7,729 | yes |
| theDotNetWay | 3 | warm-up c=16 | 86,469 | 86,469 | yes |
| theDotNetWay | 3 | run c=16 | 8,800 | 8,800 | yes |
| theDotNetWay | 3 | **all** | 140,638 | 140,638 | yes |

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
