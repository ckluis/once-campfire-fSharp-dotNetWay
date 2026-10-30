theDotNetWay in bench/results/dotnetway-p2-3/pair2-new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 4,430 [4,406-4,455] | 37,555 | 0.118x | 28,238 | **0.157x** | 599 | **7.401x** | 777.4 [771-784] | 101.8 | 123.2 |
| sidebar | 1 | 1,486 [1,462-1,510] | 8,000 | 0.186x | 6,196 | **0.240x** | 287 | **5.185x** | 753.6 [741-766] | 181.0 | 149.0 |
| post a message | 16 | 1,438 [1,433-1,442] | 8,905 | 0.161x | 7,174 | **0.200x** | 330 | **4.356x** | 2,181.7 [2,175-2,189] | 353.0 | 385.6 |
| post a message | 1 | 819 [813-826] | 3,052 | 0.268x | 2,779 | **0.295x** | 167 | **4.908x** | 2,108.7 [2,088-2,130] | 629.1 | 403.7 |

Checks:
  note sidebar c=1: host probe 1.12 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 8 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
