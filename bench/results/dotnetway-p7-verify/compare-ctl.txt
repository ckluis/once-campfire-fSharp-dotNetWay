theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p7-verify/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,416 [7,395-7,434] | 37,555 | 0.197x | 28,238 | **0.263x** | 599 | **12.388x** | 448.6 [446-452] | 101.8 | 123.2 |
| sidebar | 1 | 2,236 [2,094-2,243] | 8,000 | 0.279x | 6,196 | **0.361x** | 287 | **7.801x** | 560.1 [560-594] | 181.0 | 149.0 |
| search | 16 | 7,161 [6,630-7,206] | 34,070 | 0.210x | 26,606 | **0.269x** | 408 | **17.555x** | 490.9 [489-536] | 107.3 | 123.9 |
| search | 1 | 1,906 [1,872-1,993] | 6,055 | 0.315x | 5,646 | **0.338x** | 193 | **9.889x** | 638.7 [613-649] | 315.1 | 158.3 |
| post a message | 16 | 2,144 [2,128-2,152] | 8,905 | 0.241x | 7,174 | **0.299x** | 330 | **6.497x** | 1,502.9 [1,491-1,510] | 353.0 | 385.6 |
| post a message | 1 | 1,090 [1,082-1,105] | 3,052 | 0.357x | 2,779 | **0.392x** | 167 | **6.532x** | 1,698.5 [1,696-1,717] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 24 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
