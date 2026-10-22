```
date: 2026-10-22T14:26:46-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p5ctl sha256:3b424112aecf6835e2936f7c2f97c8e7fff7b0833959397fc8dd4b85d8a8c202 2026-10-22T13:29:30.533632367-04:00 built from commit: 853604f1
dotnetway image: campfire-dotnetway:app sha256:25f73eb5285c3622863a2533580801b6cdd5e1f524e8ba4ed032c8dfe9c73f68 2026-10-22T14:25:56.767303321-04:00 built from commit: 47eb5b8d181942124e1e14563d5596cbb72d096c
repo HEAD: 47eb5b8 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message sidebar, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 382 [346–398] | 505 [440–508] |
| idle memory.current (MB) | 49.0 [48.0–49.0] | 70.0 [70.0–70.0] |
| idle anon (MB) | 25.0 [25.0–25.0] | 45.0 [45.0–45.0] |
| peak memory.current under load (MB) | 270 [269–276] | 293 [290–299] |
| peak anon under load (MB) | 175 [174–177] | 196 [194–196] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| sidebar c=1 req/s | 2,530 [2,515–2,589] | 2,562 [2,466–2,585] |
| sidebar c=1 CPU µs/req | 489 [480–490] | 479 [476–499] |
| sidebar c=1 p50 ms | 0.37 [0.36–0.38] | 0.37 [0.36–0.38] |
| sidebar c=1 p99 ms | 0.61 [0.56–0.66] | 0.58 [0.57–0.62] |
| sidebar c=16 req/s | 7,728 [7,449–7,770] | 7,734 [7,468–7,759] |
| sidebar c=16 CPU µs/req | 426 [421–443] | 422 [422–440] |
| sidebar c=16 p50 ms | 1.59 [1.57–1.67] | 1.57 [1.57–1.65] |
| sidebar c=16 p99 ms | 6.81 [6.74–6.95] | 6.96 [6.80–6.96] |
| post_message c=1 req/s | 1,111 [1,089–1,112] | 1,123 [1,107–1,124] |
| post_message c=1 CPU µs/req | 1,671 [1,664–1,682] | 1,639 [1,637–1,688] |
| post_message c=1 p50 ms | 0.78 [0.78–0.78] | 0.77 [0.77–0.78] |
| post_message c=1 p99 ms | 3.94 [3.88–4.63] | 3.90 [3.87–3.90] |
| post_message c=16 req/s | 2,028 [1,970–2,050] | 2,011 [1,954–2,047] |
| post_message c=16 CPU µs/req | 1,482 [1,463–1,539] | 1,480 [1,470–1,553] |
| post_message c=16 p50 ms | 7.29 [7.09–7.46] | 7.24 [7.17–7.55] |
| post_message c=16 p99 ms | 14.5 [14.3–15.0] | 14.4 [14.0–16.7] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.07 0.50 0.27 | 2.54 1.43 0.67 |
| F# | 2 | 1.26 1.76 1.15 | 2.13 1.95 1.33 |
| F# | 3 | 1.27 1.75 1.28 | 2.84 2.11 1.49 |
| theDotNetWay | 1 | 1.28 1.25 0.64 | 2.25 1.71 0.93 |
| theDotNetWay | 2 | 1.33 1.54 0.90 | 2.95 2.10 1.22 |
| theDotNetWay | 3 | 1.38 1.83 1.42 | 2.77 2.15 1.60 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.05 | 1.06 | 0/4 | 1.08 |
| F# | 2 | 1.07 | 1.06 | 0/4 | 1.09 |
| F# | 3 | 1.06 | 1.08 | 0/4 | 1.07 |
| theDotNetWay | 1 | 1.07 | 1.07 | 0/4 | 1.08 |
| theDotNetWay | 2 | 1.07 | 1.07 | 0/4 | 1.08 |
| theDotNetWay | 3 | 1.03 | 1.07 | 0/4 | 1.07 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | sidebar | 244,666 | 0 | 0 | 0 | 0 |
| F# | post_message | 74,926 | 0 | 0 | 0 | 0 |
| theDotNetWay | sidebar | 244,642 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 74,989 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | sidebar | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–42.2] | 3/3 | 1.01 |
| theDotNetWay | sidebar | 1 | 30.0 [30.0–32.0] | 3/3 | 1.00 |
| theDotNetWay | sidebar | 16 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–36.2] | 3/3 | 1.00 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 31,824 | 31,824 | yes |
| F# | 1 | run c=1 | 8,714 | 8,714 | yes |
| F# | 1 | warm-up c=16 | 60,150 | 60,150 | yes |
| F# | 1 | run c=16 | 16,239 | 16,239 | yes |
| F# | 1 | **all** | 116,927 | 116,927 | yes |
| F# | 2 | warm-up c=1 | 32,608 | 32,608 | yes |
| F# | 2 | run c=1 | 8,901 | 8,901 | yes |
| F# | 2 | warm-up c=16 | 61,685 | 61,685 | yes |
| F# | 2 | run c=16 | 16,408 | 16,408 | yes |
| F# | 2 | **all** | 119,602 | 119,602 | yes |
| F# | 3 | warm-up c=1 | 32,035 | 32,035 | yes |
| F# | 3 | run c=1 | 8,890 | 8,890 | yes |
| F# | 3 | warm-up c=16 | 81,215 | 81,215 | yes |
| F# | 3 | run c=16 | 15,774 | 15,774 | yes |
| F# | 3 | **all** | 137,914 | 137,914 | yes |
| theDotNetWay | 1 | warm-up c=1 | 32,489 | 32,489 | yes |
| theDotNetWay | 1 | run c=1 | 8,990 | 8,990 | yes |
| theDotNetWay | 1 | warm-up c=16 | 61,424 | 61,424 | yes |
| theDotNetWay | 1 | run c=16 | 16,388 | 16,388 | yes |
| theDotNetWay | 1 | **all** | 119,291 | 119,291 | yes |
| theDotNetWay | 2 | warm-up c=1 | 32,451 | 32,451 | yes |
| theDotNetWay | 2 | run c=1 | 8,986 | 8,986 | yes |
| theDotNetWay | 2 | warm-up c=16 | 72,630 | 72,630 | yes |
| theDotNetWay | 2 | run c=16 | 16,119 | 16,119 | yes |
| theDotNetWay | 2 | **all** | 130,186 | 130,186 | yes |
| theDotNetWay | 3 | warm-up c=1 | 31,785 | 31,785 | yes |
| theDotNetWay | 3 | run c=1 | 8,859 | 8,859 | yes |
| theDotNetWay | 3 | warm-up c=16 | 59,009 | 59,009 | yes |
| theDotNetWay | 3 | run c=16 | 15,647 | 15,647 | yes |
| theDotNetWay | 3 | **all** | 115,300 | 115,300 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| sidebar | 200 7,695 / 29,710 | 200 7,695 / 29,710 | 200 29,710 | 200 29,710 | 7,695 | 7,695 | – |
| post_message | – | – | – | – | 2,487 | 2,487 | – |
