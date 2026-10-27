theDotNetWay in bench/results/quick-86-q1b against the stored medians of bench/results/phase7-final (Rails, Rust and the F# port, 3 reps each, one session)

| Workload | c | theDotNetWay req/s | F# port stored | vs port | Rust stored | **theDotNetWay / Rust (vs stored Rust baseline)** | Rails stored | **theDotNetWay / Rails** | CPU us/req | port | Rust stored |
|---|---|---|---|---|---|---|---|---|---|---|---|

Checks:
  note static_css c=1: host probe 1.111 after the window (noisy host)
  note static_css c=1: warm-up did not settle (reached the maximum)
  note static_css c=1: host probe 1.102 after the window (noisy host)
  note static_css c=16: host probe 1.11 after the window (noisy host)
  responses: every one 2xx with 0 connection errors in 6 runs
  decoded bytes equal the stored expected size (Rust's where the page is unchanged) on every probed route; post rows written equal the 2xx answers in every phase
