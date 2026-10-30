# The app's front server (Web.Host on Kestrel) against campfire_kit's

Phase 7 unit 7.6. `bench/front/run` serves the bench endpoints behind each front server on loopback: the app's `Web.Host.build`
(Kestrel, `UseOutputCache` with `OutputCaching.configure`, `UseResponseCompression` with `Compression.configure`; `LOG_REQUESTS=false`)
and the Rust `campfire_kit::front::serve` (from `rust/`, hyper), driven by `wrk -t4 -c64 -d8s`. Apple M4, other work on the machine
(load average 20 at the end of the run, so the figures are noisy; the CPU per request is the number to read).

| Scenario | Web.Host (req/s) | Web.Host (us/request) | campfire_kit (req/s) | campfire_kit (us/request) |
|---|---:|---:|---:|---:|
| tiny page | 180,284 | 29.6 | 153,193 | 25.1 |
| 100 KB page, identity | 62,356 | 73.8 | 62,536 | 84.0 |
| 100 KB page, gzip (the F# side compresses per request; Rust served a cached member) | 86,583 | 61.4 | 98,201 | 73.4 |
| asset, cache hit (OutputCache `/users/1/avatar`; Rust: its cache) | 130,204 | 37.6 | 158,205 | 21.4 |
| asset, zstd, cache hit | 171,550 | 29.8 | 61,511 | 71.7 |
| POST form -> redirect | 173,522 | 29.3 | 132,749 | 24.5 |

The old Campfire.Kit table is `bench/results/front.md` (history). The signed-cookie scenario is gone with the kit's request layer (the
F# side has no `/session`); the Rust side prints it only when `COOKIE` is set.
