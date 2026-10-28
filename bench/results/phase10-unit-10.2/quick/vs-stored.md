theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/phase10-unit-10.2/quick against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 9,811 [9,747-9,962] | 40,422 | 0.243x | 28,623 | **0.343x** | 239 | **41.000x** | 355.5 [350-357] | 93.6 | 122.4 |
| room page | 1 | 2,466 [2,461-2,506] | 7,617 | 0.324x | 6,481 | **0.380x** | 107 | **23.024x** | 522.4 [516-523] | 208.7 | 142.1 |
| messages page | 16 | 9,613 [9,520-9,620] | 37,836 | 0.254x | 30,063 | **0.320x** | 381 | **25.258x** | 373.9 [372-377] | 102.2 | 114.2 |
| messages page | 1 | 2,289 [2,282-2,304] | 8,312 | 0.275x | 6,822 | **0.336x** | 218 | **10.506x** | 551.6 [551-556] | 199.3 | 139.2 |
| sidebar | 16 | 10,226 [9,935-10,419] | 37,555 | 0.272x | 28,238 | **0.362x** | 599 | **17.084x** | 347.3 [341-357] | 101.8 | 123.2 |
| sidebar | 1 | 2,422 [2,391-2,452] | 8,000 | 0.303x | 6,196 | **0.391x** | 287 | **8.453x** | 527.9 [524-537] | 181.0 | 149.0 |
| search | 16 | 8,566 [8,528-8,611] | 34,070 | 0.251x | 26,606 | **0.322x** | 408 | **21.001x** | 419.4 [418-422] | 107.3 | 123.9 |
| search | 1 | 2,125 [2,102-2,132] | 6,055 | 0.351x | 5,646 | **0.376x** | 193 | **11.030x** | 591.4 [587-594] | 315.1 | 158.3 |
| post a message | 16 | 2,400 [2,398-2,433] | 8,905 | 0.269x | 7,174 | **0.335x** | 330 | **7.270x** | 1,324.4 [1,318-1,337] | 353.0 | 385.6 |
| post a message | 1 | 1,246 [1,236-1,257] | 3,052 | 0.408x | 2,779 | **0.448x** | 167 | **7.466x** | 1,540.2 [1,540-1,545] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
