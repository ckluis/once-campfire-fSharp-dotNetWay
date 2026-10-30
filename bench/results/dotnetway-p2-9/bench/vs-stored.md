theDotNetWay in bench/results/dotnetway-p2-9/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,413 [4,946-5,462] | 40,422 | 0.134x | 28,623 | **0.189x** | 239 | **22.621x** | 637.2 [632-709] | 93.6 | 122.4 |
| room page | 1 | 1,647 [1,585-1,684] | 7,617 | 0.216x | 6,481 | **0.254x** | 107 | **15.381x** | 701.0 [690-726] | 208.7 | 142.1 |
| messages page | 16 | 5,099 [4,626-5,132] | 37,836 | 0.135x | 30,063 | **0.170x** | 381 | **13.397x** | 679.4 [674-758] | 102.2 | 114.2 |
| messages page | 1 | 1,489 [1,445-1,524] | 8,312 | 0.179x | 6,822 | **0.218x** | 218 | **6.834x** | 765.7 [754-790] | 199.3 | 139.2 |
| sidebar | 16 | 5,469 [5,039-5,492] | 37,555 | 0.146x | 28,238 | **0.194x** | 599 | **9.137x** | 635.1 [631-697] | 101.8 | 123.2 |
| sidebar | 1 | 1,556 [1,501-1,597] | 8,000 | 0.195x | 6,196 | **0.251x** | 287 | **5.431x** | 736.1 [724-766] | 181.0 | 149.0 |
| search | 16 | 4,917 [4,700-4,983] | 34,070 | 0.144x | 26,606 | **0.185x** | 408 | **12.054x** | 715.1 [703-753] | 107.3 | 123.9 |
| search | 1 | 1,392 [1,367-1,421] | 6,055 | 0.230x | 5,646 | **0.246x** | 193 | **7.222x** | 813.6 [801-830] | 315.1 | 158.3 |
| post a message | 16 | 1,854 [1,814-1,878] | 8,905 | 0.208x | 7,174 | **0.258x** | 330 | **5.618x** | 1,653.5 [1,651-1,719] | 353.0 | 385.6 |
| post a message | 1 | 916 [904-930] | 3,052 | 0.300x | 2,779 | **0.330x** | 167 | **5.491x** | 1,869.3 [1,864-1,925] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
