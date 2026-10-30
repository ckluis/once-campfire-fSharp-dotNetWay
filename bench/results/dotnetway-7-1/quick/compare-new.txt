theDotNetWay in bench/results/dotnetway-7-1/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,281 [7,133-7,295] | 40,422 | 0.180x | 28,623 | **0.254x** | 239 | **30.427x** | 452.9 [452-459] | 93.6 | 122.4 |
| room page | 1 | 2,224 [2,203-2,263] | 7,617 | 0.292x | 6,481 | **0.343x** | 107 | **20.769x** | 561.0 [554-568] | 208.7 | 142.1 |
| messages page | 16 | 7,166 [7,032-7,181] | 37,836 | 0.189x | 30,063 | **0.238x** | 381 | **18.827x** | 467.1 [467-478] | 102.2 | 114.2 |
| messages page | 1 | 2,081 [2,014-2,102] | 8,312 | 0.250x | 6,822 | **0.305x** | 218 | **9.550x** | 592.6 [586-609] | 199.3 | 139.2 |
| sidebar | 16 | 7,381 [7,354-7,382] | 37,555 | 0.197x | 28,238 | **0.261x** | 599 | **12.330x** | 449.1 [448-451] | 101.8 | 123.2 |
| sidebar | 1 | 2,170 [2,145-2,172] | 8,000 | 0.271x | 6,196 | **0.350x** | 287 | **7.572x** | 573.4 [571-578] | 181.0 | 149.0 |
| search | 16 | 6,947 [6,176-7,013] | 34,070 | 0.204x | 26,606 | **0.261x** | 408 | **17.030x** | 495.1 [489-542] | 107.3 | 123.9 |
| search | 1 | 1,957 [1,871-1,964] | 6,055 | 0.323x | 5,646 | **0.347x** | 193 | **10.154x** | 626.0 [622-645] | 315.1 | 158.3 |
| post a message | 16 | 1,971 [1,854-1,997] | 8,905 | 0.221x | 7,174 | **0.275x** | 330 | **5.970x** | 1,545.1 [1,519-1,628] | 353.0 | 385.6 |
| post a message | 1 | 1,034 [964-1,043] | 3,052 | 0.339x | 2,779 | **0.372x** | 167 | **6.195x** | 1,732.4 [1,718-1,808] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
