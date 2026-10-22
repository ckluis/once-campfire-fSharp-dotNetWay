theDotNetWay in bench/results/dotnetway-p4/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,433 [7,359-7,450] | 40,422 | 0.184x | 28,623 | **0.260x** | 239 | **31.062x** | 437.3 [436-439] | 93.6 | 122.4 |
| room page | 1 | 2,504 [2,495-2,517] | 7,617 | 0.329x | 6,481 | **0.386x** | 107 | **23.377x** | 496.0 [493-499] | 208.7 | 142.1 |
| messages page | 16 | 7,286 [7,263-7,295] | 37,836 | 0.193x | 30,063 | **0.242x** | 381 | **19.145x** | 457.6 [457-458] | 102.2 | 114.2 |
| messages page | 1 | 2,303 [2,294-2,310] | 8,312 | 0.277x | 6,822 | **0.338x** | 218 | **10.567x** | 529.6 [528-532] | 199.3 | 139.2 |
| sidebar | 16 | 7,605 [7,547-7,617] | 37,555 | 0.203x | 28,238 | **0.269x** | 599 | **12.705x** | 433.1 [428-434] | 101.8 | 123.2 |
| sidebar | 1 | 2,440 [2,434-2,457] | 8,000 | 0.305x | 6,196 | **0.394x** | 287 | **8.512x** | 504.3 [501-506] | 181.0 | 149.0 |
| search | 16 | 6,650 [6,627-7,053] | 34,070 | 0.195x | 26,606 | **0.250x** | 408 | **16.303x** | 510.6 [480-517] | 107.3 | 123.9 |
| search | 1 | 2,181 [2,123-2,212] | 6,055 | 0.360x | 5,646 | **0.386x** | 193 | **11.319x** | 557.0 [545-568] | 315.1 | 158.3 |
| post a message | 16 | 1,995 [1,974-2,058] | 8,905 | 0.224x | 7,174 | **0.278x** | 330 | **6.044x** | 1,523.1 [1,457-1,529] | 353.0 | 385.6 |
| post a message | 1 | 1,138 [1,136-1,151] | 3,052 | 0.373x | 2,779 | **0.409x** | 167 | **6.817x** | 1,665.2 [1,628-1,674] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
