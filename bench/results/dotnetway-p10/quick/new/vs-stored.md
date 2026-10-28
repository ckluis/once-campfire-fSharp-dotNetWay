theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p10/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 33,418 [32,628-33,633] | 40,422 | 0.827x | 28,623 | **1.168x** | 239 | **139.650x** | 96.7 [96-98] | 93.6 | 122.4 |
| room page | 1 | 6,438 [6,096-6,460] | 7,617 | 0.845x | 6,481 | **0.993x** | 107 | **60.117x** | 268.7 [265-275] | 208.7 | 142.1 |
| messages page | 16 | 31,425 [31,315-32,222] | 37,836 | 0.831x | 30,063 | **1.045x** | 381 | **82.566x** | 101.9 [99-102] | 102.2 | 114.2 |
| messages page | 1 | 6,268 [5,961-6,372] | 8,312 | 0.754x | 6,822 | **0.919x** | 218 | **28.764x** | 270.8 [270-279] | 199.3 | 139.2 |
| sidebar | 16 | 39,313 [39,100-39,664] | 37,555 | 1.047x | 28,238 | **1.392x** | 599 | **65.675x** | 78.6 [78-80] | 101.8 | 123.2 |
| sidebar | 1 | 7,265 [6,715-7,350] | 8,000 | 0.908x | 6,196 | **1.172x** | 287 | **25.348x** | 247.8 [248-262] | 181.0 | 149.0 |
| search | 16 | 37,663 [35,615-40,427] | 34,070 | 1.105x | 26,606 | **1.416x** | 408 | **92.333x** | 82.1 [77-88] | 107.3 | 123.9 |
| search | 1 | 7,342 [7,334-7,469] | 6,055 | 1.212x | 5,646 | **1.300x** | 193 | **38.099x** | 250.3 [248-251] | 315.1 | 158.3 |
| post a message | 16 | 2,464 [2,456-2,511] | 8,905 | 0.277x | 7,174 | **0.344x** | 330 | **7.466x** | 1,313.6 [1,291-1,316] | 353.0 | 385.6 |
| post a message | 1 | 1,247 [1,222-1,287] | 3,052 | 0.409x | 2,779 | **0.449x** | 167 | **7.471x** | 1,524.1 [1,510-1,535] | 629.1 | 403.7 |

Checks:
  note up c=1: warm-up did not settle (reached the maximum)
  note up c=1: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
