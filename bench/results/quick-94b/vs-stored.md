theDotNetWay in bench/results/quick-94b against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 2,198 [2,122-2,221] | 8,905 | 0.247x | 7,174 | **0.306x** | 330 | **6.658x** | 1,467.9 [1,451-1,534] | 353.0 | 385.6 |
| post a message | 1 | 1,122 [1,095-1,152] | 3,052 | 0.368x | 2,779 | **0.404x** | 167 | **6.724x** | 1,646.4 [1,615-1,690] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
