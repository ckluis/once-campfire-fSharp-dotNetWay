theDotNetWay in bench/results/dotnetway-p2-8/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,492 [5,411-5,532] | 40,422 | 0.136x | 28,623 | **0.192x** | 239 | **22.952x** | 629.1 [625-640] | 93.6 | 122.4 |
| room page | 1 | 1,654 [1,636-1,678] | 7,617 | 0.217x | 6,481 | **0.255x** | 107 | **15.448x** | 703.8 [693-714] | 208.7 | 142.1 |
| messages page | 16 | 5,160 [5,106-5,188] | 37,836 | 0.136x | 30,063 | **0.172x** | 381 | **13.558x** | 669.6 [669-680] | 102.2 | 114.2 |
| messages page | 1 | 1,495 [1,482-1,509] | 8,312 | 0.180x | 6,822 | **0.219x** | 218 | **6.861x** | 767.0 [759-773] | 199.3 | 139.2 |
| sidebar | 16 | 5,544 [5,237-5,553] | 37,555 | 0.148x | 28,238 | **0.196x** | 599 | **9.262x** | 626.7 [624-659] | 101.8 | 123.2 |
| sidebar | 1 | 1,564 [1,540-1,573] | 8,000 | 0.195x | 6,196 | **0.252x** | 287 | **5.455x** | 737.1 [736-749] | 181.0 | 149.0 |
| search | 16 | 4,802 [4,720-5,019] | 34,070 | 0.141x | 26,606 | **0.180x** | 408 | **11.772x** | 730.6 [699-744] | 107.3 | 123.9 |
| search | 1 | 1,361 [1,324-1,423] | 6,055 | 0.225x | 5,646 | **0.241x** | 193 | **7.063x** | 832.2 [797-856] | 315.1 | 158.3 |
| post a message | 16 | 1,832 [1,810-1,872] | 8,905 | 0.206x | 7,174 | **0.255x** | 330 | **5.549x** | 1,682.8 [1,634-1,705] | 353.0 | 385.6 |
| post a message | 1 | 913 [899-915] | 3,052 | 0.299x | 2,779 | **0.329x** | 167 | **5.471x** | 1,905.6 [1,862-1,942] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
