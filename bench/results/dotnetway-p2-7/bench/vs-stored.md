theDotNetWay in bench/results/dotnetway-p2-7/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,206 [4,978-5,474] | 40,422 | 0.129x | 28,623 | **0.182x** | 239 | **21.755x** | 663.9 [633-698] | 93.6 | 122.4 |
| room page | 1 | 1,575 [1,527-1,652] | 7,617 | 0.207x | 6,481 | **0.243x** | 107 | **14.707x** | 731.2 [703-754] | 208.7 | 142.1 |
| messages page | 16 | 4,798 [4,618-5,127] | 37,836 | 0.127x | 30,063 | **0.160x** | 381 | **12.606x** | 722.1 [678-754] | 102.2 | 114.2 |
| messages page | 1 | 1,407 [1,395-1,506] | 8,312 | 0.169x | 6,822 | **0.206x** | 218 | **6.459x** | 806.2 [763-816] | 199.3 | 139.2 |
| sidebar | 16 | 5,078 [4,886-5,348] | 37,555 | 0.135x | 28,238 | **0.180x** | 599 | **8.483x** | 685.5 [643-704] | 101.8 | 123.2 |
| sidebar | 1 | 1,585 [1,544-1,661] | 8,000 | 0.198x | 6,196 | **0.256x** | 287 | **5.530x** | 728.4 [702-747] | 181.0 | 149.0 |
| search | 16 | 4,767 [4,494-4,852] | 34,070 | 0.140x | 26,606 | **0.179x** | 408 | **11.687x** | 738.1 [723-788] | 107.3 | 123.9 |
| search | 1 | 1,298 [1,290-1,324] | 6,055 | 0.214x | 5,646 | **0.230x** | 193 | **6.733x** | 868.9 [852-871] | 315.1 | 158.3 |
| post a message | 16 | 1,774 [1,594-1,804] | 8,905 | 0.199x | 7,174 | **0.247x** | 330 | **5.374x** | 1,749.3 [1,712-1,962] | 353.0 | 385.6 |
| post a message | 1 | 852 [845-865] | 3,052 | 0.279x | 2,779 | **0.307x** | 167 | **5.107x** | 2,016.0 [2,010-2,044] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
