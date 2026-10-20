F# port in bench/results/dotnetway-p2-12/control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | F# port req/s | F# port stored | vs port | Rust stored | **F# port / Rust (vs stored Rust baseline)** | Rails stored | **F# port / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 35,030 | 37,555 | 0.933x | 28,238 | **1.241x** | 599 | **58.520x** | 102.1 | 101.8 | 123.2 |
| sidebar | 1 | 7,028 | 8,000 | 0.879x | 6,196 | **1.134x** | 287 | **24.523x** | 214.6 | 181.0 | 149.0 |
| post a message | 16 | 5,605 | 8,905 | 0.629x | 7,174 | **0.781x** | 330 | **16.981x** | 399.9 | 353.0 | 385.6 |
| post a message | 1 | 2,838 | 3,052 | 0.930x | 2,779 | **1.021x** | 167 | **17.002x** | 653.0 | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
