theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-0/after-rerun against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 30,788 | 37,555 | 0.820x | 28,238 | **1.090x** | 599 | **51.433x** | 108.6 | 101.8 | 123.2 |
| sidebar | 1 | 5,596 | 8,000 | 0.699x | 6,196 | **0.903x** | 287 | **19.525x** | 235.0 | 181.0 | 149.0 |
| search | 16 | 17,436 | 34,070 | 0.512x | 26,606 | **0.655x** | 408 | **42.745x** | 158.2 | 107.3 | 123.9 |
| search | 1 | 3,843 | 6,055 | 0.635x | 5,646 | **0.681x** | 193 | **19.942x** | 373.4 | 315.1 | 158.3 |

Checks:
  note search c=1: host probe 1.117 after the window (noisy host)
  note search c=16: warm-up did not settle (reached the maximum)
  note search c=16: host probe 1.12 after the window (noisy host)
  note sidebar c=1: host probe 1.145 after the window (noisy host)
  note sidebar c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
