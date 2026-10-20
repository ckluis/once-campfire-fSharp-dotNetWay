F# port in bench/results/dotnetway-p2-9/control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | F# port req/s | F# port stored | vs port | Rust stored | **F# port / Rust (vs stored Rust baseline)** | Rails stored | **F# port / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 37,363 | 37,555 | 0.995x | 28,238 | **1.323x** | 599 | **62.417x** | 96.4 | 101.8 | 123.2 |
| sidebar | 1 | 7,792 | 8,000 | 0.974x | 6,196 | **1.257x** | 287 | **27.187x** | 182.9 | 181.0 | 149.0 |
| post a message | 16 | 8,805 | 8,905 | 0.989x | 7,174 | **1.227x** | 330 | **26.674x** | 359.6 | 353.0 | 385.6 |
| post a message | 1 | 3,034 | 3,052 | 0.994x | 2,779 | **1.092x** | 167 | **18.177x** | 633.1 | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
