theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-0/after against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 35,116 | 40,422 | 0.869x | 28,623 | **1.227x** | 239 | **146.746x** | 97.1 | 93.6 | 122.4 |
| room page | 1 | 7,742 | 7,617 | 1.016x | 6,481 | **1.195x** | 107 | **72.288x** | 220.0 | 208.7 | 142.1 |
| messages page | 16 | 37,688 | 37,836 | 0.996x | 30,063 | **1.254x** | 381 | **99.021x** | 90.2 | 102.2 | 114.2 |
| messages page | 1 | 8,402 | 8,312 | 1.011x | 6,822 | **1.232x** | 218 | **38.560x** | 206.9 | 199.3 | 139.2 |
| sidebar | 16 | 35,317 | 37,555 | 0.940x | 28,238 | **1.251x** | 599 | **58.999x** | 98.9 | 101.8 | 123.2 |
| sidebar | 1 | 4,686 | 8,000 | 0.586x | 6,196 | **0.756x** | 287 | **16.349x** | 261.2 | 181.0 | 149.0 |
| search | 16 | 25,217 | 34,070 | 0.740x | 26,606 | **0.948x** | 408 | **61.821x** | 134.1 | 107.3 | 123.9 |
| search | 1 | 5,737 | 6,055 | 0.947x | 5,646 | **1.016x** | 193 | **29.772x** | 324.6 | 315.1 | 158.3 |
| post a message | 16 | 4,549 | 8,905 | 0.511x | 7,174 | **0.634x** | 330 | **13.780x** | 436.5 | 353.0 | 385.6 |
| post a message | 1 | 2,894 | 3,052 | 0.948x | 2,779 | **1.041x** | 167 | **17.340x** | 691.1 | 629.1 | 403.7 |

Checks:
  note post_message c=16: warm-up did not settle (reached the maximum)
  note room_show c=16: warm-up did not settle (reached the maximum)
  note search c=1: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
