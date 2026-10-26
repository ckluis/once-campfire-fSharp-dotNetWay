theDotNetWay in bench/results/dotnetway-7-1/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,350 [7,349-7,718] | 40,422 | 0.182x | 28,623 | **0.257x** | 239 | **30.714x** | 438.1 [428-439] | 93.6 | 122.4 |
| room page | 1 | 2,483 [2,439-2,487] | 7,617 | 0.326x | 6,481 | **0.383x** | 107 | **23.180x** | 499.6 [499-505] | 208.7 | 142.1 |
| messages page | 16 | 7,405 [7,354-7,649] | 37,836 | 0.196x | 30,063 | **0.246x** | 381 | **19.456x** | 446.3 [442-449] | 102.2 | 114.2 |
| messages page | 1 | 2,326 [2,271-2,342] | 8,312 | 0.280x | 6,822 | **0.341x** | 218 | **10.676x** | 524.8 [521-534] | 199.3 | 139.2 |
| sidebar | 16 | 7,791 [7,598-7,979] | 37,555 | 0.207x | 28,238 | **0.276x** | 599 | **13.015x** | 419.9 [420-432] | 101.8 | 123.2 |
| sidebar | 1 | 2,443 [2,418-2,455] | 8,000 | 0.305x | 6,196 | **0.394x** | 287 | **8.523x** | 502.9 [501-506] | 181.0 | 149.0 |
| search | 16 | 6,977 [6,108-7,009] | 34,070 | 0.205x | 26,606 | **0.262x** | 408 | **17.106x** | 481.3 [478-563] | 107.3 | 123.9 |
| search | 1 | 2,155 [1,993-2,166] | 6,055 | 0.356x | 5,646 | **0.382x** | 193 | **11.184x** | 558.7 [556-597] | 315.1 | 158.3 |
| post a message | 16 | 1,982 [1,820-1,992] | 8,905 | 0.223x | 7,174 | **0.276x** | 330 | **6.004x** | 1,504.6 [1,497-1,697] | 353.0 | 385.6 |
| post a message | 1 | 1,096 [1,011-1,101] | 3,052 | 0.359x | 2,779 | **0.394x** | 167 | **6.568x** | 1,670.9 [1,658-1,811] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
