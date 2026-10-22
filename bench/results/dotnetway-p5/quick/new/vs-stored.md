theDotNetWay in bench/results/dotnetway-p5/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,299 [7,112-7,395] | 40,422 | 0.181x | 28,623 | **0.255x** | 239 | **30.500x** | 448.3 [441-455] | 93.6 | 122.4 |
| room page | 1 | 2,383 [2,354-2,428] | 7,617 | 0.313x | 6,481 | **0.368x** | 107 | **22.249x** | 515.4 [510-523] | 208.7 | 142.1 |
| messages page | 16 | 7,031 [6,909-7,324] | 37,836 | 0.186x | 30,063 | **0.234x** | 381 | **18.474x** | 472.2 [462-483] | 102.2 | 114.2 |
| messages page | 1 | 2,206 [2,187-2,218] | 8,312 | 0.265x | 6,822 | **0.323x** | 218 | **10.123x** | 549.8 [548-552] | 199.3 | 139.2 |
| sidebar | 16 | 7,500 [7,140-7,664] | 37,555 | 0.200x | 28,238 | **0.266x** | 599 | **12.528x** | 436.5 [433-460] | 101.8 | 123.2 |
| sidebar | 1 | 2,217 [2,204-2,339] | 8,000 | 0.277x | 6,196 | **0.358x** | 287 | **7.736x** | 546.7 [523-552] | 181.0 | 149.0 |
| search | 16 | 6,923 [6,870-6,932] | 34,070 | 0.203x | 26,606 | **0.260x** | 408 | **16.972x** | 488.5 [483-491] | 107.3 | 123.9 |
| search | 1 | 2,125 [2,068-2,153] | 6,055 | 0.351x | 5,646 | **0.376x** | 193 | **11.029x** | 568.7 [558-579] | 315.1 | 158.3 |
| post a message | 16 | 1,994 [1,978-2,005] | 8,905 | 0.224x | 7,174 | **0.278x** | 330 | **6.040x** | 1,508.7 [1,507-1,524] | 353.0 | 385.6 |
| post a message | 1 | 1,082 [1,065-1,086] | 3,052 | 0.355x | 2,779 | **0.390x** | 167 | **6.486x** | 1,691.3 [1,689-1,694] | 629.1 | 403.7 |

Checks:
  note sidebar c=1: host probe 1.127 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
