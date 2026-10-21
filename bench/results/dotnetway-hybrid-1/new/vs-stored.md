theDotNetWay in bench/results/dotnetway-hybrid-1/new against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,958 [5,829-5,974] | 40,422 | 0.147x | 28,623 | **0.208x** | 239 | **24.896x** | 589.1 [587-589] | 93.6 | 122.4 |
| room page | 1 | 1,814 [1,793-1,818] | 7,617 | 0.238x | 6,481 | **0.280x** | 107 | **16.936x** | 651.8 [649-657] | 208.7 | 142.1 |
| messages page | 16 | 5,340 [4,939-5,444] | 37,836 | 0.141x | 30,063 | **0.178x** | 381 | **14.030x** | 651.2 [648-716] | 102.2 | 114.2 |
| messages page | 1 | 1,593 [1,586-1,608] | 8,312 | 0.192x | 6,822 | **0.233x** | 218 | **7.310x** | 725.7 [720-728] | 199.3 | 139.2 |
| sidebar | 16 | 5,965 [5,865-5,980] | 37,555 | 0.159x | 28,238 | **0.211x** | 599 | **9.965x** | 589.3 [588-590] | 101.8 | 123.2 |
| sidebar | 1 | 1,686 [1,647-1,730] | 8,000 | 0.211x | 6,196 | **0.272x** | 287 | **5.882x** | 693.3 [680-701] | 181.0 | 149.0 |
| search | 16 | 5,244 [5,232-5,278] | 34,070 | 0.154x | 26,606 | **0.197x** | 408 | **12.856x** | 671.1 [667-672] | 107.3 | 123.9 |
| search | 1 | 1,573 [1,539-1,574] | 6,055 | 0.260x | 5,646 | **0.279x** | 193 | **8.163x** | 731.3 [729-747] | 315.1 | 158.3 |
| post a message | 16 | 1,928 [1,925-1,929] | 8,905 | 0.217x | 7,174 | **0.269x** | 330 | **5.841x** | 1,586.7 [1,585-1,597] | 353.0 | 385.6 |
| post a message | 1 | 973 [966-982] | 3,052 | 0.319x | 2,779 | **0.350x** | 167 | **5.830x** | 1,782.8 [1,783-1,810] | 629.1 | 403.7 |

Checks:
  note messages_page c=16: warm-up did not settle (reached the maximum)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
