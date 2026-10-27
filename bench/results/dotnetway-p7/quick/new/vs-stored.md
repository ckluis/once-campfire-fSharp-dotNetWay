theDotNetWay in bench/results/dotnetway-p7/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,234 [7,188-7,254] | 40,422 | 0.179x | 28,623 | **0.253x** | 239 | **30.232x** | 456.4 [455-459] | 93.6 | 122.4 |
| room page | 1 | 2,227 [2,186-2,239] | 7,617 | 0.292x | 6,481 | **0.344x** | 107 | **20.795x** | 563.2 [559-571] | 208.7 | 142.1 |
| messages page | 16 | 7,106 [7,106-7,117] | 37,836 | 0.188x | 30,063 | **0.236x** | 381 | **18.671x** | 475.5 [473-476] | 102.2 | 114.2 |
| messages page | 1 | 2,085 [2,045-2,085] | 8,312 | 0.251x | 6,822 | **0.306x** | 218 | **9.567x** | 593.9 [592-607] | 199.3 | 139.2 |
| sidebar | 16 | 7,357 [7,173-7,373] | 37,555 | 0.196x | 28,238 | **0.261x** | 599 | **12.290x** | 454.0 [452-466] | 101.8 | 123.2 |
| sidebar | 1 | 2,224 [2,217-2,234] | 8,000 | 0.278x | 6,196 | **0.359x** | 287 | **7.759x** | 571.5 [562-576] | 181.0 | 149.0 |
| search | 16 | 6,801 [6,614-6,933] | 34,070 | 0.200x | 26,606 | **0.256x** | 408 | **16.672x** | 504.8 [495-522] | 107.3 | 123.9 |
| search | 1 | 1,913 [1,885-1,949] | 6,055 | 0.316x | 5,646 | **0.339x** | 193 | **9.927x** | 639.0 [626-650] | 315.1 | 158.3 |
| post a message | 16 | 2,113 [2,097-2,149] | 8,905 | 0.237x | 7,174 | **0.295x** | 330 | **6.401x** | 1,513.4 [1,500-1,518] | 353.0 | 385.6 |
| post a message | 1 | 1,074 [1,023-1,079] | 3,052 | 0.352x | 2,779 | **0.386x** | 167 | **6.434x** | 1,726.1 [1,725-1,750] | 629.1 | 403.7 |

Checks:
  note avatar c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
