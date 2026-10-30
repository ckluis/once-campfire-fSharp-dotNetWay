```
date: 2026-10-22T16:51:43-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p5ctl sha256:3b424112aecf6835e2936f7c2f97c8e7fff7b0833959397fc8dd4b85d8a8c202 2026-10-22T13:29:30.533632367-04:00 built from commit: 853604f1
dotnetway image: campfire-dotnetway:app sha256:7847a6b598cf9c6fabbd42b0b3fe08f52df0058f8b29a96a0d5b7d2506266b15 2026-10-22T16:21:23.911450782-04:00 built from commit: 184cae374698b06ba000cde6a32375dffc87bd94
repo HEAD: 13f137f (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes room_show messages_page sidebar search post_message static_css, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 515 [405–569] | 516 [506–557] |
| idle memory.current (MB) | 49.0 [48.0–135.0] | 70.0 [70.0–70.0] |
| idle anon (MB) | 25.0 [25.0–25.0] | 45.0 [45.0–45.0] |
| peak memory.current under load (MB) | 283 [273–368] | 296 [295–296] |
| peak anon under load (MB) | 176 [176–176] | 199 [199–201] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| room_show c=1 req/s | 2,377 [2,339–2,468] | 2,403 [2,401–2,485] |
| room_show c=1 CPU µs/req | 516 [504–527] | 514 [497–520] |
| room_show c=1 p50 ms | 0.40 [0.38–0.40] | 0.39 [0.38–0.40] |
| room_show c=1 p99 ms | 0.65 [0.63–0.66] | 0.62 [0.62–0.65] |
| room_show c=16 req/s | 7,105 [7,081–7,376] | 6,979 [6,966–7,208] |
| room_show c=16 CPU µs/req | 454 [436–458] | 472 [462–476] |
| room_show c=16 p50 ms | 1.71 [1.63–1.72] | 1.76 [1.76–1.80] |
| room_show c=16 p99 ms | 6.92 [6.82–7.09] | 6.85 [6.47–7.01] |
| messages_page c=1 req/s | 2,201 [2,190–2,290] | 2,213 [2,195–2,315] |
| messages_page c=1 CPU µs/req | 548 [533–553] | 547 [524–551] |
| messages_page c=1 p50 ms | 0.43 [0.41–0.43] | 0.43 [0.41–0.43] |
| messages_page c=1 p99 ms | 0.59 [0.57–0.68] | 0.59 [0.57–0.60] |
| messages_page c=16 req/s | 7,153 [6,962–7,286] | 6,925 [6,776–6,930] |
| messages_page c=16 CPU µs/req | 467 [457–483] | 490 [490–494] |
| messages_page c=16 p50 ms | 1.73 [1.69–1.80] | 1.84 [1.83–1.85] |
| messages_page c=16 p99 ms | 7.20 [7.15–7.21] | 7.12 [6.86–7.46] |
| sidebar c=1 req/s | 2,312 [2,306–2,398] | 2,338 [2,334–2,451] |
| sidebar c=1 CPU µs/req | 525 [512–529] | 522 [499–523] |
| sidebar c=1 p50 ms | 0.40 [0.39–0.41] | 0.40 [0.39–0.40] |
| sidebar c=1 p99 ms | 0.64 [0.59–1.41] | 0.64 [0.59–0.64] |
| sidebar c=16 req/s | 7,437 [7,347–7,560] | 7,476 [7,203–7,984] |
| sidebar c=16 CPU µs/req | 443 [434–449] | 451 [420–460] |
| sidebar c=16 p50 ms | 1.65 [1.62–1.68] | 1.71 [1.58–1.72] |
| sidebar c=16 p99 ms | 7.05 [6.92–7.07] | 6.53 [6.23–7.18] |
| search c=1 req/s | 2,051 [2,019–2,078] | 2,026 [2,016–2,131] |
| search c=1 CPU µs/req | 582 [577–589] | 588 [561–591] |
| search c=1 p50 ms | 0.46 [0.45–0.47] | 0.47 [0.45–0.47] |
| search c=1 p99 ms | 0.69 [0.67–0.70] | 0.67 [0.67–0.68] |
| search c=16 req/s | 6,855 [6,612–6,998] | 6,687 [6,477–7,336] |
| search c=16 CPU µs/req | 494 [484–515] | 521 [470–528] |
| search c=16 p50 ms | 1.85 [1.81–1.94] | 1.98 [1.77–1.98] |
| search c=16 p99 ms | 6.75 [6.60–6.89] | 6.38 [6.07–7.05] |
| static_css c=1 req/s | 17,192 [17,057–18,635] | 16,616 [16,515–17,323] |
| static_css c=1 CPU µs/req | 128 [117–129] | 144 [138–144] |
| static_css c=1 p50 ms | 0.06 [0.05–0.06] | 0.06 [0.05–0.06] |
| static_css c=1 p99 ms | 0.10 [0.10–0.10] | 0.11 [0.11–0.11] |
| static_css c=16 req/s | 99,303 [98,797–102,028] | 98,434 [96,292–99,124] |
| static_css c=16 CPU µs/req | 28.4 [27.9–28.6] | 29.4 [29.3–30.3] |
| static_css c=16 p50 ms | 0.14 [0.14–0.15] | 0.15 [0.14–0.15] |
| static_css c=16 p99 ms | 0.36 [0.36–0.36] | 0.40 [0.38–0.40] |
| post_message c=1 req/s | 1,077 [1,070–1,078] | 1,067 [1,061–1,104] |
| post_message c=1 CPU µs/req | 1,701 [1,682–1,734] | 1,741 [1,662–1,742] |
| post_message c=1 p50 ms | 0.80 [0.79–0.80] | 0.81 [0.78–0.81] |
| post_message c=1 p99 ms | 4.04 [4.04–4.71] | 3.98 [3.90–4.00] |
| post_message c=16 req/s | 1,964 [1,789–1,996] | 1,927 [1,900–2,021] |
| post_message c=16 CPU µs/req | 1,535 [1,498–1,716] | 1,592 [1,484–1,611] |
| post_message c=16 p50 ms | 7.88 [7.19–7.96] | 7.60 [7.03–7.77] |
| post_message c=16 p99 ms | 16.1 [15.1–16.3] | 15.0 [14.8–15.2] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.32 2.10 1.85 | 3.20 2.81 2.27 |
| F# | 2 | 1.49 2.34 2.32 | 2.98 2.65 2.43 |
| F# | 3 | 1.27 2.22 2.30 | 2.66 2.54 2.39 |
| theDotNetWay | 1 | 1.37 2.37 2.15 | 2.73 2.68 2.37 |
| theDotNetWay | 2 | 1.33 2.32 2.26 | 2.59 2.62 2.41 |
| theDotNetWay | 3 | 1.33 2.20 2.28 | 2.62 2.71 2.49 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.05 | 1.06 | 0/12 | 1.08 |
| F# | 2 | 1.06 | 1.04 | 0/12 | 1.07 |
| F# | 3 | 1.08 | 1.06 | 0/12 | 1.09 |
| theDotNetWay | 1 | 1.06 | 1.07 | 0/12 | 1.07 |
| theDotNetWay | 2 | 1.07 | 1.06 | 0/12 | 1.07 |
| theDotNetWay | 3 | 1.06 | 1.04 | 0/12 | 1.08 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | room_show | 229,997 | 0 | 0 | 0 | 0 |
| F# | messages_page | 224,686 | 0 | 0 | 0 | 0 |
| F# | sidebar | 234,914 | 0 | 0 | 0 | 0 |
| F# | search | 212,948 | 0 | 0 | 0 | 0 |
| F# | static_css | 2,824,347 | 0 | 0 | 0 | 0 |
| F# | post_message | 71,842 | 0 | 0 | 0 | 0 |
| theDotNetWay | room_show | 227,580 | 0 | 0 | 0 | 0 |
| theDotNetWay | messages_page | 218,849 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 238,324 | 0 | 0 | 0 | 0 |
| theDotNetWay | search | 213,430 | 0 | 0 | 0 | 0 |
| theDotNetWay | static_css | 2,754,535 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 72,686 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | room_show | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | room_show | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| F# | messages_page | 1 | 30.0 [30.0–40.0] | 3/3 | 1.00 |
| F# | messages_page | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| F# | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 0.99 |
| F# | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| F# | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| F# | static_css | 1 | 90.0 [30.0–90.0] | 1/3 **←** | 1.00 |
| F# | static_css | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 1 | 30.0 [30.0–68.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–32.1] | 3/3 | 0.99 |
| theDotNetWay | room_show | 1 | 30.0 [30.0–32.0] | 3/3 | 1.01 |
| theDotNetWay | room_show | 16 | 30.0 [30.0–50.0] | 3/3 | 0.99 |
| theDotNetWay | messages_page | 1 | 30.0 [30.0–30.0] | 3/3 | 1.01 |
| theDotNetWay | messages_page | 16 | 30.0 [30.0–54.0] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | search | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | static_css | 1 | 90.0 [30.0–90.0] | 1/3 **←** | 1.00 |
| theDotNetWay | static_css | 16 | 30.0 [30.0–36.0] | 3/3 | 0.99 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 30,880 | 30,880 | yes |
| F# | 1 | run c=1 | 8,564 | 8,564 | yes |
| F# | 1 | warm-up c=16 | 59,466 | 59,466 | yes |
| F# | 1 | run c=16 | 15,990 | 15,990 | yes |
| F# | 1 | **all** | 114,900 | 114,900 | yes |
| F# | 2 | warm-up c=1 | 31,252 | 31,252 | yes |
| F# | 2 | run c=1 | 8,621 | 8,621 | yes |
| F# | 2 | warm-up c=16 | 63,020 | 63,020 | yes |
| F# | 2 | run c=16 | 15,728 | 15,728 | yes |
| F# | 2 | **all** | 118,621 | 118,621 | yes |
| F# | 3 | warm-up c=1 | 69,790 | 69,790 | yes |
| F# | 3 | run c=1 | 8,618 | 8,618 | yes |
| F# | 3 | warm-up c=16 | 54,391 | 54,391 | yes |
| F# | 3 | run c=16 | 14,321 | 14,321 | yes |
| F# | 3 | **all** | 147,120 | 147,120 | yes |
| theDotNetWay | 1 | warm-up c=1 | 30,984 | 30,984 | yes |
| theDotNetWay | 1 | run c=1 | 8,486 | 8,486 | yes |
| theDotNetWay | 1 | warm-up c=16 | 57,446 | 57,446 | yes |
| theDotNetWay | 1 | run c=16 | 15,433 | 15,433 | yes |
| theDotNetWay | 1 | **all** | 112,349 | 112,349 | yes |
| theDotNetWay | 2 | warm-up c=1 | 30,855 | 30,855 | yes |
| theDotNetWay | 2 | run c=1 | 8,538 | 8,538 | yes |
| theDotNetWay | 2 | warm-up c=16 | 57,019 | 57,019 | yes |
| theDotNetWay | 2 | run c=16 | 15,211 | 15,211 | yes |
| theDotNetWay | 2 | **all** | 111,623 | 111,623 | yes |
| theDotNetWay | 3 | warm-up c=1 | 32,141 | 32,141 | yes |
| theDotNetWay | 3 | run c=1 | 8,832 | 8,832 | yes |
| theDotNetWay | 3 | warm-up c=16 | 60,865 | 60,865 | yes |
| theDotNetWay | 3 | run c=16 | 16,186 | 16,186 | yes |
| theDotNetWay | 3 | **all** | 118,024 | 118,024 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| room_show | 200 36,115 / 376,570 | 200 37,356 / 377,518 | 200 376,570 | 200 377,518 | 36,115 | 37,356 | – |
| messages_page | 200 23,443 / 345,884 | 200 24,267 / 346,364 | 200 345,884 | 200 346,364 | 23,443 | 24,267 | – |
| sidebar | 200 7,695 / 29,710 | 200 7,979 / 30,144 | 200 29,710 | 200 30,144 | 7,695 | 7,979 | – |
| search | 200 14,836 / 136,633 | 200 15,053 / 137,229 | 200 136,633 | 200 137,229 | 14,836 | 15,053 | – |
| static_css | 200 650 / 1,218 | 200 648 / 1,218 | 200 1,218 | 200 1,218 | 650 | 648 | – |
| post_message | – | – | – | – | 2,487 | 2,494 | – |
