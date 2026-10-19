theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p1-10/quick3 against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,343 | 40,422 | 0.157x | 28,623 | **0.222x** | 239 | **26.505x** | 532.0 | 93.6 | 122.4 |
| room page | 1 | 1,789 | 7,617 | 0.235x | 6,481 | **0.276x** | 107 | **16.702x** | 650.2 | 208.7 | 142.1 |
| messages page | 16 | 5,196 | 37,836 | 0.137x | 30,063 | **0.173x** | 381 | **13.652x** | 664.9 | 102.2 | 114.2 |
| messages page | 1 | 1,502 | 8,312 | 0.181x | 6,822 | **0.220x** | 218 | **6.891x** | 762.2 | 199.3 | 139.2 |
| sidebar | 16 | 4,075 | 37,555 | 0.109x | 28,238 | **0.144x** | 599 | **6.808x** | 849.9 | 101.8 | 123.2 |
| sidebar | 1 | 1,180 | 8,000 | 0.148x | 6,196 | **0.190x** | 287 | **4.119x** | 942.5 | 181.0 | 149.0 |
| search | 16 | 5,036 | 34,070 | 0.148x | 26,606 | **0.189x** | 408 | **12.346x** | 692.1 | 107.3 | 123.9 |
| search | 1 | 1,388 | 6,055 | 0.229x | 5,646 | **0.246x** | 193 | **7.204x** | 815.3 | 315.1 | 158.3 |
| post a message | 16 | 1,807 | 8,905 | 0.203x | 7,174 | **0.252x** | 330 | **5.475x** | 1,671.5 | 353.0 | 385.6 |
| post a message | 1 | 913 | 3,052 | 0.299x | 2,779 | **0.329x** | 167 | **5.472x** | 1,900.0 | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 10 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
