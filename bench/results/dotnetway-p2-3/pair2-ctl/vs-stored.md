theDotNetWay in bench/results/dotnetway-p2-3/pair2-ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 4,283 [4,272-4,293] | 37,555 | 0.114x | 28,238 | **0.152x** | 599 | **7.154x** | 809.5 [806-813] | 101.8 | 123.2 |
| sidebar | 1 | 1,427 [1,425-1,430] | 8,000 | 0.178x | 6,196 | **0.230x** | 287 | **4.981x** | 782.5 [782-783] | 181.0 | 149.0 |
| post a message | 16 | 1,376 [1,373-1,379] | 8,905 | 0.155x | 7,174 | **0.192x** | 330 | **4.168x** | 2,312.1 [2,303-2,321] | 353.0 | 385.6 |
| post a message | 1 | 795 [792-798] | 3,052 | 0.260x | 2,779 | **0.286x** | 167 | **4.762x** | 2,179.4 [2,171-2,188] | 629.1 | 403.7 |

Checks:
  note post_message c=1: host probe 1.107 after the window (noisy host)
  note post_message c=1: host probe 1.121 after the window (noisy host)
  note post_message c=16: host probe 1.103 after the window (noisy host)
  note sidebar c=16: host probe 1.109 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 8 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
