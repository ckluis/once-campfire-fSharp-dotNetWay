theDotNetWay in bench/results/dotnetway-4-10/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,354 [7,349-7,394] | 40,422 | 0.182x | 28,623 | **0.257x** | 239 | **30.729x** | 440.8 [437-443] | 93.6 | 122.4 |
| room page | 1 | 2,515 [2,488-2,518] | 7,617 | 0.330x | 6,481 | **0.388x** | 107 | **23.486x** | 493.5 [492-499] | 208.7 | 142.1 |
| messages page | 16 | 7,331 [7,244-7,360] | 37,836 | 0.194x | 30,063 | **0.244x** | 381 | **19.261x** | 455.3 [452-457] | 102.2 | 114.2 |
| messages page | 1 | 2,308 [2,281-2,311] | 8,312 | 0.278x | 6,822 | **0.338x** | 218 | **10.592x** | 529.9 [529-534] | 199.3 | 139.2 |
| sidebar | 16 | 7,599 [7,572-7,644] | 37,555 | 0.202x | 28,238 | **0.269x** | 599 | **12.695x** | 429.4 [428-434] | 101.8 | 123.2 |
| sidebar | 1 | 2,447 [2,432-2,461] | 8,000 | 0.306x | 6,196 | **0.395x** | 287 | **8.537x** | 503.1 [500-505] | 181.0 | 149.0 |
| search | 16 | 6,993 [6,960-7,061] | 34,070 | 0.205x | 26,606 | **0.263x** | 408 | **17.145x** | 482.6 [480-484] | 107.3 | 123.9 |
| search | 1 | 2,204 [2,181-2,206] | 6,055 | 0.364x | 5,646 | **0.390x** | 193 | **11.440x** | 548.8 [547-552] | 315.1 | 158.3 |
| post a message | 16 | 2,041 [2,019-2,052] | 8,905 | 0.229x | 7,174 | **0.284x** | 330 | **6.182x** | 1,458.5 [1,454-1,460] | 353.0 | 385.6 |
| post a message | 1 | 1,145 [1,123-1,164] | 3,052 | 0.375x | 2,779 | **0.412x** | 167 | **6.858x** | 1,637.5 [1,617-1,648] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  FAIL decoded bytes room_show: 376570 vs Rust 416122
  FAIL decoded bytes messages_page: 345884 vs Rust 383844
  FAIL decoded bytes sidebar: 29710 vs Rust 30763
  FAIL decoded bytes search: 136633 vs Rust 149623
  FAIL decoded bytes room_show: 376570 vs Rust 416122
  FAIL decoded bytes messages_page: 345884 vs Rust 383844
  FAIL decoded bytes sidebar: 29710 vs Rust 30763
  FAIL decoded bytes search: 136633 vs Rust 149623
  FAIL decoded bytes room_show: 376570 vs Rust 416122
  FAIL decoded bytes messages_page: 345884 vs Rust 383844
  FAIL decoded bytes sidebar: 29710 vs Rust 30763
  FAIL decoded bytes search: 136633 vs Rust 149623
