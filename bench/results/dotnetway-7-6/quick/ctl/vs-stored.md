theDotNetWay in bench/results/dotnetway-7-6/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,943 [6,916-6,956] | 40,422 | 0.172x | 28,623 | **0.243x** | 239 | **29.012x** | 467.8 [468-469] | 93.6 | 122.4 |
| room page | 1 | 2,356 [2,349-2,372] | 7,617 | 0.309x | 6,481 | **0.364x** | 107 | **22.002x** | 525.2 [520-529] | 208.7 | 142.1 |
| messages page | 16 | 6,828 [6,825-6,852] | 37,836 | 0.180x | 30,063 | **0.227x** | 381 | **17.940x** | 486.0 [483-488] | 102.2 | 114.2 |
| messages page | 1 | 2,177 [2,164-2,189] | 8,312 | 0.262x | 6,822 | **0.319x** | 218 | **9.992x** | 558.2 [555-562] | 199.3 | 139.2 |
| sidebar | 16 | 7,168 [7,105-7,221] | 37,555 | 0.191x | 28,238 | **0.254x** | 599 | **11.974x** | 458.7 [457-459] | 101.8 | 123.2 |
| sidebar | 1 | 2,381 [2,373-2,396] | 8,000 | 0.298x | 6,196 | **0.384x** | 287 | **8.309x** | 517.7 [517-519] | 181.0 | 149.0 |
| search | 16 | 6,570 [6,531-6,593] | 34,070 | 0.193x | 26,606 | **0.247x** | 408 | **16.108x** | 513.5 [512-515] | 107.3 | 123.9 |
| search | 1 | 2,007 [1,995-2,008] | 6,055 | 0.331x | 5,646 | **0.356x** | 193 | **10.416x** | 595.0 [594-598] | 315.1 | 158.3 |
| post a message | 16 | 1,912 [1,896-2,070] | 8,905 | 0.215x | 7,174 | **0.267x** | 330 | **5.792x** | 1,578.5 [1,562-1,586] | 353.0 | 385.6 |
| post a message | 1 | 1,051 [1,049-1,052] | 3,052 | 0.344x | 2,779 | **0.378x** | 167 | **6.297x** | 1,729.2 [1,725-1,739] | 629.1 | 403.7 |

Checks:
  note post_message c=16: host probe 1.108 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
