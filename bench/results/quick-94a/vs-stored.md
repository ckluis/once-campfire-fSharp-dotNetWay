theDotNetWay in bench/results/quick-94a against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| post a message | 16 | 2,199 [2,187-2,213] | 8,905 | 0.247x | 7,174 | **0.307x** | 330 | **6.662x** | 1,467.9 [1,464-1,487] | 353.0 | 385.6 |
| post a message | 1 | 1,107 [1,095-1,124] | 3,052 | 0.363x | 2,779 | **0.398x** | 167 | **6.633x** | 1,665.4 [1,652-1,680] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
