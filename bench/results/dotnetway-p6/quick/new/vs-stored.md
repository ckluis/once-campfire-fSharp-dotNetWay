theDotNetWay in bench/results/dotnetway-p6/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,454 [7,398-7,621] | 40,422 | 0.184x | 28,623 | **0.260x** | 239 | **31.147x** | 435.3 [433-440] | 93.6 | 122.4 |
| room page | 1 | 2,496 [2,496-2,567] | 7,617 | 0.328x | 6,481 | **0.385x** | 107 | **23.310x** | 493.8 [485-500] | 208.7 | 142.1 |
| messages page | 16 | 7,398 [7,385-7,474] | 37,836 | 0.196x | 30,063 | **0.246x** | 381 | **19.437x** | 450.8 [450-452] | 102.2 | 114.2 |
| messages page | 1 | 2,344 [2,324-2,380] | 8,312 | 0.282x | 6,822 | **0.344x** | 218 | **10.759x** | 521.8 [515-528] | 199.3 | 139.2 |
| sidebar | 16 | 7,667 [7,654-7,830] | 37,555 | 0.204x | 28,238 | **0.272x** | 599 | **12.809x** | 427.9 [427-430] | 101.8 | 123.2 |
| sidebar | 1 | 2,474 [2,471-2,490] | 8,000 | 0.309x | 6,196 | **0.399x** | 287 | **8.634x** | 498.8 [495-499] | 181.0 | 149.0 |
| search | 16 | 7,004 [6,474-7,107] | 34,070 | 0.206x | 26,606 | **0.263x** | 408 | **17.170x** | 480.4 [475-530] | 107.3 | 123.9 |
| search | 1 | 2,228 [2,135-2,246] | 6,055 | 0.368x | 5,646 | **0.395x** | 193 | **11.563x** | 542.5 [541-563] | 315.1 | 158.3 |
| post a message | 16 | 1,997 [1,888-2,002] | 8,905 | 0.224x | 7,174 | **0.278x** | 330 | **6.048x** | 1,496.6 [1,492-1,620] | 353.0 | 385.6 |
| post a message | 1 | 1,106 [1,073-1,116] | 3,052 | 0.362x | 2,779 | **0.398x** | 167 | **6.626x** | 1,645.1 [1,645-1,724] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes messages_page: 346364 vs expected 345884
  FAIL decoded bytes sidebar: 30144 vs expected 29710
  FAIL decoded bytes search: 137229 vs expected 136633
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes messages_page: 346364 vs expected 345884
  FAIL decoded bytes sidebar: 30144 vs expected 29710
  FAIL decoded bytes search: 137229 vs expected 136633
  FAIL decoded bytes room_show: 377518 vs expected 376570
  FAIL decoded bytes messages_page: 346364 vs expected 345884
  FAIL decoded bytes sidebar: 30144 vs expected 29710
  FAIL decoded bytes search: 137229 vs expected 136633
