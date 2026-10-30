# upstream-quick (normal profile, 3 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | fsharp | dotnetway |
|---|---|---|---|
| room_show | 4,918 (4,909-4,971) | 35,861 (35,527-35,987) **7.29x** | 34,764 (34,342-34,938) **7.07x** |
| messages_page | 4,888 (4,801-4,922) | 42,110 (41,305-42,125) **8.62x** | 33,332 (32,835-33,459) **6.82x** |
| sidebar | 5,226 (5,081-5,348) | 37,993 (37,559-38,077) **7.27x** | 41,679 (41,452-41,697) **7.97x** |
| search | 5,101 (5,087-5,175) | 33,803 (33,658-33,882) **6.63x** | 41,153 (40,641-41,548) **8.07x** |
| post_message | 403 (401-404) | 9,404 (9,367-9,424) **23.35x** | 2,891 (2,865-2,912) **7.18x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- fsharp: `9954ad0` dirty=false  image `sha256:eff5fc297f6a`
- dotnetway: `953023e` dirty=false  image `sha256:a430b0d66ade`
- seed sha256 `e7b7f2b4`, harness `eb45b63`

Host:
- before: {"spin_ms": 59.15, "spin_ms_max_cpu": 62.57, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.064} load=1.24 2.41 2.77 1/207 1888383
- after: {"spin_ms": 60.29, "spin_ms_max_cpu": 61.94, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.085} load=4.88 5.05 4.04 1/203 1891454
- commit=953023e9f4a4215d2a88aa8624120f5f360a439d image=campfire-dotnetway:uq-953023e mounted= mixed=0 harness=eb45b63
