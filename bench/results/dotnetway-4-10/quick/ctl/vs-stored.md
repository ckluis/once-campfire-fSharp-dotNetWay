theDotNetWay in bench/results/dotnetway-4-10/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,808 [7,507-7,843] | 40,422 | 0.193x | 28,623 | **0.273x** | 239 | **32.631x** | 429.2 [427-435] | 93.6 | 122.4 |
| room page | 1 | 2,506 [2,495-2,511] | 7,617 | 0.329x | 6,481 | **0.387x** | 107 | **23.395x** | 495.9 [495-499] | 208.7 | 142.1 |
| messages page | 16 | 7,534 [7,247-7,542] | 37,836 | 0.199x | 30,063 | **0.251x** | 381 | **19.796x** | 450.6 [450-457] | 102.2 | 114.2 |
| messages page | 1 | 2,318 [2,295-2,340] | 8,312 | 0.279x | 6,822 | **0.340x** | 218 | **10.640x** | 527.4 [523-534] | 199.3 | 139.2 |
| sidebar | 16 | 7,999 [7,815-8,089] | 37,555 | 0.213x | 28,238 | **0.283x** | 599 | **13.364x** | 423.0 [417-425] | 101.8 | 123.2 |
| sidebar | 1 | 2,445 [2,442-2,492] | 8,000 | 0.306x | 6,196 | **0.395x** | 287 | **8.532x** | 503.3 [495-504] | 181.0 | 149.0 |
| search | 16 | 7,207 [7,203-7,215] | 34,070 | 0.212x | 26,606 | **0.271x** | 408 | **17.669x** | 474.2 [473-475] | 107.3 | 123.9 |
| search | 1 | 2,205 [2,163-2,229] | 6,055 | 0.364x | 5,646 | **0.390x** | 193 | **11.441x** | 546.4 [545-559] | 315.1 | 158.3 |
| post a message | 16 | 2,060 [2,060-2,063] | 8,905 | 0.231x | 7,174 | **0.287x** | 330 | **6.241x** | 1,455.6 [1,448-1,457] | 353.0 | 385.6 |
| post a message | 1 | 1,160 [1,155-1,164] | 3,052 | 0.380x | 2,779 | **0.417x** | 167 | **6.948x** | 1,634.2 [1,614-1,635] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  FAIL decoded bytes room_show: 376810 vs Rust 416122
  FAIL decoded bytes messages_page: 345884 vs Rust 383844
  FAIL decoded bytes sidebar: 29950 vs Rust 30763
  FAIL decoded bytes search: 136873 vs Rust 149623
  FAIL decoded bytes room_show: 376810 vs Rust 416122
  FAIL decoded bytes messages_page: 345884 vs Rust 383844
  FAIL decoded bytes sidebar: 29950 vs Rust 30763
  FAIL decoded bytes search: 136873 vs Rust 149623
  FAIL decoded bytes room_show: 376810 vs Rust 416122
  FAIL decoded bytes messages_page: 345884 vs Rust 383844
  FAIL decoded bytes sidebar: 29950 vs Rust 30763
  FAIL decoded bytes search: 136873 vs Rust 149623
