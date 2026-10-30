theDotNetWay in bench/results/dotnetway-3-5/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,152 | 40,422 | 0.202x | 28,623 | **0.285x** | 239 | **34.066x** | 406.4 | 93.6 | 122.4 |
| room page | 1 | 2,739 | 7,617 | 0.360x | 6,481 | **0.423x** | 107 | **25.578x** | 460.8 | 208.7 | 142.1 |
| messages page | 16 | 7,483 | 37,836 | 0.198x | 30,063 | **0.249x** | 381 | **19.661x** | 446.2 | 102.2 | 114.2 |
| messages page | 1 | 2,406 | 8,312 | 0.289x | 6,822 | **0.353x** | 218 | **11.042x** | 509.7 | 199.3 | 139.2 |
| sidebar | 16 | 7,545 | 37,555 | 0.201x | 28,238 | **0.267x** | 599 | **12.604x** | 447.1 | 101.8 | 123.2 |
| sidebar | 1 | 2,315 | 8,000 | 0.289x | 6,196 | **0.374x** | 287 | **8.076x** | 527.9 | 181.0 | 149.0 |
| search | 16 | 7,525 | 34,070 | 0.221x | 26,606 | **0.283x** | 408 | **18.448x** | 457.2 | 107.3 | 123.9 |
| search | 1 | 2,321 | 6,055 | 0.383x | 5,646 | **0.411x** | 193 | **12.045x** | 525.7 | 315.1 | 158.3 |
| post a message | 16 | 2,046 | 8,905 | 0.230x | 7,174 | **0.285x** | 330 | **6.199x** | 1,472.7 | 353.0 | 385.6 |
| post a message | 1 | 1,100 | 3,052 | 0.360x | 2,779 | **0.396x** | 167 | **6.590x** | 1,638.3 | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
