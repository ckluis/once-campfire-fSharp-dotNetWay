theDotNetWay in bench/results/dotnetway-p6/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,364 [7,161-7,438] | 40,422 | 0.182x | 28,623 | **0.257x** | 239 | **30.775x** | 439.1 [435-455] | 93.6 | 122.4 |
| room page | 1 | 2,513 [2,483-2,569] | 7,617 | 0.330x | 6,481 | **0.388x** | 107 | **23.465x** | 497.1 [486-499] | 208.7 | 142.1 |
| messages page | 16 | 7,292 [6,937-7,390] | 37,836 | 0.193x | 30,063 | **0.243x** | 381 | **19.158x** | 454.7 [449-484] | 102.2 | 114.2 |
| messages page | 1 | 2,315 [2,267-2,331] | 8,312 | 0.279x | 6,822 | **0.339x** | 218 | **10.625x** | 529.5 [524-538] | 199.3 | 139.2 |
| sidebar | 16 | 7,652 [7,330-7,677] | 37,555 | 0.204x | 28,238 | **0.271x** | 599 | **12.784x** | 429.3 [426-447] | 101.8 | 123.2 |
| sidebar | 1 | 2,468 [2,421-2,470] | 8,000 | 0.309x | 6,196 | **0.398x** | 287 | **8.612x** | 501.9 [500-509] | 181.0 | 149.0 |
| search | 16 | 7,049 [6,968-7,081] | 34,070 | 0.207x | 26,606 | **0.265x** | 408 | **17.281x** | 479.9 [479-483] | 107.3 | 123.9 |
| search | 1 | 2,201 [2,188-2,208] | 6,055 | 0.363x | 5,646 | **0.390x** | 193 | **11.420x** | 549.9 [549-554] | 315.1 | 158.3 |
| post a message | 16 | 2,012 [1,985-2,021] | 8,905 | 0.226x | 7,174 | **0.281x** | 330 | **6.096x** | 1,491.2 [1,484-1,512] | 353.0 | 385.6 |
| post a message | 1 | 1,108 [1,101-1,114] | 3,052 | 0.363x | 2,779 | **0.399x** | 167 | **6.638x** | 1,663.5 [1,661-1,677] | 629.1 | 403.7 |

Checks:
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
