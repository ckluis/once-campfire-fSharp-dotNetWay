theDotNetWay in bench/results/quick-93 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,563 [6,972-7,621] | 40,422 | 0.187x | 28,623 | **0.264x** | 239 | **31.604x** | 446.1 [440-476] | 93.6 | 122.4 |
| room page | 1 | 2,245 [2,128-2,261] | 7,617 | 0.295x | 6,481 | **0.346x** | 107 | **20.963x** | 560.8 [558-583] | 208.7 | 142.1 |
| post a message | 16 | 2,159 [2,109-2,162] | 8,905 | 0.242x | 7,174 | **0.301x** | 330 | **6.541x** | 1,507.0 [1,505-1,534] | 353.0 | 385.6 |
| post a message | 1 | 1,058 [1,054-1,064] | 3,052 | 0.347x | 2,779 | **0.381x** | 167 | **6.339x** | 1,809.8 [1,800-1,815] | 629.1 | 403.7 |

Checks:
  note static_css c=1: warm-up did not settle (reached the maximum)
  note up c=1: warm-up did not settle (reached the maximum)
  note up c=1: warm-up did not settle (reached the maximum)
  note up c=1: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
