theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p10/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,228 [7,031-7,300] | 40,422 | 0.179x | 28,623 | **0.253x** | 239 | **30.207x** | 452.9 [449-472] | 93.6 | 122.4 |
| room page | 1 | 2,198 [2,191-2,252] | 7,617 | 0.289x | 6,481 | **0.339x** | 107 | **20.525x** | 565.6 [559-568] | 208.7 | 142.1 |
| messages page | 16 | 7,165 [6,792-7,171] | 37,836 | 0.189x | 30,063 | **0.238x** | 381 | **18.826x** | 468.8 [468-501] | 102.2 | 114.2 |
| messages page | 1 | 2,024 [2,004-2,062] | 8,312 | 0.244x | 6,822 | **0.297x** | 218 | **9.288x** | 607.5 [599-610] | 199.3 | 139.2 |
| sidebar | 16 | 7,410 [7,153-7,464] | 37,555 | 0.197x | 28,238 | **0.262x** | 599 | **12.379x** | 445.9 [444-468] | 101.8 | 123.2 |
| sidebar | 1 | 2,120 [2,078-2,174] | 8,000 | 0.265x | 6,196 | **0.342x** | 287 | **7.398x** | 584.5 [574-593] | 181.0 | 149.0 |
| search | 16 | 7,023 [6,989-7,061] | 34,070 | 0.206x | 26,606 | **0.264x** | 408 | **17.217x** | 488.7 [486-489] | 107.3 | 123.9 |
| search | 1 | 2,009 [1,882-2,010] | 6,055 | 0.332x | 5,646 | **0.356x** | 193 | **10.424x** | 624.5 [612-648] | 315.1 | 158.3 |
| post a message | 16 | 2,166 [2,159-2,200] | 8,905 | 0.243x | 7,174 | **0.302x** | 330 | **6.563x** | 1,487.9 [1,463-1,496] | 353.0 | 385.6 |
| post a message | 1 | 1,085 [1,083-1,099] | 3,052 | 0.356x | 2,779 | **0.391x** | 167 | **6.503x** | 1,690.7 [1,673-1,696] | 629.1 | 403.7 |

Checks:
  note avatar c=16: warm-up did not settle (reached the maximum)
  note up c=1: warm-up did not settle (reached the maximum)
  note up c=1: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
