# Campfire.Cable's fan-out on ASP.NET Core's WebSocket middleware (theDotNetWay, unit 3.6)

`bench/cable/run fsharp --reps 2 --plain` (2026-10-21, Apple silicon Mac, the driver, one Python process, shares it with the server): 1,000
Action Cable clients on loopback, 600-byte Turbo Stream appends, as in `bench/results/cable-fanout.md`, which has the method, what each column
measures and the stored rows this one sits beside. **Plain mode only**: since unit 3.4 the F# server negotiates no `permessage-deflate` (as
Rails), so the driver's deflate mode would measure the same plain connection a second time; those rows stay in `cable-fanout.md` as history of the
old transport.

Each run is two reps against one server process (the second rep's `rss_kb_per_client` and `rss_mb_before` carry what the first left behind; the
first rep is the memory figure). `ctl` is the same bench built at `a17753e` (unit 3.3: the new channel API on the **old** hand-written transport),
run in the same session with the new build in between; `new` is this commit's `Connection.fs`.

| run (host load average at its start) | latency p50 (ms) | single (us/delivery) | burst (us/delivery) | rss per client, rep 1 (kB) |
|---|---|---|---|---|
| stored F#, plain (unit 3.3 of the port, `cable-fanout.md`) | 12.6, 13.7 | 47.8, 41.3 | 1.75, 1.60 (user 0.85, system 0.65 to 0.90) | 30.3 |
| new, run 1 (load 6.7 after `bin/verify`; `cable-run.log`) | 15.8, 16.5 | 55.5, 51.0 | 1.55, 1.85 | 44.3 |
| ctl (old transport, load 2.2; `cable-ctl.log`) | 16.1, 17.9 | 55.2, 53.2 | 1.60, 1.80 | 44.1 |
| new, run 2 (load 3.0; `cable-run2.log`) | 15.6, 17.2 | 45.7, 43.2 | 0.95, 0.75 (user 0.60, 0.45; system 0.35, 0.30) | 33.4 |

Reading it:

- **Not slower than the transport it replaced.** On this host in this session the old transport (ctl) costs 1.6 to 1.8 us per delivery in a burst
  and 53 to 55 us for a lone broadcast; the new one costs 1.55 to 1.85 on the loaded run and 0.75 to 0.95 on the quiet one, and 43 to 55 for a lone
  broadcast. The host moved between runs more than the two builds differ (load 6.7 against 3.0), so the safe statement is "equal within noise, and
  at best a good deal less CPU per delivery": the stored 1.6 to 1.75 is matched by every run, and the quiet run is below it. No claim is made that
  the framework's `SendAsync` is cheaper than the hand-written writer; one writer loop per connection and one shared frame per identifier is the same
  work, and what the old transport spent on its own framing and masking checks is gone.
- **Latency** is the driver reading 1,000 frames (12 to 19 ms for both servers in `cable-fanout.md`): 15.6 to 17.9 ms here, ctl included, so it moved
  with the host and not with the build.
- **Memory.** `rss_kb_per_client` is the resident set per client with 1,000 idle subscribed clients, from the first rep: 33.4 kB (quiet run), 44.3 kB
  (loaded run) and 44.1 kB (ctl) against the stored 30.3 kB. The resident set counts the GC heap's slack and the thread pool, so it swings 10 kB with the
  host; `MemoryTests` L7 counts managed bytes instead (`GC.GetTotalMemory` before and after 1,000 connections, in process, three runs of the test): **16.2
  to 16.3 KB per connection on the server side**, of which a bare accepted WebSocket on the same Kestrel, with no Cable, is 13.6 to 13.7 KB and
  Cable's own part 2.6 KB. The design's figure (under 16 KB) is missed by 0.3 KB, all of it the host's; the test bounds Cable's part (under 5 KB) and
  the whole (under 20 KB). Rust's resident set is about 9 kB per client (`cable-fanout.md`).
- **Slow consumers.** `LimitTests` L1 shows a client that never reads cut after the kernel and Kestrel have absorbed a few hundred frames of 600 bytes and
  its queue has filled (another client on the stream receives every numbered frame, in order, meanwhile), and L2 shows a stalled 64 KB send cut about one
  `SendTimeout` (1 s in the test) after it began. In neither does a broadcaster wait.

The raw lines are in `bench/results/dotnetway-3-6/` (`cable-run.log`, `cable-ctl.log`, `cable-run2.log`, `l7.txt`).

## Phase 3 gate (2026-10-21, HEAD `c2742ad`, hybrid reads in)

`bench/cable/run fsharp --reps 2 --plain`, same method, after `bin/verify` and the pixel slices had finished (host load average 2.4 at the start;
`bench/results/dotnetway-p3/cable-run.log`). Both reps ran to the end and printed their rows; the script's exit status was 1 (the cleanup `wait` on the
killed server), not a failure of a rep.

| run | latency p50 (ms) | single (us/delivery) | burst (us/delivery) | rss per client, rep 1 (kB) |
|---|---|---|---|---|
| stored F#, plain (`cable-fanout.md`) | 12.6, 13.7 | 47.8, 41.3 | 1.75, 1.60 | 30.3 |
| 3.6 run 2 (quiet, load 3.0) | 15.6, 17.2 | 45.7, 43.2 | 0.95, 0.75 | 33.4 |
| Phase 3 gate | 17.0, 17.0 | 59.5, 53.3 | 1.60, 1.85 (user 1.15, 1.40; system 0.45, 0.45) | 44.0 |

Equal to the stored row and to 3.6's loaded run (burst 1.55-1.85); slower than 3.6's quiet run (0.75-0.95), which was the best case of that session. The hybrid
reads touch no Cable path, so the difference is the host (the resident set and the lone-broadcast cost moved with load in 3.6 too). No regression claimed or found.
