theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p8-verify/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 6,834 [6,780-7,505] | 37,555 | 0.182x | 28,238 | **0.242x** | 599 | **11.417x** | 476.4 [443-487] | 101.8 | 123.2 |
| sidebar | 1 | 2,126 [2,069-2,278] | 8,000 | 0.266x | 6,196 | **0.343x** | 287 | **7.419x** | 585.8 [554-598] | 181.0 | 149.0 |
| post a message | 16 | 1,974 [1,968-2,171] | 8,905 | 0.222x | 7,174 | **0.275x** | 330 | **5.980x** | 1,634.8 [1,480-1,639] | 353.0 | 385.6 |
| post a message | 1 | 1,020 [955-1,085] | 3,052 | 0.334x | 2,779 | **0.367x** | 167 | **6.110x** | 1,859.5 [1,768-1,934] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
