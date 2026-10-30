theDotNetWay in bench/results/dotnetway-p8/quick/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,202 [7,088-7,202] | 40,422 | 0.178x | 28,623 | **0.252x** | 239 | **30.094x** | 457.7 [455-463] | 93.6 | 122.4 |
| room page | 1 | 2,232 [2,217-2,257] | 7,617 | 0.293x | 6,481 | **0.344x** | 107 | **20.839x** | 563.9 [556-564] | 208.7 | 142.1 |
| messages page | 16 | 7,083 [7,077-7,087] | 37,836 | 0.187x | 30,063 | **0.236x** | 381 | **18.610x** | 473.9 [474-476] | 102.2 | 114.2 |
| messages page | 1 | 2,072 [2,058-2,101] | 8,312 | 0.249x | 6,822 | **0.304x** | 218 | **9.507x** | 597.3 [589-599] | 199.3 | 139.2 |
| sidebar | 16 | 7,373 [7,027-7,381] | 37,555 | 0.196x | 28,238 | **0.261x** | 599 | **12.318x** | 451.0 [450-478] | 101.8 | 123.2 |
| sidebar | 1 | 2,240 [2,135-2,243] | 8,000 | 0.280x | 6,196 | **0.361x** | 287 | **7.814x** | 560.6 [558-579] | 181.0 | 149.0 |
| search | 16 | 6,856 [6,518-6,893] | 34,070 | 0.201x | 26,606 | **0.258x** | 408 | **16.808x** | 499.1 [499-531] | 107.3 | 123.9 |
| search | 1 | 1,910 [1,758-1,962] | 6,055 | 0.316x | 5,646 | **0.338x** | 193 | **9.914x** | 635.0 [625-686] | 315.1 | 158.3 |
| post a message | 16 | 2,135 [2,014-2,141] | 8,905 | 0.240x | 7,174 | **0.298x** | 330 | **6.467x** | 1,519.3 [1,506-1,615] | 353.0 | 385.6 |
| post a message | 1 | 1,028 [1,018-1,052] | 3,052 | 0.337x | 2,779 | **0.370x** | 167 | **6.159x** | 1,827.5 [1,801-1,839] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 48 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
