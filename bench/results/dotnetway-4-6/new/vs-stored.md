theDotNetWay in bench/results/dotnetway-4-6/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,270 [6,853-7,686] | 37,555 | 0.194x | 28,238 | **0.257x** | 599 | **12.144x** | 455.2 [432-478] | 101.8 | 123.2 |
| sidebar | 1 | 2,373 [2,254-2,492] | 8,000 | 0.297x | 6,196 | **0.383x** | 287 | **8.280x** | 516.2 [497-536] | 181.0 | 149.0 |
| search | 16 | 7,135 [7,088-7,193] | 34,070 | 0.209x | 26,606 | **0.268x** | 408 | **17.491x** | 478.3 [477-482] | 107.3 | 123.9 |
| search | 1 | 2,140 [1,945-2,187] | 6,055 | 0.353x | 5,646 | **0.379x** | 193 | **11.105x** | 563.9 [553-606] | 315.1 | 158.3 |

Checks:
  responses: every one 2xx with 0 connection errors in 10 runs
  FAIL decoded bytes sidebar: 29950 vs Rust 30763
  FAIL decoded bytes search: 136873 vs Rust 149623
  FAIL decoded bytes sidebar: 29950 vs Rust 30763
  FAIL decoded bytes search: 136873 vs Rust 149623
  FAIL decoded bytes search: 136873 vs Rust 149623
