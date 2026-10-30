theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2-0/old against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 1,882 [1,871-1,920] | 8,905 | 0.211x | 7,174 | **0.262x** | 330 | **5.702x** | 1,641.3 [1,596-1,644] | 353.0 | 385.6 |
| post a message | 1 | 979 [973-985] | 3,052 | 0.321x | 2,779 | **0.352x** | 167 | **5.864x** | 1,829.0 [1,807-1,835] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
