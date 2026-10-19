theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-10/quick against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 4,405 | 40,422 | 0.109x | 28,623 | **0.154x** | 239 | **18.409x** | 794.1 | 93.6 | 122.4 |
| room page | 1 | 1,288 | 7,617 | 0.169x | 6,481 | **0.199x** | 107 | **12.026x** | 869.9 | 208.7 | 142.1 |
| messages page | 16 | 4,379 | 37,836 | 0.116x | 30,063 | **0.146x** | 381 | **11.506x** | 797.9 | 102.2 | 114.2 |
| messages page | 1 | 1,246 | 8,312 | 0.150x | 6,822 | **0.183x** | 218 | **5.718x** | 899.7 | 199.3 | 139.2 |
| sidebar | 16 | 3,773 | 37,555 | 0.100x | 28,238 | **0.134x** | 599 | **6.303x** | 929.4 | 101.8 | 123.2 |
| sidebar | 1 | 1,075 | 8,000 | 0.134x | 6,196 | **0.174x** | 287 | **3.751x** | 1,026.8 | 181.0 | 149.0 |
| search | 16 | 4,612 | 34,070 | 0.135x | 26,606 | **0.173x** | 408 | **11.307x** | 761.1 | 107.3 | 123.9 |
| search | 1 | 1,287 | 6,055 | 0.213x | 5,646 | **0.228x** | 193 | **6.678x** | 873.3 | 315.1 | 158.3 |
| post a message | 16 | 693 | 8,905 | 0.078x | 7,174 | **0.097x** | 330 | **2.100x** | 4,114.4 | 353.0 | 385.6 |
| post a message | 1 | 544 | 3,052 | 0.178x | 2,779 | **0.196x** | 167 | **3.258x** | 2,865.2 | 629.1 | 403.7 |

Checks:
  note post_message c=16: host probe 1.144 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
