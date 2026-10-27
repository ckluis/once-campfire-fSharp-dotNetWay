theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p8-verify/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 6,938 [6,860-7,413] | 37,555 | 0.185x | 28,238 | **0.246x** | 599 | **11.590x** | 480.7 [450-481] | 101.8 | 123.2 |
| sidebar | 1 | 2,116 [2,062-2,225] | 8,000 | 0.264x | 6,196 | **0.341x** | 287 | **7.382x** | 597.3 [565-600] | 181.0 | 149.0 |
| post a message | 16 | 2,144 [2,008-2,167] | 8,905 | 0.241x | 7,174 | **0.299x** | 330 | **6.495x** | 1,509.8 [1,502-1,613] | 353.0 | 385.6 |
| post a message | 1 | 1,023 [1,003-1,079] | 3,052 | 0.335x | 2,779 | **0.368x** | 167 | **6.131x** | 1,798.6 [1,721-1,823] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
