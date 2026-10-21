theDotNetWay in bench/results/dotnetway-4-3/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,044 [7,976-8,372] | 40,422 | 0.199x | 28,623 | **0.281x** | 239 | **33.616x** | 411.9 [403-417] | 93.6 | 122.4 |
| room page | 1 | 2,650 [2,628-2,690] | 7,617 | 0.348x | 6,481 | **0.409x** | 107 | **24.746x** | 474.6 [469-477] | 208.7 | 142.1 |
| messages page | 16 | 7,422 [7,393-7,718] | 37,836 | 0.196x | 30,063 | **0.247x** | 381 | **19.501x** | 448.0 [440-450] | 102.2 | 114.2 |
| messages page | 1 | 2,362 [2,352-2,439] | 8,312 | 0.284x | 6,822 | **0.346x** | 218 | **10.840x** | 518.8 [505-520] | 199.3 | 139.2 |
| sidebar | 16 | 7,601 [7,585-7,987] | 37,555 | 0.202x | 28,238 | **0.269x** | 599 | **12.698x** | 444.5 [427-446] | 101.8 | 123.2 |
| sidebar | 1 | 2,298 [2,265-2,373] | 8,000 | 0.287x | 6,196 | **0.371x** | 287 | **8.017x** | 534.2 [527-541] | 181.0 | 149.0 |
| search | 16 | 6,955 [6,944-7,493] | 34,070 | 0.204x | 26,606 | **0.261x** | 408 | **17.052x** | 495.7 [464-498] | 107.3 | 123.9 |
| search | 1 | 2,220 [2,218-2,277] | 6,055 | 0.367x | 5,646 | **0.393x** | 193 | **11.518x** | 544.6 [533-546] | 315.1 | 158.3 |
| post a message | 16 | 1,974 [1,939-2,055] | 8,905 | 0.222x | 7,174 | **0.275x** | 330 | **5.981x** | 1,547.2 [1,459-1,567] | 353.0 | 385.6 |
| post a message | 1 | 1,095 [1,085-1,122] | 3,052 | 0.359x | 2,779 | **0.394x** | 167 | **6.558x** | 1,692.5 [1,646-1,696] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
