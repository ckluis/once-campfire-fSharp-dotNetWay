theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-0/before-rerun against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 34,294 | 37,555 | 0.913x | 28,238 | **1.214x** | 599 | **57.291x** | 102.9 | 101.8 | 123.2 |
| sidebar | 1 | 5,966 | 8,000 | 0.746x | 6,196 | **0.963x** | 287 | **20.817x** | 227.7 | 181.0 | 149.0 |
| search | 16 | 32,658 | 34,070 | 0.959x | 26,606 | **1.227x** | 408 | **80.063x** | 111.3 | 107.3 | 123.9 |
| search | 1 | 6,074 | 6,055 | 1.003x | 5,646 | **1.076x** | 193 | **31.518x** | 309.1 | 315.1 | 158.3 |

Checks:
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
