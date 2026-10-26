theDotNetWay in bench/results/dotnetway-7-2/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,768 [7,753-7,783] | 40,422 | 0.192x | 28,623 | **0.271x** | 239 | **32.461x** | 428.0 [427-429] | 93.6 | 122.4 |
| room page | 1 | 2,539 [2,498-2,548] | 7,617 | 0.333x | 6,481 | **0.392x** | 107 | **23.705x** | 488.9 [488-495] | 208.7 | 142.1 |
| post a message | 16 | 2,036 [2,032-2,048] | 8,905 | 0.229x | 7,174 | **0.284x** | 330 | **6.168x** | 1,475.4 [1,470-1,476] | 353.0 | 385.6 |
| post a message | 1 | 1,128 [1,127-1,136] | 3,052 | 0.370x | 2,779 | **0.406x** | 167 | **6.757x** | 1,634.3 [1,629-1,651] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 18 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
