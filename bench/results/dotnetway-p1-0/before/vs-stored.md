theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-0/before against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 34,626 | 40,422 | 0.857x | 28,623 | **1.210x** | 239 | **144.695x** | 99.8 | 93.6 | 122.4 |
| room page | 1 | 7,712 | 7,617 | 1.012x | 6,481 | **1.190x** | 107 | **72.003x** | 212.9 | 208.7 | 142.1 |
| messages page | 16 | 35,998 | 37,836 | 0.951x | 30,063 | **1.197x** | 381 | **94.583x** | 93.3 | 102.2 | 114.2 |
| messages page | 1 | 7,969 | 8,312 | 0.959x | 6,822 | **1.168x** | 218 | **36.571x** | 205.0 | 199.3 | 139.2 |
| sidebar | 16 | 36,067 | 37,555 | 0.960x | 28,238 | **1.277x** | 599 | **60.253x** | 98.5 | 101.8 | 123.2 |
| sidebar | 1 | 7,485 | 8,000 | 0.936x | 6,196 | **1.208x** | 287 | **26.118x** | 192.4 | 181.0 | 149.0 |
| search | 16 | 32,982 | 34,070 | 0.968x | 26,606 | **1.240x** | 408 | **80.858x** | 110.5 | 107.3 | 123.9 |
| search | 1 | 5,836 | 6,055 | 0.964x | 5,646 | **1.034x** | 193 | **30.288x** | 321.3 | 315.1 | 158.3 |
| post a message | 16 | 5,202 | 8,905 | 0.584x | 7,174 | **0.725x** | 330 | **15.758x** | 378.6 | 353.0 | 385.6 |
| post a message | 1 | 2,837 | 3,052 | 0.930x | 2,779 | **1.021x** | 167 | **16.996x** | 664.1 | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  note room_show c=16: warm-up did not settle (reached the maximum)
  note room_show c=16: host probe 1.101 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
