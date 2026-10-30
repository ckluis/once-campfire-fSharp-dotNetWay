# Campfire, six ways: theDotNetWay's Phase 9 bench (2026-10-28)

Requests per second at 16 clients, one column per implementation with its commit, in the form of Ryan Rodemoyer's "Campfire, seven ways"
table: two row groups ("Each route alone", "Reads while a writer posts 10/s"), the fastest per row in bold. `table.json` is the
machine-readable form the landing page reads (`{apps:[{id,name,commit}], rows:[{group, route, values:{app:{median,min,max,n,sources}}}]}`);
every cell's `sources` are the per-round harness files it pools. `aggregate.py` makes `table.json` and `table.md` from the session folders.

## Result


**Each route alone** (requests per second, c=16, median [min-max] over 6 rounds)

| route | theDotNetWay `f9f4637` | onCrack `3770dff` | F# port `9954ad0` | Rails `ca9b963` | Rust `6dae2fd` | C `873a51f` |
|---|---:|---:|---:|---:|---:|---:|
| Room page | 3,300 [3,143-3,479] | **168,948 [158,235-170,083]** | 35,365 [30,062-36,203] | 4,880 [4,208-5,033] | 50,146 [48,905-51,077] | 76,748 [72,432-78,822] |
| Messages page | 7,349 [6,742-7,530] | **178,725 [166,579-180,358]** | 41,083 [36,917-42,393] | 4,882 [4,282-4,980] | 46,978 [45,988-47,334] | 77,560 [74,848-78,419] |
| Sidebar | 7,707 [7,109-7,826] | **190,428 [180,065-191,398]** | 37,524 [35,062-38,032] | 5,243 [5,099-5,347] | 55,269 [53,590-56,251] | 83,030 [82,330-83,644] |
| Search | 7,187 [6,603-7,336] | **185,206 [175,275-186,868]** | 33,742 [31,910-34,121] | 5,063 [4,918-5,247] | 54,112 [53,484-56,107] | 82,902 [80,960-83,961] |
| Post a message | 2,263 [2,086-2,291] | **15,200 [13,785-15,326]** | 9,317 [9,072-9,548] | 398 [396-405] | 6,414 [6,192-6,581] | 5,819 [5,567-5,850] |

**Reads while a writer posts 10/s** (requests per second, c=16, median [min-max] over 6 rounds)

| route | theDotNetWay `f9f4637` | onCrack `3770dff` | F# port `9954ad0` | Rails `ca9b963` | Rust `6dae2fd` | C `873a51f` |
|---|---:|---:|---:|---:|---:|---:|
| Room page | 2,868 [2,629-2,997] | **162,224 [153,722-163,951]** | 31,509 [24,887-33,747] | 2,711 [2,294-2,863] | 46,560 [41,866-47,446] | 72,040 [59,997-72,818] |
| Messages page | 6,616 [5,683-6,771] | **177,586 [166,852-181,542]** | 41,420 [36,913-41,757] | 3,282 [3,070-3,508] | 44,347 [39,620-45,256] | 73,313 [61,891-74,180] |
| Sidebar | 7,322 [6,220-7,561] | **187,231 [173,676-188,500]** | 37,336 [34,739-38,110] | 4,154 [3,629-4,446] | 52,332 [41,860-55,495] | 82,266 [67,695-84,212] |
| Search | 6,886 [6,169-6,994] | **182,827 [144,568-184,054]** | 34,645 [32,655-34,956] | 3,901 [3,410-4,209] | 53,424 [44,880-54,374] | 80,064 [66,767-80,848] |

**theDotNetWay over Rails (this harness's Rails), pooled medians**

| group | route | x Rails |
|---|---|---:|
| Each route alone | Room page | 0.68x |
| Each route alone | Messages page | 1.51x |
| Each route alone | Sidebar | 1.47x |
| Each route alone | Search | 1.42x |
| Each route alone | Post a message | 5.68x |
| Reads while a writer posts 10/s | Room page | 1.06x |
| Reads while a writer posts 10/s | Messages page | 2.02x |
| Reads while a writer posts 10/s | Sidebar | 1.76x |
| Reads while a writer posts 10/s | Search | 1.77x |

**Floor check (step 5): FAILED here.** theDotNetWay is at least 5x Rails on only one of the five normal-profile rows (post a message, 5.68x). Room page
is 0.68x, messages page 1.51x, sidebar 1.47x, search 1.42x. This Rails is upstream's current Rails (`ca9b963`) in Basecamp's harness: four Puma workers
with its response caching on (about 4,900-5,200 req/s on a page), against `bench/quick`'s Rails (stored medians, about 240-600 req/s on a page: a different load and
configuration) that the 5x floor was set on; the two Rails figures are not comparable, and the floor as written does not hold against this harness's Rails. No code was changed in this step; reported as a blocker with the numbers above. Rails `0aa339d` in onCrack's
round 5 read 3,600-4,900 on the same rows, so the Rails column is not a regression of the newer commit. The room page is theDotNetWay's thinnest read
here (3,300 against 7,200-7,700 for the other three pages); not investigated.

## Method

Upstream's method as onCrack's round 5 ran it (`once-campfire-fsharp-oncrack/AGENTS.md`, "Upstream's method"): Basecamp's harness
`once-campfire-verification`, branch `oncrack-adapter` (its `oncrack` and `fsharp` adapters plus two local commits for this step, never pushed:
`4d47384` adds `dotnetway`, measured like the port: no page cache, Rails 90b3300's `created_at` search order, topology row; `f3103c7` caps each measured container's
json-file log at 64m x 2 as `docker run` flags, because upstream Rust's stdout filled the Mac's disk past the 12 GB floor in the first attempt and the
VM daemon's config is not mine to change; the same cap applies to every app. `verification-adapter-dotnetway.diff` is `git diff a05b344 f3103c7`). Run in colima
(8 vCPUs, 6 GiB) from a 3 GiB tmpfs copy at `/mnt/cfv`, seed built by `bin/seed --image campfire-reference:app` and made group-writable
(`setup.sh`), the workspace a set of symlinks to clean detached git worktrees of each app (`/Users/clank/cfsix-ws/`, removed afterwards), load
generator upstream's, Rust 1.98.1, static aarch64 (sha `351a66af`), seed sha `b775e125`.

```
bin/benchmark --workspace /mnt/cfv/ws --rounds 3 --duration 8 --concurrencies 16 --cpus 0-3 --client-cpus 4-7 \
  --apps rails,rust,c,fsharp,oncrack,dotnetway --routes room_show,messages_page,sidebar,search,post_message      # normal
  (same, with --mixed-write-rate 10 --routes room_show,messages_page,sidebar,search)                              # mixed
```

Four sessions, every app in the same session: `normal-1`, `mixed-1`, `normal-2`, `mixed-2` (`drive.sh`, detached, polled; `env.sh` has the images). Pooled: the
median over all 6 rounds (2 sessions x 3 rounds) of every app and row, with the min and max of those 6; cells in `table.json` carry `n` and the files.
Each round's order alternates (apps, then reversed). onCrack's column is the fresh-authorization mode (its default and its headline; its response cache at
64 MB as in its round 5; the cached decision is not run). Numbers from Ryan's GCP rig and from onCrack's earlier rounds are not used.

### Commits and images

| app | commit measured | image |
|---|---|---|
| theDotNetWay | `f9f4637` (branch `dotnetway`, the Phase 9 gate's commit) | `campfire-dotnetway:app`, built from `dcc9434`; `git diff dcc9434 f9f4637 -- src Dockerfile global.json Directory.Packages.props` is empty |
| onCrack | `3770dff` (branch `oncrack`, published head) | `campfire-oncrack:app` rebuilt from a clean worktree of it, `git diff ee5a2e9 3770dff -- src Dockerfile` is empty (the round 5 closing code) |
| F# port | `9954ad0` (branch `port`, published head) | `campfire-fsharp:app` rebuilt from a clean worktree of it |
| Rails | `ca9b963` (basecamp/once-campfire, 2026-10-26, Ruby 4.0.9) | `campfire-rails-upstream:ca9b963` built here (`WEB_CONCURRENCY=4`, `RAILS_MAX_THREADS=1`, as round 5 ran it) |
| Rust | `6dae2fd` (basecamp/once-campfire-rust, 2026-10-09) | `campfire-rust-upstream:6dae2fd` built here |
| C | `873a51f` (basecamp/once-campfire-c, 2026-10-09) | `campfire-c-upstream:cd0cbe4` (round 5's build); `git diff cd0cbe4 873a51f` is `README.md` only, so the binary is the same source, not rebuilt |

Harness `f3103c7`, and every session's `summary.json` records the six source heads and `dirty: false` for each (the harness refuses a checkout that changes).
Search order per app (`metadata.search_order`): created_at for the F# port and theDotNetWay, id for the rest.

### Sessions, host probe and load

`bench/lib/hostprobe.py 0-7` (a fixed spin on every vCPU against the quietest this VM has shown, 55.57 ms) before and after each session, the VM's load average
(`*.host`, `*.loadsamples`) and the Mac's (`host-load-mac.txt`: time, load average, CPU of everything outside the VM, free GiB, every 30 s, 05:03-06:21).

| session | time (EDT) | probe ratio before / after | Mac load 1-min range | Mac non-VM CPU max | Mac free |
|---|---|---|---|---|---|
| normal-1 | 05:20:58-05:37:33 | 1.049 / 1.065 | 4.1-14.0 | 155% | 24 GB |
| mixed-1 | 05:37:38-05:51:14 | 1.079 / 1.075 | 5.0-6.2 | 289% | 24 GB |
| normal-2 | 05:51:19-06:07:54 | 1.056 / 1.078 | 3.9-7.5 | 171% | 24 GB |
| mixed-2 | 06:07:59-06:21:35 | 1.063 / 1.067 | 5.0-7.6 | 518% | 24 GB |

Other sessions were running on the Mac (a load of 14 once in normal-1; the spread of the rounds in the table shows it: Rails and the port swing most).
`aborted-normal-1.*` (kept, not pooled): the first normal session, stopped by `guard.sh` at 11 GB free in its last app (Rust's container log, before the log cap);
its numbers are not in the table.

### Raw files

`normal-N/` and `mixed-N/` hold the harness output as written: `{app}-{round}.json` (per round: preflight contract checks, the samples with latency and
the write audits), `raw/http-*.json`, `summary.json` (metadata and medians for that session). `*.log` are the harness's output.
