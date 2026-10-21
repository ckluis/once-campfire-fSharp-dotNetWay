theDotNetWay in bench/results/dotnetway-4-11/quick/ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 7,433 [7,365-7,657] | 40,422 | 0.184x | 28,623 | **0.260x** | 239 | **31.060x** | 436.3 [435-440] | 93.6 | 122.4 |
| room page | 1 | 2,512 [2,506-2,526] | 7,617 | 0.330x | 6,481 | **0.388x** | 107 | **23.455x** | 497.0 [495-499] | 208.7 | 142.1 |
| messages page | 16 | 7,402 [7,343-7,552] | 37,836 | 0.196x | 30,063 | **0.246x** | 381 | **19.449x** | 450.8 [449-454] | 102.2 | 114.2 |
| messages page | 1 | 2,306 [2,302-2,335] | 8,312 | 0.277x | 6,822 | **0.338x** | 218 | **10.581x** | 528.0 [526-532] | 199.3 | 139.2 |
| sidebar | 16 | 7,692 [7,575-7,936] | 37,555 | 0.205x | 28,238 | **0.272x** | 599 | **12.850x** | 427.0 [425-434] | 101.8 | 123.2 |
| sidebar | 1 | 2,452 [2,441-2,492] | 8,000 | 0.307x | 6,196 | **0.396x** | 287 | **8.557x** | 503.7 [497-505] | 181.0 | 149.0 |
| search | 16 | 7,075 [7,061-7,164] | 34,070 | 0.208x | 26,606 | **0.266x** | 408 | **17.345x** | 477.7 [475-478] | 107.3 | 123.9 |
| search | 1 | 2,205 [2,198-2,214] | 6,055 | 0.364x | 5,646 | **0.390x** | 193 | **11.441x** | 549.4 [545-553] | 315.1 | 158.3 |
| post a message | 16 | 2,048 [2,048-2,074] | 8,905 | 0.230x | 7,174 | **0.286x** | 330 | **6.205x** | 1,451.0 [1,444-1,464] | 353.0 | 385.6 |
| post a message | 1 | 1,154 [1,132-1,161] | 3,052 | 0.378x | 2,779 | **0.415x** | 167 | **6.915x** | 1,631.6 [1,620-1,647] | 629.1 | 403.7 |

Checks:
  note post_message c=16: host probe 1.102 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
