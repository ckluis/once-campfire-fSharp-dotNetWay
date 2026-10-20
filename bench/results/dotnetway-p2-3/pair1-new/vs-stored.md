theDotNetWay in bench/results/dotnetway-p2-3/pair1-new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 4,426 [4,381-4,472] | 37,555 | 0.118x | 28,238 | **0.157x** | 599 | **7.395x** | 779.6 [770-789] | 101.8 | 123.2 |
| sidebar | 1 | 1,496 [1,493-1,498] | 8,000 | 0.187x | 6,196 | **0.241x** | 287 | **5.218x** | 748.1 [745-751] | 181.0 | 149.0 |
| post a message | 16 | 1,418 [1,407-1,430] | 8,905 | 0.159x | 7,174 | **0.198x** | 330 | **4.297x** | 2,186.7 [2,182-2,191] | 353.0 | 385.6 |
| post a message | 1 | 806 [803-808] | 3,052 | 0.264x | 2,779 | **0.290x** | 167 | **4.829x** | 2,119.8 [2,111-2,129] | 629.1 | 403.7 |

Checks:
  note post_message c=1: host probe 1.107 after the window (noisy host)
  note post_message c=16: host probe 1.111 after the window (noisy host)
  note sidebar c=1: host probe 1.109 after the window (noisy host)
  note sidebar c=16: host probe 1.108 after the window (noisy host)
  note sidebar c=16: host probe 1.103 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 8 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
