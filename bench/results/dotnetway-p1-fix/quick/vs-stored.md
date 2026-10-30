theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-fix/quick against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,013 [4,430-6,417] | 40,422 | 0.149x | 28,623 | **0.210x** | 239 | **25.128x** | 564.1 [524-736] | 93.6 | 122.4 |
| room page | 1 | 1,250 [1,016-1,937] | 7,617 | 0.164x | 6,481 | **0.193x** | 107 | **11.674x** | 870.6 [594-1,019] | 208.7 | 142.1 |
| messages page | 16 | 5,178 [3,748-5,239] | 37,836 | 0.137x | 30,063 | **0.172x** | 381 | **13.605x** | 663.1 [658-899] | 102.2 | 114.2 |
| messages page | 1 | 1,501 [1,199-1,506] | 8,312 | 0.181x | 6,822 | **0.220x** | 218 | **6.890x** | 761.9 [761-911] | 199.3 | 139.2 |
| sidebar | 16 | 5,896 [5,874-5,947] | 37,555 | 0.157x | 28,238 | **0.209x** | 599 | **9.850x** | 577.9 [576-578] | 101.8 | 123.2 |
| sidebar | 1 | 1,703 [1,692-1,709] | 8,000 | 0.213x | 6,196 | **0.275x** | 287 | **5.943x** | 682.4 [680-686] | 181.0 | 149.0 |
| search | 16 | 5,071 [4,407-5,199] | 34,070 | 0.149x | 26,606 | **0.191x** | 408 | **12.431x** | 686.6 [676-792] | 107.3 | 123.9 |
| search | 1 | 1,480 [1,463-1,493] | 6,055 | 0.244x | 5,646 | **0.262x** | 193 | **7.681x** | 770.6 [766-779] | 315.1 | 158.3 |
| post a message | 16 | 1,857 [1,500-1,875] | 8,905 | 0.209x | 7,174 | **0.259x** | 330 | **5.626x** | 1,649.1 [1,630-2,075] | 353.0 | 385.6 |
| post a message | 1 | 930 [867-949] | 3,052 | 0.305x | 2,779 | **0.335x** | 167 | **5.570x** | 1,866.1 [1,862-2,020] | 629.1 | 403.7 |

Checks:
  note room_show c=16: host probe 1.131 after the window (noisy host)
  note room_show c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
