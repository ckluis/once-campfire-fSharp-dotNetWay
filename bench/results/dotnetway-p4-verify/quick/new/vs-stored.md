theDotNetWay in bench/results/dotnetway-p4-verify/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,619 [7,557-7,646] | 37,555 | 0.203x | 28,238 | **0.270x** | 599 | **12.727x** | 432.7 [430-434] | 101.8 | 123.2 |
| sidebar | 1 | 2,525 [2,468-2,531] | 8,000 | 0.316x | 6,196 | **0.407x** | 287 | **8.810x** | 491.7 [490-499] | 181.0 | 149.0 |
| post a message | 16 | 2,046 [2,045-2,050] | 8,905 | 0.230x | 7,174 | **0.285x** | 330 | **6.200x** | 1,464.8 [1,461-1,465] | 353.0 | 385.6 |
| post a message | 1 | 1,150 [1,125-1,158] | 3,052 | 0.377x | 2,779 | **0.414x** | 167 | **6.892x** | 1,640.6 [1,622-1,648] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
