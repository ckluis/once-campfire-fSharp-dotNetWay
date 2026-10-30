theDotNetWay in bench/results/dotnetway-4-2/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,239 [8,136-8,280] | 40,422 | 0.204x | 28,623 | **0.288x** | 239 | **34.430x** | 399.0 [398-413] | 93.6 | 122.4 |
| room page | 1 | 2,740 [2,692-2,745] | 7,617 | 0.360x | 6,481 | **0.423x** | 107 | **25.582x** | 462.5 [461-469] | 208.7 | 142.1 |
| messages page | 16 | 7,579 [7,440-7,609] | 37,836 | 0.200x | 30,063 | **0.252x** | 381 | **19.914x** | 440.7 [440-461] | 102.2 | 114.2 |
| messages page | 1 | 2,403 [2,373-2,418] | 8,312 | 0.289x | 6,822 | **0.352x** | 218 | **11.028x** | 512.1 [509-517] | 199.3 | 139.2 |
| sidebar | 16 | 7,683 [7,680-7,704] | 37,555 | 0.205x | 28,238 | **0.272x** | 599 | **12.835x** | 437.5 [437-448] | 101.8 | 123.2 |
| sidebar | 1 | 2,327 [2,300-2,333] | 8,000 | 0.291x | 6,196 | **0.375x** | 287 | **8.118x** | 529.3 [528-533] | 181.0 | 149.0 |
| search | 16 | 7,510 [7,509-7,594] | 34,070 | 0.220x | 26,606 | **0.282x** | 408 | **18.411x** | 454.5 [452-455] | 107.3 | 123.9 |
| search | 1 | 2,318 [2,312-2,335] | 6,055 | 0.383x | 5,646 | **0.411x** | 193 | **12.029x** | 524.6 [523-528] | 315.1 | 158.3 |
| post a message | 16 | 2,049 [2,011-2,074] | 8,905 | 0.230x | 7,174 | **0.286x** | 330 | **6.207x** | 1,468.3 [1,449-1,496] | 353.0 | 385.6 |
| post a message | 1 | 1,122 [1,117-1,132] | 3,052 | 0.368x | 2,779 | **0.404x** | 167 | **6.724x** | 1,624.7 [1,623-1,644] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
