theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/quick-87 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,241 [6,958-7,267] | 40,422 | 0.179x | 28,623 | **0.253x** | 239 | **30.257x** | 455.2 [453-460] | 93.6 | 122.4 |
| room page | 1 | 2,235 [2,182-2,259] | 7,617 | 0.293x | 6,481 | **0.345x** | 107 | **20.868x** | 559.3 [558-567] | 208.7 | 142.1 |
| messages page | 16 | 7,121 [7,024-7,140] | 37,836 | 0.188x | 30,063 | **0.237x** | 381 | **18.710x** | 470.7 [470-474] | 102.2 | 114.2 |
| messages page | 1 | 2,046 [1,951-2,102] | 8,312 | 0.246x | 6,822 | **0.300x** | 218 | **9.390x** | 598.8 [589-626] | 199.3 | 139.2 |
| sidebar | 16 | 7,353 [7,321-7,430] | 37,555 | 0.196x | 28,238 | **0.260x** | 599 | **12.283x** | 449.5 [447-454] | 101.8 | 123.2 |
| sidebar | 1 | 2,169 [2,143-2,178] | 8,000 | 0.271x | 6,196 | **0.350x** | 287 | **7.568x** | 571.9 [572-577] | 181.0 | 149.0 |
| search | 16 | 6,906 [6,823-6,909] | 34,070 | 0.203x | 26,606 | **0.260x** | 408 | **16.930x** | 495.0 [495-501] | 107.3 | 123.9 |
| search | 1 | 1,906 [1,860-1,916] | 6,055 | 0.315x | 5,646 | **0.338x** | 193 | **9.891x** | 638.3 [632-649] | 315.1 | 158.3 |
| post a message | 16 | 2,139 [2,121-2,155] | 8,905 | 0.240x | 7,174 | **0.298x** | 330 | **6.480x** | 1,506.7 [1,498-1,508] | 353.0 | 385.6 |
| post a message | 1 | 1,067 [1,059-1,069] | 3,052 | 0.350x | 2,779 | **0.384x** | 167 | **6.391x** | 1,794.8 [1,789-1,811] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
