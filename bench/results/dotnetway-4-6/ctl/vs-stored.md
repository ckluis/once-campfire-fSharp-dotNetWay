theDotNetWay in bench/results/dotnetway-4-6/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,698 [7,571-7,952] | 37,555 | 0.205x | 28,238 | **0.273x** | 599 | **12.861x** | 435.0 [428-444] | 101.8 | 123.2 |
| sidebar | 1 | 2,328 [2,318-2,392] | 8,000 | 0.291x | 6,196 | **0.376x** | 287 | **8.123x** | 523.7 [517-528] | 181.0 | 149.0 |
| search | 16 | 7,252 [6,906-7,387] | 34,070 | 0.213x | 26,606 | **0.273x** | 408 | **17.778x** | 471.4 [463-499] | 107.3 | 123.9 |
| search | 1 | 2,155 [2,123-2,249] | 6,055 | 0.356x | 5,646 | **0.382x** | 193 | **11.181x** | 558.3 [540-565] | 315.1 | 158.3 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  FAIL decoded bytes search: 137286 vs Rust 149623
  FAIL decoded bytes search: 137286 vs Rust 149623
  FAIL decoded bytes search: 137286 vs Rust 149623
