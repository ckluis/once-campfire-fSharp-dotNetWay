theDotNetWay in bench/results/dotnetway-p2-5/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,393 [4,919-5,428] | 40,422 | 0.133x | 28,623 | **0.188x** | 239 | **22.537x** | 639.8 [635-702] | 93.6 | 122.4 |
| room page | 1 | 1,612 [1,589-1,628] | 7,617 | 0.212x | 6,481 | **0.249x** | 107 | **15.055x** | 718.9 [713-722] | 208.7 | 142.1 |
| messages page | 16 | 5,130 [5,076-5,141] | 37,836 | 0.136x | 30,063 | **0.171x** | 381 | **13.478x** | 675.0 [674-681] | 102.2 | 114.2 |
| messages page | 1 | 1,466 [1,458-1,472] | 8,312 | 0.176x | 6,822 | **0.215x** | 218 | **6.726x** | 777.8 [778-782] | 199.3 | 139.2 |
| sidebar | 16 | 5,734 [5,693-5,782] | 37,555 | 0.153x | 28,238 | **0.203x** | 599 | **9.579x** | 599.3 [595-600] | 101.8 | 123.2 |
| sidebar | 1 | 1,641 [1,640-1,656] | 8,000 | 0.205x | 6,196 | **0.265x** | 287 | **5.726x** | 706.1 [702-708] | 181.0 | 149.0 |
| search | 16 | 4,955 [4,935-4,988] | 34,070 | 0.145x | 26,606 | **0.186x** | 408 | **12.147x** | 708.6 [699-709] | 107.3 | 123.9 |
| search | 1 | 1,382 [1,378-1,383] | 6,055 | 0.228x | 5,646 | **0.245x** | 193 | **7.170x** | 821.3 [819-824] | 315.1 | 158.3 |
| post a message | 16 | 1,822 [1,677-1,839] | 8,905 | 0.205x | 7,174 | **0.254x** | 330 | **5.519x** | 1,686.5 [1,671-1,862] | 353.0 | 385.6 |
| post a message | 1 | 891 [886-905] | 3,052 | 0.292x | 2,779 | **0.321x** | 167 | **5.339x** | 1,935.3 [1,932-1,952] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
