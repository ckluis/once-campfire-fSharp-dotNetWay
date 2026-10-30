theDotNetWay in bench/results/dotnetway-4-11/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,386 [7,333-7,395] | 40,422 | 0.183x | 28,623 | **0.258x** | 239 | **30.864x** | 440.4 [438-441] | 93.6 | 122.4 |
| room page | 1 | 2,502 [2,485-2,535] | 7,617 | 0.328x | 6,481 | **0.386x** | 107 | **23.359x** | 498.2 [492-500] | 208.7 | 142.1 |
| messages page | 16 | 7,301 [7,239-7,319] | 37,836 | 0.193x | 30,063 | **0.243x** | 381 | **19.182x** | 455.5 [454-460] | 102.2 | 114.2 |
| messages page | 1 | 2,298 [2,278-2,304] | 8,312 | 0.277x | 6,822 | **0.337x** | 218 | **10.547x** | 533.5 [531-536] | 199.3 | 139.2 |
| sidebar | 16 | 7,574 [7,534-7,624] | 37,555 | 0.202x | 28,238 | **0.268x** | 599 | **12.652x** | 432.7 [430-434] | 101.8 | 123.2 |
| sidebar | 1 | 2,421 [2,397-2,428] | 8,000 | 0.303x | 6,196 | **0.391x** | 287 | **8.448x** | 508.4 [506-513] | 181.0 | 149.0 |
| search | 16 | 7,019 [6,984-7,033] | 34,070 | 0.206x | 26,606 | **0.264x** | 408 | **17.208x** | 478.4 [478-483] | 107.3 | 123.9 |
| search | 1 | 2,191 [2,174-2,213] | 6,055 | 0.362x | 5,646 | **0.388x** | 193 | **11.369x** | 550.3 [550-555] | 315.1 | 158.3 |
| post a message | 16 | 2,058 [2,041-2,064] | 8,905 | 0.231x | 7,174 | **0.287x** | 330 | **6.235x** | 1,448.6 [1,448-1,453] | 353.0 | 385.6 |
| post a message | 1 | 1,154 [1,116-1,155] | 3,052 | 0.378x | 2,779 | **0.415x** | 167 | **6.916x** | 1,627.9 [1,626-1,632] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
