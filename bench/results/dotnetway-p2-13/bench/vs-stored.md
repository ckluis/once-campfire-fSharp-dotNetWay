theDotNetWay in bench/results/dotnetway-p2-13/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,545 [5,527-5,552] | 40,422 | 0.137x | 28,623 | **0.194x** | 239 | **23.171x** | 626.2 [624-628] | 93.6 | 122.4 |
| room page | 1 | 1,706 [1,697-1,718] | 7,617 | 0.224x | 6,481 | **0.263x** | 107 | **15.934x** | 683.5 [678-687] | 208.7 | 142.1 |
| messages page | 16 | 5,243 [5,210-5,256] | 37,836 | 0.139x | 30,063 | **0.174x** | 381 | **13.775x** | 665.7 [664-668] | 102.2 | 114.2 |
| messages page | 1 | 1,538 [1,537-1,550] | 8,312 | 0.185x | 6,822 | **0.225x** | 218 | **7.059x** | 745.1 [741-746] | 199.3 | 139.2 |
| sidebar | 16 | 5,712 [5,695-5,719] | 37,555 | 0.152x | 28,238 | **0.202x** | 599 | **9.542x** | 607.4 [607-610] | 101.8 | 123.2 |
| sidebar | 1 | 1,645 [1,630-1,648] | 8,000 | 0.206x | 6,196 | **0.266x** | 287 | **5.741x** | 708.4 [705-713] | 181.0 | 149.0 |
| search | 16 | 5,082 [5,050-5,120] | 34,070 | 0.149x | 26,606 | **0.191x** | 408 | **12.459x** | 694.8 [690-696] | 107.3 | 123.9 |
| search | 1 | 1,455 [1,453-1,458] | 6,055 | 0.240x | 5,646 | **0.258x** | 193 | **7.553x** | 782.1 [782-784] | 315.1 | 158.3 |
| post a message | 16 | 1,902 [1,886-1,915] | 8,905 | 0.214x | 7,174 | **0.265x** | 330 | **5.761x** | 1,618.9 [1,619-1,638] | 353.0 | 385.6 |
| post a message | 1 | 952 [932-957] | 3,052 | 0.312x | 2,779 | **0.343x** | 167 | **5.703x** | 1,840.2 [1,835-1,845] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
