F# port in bench/results/dotnetway-p2-7/control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | F# port req/s | F# port stored | vs port | Rust stored | **F# port / Rust (vs stored Rust baseline)** | Rails stored | **F# port / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 35,658 | 37,555 | 0.949x | 28,238 | **1.263x** | 599 | **59.569x** | 100.9 | 101.8 | 123.2 |
| sidebar | 1 | 7,074 | 8,000 | 0.884x | 6,196 | **1.142x** | 287 | **24.682x** | 214.7 | 181.0 | 149.0 |
| post a message | 16 | 4,754 | 8,905 | 0.534x | 7,174 | **0.663x** | 330 | **14.402x** | 383.7 | 353.0 | 385.6 |
| post a message | 1 | 1,784 | 3,052 | 0.585x | 2,779 | **0.642x** | 167 | **10.690x** | 866.2 | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
