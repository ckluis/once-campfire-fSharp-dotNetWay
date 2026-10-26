theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-7-4b/vs-p7pre against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,896 [6,385-7,210] | 40,422 | 0.171x | 28,623 | **0.241x** | 239 | **28.819x** | 476.1 [462-524] | 93.6 | 122.4 |
| room page | 1 | 1,877 [1,713-2,149] | 7,617 | 0.246x | 6,481 | **0.290x** | 107 | **17.522x** | 644.0 [575-678] | 208.7 | 142.1 |
| messages page | 16 | 6,813 [6,360-6,842] | 37,836 | 0.180x | 30,063 | **0.227x** | 381 | **17.900x** | 502.8 [496-531] | 102.2 | 114.2 |
| messages page | 1 | 1,933 [1,844-1,970] | 8,312 | 0.233x | 6,822 | **0.283x** | 218 | **8.870x** | 626.7 [615-651] | 199.3 | 139.2 |
| sidebar | 16 | 6,433 [4,872-6,903] | 37,555 | 0.171x | 28,238 | **0.228x** | 599 | **10.747x** | 520.9 [482-659] | 101.8 | 123.2 |
| sidebar | 1 | 2,055 [1,895-2,097] | 8,000 | 0.257x | 6,196 | **0.332x** | 287 | **7.169x** | 594.8 [587-637] | 181.0 | 149.0 |
| search | 16 | 6,748 [6,479-6,788] | 34,070 | 0.198x | 26,606 | **0.254x** | 408 | **16.542x** | 506.4 [505-536] | 107.3 | 123.9 |
| search | 1 | 1,819 [1,553-1,837] | 6,055 | 0.300x | 5,646 | **0.322x** | 193 | **9.437x** | 657.5 [655-753] | 315.1 | 158.3 |
| post a message | 16 | 1,837 [806-2,055] | 8,905 | 0.206x | 7,174 | **0.256x** | 330 | **5.566x** | 1,746.6 [1,560-3,744] | 353.0 | 385.6 |
| post a message | 1 | 1,001 [910-1,006] | 3,052 | 0.328x | 2,779 | **0.360x** | 167 | **5.996x** | 1,792.8 [1,782-1,955] | 629.1 | 403.7 |

Checks:
  note avatar c=16: warm-up did not settle (reached the maximum)
  note avatar c=16: host probe 1.125 after the window (noisy host)
  note avatar c=16: warm-up did not settle (reached the maximum)
  note post_message c=1: warm-up did not settle (reached the maximum)
  note post_message c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: host probe 1.103 after the window (noisy host)
  note search c=16: host probe 1.108 after the window (noisy host)
  note sidebar c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 36 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
