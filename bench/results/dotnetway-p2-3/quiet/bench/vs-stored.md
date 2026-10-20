theDotNetWay in bench/results/dotnetway-p2-3/quiet/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 6,050 [6,009-6,076] | 37,555 | 0.161x | 28,238 | **0.214x** | 599 | **10.107x** | 573.2 [568-576] | 101.8 | 123.2 |
| sidebar | 1 | 1,752 [1,749-1,757] | 8,000 | 0.219x | 6,196 | **0.283x** | 287 | **6.115x** | 667.6 [665-669] | 181.0 | 149.0 |
| post a message | 16 | 1,876 [1,866-1,906] | 8,905 | 0.211x | 7,174 | **0.261x** | 330 | **5.682x** | 1,644.5 [1,623-1,649] | 353.0 | 385.6 |
| post a message | 1 | 933 [915-944] | 3,052 | 0.306x | 2,779 | **0.336x** | 167 | **5.591x** | 1,890.3 [1,880-1,894] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
