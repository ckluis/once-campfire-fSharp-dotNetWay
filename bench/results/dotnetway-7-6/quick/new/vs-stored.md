theDotNetWay in bench/results/dotnetway-7-6/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 6,638 [6,609-6,722] | 40,422 | 0.164x | 28,623 | **0.232x** | 239 | **27.739x** | 492.5 [488-493] | 93.6 | 122.4 |
| room page | 1 | 2,104 [2,071-2,108] | 7,617 | 0.276x | 6,481 | **0.325x** | 107 | **19.641x** | 589.0 [588-595] | 208.7 | 142.1 |
| messages page | 16 | 6,541 [6,521-6,621] | 37,836 | 0.173x | 30,063 | **0.218x** | 381 | **17.185x** | 507.4 [503-514] | 102.2 | 114.2 |
| messages page | 1 | 1,960 [1,945-1,974] | 8,312 | 0.236x | 6,822 | **0.287x** | 218 | **8.994x** | 623.7 [620-632] | 199.3 | 139.2 |
| sidebar | 16 | 6,685 [6,552-6,865] | 37,555 | 0.178x | 28,238 | **0.237x** | 599 | **11.167x** | 501.8 [486-503] | 101.8 | 123.2 |
| sidebar | 1 | 2,031 [2,027-2,043] | 8,000 | 0.254x | 6,196 | **0.328x** | 287 | **7.086x** | 609.3 [607-612] | 181.0 | 149.0 |
| search | 16 | 6,055 [6,014-6,403] | 34,070 | 0.178x | 26,606 | **0.228x** | 408 | **14.844x** | 565.4 [536-568] | 107.3 | 123.9 |
| search | 1 | 1,770 [1,746-1,780] | 6,055 | 0.292x | 5,646 | **0.314x** | 193 | **9.187x** | 680.7 [678-688] | 315.1 | 158.3 |
| post a message | 16 | 1,992 [1,978-2,029] | 8,905 | 0.224x | 7,174 | **0.278x** | 330 | **6.034x** | 1,618.0 [1,605-1,623] | 353.0 | 385.6 |
| post a message | 1 | 999 [997-1,017] | 3,052 | 0.327x | 2,779 | **0.360x** | 167 | **5.987x** | 1,805.0 [1,800-1,820] | 629.1 | 403.7 |

Checks:
  note avatar c=16: warm-up did not settle (reached the maximum)
  note up c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
