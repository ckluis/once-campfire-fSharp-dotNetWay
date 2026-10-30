theDotNetWay in /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/bench/results/dotnetway-p0/quick against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|
| room page | 16 | 30,751 [21,741-31,424] | 40,422 | 0.761x | 28,623 | **1.074x** | 239 | **128.504x** | 113.1 [110-156] | 93.6 | 122.4 |
| room page | 1 | 5,800 [4,794-7,032] | 7,617 | 0.762x | 6,481 | **0.895x** | 107 | **54.158x** | 230.2 [217-247] | 208.7 | 142.1 |
| messages page | 16 | 33,744 [18,930-35,357] | 37,836 | 0.892x | 30,063 | **1.122x** | 381 | **88.660x** | 100.8 [98-164] | 102.2 | 114.2 |
| messages page | 1 | 6,608 [5,740-7,386] | 8,312 | 0.795x | 6,822 | **0.969x** | 218 | **30.327x** | 217.9 [211-225] | 199.3 | 139.2 |
| sidebar | 16 | 31,306 [16,679-32,550] | 37,555 | 0.834x | 28,238 | **1.109x** | 599 | **52.298x** | 112.6 [108-186] | 101.8 | 123.2 |
| sidebar | 1 | 6,144 [6,082-6,909] | 8,000 | 0.768x | 6,196 | **0.992x** | 287 | **21.439x** | 226.5 [212-227] | 181.0 | 149.0 |
| search | 16 | 17,752 [17,605-28,409] | 34,070 | 0.521x | 26,606 | **0.667x** | 408 | **43.522x** | 183.2 [126-186] | 107.3 | 123.9 |
| search | 1 | 4,494 [4,148-5,459] | 6,055 | 0.742x | 5,646 | **0.796x** | 193 | **23.322x** | 337.2 [321-367] | 315.1 | 158.3 |
| post a message | 16 | 4,055 [3,193-4,566] | 8,905 | 0.455x | 7,174 | **0.565x** | 330 | **12.285x** | 559.7 [500-666] | 353.0 | 385.6 |
| post a message | 1 | 2,389 [1,271-2,501] | 3,052 | 0.783x | 2,779 | **0.860x** | 167 | **14.313x** | 709.3 [688-1,024] | 629.1 | 403.7 |

Checks:
  note messages_page c=1: host probe 1.158 after the window (noisy host)
  note messages_page c=1: warm-up did not settle (reached the maximum)
  note messages_page c=1: host probe 1.139 after the window (noisy host)
  note messages_page c=16: warm-up did not settle (reached the maximum)
  note messages_page c=16: warm-up did not settle (reached the maximum)
  note messages_page c=16: host probe 1.23 after the window (noisy host)
  note post_message c=1: host probe 1.109 after the window (noisy host)
  note post_message c=1: warm-up did not settle (reached the maximum)
  note post_message c=1: host probe 1.136 after the window (noisy host)
  note post_message c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: host probe 1.496 after the window (noisy host)
  note post_message c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: host probe 1.206 after the window (noisy host)
  note post_message c=16: warm-up did not settle (reached the maximum)
  note post_message c=16: host probe 1.2 after the window (noisy host)
  note room_show c=1: host probe 1.123 after the window (noisy host)
  note room_show c=16: warm-up did not settle (reached the maximum)
  note search c=1: warm-up did not settle (reached the maximum)
  note search c=1: host probe 1.124 after the window (noisy host)
  note search c=1: warm-up did not settle (reached the maximum)
  note search c=1: host probe 1.128 after the window (noisy host)
  note search c=16: warm-up did not settle (reached the maximum)
  note search c=16: host probe 1.191 after the window (noisy host)
  note search c=16: warm-up did not settle (reached the maximum)
  note search c=16: host probe 1.265 after the window (noisy host)
  note sidebar c=1: host probe 1.117 after the window (noisy host)
  note sidebar c=16: warm-up did not settle (reached the maximum)
  note sidebar c=16: host probe 1.171 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 30 runs
  decoded bytes equal Rust's on every probed route; post rows written equal the 2xx answers in every phase
