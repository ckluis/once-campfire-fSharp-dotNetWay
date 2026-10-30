theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-6-quick against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 33,412 [33,036-33,487] | 40,422 | 0.827x | 28,623 | **1.167x** | 239 | **139.624x** | 96.1 [95-96] | 93.6 | 122.4 |
| room page | 1 | 6,416 [5,931-6,679] | 7,617 | 0.842x | 6,481 | **0.990x** | 107 | **59.902x** | 271.0 [265-283] | 208.7 | 142.1 |
| messages page | 16 | 31,762 [31,491-31,896] | 37,836 | 0.839x | 30,063 | **1.057x** | 381 | **83.453x** | 100.1 [100-101] | 102.2 | 114.2 |
| messages page | 1 | 6,289 [6,248-6,599] | 8,312 | 0.757x | 6,822 | **0.922x** | 218 | **28.861x** | 272.0 [266-273] | 199.3 | 139.2 |
| sidebar | 16 | 39,560 [38,704-39,692] | 37,555 | 1.053x | 28,238 | **1.401x** | 599 | **66.088x** | 78.6 [78-80] | 101.8 | 123.2 |
| sidebar | 1 | 7,138 [7,135-7,197] | 8,000 | 0.892x | 6,196 | **1.152x** | 287 | **24.905x** | 254.1 [252-255] | 181.0 | 149.0 |
| search | 16 | 39,313 [38,726-39,374] | 34,070 | 1.154x | 26,606 | **1.478x** | 408 | **96.379x** | 79.4 [79-80] | 107.3 | 123.9 |
| search | 1 | 7,127 [7,119-7,231] | 6,055 | 1.177x | 5,646 | **1.262x** | 193 | **36.987x** | 253.9 [252-255] | 315.1 | 158.3 |
| post a message | 16 | 2,474 [2,419-2,486] | 8,905 | 0.278x | 7,174 | **0.345x** | 330 | **7.493x** | 1,315.1 [1,312-1,339] | 353.0 | 385.6 |
| post a message | 1 | 1,210 [1,201-1,233] | 3,052 | 0.397x | 2,779 | **0.435x** | 167 | **7.251x** | 1,562.5 [1,548-1,595] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
