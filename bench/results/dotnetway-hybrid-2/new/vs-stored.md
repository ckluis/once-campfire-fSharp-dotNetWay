theDotNetWay in bench/results/dotnetway-hybrid-2/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 8,305 [8,271-8,316] | 40,422 | 0.205x | 28,623 | **0.290x** | 239 | **34.707x** | 396.4 [396-398] | 93.6 | 122.4 |
| room page | 1 | 2,770 [2,760-2,782] | 7,617 | 0.364x | 6,481 | **0.427x** | 107 | **25.866x** | 456.6 [456-457] | 208.7 | 142.1 |
| messages page | 16 | 7,657 [7,560-7,672] | 37,836 | 0.202x | 30,063 | **0.255x** | 381 | **20.118x** | 436.3 [436-441] | 102.2 | 114.2 |
| messages page | 1 | 2,402 [2,398-2,437] | 8,312 | 0.289x | 6,822 | **0.352x** | 218 | **11.025x** | 511.8 [505-516] | 199.3 | 139.2 |
| sidebar | 16 | 7,793 [7,764-7,818] | 37,555 | 0.207x | 28,238 | **0.276x** | 599 | **13.018x** | 433.1 [433-434] | 101.8 | 123.2 |
| sidebar | 1 | 2,337 [2,316-2,366] | 8,000 | 0.292x | 6,196 | **0.377x** | 287 | **8.155x** | 527.7 [521-535] | 181.0 | 149.0 |
| search | 16 | 7,580 [7,572-7,594] | 34,070 | 0.222x | 26,606 | **0.285x** | 408 | **18.583x** | 451.3 [450-453] | 107.3 | 123.9 |
| search | 1 | 2,341 [2,329-2,344] | 6,055 | 0.387x | 5,646 | **0.415x** | 193 | **12.148x** | 521.4 [521-524] | 315.1 | 158.3 |
| post a message | 16 | 2,053 [2,047-2,062] | 8,905 | 0.231x | 7,174 | **0.286x** | 330 | **6.219x** | 1,447.8 [1,441-1,457] | 353.0 | 385.6 |
| post a message | 1 | 1,142 [1,138-1,148] | 3,052 | 0.374x | 2,779 | **0.411x** | 167 | **6.845x** | 1,621.2 [1,607-1,622] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
