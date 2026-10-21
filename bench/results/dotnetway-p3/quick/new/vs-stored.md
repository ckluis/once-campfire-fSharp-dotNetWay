theDotNetWay in bench/results/dotnetway-p3/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,082 [5,607-8,332] | 40,422 | 0.200x | 28,623 | **0.282x** | 239 | **33.773x** | 404.9 [396-638] | 93.6 | 122.4 |
| room page | 1 | 2,690 [2,355-2,776] | 7,617 | 0.353x | 6,481 | **0.415x** | 107 | **25.115x** | 468.6 [458-522] | 208.7 | 142.1 |
| messages page | 16 | 7,526 [5,032-7,617] | 37,836 | 0.199x | 30,063 | **0.250x** | 381 | **19.774x** | 441.2 [439-715] | 102.2 | 114.2 |
| messages page | 1 | 2,361 [2,063-2,398] | 8,312 | 0.284x | 6,822 | **0.346x** | 218 | **10.836x** | 518.9 [518-582] | 199.3 | 139.2 |
| sidebar | 16 | 7,634 [5,910-7,725] | 37,555 | 0.203x | 28,238 | **0.270x** | 599 | **12.753x** | 440.8 [438-602] | 101.8 | 123.2 |
| sidebar | 1 | 2,289 [2,094-2,326] | 8,000 | 0.286x | 6,196 | **0.369x** | 287 | **7.988x** | 536.5 [532-575] | 181.0 | 149.0 |
| search | 16 | 7,545 [7,541-7,644] | 34,070 | 0.221x | 26,606 | **0.284x** | 408 | **18.497x** | 454.6 [450-456] | 107.3 | 123.9 |
| search | 1 | 2,315 [2,309-2,344] | 6,055 | 0.382x | 5,646 | **0.410x** | 193 | **12.014x** | 528.1 [519-530] | 315.1 | 158.3 |
| post a message | 16 | 2,046 [2,028-2,047] | 8,905 | 0.230x | 7,174 | **0.285x** | 330 | **6.199x** | 1,476.0 [1,465-1,477] | 353.0 | 385.6 |
| post a message | 1 | 1,116 [1,104-1,121] | 3,052 | 0.366x | 2,779 | **0.401x** | 167 | **6.685x** | 1,643.0 [1,630-1,643] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
