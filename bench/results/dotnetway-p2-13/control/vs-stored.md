F# port in bench/results/dotnetway-p2-13/control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | F# port req/s | F# port stored | vs port | Rust stored | **F# port / Rust (vs stored Rust baseline)** | Rails stored | **F# port / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 37,529 | 37,555 | 0.999x | 28,238 | **1.329x** | 599 | **62.695x** | 95.7 | 101.8 | 123.2 |
| sidebar | 1 | 7,899 | 8,000 | 0.987x | 6,196 | **1.275x** | 287 | **27.560x** | 181.9 | 181.0 | 149.0 |
| post a message | 16 | 5,057 | 8,905 | 0.568x | 7,174 | **0.705x** | 330 | **15.319x** | 369.7 | 353.0 | 385.6 |
| post a message | 1 | 2,993 | 3,052 | 0.981x | 2,779 | **1.077x** | 167 | **17.935x** | 639.1 | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
