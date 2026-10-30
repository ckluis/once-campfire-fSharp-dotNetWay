theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-10/quick-post2 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 1,364 | 8,905 | 0.153x | 7,174 | **0.190x** | 330 | **4.131x** | 2,375.1 | 353.0 | 385.6 |
| post a message | 1 | 650 | 3,052 | 0.213x | 2,779 | **0.234x** | 167 | **3.895x** | 2,577.5 | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 2 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
