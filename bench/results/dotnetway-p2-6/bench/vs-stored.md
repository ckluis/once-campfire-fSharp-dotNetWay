theDotNetWay in bench/results/dotnetway-p2-6/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,484 [5,483-5,511] | 40,422 | 0.136x | 28,623 | **0.192x** | 239 | **22.916x** | 630.0 [627-632] | 93.6 | 122.4 |
| room page | 1 | 1,649 [1,624-1,655] | 7,617 | 0.216x | 6,481 | **0.254x** | 107 | **15.396x** | 704.1 [701-706] | 208.7 | 142.1 |
| messages page | 16 | 5,167 [5,099-5,173] | 37,836 | 0.137x | 30,063 | **0.172x** | 381 | **13.575x** | 671.6 [671-682] | 102.2 | 114.2 |
| messages page | 1 | 1,495 [1,489-1,497] | 8,312 | 0.180x | 6,822 | **0.219x** | 218 | **6.860x** | 767.5 [767-770] | 199.3 | 139.2 |
| sidebar | 16 | 5,850 [5,812-5,879] | 37,555 | 0.156x | 28,238 | **0.207x** | 599 | **9.773x** | 590.4 [589-594] | 101.8 | 123.2 |
| sidebar | 1 | 1,666 [1,664-1,674] | 8,000 | 0.208x | 6,196 | **0.269x** | 287 | **5.814x** | 697.7 [697-700] | 181.0 | 149.0 |
| search | 16 | 5,015 [4,994-5,045] | 34,070 | 0.147x | 26,606 | **0.188x** | 408 | **12.295x** | 701.8 [699-705] | 107.3 | 123.9 |
| search | 1 | 1,400 [1,398-1,412] | 6,055 | 0.231x | 5,646 | **0.248x** | 193 | **7.266x** | 810.7 [808-814] | 315.1 | 158.3 |
| post a message | 16 | 1,836 [1,666-1,848] | 8,905 | 0.206x | 7,174 | **0.256x** | 330 | **5.560x** | 1,672.0 [1,662-1,866] | 353.0 | 385.6 |
| post a message | 1 | 909 [901-915] | 3,052 | 0.298x | 2,779 | **0.327x** | 167 | **5.448x** | 1,924.5 [1,922-1,925] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
