theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2-0/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 1,442 [1,412-1,470] | 8,905 | 0.162x | 7,174 | **0.201x** | 330 | **4.367x** | 2,176.9 [2,143-2,240] | 353.0 | 385.6 |
| post a message | 1 | 791 [773-924] | 3,052 | 0.259x | 2,779 | **0.285x** | 167 | **4.741x** | 2,176.8 [1,906-2,235] | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
