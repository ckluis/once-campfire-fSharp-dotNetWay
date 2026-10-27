theDotNetWay in bench/results/quick-82 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 2,150 [2,148-2,161] | 8,905 | 0.241x | 7,174 | **0.300x** | 330 | **6.514x** | 1,504.4 [1,484-1,521] | 353.0 | 385.6 |
| post a message | 1 | 1,118 [1,055-1,118] | 3,052 | 0.366x | 2,779 | **0.402x** | 167 | **6.696x** | 1,748.6 [1,740-1,829] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
