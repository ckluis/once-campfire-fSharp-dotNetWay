theDotNetWay in bench/results/dotnetway-p2-3/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 4,451 [4,439-4,475] | 37,555 | 0.119x | 28,238 | **0.158x** | 599 | **7.435x** | 773.9 [769-779] | 101.8 | 123.2 |
| sidebar | 1 | 1,478 [1,473-1,479] | 8,000 | 0.185x | 6,196 | **0.238x** | 287 | **5.155x** | 759.8 [759-762] | 181.0 | 149.0 |
| post a message | 16 | 1,422 [1,398-1,434] | 8,905 | 0.160x | 7,174 | **0.198x** | 330 | **4.307x** | 2,202.9 [2,181-2,246] | 353.0 | 385.6 |
| post a message | 1 | 806 [790-819] | 3,052 | 0.264x | 2,779 | **0.290x** | 167 | **4.830x** | 2,140.6 [2,114-2,155] | 629.1 | 403.7 |

Checks:
  note post_message c=1: host probe 1.103 after the window (noisy host)
  note post_message c=1: host probe 1.101 after the window (noisy host)
  note post_message c=16: host probe 1.102 after the window (noisy host)
  note post_message c=16: host probe 1.112 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
