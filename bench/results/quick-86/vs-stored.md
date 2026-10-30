theDotNetWay in bench/results/quick-86 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,945 [6,931-6,960] | 40,422 | 0.172x | 28,623 | **0.243x** | 239 | **29.024x** | 483.1 [482-485] | 93.6 | 122.4 |
| room page | 1 | 2,085 [2,066-2,104] | 7,617 | 0.274x | 6,481 | **0.322x** | 107 | **19.467x** | 588.0 [584-592] | 208.7 | 142.1 |
| post a message | 16 | 2,081 [2,000-2,162] | 8,905 | 0.234x | 7,174 | **0.290x** | 330 | **6.305x** | 1,559.2 [1,500-1,618] | 353.0 | 385.6 |
| post a message | 1 | 1,029 [988-1,070] | 3,052 | 0.337x | 2,779 | **0.370x** | 167 | **6.166x** | 1,835.6 [1,786-1,886] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 20 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
