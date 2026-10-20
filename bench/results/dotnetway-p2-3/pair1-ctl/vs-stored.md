theDotNetWay in bench/results/dotnetway-p2-3/pair1-ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 4,432 [4,343-4,521] | 37,555 | 0.118x | 28,238 | **0.157x** | 599 | **7.404x** | 776.7 [763-790] | 101.8 | 123.2 |
| sidebar | 1 | 1,492 [1,490-1,493] | 8,000 | 0.186x | 6,196 | **0.241x** | 287 | **5.205x** | 753.1 [750-756] | 181.0 | 149.0 |
| post a message | 16 | 1,408 [1,393-1,423] | 8,905 | 0.158x | 7,174 | **0.196x** | 330 | **4.265x** | 2,224.4 [2,199-2,250] | 353.0 | 385.6 |
| post a message | 1 | 819 [815-823] | 3,052 | 0.268x | 2,779 | **0.295x** | 167 | **4.907x** | 2,116.6 [2,111-2,122] | 629.1 | 403.7 |

Checks:
  note post_message c=1: host probe 1.105 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 8 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
