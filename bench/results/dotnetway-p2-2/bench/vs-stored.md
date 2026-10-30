theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2-2/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,531 [6,292-6,587] | 40,422 | 0.162x | 28,623 | **0.228x** | 239 | **27.292x** | 518.8 [517-538] | 93.6 | 122.4 |
| room page | 1 | 2,033 [2,016-2,047] | 7,617 | 0.267x | 6,481 | **0.314x** | 107 | **18.981x** | 591.5 [591-593] | 208.7 | 142.1 |
| post a message | 16 | 1,881 [1,779-1,897] | 8,905 | 0.211x | 7,174 | **0.262x** | 330 | **5.699x** | 1,631.6 [1,615-1,743] | 353.0 | 385.6 |
| post a message | 1 | 950 [944-963] | 3,052 | 0.311x | 2,779 | **0.342x** | 167 | **5.690x** | 1,860.6 [1,858-1,877] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
