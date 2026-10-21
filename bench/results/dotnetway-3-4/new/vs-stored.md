theDotNetWay in bench/results/dotnetway-3-4/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,326 [8,250-8,415] | 40,422 | 0.206x | 28,623 | **0.291x** | 239 | **34.794x** | 400.0 [396-401] | 93.6 | 122.4 |
| room page | 1 | 2,730 [2,725-2,796] | 7,617 | 0.358x | 6,481 | **0.421x** | 107 | **25.490x** | 462.8 [449-463] | 208.7 | 142.1 |
| messages page | 16 | 7,660 [7,607-7,742] | 37,836 | 0.202x | 30,063 | **0.255x** | 381 | **20.126x** | 440.2 [438-441] | 102.2 | 114.2 |
| messages page | 1 | 2,404 [2,360-2,470] | 8,312 | 0.289x | 6,822 | **0.352x** | 218 | **11.033x** | 511.5 [500-519] | 199.3 | 139.2 |
| sidebar | 16 | 7,708 [7,685-7,778] | 37,555 | 0.205x | 28,238 | **0.273x** | 599 | **12.876x** | 437.9 [437-439] | 101.8 | 123.2 |
| sidebar | 1 | 2,304 [2,294-2,362] | 8,000 | 0.288x | 6,196 | **0.372x** | 287 | **8.041x** | 534.2 [520-534] | 181.0 | 149.0 |
| search | 16 | 7,535 [7,376-7,580] | 34,070 | 0.221x | 26,606 | **0.283x** | 408 | **18.474x** | 453.7 [452-464] | 107.3 | 123.9 |
| search | 1 | 2,322 [2,253-2,350] | 6,055 | 0.383x | 5,646 | **0.411x** | 193 | **12.047x** | 524.0 [520-540] | 315.1 | 158.3 |
| post a message | 16 | 2,050 [2,040-2,059] | 8,905 | 0.230x | 7,174 | **0.286x** | 330 | **6.210x** | 1,456.2 [1,451-1,479] | 353.0 | 385.6 |
| post a message | 1 | 1,115 [1,109-1,138] | 3,052 | 0.365x | 2,779 | **0.401x** | 167 | **6.682x** | 1,621.3 [1,620-1,658] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
