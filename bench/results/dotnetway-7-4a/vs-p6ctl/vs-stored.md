theDotNetWay in bench/results/dotnetway-7-4a/vs-p6ctl against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 5,988 [3,656-7,080] | 40,422 | 0.148x | 28,623 | **0.209x** | 239 | **25.022x** | 534.5 [474-907] | 93.6 | 122.4 |
| room page | 1 | 1,838 [1,016-2,004] | 7,617 | 0.241x | 6,481 | **0.284x** | 107 | **17.165x** | 646.5 [609-1,089] | 208.7 | 142.1 |
| messages page | 16 | 6,140 [5,237-6,736] | 37,836 | 0.162x | 30,063 | **0.204x** | 381 | **16.133x** | 548.5 [499-612] | 102.2 | 114.2 |
| messages page | 1 | 1,964 [1,074-1,991] | 8,312 | 0.236x | 6,822 | **0.288x** | 218 | **9.013x** | 619.1 [612-1,035] | 199.3 | 139.2 |
| sidebar | 16 | 6,832 [6,371-7,244] | 37,555 | 0.182x | 28,238 | **0.242x** | 599 | **11.414x** | 486.8 [468-528] | 101.8 | 123.2 |
| sidebar | 1 | 2,051 [1,764-2,086] | 8,000 | 0.256x | 6,196 | **0.331x** | 287 | **7.156x** | 600.2 [590-659] | 181.0 | 149.0 |
| search | 16 | 6,463 [5,932-6,694] | 34,070 | 0.190x | 26,606 | **0.243x** | 408 | **15.845x** | 538.8 [513-568] | 107.3 | 123.9 |
| search | 1 | 1,715 [1,581-1,842] | 6,055 | 0.283x | 5,646 | **0.304x** | 193 | **8.901x** | 694.0 [656-723] | 315.1 | 158.3 |
| post a message | 16 | 1,855 [1,098-2,028] | 8,905 | 0.208x | 7,174 | **0.259x** | 330 | **5.620x** | 1,606.9 [1,593-2,909] | 353.0 | 385.6 |
| post a message | 1 | 966 [905-1,003] | 3,052 | 0.317x | 2,779 | **0.348x** | 167 | **5.788x** | 1,856.0 [1,801-1,946] | 629.1 | 403.7 |

Checks:
  note messages_page c=1: warm-up did not settle (reached the maximum)
  note messages_page c=1: host probe 1.209 after the window (noisy host)
  note messages_page c=16: warm-up did not settle (reached the maximum)
  note messages_page c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: host probe 1.274 after the window (noisy host)
  note room_show c=1: host probe 1.267 after the window (noisy host)
  note room_show c=16: host probe 1.106 after the window (noisy host)
  note room_show c=16: warm-up did not settle (reached the maximum)
  note room_show c=16: host probe 1.192 after the window (noisy host)
  note search c=16: host probe 1.102 after the window (noisy host)
  note search c=16: warm-up did not settle (reached the maximum)
  note sidebar c=1: host probe 1.101 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 36 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
