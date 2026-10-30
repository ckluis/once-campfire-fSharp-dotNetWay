theDotNetWay in bench/results/dotnetway-4-4/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| messages page | 16 | 7,283 [7,028-7,366] | 37,836 | 0.192x | 30,063 | **0.242x** | 381 | **19.135x** | 457.0 [453-477] | 102.2 | 114.2 |
| messages page | 1 | 2,411 [2,369-2,437] | 8,312 | 0.290x | 6,822 | **0.353x** | 218 | **11.066x** | 508.4 [505-518] | 199.3 | 139.2 |
| post a message | 16 | 2,098 [2,095-2,103] | 8,905 | 0.236x | 7,174 | **0.292x** | 330 | **6.356x** | 1,421.0 [1,417-1,431] | 353.0 | 385.6 |
| post a message | 1 | 1,183 [1,173-1,187] | 3,052 | 0.388x | 2,779 | **0.426x** | 167 | **7.090x** | 1,600.2 [1,596-1,633] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
