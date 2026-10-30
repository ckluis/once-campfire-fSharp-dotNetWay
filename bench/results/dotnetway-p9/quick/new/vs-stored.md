theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p9/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,215 [7,161-7,244] | 40,422 | 0.178x | 28,623 | **0.252x** | 239 | **30.150x** | 453.8 [452-458] | 93.6 | 122.4 |
| room page | 1 | 2,232 [2,196-2,240] | 7,617 | 0.293x | 6,481 | **0.344x** | 107 | **20.845x** | 572.5 [561-574] | 208.7 | 142.1 |
| messages page | 16 | 7,062 [7,039-7,083] | 37,836 | 0.187x | 30,063 | **0.235x** | 381 | **18.556x** | 475.7 [473-478] | 102.2 | 114.2 |
| messages page | 1 | 2,079 [2,065-2,094] | 8,312 | 0.250x | 6,822 | **0.305x** | 218 | **9.539x** | 597.9 [593-603] | 199.3 | 139.2 |
| sidebar | 16 | 7,337 [7,336-7,348] | 37,555 | 0.195x | 28,238 | **0.260x** | 599 | **12.258x** | 451.9 [452-454] | 101.8 | 123.2 |
| sidebar | 1 | 2,175 [2,164-2,188] | 8,000 | 0.272x | 6,196 | **0.351x** | 287 | **7.588x** | 576.5 [572-579] | 181.0 | 149.0 |
| search | 16 | 6,887 [6,860-6,950] | 34,070 | 0.202x | 26,606 | **0.259x** | 408 | **16.885x** | 494.6 [493-498] | 107.3 | 123.9 |
| search | 1 | 1,980 [1,942-1,989] | 6,055 | 0.327x | 5,646 | **0.351x** | 193 | **10.277x** | 621.7 [617-622] | 315.1 | 158.3 |
| post a message | 16 | 2,150 [2,139-2,165] | 8,905 | 0.241x | 7,174 | **0.300x** | 330 | **6.515x** | 1,507.5 [1,491-1,518] | 353.0 | 385.6 |
| post a message | 1 | 1,065 [1,038-1,078] | 3,052 | 0.349x | 2,779 | **0.383x** | 167 | **6.382x** | 1,706.5 [1,693-1,729] | 629.1 | 403.7 |

Checks:
  note avatar c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
