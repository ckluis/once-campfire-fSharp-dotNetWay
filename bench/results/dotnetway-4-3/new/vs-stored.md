theDotNetWay in bench/results/dotnetway-4-3/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,960 [7,934-8,195] | 40,422 | 0.197x | 28,623 | **0.278x** | 239 | **33.264x** | 415.6 [411-416] | 93.6 | 122.4 |
| room page | 1 | 2,638 [2,601-2,667] | 7,617 | 0.346x | 6,481 | **0.407x** | 107 | **24.633x** | 474.9 [473-480] | 208.7 | 142.1 |
| messages page | 16 | 7,437 [7,392-7,627] | 37,836 | 0.197x | 30,063 | **0.247x** | 381 | **19.540x** | 448.4 [444-450] | 102.2 | 114.2 |
| messages page | 1 | 2,355 [2,319-2,387] | 8,312 | 0.283x | 6,822 | **0.345x** | 218 | **10.808x** | 521.2 [515-527] | 199.3 | 139.2 |
| sidebar | 16 | 7,727 [7,661-7,897] | 37,555 | 0.206x | 28,238 | **0.274x** | 599 | **12.908x** | 437.2 [432-439] | 101.8 | 123.2 |
| sidebar | 1 | 2,311 [2,299-2,338] | 8,000 | 0.289x | 6,196 | **0.373x** | 287 | **8.063x** | 533.3 [528-534] | 181.0 | 149.0 |
| search | 16 | 7,321 [7,003-7,390] | 34,070 | 0.215x | 26,606 | **0.275x** | 408 | **17.948x** | 465.9 [464-490] | 107.3 | 123.9 |
| search | 1 | 2,231 [2,221-2,267] | 6,055 | 0.368x | 5,646 | **0.395x** | 193 | **11.577x** | 543.6 [536-544] | 315.1 | 158.3 |
| post a message | 16 | 2,063 [1,984-2,074] | 8,905 | 0.232x | 7,174 | **0.288x** | 330 | **6.251x** | 1,449.3 [1,436-1,509] | 353.0 | 385.6 |
| post a message | 1 | 1,156 [1,109-1,156] | 3,052 | 0.379x | 2,779 | **0.416x** | 167 | **6.923x** | 1,633.0 [1,623-1,656] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
