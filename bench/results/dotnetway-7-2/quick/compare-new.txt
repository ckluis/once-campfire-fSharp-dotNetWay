theDotNetWay in bench/results/dotnetway-7-2/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,409 [7,292-7,501] | 40,422 | 0.183x | 28,623 | **0.259x** | 239 | **30.960x** | 456.7 [450-468] | 93.6 | 122.4 |
| room page | 1 | 2,190 [2,056-2,211] | 7,617 | 0.288x | 6,481 | **0.338x** | 107 | **20.451x** | 570.4 [562-596] | 208.7 | 142.1 |
| post a message | 16 | 2,127 [2,107-2,167] | 8,905 | 0.239x | 7,174 | **0.296x** | 330 | **6.444x** | 1,529.6 [1,502-1,542] | 353.0 | 385.6 |
| post a message | 1 | 1,032 [1,002-1,043] | 3,052 | 0.338x | 2,779 | **0.371x** | 167 | **6.180x** | 1,753.3 [1,730-1,776] | 629.1 | 403.7 |

Checks:
  note avatar c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 18 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
