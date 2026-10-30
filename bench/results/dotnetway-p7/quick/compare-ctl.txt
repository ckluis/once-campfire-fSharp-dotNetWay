theDotNetWay in bench/results/dotnetway-p7/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,399 [7,069-7,427] | 40,422 | 0.183x | 28,623 | **0.259x** | 239 | **30.921x** | 437.0 [434-464] | 93.6 | 122.4 |
| room page | 1 | 2,519 [2,504-2,553] | 7,617 | 0.331x | 6,481 | **0.389x** | 107 | **23.522x** | 495.6 [488-497] | 208.7 | 142.1 |
| messages page | 16 | 7,339 [6,916-7,400] | 37,836 | 0.194x | 30,063 | **0.244x** | 381 | **19.283x** | 453.3 [448-487] | 102.2 | 114.2 |
| messages page | 1 | 2,339 [2,299-2,352] | 8,312 | 0.281x | 6,822 | **0.343x** | 218 | **10.734x** | 524.3 [520-533] | 199.3 | 139.2 |
| sidebar | 16 | 7,696 [7,671-7,814] | 37,555 | 0.205x | 28,238 | **0.273x** | 599 | **12.856x** | 425.5 [420-429] | 101.8 | 123.2 |
| sidebar | 1 | 2,581 [2,543-2,601] | 8,000 | 0.323x | 6,196 | **0.417x** | 287 | **9.006x** | 479.1 [477-480] | 181.0 | 149.0 |
| search | 16 | 7,082 [7,002-7,084] | 34,070 | 0.208x | 26,606 | **0.266x** | 408 | **17.361x** | 478.3 [475-482] | 107.3 | 123.9 |
| search | 1 | 2,157 [2,142-2,168] | 6,055 | 0.356x | 5,646 | **0.382x** | 193 | **11.192x** | 557.0 [557-566] | 315.1 | 158.3 |
| post a message | 16 | 2,013 [1,974-2,176] | 8,905 | 0.226x | 7,174 | **0.281x** | 330 | **6.099x** | 1,484.7 [1,459-1,504] | 353.0 | 385.6 |
| post a message | 1 | 1,121 [1,116-1,126] | 3,052 | 0.367x | 2,779 | **0.403x** | 167 | **6.715x** | 1,648.0 [1,647-1,650] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
