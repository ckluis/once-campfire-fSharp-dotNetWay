theDotNetWay in bench/results/dotnetway-p2-10/bench against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,387 [5,166-5,444] | 40,422 | 0.133x | 28,623 | **0.188x** | 239 | **22.510x** | 640.5 [635-675] | 93.6 | 122.4 |
| room page | 1 | 1,642 [1,608-1,674] | 7,617 | 0.216x | 6,481 | **0.253x** | 107 | **15.331x** | 709.3 [693-724] | 208.7 | 142.1 |
| messages page | 16 | 5,107 [4,846-5,144] | 37,836 | 0.135x | 30,063 | **0.170x** | 381 | **13.417x** | 679.4 [674-721] | 102.2 | 114.2 |
| messages page | 1 | 1,491 [1,430-1,528] | 8,312 | 0.179x | 6,822 | **0.219x** | 218 | **6.842x** | 769.6 [755-799] | 199.3 | 139.2 |
| sidebar | 16 | 5,579 [4,716-5,616] | 37,555 | 0.149x | 28,238 | **0.198x** | 599 | **9.321x** | 619.2 [618-736] | 101.8 | 123.2 |
| sidebar | 1 | 1,588 [1,471-1,597] | 8,000 | 0.199x | 6,196 | **0.256x** | 287 | **5.542x** | 729.6 [729-779] | 181.0 | 149.0 |
| search | 16 | 4,920 [4,682-4,970] | 34,070 | 0.144x | 26,606 | **0.185x** | 408 | **12.062x** | 709.3 [706-756] | 107.3 | 123.9 |
| search | 1 | 1,401 [1,366-1,408] | 6,055 | 0.231x | 5,646 | **0.248x** | 193 | **7.268x** | 814.4 [809-829] | 315.1 | 158.3 |
| post a message | 16 | 1,838 [1,806-1,870] | 8,905 | 0.206x | 7,174 | **0.256x** | 330 | **5.568x** | 1,652.8 [1,652-1,724] | 353.0 | 385.6 |
| post a message | 1 | 905 [864-908] | 3,052 | 0.297x | 2,779 | **0.326x** | 167 | **5.424x** | 1,926.6 [1,903-2,023] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
