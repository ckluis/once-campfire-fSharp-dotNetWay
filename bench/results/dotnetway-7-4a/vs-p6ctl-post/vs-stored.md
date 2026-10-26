theDotNetWay in bench/results/dotnetway-7-4a/vs-p6ctl-post against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 2,139 [1,990-2,140] | 8,905 | 0.240x | 7,174 | **0.298x** | 330 | **6.481x** | 1,515.0 [1,515-1,650] | 353.0 | 385.6 |
| post a message | 1 | 1,059 [1,018-1,071] | 3,052 | 0.347x | 2,779 | **0.381x** | 167 | **6.345x** | 1,749.8 [1,728-1,817] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
