theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2-1/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 5,800 | 37,555 | 0.154x | 28,238 | **0.205x** | 599 | **9.690x** | 590.5 | 101.8 | 123.2 |
| sidebar | 1 | 1,762 | 8,000 | 0.220x | 6,196 | **0.284x** | 287 | **6.149x** | 662.3 | 181.0 | 149.0 |
| post a message | 16 | 1,853 | 8,905 | 0.208x | 7,174 | **0.258x** | 330 | **5.613x** | 1,658.8 | 353.0 | 385.6 |
| post a message | 1 | 935 | 3,052 | 0.306x | 2,779 | **0.336x** | 167 | **5.600x** | 1,875.2 | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
