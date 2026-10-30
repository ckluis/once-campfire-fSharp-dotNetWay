theDotNetWay in bench/results/dotnetway-7-4a/vs-p7pre against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,481 [7,436-7,504] | 40,422 | 0.185x | 28,623 | **0.261x** | 239 | **31.262x** | 450.0 [448-452] | 93.6 | 122.4 |
| room page | 1 | 2,192 [2,179-2,203] | 7,617 | 0.288x | 6,481 | **0.338x** | 107 | **20.466x** | 568.8 [565-572] | 208.7 | 142.1 |
| messages page | 16 | 7,331 [7,282-7,416] | 37,836 | 0.194x | 30,063 | **0.244x** | 381 | **19.261x** | 468.4 [465-471] | 102.2 | 114.2 |
| messages page | 1 | 2,036 [2,036-2,050] | 8,312 | 0.245x | 6,822 | **0.298x** | 218 | **9.345x** | 602.6 [599-604] | 199.3 | 139.2 |
| sidebar | 16 | 7,583 [7,460-7,727] | 37,555 | 0.202x | 28,238 | **0.269x** | 599 | **12.668x** | 447.8 [442-456] | 101.8 | 123.2 |
| sidebar | 1 | 2,133 [2,132-2,141] | 8,000 | 0.267x | 6,196 | **0.344x** | 287 | **7.443x** | 579.7 [579-581] | 181.0 | 149.0 |
| search | 16 | 7,101 [5,777-7,108] | 34,070 | 0.208x | 26,606 | **0.267x** | 408 | **17.408x** | 493.9 [492-602] | 107.3 | 123.9 |
| search | 1 | 1,879 [1,842-1,889] | 6,055 | 0.310x | 5,646 | **0.333x** | 193 | **9.752x** | 643.7 [642-656] | 315.1 | 158.3 |
| post a message | 16 | 2,118 [1,975-2,123] | 8,905 | 0.238x | 7,174 | **0.295x** | 330 | **6.416x** | 1,529.9 [1,528-1,648] | 353.0 | 385.6 |
| post a message | 1 | 1,025 [942-1,054] | 3,052 | 0.336x | 2,779 | **0.369x** | 167 | **6.139x** | 1,769.5 [1,749-1,892] | 629.1 | 403.7 |

Checks:
  note avatar c=16: warm-up did not settle (reached the maximum)
  note avatar c=16: host probe 1.105 after the window (noisy host)
  note post_message c=16: host probe 1.102 after the window (noisy host)
  note search c=1: host probe 1.243 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 36 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
