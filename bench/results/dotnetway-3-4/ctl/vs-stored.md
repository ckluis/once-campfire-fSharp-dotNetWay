theDotNetWay in bench/results/dotnetway-3-4/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,261 [8,186-8,286] | 40,422 | 0.204x | 28,623 | **0.289x** | 239 | **34.522x** | 408.3 [398-411] | 93.6 | 122.4 |
| room page | 1 | 2,781 [2,684-2,788] | 7,617 | 0.365x | 6,481 | **0.429x** | 107 | **25.969x** | 457.9 [455-469] | 208.7 | 142.1 |
| messages page | 16 | 7,583 [7,546-7,790] | 37,836 | 0.200x | 30,063 | **0.252x** | 381 | **19.924x** | 442.3 [436-451] | 102.2 | 114.2 |
| messages page | 1 | 2,376 [2,343-2,414] | 8,312 | 0.286x | 6,822 | **0.348x** | 218 | **10.902x** | 516.9 [510-522] | 199.3 | 139.2 |
| sidebar | 16 | 7,796 [7,736-7,920] | 37,555 | 0.208x | 28,238 | **0.276x** | 599 | **13.023x** | 434.0 [434-442] | 101.8 | 123.2 |
| sidebar | 1 | 2,333 [2,318-2,357] | 8,000 | 0.292x | 6,196 | **0.376x** | 287 | **8.139x** | 526.3 [522-535] | 181.0 | 149.0 |
| search | 16 | 7,560 [6,990-7,571] | 34,070 | 0.222x | 26,606 | **0.284x** | 408 | **18.533x** | 452.8 [452-488] | 107.3 | 123.9 |
| search | 1 | 2,348 [2,317-2,374] | 6,055 | 0.388x | 5,646 | **0.416x** | 193 | **12.182x** | 519.6 [518-524] | 315.1 | 158.3 |
| post a message | 16 | 2,060 [2,047-2,069] | 8,905 | 0.231x | 7,174 | **0.287x** | 330 | **6.240x** | 1,455.2 [1,454-1,467] | 353.0 | 385.6 |
| post a message | 1 | 1,142 [1,124-1,144] | 3,052 | 0.374x | 2,779 | **0.411x** | 167 | **6.842x** | 1,625.1 [1,617-1,633] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
