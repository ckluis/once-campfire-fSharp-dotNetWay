theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/quick-67 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,979 [6,966-7,208] | 40,422 | 0.173x | 28,623 | **0.244x** | 239 | **29.165x** | 472.3 [462-476] | 93.6 | 122.4 |
| room page | 1 | 2,403 [2,401-2,485] | 7,617 | 0.315x | 6,481 | **0.371x** | 107 | **22.433x** | 513.8 [497-520] | 208.7 | 142.1 |
| messages page | 16 | 6,925 [6,776-6,930] | 37,836 | 0.183x | 30,063 | **0.230x** | 381 | **18.196x** | 490.5 [490-494] | 102.2 | 114.2 |
| messages page | 1 | 2,213 [2,195-2,315] | 8,312 | 0.266x | 6,822 | **0.324x** | 218 | **10.156x** | 547.4 [524-551] | 199.3 | 139.2 |
| sidebar | 16 | 7,476 [7,203-7,984] | 37,555 | 0.199x | 28,238 | **0.265x** | 599 | **12.488x** | 450.7 [420-460] | 101.8 | 123.2 |
| sidebar | 1 | 2,338 [2,334-2,451] | 8,000 | 0.292x | 6,196 | **0.377x** | 287 | **8.158x** | 522.1 [499-523] | 181.0 | 149.0 |
| search | 16 | 6,687 [6,477-7,336] | 34,070 | 0.196x | 26,606 | **0.251x** | 408 | **16.394x** | 520.9 [470-528] | 107.3 | 123.9 |
| search | 1 | 2,026 [2,016-2,131] | 6,055 | 0.335x | 5,646 | **0.359x** | 193 | **10.515x** | 588.5 [561-591] | 315.1 | 158.3 |
| post a message | 16 | 1,927 [1,900-2,021] | 8,905 | 0.216x | 7,174 | **0.269x** | 330 | **5.837x** | 1,591.9 [1,484-1,611] | 353.0 | 385.6 |
| post a message | 1 | 1,067 [1,061-1,104] | 3,052 | 0.350x | 2,779 | **0.384x** | 167 | **6.394x** | 1,741.2 [1,662-1,742] | 629.1 | 403.7 |

Checks:
  note static_css c=1: warm-up did not settle (reached the maximum)
  note static_css c=1: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 36 runs
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes messages_page: 346364 vs expected 345884
  FAIL decoded bytes sidebar: 30144 vs expected 29710
  FAIL decoded bytes search: 137229 vs expected 136633
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes messages_page: 346364 vs expected 345884
  FAIL decoded bytes sidebar: 30144 vs expected 29710
  FAIL decoded bytes search: 137229 vs expected 136633
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes messages_page: 346364 vs expected 345884
  FAIL decoded bytes sidebar: 30144 vs expected 29710
  FAIL decoded bytes search: 137229 vs expected 136633
