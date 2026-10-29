# Campfire, six ways: theDotNetWay's Phase 10 bench (2026-10-29)

Second run of the six-ways bench, after Phase 10 (performance on upstream's harness). The first run (Phase 9, theDotNetWay `f9f4637`, 0.7x-1.5x Rails on reads)
is kept in `before-phase10/` and is not pooled here. Requests per second at 16 clients, one column per implementation with its commit, in the form of Ryan Rodemoyer's
"Campfire, seven ways" table: two row groups, the fastest per row in bold. `table.json` is the machine-readable form the landing page reads
(`{apps:[{id,name,commit}], rows:[{group, route, values:{app:{median,min,max,n,sources}}}]}`); every cell's `sources` are the per-round harness files it pools.
`aggregate.py` makes `table.json` and `table.md` from the session folders.

## Result

**Each route alone** (requests per second, c=16, median [min-max] over 6 rounds)

| route | theDotNetWay `75487b7` | onCrack `3770dff` | F# port `9954ad0` | Rails `ca9b963` | Rust `6dae2fd` | C `873a51f` |
|---|---:|---:|---:|---:|---:|---:|
| Room page | 34,989 [34,059-35,620] | **169,088 [167,975-170,184]** | 35,619 [31,680-36,667] | 4,944 [4,686-5,059] | 49,902 [47,158-50,206] | 77,135 [74,907-78,851] |
| Messages page | 33,831 [32,446-34,269] | **179,077 [176,146-179,802]** | 42,126 [41,200-42,774] | 4,953 [4,738-5,020] | 47,112 [45,976-47,600] | 77,086 [74,958-78,652] |
| Sidebar | 41,581 [40,390-42,302] | **190,320 [189,060-191,608]** | 38,195 [37,953-38,619] | 5,425 [5,041-5,567] | 55,561 [54,090-56,086] | 83,137 [81,450-84,689] |
| Search | 41,325 [40,019-42,399] | **184,644 [183,974-185,593]** | 34,009 [33,751-34,483] | 5,280 [4,770-5,383] | 55,920 [54,801-56,330] | 82,402 [81,560-83,325] |
| Post a message | 2,901 [2,781-2,940] | **15,149 [14,549-15,302]** | 9,516 [9,452-9,578] | 401 [392-407] | 6,498 [6,418-6,570] | 5,858 [5,812-5,909] |

**Reads while a writer posts 10/s** (requests per second, c=16, median [min-max] over 6 rounds)

| route | theDotNetWay `75487b7` | onCrack `3770dff` | F# port `9954ad0` | Rails `ca9b963` | Rust `6dae2fd` | C `873a51f` |
|---|---:|---:|---:|---:|---:|---:|
| Room page | 32,886 [32,345-33,772] | **163,174 [160,988-164,058]** | 33,217 [31,722-33,959] | 2,800 [2,520-2,876] | 47,411 [46,435-48,216] | 71,829 [70,872-71,976] |
| Messages page | 33,012 [31,630-33,442] | **178,188 [176,259-181,944]** | 41,137 [39,970-41,517] | 3,461 [3,061-3,613] | 45,002 [43,537-45,752] | 73,843 [72,986-74,742] |
| Sidebar | 40,246 [38,353-40,578] | **187,713 [187,174-189,143]** | 37,798 [37,303-38,324] | 4,267 [4,196-4,425] | 55,628 [53,581-56,234] | 82,396 [81,439-84,035] |
| Search | 39,610 [38,017-40,230] | **182,965 [181,279-184,444]** | 34,790 [34,584-35,410] | 4,084 [3,847-4,187] | 54,140 [50,690-54,722] | 79,870 [78,460-83,297] |

**theDotNetWay over Rails (this harness's Rails), pooled medians**

| group | route | x Rails |
|---|---|---:|
| Each route alone | Room page | 7.08x |
| Each route alone | Messages page | 6.83x |
| Each route alone | Sidebar | 7.66x |
| Each route alone | Search | 7.83x |
| Each route alone | Post a message | 7.24x |
| Reads while a writer posts 10/s | Room page | 11.75x |
| Reads while a writer posts 10/s | Messages page | 9.54x |
| Reads while a writer posts 10/s | Sidebar | 9.43x |
| Reads while a writer posts 10/s | Search | 9.70x |

**Floor check (step 5), against the Phase 10 target (every normal route at least 5x upstream's current Rails in the same session): MET.** Normal profile: room page 7.08x,
messages page 6.83x, sidebar 7.66x, search 7.83x, post a message 7.24x (thinnest: messages page, 6.83x). Mixed profile: 9.43x-11.75x. No route is under 5x. The Phase 10 gate's own
`bench/upstream-quick` read 6.76x-8.04x normal and 9.16x-12.17x mixed, so this run agrees within about 5%. Against the first run theDotNetWay is 4.6x-10.6x faster on the page rows
(room page 3,300 to 34,989) and 1.28x on post (2,263 to 2,901); Rails moved +-4% between the runs.

## Method

Upstream's method as onCrack's round 5 ran it (`once-campfire-fsharp-oncrack/AGENTS.md`, "Upstream's method"): Basecamp's harness `once-campfire-verification`, branch
`oncrack-adapter` at `eb45b63` (local commits, never pushed: `4d47384` adds the `dotnetway` adapter, `f3103c7` caps each container's json-file log at 64m x 2, `eb45b63`
records the page response cache in the `dotnetway` row; `verification-adapter-dotnetway.diff` is `git diff a05b344 eb45b63`). The adapter needed no change for Phase 10. Run in colima
(8 vCPUs, 6 GiB) from a 3 GiB tmpfs copy at `/mnt/cfv`, seed built by `bin/seed --image campfire-reference:app` and made group-writable (`setup.sh`; seed sha `b62498d4`), the workspace a set of
symlinks to clean detached git worktrees of each app (`/Users/clank/cfsix-ws/`, removed afterwards), upstream's load generator (Rust 1.98.1, static aarch64, sha `351a66af`).

```
bin/benchmark --workspace /mnt/cfv/ws --rounds 3 --duration 8 --concurrencies 16 --cpus 0-3 --client-cpus 4-7 \
  --apps rails,rust,c,fsharp,oncrack,dotnetway --routes room_show,messages_page,sidebar,search,post_message      # normal
  (same, with --mixed-write-rate 10 --routes room_show,messages_page,sidebar,search)                              # mixed
```

Four sessions, every app in the same session: `normal-1`, `mixed-1`, `normal-2`, `mixed-2` (`drive.sh`, detached, polled; `env.sh` has the images). Pooled: the median over all
6 rounds (2 sessions x 3 rounds) of every app and row, with the min and max of those 6. onCrack's column is the fresh-authorization mode (its response cache at 64 MB as in its round 5; the
cached decision is not run). Numbers from Ryan's GCP rig and from onCrack's earlier rounds are not used.

### Commits and images

| app | commit measured | image |
|---|---|---|
| theDotNetWay | `75487b75` (branch `dotnetway`, the Phase 10 gate's HEAD; `git diff 903d2a8 75487b75 -- src Dockerfile global.json Directory.Packages.props` is empty) | `campfire-dotnetway:app`, label `903d2a8` (the gate runner's rebuild; same source) |
| onCrack | `3770dff` (branch `oncrack`, published head, unchanged since the first run) | `campfire-oncrack:app` (label `3770dff`) |
| F# port | `9954ad0` (local branch `port`, the head the first run used; unchanged) | `campfire-fsharp:app` (label `9954ad0`) |
| Rails | `ca9b963` (basecamp/once-campfire origin/main, checked with ls-remote today: unchanged) | `campfire-rails-upstream:ca9b963` (`WEB_CONCURRENCY=4`, `RAILS_MAX_THREADS=1`) |
| Rust | `6dae2fd` (unchanged) | `campfire-rust-upstream:6dae2fd` |
| C | `873a51f` (unchanged; the binary is `cd0cbe4`'s, README-only difference) | `campfire-c-upstream:cd0cbe4` |

Every session's `summary.json` records the six source heads with `dirty: false`. All six images existed from the first run and were not rebuilt (all heads unchanged; theDotNetWay's image is the
gate's). Search order per app: created_at for the F# port and theDotNetWay, id for the rest.

### Sessions, host probe and load

`bench/lib/hostprobe.py 0-7` (a fixed spin on every vCPU against the quietest this VM has shown, 55.57 ms) before and after each session, and the VM's load average (`*.host`, `*.loadsamples`). The probe column is each session's own `<session>.host` (corrected by the Phases 9-10 verifier: it had been offset against the sessions; the load and the pid counter in each file order them normal-1, mixed-1, normal-2, mixed-2).

| session | time (EDT, 2026-10-29) | probe ratio before / after | VM 1-min load range | Mac free |
|---|---|---|---|---|
| normal-1 | 01:57:54-02:14:29 | 1.028 / 1.080 | 0.49-7.18 | 22 GB |
| mixed-1 | 02:14:34-02:28:10 | 1.064 / 1.066 | 4.23-6.30 | 22 GB |
| normal-2 | 02:28:15-02:44:50 | 1.050 / 1.075 | 3.76-8.04 | 28 GB |
| mixed-2 | 02:44:55-02:58:31 | 1.069 / 1.054 | 4.09-6.89 | 28 GB |

Recorded gap: the Mac-side load sampler's output file was overwritten when the VM's output directory was copied back, so the Mac's own load average and non-VM CPU per session were not kept;
other sessions were running on the Mac (the VM load of 4-6 with the benchmark's 4 server CPUs and 4 client CPUs is typical of this host). The probe ratios (1.03-1.08, none above 1.10) are the evidence of a quiet VM.

### Raw files

`normal-N/` and `mixed-N/` hold the harness output as written: `{app}-{round}.json` (per round: preflight contract checks, the samples with latency and the write audits), `raw/http-*.json`,
`summary.json`. `*.log` are the harness's output.
