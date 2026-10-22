theDotNetWay in bench/results/quick-63 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,734 [7,468-7,759] | 37,555 | 0.206x | 28,238 | **0.274x** | 599 | **12.920x** | 421.9 [422-440] | 101.8 | 123.2 |
| sidebar | 1 | 2,562 [2,466-2,585] | 8,000 | 0.320x | 6,196 | **0.414x** | 287 | **8.941x** | 479.4 [476-499] | 181.0 | 149.0 |
| post a message | 16 | 2,011 [1,954-2,047] | 8,905 | 0.226x | 7,174 | **0.280x** | 330 | **6.092x** | 1,480.0 [1,470-1,553] | 353.0 | 385.6 |
| post a message | 1 | 1,123 [1,107-1,124] | 3,052 | 0.368x | 2,779 | **0.404x** | 167 | **6.728x** | 1,639.0 [1,637-1,688] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
