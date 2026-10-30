```
date: 2026-10-20T12:51:14-04:00
host: 6.8.0-117-generic, , 8 threads, 5GB
server cpus: 0-3 (nproc 4); loadgen cpus: 4-7; network: host
env: WEB_CONCURRENCY=3 JOB_CONCURRENCY=3 RAILS_MAX_THREADS=5 
rust extra env: 
fsharp extra env: 
dotnetway extra env: 
user agent: (none)
dotnetway-identity image: campfire-dotnetway:app sha256:7777de1bf77845a7ac2be5b893abdffe7985497eb4200233e1706bb1e256db27 2026-10-20T11:59:06.296142201-04:00 built from commit: 55b4e675aef0acc2eeaf19957fe083b504b552d6
repo HEAD: 55b4e67 (dirty: 0 files under src/ and rust/)
work dir (seed copies): /var/tmp/campfire-bench-work (ext4)
loadgen: /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/target/rust/aarch64-unknown-linux-gnu/release/loadgen (2026-10-13 18:47:07), built with rust:1.98.1-trixie
schedule: apps dotnetway-identity, reps 1; suites: http; http secs 8, concs 1 16, routes room_show sidebar post_message, warm-up 30s at each route and concentration, idle 5s
log retention: 2 x 10m (json-file)
```

Reps: , dotnetway-identity 1. Cells: median [min–max].

### Startup and memory

| Metric |  |
|---|
| cold start: docker run → /up 200 (ms) |  |
| idle memory.current (MB) |  |
| idle anon (MB) |  |
| peak memory.current under load (MB) |  |
| peak anon under load (MB) |  |
