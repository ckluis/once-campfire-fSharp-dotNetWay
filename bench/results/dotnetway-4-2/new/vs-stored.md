theDotNetWay in bench/results/dotnetway-4-2/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,149 [8,065-8,321] | 40,422 | 0.202x | 28,623 | **0.285x** | 239 | **34.054x** | 406.6 [406-410] | 93.6 | 122.4 |
| room page | 1 | 2,712 [2,664-2,717] | 7,617 | 0.356x | 6,481 | **0.418x** | 107 | **25.322x** | 466.0 [464-472] | 208.7 | 142.1 |
| messages page | 16 | 7,634 [7,530-7,787] | 37,836 | 0.202x | 30,063 | **0.254x** | 381 | **20.059x** | 436.9 [436-438] | 102.2 | 114.2 |
| messages page | 1 | 2,388 [2,374-2,418] | 8,312 | 0.287x | 6,822 | **0.350x** | 218 | **10.959x** | 513.9 [510-518] | 199.3 | 139.2 |
| sidebar | 16 | 7,670 [7,601-7,829] | 37,555 | 0.204x | 28,238 | **0.272x** | 599 | **12.813x** | 439.7 [437-445] | 101.8 | 123.2 |
| sidebar | 1 | 2,274 [2,270-2,290] | 8,000 | 0.284x | 6,196 | **0.367x** | 287 | **7.935x** | 642.8 [613-649] | 181.0 | 149.0 |
| search | 16 | 7,378 [7,326-7,473] | 34,070 | 0.217x | 26,606 | **0.277x** | 408 | **18.088x** | 462.2 [457-467] | 107.3 | 123.9 |
| search | 1 | 2,294 [2,272-2,302] | 6,055 | 0.379x | 5,646 | **0.406x** | 193 | **11.905x** | 527.9 [527-534] | 315.1 | 158.3 |
| post a message | 16 | 2,030 [2,014-2,055] | 8,905 | 0.228x | 7,174 | **0.283x** | 330 | **6.148x** | 1,468.4 [1,455-1,470] | 353.0 | 385.6 |
| post a message | 1 | 1,113 [1,103-1,121] | 3,052 | 0.365x | 2,779 | **0.401x** | 167 | **6.670x** | 1,625.3 [1,621-1,631] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
