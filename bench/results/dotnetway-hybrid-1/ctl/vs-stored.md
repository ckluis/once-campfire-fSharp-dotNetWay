theDotNetWay in bench/results/dotnetway-hybrid-1/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,579 [5,564-5,703] | 40,422 | 0.138x | 28,623 | **0.195x** | 239 | **23.316x** | 621.7 [622-624] | 93.6 | 122.4 |
| room page | 1 | 1,704 [1,700-1,711] | 7,617 | 0.224x | 6,481 | **0.263x** | 107 | **15.908x** | 685.4 [682-686] | 208.7 | 142.1 |
| messages page | 16 | 5,227 [5,195-5,317] | 37,836 | 0.138x | 30,063 | **0.174x** | 381 | **13.733x** | 669.9 [666-670] | 102.2 | 114.2 |
| messages page | 1 | 1,543 [1,536-1,558] | 8,312 | 0.186x | 6,822 | **0.226x** | 218 | **7.081x** | 744.8 [739-749] | 199.3 | 139.2 |
| sidebar | 16 | 5,712 [5,671-5,793] | 37,555 | 0.152x | 28,238 | **0.202x** | 599 | **9.542x** | 610.2 [609-613] | 101.8 | 123.2 |
| sidebar | 1 | 1,631 [1,617-1,636] | 8,000 | 0.204x | 6,196 | **0.263x** | 287 | **5.689x** | 713.5 [711-718] | 181.0 | 149.0 |
| search | 16 | 5,101 [5,085-5,113] | 34,070 | 0.150x | 26,606 | **0.192x** | 408 | **12.505x** | 694.3 [691-695] | 107.3 | 123.9 |
| search | 1 | 1,515 [1,490-1,523] | 6,055 | 0.250x | 5,646 | **0.268x** | 193 | **7.864x** | 754.9 [753-769] | 315.1 | 158.3 |
| post a message | 16 | 1,901 [1,900-1,904] | 8,905 | 0.213x | 7,174 | **0.265x** | 330 | **5.758x** | 1,624.5 [1,622-1,628] | 353.0 | 385.6 |
| post a message | 1 | 954 [950-955] | 3,052 | 0.312x | 2,779 | **0.343x** | 167 | **5.714x** | 1,839.1 [1,838-1,844] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
