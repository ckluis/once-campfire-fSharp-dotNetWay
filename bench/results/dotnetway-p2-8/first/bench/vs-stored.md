theDotNetWay in bench/results/dotnetway-p2-8/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 3,633 [3,595-3,739] | 40,422 | 0.090x | 28,623 | **0.127x** | 239 | **15.181x** | 974.7 [971-991] | 93.6 | 122.4 |
| room page | 1 | 957 [949-992] | 7,617 | 0.126x | 6,481 | **0.148x** | 107 | **8.937x** | 1,832.4 [1,792-1,851] | 208.7 | 142.1 |
| messages page | 16 | 3,691 [3,658-3,822] | 37,836 | 0.098x | 30,063 | **0.123x** | 381 | **9.699x** | 960.0 [950-976] | 102.2 | 114.2 |
| messages page | 1 | 969 [967-979] | 8,312 | 0.117x | 6,822 | **0.142x** | 218 | **4.447x** | 1,652.3 [1,639-1,660] | 199.3 | 139.2 |
| sidebar | 16 | 5,219 [5,187-5,294] | 37,555 | 0.139x | 28,238 | **0.185x** | 599 | **8.718x** | 665.2 [664-668] | 101.8 | 123.2 |
| sidebar | 1 | 1,488 [1,485-1,496] | 8,000 | 0.186x | 6,196 | **0.240x** | 287 | **5.193x** | 768.1 [767-768] | 181.0 | 149.0 |
| search | 16 | 3,911 [3,144-4,156] | 34,070 | 0.115x | 26,606 | **0.147x** | 408 | **9.588x** | 903.7 [859-1,148] | 107.3 | 123.9 |
| search | 1 | 1,070 [1,060-1,080] | 6,055 | 0.177x | 5,646 | **0.189x** | 193 | **5.551x** | 1,286.2 [1,286-1,297] | 315.1 | 158.3 |
| post a message | 16 | 1,753 [1,753-1,787] | 8,905 | 0.197x | 7,174 | **0.244x** | 330 | **5.311x** | 1,761.4 [1,732-1,770] | 353.0 | 385.6 |
| post a message | 1 | 872 [736-897] | 3,052 | 0.286x | 2,779 | **0.314x** | 167 | **5.226x** | 1,951.2 [1,936-2,334] | 629.1 | 403.7 |

Checks:
  note messages_page c=16: host probe 1.103 after the window (noisy host)
  note room_show c=16: warm-up did not settle (reached the maximum)
  note sidebar c=16: host probe 1.148 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
