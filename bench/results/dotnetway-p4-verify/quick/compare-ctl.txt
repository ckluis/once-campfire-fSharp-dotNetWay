theDotNetWay in bench/results/dotnetway-p4-verify/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,593 [7,542-7,629] | 37,555 | 0.202x | 28,238 | **0.269x** | 599 | **12.684x** | 433.2 [429-436] | 101.8 | 123.2 |
| sidebar | 1 | 2,552 [2,523-2,572] | 8,000 | 0.319x | 6,196 | **0.412x** | 287 | **8.905x** | 486.6 [486-491] | 181.0 | 149.0 |
| post a message | 16 | 2,070 [2,061-2,078] | 8,905 | 0.232x | 7,174 | **0.289x** | 330 | **6.270x** | 1,452.1 [1,436-1,454] | 353.0 | 385.6 |
| post a message | 1 | 1,152 [1,151-1,175] | 3,052 | 0.378x | 2,779 | **0.415x** | 167 | **6.904x** | 1,638.1 [1,620-1,644] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
