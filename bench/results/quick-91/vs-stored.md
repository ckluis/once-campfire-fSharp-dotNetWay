theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/quick-91 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,501 [7,288-7,502] | 40,422 | 0.186x | 28,623 | **0.262x** | 239 | **31.346x** | 451.4 [451-466] | 93.6 | 122.4 |
| room page | 1 | 2,236 [2,185-2,263] | 7,617 | 0.294x | 6,481 | **0.345x** | 107 | **20.880x** | 559.8 [551-570] | 208.7 | 142.1 |

Checks:
  responses: every one 2xx with 0 connection errors in 12 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
