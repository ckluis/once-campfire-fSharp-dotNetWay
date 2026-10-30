theDotNetWay in bench/results/dotnetway-4-5/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,287 [7,183-7,468] | 40,422 | 0.180x | 28,623 | **0.255x** | 239 | **30.451x** | 446.4 [436-448] | 93.6 | 122.4 |
| room page | 1 | 2,347 [2,327-2,418] | 7,617 | 0.308x | 6,481 | **0.362x** | 107 | **21.910x** | 522.2 [510-530] | 208.7 | 142.1 |
| post a message | 16 | 2,013 [1,972-2,055] | 8,905 | 0.226x | 7,174 | **0.281x** | 330 | **6.099x** | 1,473.1 [1,459-1,508] | 353.0 | 385.6 |
| post a message | 1 | 1,140 [1,122-1,148] | 3,052 | 0.374x | 2,779 | **0.410x** | 167 | **6.831x** | 1,642.8 [1,634-1,651] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  FAIL decoded bytes room_show: 376936 vs Rust 416122
  FAIL decoded bytes room_show: 376936 vs Rust 416122
  FAIL decoded bytes room_show: 376936 vs Rust 416122
