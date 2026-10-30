theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p7-verify/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| sidebar | 16 | 7,380 [7,271-7,432] | 37,555 | 0.197x | 28,238 | **0.261x** | 599 | **12.328x** | 450.4 [448-458] | 101.8 | 123.2 |
| sidebar | 1 | 2,215 [2,206-2,248] | 8,000 | 0.277x | 6,196 | **0.358x** | 287 | **7.730x** | 563.0 [556-570] | 181.0 | 149.0 |
| search | 16 | 7,130 [6,700-7,207] | 34,070 | 0.209x | 26,606 | **0.268x** | 408 | **17.479x** | 493.8 [486-531] | 107.3 | 123.9 |
| search | 1 | 1,929 [1,837-1,999] | 6,055 | 0.319x | 5,646 | **0.342x** | 193 | **10.009x** | 631.3 [618-663] | 315.1 | 158.3 |
| post a message | 16 | 2,119 [2,112-2,123] | 8,905 | 0.238x | 7,174 | **0.295x** | 330 | **6.420x** | 1,532.2 [1,506-1,537] | 353.0 | 385.6 |
| post a message | 1 | 1,076 [1,046-1,087] | 3,052 | 0.353x | 2,779 | **0.387x** | 167 | **6.448x** | 1,716.6 [1,708-1,752] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 24 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
