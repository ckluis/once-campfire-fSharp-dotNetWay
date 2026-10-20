theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2-0/new2 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 1,875 [1,796-1,893] | 8,905 | 0.211x | 7,174 | **0.261x** | 330 | **5.679x** | 1,653.0 [1,623-1,717] | 353.0 | 385.6 |
| post a message | 1 | 963 [806-968] | 3,052 | 0.315x | 2,779 | **0.346x** | 167 | **5.768x** | 1,844.2 [1,839-2,172] | 629.1 | 403.7 |

Checks:
  note post_message c=1: host probe 1.106 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
