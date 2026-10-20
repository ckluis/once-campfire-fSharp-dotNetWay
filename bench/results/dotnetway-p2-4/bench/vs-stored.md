theDotNetWay in bench/results/dotnetway-p2-4/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 5,835 [5,814-5,979] | 37,555 | 0.155x | 28,238 | **0.207x** | 599 | **9.747x** | 588.4 [579-593] | 101.8 | 123.2 |
| sidebar | 1 | 1,727 [1,711-1,775] | 8,000 | 0.216x | 6,196 | **0.279x** | 287 | **6.025x** | 676.7 [661-678] | 181.0 | 149.0 |
| post a message | 16 | 1,813 [1,801-1,874] | 8,905 | 0.204x | 7,174 | **0.253x** | 330 | **5.493x** | 1,702.2 [1,647-1,706] | 353.0 | 385.6 |
| post a message | 1 | 910 [877-917] | 3,052 | 0.298x | 2,779 | **0.327x** | 167 | **5.451x** | 1,928.8 [1,899-1,967] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
