theDotNetWay in bench/results/quick-85 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 2,133 [2,132-2,177] | 8,905 | 0.240x | 7,174 | **0.297x** | 330 | **6.463x** | 1,503.2 [1,484-1,506] | 353.0 | 385.6 |
| post a message | 1 | 1,086 [1,082-1,098] | 3,052 | 0.356x | 2,779 | **0.391x** | 167 | **6.505x** | 1,768.1 [1,745-1,779] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
