# upstream-quick (mixed profile, 3 rounds, c=16)

Requests per second at c=16: median (min-max over the rounds); x Rails is the median over the same session's Rails median.

| route | rails | fsharp | dotnetway |
|---|---|---|---|
| room_show | 2,731 (2,716-2,778) | 33,551 (33,444-33,702) **12.29x** | 33,416 (32,850-33,531) **12.24x** |
| messages_page | 3,466 (3,414-3,567) | 41,557 (41,344-41,767) **11.99x** | 32,642 (32,306-33,170) **9.42x** |
| sidebar | 4,265 (4,230-4,280) | 37,834 (37,425-37,932) **8.87x** | 40,062 (39,802-40,228) **9.39x** |
| search | 4,029 (3,994-4,146) | 34,722 (34,722-35,299) **8.62x** | 39,451 (39,321-39,579) **9.79x** |

Commits:
- rails: `ca9b963` dirty=false  image `sha256:abaf6099efd4`
- fsharp: `9954ad0` dirty=false  image `sha256:eff5fc297f6a`
- dotnetway: `953023e` dirty=false  image `sha256:89085eca34a9`
- seed sha256 `e7b7f2b4`, harness `eb45b63`

Host:
- before: {"spin_ms": 57.47, "spin_ms_max_cpu": 60.78, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.034} load=4.51 4.96 4.02 1/203 1891794
- after: {"spin_ms": 58.38, "spin_ms_max_cpu": 61.4, "cpus": "0-7", "baseline_ms": 55.57, "ratio": 1.051} load=4.54 5.40 4.62 1/201 1894590
- commit=953023e9f4a4215d2a88aa8624120f5f360a439d image=campfire-dotnetway:uq-953023e mounted= mixed=1 harness=eb45b63
