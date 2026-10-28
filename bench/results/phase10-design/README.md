# Phase 10 design spikes (2026-10-28)

This is the evidence for `plans/design-perf.md` section 2 (S1-S7). Everything ran in colima on the Mac, in scratch directories that
are now deleted. The code variants were scratch builds that were never committed. Their source is `spike3.diff`, the scratch tree
against `838f700`. It contains spike1's and spike2's switches as well, because spike3 grew from them. The builds were published like
the `Dockerfile` (toolchain image, ReadyToRun, linux-arm64) and layered over `campfire-dotnetway:app` as `:spike1`-`:spike3`. Those
images are removed. Environment variables switched the variants:
- `SPIKE_OPEN_ONCE=1`: one connection per request;
- `SPIKE_PAGE_CACHE=1`: the page cache;
- `DOTNET_*` and `LOG_REQUESTS` as named in each run.

## The harness runs (`harness/`)

Upstream's `once-campfire-verification` at `oncrack-adapter` (`f3103c7`). `run.sh` is the wrapper: `bin/benchmark --rounds 2
--duration 8 --concurrencies 16 --cpus 0-3 --client-cpus 4-7`, `--mixed-write-rate 10` when `MIXED=1`. `setup.sh` made the harness
clone, the loadgen copy, the seed (sha `4b584669`) and the workspace clones. `summ.py` prints medians. Images: Rails
`campfire-rails-upstream:ca9b963` (`WEB_CONCURRENCY=4`, `RAILS_MAX_THREADS=1`), the port `campfire-fsharp:app` (`9954ad0`),
theDotNetWay `campfire-dotnetway:app` (`dcc9434`, with `src` identical to `f9f4637`) unless a run names a spike image.

| run | apps, image | variant | note |
|---|---|---|---|
| `e0` | rails, fsharp, dotnetway | control | a build ran beside its fsharp round 2 (fsharp sidebar 15,390): use dotnetway and rails only |
| `e1-nolog` | dotnetway | `LOG_REQUESTS=false` | round 1 ran beside a build: use round 2 only |
| `e2-hill` | dotnetway | `DOTNET_HillClimbing_Disable=0` | |
| `e3-min16` | dotnetway | `DOTNET_ThreadPool_ForceMinWorkerThreads=16` | |
| `e4-nolog-min16` | dotnetway | both | |
| `s0-base` | rails, dotnetway `:spike1` | no switch | |
| `s1-open` | dotnetway `:spike1` | open once | |
| `s2-cache` | dotnetway `:spike1` | page cache (room page only) | |
| `s3-both` | dotnetway `:spike1` | open once + cache | |
| `s4-both-nolog` | dotnetway `:spike1` | open once + cache, log off | |
| `t0-ctl` ... `t3-fulljit` | dotnetway | `TC_CallCountingDelayMs=0`, `TieredPGO=0`, `TieredCompilation=0` + `ReadyToRun=0` | S3 |
| `u0-ctl` | rails, fsharp, dotnetway | control | the clean control session (S7) |
| `u1-A` | dotnetway `:spike2` | open once + `TC_CallCountingDelayMs=0` | |
| `u2-AB` | dotnetway `:spike2` | + cache that stores only without `Set-Cookie` | the room page is never stored: S6's point |
| `u3-AB-mixed` | rails, dotnetway `:spike2` | as u2, mixed | |
| `v0-AB` | rails, dotnetway `:spike3` | open once + TC + cache (spike3) | S7's A+B, same session as its Rails |
| `v1-AB-mixed` | rails, dotnetway `:spike3` | as v0, mixed | |
| `v2-AB-nolog` | dotnetway `:spike3` | as v0, log off | |

Every run exited 0. Every sample was 2xx and passed the harness's `route-contract-v1` validation.

## The profiles (`perf/`)

`bench/breakdown` from a copy of `bench/` with `SEED` set to the harness's seed and the spike's `-e` switches added (`perf2.sh`,
`perf3.sh`):
- `profile-dnw/`: dotnet-trace, parity seed, base image;
- `perf-dnw/` and `perf-port/`: perf, parity seed. `perf-dnw`'s sidebar ran on a busy host (probe 1.27): ignore it;
- `perf-base-hseed/`, `perf-hit/`, `perf-A/`, `perf-AB/`: perf, harness seed;
- `perf-open/`: ran on a host at probe 2.1: discarded, kept only for the record.

`outputcache-spike.cs` is S6: a minimal ASP.NET Core 11 RC2 app with an `IOutputCachePolicy`, run on the Mac.
