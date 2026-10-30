theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2-0/ctl2 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 1,848 [1,816-1,890] | 8,905 | 0.208x | 7,174 | **0.258x** | 330 | **5.598x** | 1,676.0 [1,626-1,719] | 353.0 | 385.6 |
| post a message | 1 | 946 [937-976] | 3,052 | 0.310x | 2,779 | **0.341x** | 167 | **5.670x** | 1,887.5 [1,833-1,888] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
