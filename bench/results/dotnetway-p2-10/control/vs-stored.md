F# port in bench/results/dotnetway-p2-10/control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | F# port req/s | F# port stored | vs port | Rust stored | **F# port / Rust (vs stored Rust baseline)** | Rails stored | **F# port / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 37,247 | 37,555 | 0.992x | 28,238 | **1.319x** | 599 | **62.224x** | 96.8 | 101.8 | 123.2 |
| sidebar | 1 | 7,559 | 8,000 | 0.945x | 6,196 | **1.220x** | 287 | **26.375x** | 193.5 | 181.0 | 149.0 |
| post a message | 16 | 8,667 | 8,905 | 0.973x | 7,174 | **1.208x** | 330 | **26.255x** | 362.3 | 353.0 | 385.6 |
| post a message | 1 | 2,897 | 3,052 | 0.949x | 2,779 | **1.043x** | 167 | **17.360x** | 652.9 | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 4 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
