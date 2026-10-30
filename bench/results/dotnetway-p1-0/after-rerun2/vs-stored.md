theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-0/after-rerun2 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 35,315 | 37,555 | 0.940x | 28,238 | **1.251x** | 599 | **58.995x** | 98.9 | 101.8 | 123.2 |
| sidebar | 1 | 7,924 | 8,000 | 0.991x | 6,196 | **1.279x** | 287 | **27.649x** | 189.0 | 181.0 | 149.0 |
| search | 16 | 30,319 | 34,070 | 0.890x | 26,606 | **1.140x** | 408 | **74.330x** | 111.6 | 107.3 | 123.9 |
| search | 1 | 6,391 | 6,055 | 1.055x | 5,646 | **1.132x** | 193 | **33.166x** | 316.7 | 315.1 | 158.3 |

Checks:
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
