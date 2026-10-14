theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p0/quick-control against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 16,829 | 40,422 | 0.416x | 28,623 | **0.588x** | 239 | **70.327x** | 189.8 | 93.6 | 122.4 |
| room page | 1 | 5,753 | 7,617 | 0.755x | 6,481 | **0.888x** | 107 | **53.719x** | 233.8 | 208.7 | 142.1 |
| messages page | 16 | 16,916 | 37,836 | 0.447x | 30,063 | **0.563x** | 381 | **44.445x** | 185.0 | 102.2 | 114.2 |
| messages page | 1 | 4,593 | 8,312 | 0.553x | 6,822 | **0.673x** | 218 | **21.078x** | 258.1 | 199.3 | 139.2 |
| sidebar | 16 | 24,456 | 37,555 | 0.651x | 28,238 | **0.866x** | 599 | **40.856x** | 143.4 | 101.8 | 123.2 |
| sidebar | 1 | 5,683 | 8,000 | 0.710x | 6,196 | **0.917x** | 287 | **19.828x** | 241.1 | 181.0 | 149.0 |
| search | 16 | 27,702 | 34,070 | 0.813x | 26,606 | **1.041x** | 408 | **67.914x** | 122.2 | 107.3 | 123.9 |
| search | 1 | 4,842 | 6,055 | 0.800x | 5,646 | **0.858x** | 193 | **25.126x** | 328.3 | 315.1 | 158.3 |
| post a message | 16 | 3,437 | 8,905 | 0.386x | 7,174 | **0.479x** | 330 | **10.411x** | 401.9 | 353.0 | 385.6 |
| post a message | 1 | 2,077 | 3,052 | 0.680x | 2,779 | **0.747x** | 167 | **12.442x** | 723.3 | 629.1 | 403.7 |

Checks:
  note messages_page c=1: host probe 1.115 after the window (noisy host)
  note messages_page c=16: warm-up did not settle (reached the maximum)
  note messages_page c=16: host probe 1.272 after the window (noisy host)
  note post_message c=1: warm-up did not settle (reached the maximum)
  note post_message c=1: host probe 1.107 after the window (noisy host)
  note post_message c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: host probe 1.119 after the window (noisy host)
  note room_show c=1: host probe 1.121 after the window (noisy host)
  note room_show c=16: warm-up did not settle (reached the maximum)
  note room_show c=16: host probe 1.104 after the window (noisy host)
  note search c=1: host probe 1.131 after the window (noisy host)
  note search c=16: host probe 1.139 after the window (noisy host)
  note sidebar c=16: host probe 1.111 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
