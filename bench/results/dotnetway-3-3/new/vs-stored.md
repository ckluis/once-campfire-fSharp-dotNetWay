theDotNetWay in bench/results/dotnetway-3-3/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,271 [8,249-8,324] | 40,422 | 0.205x | 28,623 | **0.289x** | 239 | **34.562x** | 399.6 [398-400] | 93.6 | 122.4 |
| room page | 1 | 2,782 [2,744-2,804] | 7,617 | 0.365x | 6,481 | **0.429x** | 107 | **25.975x** | 454.7 [454-462] | 208.7 | 142.1 |
| messages page | 16 | 7,591 [7,574-7,685] | 37,836 | 0.201x | 30,063 | **0.253x** | 381 | **19.945x** | 439.4 [436-442] | 102.2 | 114.2 |
| messages page | 1 | 2,411 [2,397-2,457] | 8,312 | 0.290x | 6,822 | **0.353x** | 218 | **11.066x** | 512.9 [503-515] | 199.3 | 139.2 |
| sidebar | 16 | 7,712 [7,688-7,780] | 37,555 | 0.205x | 28,238 | **0.273x** | 599 | **12.884x** | 437.2 [434-438] | 101.8 | 123.2 |
| sidebar | 1 | 2,364 [2,325-2,371] | 8,000 | 0.296x | 6,196 | **0.382x** | 287 | **8.249x** | 525.9 [523-530] | 181.0 | 149.0 |
| search | 16 | 7,569 [7,510-7,684] | 34,070 | 0.222x | 26,606 | **0.284x** | 408 | **18.555x** | 450.9 [448-454] | 107.3 | 123.9 |
| search | 1 | 2,338 [2,328-2,340] | 6,055 | 0.386x | 5,646 | **0.414x** | 193 | **12.134x** | 521.4 [520-524] | 315.1 | 158.3 |
| post a message | 16 | 2,047 [2,029-2,063] | 8,905 | 0.230x | 7,174 | **0.285x** | 330 | **6.201x** | 1,465.5 [1,442-1,471] | 353.0 | 385.6 |
| post a message | 1 | 1,137 [1,115-1,140] | 3,052 | 0.372x | 2,779 | **0.409x** | 167 | **6.811x** | 1,618.5 [1,618-1,635] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
