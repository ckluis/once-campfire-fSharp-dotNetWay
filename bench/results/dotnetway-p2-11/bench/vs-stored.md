theDotNetWay in bench/results/dotnetway-p2-11/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,231 [5,131-5,315] | 40,422 | 0.129x | 28,623 | **0.183x** | 239 | **21.859x** | 658.5 [650-672] | 93.6 | 122.4 |
| room page | 1 | 1,545 [1,541-1,547] | 7,617 | 0.203x | 6,481 | **0.238x** | 107 | **14.429x** | 744.9 [742-749] | 208.7 | 142.1 |
| messages page | 16 | 4,947 [4,710-4,977] | 37,836 | 0.131x | 30,063 | **0.165x** | 381 | **12.998x** | 699.2 [697-734] | 102.2 | 114.2 |
| messages page | 1 | 1,426 [1,414-1,440] | 8,312 | 0.172x | 6,822 | **0.209x** | 218 | **6.544x** | 802.5 [793-803] | 199.3 | 139.2 |
| sidebar | 16 | 5,358 [5,217-5,469] | 37,555 | 0.143x | 28,238 | **0.190x** | 599 | **8.951x** | 647.2 [633-665] | 101.8 | 123.2 |
| sidebar | 1 | 1,528 [1,519-1,537] | 8,000 | 0.191x | 6,196 | **0.247x** | 287 | **5.330x** | 749.8 [750-760] | 181.0 | 149.0 |
| search | 16 | 4,757 [4,732-4,799] | 34,070 | 0.140x | 26,606 | **0.179x** | 408 | **11.661x** | 740.0 [733-741] | 107.3 | 123.9 |
| search | 1 | 1,351 [1,349-1,370] | 6,055 | 0.223x | 5,646 | **0.239x** | 193 | **7.009x** | 837.9 [830-840] | 315.1 | 158.3 |
| post a message | 16 | 1,734 [1,731-1,784] | 8,905 | 0.195x | 7,174 | **0.242x** | 330 | **5.251x** | 1,779.8 [1,721-1,786] | 353.0 | 385.6 |
| post a message | 1 | 875 [856-889] | 3,052 | 0.287x | 2,779 | **0.315x** | 167 | **5.241x** | 1,976.8 [1,945-2,002] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
