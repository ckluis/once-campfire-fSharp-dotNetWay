theDotNetWay in bench/results/dotnetway-4-2/ctl2 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,767 [7,712-7,808] | 37,555 | 0.207x | 28,238 | **0.275x** | 599 | **12.976x** | 435.0 [432-436] | 101.8 | 123.2 |
| sidebar | 1 | 2,422 [2,390-2,426] | 8,000 | 0.303x | 6,196 | **0.391x** | 287 | **8.452x** | 510.0 [504-516] | 181.0 | 149.0 |

Checks:
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
