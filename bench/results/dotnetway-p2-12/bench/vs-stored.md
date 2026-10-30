theDotNetWay in bench/results/dotnetway-p2-12/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,210 [5,105-5,289] | 40,422 | 0.129x | 28,623 | **0.182x** | 239 | **21.770x** | 663.1 [647-679] | 93.6 | 122.4 |
| room page | 1 | 1,553 [1,532-1,656] | 7,617 | 0.204x | 6,481 | **0.240x** | 107 | **14.503x** | 744.8 [710-750] | 208.7 | 142.1 |
| messages page | 16 | 4,872 [4,824-5,041] | 37,836 | 0.129x | 30,063 | **0.162x** | 381 | **12.802x** | 713.6 [691-721] | 102.2 | 114.2 |
| messages page | 1 | 1,412 [1,405-1,470] | 8,312 | 0.170x | 6,822 | **0.207x** | 218 | **6.482x** | 808.3 [780-810] | 199.3 | 139.2 |
| sidebar | 16 | 5,234 [4,900-5,527] | 37,555 | 0.139x | 28,238 | **0.185x** | 599 | **8.744x** | 663.4 [629-707] | 101.8 | 123.2 |
| sidebar | 1 | 1,493 [1,493-1,565] | 8,000 | 0.187x | 6,196 | **0.241x** | 287 | **5.209x** | 765.6 [737-768] | 181.0 | 149.0 |
| search | 16 | 4,784 [4,694-4,913] | 34,070 | 0.140x | 26,606 | **0.180x** | 408 | **11.728x** | 733.3 [718-749] | 107.3 | 123.9 |
| search | 1 | 1,332 [1,313-1,379] | 6,055 | 0.220x | 5,646 | **0.236x** | 193 | **6.914x** | 850.8 [823-859] | 315.1 | 158.3 |
| post a message | 16 | 1,820 [1,758-1,864] | 8,905 | 0.204x | 7,174 | **0.254x** | 330 | **5.513x** | 1,706.6 [1,669-1,773] | 353.0 | 385.6 |
| post a message | 1 | 897 [873-902] | 3,052 | 0.294x | 2,779 | **0.323x** | 167 | **5.376x** | 1,930.3 [1,900-1,978] | 629.1 | 403.7 |

Checks:
  note sidebar c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
