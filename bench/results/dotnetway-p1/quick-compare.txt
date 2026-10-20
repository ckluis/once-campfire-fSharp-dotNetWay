theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1/quick against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,463 [6,462-6,570] | 40,422 | 0.160x | 28,623 | **0.226x** | 239 | **27.008x** | 521.6 [518-528] | 93.6 | 122.4 |
| room page | 1 | 1,946 [1,644-1,993] | 7,617 | 0.255x | 6,481 | **0.300x** | 107 | **18.170x** | 612.7 [595-696] | 208.7 | 142.1 |
| messages page | 16 | 5,264 [5,180-5,304] | 37,836 | 0.139x | 30,063 | **0.175x** | 381 | **13.832x** | 660.1 [652-671] | 102.2 | 114.2 |
| messages page | 1 | 1,519 [1,496-1,532] | 8,312 | 0.183x | 6,822 | **0.223x** | 218 | **6.973x** | 754.7 [748-766] | 199.3 | 139.2 |
| sidebar | 16 | 4,163 [4,102-4,165] | 37,555 | 0.111x | 28,238 | **0.147x** | 599 | **6.955x** | 839.3 [836-853] | 101.8 | 123.2 |
| sidebar | 1 | 1,180 [1,178-1,198] | 8,000 | 0.148x | 6,196 | **0.190x** | 287 | **4.118x** | 944.2 [931-947] | 181.0 | 149.0 |
| search | 16 | 5,228 [5,089-5,232] | 34,070 | 0.153x | 26,606 | **0.197x** | 408 | **12.818x** | 677.5 [675-691] | 107.3 | 123.9 |
| search | 1 | 1,486 [1,447-1,523] | 6,055 | 0.245x | 5,646 | **0.263x** | 193 | **7.713x** | 769.6 [754-785] | 315.1 | 158.3 |
| post a message | 16 | 1,857 [1,839-1,899] | 8,905 | 0.209x | 7,174 | **0.259x** | 330 | **5.626x** | 1,657.7 [1,617-1,670] | 353.0 | 385.6 |
| post a message | 1 | 931 [915-964] | 3,052 | 0.305x | 2,779 | **0.335x** | 167 | **5.579x** | 1,890.4 [1,850-1,896] | 629.1 | 403.7 |

Checks:
  note sidebar c=16: host probe 1.105 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
