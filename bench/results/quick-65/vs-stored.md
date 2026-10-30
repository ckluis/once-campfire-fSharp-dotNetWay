theDotNetWay in bench/results/quick-65 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,338 [6,504-7,409] | 40,422 | 0.182x | 28,623 | **0.256x** | 239 | **30.666x** | 438.8 [437-509] | 93.6 | 122.4 |
| room page | 1 | 2,466 [2,318-2,494] | 7,617 | 0.324x | 6,481 | **0.380x** | 107 | **23.021x** | 503.6 [499-529] | 208.7 | 142.1 |
| post a message | 16 | 2,015 [1,950-2,032] | 8,905 | 0.226x | 7,174 | **0.281x** | 330 | **6.104x** | 1,490.6 [1,483-1,554] | 353.0 | 385.6 |
| post a message | 1 | 1,104 [1,076-1,114] | 3,052 | 0.362x | 2,779 | **0.397x** | 167 | **6.613x** | 1,664.2 [1,663-1,711] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 18 runs
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes room_show: 377518 vs expected 376570
