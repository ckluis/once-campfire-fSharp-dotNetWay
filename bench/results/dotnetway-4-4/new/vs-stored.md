theDotNetWay in bench/results/dotnetway-4-4/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| messages page | 16 | 7,325 [7,323-7,388] | 37,836 | 0.194x | 30,063 | **0.244x** | 381 | **19.246x** | 452.5 [452-455] | 102.2 | 114.2 |
| messages page | 1 | 2,397 [2,357-2,431] | 8,312 | 0.288x | 6,822 | **0.351x** | 218 | **10.999x** | 512.7 [507-524] | 199.3 | 139.2 |
| post a message | 16 | 2,076 [2,075-2,088] | 8,905 | 0.233x | 7,174 | **0.289x** | 330 | **6.289x** | 1,433.2 [1,427-1,440] | 353.0 | 385.6 |
| post a message | 1 | 1,185 [1,168-1,190] | 3,052 | 0.388x | 2,779 | **0.426x** | 167 | **7.100x** | 1,599.4 [1,594-1,600] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  FAIL decoded bytes messages_page: 345844 vs Rust 383844
  FAIL decoded bytes messages_page: 345844 vs Rust 383844
  FAIL decoded bytes messages_page: 345844 vs Rust 383844
