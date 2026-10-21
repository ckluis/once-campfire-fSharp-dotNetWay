theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p2/quick against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,498 [4,392-5,529] | 40,422 | 0.136x | 28,623 | **0.192x** | 239 | **22.974x** | 631.2 [625-810] | 93.6 | 122.4 |
| room page | 1 | 1,689 [1,516-1,717] | 7,617 | 0.222x | 6,481 | **0.261x** | 107 | **15.772x** | 689.5 [682-764] | 208.7 | 142.1 |
| messages page | 16 | 5,191 [4,085-5,195] | 37,836 | 0.137x | 30,063 | **0.173x** | 381 | **13.639x** | 670.6 [670-874] | 102.2 | 114.2 |
| messages page | 1 | 1,541 [1,383-1,551] | 8,312 | 0.185x | 6,822 | **0.226x** | 218 | **7.070x** | 745.5 [742-828] | 199.3 | 139.2 |
| sidebar | 16 | 5,582 [4,660-5,664] | 37,555 | 0.149x | 28,238 | **0.198x** | 599 | **9.325x** | 618.3 [614-754] | 101.8 | 123.2 |
| sidebar | 1 | 1,620 [1,485-1,646] | 8,000 | 0.202x | 6,196 | **0.261x** | 287 | **5.652x** | 716.8 [710-774] | 181.0 | 149.0 |
| search | 16 | 5,062 [5,052-5,121] | 34,070 | 0.149x | 26,606 | **0.190x** | 408 | **12.410x** | 699.3 [691-701] | 107.3 | 123.9 |
| search | 1 | 1,505 [1,503-1,528] | 6,055 | 0.249x | 5,646 | **0.267x** | 193 | **7.812x** | 760.4 [751-764] | 315.1 | 158.3 |
| post a message | 16 | 1,885 [1,882-1,916] | 8,905 | 0.212x | 7,174 | **0.263x** | 330 | **5.709x** | 1,629.0 [1,616-1,641] | 353.0 | 385.6 |
| post a message | 1 | 945 [935-949] | 3,052 | 0.310x | 2,779 | **0.340x** | 167 | **5.661x** | 1,839.1 [1,829-1,877] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
