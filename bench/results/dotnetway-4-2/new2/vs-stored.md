theDotNetWay in bench/results/dotnetway-4-2/new2 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,748 [7,681-7,764] | 37,555 | 0.206x | 28,238 | **0.274x** | 599 | **12.944x** | 435.6 [435-439] | 101.8 | 123.2 |
| sidebar | 1 | 2,401 [2,385-2,431] | 8,000 | 0.300x | 6,196 | **0.388x** | 287 | **8.379x** | 504.7 [505-519] | 181.0 | 149.0 |

Checks:
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
