theDotNetWay in bench/results/dotnetway-p3/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,565 [5,495-5,729] | 40,422 | 0.138x | 28,623 | **0.194x** | 239 | **23.256x** | 622.3 [619-632] | 93.6 | 122.4 |
| room page | 1 | 1,703 [1,696-1,710] | 7,617 | 0.224x | 6,481 | **0.263x** | 107 | **15.900x** | 685.3 [684-690] | 208.7 | 142.1 |
| messages page | 16 | 5,189 [5,165-5,354] | 37,836 | 0.137x | 30,063 | **0.173x** | 381 | **13.634x** | 672.0 [665-673] | 102.2 | 114.2 |
| messages page | 1 | 1,527 [1,521-1,543] | 8,312 | 0.184x | 6,822 | **0.224x** | 218 | **7.010x** | 752.3 [746-756] | 199.3 | 139.2 |
| sidebar | 16 | 5,623 [5,620-5,807] | 37,555 | 0.150x | 28,238 | **0.199x** | 599 | **9.394x** | 613.9 [610-618] | 101.8 | 123.2 |
| sidebar | 1 | 1,620 [1,574-1,641] | 8,000 | 0.202x | 6,196 | **0.261x** | 287 | **5.652x** | 716.1 [710-735] | 181.0 | 149.0 |
| search | 16 | 5,098 [5,033-5,113] | 34,070 | 0.150x | 26,606 | **0.192x** | 408 | **12.499x** | 695.0 [689-703] | 107.3 | 123.9 |
| search | 1 | 1,498 [1,481-1,510] | 6,055 | 0.247x | 5,646 | **0.265x** | 193 | **7.774x** | 764.8 [762-773] | 315.1 | 158.3 |
| post a message | 16 | 1,880 [1,872-1,890] | 8,905 | 0.211x | 7,174 | **0.262x** | 330 | **5.697x** | 1,644.3 [1,625-1,647] | 353.0 | 385.6 |
| post a message | 1 | 950 [935-952] | 3,052 | 0.311x | 2,779 | **0.342x** | 167 | **5.692x** | 1,849.9 [1,840-1,871] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
