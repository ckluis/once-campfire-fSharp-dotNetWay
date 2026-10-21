theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-4-9 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,507 [7,492-7,521] | 40,422 | 0.186x | 28,623 | **0.262x** | 239 | **31.370x** | 436.4 [434-439] | 93.6 | 122.4 |
| room page | 1 | 2,523 [2,509-2,537] | 7,617 | 0.331x | 6,481 | **0.389x** | 107 | **23.555x** | 494.3 [494-495] | 208.7 | 142.1 |

Checks:
  responses: every one 2xx with 0 connection errors in 4 runs
  FAIL decoded bytes room_show: 376810 vs Rust 416122
  FAIL decoded bytes room_show: 376810 vs Rust 416122
