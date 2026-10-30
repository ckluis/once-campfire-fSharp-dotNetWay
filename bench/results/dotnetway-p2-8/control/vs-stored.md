F# port in bench/results/dotnetway-p2-8/control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | F# port req/s | F# port stored | vs port | Rust stored | **F# port / Rust (vs stored Rust baseline)** | Rails stored | **F# port / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 36,480 | 37,555 | 0.971x | 28,238 | **1.292x** | 599 | **60.942x** | 98.3 | 101.8 | 123.2 |
| sidebar | 1 | 7,204 | 8,000 | 0.900x | 6,196 | **1.163x** | 287 | **25.134x** | 205.0 | 181.0 | 149.0 |
| post a message | 16 | 5,348 | 8,905 | 0.601x | 7,174 | **0.745x** | 330 | **16.200x** | 379.4 | 353.0 | 385.6 |
| post a message | 1 | 2,895 | 3,052 | 0.949x | 2,779 | **1.042x** | 167 | **17.345x** | 647.6 | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
