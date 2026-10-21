theDotNetWay in bench/results/dotnetway-3-3/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,309 [8,275-8,350] | 40,422 | 0.206x | 28,623 | **0.290x** | 239 | **34.724x** | 395.4 [394-396] | 93.6 | 122.4 |
| room page | 1 | 2,766 [2,762-2,810] | 7,617 | 0.363x | 6,481 | **0.427x** | 107 | **25.825x** | 456.8 [451-458] | 208.7 | 142.1 |
| messages page | 16 | 7,620 [7,534-7,672] | 37,836 | 0.201x | 30,063 | **0.253x** | 381 | **20.022x** | 438.0 [436-442] | 102.2 | 114.2 |
| messages page | 1 | 2,424 [2,412-2,428] | 8,312 | 0.292x | 6,822 | **0.355x** | 218 | **11.123x** | 508.3 [506-510] | 199.3 | 139.2 |
| sidebar | 16 | 7,762 [7,749-7,815] | 37,555 | 0.207x | 28,238 | **0.275x** | 599 | **12.967x** | 434.2 [433-435] | 101.8 | 123.2 |
| sidebar | 1 | 2,341 [2,335-2,354] | 8,000 | 0.293x | 6,196 | **0.378x** | 287 | **8.169x** | 528.6 [524-530] | 181.0 | 149.0 |
| search | 16 | 7,556 [7,552-7,633] | 34,070 | 0.222x | 26,606 | **0.284x** | 408 | **18.525x** | 452.8 [449-454] | 107.3 | 123.9 |
| search | 1 | 2,347 [2,312-2,353] | 6,055 | 0.388x | 5,646 | **0.416x** | 193 | **12.177x** | 520.3 [515-528] | 315.1 | 158.3 |
| post a message | 16 | 2,077 [2,069-2,083] | 8,905 | 0.233x | 7,174 | **0.289x** | 330 | **6.291x** | 1,453.0 [1,445-1,454] | 353.0 | 385.6 |
| post a message | 1 | 1,142 [1,136-1,162] | 3,052 | 0.374x | 2,779 | **0.411x** | 167 | **6.845x** | 1,619.7 [1,609-1,639] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
