theDotNetWay in bench/results/dotnetway-p8/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,219 [6,897-7,262] | 40,422 | 0.179x | 28,623 | **0.252x** | 239 | **30.167x** | 457.1 [456-480] | 93.6 | 122.4 |
| room page | 1 | 2,250 [2,144-2,260] | 7,617 | 0.295x | 6,481 | **0.347x** | 107 | **21.007x** | 567.8 [563-586] | 208.7 | 142.1 |
| messages page | 16 | 7,022 [6,743-7,155] | 37,836 | 0.186x | 30,063 | **0.234x** | 381 | **18.449x** | 479.8 [471-503] | 102.2 | 114.2 |
| messages page | 1 | 2,073 [2,002-2,078] | 8,312 | 0.249x | 6,822 | **0.304x** | 218 | **9.514x** | 596.9 [596-615] | 199.3 | 139.2 |
| sidebar | 16 | 7,357 [7,326-7,414] | 37,555 | 0.196x | 28,238 | **0.261x** | 599 | **12.291x** | 452.8 [452-457] | 101.8 | 123.2 |
| sidebar | 1 | 2,222 [2,130-2,231] | 8,000 | 0.278x | 6,196 | **0.359x** | 287 | **7.751x** | 564.0 [562-596] | 181.0 | 149.0 |
| search | 16 | 6,930 [6,829-6,932] | 34,070 | 0.203x | 26,606 | **0.260x** | 408 | **16.989x** | 496.7 [496-504] | 107.3 | 123.9 |
| search | 1 | 1,912 [1,828-1,918] | 6,055 | 0.316x | 5,646 | **0.339x** | 193 | **9.920x** | 637.1 [635-668] | 315.1 | 158.3 |
| post a message | 16 | 2,158 [2,139-2,169] | 8,905 | 0.242x | 7,174 | **0.301x** | 330 | **6.536x** | 1,504.7 [1,492-1,513] | 353.0 | 385.6 |
| post a message | 1 | 1,070 [1,060-1,079] | 3,052 | 0.350x | 2,779 | **0.385x** | 167 | **6.408x** | 1,732.3 [1,716-1,741] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
