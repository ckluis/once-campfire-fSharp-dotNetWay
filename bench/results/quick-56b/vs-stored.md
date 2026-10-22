theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/quick-56b against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,373 [7,320-7,426] | 40,422 | 0.182x | 28,623 | **0.258x** | 239 | **30.812x** | 437.7 [436-441] | 93.6 | 122.4 |
| room page | 1 | 2,468 [2,463-2,512] | 7,617 | 0.324x | 6,481 | **0.381x** | 107 | **23.049x** | 502.3 [494-508] | 208.7 | 142.1 |
| messages page | 16 | 7,277 [7,274-7,318] | 37,836 | 0.192x | 30,063 | **0.242x** | 381 | **19.121x** | 455.6 [453-458] | 102.2 | 114.2 |
| messages page | 1 | 2,284 [2,273-2,301] | 8,312 | 0.275x | 6,822 | **0.335x** | 218 | **10.484x** | 533.2 [530-537] | 199.3 | 139.2 |
| search | 16 | 6,965 [6,899-7,036] | 34,070 | 0.204x | 26,606 | **0.262x** | 408 | **17.076x** | 484.4 [478-488] | 107.3 | 123.9 |
| search | 1 | 2,101 [2,077-2,112] | 6,055 | 0.347x | 5,646 | **0.372x** | 193 | **10.902x** | 570.6 [568-578] | 315.1 | 158.3 |
| post a message | 16 | 2,020 [2,014-2,024] | 8,905 | 0.227x | 7,174 | **0.282x** | 330 | **6.118x** | 1,486.7 [1,483-1,503] | 353.0 | 385.6 |
| post a message | 1 | 1,100 [1,092-1,107] | 3,052 | 0.360x | 2,779 | **0.396x** | 167 | **6.588x** | 1,673.9 [1,664-1,690] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 24 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
