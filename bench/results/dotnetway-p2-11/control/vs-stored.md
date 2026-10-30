F# port in bench/results/dotnetway-p2-11/control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | F# port req/s | F# port stored | vs port | Rust stored | **F# port / Rust (vs stored Rust baseline)** | Rails stored | **F# port / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 36,010 | 37,555 | 0.959x | 28,238 | **1.275x** | 599 | **60.158x** | 99.7 | 101.8 | 123.2 |
| sidebar | 1 | 7,192 | 8,000 | 0.899x | 6,196 | **1.161x** | 287 | **25.093x** | 201.6 | 181.0 | 149.0 |
| post a message | 16 | 4,853 | 8,905 | 0.545x | 7,174 | **0.676x** | 330 | **14.701x** | 464.6 | 353.0 | 385.6 |
| post a message | 1 | 2,802 | 3,052 | 0.918x | 2,779 | **1.008x** | 167 | **16.788x** | 656.7 | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
