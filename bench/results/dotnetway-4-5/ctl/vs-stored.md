theDotNetWay in bench/results/dotnetway-4-5/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,590 [6,383-7,642] | 40,422 | 0.188x | 28,623 | **0.265x** | 239 | **31.719x** | 434.8 [431-516] | 93.6 | 122.4 |
| room page | 1 | 2,399 [1,924-2,492] | 7,617 | 0.315x | 6,481 | **0.370x** | 107 | **22.403x** | 513.1 [498-600] | 208.7 | 142.1 |
| post a message | 16 | 1,982 [1,943-2,012] | 8,905 | 0.223x | 7,174 | **0.276x** | 330 | **6.004x** | 1,515.2 [1,460-1,552] | 353.0 | 385.6 |
| post a message | 1 | 1,119 [1,094-1,136] | 3,052 | 0.367x | 2,779 | **0.403x** | 167 | **6.706x** | 1,658.5 [1,650-1,692] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  FAIL decoded bytes room_show: 378036 vs Rust 416122
  FAIL decoded bytes room_show: 378036 vs Rust 416122
  FAIL decoded bytes room_show: 378036 vs Rust 416122
