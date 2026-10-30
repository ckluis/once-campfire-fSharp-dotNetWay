theDotNetWay in bench/results/dotnetway-hybrid-2/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,823 [5,811-5,988] | 40,422 | 0.144x | 28,623 | **0.203x** | 239 | **24.332x** | 588.9 [586-592] | 93.6 | 122.4 |
| room page | 1 | 1,819 [1,802-1,820] | 7,617 | 0.239x | 6,481 | **0.281x** | 107 | **16.986x** | 647.4 [647-652] | 208.7 | 142.1 |
| messages page | 16 | 5,305 [5,302-5,418] | 37,836 | 0.140x | 30,063 | **0.176x** | 381 | **13.940x** | 651.3 [649-652] | 102.2 | 114.2 |
| messages page | 1 | 1,592 [1,575-1,603] | 8,312 | 0.192x | 6,822 | **0.233x** | 218 | **7.306x** | 725.1 [721-731] | 199.3 | 139.2 |
| sidebar | 16 | 5,824 [5,795-5,983] | 37,555 | 0.155x | 28,238 | **0.206x** | 599 | **9.729x** | 591.9 [588-596] | 101.8 | 123.2 |
| sidebar | 1 | 1,706 [1,682-1,708] | 8,000 | 0.213x | 6,196 | **0.275x** | 287 | **5.952x** | 686.1 [685-693] | 181.0 | 149.0 |
| search | 16 | 5,224 [5,212-5,244] | 34,070 | 0.153x | 26,606 | **0.196x** | 408 | **12.806x** | 673.1 [671-675] | 107.3 | 123.9 |
| search | 1 | 1,556 [1,544-1,576] | 6,055 | 0.257x | 5,646 | **0.276x** | 193 | **8.077x** | 738.7 [732-744] | 315.1 | 158.3 |
| post a message | 16 | 1,907 [1,898-1,913] | 8,905 | 0.214x | 7,174 | **0.266x** | 330 | **5.777x** | 1,609.0 [1,590-1,616] | 353.0 | 385.6 |
| post a message | 1 | 974 [973-978] | 3,052 | 0.319x | 2,779 | **0.350x** | 167 | **5.833x** | 1,799.2 [1,799-1,813] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
