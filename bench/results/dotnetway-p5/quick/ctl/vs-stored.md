theDotNetWay in bench/results/dotnetway-p5/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,472 [7,346-7,632] | 40,422 | 0.185x | 28,623 | **0.261x** | 239 | **31.222x** | 439.6 [437-448] | 93.6 | 122.4 |
| room page | 1 | 2,453 [2,396-2,460] | 7,617 | 0.322x | 6,481 | **0.378x** | 107 | **22.902x** | 505.3 [504-516] | 208.7 | 142.1 |
| messages page | 16 | 7,265 [7,242-7,520] | 37,836 | 0.192x | 30,063 | **0.242x** | 381 | **19.089x** | 456.5 [453-468] | 102.2 | 114.2 |
| messages page | 1 | 2,264 [2,161-2,303] | 8,312 | 0.272x | 6,822 | **0.332x** | 218 | **10.389x** | 538.7 [530-560] | 199.3 | 139.2 |
| sidebar | 16 | 7,554 [7,516-7,782] | 37,555 | 0.201x | 28,238 | **0.268x** | 599 | **12.620x** | 433.7 [431-444] | 101.8 | 123.2 |
| sidebar | 1 | 2,393 [2,337-2,435] | 8,000 | 0.299x | 6,196 | **0.386x** | 287 | **8.351x** | 513.2 [505-525] | 181.0 | 149.0 |
| search | 16 | 7,029 [6,853-7,030] | 34,070 | 0.206x | 26,606 | **0.264x** | 408 | **17.231x** | 480.9 [480-492] | 107.3 | 123.9 |
| search | 1 | 2,162 [2,085-2,191] | 6,055 | 0.357x | 5,646 | **0.383x** | 193 | **11.220x** | 557.1 [550-577] | 315.1 | 158.3 |
| post a message | 16 | 2,027 [1,996-2,067] | 8,905 | 0.228x | 7,174 | **0.283x** | 330 | **6.140x** | 1,464.8 [1,454-1,512] | 353.0 | 385.6 |
| post a message | 1 | 1,144 [1,129-1,157] | 3,052 | 0.375x | 2,779 | **0.411x** | 167 | **6.851x** | 1,638.9 [1,633-1,666] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
