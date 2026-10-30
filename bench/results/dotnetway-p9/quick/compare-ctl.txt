theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p9/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,168 [7,134-7,175] | 40,422 | 0.177x | 28,623 | **0.250x** | 239 | **29.956x** | 460.0 [458-462] | 93.6 | 122.4 |
| room page | 1 | 2,190 [2,150-2,223] | 7,617 | 0.288x | 6,481 | **0.338x** | 107 | **20.450x** | 567.8 [563-577] | 208.7 | 142.1 |
| messages page | 16 | 7,061 [7,026-7,098] | 37,836 | 0.187x | 30,063 | **0.235x** | 381 | **18.551x** | 475.6 [474-479] | 102.2 | 114.2 |
| messages page | 1 | 2,090 [2,057-2,145] | 8,312 | 0.252x | 6,822 | **0.306x** | 218 | **9.593x** | 592.4 [577-599] | 199.3 | 139.2 |
| sidebar | 16 | 7,296 [7,293-7,315] | 37,555 | 0.194x | 28,238 | **0.258x** | 599 | **12.188x** | 454.4 [454-458] | 101.8 | 123.2 |
| sidebar | 1 | 2,159 [2,157-2,236] | 8,000 | 0.270x | 6,196 | **0.348x** | 287 | **7.535x** | 576.5 [558-577] | 181.0 | 149.0 |
| search | 16 | 6,859 [6,851-6,914] | 34,070 | 0.201x | 26,606 | **0.258x** | 408 | **16.815x** | 498.8 [496-502] | 107.3 | 123.9 |
| search | 1 | 1,976 [1,955-2,010] | 6,055 | 0.326x | 5,646 | **0.350x** | 193 | **10.255x** | 619.2 [612-625] | 315.1 | 158.3 |
| post a message | 16 | 2,110 [2,107-2,136] | 8,905 | 0.237x | 7,174 | **0.294x** | 330 | **6.392x** | 1,532.5 [1,511-1,533] | 353.0 | 385.6 |
| post a message | 1 | 1,051 [1,050-1,069] | 3,052 | 0.345x | 2,779 | **0.378x** | 167 | **6.300x** | 1,796.9 [1,789-1,816] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
