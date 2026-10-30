```
date: 2026-10-27T05:18:58-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
fsharp image: campfire-dotnetway:p8ctl sha256:b4ccd7e5b4d6616956a2ffe4659158b14558ffe63304073e402791035ec7805b 2026-10-27T04:58:07.292450478-04:00 built from commit: 6da5ef7ca37f4a07b9f25af708e2da299bfca1db
dotnetway image: campfire-dotnetway:app sha256:0f8e5b239f7a156719df668b1c753c3daa4c37fbf7106e50f0f6adcdadb8b53a 2026-10-27T05:16:12.758761404-04:00 built from commit: b6fac7c7b03a59c20c50016ad4945b073262e7d0
repo HEAD: b6fac7c (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps fsharp,dotnetway, reps 3; suites: http; http secs 8, concs 1 16, routes post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: fsharp 3, dotnetway 3. Cells: median [min–max].

### Startup and memory

| Metric | F# | theDotNetWay |
|---|---|---|
| cold start: docker run → /up 200 (ms) | 461 [452–484] | 477 [461–486] |
| idle memory.current (MB) | 83.0 [83.0–84.0] | 83.0 [83.0–84.0] |
| idle anon (MB) | 57.0 [57.0–57.0] | 57.0 [57.0–57.0] |
| peak memory.current under load (MB) | 380 [378–382] | 378 [375–379] |
| peak anon under load (MB) | 285 [284–285] | 285 [285–287] |

### HTTP (signed in as david; keep-alive; c = concurrent connections)

`CPU µs/req` is the container's CPU (cgroup cpu.stat, user + system, unprofiled) over the run divided by its requests; for CPU a ratio below 1 means the first app spends more per request.

| Metric | F# | theDotNetWay |
|---|---|---|
| post_message c=1 req/s | 1,124 [1,054–1,128] | 1,118 [1,055–1,118] |
| post_message c=1 CPU µs/req | 1,670 [1,660–1,722] | 1,749 [1,740–1,829] |
| post_message c=1 p50 ms | 0.79 [0.79–0.82] | 0.79 [0.79–0.84] |
| post_message c=1 p99 ms | 4.09 [3.98–4.93] | 3.99 [3.99–4.09] |
| post_message c=16 req/s | 2,164 [2,144–2,167] | 2,150 [2,148–2,161] |
| post_message c=16 CPU µs/req | 1,487 [1,485–1,506] | 1,504 [1,484–1,521] |
| post_message c=16 p50 ms | 5.92 [5.90–5.97] | 6.00 [5.89–6.21] |
| post_message c=16 p99 ms | 14.8 [14.5–15.1] | 15.0 [14.6–15.0] |

## Validity

### Load average before each run (1, 5, 15 minutes; the run waited for the 1-minute figure to fall below LOAD_MAX)

| App | Rep | Before | After |
|---|---|---|---|
| F# | 1 | 1.09 0.77 0.63 | 3.08 1.45 0.88 |
| F# | 2 | 1.46 1.56 1.09 | 2.13 1.68 1.17 |
| F# | 3 | 1.27 1.51 1.13 | 2.32 1.73 1.24 |
| theDotNetWay | 1 | 1.35 1.23 0.84 | 2.49 1.62 1.01 |
| theDotNetWay | 2 | 1.25 1.41 0.97 | 2.54 1.74 1.13 |
| theDotNetWay | 3 | 1.37 1.56 1.20 | 2.56 1.87 1.34 |

### Host CPU probe (a fixed spin timed on every pinned CPU, against the quietest this VM has shown; bench/lib/hostprobe.py)

The VM's load average cannot see another program on the Mac taking the cores its vCPUs run on; this can. A ratio of 1.00 is a quiet host, above 1.10 the pinned server and load generator were slowed by something outside the run. `After each window` probes the machine right after every measured run of the rep.

| App | Rep | Before the run | After the run | Windows probed above 1.10 | Highest after a window |
|---|---|---|---|---|---|
| F# | 1 | 1.04 | 1.07 | 0/2 | 1.05 |
| F# | 2 | 1.05 | 1.08 | 0/2 | 1.06 |
| F# | 3 | 1.04 | 1.05 | 0/2 | 1.07 |
| theDotNetWay | 1 | 1.04 | 1.05 | 0/2 | 1.05 |
| theDotNetWay | 2 | 1.04 | 1.06 | 0/2 | 1.05 |
| theDotNetWay | 3 | 1.08 | 1.05 | 0/2 | 1.05 |

### Responses by status class, every HTTP run of every rep

| App | Route | 2xx | 3xx | 4xx | 5xx | Errors (no response) |
|---|---|---|---|---|---|---|
| F# | post_message | 78,301 | 0 | 0 | 0 | 0 |
| theDotNetWay | post_message | 78,047 | 0 | 0 | 0 | 0 |

### Warm-up before each measured run (the app runs the route at the measured concurrency until its throughput is flat; bench/lib/warmup.py)

`measured / warm-up tail` is the measured run's requests a second over the mean of the last three 2 s warm-up windows: well above 1 would mean the app was still speeding up when measurement began, well below 1 that it was slowing down.

| App | Route | c | warm-up seconds | settled | measured / warm-up tail |
|---|---|---|---|---|---|
| F# | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| F# | post_message | 16 | 30.1 [30.1–32.1] | 3/3 | 1.00 |
| theDotNetWay | post_message | 1 | 30.0 [30.0–30.0] | 3/3 | 1.00 |
| theDotNetWay | post_message | 16 | 30.1 [30.1–30.1] | 3/3 | 0.99 |

### Rows written by `post_message` (messages of the write room counted in the app's SQLite file around each run)

| App | Rep | Phase | Posts answered below 400 | Rows written | Match |
|---|---|---|---|---|---|
| F# | 1 | warm-up c=1 | 24,020 | 24,020 | yes |
| F# | 1 | run c=1 | 8,430 | 8,430 | yes |
| F# | 1 | warm-up c=16 | 63,733 | 63,733 | yes |
| F# | 1 | run c=16 | 17,174 | 17,174 | yes |
| F# | 1 | **all** | 113,357 | 113,357 | yes |
| F# | 2 | warm-up c=1 | 25,219 | 25,219 | yes |
| F# | 2 | run c=1 | 8,989 | 8,989 | yes |
| F# | 2 | warm-up c=16 | 65,548 | 65,548 | yes |
| F# | 2 | run c=16 | 17,322 | 17,322 | yes |
| F# | 2 | **all** | 117,078 | 117,078 | yes |
| F# | 3 | warm-up c=1 | 24,961 | 24,961 | yes |
| F# | 3 | run c=1 | 9,029 | 9,029 | yes |
| F# | 3 | warm-up c=16 | 69,984 | 69,984 | yes |
| F# | 3 | run c=16 | 17,357 | 17,357 | yes |
| F# | 3 | **all** | 121,331 | 121,331 | yes |
| theDotNetWay | 1 | warm-up c=1 | 25,073 | 25,073 | yes |
| theDotNetWay | 1 | run c=1 | 8,941 | 8,941 | yes |
| theDotNetWay | 1 | warm-up c=16 | 65,450 | 65,450 | yes |
| theDotNetWay | 1 | run c=16 | 17,220 | 17,220 | yes |
| theDotNetWay | 1 | **all** | 116,684 | 116,684 | yes |
| theDotNetWay | 2 | warm-up c=1 | 24,070 | 24,070 | yes |
| theDotNetWay | 2 | run c=1 | 8,440 | 8,440 | yes |
| theDotNetWay | 2 | warm-up c=16 | 65,310 | 65,310 | yes |
| theDotNetWay | 2 | run c=16 | 17,296 | 17,296 | yes |
| theDotNetWay | 2 | **all** | 115,116 | 115,116 | yes |
| theDotNetWay | 3 | warm-up c=1 | 24,528 | 24,528 | yes |
| theDotNetWay | 3 | run c=1 | 8,945 | 8,945 | yes |
| theDotNetWay | 3 | warm-up c=16 | 66,184 | 66,184 | yes |
| theDotNetWay | 3 | run c=16 | 17,205 | 17,205 | yes |
| theDotNetWay | 3 | **all** | 116,862 | 116,862 | yes |

### Response bytes, one request per route (gzip: wire / decoded; identity: bytes) and the average over the c=16 run

The probe is a single request after sign-in, before any load. Bodies carry per-request tokens and the clock, so sizes of two apps agree to within a few bytes when they render the same page. "c=16 avg" is what the load generator received per request on the wire (gzip unless the column says otherwise).

| Route | F# gzip wire / decoded | theDotNetWay gzip wire / decoded | F# identity | theDotNetWay identity | F# c=16 avg | theDotNetWay c=16 avg | F# vs Rust |
|---|---|---|---|---|---|---|---|
| post_message | – | – | – | – | 2,494 | 2,494 | – |
